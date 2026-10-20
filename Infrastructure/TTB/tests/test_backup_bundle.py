import hashlib
import io
import json
import pathlib
import subprocess
import sys
import tarfile
import unittest
import uuid

REPO = pathlib.Path(__file__).resolve().parents[3]
ROOT = REPO / '.artifacts' / 'ttb-recovery'
VERIFY = REPO / 'Infrastructure' / 'TTB' / 'uat' / 'verify-backup-bundle.py'
FINGERPRINT = 'a' * 64

class BackupBundleTests(unittest.TestCase):
    def fixture(self, change=None, members=None, environment='Uat'):
        root = ROOT / ('bundle-test-' + uuid.uuid4().hex)
        root.mkdir(parents=True)
        private = 'private/finance/' + 'b' * 32 + '.proof'
        contents = {
            'database.dump': b'PGDMP\x00Synthetic structural-test fixture; not a restorable database',
            'database.contents': b'Synthetic TOC',
            'manifest.txt': f'format=1\nenvironment=Uat\nminimum_reader_contract=2\nrecipient_sha256={FINGERPRINT}\n'.encode(),
            private: b'Synthetic private proof; no payment or bank data',
        }
        contents['bundle-sha256.txt'] = ''.join(hashlib.sha256(contents[x]).hexdigest() + '  ' + x + '\n' for x in ['database.dump','database.contents','manifest.txt']).encode()
        contents['private-sha256.txt'] = (hashlib.sha256(contents[private]).hexdigest() + '  ' + private + '\n').encode()
        if change:
            change(contents)
        with tarfile.open(root / 'bundle.tar.gz', 'w:gz') as archive:
            for name, data in contents.items():
                item = tarfile.TarInfo(name); item.size = len(data); archive.addfile(item, io.BytesIO(data))
            if members:
                for item in members:
                    archive.addfile(item, io.BytesIO(b'x') if item.size else None)
        result = subprocess.run([sys.executable, str(VERIFY), str(root), '--environment', environment, '--recipient-sha256', FINGERPRINT], capture_output=True, text=True)
        return root, result

    def test_valid_bundle_checks_private_hashes_but_never_claims_database_restore(self):
        root, result = self.fixture(); self.assertEqual(0, result.returncode, result.stderr)
        proof = json.loads((root / 'verification.json').read_text())
        self.assertEqual(4, proof['verifiedFiles']); self.assertFalse(proof['databaseRestored'])

    def test_tampered_private_data_is_rejected(self):
        _, result = self.fixture(lambda files: files.update({'private/finance/' + 'b' * 32 + '.proof': b'Changed'}))
        self.assertNotEqual(0, result.returncode); self.assertIn('hash mismatch', result.stderr)

    def test_environment_mismatch_is_rejected(self):
        _, result = self.fixture(environment='Local'); self.assertNotEqual(0, result.returncode); self.assertIn('environment/format mismatch', result.stderr)

    def test_unsupported_reader_is_rejected(self):
        def change(files):
            files['manifest.txt'] = files['manifest.txt'].replace(b'contract=2', b'contract=99')
        _, result = self.fixture(change); self.assertNotEqual(0, result.returncode); self.assertIn('reader/recipient mismatch', result.stderr)

    def test_missing_hashes_are_rejected(self):
        _, result = self.fixture(lambda files: files.update({'bundle-sha256.txt': b''}))
        self.assertNotEqual(0, result.returncode); self.assertIn('Missing mandatory', result.stderr)

    def test_parent_traversal_absolute_paths_links_and_unexpected_private_paths_are_rejected_before_extraction(self):
        for name in ['../escape.txt', '/escape.txt', 'private/finance/../../escape.txt', 'private/finance/name.txt:secret', 'private/finance/unbounded.exe']:
            with self.subTest(name=name):
                item = tarfile.TarInfo(name); item.size = 1; root, result = self.fixture(members=[item])
                self.assertNotEqual(0, result.returncode); self.assertFalse((root / 'verified').exists())
        item = tarfile.TarInfo('private/finance/link.proof'); item.type = tarfile.SYMTYPE; item.linkname = '../../outside'
        root, result = self.fixture(members=[item]); self.assertNotEqual(0, result.returncode); self.assertFalse((root / 'verified').exists())

    def test_duplicate_archive_members_are_rejected(self):
        item = tarfile.TarInfo('manifest.txt'); item.size = 1
        root, result = self.fixture(members=[item]); self.assertNotEqual(0, result.returncode); self.assertFalse((root / 'verified').exists())

if __name__ == '__main__':
    unittest.main()
