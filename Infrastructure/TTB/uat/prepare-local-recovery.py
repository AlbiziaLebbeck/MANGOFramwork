"""Package only the existing synthetic Local fixture; never connect to a database or write source files."""
import argparse
import hashlib
import json
import pathlib
import re
import shutil
import tarfile
import datetime as dt

REPO = pathlib.Path(__file__).resolve().parents[3]
SOURCE = REPO / '.tools/local/private-commerce'
RECOVERY = REPO / '.artifacts/ttb-recovery'
AREAS = {'assets': '.asset', 'documents': '.pdf', 'reports': '.pgp', 'finance': '.proof'}

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=pathlib.Path)
    parser.add_argument('--recipient-sha256', required=True)
    args = parser.parse_args()
    root = args.directory.resolve()
    if root.parent != RECOVERY.resolve() or not root.name.startswith('local-source-') or re.fullmatch('[a-f0-9]{64}', args.recipient_sha256) is None:
        raise ValueError('Use the specifically owned Local recovery fixture directory.')
    if (root / 'bundle.tar.gz').exists() or (root / 'private').exists() or not SOURCE.is_dir() or SOURCE.is_symlink():
        raise ValueError('Existing output is never overwritten and the Local source must exist.')
    with (root / 'database.dump').open('rb') as stream:
        if stream.read(5) != b'PGDMP':
            raise ValueError('Expected the already exported PostgreSQL custom dump.')
    (root / 'private').mkdir()
    entries, total = [], 0
    for area, extension in AREAS.items():
        origin = SOURCE / area
        target = root / 'private' / area
        target.mkdir()
        if not origin.exists():
            continue
        if not origin.is_dir() or origin.is_symlink():
            raise ValueError('Private fixture directory is not an ordinary directory.')
        for path in origin.iterdir():
            if path.suffix in {'.upload', '.tmp', '.part'}:
                continue
            if path.is_symlink() or not path.is_file() or re.fullmatch(r'[a-z0-9_-]{1,100}' + re.escape(extension), path.name) is None:
                raise ValueError('Unexpected private fixture entry; no links or unrecognized files are copied.')
            before = path.stat()
            total += before.st_size
            if total > 20 * 1024**3 or len(entries) >= 99_000:
                raise ValueError('Local fixture exceeds the recovery size/member budget.')
            destination = target / path.name
            shutil.copyfile(path, destination)
            after = path.stat()
            if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns or digest(destination) != digest(path):
                raise ValueError('An immutable private fixture changed during packaging; retry from a fresh dump/directory.')
            entries.append((destination.relative_to(root).as_posix(), digest(destination)))
    (root / 'private-sha256.txt').write_text(''.join(f'{value}  {name}\n' for name, value in sorted(entries)), encoding='utf-8')
    manifest = ('format=1\nenvironment=Local\ndatabase=ttb_local_commerce\nminimum_reader_contract=3\n'
                f'packaged_at={dt.datetime.now(dt.timezone.utc).isoformat()}\nrecipient_sha256={args.recipient_sha256}\n'
                'cipher=CMS-AES-256-GCM-RSA-OAEP-SHA256\nfixture=synthetic-local-only\n')
    (root / 'manifest.txt').write_text(manifest, encoding='utf-8')
    (root / 'bundle-sha256.txt').write_text(''.join(f'{digest(root / name)}  {name}\n' for name in ['database.dump', 'database.contents', 'manifest.txt']), encoding='utf-8')
    with tarfile.open(root / 'bundle.tar.gz', 'w:gz', format=tarfile.PAX_FORMAT) as bundle:
        for name in ['database.dump', 'database.contents', 'manifest.txt', 'bundle-sha256.txt', 'private-sha256.txt', 'private']:
            bundle.add(root / name, arcname=name)
    result = {'environment': 'Local', 'sourceUnchanged': True, 'privateFiles': len(entries), 'privateBytes': total, 'databaseRestored': False}
    (root / 'packaging.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))

if __name__ == '__main__':
    main()
