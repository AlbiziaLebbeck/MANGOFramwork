"""Run on ntserver02: confirm non-bank callers cannot spoof callback identity.

Sends an incomplete, explicitly tagged test body with no amount/order/reference.
Every probe must be rejected before processing. No bank API is called.
"""
import datetime as dt
import json
import subprocess
import urllib.error
import urllib.request

URL = 'https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification'
BODY = b'{"InstructionId":"UAT-SOURCE-BOUNDARY-PROBE"}'
BANK_IP = '203.148.143.101'
HEADERS = {'Content-Type': 'application/json; charset=UTF-8', 'Service-Name': 'credit-notification',
           'Request-Datetime': dt.datetime.now(dt.timezone.utc).isoformat(timespec='microseconds')}
results = []
for name, extra in (('public-non-bank', {}), ('public-forged-bank-headers',
                   {'X-Forwarded-For': BANK_IP, 'X-Real-IP': BANK_IP, 'Forwarded': 'for=' + BANK_IP})):
    request = urllib.request.Request(URL, method='POST', data=BODY, headers={**HEADERS, **extra})
    try:
        response = urllib.request.urlopen(request, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        assert response.status == 403, (name, response.status)
        results.append({'probe': name, 'httpStatus': response.status, 'passed': True})

# The API's own loopback caller is not the pinned ingress and cannot invent a bank peer.
arguments = ['kubectl', '-n', 'ttb-uat', 'exec', 'deployment/ttb-commerce-api', '--',
             'curl', '-sS', '--max-time', '10', '-i', '-X', 'POST',
             'http://127.0.0.1:8080/ttb/v1/credit-notification', '--data-binary', BODY.decode()]
for name, value in {**HEADERS, 'X-Forwarded-For': BANK_IP, 'X-Real-IP': BANK_IP}.items():
    arguments.extend(['-H', name + ': ' + value])
response = subprocess.run(arguments, text=True, capture_output=True, timeout=20)
assert response.returncode == 0
headers, body = response.stdout.replace('\r\n', '\n').split('\n\n', 1)
assert headers.splitlines()[0].startswith('HTTP/1.1 403'), headers.splitlines()[0]
assert 'www-authenticate:' not in headers.lower()
assert json.loads(body)['code'] == 'callback_source_rejected'
results.append({'probe': 'application-loopback-forged-bank-headers', 'httpStatus': 403,
                'code': 'callback_source_rejected', 'passed': True})
print(json.dumps({'passed': True, 'probes': results, 'bankApiCalls': 0, 'financialMutations': 0}))
