"""Initial private UAT world deployment. Create once, observe the same immutable run."""
import argparse
import base64
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import subprocess
import tarfile

NS = 'ttb-uat'
APP = 'ttb-world-uat'
WEB = 'ttb-world-web-uat'


def command(*args, payload=None):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload is not None else None, capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError('Private UAT deployment operation failed. Inspect this existing run; no automatic create retry.')
    return result.stdout


def get(*args):
    return json.loads(command('-n', NS, 'get', *args, '-o', 'json'))


def financial_counts():
    names = ['Orders', 'Attempts', 'BankTransactions', 'Allocations', 'Observations', 'CallbackReceipts', 'Voids', 'Reports',
             'ManualRefunds', 'Documents', 'Entitlements', 'Fulfillments', 'MockCredits', 'MockVoids', 'Jobs']
    sql = 'SELECT json_build_object(' + ','.join("'" + name + "',(SELECT count(*) FROM ttb.\"" + name + '\")' for name in names) + ')'
    return json.loads(command('-n', NS, 'exec', 'deployment/ttb-postgres', '--', 'psql', '-X', '-U', 'ttb_owner', '-d', 'ttb_uat', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql))


def api_state():
    script = "import json,urllib.request; print(json.dumps({p:json.load(urllib.request.urlopen('http://127.0.0.1:8080'+p,timeout=8)) for p in ['/health/ready','/ttb-commerce-test/v1/storefront/config']}))"
    return json.loads(command('-n', NS, 'exec', 'deployment/ttb-commerce-api', '--', 'python3', '-c', script))


def ready_pod(app):
    pods = get('pods', '-l', 'app=' + app)['items']
    ready = [p for p in pods if not p['metadata'].get('deletionTimestamp') and any(x['type'] == 'Ready' and x['status'] == 'True' for x in p['status'].get('conditions', []))]
    if len(ready) != 1:
        raise RuntimeError('Expected exactly one ready owned pod; observe the existing rollout.')
    return ready[0]


def identity(obj):
    return {k: obj['metadata'][k] for k in ('uid', 'resourceVersion')}


def matches(selector, labels):
    if any(labels.get(k) != v for k, v in selector.get('matchLabels', {}).items()):
        return False
    for expression in selector.get('matchExpressions', []):
        key, operator, values = expression['key'], expression['operator'], expression.get('values', [])
        if operator == 'In' and labels.get(key) not in values or operator == 'NotIn' and labels.get(key) in values:
            return False
        if operator == 'Exists' and key not in labels or operator == 'DoesNotExist' and key in labels:
            return False
    return True


