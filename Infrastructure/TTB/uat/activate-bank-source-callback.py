"""Activate only the bank-confirmed UAT callback profile after a compatible deploy.

Run on ntserver02. Default is dry-run. No ingress, proxy trust, bank source list,
acceptance flag, Secret, financial record or Production resource is changed.
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import subprocess

NS = 'ttb-uat'
CM = 'ttb-bank-acceptance'
BANK_CIDRS = {'203.148.142.200/32', *(f'203.148.143.{x}/32' for x in range(101, 106))}
SETTINGS = {
    'Ttb__CallbackAuthentication': 'BankSourceIp',
    'Ttb__CallbackProfileApproved': 'true',
    'Ttb__CallbackUsername': '',
    'Ttb__CallbackPassword': '',
}


def command(*args, payload=None, timeout=25):
    result = subprocess.run(['kubectl', *args], input=None if payload is None else json.dumps(payload),
                            text=True, capture_output=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError('UAT operation failed; inspect this same activation state before retrying.')
    return result.stdout


def obj(*args):
    return json.loads(command(*args, '-o', 'json'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    os.umask(0o077)
    current = obj('-n', NS, 'get', 'configmap', CM)
    assert current.get('data', {}).get('Ttb__CallbackAuthentication') in (None, 'Basic', 'BankSourceIp')
    assert current['data']['Ttb__GatewayAuthenticationValidated'] == 'true'
    assert current['data']['Ttb__UatTestCheckoutEnabled'] == 'true'
    assert 'Ttb__CallbackSourceIpValidated' not in current['data']

    controllers = obj('-n', 'mangos-ingress', 'get', 'pods', '-l', 'app.kubernetes.io/component=controller')['items']
    assert len(controllers) == 1
    controller = controllers[0]
    assert any(x['type'] == 'Ready' and x['status'] == 'True' for x in controller['status']['conditions'])
    transport = obj('-n', NS, 'get', 'configmap', 'ttb-transport')
    assert transport['data'] == {'Ttb__TrustedProxyCidrs__0': controller['status']['podIP'] + '/32'}
    assert transport['metadata']['annotations']['mangosgo.com/ingress-controller-uid'] == controller['metadata']['uid']

    ingress = obj('-n', NS, 'get', 'ingress', 'ttb-bank-callback-uat')
    annotations = ingress['metadata']['annotations']
    assert set(annotations['nginx.ingress.kubernetes.io/whitelist-source-range'].split(',')) == BANK_CIDRS
    assert annotations['nginx.ingress.kubernetes.io/ssl-redirect'] == 'true'
    assert ingress['spec']['tls'][0]['hosts'] == ['ttb-api-test.mangosgo.com']
    rules = ingress['spec']['rules']
    assert len(rules) == 1 and rules[0]['host'] == 'ttb-api-test.mangosgo.com'
    paths = rules[0]['http']['paths']
    assert len(paths) == 1 and paths[0]['path'] == '/ttb/v1/credit-notification' and paths[0]['pathType'] == 'Exact'
    assert paths[0]['backend']['service']['name'] == 'ttb-commerce-api'

    releases = {}
    for name in ('ttb-commerce-api', 'ttb-commerce-worker'):
        deployment = obj('-n', NS, 'get', 'deployment', name)
        spec = deployment['spec']['template']['spec']
        release = next(x['hostPath']['path'] for x in spec['volumes'] if x['name'] == 'app')
        assert release.startswith('/opt/ttb-commerce/uat/releases/') and release.endswith('/api')
        capability = json.loads((Path(release) / 'release-compatibility.json').read_text())
        assert 'bank-source-ip-callback-v1' in capability['capabilities']
        assert deployment['status']['observedGeneration'] == deployment['metadata']['generation']
        assert deployment['status']['readyReplicas'] == deployment['spec']['replicas'] == 1
        container = spec['containers'][0]
        assert not any(x['name'] in SETTINGS for x in container.get('env', []))
        references = container['envFrom']
        bank_index = next(i for i, x in enumerate(references) if x.get('secretRef', {}).get('name') == 'ttb-bank')
        acceptance_index = next(i for i, x in enumerate(references) if x.get('configMapRef', {}).get('name') == CM)
        assert acceptance_index > bank_index
        releases[name] = release

    state = {'environment': 'Uat', 'targetProfile': 'BankSourceIp', 'compatibleReleases': releases,
             'ingressUid': ingress['metadata']['uid'], 'ingressResourceVersion': ingress['metadata']['resourceVersion'],
             'transportUid': transport['metadata']['uid'], 'transportResourceVersion': transport['metadata']['resourceVersion'],
             'realBankCallbackAccepted': False, 'productionChanged': False}
    print(json.dumps({**state, 'dryRun': not args.apply}))
    if not args.apply:
        return
    directory = Path('/opt/ttb-commerce/uat/callback-source-profile') / dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    directory.mkdir(parents=True, mode=0o700, exist_ok=False)
    (directory / 'before.json').write_text(json.dumps(current, indent=2))
    data = {**current['data'], **SETTINGS}
    cm_annotations = {**current['metadata'].get('annotations', {}),
                      'mangosgo.com/callback-profile-source': 'TTB-Bird-confirmed-no-username-password-20261021'}
    patch = [{'op': 'test', 'path': '/metadata/uid', 'value': current['metadata']['uid']},
             {'op': 'test', 'path': '/metadata/resourceVersion', 'value': current['metadata']['resourceVersion']},
             {'op': 'replace', 'path': '/data', 'value': data},
             {'op': 'replace', 'path': '/metadata/annotations', 'value': cm_annotations}]
    after = json.loads(command('-n', NS, 'patch', 'configmap', CM, '--type=json', '-p', json.dumps(patch), '-o', 'json'))
    (directory / 'after.json').write_text(json.dumps(after, indent=2))
    for name in releases:
        command('-n', NS, 'rollout', 'restart', 'deployment/' + name)
        command('-n', NS, 'rollout', 'status', 'deployment/' + name, '--timeout=60s', timeout=65)
        mode = command('-n', NS, 'exec', 'deployment/' + name, '--', 'printenv', 'Ttb__CallbackAuthentication').strip()
        assert mode == 'BankSourceIp'
    state['activated'] = True
    (directory / 'state.json').write_text(json.dumps(state, indent=2))
    print(json.dumps({'activated': True, 'environment': 'Uat', 'callbackAuthentication': 'BankSourceIp',
                      'evidencePath': str(directory / 'state.json'), 'realBankCallbackAccepted': False, 'productionChanged': False}))


if __name__ == '__main__':
    main()
