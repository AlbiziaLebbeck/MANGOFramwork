"""Safely extract a decrypted, authenticated TTB backup into a fresh restricted recovery directory."""
import argparse
import hashlib
import json
import pathlib
import re
import tarfile

REPO = pathlib.Path(__file__).resolve().parents[3]
ROOT = REPO / '.artifacts' / 'ttb-recovery'
TOP = {'database.dump', 'database.contents', 'manifest.txt', 'bundle-sha256.txt', 'private-sha256.txt'}
KEY = r'[a-z0-9_-]{1,100}'
FILE = re.compile(r'private/(?:assets/' + KEY + r'\.asset|documents/' + KEY + r'\.pdf|reports/' + KEY + r'\.pgp|finance/' + KEY + r'\.proof)')

def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=pathlib.Path)
    parser.add_argument('--environment', choices=['Uat', 'Local'], required=True)
    parser.add_argument('--recipient-sha256', required=True)
    args = parser.parse_args()
    root = args.directory.resolve()
    if not root.is_relative_to(ROOT.resolve()) or root == ROOT.resolve():
        raise ValueError('Recovery must stay inside the dedicated workspace recovery root.')
    archive = root / 'bundle.tar.gz'
    destination = root / 'verified'
    if destination.exists():
        raise ValueError('Existing recovery data is never overwritten.')
    with tarfile.open(archive, mode='r:gz') as source:
        members = source.getmembers()
        if len(members) > 100_000 or sum(x.size for x in members) > 20 * 1024**3:
            raise ValueError('Backup member/size budget exceeded.')
        names = set()
        for item in members:
            name = item.name.rstrip('/')
            if name in names or '\\' in name or ':' in name or item.size < 0:
                raise ValueError('Duplicate/unsafe archive member.')
            names.add(name)
            if item.isdir():
                if name not in {'private', 'private/assets', 'private/documents', 'private/reports', 'private/finance'}:
                    raise ValueError('Unexpected archive directory.')
            elif not item.isfile() or (name not in TOP and FILE.fullmatch(name) is None):
                raise ValueError('Unsafe/unexpected backup member; links and executable paths are forbidden.')
        if not TOP.issubset(names):
            raise ValueError('Incomplete backup bundle.')
        destination.mkdir(mode=0o700)
        source.extractall(destination, filter='data')
    manifest = {}
    for line in (destination / 'manifest.txt').read_text(encoding='utf-8').splitlines():
        key, separator, value = line.partition('=')
        if not separator or key in manifest:
            raise ValueError('Invalid manifest key.')
        manifest[key] = value
    if manifest.get('format') != '1' or manifest.get('environment') != args.environment:
        raise ValueError('Backup environment/format mismatch.')
    if manifest.get('minimum_reader_contract') not in {'2', '3', '4'} or manifest.get('recipient_sha256') != args.recipient_sha256:
        raise ValueError('Backup reader/recipient mismatch.')
    with (destination / 'database.dump').open('rb') as dump:
        if dump.read(5) != b'PGDMP':
            raise ValueError('The database archive is not a PostgreSQL custom dump.')
    checked = set()
    for index, allowed in [('bundle-sha256.txt', {'database.dump', 'database.contents', 'manifest.txt'}), ('private-sha256.txt', None)]:
        for line in (destination / index).read_text(encoding='utf-8').splitlines():
            expected, separator, name = line.partition('  ')
            if not separator or re.fullmatch(r'[a-f0-9]{64}', expected) is None or name in checked:
                raise ValueError('Invalid/duplicate hash record.')
            if (allowed is not None and name not in allowed) or (allowed is None and FILE.fullmatch(name) is None):
                raise ValueError('Unsafe hash reference.')
            if digest(destination / name) != expected:
                raise ValueError('Backup content hash mismatch.')
            checked.add(name)
    if not {'database.dump', 'database.contents', 'manifest.txt'}.issubset(checked):
        raise ValueError('Missing mandatory bundle hash.')
    result = {'environment': args.environment, 'readerContract': int(manifest['minimum_reader_contract']), 'verifiedFiles': len(checked), 'directory': str(destination), 'databaseRestored': False}
    (root / 'verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))

if __name__ == '__main__':
    main()
