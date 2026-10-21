"""Run on ntserver02, --apply only after compatible UAT deployment. No Secret changes."""
import argparse
import datetime as dt
import json
from pathlib import Path
import subprocess

NS = 'ttb-uat'
SETTINGS = {'Ttb__BeneficiaryName': 'บริษัท สร้างสรรค์ปัญญา จำกัด',
            'Ttb__CallbackSourceIpValidated': 'true', 'Ttb__UatTestCheckoutEnabled': 'false',
            'BankTests__Enabled': 'true', 'BankTests__Reference1': 'TEST', 'BankTests__MaximumAmountMinor': '10000',
            'BankTests__ReadBudget': '10', 'BankTests__VoidEnabled': 'true'}

def run(*args, data=None, timeout=30):
    r = subprocess.run(['kubectl', *args], input=data, text=True, capture_output=True, timeout=timeout)
    if r.returncode:
        raise RuntimeError('UAT command failed; inspect the same rollout before retrying')
    return r.stdout

def obj(*args):
    return json.loads(run('-n', NS, *args, '-o', 'json'))

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--apply', action='store_true'); args = parser.parse_args()
    cm = obj('get', 'configmap', 'ttb-bank-acceptance')
    assert cm['data']['Ttb__CallbackAuthentication'] == 'BankSourceIp'
    assert cm['data']['Ttb__GatewayAuthenticationValidated'] == 'true'
    releases = {}
    for name in ('ttb-commerce-api', 'ttb-commerce-worker', 'ttb-receipt-worker'):
        d = obj('get', 'deployment', name); spec = d['spec']['template']['spec']
        release = next(x['hostPath']['path'] for x in spec['volumes'] if x['name'] == 'app')
        assert release.startswith('/opt/ttb-commerce/uat/releases/') and release.endswith('/api')
        capability = json.loads((Path(release) / 'release-compatibility.json').read_text())
        assert capability['readerContract'] >= 4 and 'isolated-bank-test-console-v1' in capability['capabilities']
        assert d['status']['observedGeneration'] == d['metadata']['generation']
        assert d['status']['readyReplicas'] == d['spec']['replicas'] == 1
        if name != 'ttb-receipt-worker':
            refs = spec['containers'][0]['envFrom']
            assert next(i for i, x in enumerate(refs) if x.get('configMapRef', {}).get('name') == 'ttb-bank-acceptance') > next(i for i, x in enumerate(refs) if x.get('secretRef', {}).get('name') == 'ttb-bank')
            assert not any(x['name'] in SETTINGS for x in spec['containers'][0].get('env', []))
        releases[name] = release
    # Confirm the original real bank callback evidence, without reading payer details.
    sql = '''BEGIN READ ONLY;
    SELECT count(*) FROM ttb."Orders" o JOIN ttb."Allocations" a ON a."OrderId"=o."Id"
    JOIN ttb."BankTransactions" b ON b."Id"=a."BankTransactionId"
    JOIN ttb."CallbackReceipts" r ON r."InstructionId"=b."InstructionId"
    JOIN ttb."Observations" p ON p."Id"=r."ObservationId"
    WHERE o."Number"='T20261021000002' AND o."PaymentState"='Paid' AND b."Environment"='Uat'
    AND b."MerchantId"='PYA' AND b."BillerNo"='075555900002101' AND b."AmountMinor"=100
    AND r."AckCode"='0000' AND p."Source"='Callback' AND p."Outcome"='Confirmed'
    AND p."MetadataJson"->>'peerIp'='203.148.143.103';
    ROLLBACK;'''
    proof = run('-n', NS, 'exec', '-i', 'deployment/ttb-postgres', '--', 'psql', '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-U', 'ttb_owner', '-d', 'ttb_uat', data=sql)
    assert proof.strip() == '1', 'Original real callback acceptance evidence was not found'
    print(json.dumps({'dryRun': not args.apply, 'compatibleReleases': releases, 'realCallbackProof': True, 'productionChanged': False}))
    if not args.apply:
        return
    directory = Path('/opt/ttb-commerce/uat/bank-test-activation') / dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    directory.mkdir(parents=True, mode=0o700, exist_ok=False)
    (directory / 'before.json').write_text(json.dumps(cm, ensure_ascii=False, indent=2))
    backup = obj('get', 'configmap', 'ttb-backup-scripts')
    encrypt = backup['data']['encrypt.sh']
    assert 'minimum_reader_contract=3\\n' in encrypt or 'minimum_reader_contract=4\\n' in encrypt
    if 'minimum_reader_contract=3\\n' in encrypt:
        backup_patch = [{'op': 'test', 'path': '/metadata/uid', 'value': backup['metadata']['uid']},
                        {'op': 'test', 'path': '/metadata/resourceVersion', 'value': backup['metadata']['resourceVersion']},
                        {'op': 'replace', 'path': '/data/encrypt.sh', 'value': encrypt.replace('minimum_reader_contract=3\\n', 'minimum_reader_contract=4\\n')}]
        run('-n', NS, 'patch', 'configmap', 'ttb-backup-scripts', '--type=json', '-p', json.dumps(backup_patch))
    (directory / 'backup-reader-floor.json').write_text(json.dumps({'readerContract': 4, 'secretChanged': False}))
    annotations = {**cm['metadata'].get('annotations', {}), 'mangosgo.com/callback-acceptance-order': 'T20261021000002',
                   'mangosgo.com/callback-acceptance-peer': '203.148.143.103', 'mangosgo.com/callback-acceptance-at': '2026-10-21T14:14:03.694881Z',
                   'mangosgo.com/beneficiary-source': 'operator-confirmed-bank-app-20261021'}
    patch = [{'op': 'test', 'path': '/metadata/uid', 'value': cm['metadata']['uid']},
             {'op': 'test', 'path': '/metadata/resourceVersion', 'value': cm['metadata']['resourceVersion']},
             {'op': 'replace', 'path': '/data', 'value': {**cm['data'], **SETTINGS}},
             {'op': 'replace', 'path': '/metadata/annotations', 'value': annotations}]
    after = json.loads(run('-n', NS, 'patch', 'configmap', 'ttb-bank-acceptance', '--type=json', '-p', json.dumps(patch), '-o', 'json'))
    (directory / 'after.json').write_text(json.dumps(after, ensure_ascii=False, indent=2))
    for name in ('ttb-commerce-api', 'ttb-commerce-worker'):
        run('-n', NS, 'rollout', 'restart', 'deployment/' + name)
        run('-n', NS, 'rollout', 'status', 'deployment/' + name, '--timeout=60s', timeout=65)
    print(json.dumps({'activated': True, 'environment': 'Uat', 'reference1': 'TEST', 'evidencePath': str(directory), 'productionChanged': False}))

if __name__ == '__main__':
    main()
