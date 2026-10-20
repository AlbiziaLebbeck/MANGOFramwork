"""Create explicitly synthetic structural data for the local CMS round-trip test, never bank data."""
import importlib.util
import json
import pathlib
import sys

here = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('backup_fixture_tests', here / 'test_backup_bundle.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
metadata = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
module.FINGERPRINT = metadata['recipientSha256']
root, result = module.BackupBundleTests().fixture()
if result.returncode:
    raise RuntimeError(result.stderr)
print(root)
