import hashlib
import importlib.util
import pathlib
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

DIRECTORY = pathlib.Path(__file__).resolve().parents[1] / 'monitoring'
sys.path.insert(0, str(DIRECTORY))
import certificate_probe as probe

class TlsListener:
    def __init__(self, certificate, key):
        self.listener = socket.socket()
        self.listener.bind(('127.0.0.1', 0))
        self.listener.listen(1)
        self.listener.settimeout(3)
        self.port = self.listener.getsockname()[1]
        self.received = b''
        self.context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        self.context.load_cert_chain(certificate, key)
        self.thread = threading.Thread(target=self.run, daemon=True)

    def run(self):
        try:
            raw, _ = self.listener.accept()
            with raw:
                raw.settimeout(2)
                with self.context.wrap_socket(raw, server_side=True) as secured:
                    self.received = secured.recv(1)
        except (OSError, ssl.SSLError):
            pass

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.thread.join(timeout=4)
        self.listener.close()
        if self.thread.is_alive():
            raise AssertionError('The bounded local TLS listener did not exit.')

class CertificateProbeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.root = pathlib.Path(cls.temp.name)
        openssl = shutil.which('openssl') or 'C:/Program Files/Git/usr/bin/openssl.exe'
        for name in ['issued', 'different', 'wrong-host']:
            hostname = 'wrong-host.invalid' if name == 'wrong-host' else probe.HOST
            subprocess.run([openssl, 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '2',
                            '-keyout', str(cls.root / (name + '.key')), '-out', str(cls.root / (name + '.pem')),
                            '-subj', '/CN=' + hostname, '-addext', 'subjectAltName=DNS:' + hostname],
                           capture_output=True, timeout=15, check=True)
        cls.certificate = cls.root / 'issued.pem'
        cls.key = cls.root / 'issued.key'
        cls.trust = cls.root / 'trust.pem'
        cls.trust.write_bytes(b''.join((cls.root / (name + '.pem')).read_bytes() for name in ['issued', 'different', 'wrong-host']))

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def trusted_context(self):
        return ssl.create_default_context(cafile=str(self.trust))

    def run_local_probe(self, certificate, key, context_factory):
        original = socket.create_connection
        with TlsListener(certificate, key) as server:
            def connect(address, timeout):
                self.assertEqual(address, (probe.ADDRESS, 443))
                self.assertLessEqual(timeout, 3)
                return original(('127.0.0.1', server.port), timeout=timeout)
            with patch.object(probe.socket, 'create_connection', side_effect=connect):
                result = probe.callback_values(self.certificate, time.time(), context_factory=context_factory)
        self.assertEqual(server.received, b'', 'A TLS observer must never send an HTTP/payment body.')
        return result

    def test_public_certificate_utc_window_and_pinned_der_identity_are_checked(self):
        result = probe.public_certificate(self.certificate)
        der = ssl.PEM_cert_to_DER_cert(self.certificate.read_text())
        self.assertEqual(result['sha256'], hashlib.sha256(der).hexdigest())
        self.assertLess(result['notBefore'], result['notAfter'])
        self.assertEqual(probe.public_certificate(self.certificate, result['sha256']), result)
        with self.assertRaises(ValueError):
            probe.public_certificate(self.certificate, '0' * 64)
        with self.assertRaises(ValueError):
            probe.public_certificate(self.key)

    def test_verified_matching_tls_handshake_succeeds_without_any_http_data(self):
        result = self.run_local_probe(self.certificate, self.key, self.trusted_context)
        self.assertEqual(result['probe_success'], 1)
        self.assertEqual(result['ttb_callback_certificate_probe_success'], 1)
        self.assertEqual(result['ttb_callback_public_serving_expected'], 0)
        self.assertGreater(result['ttb_callback_tls_leaf_not_after_timestamp_seconds'], time.time())

    def test_untrusted_self_signed_peer_cannot_claim_success_or_export_trusted_live_expiry(self):
        result = self.run_local_probe(self.certificate, self.key, probe.client_context)
        self.assertEqual(result['probe_success'], 0)
        self.assertEqual(result['ttb_callback_certificate_probe_success'], 1)
        self.assertNotIn('ttb_callback_tls_leaf_not_after_timestamp_seconds', result)

    def test_another_trusted_certificate_for_the_same_hostname_cannot_impersonate_the_issued_environment(self):
        result = self.run_local_probe(self.root / 'different.pem', self.root / 'different.key', self.trusted_context)
        self.assertEqual(result['probe_success'], 0)
        self.assertNotIn('ttb_callback_tls_leaf_not_after_timestamp_seconds', result)

    def test_trusting_the_issuer_does_not_bypass_the_exact_uat_hostname(self):
        result = self.run_local_probe(self.root / 'wrong-host.pem', self.root / 'wrong-host.key', self.trusted_context)
        self.assertEqual(result['probe_success'], 0)
        self.assertNotIn('ttb_callback_tls_leaf_not_after_timestamp_seconds', result)

    def test_missing_or_oversized_public_certificate_prevents_a_network_probe(self):
        oversized = self.root / 'oversized.pem'
        oversized.write_bytes(b'x' * 65537)
        with patch.object(probe.socket, 'create_connection') as connect:
            for path in [self.root / 'absent.pem', oversized, self.key]:
                with self.subTest(path=path.name):
                    result = probe.callback_values(path, time.time())
                    self.assertEqual(result['ttb_callback_certificate_probe_success'], 0)
                    self.assertEqual(result['probe_success'], 0)
            connect.assert_not_called()

    def test_timeout_remains_unconfirmed_and_does_not_publish_fake_expiry(self):
        with patch.object(probe.socket, 'create_connection', side_effect=TimeoutError):
            result = probe.callback_values(self.certificate, time.time())
        self.assertEqual(result['probe_success'], 0)
        self.assertNotIn('ttb_callback_tls_leaf_not_after_timestamp_seconds', result)

    def test_ca_and_hostname_validation_cannot_be_disabled_or_tls_keys_logged(self):
        insecure = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        insecure.check_hostname = False
        insecure.verify_mode = ssl.CERT_NONE
        with patch.object(probe.socket, 'create_connection') as connect:
            self.assertEqual(probe.callback_values(self.certificate, time.time(), context_factory=lambda: insecure)['probe_success'], 0)
            connect.assert_not_called()
        log = self.root / 'must-not-exist.log'
        with patch.dict('os.environ', {'SSLKEYLOGFILE': str(log)}):
            context = probe.client_context()
            self.assertIsNone(context.keylog_filename)
            self.assertTrue(context.check_hostname)
            self.assertEqual(context.verify_mode, ssl.CERT_REQUIRED)
            self.assertFalse(context.hostname_checks_common_name)
        self.assertFalse(log.exists())

    def test_backup_recipient_failure_keeps_capacity_signals_without_claiming_a_valid_key(self):
        spec = importlib.util.spec_from_file_location('certificate_backup_metrics', DIRECTORY / 'backup-metrics.py')
        backup = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(backup)
        pool = self.root / 'pool'
        pool.mkdir(exist_ok=True)
        valid = backup.render(pool, time.time(), recipient=self.certificate, recipient_sha256=probe.public_certificate(self.certificate)['sha256'])
        self.assertIn('ttb_backup_recipient_probe_success 1', valid)
        self.assertIn('ttb_backup_recipient_not_after_timestamp_seconds', valid)
        invalid = backup.render(pool, time.time(), recipient=self.certificate, recipient_sha256='0' * 64)
        self.assertIn('ttb_backup_recipient_probe_success 0', invalid)
        self.assertIn('ttb_backup_snapshot_probe_success 1', invalid)
        self.assertNotIn('ttb_backup_recipient_not_after_timestamp_seconds', invalid)

if __name__ == '__main__':
    unittest.main()
