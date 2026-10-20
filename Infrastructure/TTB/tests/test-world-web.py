import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('world_web', Path(__file__).resolve().parents[1] / 'uat/world-web.py')
web = importlib.util.module_from_spec(spec)
spec.loader.exec_module(web)


class PublicFilesTests(unittest.TestCase):
    def test_names_do_not_escape_public_prefix(self):
        self.assertEqual(web.public_name('/store-uat/'), 'index.html')
        self.assertEqual(web.public_name('/store-uat/Build/Client.wasm?v=1'), 'Build/Client.wasm')
        for path in ['/store-uat/../secret', '/store-uat/%2e%2e/secret', '/store-uat/.env', '/store-uat/Build//secret',
                     '/store-uat/Build%5csecret', '/store-uat/%00secret', '/ttb-commerce-test/v1', '/store-uatx/index.html']:
            self.assertIsNone(web.public_name(path), path)

    def test_gzip_quality_is_respected(self):
        for value in ['gzip', 'br, gzip;q=0.5', 'gzip; q=1']:
            self.assertTrue(web.accepts_gzip(value), value)
        for value in ['', 'br', 'gzip;q=0', 'gzip;q=garbage', 'gzip;q=2', 'gzip;q=-1']:
            self.assertFalse(web.accepts_gzip(value), value)

    def test_startup_requires_hashes_and_bounded_paths(self):
        workspace = Path(__file__).resolve().parents[3]
        scratch = workspace / '.artifacts/uat-world-web-tests'
        scratch.mkdir(parents=True, exist_ok=True)
        assert workspace in scratch.resolve().parents
        with tempfile.TemporaryDirectory(dir=scratch) as folder:
            root = Path(folder)
            entries = []
            for name in ['index.html', 'ttb-commerce-config.js']:
                (root / name).write_bytes(b'public')
                entries.append({'path': name, 'bytes': 6, 'sha256': hashlib.sha256(b'public').hexdigest()})
            manifest = root / 'public-files.json'
            manifest.write_text(json.dumps({'files': entries}))
            self.assertEqual(len(web.load_files(root)), 2)
            (root / 'index.html').write_bytes(b'tamper')
            with self.assertRaises(ValueError):
                web.load_files(root)
            entries[0]['path'] = '../index.html'
            manifest.write_text(json.dumps({'files': entries}))
            with self.assertRaises(ValueError):
                web.load_files(root)


if __name__ == '__main__':
    unittest.main()
