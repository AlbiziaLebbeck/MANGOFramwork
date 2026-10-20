"""Private, bounded Linux smoke runner for a Portal build. No Service or Ingress.

Upload the approved Server ZIP to this run's directory before create. Never replay
create after an observation timeout. Cleanup is restricted to the recorded Job UID.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import zipfile

NS = 'ttb-uat'


def command(*args, payload=None):
    result = subprocess.run(['kubectl', *args], input=json.dumps(payload) if payload is not None else None,
                            capture_output=True, text=True, timeout=25)
    if result.returncode:
        raise RuntimeError('Private Portal smoke operation failed. Observe this run; do not repeat creation.')
    return result.stdout


def obj(*args):
    return json.loads(command(*args, '-o', 'json'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--archive-sha256', required=True)
    parser.add_argument('--phase', choices=['create', 'observe', 'cleanup'], required=True)
    args = parser.parse_args()
    if not re.fullmatch('[a-f0-9]{32}', args.run_id) or not re.fullmatch('[a-f0-9]{64}', args.archive_sha256):
        raise ValueError('Invalid immutable build/archive identity')
    directory = Path('/opt/ttb-commerce/uat/portal-qa') / args.run_id
    state_path = directory / 'state.json'
    name = 'ttb-portal-qa-' + args.run_id
    label = 'mangosgo.com/portal-qa'

    def save(state):
        state_path.write_text(json.dumps(state))

    if args.phase == 'create':
        if state_path.exists():
            raise RuntimeError('This smoke run was already attempted. Observe or clean it up.')
        archive = directory / 'ttb-uat-server.zip'
        if hashlib.sha256(archive.read_bytes()).hexdigest() != args.archive_sha256:
            raise RuntimeError('The uploaded Server ZIP hash differs from the verified local release')
        source = directory / 'Server'
        source.mkdir(mode=0o755)
        with zipfile.ZipFile(archive) as bundle:
            for entry in bundle.infolist():
                path = Path(entry.filename)
                if path.is_absolute() or '..' in path.parts or '\\' in entry.filename or stat.S_ISLNK(entry.external_attr >> 16):
                    raise RuntimeError('Unsafe Server archive entry')
                if '_DoNotShip' in entry.filename or 'ButDontShipItWithYourGame' in entry.filename:
                    raise RuntimeError('Debug/recovery folders must not be uploaded in the shipping archive')
            bundle.extractall(source)
        for path in [directory, source, *source.rglob('*')]:
            path.chmod(0o755 if path.is_dir() else 0o644)
        if (source / 'MANGOsServer.x86_64').read_bytes()[:4] != b'\x7fELF' or not (source / 'GameAssembly.so').is_file():
            raise RuntimeError('Expected a complete Linux IL2CPP Server release')
        api = obj('-n', NS, 'get', 'deployment', 'ttb-commerce-api')
        node = obj('-n', NS, 'get', 'pods', '-l', 'app=ttb-commerce-api')['items'][0]['spec']['nodeName']
        health = json.loads(command('-n', NS, 'exec', 'deployment/ttb-commerce-api', '--', 'curl', '-fsS', 'http://127.0.0.1:8080/health/ready'))
        if health['paymentsReady'] or health['paymentMode'] != 'Bank':
            raise RuntimeError('This private QA requires the existing UAT Bank profile with payments closed')
        labels = {'app': 'ttb-portal-qa', 'app.kubernetes.io/part-of': 'ttb-commerce', label: args.run_id}
        policy = {'apiVersion': 'networking.k8s.io/v1', 'kind': 'NetworkPolicy', 'metadata': {'name': name, 'namespace': NS, 'labels': labels},
                  'spec': {'podSelector': {'matchLabels': {label: args.run_id}}, 'policyTypes': ['Ingress', 'Egress'], 'ingress': [], 'egress': [
                      {'to': [{'podSelector': {'matchLabels': {'app': 'ttb-commerce-api'}}}], 'ports': [{'protocol': 'TCP', 'port': 8080}]},
                      {'to': [{'namespaceSelector': {'matchLabels': {'kubernetes.io/metadata.name': 'kube-system'}}, 'podSelector': {'matchLabels': {'k8s-app': 'kube-dns'}}}],
                       'ports': [{'protocol': 'UDP', 'port': 53}, {'protocol': 'TCP', 'port': 53}]}]}}
        security = {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True, 'capabilities': {'drop': ['ALL']}}
        job = {'apiVersion': 'batch/v1', 'kind': 'Job', 'metadata': {'name': name, 'namespace': NS, 'labels': labels}, 'spec': {
            'backoffLimit': 0, 'activeDeadlineSeconds': 900, 'ttlSecondsAfterFinished': 600,
            'template': {'metadata': {'labels': labels}, 'spec': {'nodeName': node, 'automountServiceAccountToken': False, 'restartPolicy': 'Never',
                'securityContext': {'runAsNonRoot': True, 'runAsUser': 1000, 'runAsGroup': 1000, 'fsGroup': 1000},
                'initContainers': [{'name': 'copy-runtime', 'image': 'ubuntu:24.04', 'imagePullPolicy': 'Never', 'securityContext': security,
                    'command': ['/bin/sh', '-c', 'cp -R /source/. /runtime/ && chmod +x /runtime/MANGOsServer.x86_64'],
                    'volumeMounts': [{'name': 'source', 'mountPath': '/source', 'readOnly': True}, {'name': 'runtime', 'mountPath': '/runtime'}]}],
                'containers': [{'name': 'game', 'image': 'ubuntu:24.04', 'imagePullPolicy': 'Never', 'securityContext': security, 'workingDir': '/runtime',
                    'command': ['/runtime/MANGOsServer.x86_64'], 'args': ['-batchmode', '-nographics', '-logFile', '-'],
                    'env': [{'name': 'HOME', 'value': '/tmp'}, {'name': 'XDG_CONFIG_HOME', 'value': '/tmp/.config'},
                        {'name': 'MANGOS_TTB_GAME_ENVIRONMENT', 'value': 'Uat'},
                        {'name': 'MANGOS_TTB_GAME_API', 'value': 'http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce-test/v1'},
                        {'name': 'MANGOS_TTB_GAME_ID', 'valueFrom': {'secretKeyRef': {'name': 'ttb-game-service', 'key': 'Storage__GameServerId'}}},
                        {'name': 'MANGOS_TTB_GAME_KEY', 'valueFrom': {'secretKeyRef': {'name': 'ttb-game-service', 'key': 'Storage__GameServerKey'}}}],
                    'resources': {'requests': {'cpu': '100m', 'memory': '256Mi'}, 'limits': {'cpu': '1', 'memory': '1Gi'}},
                    'readinessProbe': {'tcpSocket': {'port': 7770}, 'initialDelaySeconds': 3, 'periodSeconds': 3},
                    'volumeMounts': [{'name': 'runtime', 'mountPath': '/runtime'}, {'name': 'temporary', 'mountPath': '/tmp'}]}],
                'volumes': [{'name': 'source', 'hostPath': {'path': str(source), 'type': 'Directory'}},
                            {'name': 'runtime', 'emptyDir': {'sizeLimit': '1Gi'}}, {'name': 'temporary', 'emptyDir': {'sizeLimit': '128Mi'}}]}}}}
        state = {'runId': args.run_id, 'archiveSha256': args.archive_sha256, 'name': name, 'phase': 'create-attempted', 'apiUid': api['metadata']['uid'], 'apiGeneration': api['metadata']['generation']}
        save(state)
        for resource in (policy, job):
            command('create', '--dry-run=server', '-f', '-', payload=resource)
        p = json.loads(command('create', '-f', '-', '-o', 'json', payload=policy))
        state['policyUid'] = p['metadata']['uid']; save(state)
        j = json.loads(command('create', '-f', '-', '-o', 'json', payload=job))
        state['jobUid'] = j['metadata']['uid']; state['phase'] = 'created'; save(state)
        command('-n', NS, 'patch', 'networkpolicy', name, '--type=json', '-p', json.dumps([
            {'op': 'test', 'path': '/metadata/uid', 'value': state['policyUid']},
            {'op': 'add', 'path': '/metadata/ownerReferences', 'value': [{'apiVersion': 'batch/v1', 'kind': 'Job', 'name': name,
                'uid': state['jobUid'], 'controller': False, 'blockOwnerDeletion': False}]}]))
        print(json.dumps({'runId': args.run_id, 'phase': 'created', 'jobUid': state['jobUid'], 'publicRouteCreated': False}))
        return
    state = json.loads(state_path.read_text())
    if state['archiveSha256'] != args.archive_sha256:
        raise RuntimeError('Observe the same immutable archive')
    job = obj('-n', NS, 'get', 'job', name)
    if job['metadata']['uid'] != state['jobUid']:
        raise RuntimeError('The owned Job was replaced')
    if args.phase == 'cleanup':
        command('-n', NS, 'delete', 'job', name, '--wait=false')
        state['phase'] = 'cleanup-requested'; save(state)
        print(json.dumps({'runId': args.run_id, 'phase': state['phase'], 'jobUid': state['jobUid']}))
        return
    if job['status'].get('failed'):
        raise RuntimeError('The private Job failed. Preserve its evidence; do not recreate it')
    pods = obj('-n', NS, 'get', 'pods', '-l', label + '=' + args.run_id)['items']
    ready = len(pods) == 1 and any(c['type'] == 'Ready' and c['status'] == 'True' for c in pods[0]['status'].get('conditions', []))
    api = obj('-n', NS, 'get', 'deployment', 'ttb-commerce-api')
    if api['metadata']['uid'] != state['apiUid'] or api['metadata']['generation'] != state['apiGeneration']:
        raise RuntimeError('The existing UAT API changed during private QA')
    result = {'runId': args.run_id, 'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'phase': 'ready' if ready else 'starting',
              'jobUid': state['jobUid'], 'pod': pods[0]['metadata']['name'] if len(pods) == 1 else None,
              'podUid': pods[0]['metadata']['uid'] if len(pods) == 1 else None, 'publicRouteCreated': False, 'portalChanged': False, 'bankRequestMade': False}
    (directory / 'result.json').write_text(json.dumps(result))
    print(json.dumps(result))


if __name__ == '__main__':
    main()
