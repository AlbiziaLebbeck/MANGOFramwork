"""One explicitly authorized Service test, with a durable, version-bound rollback.

Never invoke this as part of routine deployment. The 19 Oct human authorization
is scoped to this shared-Service test and KKU checks, not bank or public routes.
"""
import argparse
import datetime as dt
import json
from pathlib import Path
import re
import subprocess

NS = 'mangos-ingress'
NAME = 'ingress-nginx-controller'
UID = '1cca9e12-7496-4fa3-94c5-01f6b5778bb2'
VERSION = '999'


def kubectl(*args):
    return json.loads(subprocess.run(['kubectl', *args, '-o', 'json'], check=True,
                                    capture_output=True, text=True, timeout=20).stdout)


def service():
    return kubectl('-n', NS, 'get', 'service', NAME)


def patch(version, old, new):
    return [{'op': 'test', 'path': '/metadata/uid', 'value': UID},
            {'op': 'test', 'path': '/metadata/resourceVersion', 'value': version},
            {'op': 'test', 'path': '/spec/externalTrafficPolicy', 'value': old},
            {'op': 'replace', 'path': '/spec/externalTrafficPolicy', 'value': new}]


def write(path, value):
    with path.open('x', encoding='utf-8') as stream:
        json.dump(value, stream)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--action', required=True, choices=['apply', 'rollback'])
    parser.add_argument('--run-id', required=True)
    args = parser.parse_args()
    assert re.fullmatch('[a-f0-9]{32}', args.run_id), 'Invalid owned run ID.'
    directory = Path('/opt/ttb-commerce/network-change') / args.run_id
    current = service()
    assert current['metadata']['uid'] == UID, 'Service identity changed; do not overwrite.'
    if args.action == 'apply':
        assert current['metadata']['resourceVersion'] == VERSION and current['spec']['externalTrafficPolicy'] == 'Cluster'
        assert current['spec']['type'] == 'LoadBalancer'
        for namespace, deployment in [(NS, NAME), ('mangos', 'kku-commerce-api')]:
            deployment = kubectl('-n', namespace, 'get', 'deployment', deployment)
            assert deployment['status'].get('readyReplicas') == 1
        nodes = kubectl('get', 'nodes')['items']
        ready = {n['metadata']['name'] for n in nodes if any(c['type'] == 'Ready' and c['status'] == 'True' for c in n['status']['conditions'])}
        slices = kubectl('-n', NS, 'get', 'endpointslices', '-l', 'kubernetes.io/service-name=' + NAME)['items']
        endpoints = [e for s in slices for e in s['endpoints'] if e.get('conditions', {}).get('ready') is True]
        assert len(ready) == 1 and endpoints and all(e.get('nodeName') in ready for e in endpoints)
        change = patch(VERSION, 'Cluster', 'Local')
        proposed = kubectl('-n', NS, 'patch', 'service', NAME, '--type=json', '--dry-run=server', '--patch', json.dumps(change))
        diff = {k for k in set(current['spec']) | set(proposed['spec']) if current['spec'].get(k) != proposed['spec'].get(k)}
        assert diff == {'externalTrafficPolicy', 'healthCheckNodePort'}
        directory.mkdir(mode=0o700, parents=True, exist_ok=False)
        write(directory / 'before.json', current)
        write(directory / 'forward.json', change)
    else:
        applied = json.loads((directory / 'applied.json').read_text())
        assert current['metadata']['resourceVersion'] == applied['metadata']['resourceVersion'], 'Version changed since the owned test; inspect before rollback.'
        assert current['spec'] == applied['spec'] and current['spec']['externalTrafficPolicy'] == 'Local'
        change = patch(applied['metadata']['resourceVersion'], 'Local', 'Cluster')
        write(directory / 'rollback-request.json', change)
    # Send exactly once. If the response is lost, persist an observation and stop:
    # the caller must inspect state rather than retry this mutation blindly.
    try:
        result = kubectl('-n', NS, 'patch', 'service', NAME, '--type=json', '--patch', json.dumps(change))
    except Exception:
        write(directory / (args.action + '-uncertain-observation.json'), service())
        raise
    write(directory / ('applied.json' if args.action == 'apply' else 'rolled-back.json'), result)
    if args.action == 'apply':
        write(directory / 'rollback.json', patch(result['metadata']['resourceVersion'], 'Local', 'Cluster'))
    else:
        before = json.loads((directory / 'before.json').read_text())
        assert result['spec'] == before['spec'], 'Rollback response spec differs; inspect immediately.'
    diff = sorted(k for k in set(current['spec']) | set(result['spec']) if current['spec'].get(k) != result['spec'].get(k))
    print(json.dumps({'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'runId': args.run_id,
                      'action': args.action, 'uid': result['metadata']['uid'],
                      'resourceVersion': result['metadata']['resourceVersion'],
                      'policy': result['spec']['externalTrafficPolicy'],
                      'healthCheckNodePort': result['spec'].get('healthCheckNodePort'), 'changedSpecKeys': diff,
                      'persistentEvidenceDirectory': str(directory), 'bankOrRouteChanges': False}))


if __name__ == '__main__':
    main()
