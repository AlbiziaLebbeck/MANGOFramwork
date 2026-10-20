"""Compare PostgreSQL text data exports without printing any row/financial content."""
import argparse
import hashlib
import json
import pathlib
import re

IDENT = r'(?:"[A-Za-z_][A-Za-z0-9_]*"|[a-z_][a-z0-9_]*)'
COPY = re.compile(r'^COPY (?P<table>' + IDENT + r'\.' + IDENT + r') \((?P<columns>.+)\) FROM stdin;$')
ROOT = pathlib.Path(__file__).resolve().parents[3] / '.artifacts' / 'ttb-recovery'
REQUIRED = {'ttb.Orders', 'ttb.BankTransactions', 'ttb.Allocations', 'ttb.Jobs'}

def inspect(path):
    tables = {}
    sequences = []
    current = None
    rows = []
    columns = ''
    with path.open('rb') as source:
        for raw in source:
            line = raw.rstrip(b'\r\n')
            if current is not None:
                if line == b'\\.':
                    digest = hashlib.sha256(columns.encode('ascii') + b'\0')
                    for item in sorted(rows):
                        digest.update(item)
                    tables[current] = {'rows': len(rows), 'sha256': digest.hexdigest()}
                    current = None
                else:
                    if len(line) > 128 * 1024**2 or len(rows) >= 10_000_000:
                        raise ValueError('Data export exceeds the bounded comparison budget.')
                    rows.append(hashlib.sha256(line).digest())
                continue
            if line.startswith(b'COPY '):
                match = COPY.fullmatch(line.decode('ascii'))
                if not match:
                    raise ValueError('Unsupported COPY header; no data block may be silently skipped.')
                current = match['table'].replace('"', '')
                if current in tables:
                    raise ValueError('Duplicate table data block.')
                columns = match['columns']
                rows = []
            elif line.startswith(b'SELECT pg_catalog.setval('):
                sequences.append(hashlib.sha256(line).digest())
    if current is not None:
        raise ValueError('Unterminated COPY data block.')
    if not REQUIRED.issubset(tables):
        raise ValueError('Required financial/work tables are absent from the data export.')
    sequence_hash = hashlib.sha256(b''.join(sorted(sequences))).hexdigest()
    return {'tables': tables, 'sequenceStateSha256': sequence_hash}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=pathlib.Path)
    parser.add_argument('restored', type=pathlib.Path)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    args = parser.parse_args()
    for path in (args.source, args.restored, args.output):
        if not path.resolve().is_relative_to(ROOT.resolve()):
            raise ValueError('Data comparison must stay inside the restricted workspace recovery root.')
    if args.output.exists() or args.output.suffix != '.json':
        raise ValueError('Use a fresh JSON comparison artifact; existing recovery files are never overwritten.')
    original, restored = inspect(args.source), inspect(args.restored)
    result = {'dataMatch': original == restored, 'tableCount': len(original['tables']), 'source': original, 'restored': restored}
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    if not result['dataMatch']:
        raise ValueError('Restored data/columns/sequence state differs. No worker or mutation may resume.')
    print(json.dumps({'dataMatch': True, 'tableCount': result['tableCount'], 'comparedRows': sum(x['rows'] for x in original['tables'].values()), 'rowContentPrinted': False}))

if __name__ == '__main__':
    main()
