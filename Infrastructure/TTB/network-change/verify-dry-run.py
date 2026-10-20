"""Read and server-dry-run the one named shared Service. No persistent patch mode."""
import argparse
import base64
import copy
import datetime as dt
import hashlib
import json
import subprocess

NAMESPACE = 'mangos-ingress'
SERVICE = 'ingress-nginx-controller'
ROLLBACK_VERSION = '__APPLIED_RESOURCE_VERSION__'


def service():
    return json.loads(subprocess.run(['kubectl', '-n', NAMESPACE, 'get', 'service', SERVICE, '-o', 'json'],
                                    check=True, capture_output=True, text=True, timeout=20).stdout)


def dry_run(patch):
    # This flag is unconditional, including negative cases. There is deliberately no apply option.
    result = subprocess.run(['kubectl', '-n', NAMESPACE, 'patch', 'service', SERVICE, '--type=json',
                             '--dry-run=server', '--patch', json.dumps(patch), '-o', 'json'],
                            capture_output=True, text=True, timeout=20)
    return result.returncode, json.loads(result.stdout) if result.returncode == 0 else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--forward-base64', required=True)
    parser.add_argument('--rollback-base64', required=True)
    args = parser.parse_args()
    forward = json.loads(base64.b64decode(args.forward_base64, validate=True))
    rollback = json.loads(base64.b64decode(args.rollback_base64, validate=True))
    before = service()
    uid, version = before['metadata']['uid'], before['metadata']['resourceVersion']
    assert before['metadata']['namespace'] == NAMESPACE and before['metadata']['name'] == SERVICE
    assert before['spec']['type'] == 'LoadBalancer' and before['spec']['externalTrafficPolicy'] == 'Cluster'
    expected = [{'op': 'test', 'path': '/metadata/uid', 'value': uid},
                {'op': 'test', 'path': '/metadata/resourceVersion', 'value': version},
                {'op': 'test', 'path': '/spec/externalTrafficPolicy', 'value': 'Cluster'},
                {'op': 'replace', 'path': '/spec/externalTrafficPolicy', 'value': 'Local'}]
    assert forward == expected, 'Review the exact live identity/version before preparing a new proposal.'
    expected_rollback = copy.deepcopy(expected)
    expected_rollback[1]['value'] = ROLLBACK_VERSION
    expected_rollback[2]['value'] = 'Local'
    expected_rollback[3]['value'] = 'Cluster'
    assert rollback == expected_rollback, 'Rollback must be bound to the confirmed applied resourceVersion.'
    code, proposed = dry_run(forward)
    assert code == 0 and proposed['spec']['externalTrafficPolicy'] == 'Local'
    changed = sorted(key for key in set(before['spec']) | set(proposed['spec'])
                     if before['spec'].get(key) != proposed['spec'].get(key))
    checks = {}
    for name, index, value in [('wrongUidRejected', 0, '00000000-0000-0000-0000-000000000000'),
                               ('staleVersionRejected', 1, '0'), ('wrongBeforePolicyRejected', 2, 'Local')]:
        negative = copy.deepcopy(forward)
        negative[index]['value'] = value
        rejected, _ = dry_run(negative)
        checks[name] = rejected != 0
    code, _ = dry_run(rollback)
    checks['unresolvedRollbackVersionRejected'] = code != 0
    hydrated = copy.deepcopy(rollback)
    hydrated[1]['value'] = version
    code, _ = dry_run(hydrated)
    checks['rollbackRefusesCurrentClusterState'] = code != 0
    # One virtual document, not two live updates: this checks patch shape, not a real rollback or port allocation.
    code, round_trip = dry_run(forward + hydrated)
    checks['singleDryRunRoundTripAccepted'] = code == 0 and round_trip['spec'] == before['spec']
    after = service()
    unchanged = (after['metadata']['uid'] == uid and after['metadata']['resourceVersion'] == version and after['spec'] == before['spec'])
    proof = {'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'namespace': NAMESPACE, 'service': SERVICE,
             'uid': uid, 'resourceVersion': version, 'actualPolicy': after['spec']['externalTrafficPolicy'],
             'proposedPolicy': proposed['spec']['externalTrafficPolicy'], 'proposedSpecChangedKeys': changed,
             'proposedHealthCheckNodePort': proposed['spec'].get('healthCheckNodePort'),
             'forwardSha256': hashlib.sha256(json.dumps(forward, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
             'checks': checks, 'unchanged': unchanged, 'applied': False, 'realSourceIpAccepted': False}
    print(json.dumps(proof))
    if not unchanged or not all(checks.values()) or set(changed) - {'externalTrafficPolicy', 'healthCheckNodePort'}:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
