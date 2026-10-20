"""Create a new immutable UAT package from an accepted paired Unity build."""
import argparse
import gzip
import hashlib
import json
import mimetypes
from pathlib import Path
import re
import shutil

parser = argparse.ArgumentParser()
parser.add_argument('--run-id', required=True)
args = parser.parse_args()
if not re.fullmatch('[a-f0-9]{32}', args.run_id):
    raise SystemExit('Invalid build run ID')
root = Path(__file__).resolve().parents[3]
build = root / 'Builds/TTB/UatWorld' / args.run_id
report = json.loads((build / 'result.json').read_text(encoding='utf-8-sig'))
assert report['status'] == 'passed' and report['environment'] == 'Uat'
for entry in report['artifacts']:
    assert hashlib.sha256((build / entry['path']).read_bytes()).hexdigest() == entry['sha256'], entry['path']
destination = root / '.artifacts/uat-world' / args.run_id / 'package'
destination.mkdir(parents=True, exist_ok=False)
for folder in ('Server', 'Client'):
    shutil.copytree(build / folder, destination / folder)
config = (destination / 'Client/ttb-commerce-config.js').read_text(encoding='utf-8-sig')
assert '"environment":"Uat"' in config and 'https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1' in config
assert '127.0.0.1' not in config and '"Local"' not in config and 'mockSimulation' not in config
for file in (destination / 'Client/Build').iterdir():
    if file.suffix in ('.data', '.wasm', '.js'):
        file.with_name(file.name + '.gz').write_bytes(gzip.compress(file.read_bytes(), mtime=0))
client = destination / 'Client'
public_files = []
mimetypes.add_type('application/wasm', '.wasm')
mimetypes.add_type('application/javascript', '.js')
mimetypes.add_type('application/manifest+json', '.webmanifest')
for file in sorted(client.rglob('*')):
    if not file.is_file():
        continue
    relative = file.relative_to(client).as_posix()
    if file.is_symlink() or any(part.startswith('.') for part in file.relative_to(client).parts):
        raise ValueError('Unexpected hidden or symbolic public artifact')
    content = file.read_bytes()
    public_files.append({'path': relative, 'bytes': len(content), 'sha256': hashlib.sha256(content).hexdigest(),
                         'contentType': mimetypes.guess_type(relative.removesuffix('.gz'))[0] or 'application/octet-stream'})
(client / 'public-files.json').write_text(json.dumps({'files': public_files}, indent=2), encoding='utf-8')
shutil.copyfile(Path(__file__).with_name('world-web.py'), destination / 'world-web.py')
files = []
for file in sorted(destination.rglob('*')):
    if file.is_file():
        content = file.read_bytes()
        files.append({'path': file.relative_to(destination).as_posix(), 'bytes': len(content), 'sha256': hashlib.sha256(content).hexdigest()})
package = {'runId': args.run_id, 'environment': 'Uat', 'sourceBuildResultSha256': hashlib.sha256((build / 'result.json').read_bytes()).hexdigest(),
           'sceneFingerprint': report['sceneFingerprint'], 'files': files, 'bankAccepted': False, 'publicRouteCreated': False}
(destination / 'package.json').write_text(json.dumps(package, indent=2), encoding='utf-8')
print(json.dumps({'package': str(destination), 'files': len(files), 'bytes': sum(x['bytes'] for x in files), 'environment': 'Uat'}))
