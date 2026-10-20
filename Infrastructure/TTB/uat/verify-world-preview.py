"""Read-only postcheck of the authorized operator-only UAT preview.

Run on ntserver02 through SSH stdin. This makes no bank HTTP requests and reads
only aggregate database counts, resource metadata and private health/monitoring.
"""
import datetime as dt
import json
import subprocess

NS = 'ttb-uat'


def run(*args):
    result = subprocess.run(['kubectl', *args], capture_output=True, text=True, timeout=25)
    if result.returncode:
        raise RuntimeError('Read-only postcheck failed; inspect resource names without disclosing secrets')
    return result.stdout


def obj(*args):
    return json.loads(run(*args, '-o', 'json'))


def api_get(url):
    return json.loads(run('-n', NS, 'exec', 'deployment/ttb-commerce-api', '--', 'curl', '-fsS', url))


def main():
    health = api_get('http://127.0.0.1:8080/health/ready')
    deployments = [{'name': x['metadata']['name'], 'ready': x['status'].get('readyReplicas', 0),
                    'desired': x['spec']['replicas'], 'generation': x['metadata']['generation']}
                   for x in obj('-n', NS, 'get', 'deployments')['items']]
    names = ['Orders', 'Attempts', 'BankTransactions', 'Allocations', 'Observations', 'CallbackReceipts',
             'Voids', 'Reports', 'ManualRefunds', 'Documents', 'Entitlements', 'Fulfillments',
             'MockCredits', 'MockVoids', 'Jobs', 'GameTickets', 'Shoppers', 'Sessions']
    sql = 'SELECT json_build_object(' + ','.join(
        "'" + name + "',(SELECT count(*) FROM ttb.\"" + name + '\")' for name in names) + ')'
    counts = json.loads(run('-n', NS, 'exec', 'deployment/ttb-postgres', '--', 'psql', '-X', '-U',
                            'ttb_owner', '-d', 'ttb_uat', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql))
    kku = obj('-n', 'mangos', 'get', 'deployment', 'kku-commerce-api')
    service = obj('-n', 'mangos-ingress', 'get', 'service', 'ingress-nginx-controller')
    backup = obj('-n', NS, 'get', 'cronjob', 'ttb-uat-backup')
    ingresses = [{'name': x['metadata']['name'], 'uid': x['metadata']['uid'],
                  'whitelist': x['metadata']['annotations'].get('nginx.ingress.kubernetes.io/whitelist-source-range'),
                  'paths': [p['path'] for r in x['spec']['rules'] for p in r['http']['paths']]}
                 for x in obj('-n', NS, 'get', 'ingresses')['items']]
    prom = obj('-n', NS, 'get', 'service', 'ttb-prometheus')['spec']['clusterIP']
    targets = api_get('http://' + prom + ':9090/api/v1/targets')['data']['activeTargets']
    rules = [r for g in api_get('http://' + prom + ':9090/api/v1/rules')['data']['groups'] for r in g['rules']]
    result = {'verifiedAt': dt.datetime.now(dt.timezone.utc).isoformat(), 'health': health,
              'deployments': deployments, 'kkuReady': kku['status'].get('readyReplicas', 0),
              'sharedService': {'uid': service['metadata']['uid'], 'resourceVersion': service['metadata']['resourceVersion'],
                                'externalTrafficPolicy': service['spec']['externalTrafficPolicy'],
                                'healthCheckNodePort': service['spec']['healthCheckNodePort']},
              'ingresses': ingresses, 'financialAndIdentityCounts': counts,
              'backup': {'uid': backup['metadata']['uid'], 'suspended': backup['spec'].get('suspend', False),
                         'schedule': backup['spec']['schedule']},
              'monitor': {'targets': [{'job': t['labels']['job'], 'health': t['health']} for t in targets],
                          'rules': len(rules), 'evaluationHealthy': all(r['health'] == 'ok' for r in rules)},
              'readOnly': True, 'bankHttpRequestMade': False, 'publicPreviewCreated': True}
    checks = {
        'deploymentsReady': len(deployments) == 9 and all(d['ready'] == d['desired'] == 1 for d in deployments),
        'kkuReady': result['kkuReady'] == 1,
        'financialAndIdentityEmpty': not any(counts.values()),
        'bankGatesClosed': health['status'] == 'ready' and health['paymentMode'] == 'Bank'
                          and not health['paymentsReady'] and not health['reportsEnabled'] and not health['recoveryFrozen'],
        'sharedServiceUnchanged': result['sharedService'] == {
            'uid': '1cca9e12-7496-4fa3-94c5-01f6b5778bb2', 'resourceVersion': '5847288',
            'externalTrafficPolicy': 'Local', 'healthCheckNodePort': 31842},
        'approvedRoutesOnly': len(ingresses) == 2
                             and all(i['whitelist'] == '58.11.26.155/32' for i in ingresses)
                             and {i['name']: i['paths'] for i in ingresses} == {
                                 'ttb-uat-tls-check': ['/health/live'],
                                 'ttb-uat-world-preview': ['/store-uat', '/ttb-game-uat', '/ttb-commerce-test/v1']},
        'monitorHealthy': len(targets) == 3 and all(t['health'] == 'up' for t in targets)
                          and len(rules) == 32 and result['monitor']['evaluationHealthy'],
        'backupScheduleActive': not result['backup']['suspended'] and result['backup']['schedule'] == '*/15 * * * *'}
    result['checks'] = checks
    result['passed'] = all(checks.values())
    print(json.dumps(result))
    if not result['passed']:
        raise SystemExit('UAT preview postcheck did not pass; preserve the observed evidence')


if __name__ == '__main__':
    main()
