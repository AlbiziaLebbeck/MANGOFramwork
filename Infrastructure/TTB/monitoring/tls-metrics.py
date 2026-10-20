"""Private UAT certificate/serving observer; no API route, credentials or HTTP bank request."""
import http.server
import importlib.util
import os
import pathlib
import time
from certificate_probe import callback_values, render_values

if __name__ == '__main__':
    if os.environ.get('TTB_TLS_ENVIRONMENT') != 'Uat':
        raise ValueError('This observer is restricted to the named UAT profile.')
    expected = os.environ.get('TTB_CALLBACK_PUBLIC_SERVING_EXPECTED', 'false')
    if expected not in {'true', 'false'}:
        raise ValueError('Public-serving expectation must be explicit.')
    path = pathlib.Path('/observer/backup-metrics.py')
    spec = importlib.util.spec_from_file_location('private_metrics_http', path)
    private_http = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(private_http)
    certificate = pathlib.Path('/issued/tls.crt')
    handler = private_http.make_handler(certificate.parent, os.environ.get('Operations__MetricsToken', ''),
        renderer=lambda: render_values(callback_values(certificate, time.time(), expected == 'true')))
    with http.server.HTTPServer(('0.0.0.0', 8081), handler) as server:
        server.serve_forever()
