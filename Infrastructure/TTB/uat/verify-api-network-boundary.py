"""Measure the existing API ingress policy using two owned, health-only Jobs.

No policy, Service, application, key, database or financial route is changed.
Successful Jobs are deleted after checking their identities. Failures remain
bounded by activeDeadlineSeconds and are preserved for inspection, never retried.
"""
import argparse
import datetime as dt
import ipaddress
import json
from pathlib import Path
import re
import subprocess
import time

NS = 'ttb-uat'
PROBE = r'''
import http.client,json,sys
rows=[]
for kind,address in json.loads(sys.argv[1]):
    connection=http.client.HTTPConnection(address,8080,timeout=3)
    try:
        connection.request('GET','/health/live?network_boundary='+sys.argv[2],headers={'Host':'localhost','X-Forwarded-For':'203.148.143.101','X-Real-IP':'203.148.143.101'})
        response=connection.getresponse()
        body=json.loads(response.read(1024))
        rows.append({'target':kind,'status':response.status,'expectedHealthBody':body=={'status':'alive','service':'ttb-commerce'}})
    except (TimeoutError,ConnectionRefusedError,ConnectionResetError) as error:
        rows.append({'target':kind,'blockedTransport':type(error).__name__})
    finally:
        connection.close()
print(json.dumps({'probeRunId':sys.argv[2],'targets':rows,'method':'GET','path':'/health/live','credentialsOrPrivateVolumesMounted':False}))
'''


def command(*args, payload=None, timeout=20):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload is not None else None,
                            capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('kubectl failed during the owned network probe; inspect its named Jobs, not unrelated workloads.')
    return result.stdout


