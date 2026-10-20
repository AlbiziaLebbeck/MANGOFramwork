"""Operator-IP-only HTTPS/WSS preview; never expose bank callbacks or metrics."""
import argparse
import datetime as dt
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import subprocess

NS = 'ttb-uat'
NAME = 'ttb-uat-world-preview'
HOST = 'ttb-api-test.mangosgo.com'


def command(*args, payload=None):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload is not None else None, capture_output=True, text=True, timeout=25)
    if result.returncode:
        raise RuntimeError('Preview operation was not confirmed. Inspect this existing run; do not repeat creation.')
    return result.stdout


def obj(*args):
    return json.loads(command(*args, '-o', 'json'))


def identity(resource):
    return {k: resource['metadata'][k] for k in ('uid', 'resourceVersion')}


def topology():
    return {'sharedService': identity(obj('-n', 'mangos-ingress', 'get', 'service', 'ingress-nginx-controller')),
            'apiPolicy': identity(obj('-n', NS, 'get', 'networkpolicy', 'ttb-api-ingress')),
            'proxyConfig': identity(obj('-n', NS, 'get', 'configmap', 'ttb-transport')),
            'healthIngress': identity(obj('-n', NS, 'get', 'ingress', 'ttb-uat-tls-check'))}


def ready():
    return json.loads(command('-n', NS, 'exec', 'deployment/ttb-commerce-api', '--', 'curl', '-fsS', 'http://127.0.0.1:8080/health/ready'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--phase', choices=['prepare', 'create', 'observe'], required=True)
    args = parser.parse_args()
    if not re.fullmatch('[a-f0-9]{32}', args.run_id):
        raise ValueError('Invalid verified world run ID')
    os.umask(0o077)
    world = Path('/opt/ttb-commerce/uat/world-releases') / args.run_id
    result = json.loads((world / 'result.json').read_text())
    if result['phase'] != 'verified':
        raise RuntimeError('World deployment is not verified')
    directory = world / 'preview'
    state_path = directory / 'state.json'
    if args.phase in ('prepare', 'create'):
        directory.mkdir(exist_ok=(args.phase == 'create'))
        pending = json.loads(state_path.read_text()) if state_path.exists() else None
        if pending and pending['phase'] != 'prepared':
            raise RuntimeError('An existing create was attempted. Observe it rather than recreating the route')
        for name in ('ttb-world-uat', 'ttb-world-web-uat', 'ttb-commerce-api'):
            actual = obj('-n', NS, 'get', 'deployment', name)
            if actual['status'].get('observedGeneration') != actual['metadata']['generation'] or actual['status'].get('readyReplicas') != 1 or actual['metadata']['generation'] != result['pods'][name]['generation']:
                raise RuntimeError('The verified world/API rollout changed')
        health = ready()
        if health['status'] != 'ready' or health['paymentMode'] != 'Bank' or health['paymentsReady'] or health['reportsEnabled'] or health['recoveryFrozen']:
            raise RuntimeError('Preview requires UAT with financial gates closed')
        certificate = obj('-n', NS, 'get', 'certificate', 'ttb-api-test')
        if certificate['spec']['dnsNames'] != [HOST] or not any(c['type'] == 'Ready' and c['status'] == 'True' for c in certificate['status'].get('conditions', [])):
            raise RuntimeError('Owned UAT TLS certificate is not ready')
        existing = obj('get', 'ingresses', '-A')['items']
        matching = [i for i in existing if any(r.get('host') == HOST for r in i['spec'].get('rules', []))]
        if len(matching) != 1 or matching[0]['metadata']['namespace'] != NS or matching[0]['metadata']['name'] != 'ttb-uat-tls-check':
            raise RuntimeError('Unexpected existing routes own the UAT host')
        baseline = matching[0]
        if baseline['spec']['rules'][0]['http']['paths'][0]['path'] != '/health/live':
            raise RuntimeError('Expected the existing health-only route')
        cidr = baseline['metadata']['annotations']['nginx.ingress.kubernetes.io/whitelist-source-range']
        network = ipaddress.ip_network(cidr)
        if not network.is_global or network.version != 4 or network.prefixlen != 32 or str(network.network_address) != os.environ['SSH_CONNECTION'].split()[0]:
            raise RuntimeError('The existing verified operator /32 does not match this SSH peer; no preview exposed')
        ingress = {'apiVersion': 'networking.k8s.io/v1', 'kind': 'Ingress', 'metadata': {'name': NAME, 'namespace': NS,
            'labels': {'app.kubernetes.io/part-of': 'ttb-commerce', 'mangosgo.com/world-run': args.run_id},
            'annotations': {'nginx.ingress.kubernetes.io/whitelist-source-range': cidr, 'nginx.ingress.kubernetes.io/force-ssl-redirect': 'true',
                            'nginx.ingress.kubernetes.io/proxy-body-size': '256k', 'nginx.ingress.kubernetes.io/proxy-read-timeout': '3600',
                            'nginx.ingress.kubernetes.io/proxy-send-timeout': '3600'}},
            'spec': {'ingressClassName': 'nginx', 'tls': [{'hosts': [HOST], 'secretName': 'ttb-api-test-tls'}],
                'rules': [{'host': HOST, 'http': {'paths': [
                    {'path': '/store-uat', 'pathType': 'Prefix', 'backend': {'service': {'name': 'ttb-world-web-uat', 'port': {'name': 'tcp'}}}},
                    {'path': '/ttb-game-uat', 'pathType': 'Exact', 'backend': {'service': {'name': 'ttb-world-uat', 'port': {'name': 'tcp'}}}},
                    {'path': '/ttb-commerce-test/v1', 'pathType': 'Prefix', 'backend': {'service': {'name': 'ttb-commerce-api', 'port': {'name': 'http'}}}}
                ]}}]}}
        command('create', '--dry-run=server', '-f', '-', payload=ingress)
        state = {'runId': args.run_id, 'phase': 'prepared' if args.phase == 'prepare' else 'create-attempted', 'topology': topology(), 'operatorIpv4Sha256': hashlib.sha256(str(network.network_address).encode()).hexdigest()}
        if pending and (pending['topology'] != state['topology'] or json.loads((directory / 'ingress.json').read_text()) != ingress):
            raise RuntimeError('The prepared preview boundary changed. Preserve it for review')
        state_path.write_text(json.dumps(state))
        (directory / 'ingress.json').write_text(json.dumps(ingress))
        if args.phase == 'prepare':
            print(json.dumps({'runId': args.run_id, 'phase': 'prepared', 'serverDryRunPassed': True, 'ingressCreated': False, 'operatorOnly': True,
                              'paths': ['/store-uat', '/ttb-game-uat', '/ttb-commerce-test/v1'], 'callbackExposed': False, 'metricsExposed': False, 'bankAccepted': False}))
            return
        created = json.loads(command('create', '-f', '-', '-o', 'json', payload=ingress))
        state['ingressUid'] = created['metadata']['uid']; state['phase'] = 'created'; state_path.write_text(json.dumps(state))
        print(json.dumps({'runId': args.run_id, 'phase': 'created', 'ingressUid': state['ingressUid'], 'operatorOnly': True, 'callbackExposed': False, 'metricsExposed': False}))
        return
    state = json.loads(state_path.read_text())
    actual = obj('-n', NS, 'get', 'ingress', NAME)
    expected = json.loads((directory / 'ingress.json').read_text())
    if actual['metadata']['uid'] != state['ingressUid'] or actual['spec'] != expected['spec']:
        raise RuntimeError('The owned preview ingress was replaced or changed')
    for key, value in expected['metadata']['annotations'].items():
        if actual['metadata']['annotations'].get(key) != value:
            raise RuntimeError('Preview transport/allowlist annotation changed')
    if topology() != state['topology']:
        raise RuntimeError('Existing transport resources changed')
    if ready()['paymentsReady']:
        raise RuntimeError('Bank readiness unexpectedly changed')
    result = {'runId': args.run_id, 'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'phase': 'route-verified', 'ingressUid': state['ingressUid'],
              'operatorOnly': True, 'url': 'https://' + HOST + '/store-uat/', 'callbackExposed': False, 'metricsExposed': False,
              'bankAccepted': False, 'sharedTransportUnchanged': True, 'productionChanged': False, 'kkuChanged': False}
    (directory / 'result.json').write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == '__main__':
    main()
