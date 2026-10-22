"""Apply a hash-bound API delta to a new immutable release; never edit the live directory."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tarfile

def kubectl(*args, payload=None):
    result = subprocess.run(['kubectl', '-n', 'ttb-uat', *args], input=json.dumps(payload) if payload is not None else None,
                            capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError('Dashboard release operation failed; inspect the current run before retrying.')
    return result.stdout

def digest(path):
    with path.open('rb') as source: return hashlib.file_digest(source, 'sha256').hexdigest()

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--release', required=True);args=parser.parse_args()
    if not re.fullmatch('[a-z0-9][a-z0-9-]{2,40}', args.release): raise ValueError('Invalid release id')
    root=Path('/opt/ttb-commerce/uat/releases');target=root/args.release
    if int(Path('/opt/ttb-commerce/uat/minimum-reader-contract').read_text().strip())>4:
        raise RuntimeError('The reader floor has advanced; this release is no longer compatible')
    manifest=json.loads((target/'delta-manifest.json').read_text())
    if manifest['releaseId'] != args.release or manifest['readerContract'] != 4 or not re.fullmatch('[a-z0-9][a-z0-9-]{2,40}',manifest['baseRelease']):
        raise ValueError('Invalid release manifest')
    base=root/manifest['baseRelease']/'api'
    deployment=json.loads(kubectl('get','deployment','ttb-commerce-api','-o','json'))
    spec=deployment['spec']['template']['spec'];index=next(i for i,v in enumerate(spec['volumes']) if v['name']=='app')
    if spec['volumes'][index]['hostPath']['path'] != str(base): raise RuntimeError('The live API changed; inspect before updating')
    if (target/'api').exists(): raise RuntimeError('The target release already exists; inspect rather than overwrite')
    if digest(target/'delta.tar.gz') != manifest['deltaSha256']: raise ValueError('Delta checksum mismatch')
    expected={entry['path']:entry for entry in manifest['files']}
    for path in expected:
        pure=PurePosixPath(path)
        if pure.is_absolute() or '..' in pure.parts or '\\' in path: raise ValueError('Invalid inventory path')
    actual={p.relative_to(base).as_posix() for p in base.rglob('*') if p.is_file()}
    if actual != set(expected) or any(p.is_symlink() for p in base.rglob('*')): raise ValueError('Base release inventory differs; full transfer required')
    shutil.copytree(base,target/'api')
    with tarfile.open(target/'delta.tar.gz') as archive:
        members=archive.getmembers()
        if {m.name for m in members} != set(manifest['deltaFiles']) or sum(m.size for m in members)>16*1024*1024:
            raise ValueError('Unexpected delta members')
        for member in members:
            if not member.isfile() or member.name not in expected: raise ValueError('Unsafe delta member')
        archive.extractall(target/'api',filter='data')
    for path,entry in expected.items():
        file=target/'api'/path
        if file.stat().st_size != entry['bytes'] or digest(file) != entry['sha256']: raise ValueError('Final artifact differs from accepted CI')
        file.chmod(0o755 if path in ['TtbCommerce.Api','ReceiptProbe','.playwright/node/linux-x64/node'] else 0o644)
    patch=[{'op':'test','path':'/metadata/uid','value':deployment['metadata']['uid']},
           {'op':'test','path':'/metadata/resourceVersion','value':deployment['metadata']['resourceVersion']},
           {'op':'test','path':f'/spec/template/spec/volumes/{index}/hostPath/path','value':str(base)},
           {'op':'replace','path':f'/spec/template/spec/volumes/{index}/hostPath/path','value':str(target/'api')}]
    kubectl('patch','deployment','ttb-commerce-api','--type=json','--patch-file=/dev/stdin',payload=patch)
    evidence={'releaseId':args.release,'baseRelease':manifest['baseRelease'],'ciRunId':manifest['ciRunId'],
              'readerContract':4,'verifiedFiles':len(expected),'deltaSha256':manifest['deltaSha256'],'apiUid':deployment['metadata']['uid']}
    (target/'deployment-evidence.json').write_text(json.dumps(evidence,indent=2))
    print(json.dumps(evidence))

if __name__=='__main__':main()
