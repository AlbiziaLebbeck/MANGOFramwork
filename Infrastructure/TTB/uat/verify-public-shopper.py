"""Read-only/anonymous checks of the public UAT shopper boundary; no bank calls."""
import json
import urllib.error
import urllib.request

BASE = 'https://ttb-api-test.mangosgo.com'
PREFIX = '/ttb-commerce-test/v1'
ORIGIN = 'https://plus.mangosgo.com'


def request(path, expected, *, method='GET', origin=ORIGIN, body=None, headers=None):
    values = {'Origin': origin, 'Accept': 'application/json', **(headers or {})}
    if body is not None:
        values['Content-Type'] = 'application/json'
    req = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                 headers=values, method=method)
    try:
        response = urllib.request.urlopen(req, timeout=25)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        data = response.read()
        statuses = expected if isinstance(expected, tuple) else (expected,)
        assert response.status in statuses, (method, path, response.status, expected)
        if path.startswith(PREFIX) and expected != 403:
            assert response.headers.get('Access-Control-Allow-Origin') == ORIGIN, path
            assert response.headers.get('Access-Control-Allow-Credentials') == 'true', path
        print(json.dumps({'method': method, 'path': path, 'status': response.status, 'passed': True}))
        return response.headers, data


def main():
    headers, data = request(PREFIX + '/storefront/config', 200)
    config = json.loads(data)
    assert config['environment'] == 'Uat' and config['paymentMode'] == 'Bank'
    assert config['mockSimulation'] is False
    assert headers.get('Cache-Control') == 'no-store'
    print(json.dumps({'environment': config['environment'], 'paymentsReady': config['paymentsReady']}))
    _, data = request(PREFIX + '/catalog/products?limit=24', 200)
    catalog = json.loads(data)
    assert len(catalog['items']) > 0
    print(json.dumps({'publicProductCount': len(catalog['items'])}))
    product = catalog['items'][0]
    request(PREFIX + '/catalog/products/' + product['id'], 200)
    _, data = request(PREFIX + '/session', 200)
    assert json.loads(data)['authenticated'] is False
    headers, _ = request(PREFIX + '/session', 204, method='OPTIONS', headers={
        'Access-Control-Request-Method': 'POST',
        'Access-Control-Request-Headers': 'content-type,x-csrf-token,idempotency-key',
    })
    allowed = headers.get('Access-Control-Allow-Headers', '').lower()
    assert all(name in allowed for name in ('content-type', 'x-csrf-token', 'idempotency-key'))
    for path in ['/cart', '/checkout/options', '/orders', '/inventory',
                 '/documents/00000000-0000-0000-0000-000000000001/download']:
        _, data = request(PREFIX + path, 401)
        assert json.loads(data)['code'] == 'login_required'
    request(PREFIX + '/session', 401, method='POST')
    _, data = request(PREFIX + '/session', 403, method='POST', origin='https://untrusted.invalid')
    assert json.loads(data)['code'] == 'untrusted_origin'
    req = urllib.request.Request(BASE + PREFIX + '/storefront/config', headers={'Origin': 'https://untrusted.invalid'})
    with urllib.request.urlopen(req, timeout=25) as response:
        assert response.status == 200 and response.headers.get('Access-Control-Allow-Origin') is None
    print(json.dumps({'untrustedCorsOriginRejected': True}))
    for path in ['/admin/catalog', '/game/entitlements/validate', '/session/local',
                 '/orders/../admin/catalog', '/catalog/%2e%2e/admin/catalog']:
        request(PREFIX + path, 403)
    request('/store-uat/', 403)
    request('/ttb-game-uat', 403)
    request('/health/live', 403)
    request('/ttb/v1/credit-notification', 403, method='POST', body={})
    # Depending on the host's default ingress, unmatched operator paths are
    # rejected with 403 or 404. Neither may reach the application endpoint.
    request('/internal/metrics', (403, 404))
    request('/health/ready', (403, 404))
    print(json.dumps({'passed': True, 'financialMutations': 0, 'bankRequests': 0}))


if __name__ == '__main__':
    main()
