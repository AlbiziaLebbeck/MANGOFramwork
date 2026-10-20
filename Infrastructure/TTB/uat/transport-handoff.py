"""Guarded, health-only UAT forwarding observation. Run phases separately.

No bank profile, callback gate, shared ingress, financial record or KKU resource is changed.
The existing metrics credential is used only in memory. State files never contain it.
"""
import argparse
import base64
import datetime as dt
import hashlib
import hmac
import http.client
import ipaddress
import json
import os
from pathlib import Path
import re
import subprocess
import time

NS = 'ttb-uat'
NAME = 'ttb-commerce-api'
WINDOW = 'Operations__TransportProbeUntilUtc'
TRUST = 'Ttb__TrustedProxyCidrs__0'
CM = 'ttb-transport'
SOCKET_HEADER = 'X-Ttb-Transport-Peer-Digest'
EFFECTIVE_HEADER = 'X-Ttb-Effective-Peer-Digest'


def command(*args, payload=None, timeout=25):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload is not None else None,
                            capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('The guarded UAT operation failed; inspect the same run before retrying.')
    return result.stdout


def obj(*args):
    output = command(*args, '-o', 'json')
    return json.loads(output) if output.strip() else None


def container(deployment):
    return next(c for c in deployment['spec']['template']['spec']['containers'] if c['name'] == 'api')


def topology():
    pods = obj('-n', 'mangos-ingress', 'get', 'pods', '-l', 'app.kubernetes.io/component=controller')['items']
    assert len(pods) == 1
    pod = pods[0]
    assert any(c['type'] == 'Ready' and c['status'] == 'True' for c in pod['status'].get('conditions', []))
    address = pod['status']['podIP']
    assert ipaddress.ip_address(address).version == 4 and address.startswith('10.42.')
    assert not pod['spec'].get('hostNetwork', False)
    return {'controllerUid': pod['metadata']['uid'], 'controllerIp': address, 'cidr': address + '/32'}


def write(path, value):
    path.write_text(json.dumps(value, indent=2))
    path.chmod(0o600)


def guarded_env(deployment, replacement):
    containers = deployment['spec']['template']['spec']['containers']
    index = next(i for i, c in enumerate(containers) if c['name'] == 'api')
    patch = [{'op': 'test', 'path': '/metadata/uid', 'value': deployment['metadata']['uid']},
             {'op': 'test', 'path': '/metadata/resourceVersion', 'value': deployment['metadata']['resourceVersion']},
             {'op': 'replace', 'path': f'/spec/template/spec/containers/{index}/env', 'value': replacement}]
    return json.loads(command('-n', NS, 'patch', 'deployment', NAME, '--type=json', '-p', json.dumps(patch), '-o', 'json'))


def wait_ready():
    # A timeout is an observation failure, not proof that the rollout stopped.
    command('-n', NS, 'rollout', 'status', 'deployment/' + NAME, '--timeout=60s', timeout=65)


def health():
    service = obj('-n', NS, 'get', 'service', NAME)
    deadline = time.monotonic() + 20
    while True:
        connection = http.client.HTTPConnection(service['spec']['clusterIP'], 8080, timeout=3)
        try:
            connection.request('GET', '/health/ready', headers={'Host': 'localhost'})
            response = connection.getresponse()
            data = json.loads(response.read(4096))
            assert data['paymentMode'] == 'Bank'
            assert not data['paymentsReady'] and not data['reportsEnabled'] and not data['reportsReady']
            if response.status == 200 and data['database']:
                return data
        except (ConnectionRefusedError, ConnectionResetError, TimeoutError, http.client.RemoteDisconnected):
            pass
        finally:
            connection.close()
        if time.monotonic() >= deadline:
            raise RuntimeError('The same live rollout was not reachable through its Service within the health observation window.')
        # Read-only observation of the same Service. Never repeat a patch or infer process termination here.
        time.sleep(.25)


