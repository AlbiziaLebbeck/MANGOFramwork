"""Read-only UI QA baseline/postcheck; preserve existing shopper/session rows."""
import argparse
import datetime as dt
import json
import re
import subprocess


def command(*args):
    result = subprocess.run(['kubectl', *args], capture_output=True, text=True, timeout=25)
    if result.returncode:
        raise RuntimeError('Read-only UI QA status failed')
    return result.stdout


def obj(*args):
    return json.loads(command(*args, '-o', 'json'))


def metadata(namespace, kind, name):
    item = obj('-n', namespace, 'get', kind, name)
    return {'uid': item['metadata']['uid'], 'generation': item['metadata'].get('generation'),
            'resourceVersion': item['metadata']['resourceVersion'],
            'ready': item.get('status', {}).get('readyReplicas')}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run-id', required=True)
    args = parser.parse_args()
    if not re.fullmatch('[a-f0-9]{32}', args.run_id):
        raise ValueError('Invalid QA run identity')
    ns = 'ttb-uat'
    health = json.loads(command('-n', ns, 'exec', 'deployment/ttb-commerce-api', '--', 'curl', '-fsS',
                                'http://127.0.0.1:8080/health/ready'))
    names = ['Orders', 'Attempts', 'BankTransactions', 'Allocations', 'Observations', 'CallbackReceipts',
             'Voids', 'Reports', 'ManualRefunds', 'Documents', 'Entitlements', 'Fulfillments',
             'MockCredits', 'MockVoids', 'Jobs', 'GameTickets', 'Shoppers', 'Sessions']
    sql = 'SELECT json_build_object(' + ','.join(
        "'" + name + "',(SELECT count(*) FROM ttb.\"" + name + '\")' for name in names) + ')'
    counts = json.loads(command('-n', ns, 'exec', 'deployment/ttb-postgres', '--', 'psql', '-X', '-U',
                                'ttb_owner', '-d', 'ttb_uat', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql))
    qa = {kind: [x['metadata']['name'] for x in obj('-n', ns, 'get', kind)['items']
                 if args.run_id in x['metadata']['name']] for kind in ['jobs', 'pods', 'networkpolicies']}
    deployments = [{'name': x['metadata']['name'], 'ready': x['status'].get('readyReplicas', 0),
                    'desired': x['spec']['replicas']} for x in obj('-n', ns, 'get', 'deployments')['items']]
    print(json.dumps({'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'readOnly': True,
                      'health': health, 'counts': counts, 'deployments': deployments,
                      'api': metadata(ns, 'deployment', 'ttb-commerce-api'),
                      'kku': metadata('mangos', 'deployment', 'kku-commerce-api'),
                      'portal': metadata('mangos', 'deployment', 'mangos-upload-portal'),
                      'portalCode': metadata('mangos', 'configmap', 'upload-portal-app-code'),
                      'sharedService': metadata('mangos-ingress', 'service', 'ingress-nginx-controller'),
                      'temporaryQaResources': qa}))


if __name__ == '__main__':
    main()
