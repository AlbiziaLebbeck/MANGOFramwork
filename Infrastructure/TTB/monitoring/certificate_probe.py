"""Public-certificate metadata and a fixed UAT TLS handshake. Never send HTTP/payment data."""
import hashlib
import pathlib
import re
import shutil
import socket
import ssl
import subprocess
import time

HOST = 'ttb-api-test.mangosgo.com'
ADDRESS = '209.15.122.115'

def public_certificate(path, expected_sha256=None):
    # Kubernetes Secret/ConfigMap projections use symlinks to versioned files within the same mounted volume.
    resolved = path.resolve()
    if not resolved.is_relative_to(path.parent.resolve()) or not resolved.is_file() or resolved.stat().st_size > 65536:
        raise ValueError('Expected a bounded ordinary public certificate file.')
    raw = resolved.read_bytes()
    match = re.search(rb'-----BEGIN CERTIFICATE-----\s+[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----', raw)
    if match is None:
        raise ValueError('A public certificate is required; no private key is parsed.')
    der = ssl.PEM_cert_to_DER_cert(match[0].decode('ascii'))
    fingerprint = hashlib.sha256(der).hexdigest()
    if expected_sha256 is not None and (re.fullmatch('[a-f0-9]{64}', expected_sha256) is None or fingerprint != expected_sha256):
        raise ValueError('Recovery recipient identity changed.')
    openssl = shutil.which('openssl') or 'C:/Program Files/Git/usr/bin/openssl.exe'
    result = subprocess.run([openssl, 'x509', '-inform', 'DER', '-noout', '-dates'], input=der,
                            capture_output=True, timeout=2, check=True)
    fields = dict(line.split('=', 1) for line in result.stdout.decode('ascii').splitlines())
    before, after = ssl.cert_time_to_seconds(fields['notBefore']), ssl.cert_time_to_seconds(fields['notAfter'])
    if before >= after:
        raise ValueError('Invalid certificate validity window.')
    return {'sha256': fingerprint, 'notBefore': before, 'notAfter': after}

def client_context():
    # Construct directly: create_default_context can honor SSLKEYLOGFILE. This observer never records TLS keys.
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_default_certs()
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.hostname_checks_common_name = False
    return context

def callback_values(certificate, now, expected=False, context_factory=client_context):
    values = {'ttb_callback_public_serving_expected': int(expected), 'ttb_callback_certificate_probe_success': 0,
              'probe_success': 0}
    try:
        issued = public_certificate(certificate)
        values.update(ttb_callback_certificate_probe_success=1,
                      ttb_callback_certificate_not_before_timestamp_seconds=issued['notBefore'],
                      ttb_callback_certificate_not_after_timestamp_seconds=issued['notAfter'])
    except (OSError, ValueError, KeyError, UnicodeError, subprocess.SubprocessError):
        return values
    try:
        context = context_factory()
        if context.verify_mode != ssl.CERT_REQUIRED or not context.check_hostname:
            raise ValueError('CA/hostname validation cannot be disabled.')
        started = time.monotonic()
        # Fixed IPv4/SNI, no arbitrary target or redirect. Bound connect+handshake together to six seconds.
        with socket.create_connection((ADDRESS, 443), timeout=3) as connection:
            connection.settimeout(max(.1, 6 - (time.monotonic() - started)))
            with context.wrap_socket(connection, server_hostname=HOST) as secured:
                if hashlib.sha256(secured.getpeercert(binary_form=True)).hexdigest() != issued['sha256']:
                    raise ValueError('The listener is not serving the UAT certificate currently issued to this environment.')
                expiry = ssl.cert_time_to_seconds(secured.getpeercert()['notAfter'])
                if not issued['notBefore'] <= now < expiry:
                    raise ValueError('Certificate is not currently valid.')
                values.update(probe_success=1, ttb_callback_tls_leaf_not_after_timestamp_seconds=expiry)
    except (OSError, ValueError, KeyError, UnicodeError):
        # Never expose exception text, peer certificates, request paths or secret identifiers as labels.
        pass
    return values

def render_values(values):
    return ''.join(f'# TYPE {name} gauge\n{name} {value}\n' for name, value in values.items())