def prove_node(run):
    secret = obj('-n', NS, 'get', 'secret', 'ttb-operations')
    token = base64.b64decode(secret['data']['Operations__MetricsToken']).decode()
    assert 32 <= len(token) <= 512
    service = obj('-n', NS, 'get', 'service', NAME)
    connection = http.client.HTTPConnection(service['spec']['clusterIP'], 8080, timeout=5)
    connection.request('GET', '/health/live?transport_probe=' + run,
                       headers={'Host': 'localhost', 'Authorization': 'Bearer ' + token,
                                'X-Forwarded-For': '203.148.143.101', 'X-Real-IP': '203.148.143.101'})
    response = connection.getresponse()
    body = json.loads(response.read(1024))
    socket = response.getheader(SOCKET_HEADER); effective = response.getheader(EFFECTIVE_HEADER)
    connection.close()
    bank_digest = hmac.new(token.encode(), ('ttb-uat-transport-probe-v1\n' + run + '\n203.148.143.101').encode(), hashlib.sha256).hexdigest()
    assert response.status == 200 and body == {'status': 'alive', 'service': 'ttb-commerce'}
    assert socket and re.fullmatch('[a-f0-9]{64}', socket) and hmac.compare_digest(socket, effective or '')
    assert not hmac.compare_digest(effective, bank_digest)
    return {'healthAccepted': True, 'untrustedNodeKeepsSocketIdentity': True, 'forgedBankHeadersIgnored': True,
            'rawAddressOrCredentialExported': False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--phase', choices=['enable', 'trust', 'node-proof', 'persist', 'finish', 'abort'], required=True)
    args = parser.parse_args()
    assert re.fullmatch('[a-f0-9]{32}', args.run_id)
    os.umask(0o077)
    directory = Path('/opt/ttb-commerce/transport-check') / args.run_id
    state_path = directory / 'state.json'
    deployment = obj('-n', NS, 'get', 'deployment', NAME)
    api = container(deployment)
    env = api.get('env', [])
    current = {e['name']: e.get('value') for e in env}
    if args.phase == 'enable':
        directory.mkdir(parents=True, exist_ok=False)
        assert not any('trustedproxycidrs' in name.lower() or 'transportprobe' in name.lower() for name in current)
        assert obj('-n', NS, 'get', 'configmap', CM, '--ignore-not-found') is None
        assert any(e.get('configMapRef', {}).get('name') == CM for e in api.get('envFrom', []))
        release = next(v['hostPath']['path'] for v in deployment['spec']['template']['spec']['volumes'] if v['name'] == 'app')
        assert release.startswith('/opt/ttb-commerce/uat/releases/') and release.endswith('/api')
        contract = json.loads((Path(release) / 'release-compatibility.json').read_text())
        assert 'uat-transport-probe-v1' in contract['capabilities']
        boundary = json.loads(Path('/opt/ttb-commerce/network-probes/e4ce251695f24878a0ee068b240edff1/result.json').read_text())
        policy = obj('-n', NS, 'get', 'networkpolicy', 'ttb-api-ingress')
        assert boundary['ownedJobsAndPodsDeleted'] and boundary['unallowedPodBlockedForPodAndServiceAddresses']
        assert policy['metadata']['uid'] == boundary['networkPolicyUid'] and policy['metadata']['resourceVersion'] == boundary['networkPolicyResourceVersion']
        for reference in api.get('envFrom', []):
            if 'secretRef' not in reference:
                continue
            secret = obj('-n', NS, 'get', 'secret', reference['secretRef']['name'], '--ignore-not-found')
            if secret:
                assert not any('trustedproxycidrs' in k.lower() or 'transportprobe' in k.lower() for k in secret.get('data', {}))
        state = {'runId': args.run_id, 'deploymentUid': deployment['metadata']['uid'], 'baselineEnv': env,
                 'topology': topology(), 'until': (dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=20)).isoformat(),
                 'policyUid': policy['metadata']['uid'], 'policyResourceVersion': policy['metadata']['resourceVersion'],
                 'phase': 'enable-intent', 'baselineHealth': health()}
        write(directory / 'before-deployment.json', deployment); write(state_path, state)
        result = guarded_env(deployment, env + [{'name': WINDOW, 'value': state['until']}])
        state['phase'] = 'enabled'; state['lastAppliedResourceVersion'] = result['metadata']['resourceVersion']; write(state_path, state)
        wait_ready()
    else:
        state = json.loads(state_path.read_text())
        assert state['runId'] == args.run_id and deployment['metadata']['uid'] == state['deploymentUid']
        assert current.get(WINDOW) == state['until'], 'The observation window changed; do not overwrite another operator.'
        assert [e for e in env if e['name'] not in [WINDOW, TRUST]] == state['baselineEnv']
        if args.phase != 'abort':
            assert topology() == state['topology'], 'Controller topology changed; observe the same run, do not expand trust.'
        if args.phase == 'trust':
            assert state['phase'] == 'enabled' and TRUST not in current
            state['phase'] = 'trust-intent'; write(state_path, state)
            result = guarded_env(deployment, env + [{'name': TRUST, 'value': state['topology']['cidr']}])
            state['phase'] = 'trusted'; state['lastAppliedResourceVersion'] = result['metadata']['resourceVersion']; write(state_path, state)
            wait_ready()
        elif args.phase == 'node-proof':
            assert state['phase'] == 'trusted' and current.get(TRUST) == state['topology']['cidr']
            state['nodeProof'] = prove_node(args.run_id); write(state_path, state)
        elif args.phase == 'persist':
            assert state['phase'] == 'trusted' and state.get('nodeProof', {}).get('forgedBankHeadersIgnored')
            assert current.get(TRUST) == state['topology']['cidr']
            assert obj('-n', NS, 'get', 'configmap', CM, '--ignore-not-found') is None
            manifest = {'apiVersion': 'v1', 'kind': 'ConfigMap', 'metadata': {'name': CM, 'namespace': NS,
                'labels': {'app.kubernetes.io/part-of': 'ttb-commerce'},
                'annotations': {'mangosgo.com/transport-proof-run': args.run_id,
                                'mangosgo.com/ingress-controller-uid': state['topology']['controllerUid']}},
                'data': {TRUST: state['topology']['cidr']}}
            state['phase'] = 'persist-intent'; write(state_path, state)
            created = json.loads(command('create', '-f', '-', '-o', 'json', payload=manifest))
            state['transportConfigMapUid'] = created['metadata']['uid']; write(state_path, state)
            result = guarded_env(deployment, [e for e in env if e['name'] != TRUST])
            state['phase'] = 'persisted'; state['lastAppliedResourceVersion'] = result['metadata']['resourceVersion']; write(state_path, state)
            wait_ready()
        elif args.phase == 'finish':
            assert state['phase'] == 'persisted' and TRUST not in current
            config = obj('-n', NS, 'get', 'configmap', CM)
            assert config['metadata']['uid'] == state['transportConfigMapUid'] and config['data'] == {TRUST: state['topology']['cidr']}
            result = guarded_env(deployment, state['baselineEnv'])
            state['phase'] = 'complete'; state['lastAppliedResourceVersion'] = result['metadata']['resourceVersion']; write(state_path, state)
            wait_ready()
        else:
            # Restore only this run's env/data fields. Never roll back an entire workload or delete by an unguarded name.
            assert state['phase'] in ['enabled', 'trusted', 'trust-intent', 'persist-intent', 'persisted']
            assert current.get(TRUST) in [None, state['topology']['cidr']]
            if state.get('transportConfigMapUid'):
                config = obj('-n', NS, 'get', 'configmap', CM)
                assert config['metadata']['uid'] == state['transportConfigMapUid'] and config['data'] == {TRUST: state['topology']['cidr']}
                patch = [{'op': 'test', 'path': '/metadata/uid', 'value': config['metadata']['uid']},
                         {'op': 'test', 'path': '/metadata/resourceVersion', 'value': config['metadata']['resourceVersion']},
                         {'op': 'replace', 'path': '/data', 'value': {}}]
                command('-n', NS, 'patch', 'configmap', CM, '--type=json', '-p', json.dumps(patch), '-o', 'json')
            result = guarded_env(deployment, state['baselineEnv'])
            state['phase'] = 'aborted'; state['lastAppliedResourceVersion'] = result['metadata']['resourceVersion']; write(state_path, state)
            wait_ready()
    print(json.dumps({'runId': args.run_id, 'phase': state['phase'], 'topology': state['topology'],
                      'windowUntil': state['until'], 'readiness': health(), 'nodeProof': state.get('nodeProof'),
                      'bankCallbackAccepted': False, 'bankGatesChanged': False, 'sharedIngressChanged': False,
                      'metricsCredentialExported': False}))


if __name__ == '__main__':
    main()
