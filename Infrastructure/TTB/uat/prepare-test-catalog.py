"""Prepare only explicitly authorized UAT merchandise; never enable payments or run a simulator.

Create once, then observe the same run. An observation timeout is not permission to create another Job.
The completed Job and its owned network policy have a one-day TTL for operator inspection.
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import subprocess

NS = 'ttb-uat'
LABEL = 'mangosgo.com/catalog-run'


def command(*args, payload=None):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload is not None else None,
                            capture_output=True, text=True, timeout=25)
    if result.returncode:
        raise RuntimeError('UAT catalog operation failed; inspect the existing named run. No automatic retry is permitted.')
    return result.stdout


def obj(*args):
    return json.loads(command(*args, '-o', 'json'))


def read_api(pod):
    script = '''import json,os,urllib.request
def get(path):return json.load(urllib.request.urlopen('http://127.0.0.1:8080'+path,timeout=8))
print(json.dumps({'ready':get('/health/ready'),'catalog':get('/ttb-commerce-test/v1/catalog/products?limit=100'),
'freeze':os.environ.get('Operations__RecoveryFreeze','').lower()=='true'}))'''
    return json.loads(command('-n', NS, 'exec', pod, '--', 'python3', '-c', script))


def financial_counts():
    names = ['Orders', 'Attempts', 'BankTransactions', 'Allocations', 'Observations', 'CallbackReceipts', 'Voids', 'Reports',
             'ManualRefunds', 'Documents', 'Entitlements', 'Fulfillments', 'MockCredits', 'MockVoids', 'Jobs']
    sql = 'SELECT json_build_object(' + ','.join("'" + name + "',(SELECT count(*) FROM ttb.\"" + name + '\")' for name in names) + ')'
    return json.loads(command('-n', NS, 'exec', 'deployment/ttb-postgres', '--', 'psql', '-X', '-U', 'ttb_owner', '-d', 'ttb_uat', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql))


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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--release-id', required=True)
    parser.add_argument('--phase', choices=['create', 'observe'], required=True)
    args = parser.parse_args()
    if not re.fullmatch('[a-f0-9]{32}', args.run_id) or not re.fullmatch('[a-z0-9][a-z0-9-]{2,31}', args.release_id):
        raise ValueError('Invalid run/release identity.')
    os.umask(0o077)
    directory = Path('/opt/ttb-commerce/uat/catalog-preparation') / args.run_id
    state_path = directory / 'state.json'

    def save(state):
        temporary = directory / 'state.next'
        temporary.write_text(json.dumps(state), encoding='utf-8')
        temporary.replace(state_path)

    if args.phase == 'create':
        directory.mkdir(parents=True, exist_ok=False)
        deployment = obj('-n', NS, 'get', 'deployment', 'ttb-commerce-api')
        if deployment['status'].get('observedGeneration') != deployment['metadata']['generation'] or deployment['status'].get('readyReplicas') != 1:
            raise RuntimeError('The existing API must be ready without a pending rollout.')
        release_path = Path('/opt/ttb-commerce/uat/releases') / args.release_id / 'api'
        app = next(v for v in deployment['spec']['template']['spec']['volumes'] if v['name'] == 'app')
        if app['hostPath']['path'] != str(release_path) or not release_path.is_dir():
            raise RuntimeError('The expected immutable release is not the currently deployed UAT API.')
        contract = json.loads((release_path / 'release-compatibility.json').read_text())
        if 'uat-test-catalog-v1' not in contract.get('capabilities', []):
            raise RuntimeError('The deployed release has no UAT catalog operator command.')
        pods = obj('-n', NS, 'get', 'pods', '-l', 'app=ttb-commerce-api')['items']
        ready = [p for p in pods if not p['metadata'].get('deletionTimestamp') and any(c['type'] == 'Ready' and c['status'] == 'True' for c in p['status'].get('conditions', []))]
        if len(ready) != 1:
            raise RuntimeError('Expected one pinned ready API pod.')
        source = ready[0]
        health, before = read_api(source['metadata']['name']), financial_counts()
        if health['ready']['paymentsReady'] or health['ready']['paymentMode'] != 'Bank' or health['freeze'] or any(before.values()):
            raise RuntimeError('Preparation requires a pristine, unfrozen UAT with actual API bank payments closed.')
        name = 'ttb-uat-catalog-' + args.run_id
        labels = {'app': 'ttb-uat-test-catalog', 'app.kubernetes.io/part-of': 'ttb-commerce', LABEL: args.run_id}
        for policy in obj('-n', NS, 'get', 'networkpolicies')['items']:
            if 'Egress' in policy['spec'].get('policyTypes', []) and matches(policy['spec'].get('podSelector', {}), labels):
                raise RuntimeError('Another egress policy selects the proposed Job; inspect additive rules before proceeding.')
        policy = {'apiVersion': 'networking.k8s.io/v1', 'kind': 'NetworkPolicy', 'metadata': {'name': name, 'namespace': NS, 'labels': labels},
                  'spec': {'podSelector': {'matchLabels': {LABEL: args.run_id}}, 'policyTypes': ['Ingress', 'Egress'], 'ingress': [], 'egress': [
                      {'to': [{'podSelector': {'matchLabels': {'app': 'ttb-postgres'}}}], 'ports': [{'protocol': 'TCP', 'port': 5432}]},
                      {'to': [{'namespaceSelector': {'matchLabels': {'kubernetes.io/metadata.name': 'kube-system'}}, 'podSelector': {'matchLabels': {'k8s-app': 'kube-dns'}}}],
                       'ports': [{'protocol': 'UDP', 'port': 53}, {'protocol': 'TCP', 'port': 53}]}]}}
        service = obj('-n', NS, 'get', 'service', 'ttb-commerce-api')
        wait_then_run = '''import socket,time,os
for attempt in range(20):
 try:
  with socket.create_connection(('ttb-postgres',5432),timeout=1):break
 except OSError:
  if attempt==19:raise SystemExit('Private PostgreSQL unavailable; no catalog command started')
  time.sleep(1)
try:
 with socket.create_connection((API_IP,8080),timeout=2):pass
except OSError: print('TTB_UAT_CATALOG_API_EGRESS_DENIED',flush=True)
else: raise SystemExit('Unexpected API egress; no catalog command started')
os.execv('/app/TtbCommerce.Api',['/app/TtbCommerce.Api','--prepare-uat-test-catalog'])'''.replace('API_IP', repr(service['spec']['clusterIP']))
        container = {'name': 'prepare', 'image': source['spec']['containers'][0]['image'], 'imagePullPolicy': 'Never', 'workingDir': '/app',
                     'command': ['python3', '-c', wait_then_run], 'env': [
                         {'name': 'ASPNETCORE_ENVIRONMENT', 'value': 'Uat'}, {'name': 'Commerce__Role', 'value': 'Api'},
                         {'name': 'DOTNET_EnableDiagnostics', 'value': '0'},
                         {'name': 'ConnectionStrings__Commerce', 'valueFrom': {'secretKeyRef': {'name': 'ttb-commerce-runtime', 'key': 'ConnectionStrings__Commerce'}}},
                         {'name': 'Operations__RecoveryFreeze', 'valueFrom': {'secretKeyRef': {'name': 'ttb-operations', 'key': 'Operations__RecoveryFreeze', 'optional': True}}}],
                     'securityContext': {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True, 'capabilities': {'drop': ['ALL']}},
                     'resources': {'requests': {'cpu': '100m', 'memory': '128Mi'}, 'limits': {'cpu': '1', 'memory': '384Mi'}},
                     'volumeMounts': [{'name': 'app', 'mountPath': '/app', 'readOnly': True}, {'name': 'temporary', 'mountPath': '/tmp'}]}
        job = {'apiVersion': 'batch/v1', 'kind': 'Job', 'metadata': {'name': name, 'namespace': NS, 'labels': labels},
               'spec': {'backoffLimit': 0, 'activeDeadlineSeconds': 90, 'ttlSecondsAfterFinished': 86400,
                        'template': {'metadata': {'labels': labels}, 'spec': {'nodeName': source['spec']['nodeName'], 'restartPolicy': 'Never', 'automountServiceAccountToken': False,
                            'securityContext': {'runAsNonRoot': True, 'runAsUser': 1000, 'runAsGroup': 1000}, 'containers': [container],
                            'volumes': [{'name': 'app', 'hostPath': {'path': str(release_path), 'type': 'Directory'}}, {'name': 'temporary', 'emptyDir': {'sizeLimit': '64Mi'}}]}}}}
        state = {'runId': args.run_id, 'releaseId': args.release_id, 'job': name, 'phase': 'creation-attempted', 'apiPod': source['metadata']['name'],
                 'apiPodUid': source['metadata']['uid'], 'apiDeploymentUid': deployment['metadata']['uid'], 'apiGeneration': deployment['metadata']['generation'],
                 'apiImageId': source['status']['containerStatuses'][0]['imageID'], 'beforeFinancialCounts': before, 'beforeCatalogCount': len(health['catalog']['items'])}
        save(state)
        (directory / 'job.json').write_text(json.dumps(job))
        (directory / 'policy.json').write_text(json.dumps(policy))
        command('create', '--dry-run=server', '-f', '-', payload=policy)
        command('create', '--dry-run=server', '-f', '-', payload=job)
        created_policy = json.loads(command('create', '-f', '-', '-o', 'json', payload=policy))
        state['policyUid'] = created_policy['metadata']['uid']; save(state)
        created_job = json.loads(command('create', '-f', '-', '-o', 'json', payload=job))
        state['jobUid'] = created_job['metadata']['uid']; save(state)
        owner = {'apiVersion': 'batch/v1', 'kind': 'Job', 'name': name, 'uid': state['jobUid'], 'controller': False, 'blockOwnerDeletion': False}
        patch = [{'op': 'test', 'path': '/metadata/uid', 'value': state['policyUid']},
                 {'op': 'test', 'path': '/metadata/resourceVersion', 'value': created_policy['metadata']['resourceVersion']},
                 {'op': 'add', 'path': '/metadata/ownerReferences', 'value': [owner]}]
        command('-n', NS, 'patch', 'networkpolicy', name, '--type=json', '-p', json.dumps(patch))
        state['phase'] = 'running'; save(state)
        print(json.dumps({'runId': args.run_id, 'phase': 'running', 'job': name, 'jobUid': state['jobUid'], 'policyUid': state['policyUid']}))
        return

    state = json.loads(state_path.read_text())
    if state['releaseId'] != args.release_id:
        raise RuntimeError('Observe the same release/run identity.')
    job = obj('-n', NS, 'get', 'job', state['job'])
    if job['metadata']['uid'] != state.get('jobUid') or job['metadata']['labels'].get(LABEL) != args.run_id:
        raise RuntimeError('Owned Job identity changed; preserve evidence.')
    if job['status'].get('failed') or any(c['type'] == 'Failed' and c['status'] == 'True' for c in job['status'].get('conditions', [])):
        state['phase'] = 'failed'; save(state)
        raise RuntimeError('The existing UAT catalog Job failed; inspect it. Never retry after an observation timeout.')
    if not job['status'].get('succeeded'):
        print(json.dumps({'runId': args.run_id, 'phase': 'running', 'job': state['job'], 'jobUid': state['jobUid'], 'active': job['status'].get('active', 0)}))
        return
    pods = obj('-n', NS, 'get', 'pods', '-l', LABEL + '=' + args.run_id)['items']
    if len(pods) != 1 or pods[0]['status']['containerStatuses'][0]['imageID'] != state['apiImageId']:
        raise RuntimeError('Actual Job base image is not the inspected cached API image.')
    logs = command('-n', NS, 'logs', 'job/' + state['job'])
    (directory / 'job.log').write_text(logs)
    summaries = [json.loads(line) for line in logs.splitlines() if line.startswith('{') and 'uat-test-catalog-prepared' in line]
    if len(summaries) != 1:
        raise RuntimeError('Expected one successful operator-command summary.')
    if 'TTB_UAT_CATALOG_API_EGRESS_DENIED' not in logs:
        raise RuntimeError('The private API egress-denial observation is missing.')
    policy = obj('-n', NS, 'get', 'networkpolicy', state['job'])
    expected_policy = json.loads((directory / 'policy.json').read_text())
    for spec in (policy['spec'], expected_policy['spec']):
        spec.setdefault('ingress', [])
        spec.setdefault('egress', [])
    if policy['metadata']['uid'] != state['policyUid'] or policy['spec'] != expected_policy['spec'] or not any(o['uid'] == state['jobUid'] for o in policy['metadata'].get('ownerReferences', [])):
        raise RuntimeError('Owned egress policy or Job ownership changed during preparation.')
    labels = pods[0]['metadata']['labels']
    if any(p['metadata']['uid'] != state['policyUid'] and 'Egress' in p['spec'].get('policyTypes', []) and matches(p['spec'].get('podSelector', {}), labels)
           for p in obj('-n', NS, 'get', 'networkpolicies')['items']):
        raise RuntimeError('A new additive egress policy selects the Job; inspect its scope.')
    current = obj('-n', NS, 'get', 'pod', state['apiPod'])
    if current['metadata']['uid'] != state['apiPodUid']:
        raise RuntimeError('The source API pod changed during preparation; inspect the same Job before accepting.')
    deployment = obj('-n', NS, 'get', 'deployment', 'ttb-commerce-api')
    if deployment['metadata']['uid'] != state['apiDeploymentUid'] or deployment['metadata']['generation'] != state['apiGeneration']:
        raise RuntimeError('The API rollout changed during preparation; preserve the completed Job for inspection.')
    after, health = financial_counts(), read_api(state['apiPod'])
    items = health['catalog']['items']
    if after != state['beforeFinancialCounts'] or any(after.values()) or health['ready']['paymentsReady'] or health['ready']['reportsEnabled'] or health['freeze']:
        raise RuntimeError('Financial/bank/freeze boundaries changed; preserve the completed Job for inspection.')
    expected = {'orbit-lamp': 9900, 'mango-sculpture': 1000, 'mango-star': 100}
    if len(items) != 3 or {p['preview']['modelKey']: p['variants'][0]['priceMinor'] for p in items} != expected:
        raise RuntimeError('The public UAT catalog does not match the authorized test merchandise.')
    result = {'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'runId': args.run_id, 'phase': 'verified', 'releaseId': args.release_id,
              'job': state['job'], 'jobUid': state['jobUid'], 'policyUid': state['policyUid'], 'command': summaries[0], 'financialCounts': after,
              'catalog': [{'id': p['id'], 'name': p['name'], 'kind': p['kind'], 'modelKey': p['preview']['modelKey'], 'priceMinor': p['variants'][0]['priceMinor'],
                           'availableQuantity': p['variants'][0].get('availableQuantity')} for p in items],
              'jobNetworkLimitedToPostgresAndDns': True, 'privateApiTcpEgressDenied': True, 'bankSecretsMounted': False, 'apiOrWorkerStarted': False, 'simulatorEnabled': False,
              'bankAccepted': False, 'productionChanged': False, 'kkuChanged': False, 'completedJobAndOwnedPolicyTtlSeconds': 86400}
    state['phase'] = 'verified'; save(state)
    (directory / 'result.json').write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == '__main__':
    main()
