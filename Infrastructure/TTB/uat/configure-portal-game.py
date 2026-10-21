"""Bind only the Portal's ttb-uat Unity server to its existing UAT game identity.

Run on ntserver02. Default is a server dry-run; --apply records rollback evidence.
Bank/database credentials never leave their namespaces or appear in output.
"""
import argparse
import datetime as dt
import json
from pathlib import Path
import subprocess


def command(*args, payload=None):
    r = subprocess.run(['kubectl', *args], input=None if payload is None else json.dumps(payload),
                       capture_output=True, text=True, timeout=30)
    if r.returncode:
        raise RuntimeError('Portal game operation was not confirmed. Inspect existing named resources before retrying.')
    return r.stdout


def get(ns, kind, name):
    value = command('-n', ns, 'get', kind, name, '--ignore-not-found', '-o', 'json')
    return json.loads(value) if value.strip() else None


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--apply', action='store_true')
    args = p.parse_args()
    name = 'metaverse-server-ttb-uat'
    deployment = get('mangos', 'deployment', name)
    assert deployment and deployment['metadata']['labels']['mangos.dev/slug'] == 'ttb-uat'
    spec = deployment['spec']['template']['spec']
    matches = [(i, c) for i, c in enumerate(spec['containers']) if c['name'] == 'metaverse-server']
    assert len(matches) == 1
    index, container = matches[0]
    source = get('ttb-uat', 'secret', 'ttb-game-service')
    assert source and all(k in source['data'] for k in ('Storage__GameServerId', 'Storage__GameServerKey'))
    secret_name = 'ttb-portal-game-service'
    secret = {'apiVersion': 'v1', 'kind': 'Secret', 'metadata': {'name': secret_name, 'namespace': 'mangos',
              'labels': {'app.kubernetes.io/part-of': 'ttb-commerce', 'mangosgo.com/portal-game': 'ttb-uat'}},
              'type': 'Opaque', 'immutable': True, 'data': {
                  'game-id': source['data']['Storage__GameServerId'], 'game-key': source['data']['Storage__GameServerKey']}}
    existing_secret = get('mangos', 'secret', secret_name)
    if existing_secret:
        assert existing_secret['metadata']['labels'].get('mangosgo.com/portal-game') == 'ttb-uat'
        assert existing_secret.get('immutable') and existing_secret['data'] == secret['data'], 'Existing identity differs; no key rotation was attempted.'
    policy_name = 'ttb-api-portal-game'
    policy = {'apiVersion': 'networking.k8s.io/v1', 'kind': 'NetworkPolicy',
              'metadata': {'name': policy_name, 'namespace': 'ttb-uat', 'labels': {'app.kubernetes.io/part-of': 'ttb-commerce'}},
              'spec': {'podSelector': {'matchLabels': {'app': 'ttb-commerce-api'}}, 'policyTypes': ['Ingress'], 'ingress': [{
                  'from': [{'namespaceSelector': {'matchLabels': {'kubernetes.io/metadata.name': 'mangos'}},
                            'podSelector': {'matchLabels': {'app.kubernetes.io/name': name}}}],
                  'ports': [{'protocol': 'TCP', 'port': 8080}]}]}}
    existing_policy = get('ttb-uat', 'networkpolicy', policy_name)
    if existing_policy:
        assert existing_policy['spec'] == policy['spec'], 'Existing policy differs; inspect it first.'
    desired = [
        {'name': 'MANGOS_TTB_GAME_ENVIRONMENT', 'value': 'Uat'},
        {'name': 'MANGOS_TTB_GAME_API', 'value': 'http://ttb-commerce-api.ttb-uat.svc.cluster.local:8080/ttb-commerce-test/v1'},
        {'name': 'MANGOS_TTB_GAME_ID', 'valueFrom': {'secretKeyRef': {'name': secret_name, 'key': 'game-id'}}},
        {'name': 'MANGOS_TTB_GAME_KEY', 'valueFrom': {'secretKeyRef': {'name': secret_name, 'key': 'game-key'}}}]
    env = container.get('env', [])
    names = {x['name'] for x in desired}
    updated = [x for x in env if x['name'] not in names] + desired
    patch = [
        {'op': 'test', 'path': '/metadata/uid', 'value': deployment['metadata']['uid']},
        {'op': 'test', 'path': '/metadata/resourceVersion', 'value': deployment['metadata']['resourceVersion']},
        {'op': 'add', 'path': f'/spec/template/spec/containers/{index}/env', 'value': updated}]
    for resource, exists in ((secret, existing_secret), (policy, existing_policy)):
        if not exists:
            command('create', '--dry-run=server', '-f', '-', '-o', 'name', payload=resource)
    if updated != env:
        command('-n', 'mangos', 'patch', 'deployment', name, '--type=json', '--dry-run=server', '-p', json.dumps(patch), '-o', 'name')
    state = {'applied': args.apply, 'deployment': name, 'namespace': 'mangos', 'before': {
        'uid': deployment['metadata']['uid'], 'resourceVersion': deployment['metadata']['resourceVersion'], 'env': env},
        'secretCreated': False, 'policyCreated': False, 'environment': 'Uat', 'bankOrDatabaseSecretsCopied': False}
    if args.apply:
        folder = Path('/opt/ttb-commerce/uat/portal-game-handoff') / dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
        folder.mkdir(parents=True, exist_ok=False, mode=0o700)
        evidence = folder / 'state.json'
        evidence.write_text(json.dumps(state, indent=2))
        evidence.chmod(0o600)
        for resource, exists, key in ((secret, existing_secret, 'secretCreated'), (policy, existing_policy, 'policyCreated')):
            if not exists:
                command('create', '-f', '-', '-o', 'name', payload=resource)
                state[key] = True
                evidence.write_text(json.dumps(state, indent=2))
        if updated != env:
            command('-n', 'mangos', 'patch', 'deployment', name, '--type=json', '-p', json.dumps(patch), '-o', 'name')
        state['after'] = {k: get('mangos', 'deployment', name)['metadata'][k] for k in ('uid', 'resourceVersion')}
        evidence.write_text(json.dumps(state, indent=2))
        state['evidence'] = str(evidence)
    print(json.dumps({k: v for k, v in state.items() if k != 'before'}, indent=2))


if __name__ == '__main__':
    main()