def obj(*args):
    return json.loads(command(*args, '-o', 'json'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    args = parser.parse_args()
    run = args.run_id
    assert re.fullmatch('[a-f0-9]{32}', run)
    directory = Path('/opt/ttb-commerce/network-probes') / run
    directory.mkdir(mode=0o700, parents=True, exist_ok=False)
    policy = obj('-n', NS, 'get', 'networkpolicy', 'ttb-api-ingress')
    assert policy['spec']['podSelector'] == {'matchLabels': {'app': 'ttb-commerce-api'}}
    assert policy['spec']['policyTypes'] == ['Ingress']
    pods = obj('-n', NS, 'get', 'pods', '-l', 'app=ttb-commerce-api')['items']
    ready = [p for p in pods if any(c['type'] == 'Ready' and c['status'] == 'True' for c in p['status'].get('conditions', []))]
    assert len(ready) == 1
    pod = ready[0]
    expected_image_id = next(c['imageID'] for c in pod['status']['containerStatuses'] if c['name'] == 'api').removeprefix('docker-pullable://')
    immutable_reference = bool(re.fullmatch('mcr.microsoft.com/playwright@sha256:[a-f0-9]{64}', expected_image_id))
    if immutable_reference:
        image = expected_image_id
    else:
        # Imported k3s images can expose only a config digest, without repository digests.
        # Never pull a tag: use this same node's cached API image and attest the actual probe digest.
        assert re.fullmatch('sha256:[a-f0-9]{64}', expected_image_id), 'Unexpected API image identity.'
        image = next(c['image'] for c in pod['spec']['containers'] if c['name'] == 'api')
        assert re.fullmatch('mcr.microsoft.com/playwright:(?:v[0-9.]+-noble)', image), 'Use only the existing approved API base image.'
    service = obj('-n', NS, 'get', 'service', 'ttb-commerce-api')
    targets = [('pod', pod['status']['podIP']), ('service', service['spec']['clusterIP'])]
    for kind, address in targets:
        assert ipaddress.ip_address(address).is_private and address.startswith('10.42.' if kind == 'pod' else '10.43.')
    jobs = {}
    for kind in ['allowed', 'denied']:
        name = 'ttb-net-probe-' + run[:16] + '-' + kind
        labels = {'app': 'ttb-network-boundary-probe', 'mangosgo.com/probe-run': run}
        if kind == 'allowed':
            labels['app.kubernetes.io/part-of'] = 'ttb-commerce'
        container = {'name': 'probe', 'image': image, 'imagePullPolicy': 'Never',
                     'command': ['python3', '-B', '-c', PROBE, json.dumps(targets), run],
                     'securityContext': {'readOnlyRootFilesystem': True, 'allowPrivilegeEscalation': False,
                                         'capabilities': {'drop': ['ALL']}},
                     'resources': {'requests': {'cpu': '10m', 'memory': '32Mi'},
                                   'limits': {'cpu': '100m', 'memory': '64Mi'}}}
        pod_spec = {'restartPolicy': 'Never', 'automountServiceAccountToken': False, 'nodeName': pod['spec']['nodeName'],
                    'securityContext': {'runAsNonRoot': True, 'runAsUser': 1000, 'runAsGroup': 1000,
                                        'seccompProfile': {'type': 'RuntimeDefault'}},
                    'containers': [container]}
        manifest = {'apiVersion': 'batch/v1', 'kind': 'Job',
                    'metadata': {'name': name, 'namespace': NS, 'labels': {'mangosgo.com/probe-run': run}},
                    'spec': {'backoffLimit': 0, 'activeDeadlineSeconds': 45,
                             'template': {'metadata': {'labels': labels}, 'spec': pod_spec}}}
        created = json.loads(command('create', '-f', '-', '-o', 'json', payload=manifest))
        jobs[kind] = {'name': name, 'uid': created['metadata']['uid']}
        (directory / (kind + '-identity.json')).write_text(json.dumps(jobs[kind]))
    deadline = time.monotonic() + 65
    while True:
        states = {kind: obj('-n', NS, 'get', 'job', job['name']) for kind, job in jobs.items()}
        assert all(states[k]['metadata']['uid'] == jobs[k]['uid'] for k in jobs)
        if any(state['status'].get('failed', 0) for state in states.values()):
            raise RuntimeError('An owned health-only probe failed; preserve Jobs for inspection.')
        if all(state['status'].get('succeeded') == 1 for state in states.values()):
            break
        if time.monotonic() >= deadline:
            raise RuntimeError('Observation deadline reached; inspect the same named Jobs, never recreate them on timeout.')
        time.sleep(1)
    results = {}
    for kind, job in jobs.items():
        probe_pods = obj('-n', NS, 'get', 'pods', '-l', 'job-name=' + job['name'])['items']
        assert len(probe_pods) == 1
        probe_pod = probe_pods[0]
        assert any(owner['uid'] == job['uid'] and owner.get('controller') for owner in probe_pod['metadata']['ownerReferences'])
        actual_image_id = next(c['imageID'] for c in probe_pod['status']['containerStatuses'] if c['name'] == 'probe').removeprefix('docker-pullable://')
        assert actual_image_id == expected_image_id, 'Probe did not execute the existing API image content.'
        result = json.loads(command('-n', NS, 'logs', 'job/' + job['name'], '--tail=1'))
        assert result['probeRunId'] == run and len(result['targets']) == 2
        result['actualImageIdMatchesApi'] = True
        results[kind] = result
    assert all(r.get('status') == 200 and r.get('expectedHealthBody') for r in results['allowed']['targets'])
    assert all(r.get('blockedTransport') in ['TimeoutError', 'ConnectionRefusedError', 'ConnectionResetError'] for r in results['denied']['targets'])
    current = obj('-n', NS, 'get', 'networkpolicy', 'ttb-api-ingress')
    assert current['metadata']['uid'] == policy['metadata']['uid'] and current['metadata']['resourceVersion'] == policy['metadata']['resourceVersion'] and current['spec'] == policy['spec']
    proof = {'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'runId': run,
             'networkPolicyUid': policy['metadata']['uid'], 'networkPolicyResourceVersion': policy['metadata']['resourceVersion'],
             'networkPolicyUnchanged': True, 'fixedImageDigest': expected_image_id, 'imageReference': image,
             'immutablePullReferenceUsed': immutable_reference, 'imagePullPolicy': 'Never',
             'sameNodeCachedImageContentAttested': True, 'results': results,
             'allowedHealthAccepted': True, 'unallowedPodBlockedForPodAndServiceAddresses': True,
             'existingApiPodUid': pod['metadata']['uid'], 'jobs': jobs,
             'bankRequestsOrFinancialRoutesUsed': False, 'applicationProxyTrustChanged': False,
             'actualBankCallbackAccepted': False}
    (directory / 'result-before-cleanup.json').write_text(json.dumps(proof))
    for kind, job in jobs.items():
        actual = obj('-n', NS, 'get', 'job', job['name'])
        assert actual['metadata']['uid'] == job['uid'] and actual['metadata']['labels'].get('mangosgo.com/probe-run') == run
        command('-n', NS, 'delete', 'job', job['name'], '--wait=true', '--timeout=20s', timeout=25)
    remaining = obj('-n', NS, 'get', 'jobs,pods', '-l', 'mangosgo.com/probe-run=' + run)['items']
    assert not remaining
    proof['ownedJobsAndPodsDeleted'] = True
    (directory / 'result.json').write_text(json.dumps(proof))
    print(json.dumps(proof))


if __name__ == '__main__':
    main()
