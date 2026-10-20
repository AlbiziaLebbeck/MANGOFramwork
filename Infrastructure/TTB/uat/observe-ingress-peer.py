"""Read only this preflight's ingress log lines and export no raw address/header/user data."""
import argparse
import hashlib
import ipaddress
import json
import os
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--probe-run-id', required=True)
args = parser.parse_args()
if re.fullmatch('[a-f0-9]{32}', args.probe_run_id) is None:
    raise ValueError('Expected the exact owned health preflight correlation ID.')
expected = os.environ['SSH_CONNECTION'].split()[0]
marker = re.compile(r'transport_probe=' + args.probe_run_id + r'(?:-forged|-node)?(?:[\s"&]|$)')
result = subprocess.run(['kubectl', '-n', 'mangos-ingress', 'logs', '-l',
    'app.kubernetes.io/name=ingress-nginx,app.kubernetes.io/component=controller', '--since=10m', '--tail=1000'],
    capture_output=True, text=True, check=True, timeout=20)
node_result = subprocess.run(['kubectl', 'get', 'nodes', '-o', 'json'], capture_output=True, text=True, check=True, timeout=10)
nodes = json.loads(node_result.stdout)['items']
node_addresses = {entry['address'] for node in nodes for entry in node['status']['addresses'] if entry['type'] == 'InternalIP'}
bridges = {str(ipaddress.ip_network(node['spec']['podCIDR'])[1]) for node in nodes if node['spec'].get('podCIDR')}
rows = []
for line in result.stdout.splitlines():
    if marker.search(line) is None:
        continue
    peer = line.split()[0]
    try:
        address = ipaddress.ip_address(peer)
    except ValueError:
        continue
    rows.append({'peerMatchesTrustedSshOperator': peer == expected, 'peerIsPrivate': address.is_private,
                 'peerMatchesNodeInternalAddress': peer in node_addresses, 'peerMatchesNodeCniBridge': peer in bridges,
                 'peerSha256': hashlib.sha256(peer.encode()).hexdigest()})
if not rows:
    raise ValueError('No matching preflight log line observed; a timeout/missing sample does not prove source preservation.')
print(json.dumps({'probeRunId': args.probe_run_id, 'matchingPreflightRequests': len(rows), 'observedPeers': rows,
                  'rawAddressesOrHeadersExported': False}))