def transport():
    return {'sharedService': identity(json.loads(command('-n', 'mangos-ingress', 'get', 'service', 'ingress-nginx-controller', '-o', 'json'))),
            'proxyConfig': identity(get('configmap', 'ttb-transport')), 'apiPolicy': identity(get('networkpolicy', 'ttb-api-ingress'))}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--archive-sha256', required=True)
    parser.add_argument('--phase', choices=['create', 'observe'], required=True)
    parser.add_argument('--previous-run-id')
    args = parser.parse_args()
    if not re.fullmatch('[a-f0-9]{32}', args.run_id) or not re.fullmatch('[a-f0-9]{64}', args.archive_sha256):
        raise ValueError('Invalid immutable run identity')
    if args.previous_run_id and not re.fullmatch('[a-f0-9]{32}', args.previous_run_id):
        raise ValueError('Invalid previous run identity')
    os.umask(0o077)
    directory = Path('/opt/ttb-commerce/uat/world-releases') / args.run_id
    state_path = directory / 'state.json'

    def save(state):
        tmp = directory / 'state.next'
        tmp.write_text(json.dumps(state))
        tmp.replace(state_path)

    if args.phase == 'create':
        if state_path.exists():
            raise RuntimeError('Deployment run exists. Observe it instead of recreating resources.')
        archive = directory / 'world.tar.gz'
        if hashlib.sha256(archive.read_bytes()).hexdigest() != args.archive_sha256:
            raise ValueError('Transferred archive hash mismatch')
        package_root = directory / 'package'
        package_root.mkdir(exist_ok=False)
        with tarfile.open(archive) as tar:
            members = tar.getmembers()
            if sum(m.size for m in members) > 1024 ** 3:
                raise ValueError('Archive exceeds the UAT world size bound')
            for member in members:
                path = PurePosixPath(member.name)
                if path.is_absolute() or '..' in path.parts or '\\' in member.name or not (member.isfile() or member.isdir()):
                    raise ValueError('Unsafe archive member')
            tar.extractall(package_root, members=members, filter='data')
        manifest = json.loads((package_root / 'package.json').read_text())
        if manifest['runId'] != args.run_id or manifest['environment'] != 'Uat':
            raise ValueError('Wrong package environment')
        expected = {x['path'] for x in manifest['files']}
        actual = {p.relative_to(package_root).as_posix() for p in package_root.rglob('*') if p.is_file()}
        if actual != expected | {'package.json'}:
            raise ValueError('Unexpected or missing package artifacts')
        for entry in manifest['files']:
            file = package_root / entry['path']
            if package_root not in file.resolve().parents or file.is_symlink() or hashlib.sha256(file.read_bytes()).hexdigest() != entry['sha256']:
                raise ValueError('Package artifact hash mismatch')
        for file in package_root.rglob('*'):
            file.chmod(0o755 if file.is_dir() or file.name.endswith('.x86_64') else 0o644)
        package_root.chmod(0o755)
        # Parent folders contain only public build files/manifests. Secrets stay in Kubernetes.
        directory.chmod(0o755)
        before = financial_counts()
        health = api_state()['/health/ready']
        if any(before.values()) or health['paymentMode'] != 'Bank' or health['paymentsReady'] or health['reportsEnabled'] or health['recoveryFrozen']:
            raise RuntimeError('Initial private world deployment requires pristine UAT with bank/report gates closed')
        source = ready_pod('ttb-commerce-api')
        api = get('deployment', 'ttb-commerce-api')
        if api['status'].get('observedGeneration') != api['metadata']['generation']:
            raise RuntimeError('API rollout is still pending')
        source_image = source['spec']['containers'][0]['image']
        state = {'runId': args.run_id, 'archiveSha256': args.archive_sha256, 'beforeFinancialCounts': before, 'transport': transport(),
                 'imageId': source['status']['containerStatuses'][0]['imageID'], 'apiUid': api['metadata']['uid'], 'phase': 'preflight', 'resourceUids': {}}
        save(state)
        previous = None
        if args.previous_run_id:
            previous_dir = directory.parent / args.previous_run_id
            previous = json.loads((previous_dir / 'state.json').read_text())
            if previous['phase'] != 'verified' or previous['apiUid'] != state['apiUid'] or previous['transport'] != state['transport']:
                raise RuntimeError('The previous verified UAT deployment identity changed')
            previous_resources = {(r['kind'], r['metadata']['name']): r for r in json.loads((previous_dir / 'resources.json').read_text())}
        resources = []
        ingress_peer = {'namespaceSelector': {'matchLabels': {'kubernetes.io/metadata.name': 'mangos-ingress'}},
                        'podSelector': {'matchLabels': {'app.kubernetes.io/name': 'ingress-nginx', 'app.kubernetes.io/component': 'controller'}}}
        dns = {'to': [{'namespaceSelector': {'matchLabels': {'kubernetes.io/metadata.name': 'kube-system'}}, 'podSelector': {'matchLabels': {'k8s-app': 'kube-dns'}}}],
               'ports': [{'protocol': 'UDP', 'port': 53}, {'protocol': 'TCP', 'port': 53}]}
        for app, port, folder in [(APP, 7780, 'Server'), (WEB, 8080, 'Client')]:
            labels = {'app': app, 'app.kubernetes.io/part-of': 'ttb-commerce', 'mangosgo.com/world-run': args.run_id}
            metadata = {'name': app, 'namespace': NS, 'labels': labels}
            existing = command('-n', NS, 'get', 'deployment,service,networkpolicy', '-l', 'app=' + app, '-o', 'json')
            if json.loads(existing)['items'] and not previous:
                raise RuntimeError('An initial world resource already exists; refuse overwrite')
            egress = [] if app == WEB else [{'to': [{'podSelector': {'matchLabels': {'app': 'ttb-commerce-api'}}}], 'ports': [{'protocol': 'TCP', 'port': 8080}]}, dns]
            for policy in get('networkpolicies')['items']:
                if previous and policy['metadata']['uid'] == previous['resourceUids'].get('NetworkPolicy/' + app):
                    continue
                selector = policy['spec'].get('podSelector', {})
                if 'Egress' in policy['spec'].get('policyTypes', []) and matches(selector, labels):
                    raise RuntimeError('Another additive egress policy selects the new world pod')
            policy = {'apiVersion': 'networking.k8s.io/v1', 'kind': 'NetworkPolicy', 'metadata': metadata,
                      'spec': {'podSelector': {'matchLabels': {'app': app}}, 'policyTypes': ['Ingress', 'Egress'],
                               'ingress': [{'from': [ingress_peer], 'ports': [{'protocol': 'TCP', 'port': port}]}], 'egress': egress}}
            security = {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True, 'capabilities': {'drop': ['ALL']}}
            container = {'name': 'world' if app == APP else 'web', 'image': source_image, 'imagePullPolicy': 'Never', 'securityContext': security,
                         'ports': [{'name': 'tcp', 'containerPort': port}], 'resources': {'requests': {'cpu': '100m', 'memory': '256Mi' if app == APP else '128Mi'},
                         'limits': {'cpu': '2' if app == APP else '1', 'memory': '1Gi' if app == APP else '256Mi'}},
                         'volumeMounts': [{'name': 'release', 'mountPath': '/release', 'readOnly': True}, {'name': 'temporary', 'mountPath': '/tmp'}],
                         'startupProbe': {'tcpSocket': {'port': 'tcp'}, 'periodSeconds': 3, 'failureThreshold': 40},
                         'readinessProbe': {'tcpSocket': {'port': 'tcp'}, 'periodSeconds': 5}, 'livenessProbe': {'tcpSocket': {'port': 'tcp'}, 'periodSeconds': 20}}
            if app == APP:
                container.update({'command': ['/release/Server/TtbUatWorld.x86_64', '-batchmode', '-nographics', '-logFile', '-'], 'workingDir': '/release/Server',
                    'env': [{'name': 'HOME', 'value': '/tmp'}, {'name': 'XDG_CONFIG_HOME', 'value': '/tmp'},
                            {'name': 'MANGOS_TTB_GAME_ENVIRONMENT', 'value': 'Uat'},
                            {'name': 'MANGOS_TTB_GAME_API', 'value': 'http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce-test/v1'},
                            {'name': 'MANGOS_TTB_GAME_ID', 'valueFrom': {'secretKeyRef': {'name': 'ttb-game-service', 'key': 'Storage__GameServerId'}}},
                            {'name': 'MANGOS_TTB_GAME_KEY', 'valueFrom': {'secretKeyRef': {'name': 'ttb-game-service', 'key': 'Storage__GameServerKey'}}}]})
            else:
                container.update({'command': ['python3', '/release/world-web.py'], 'env': [{'name': 'TTB_WEB_ROOT', 'value': '/release/Client'}, {'name': 'PYTHONDONTWRITEBYTECODE', 'value': '1'}]})
                container['readinessProbe'] = {'httpGet': {'path': '/health/live', 'port': 'tcp'}, 'periodSeconds': 5}
            deployment = {'apiVersion': 'apps/v1', 'kind': 'Deployment', 'metadata': metadata, 'spec': {'replicas': 1, 'revisionHistoryLimit': 2,
                'selector': {'matchLabels': {'app': app}}, 'template': {'metadata': {'labels': labels}, 'spec': {'nodeName': source['spec']['nodeName'],
                    'automountServiceAccountToken': False, 'securityContext': {'runAsNonRoot': True, 'runAsUser': 1000, 'runAsGroup': 1000},
                    'containers': [container], 'volumes': [{'name': 'release', 'hostPath': {'path': str(package_root), 'type': 'Directory'}},
                                                        {'name': 'temporary', 'emptyDir': {'sizeLimit': '128Mi'}}]}}}}
            service = {'apiVersion': 'v1', 'kind': 'Service', 'metadata': metadata, 'spec': {'type': 'ClusterIP', 'selector': {'app': app}, 'ports': [{'name': 'tcp', 'port': port, 'targetPort': 'tcp'}]}}
            resources.extend([policy, service, deployment])
        if previous:
            patches = []
            for resource in resources:
                kind, name = resource['kind'], resource['metadata']['name']
                actual = get(kind, name)
                if actual['metadata']['uid'] != previous['resourceUids'][kind + '/' + name]:
                    raise RuntimeError('A previous UAT world resource was replaced')
                if kind == 'NetworkPolicy':
                    for field in ('ingress', 'egress'):
                        actual['spec'].setdefault(field, [])
                    if actual['spec'] != resource['spec']:
                        raise RuntimeError('The previous world network policy changed')
                if kind != 'Deployment':
                    continue
                prior_container = previous_resources[(kind, name)]['spec']['template']['spec']['containers'][0]
                container = actual['spec']['template']['spec']['containers'][0]
                for field in ('image', 'imagePullPolicy', 'command', 'env', 'securityContext', 'volumeMounts'):
                    if container.get(field) != prior_container.get(field):
                        raise RuntimeError('The previous world container profile changed')
                volumes = actual['spec']['template']['spec']['volumes']
                index = next(i for i, v in enumerate(volumes) if v['name'] == 'release')
                prior_path = str(previous_dir / 'package')
                if volumes[index]['hostPath']['path'] != prior_path:
                    raise RuntimeError('The previous world release path changed')
                patch = [{'op': 'test', 'path': '/metadata/uid', 'value': actual['metadata']['uid']},
                         {'op': 'test', 'path': '/metadata/resourceVersion', 'value': actual['metadata']['resourceVersion']},
                         {'op': 'test', 'path': f'/spec/template/spec/volumes/{index}/hostPath/path', 'value': prior_path},
                         {'op': 'replace', 'path': f'/spec/template/spec/volumes/{index}/hostPath/path', 'value': str(package_root)},
                         {'op': 'replace', 'path': '/spec/template/metadata/labels/mangosgo.com~1world-run', 'value': args.run_id}]
                command('-n', NS, 'patch', 'deployment', name, '--type=json', '-p', json.dumps(patch), '--dry-run=server')
                patches.append((name, patch))
            if get('secret', 'ttb-game-service')['metadata']['uid'] != previous['resourceUids']['Secret/ttb-game-service']:
                raise RuntimeError('The immutable UAT game service secret was replaced')
            state.update({'previousRunId': args.previous_run_id, 'resourceUids': previous['resourceUids'], 'apiGeneration': api['metadata']['generation'], 'phase': 'upgrade-attempted'})
            (directory / 'resources.json').write_text(json.dumps(resources)); save(state)
            for name, patch in patches:
                command('-n', NS, 'patch', 'deployment', name, '--type=json', '-p', json.dumps(patch))
            state['phase'] = 'created'; save(state)
            print(json.dumps({'runId': args.run_id, 'phase': 'created', 'previousRunId': args.previous_run_id, 'apiGeneration': state['apiGeneration'], 'publicRouteCreated': False, 'gameSecretRotated': False}))
            return
        secret_existing = command('-n', NS, 'get', 'secret', 'ttb-game-service', '--ignore-not-found', '-o', 'json')
        if secret_existing.strip():
            raise RuntimeError('Game service secret already exists; inspect its ownership rather than replace it')
        secret = {'apiVersion': 'v1', 'kind': 'Secret', 'metadata': {'name': 'ttb-game-service', 'namespace': NS, 'labels': {'app.kubernetes.io/part-of': 'ttb-commerce', 'mangosgo.com/world-run': args.run_id}},
                  'immutable': True, 'type': 'Opaque', 'data': {k: base64.b64encode(v.encode()).decode() for k, v in {
                      'Storage__GameServerId': 'ttb-uat-world', 'Storage__GameServerKey': secrets.token_hex(32),
                      'AllowedHosts': 'ttb-api-test.mangosgo.com;plus.mangosgo.com;localhost;127.0.0.1;ttb-commerce-metrics.ttb-uat.svc.cluster.local;ttb-commerce-api.ttb-uat.svc.cluster.local'
                  }.items()}}
        for resource in resources + [secret]:
            command('create', '--dry-run=server', '-f', '-', payload=resource)
        (directory / 'resources.json').write_text(json.dumps(resources))
        state['phase'] = 'creation-attempted'; save(state)
        # Egress isolation exists before either executable starts.
        for resource in [r for r in resources if r['kind'] == 'NetworkPolicy'] + [secret] + [r for r in resources if r['kind'] != 'NetworkPolicy']:
            created = json.loads(command('create', '-f', '-', '-o', 'json', payload=resource))
            state['resourceUids'][resource['kind'] + '/' + resource['metadata']['name']] = created['metadata']['uid']; save(state)
        env_from = api['spec']['template']['spec']['containers'][0].get('envFrom', [])
        if any(x.get('secretRef', {}).get('name') == 'ttb-game-service' for x in env_from):
            raise RuntimeError('Unexpected pre-existing game reference on API')
        patch = [{'op': 'test', 'path': '/metadata/uid', 'value': api['metadata']['uid']},
                 {'op': 'test', 'path': '/metadata/resourceVersion', 'value': api['metadata']['resourceVersion']},
                 {'op': 'test', 'path': '/spec/template/spec/containers/0/envFrom', 'value': env_from},
                 {'op': 'add', 'path': '/spec/template/spec/containers/0/envFrom/-', 'value': {'secretRef': {'name': 'ttb-game-service', 'optional': True}}}]
        updated = json.loads(command('-n', NS, 'patch', 'deployment', 'ttb-commerce-api', '--type=json', '-p', json.dumps(patch), '-o', 'json'))
        state['apiGeneration'] = updated['metadata']['generation']; state['phase'] = 'created'; save(state)
        print(json.dumps({'runId': args.run_id, 'phase': 'created', 'resourceUids': state['resourceUids'], 'apiGeneration': state['apiGeneration'], 'publicRouteCreated': False}))
        return
    state = json.loads(state_path.read_text())
    if state['archiveSha256'] != args.archive_sha256 or state['phase'] not in ('created', 'verified'):
        raise RuntimeError('Inspect the partial existing run; do not replay creation automatically')
    pods = {}
    for app in (APP, WEB, 'ttb-commerce-api'):
        deployment = get('deployment', app)
        expected_uid = state['apiUid'] if app == 'ttb-commerce-api' else state['resourceUids']['Deployment/' + app]
        if deployment['metadata']['uid'] != expected_uid or deployment['status'].get('observedGeneration') != deployment['metadata']['generation'] or deployment['status'].get('readyReplicas') != 1:
            raise RuntimeError('Observe the same pending rollout; do not redeploy')
        pod = ready_pod(app)
        if pod['status']['containerStatuses'][0]['imageID'] != state['imageId']:
            raise RuntimeError('Actual cached runtime image changed')
        pods[app] = {'uid': pod['metadata']['uid'], 'name': pod['metadata']['name'], 'generation': deployment['metadata']['generation']}
    for resource in json.loads((directory / 'resources.json').read_text()):
        actual = get(resource['kind'], resource['metadata']['name'])
        if actual['metadata']['uid'] != state['resourceUids'][resource['kind'] + '/' + resource['metadata']['name']]:
            raise RuntimeError('A created resource was replaced')
        if resource['kind'] == 'NetworkPolicy':
            for field in ('ingress', 'egress'):
                actual['spec'].setdefault(field, [])
            if actual['spec'] != resource['spec']:
                raise RuntimeError('Network isolation changed')
    after = financial_counts()
    health = api_state()
    ready = health['/health/ready']; storefront = health['/ttb-commerce-test/v1/storefront/config']
    if after != state['beforeFinancialCounts'] or ready['paymentsReady'] or ready['reportsEnabled'] or not storefront['gameUseReady'] or storefront['environment'] != 'Uat' or storefront['mockSimulation']:
        raise RuntimeError('Unexpected UAT financial/profile state')
    if transport() != state['transport']:
        raise RuntimeError('Existing transport identity changed')
    probe = """import json,socket,urllib.request
ready=json.load(urllib.request.urlopen('http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/health/ready',timeout=8))
try:
 with socket.create_connection(('ttb-postgres.ttb-uat.svc.cluster.local',5432),timeout=3):pass
except OSError:denied=True
else:denied=False
print(json.dumps({'apiReady':ready['status']=='ready','postgresTcpDenied':denied}))"""
    network = json.loads(command('-n', NS, 'exec', pods[APP]['name'], '--', 'python3', '-c', probe))
    if not all(network.values()):
        raise RuntimeError('Actual game CNI isolation failed')
    state['phase'] = 'verified'; state['verifiedAt'] = dt.datetime.now(dt.timezone.utc).isoformat(); save(state)
    result = {k: state[k] for k in ('runId', 'archiveSha256', 'phase', 'verifiedAt', 'resourceUids')}
    result.update({'pods': pods, 'financialCounts': after, 'network': network, 'bankAccepted': False, 'publicRouteCreated': False,
                   'simulatorEnabled': False, 'productionChanged': False, 'kkuChanged': False})
    (directory / 'result.json').write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == '__main__':
    main()
