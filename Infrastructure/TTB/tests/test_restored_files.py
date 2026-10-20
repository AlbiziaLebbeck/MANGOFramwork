import hashlib
import importlib.util
import pathlib
import unittest
import uuid

REPO = pathlib.Path(__file__).resolve().parents[3]
SCRIPT = REPO / 'Infrastructure' / 'TTB' / 'uat' / 'verify-restored-files.py'
spec = importlib.util.spec_from_file_location('private_verification', SCRIPT)
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)

class RestoredFileTests(unittest.TestCase):
    def fixture(self):
        root = REPO / '.artifacts' / 'ttb-recovery' / ('files-test-' + uuid.uuid4().hex)
        directory = root / 'verified' / 'private' / 'finance'; directory.mkdir(parents=True)
        data = b'Synthetic proof, no financial data'; key = 'a' * 32
        path = directory / (key + '.proof'); path.write_bytes(data)
        row = {'area': 'finance', 'key': key, 'extension': '.proof', 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)}
        return root, path, row

    def test_all_referenced_bytes_and_hashes_are_verified_and_shared_objects_are_not_double_counted(self):
        root, _, row = self.fixture(); result = module.verify(root, [row, row])
        self.assertEqual(1, result['verifiedReferencedFiles']); self.assertEqual(2, result['databaseReferenceRows']); self.assertFalse(result['contentsPrinted'])

    def test_missing_corrupt_and_wrong_length_objects_stop_restore_acceptance(self):
        for mode in ['missing', 'corrupt', 'length']:
            root, path, row = self.fixture()
            if mode == 'missing':
                path.unlink()
            elif mode == 'corrupt':
                data = bytearray(path.read_bytes()); data[0] ^= 1; path.write_bytes(data)
            else:
                row['bytes'] += 1
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                module.verify(root, [row])

    def test_foreign_areas_extensions_paths_and_incomplete_hashes_are_rejected(self):
        root, _, row = self.fixture()
        for change in [{'area': 'public'}, {'extension': '.exe'}, {'key': '../outside'}, {'sha256': ''}, {'bytes': None}]:
            with self.subTest(change=next(iter(change))), self.assertRaises(ValueError):
                module.verify(root, [{**row, **change}])

if __name__ == '__main__':
    unittest.main()
