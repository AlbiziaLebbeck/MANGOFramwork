"""Verify every restored DB-referenced private object without exposing its contents."""
import argparse
import hashlib
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[3] / '.artifacts' / 'ttb-recovery'
EXTENSIONS = {'assets': '.asset', 'documents': '.pdf', 'reports': '.pgp', 'finance': '.proof'}

def verify(root, references):
    checked = {}
    for row in references:
        area, key, extension = row.get('area'), row.get('key'), row.get('extension')
        if area not in EXTENSIONS or extension != EXTENSIONS[area] or re.fullmatch(r'[a-z0-9_-]{1,100}', key or '') is None:
            raise ValueError('Unsafe restored private-file reference.')
        expected = row.get('sha256')
        if re.fullmatch(r'[a-f0-9]{64}', expected or '') is None or not isinstance(row.get('bytes'), int) or row['bytes'] <= 0:
            raise ValueError('Incomplete restored private-file identity/hash/size.')
        path = root / 'verified' / 'private' / area / (key + extension)
        if not path.resolve().is_relative_to((root / 'verified' / 'private').resolve()) or path.is_symlink() or not path.is_file():
            raise ValueError('A restored private object is missing or unsafe.')
        if path.stat().st_size != row['bytes']:
            raise ValueError('A restored private object has the wrong byte length.')
        with path.open('rb') as source:
            actual = hashlib.file_digest(source, 'sha256').hexdigest()
        if actual != expected:
            raise ValueError('A restored private object has the wrong content hash.')
        identity = str(path.relative_to(root))
        if identity in checked and checked[identity] != actual:
            raise ValueError('Conflicting restored file references.')
        checked[identity] = actual
    return {'verifiedReferencedFiles': len(checked), 'databaseReferenceRows': len(references), 'contentsPrinted': False}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=pathlib.Path)
    parser.add_argument('references', type=pathlib.Path)
    args = parser.parse_args()
    root = args.directory.resolve()
    if not root.is_relative_to(ROOT.resolve()) or not args.references.resolve().is_relative_to(root):
        raise ValueError('File verification must stay inside the dedicated recovery directory.')
    rows = json.loads(args.references.read_text(encoding='utf-8-sig'))
    if not isinstance(rows, list) or len(rows) > 100_000:
        raise ValueError('Invalid private reference list or size budget.')
    result = verify(root, rows)
    output = root / 'referenced-files-verification.json'
    if output.exists():
        raise ValueError('Existing verification evidence is never overwritten.')
    output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))

if __name__ == '__main__':
    main()
