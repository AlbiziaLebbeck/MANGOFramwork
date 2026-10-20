import importlib.util
import pathlib
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
import http.server
import sys

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / 'monitoring/backup-metrics.py'
sys.path.insert(0, str(SCRIPT.parent))
SPEC = importlib.util.spec_from_file_location('backup_metrics', SCRIPT)
observer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(observer)

class BackupMetricsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        self.now = 1792393200  # 2026-10-19, UTC; independent of local calendar/timezone.

    def tearDown(self):
        self.temp.cleanup()

    def test_missing_mount_is_not_created_and_no_fabricated_capacity_is_emitted(self):
        root = self.root / 'missing'
        result = observer.render(root, self.now)
        self.assertIn('ttb_backup_snapshot_probe_success 0', result)
        self.assertNotIn('ttb_backup_filesystem_size_bytes', result)
        self.assertNotIn('ttb_backup_last_success_timestamp_seconds', result)
        self.assertFalse(root.exists())

    def test_empty_existing_pool_is_observed_but_never_claims_a_successful_backup(self):
        result = observer.render(self.root, self.now)
        self.assertIn('ttb_backup_snapshot_probe_success 1', result)
        self.assertIn('ttb_backup_completed_archives 0', result)
        self.assertIn('ttb_backup_last_success_timestamp_seconds 0', result)
        self.assertIn('ttb_backup_pool_budget_bytes 21474836480', result)

    def test_final_metadata_exports_only_aggregate_bytes_and_utc_time_without_reading_ciphertext(self):
        name = 'ttb-uat-private-fixture-identity.tar.gz.p7m'
        (self.root / name).write_bytes(b'private-ciphertext-never-export')
        (self.root / 'last-success').write_bytes(b'20261019T063958Z\n')
        before = (self.root / name).read_bytes()
        result = observer.render(self.root, self.now)
        self.assertIn('ttb_backup_snapshot_probe_success 1', result)
        self.assertIn('ttb_backup_completed_archives 1', result)
        self.assertIn('ttb_backup_last_success_timestamp_seconds 1792391998.0', result)
        self.assertNotIn(name, result)
        self.assertNotIn('private-ciphertext', result)
        self.assertNotIn(str(self.root), result)
        self.assertEqual(before, (self.root / name).read_bytes())

    def test_bad_or_future_or_unbacked_completion_marker_fails_instead_of_claiming_freshness(self):
        for marker in [b'20291019T063958Z\n', b'20261019T063958Z\n', b'0' * 100, b'invalid\n']:
            with self.subTest(marker=marker):
                (self.root / 'last-success').write_bytes(marker)
                result = observer.render(self.root, self.now)
                self.assertIn('ttb_backup_snapshot_probe_success 0', result)
                self.assertNotIn('ttb_backup_last_success_timestamp_seconds', result)

    def test_unexpected_directory_cannot_hide_bytes_from_the_flat_pool_scan(self):
        (self.root / 'unexpected').mkdir()
        self.assertIn('ttb_backup_snapshot_probe_success 0', observer.render(self.root, self.now))

    def test_metrics_require_the_separate_token_while_liveness_has_no_sensitive_output(self):
        token = 'synthetic-metrics-token-' + 'a' * 40
        with http.server.HTTPServer(('127.0.0.1', 0), observer.make_handler(self.root, token)) as server:
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            base = f'http://127.0.0.1:{server.server_port}'
            try:
                self.assertEqual(urllib.request.urlopen(base + '/health/live').read(), b'healthy\n')
                for header in ['', 'Bearer wrong', 'Bearer ' + 'x' * 1025, 'Bearer \u00ff']:
                    with self.subTest(header=header):
                        request = urllib.request.Request(base + '/metrics', headers={'Authorization': header})
                        with self.assertRaises(urllib.error.HTTPError) as failure:
                            urllib.request.urlopen(request)
                        self.assertEqual(failure.exception.code, 401)
                request = urllib.request.Request(base + '/metrics', headers={'Authorization': 'Bearer ' + token})
                result = urllib.request.urlopen(request).read().decode('ascii')
                self.assertIn('ttb_backup_snapshot_probe_success 1', result)
                self.assertNotIn(token, result)
            finally:
                server.shutdown()
                thread.join(timeout=3)

    def test_short_collector_credential_is_rejected_at_startup(self):
        with self.assertRaises(ValueError):
            observer.make_handler(self.root, 'short')

if __name__ == '__main__':
    unittest.main()
