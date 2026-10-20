"""Private, aggregate-only observer of the UAT ciphertext pool. No SQL/Kubernetes/bank access."""
import datetime as dt
import hashlib
import hmac
import http.server
import os
import pathlib
import re
import shutil
import stat
import time
import subprocess
from certificate_probe import public_certificate

BUDGET = 20 * 1024**3

def render(root, now, expected=True, recipient=None, recipient_sha256=None):
    values = {'ttb_backup_scheduling_expected': int(expected), 'ttb_backup_snapshot_probe_success': 0}
    if recipient is not None:
        values['ttb_backup_recipient_probe_success'] = 0
        try:
            certificate = public_certificate(recipient, recipient_sha256)
            values.update(ttb_backup_recipient_probe_success=1,
                          ttb_backup_recipient_not_before_timestamp_seconds=certificate['notBefore'],
                          ttb_backup_recipient_not_after_timestamp_seconds=certificate['notAfter'])
        except (OSError, ValueError, KeyError, UnicodeError, subprocess.SubprocessError):
            pass  # Keep disk/freshness observable even if the recipient's public certificate is invalid.
    try:
        if root.is_symlink() or not root.is_dir():
            raise ValueError('Missing ordinary ciphertext pool.')
        used = root.stat().st_size
        count = 0
        with os.scandir(root) as entries:
            for index, entry in enumerate(entries):
                if index >= 100_000:
                    raise ValueError('Pool metadata scan budget exceeded.')
                metadata = entry.stat(follow_symlinks=False)
                if not stat.S_ISREG(metadata.st_mode):
                    raise ValueError('Unexpected link/directory in the flat ciphertext pool.')
                used += metadata.st_size
                if re.fullmatch(r'ttb-uat-[a-zA-Z0-9-]+\.tar\.gz\.p7m', entry.name):
                    count += 1
        marker = root / 'last-success'
        last = 0
        if marker.exists():
            # Open without following a replacement symlink; never open an archive or checksum/payload file.
            descriptor = os.open(marker, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0))
            with os.fdopen(descriptor, 'rb') as stream:
                raw = stream.read(33)
            if re.fullmatch(rb'\d{8}T\d{6}Z\n?', raw) is None:
                raise ValueError('Invalid bounded completion marker.')
            last = dt.datetime.strptime(raw.decode('ascii').strip(), '%Y%m%dT%H%M%SZ').replace(tzinfo=dt.timezone.utc).timestamp()
            if last > now + 60 or count == 0:
                raise ValueError('Inconsistent completion metadata.')
        capacity = shutil.disk_usage(root)
        if capacity.total <= 0 or not 0 <= capacity.free <= capacity.total:
            raise ValueError('Invalid filesystem capacity.')
        values.update(ttb_backup_snapshot_probe_success=1, ttb_backup_last_success_timestamp_seconds=last,
                      ttb_backup_pool_used_bytes=used, ttb_backup_pool_budget_bytes=BUDGET,
                      ttb_backup_completed_archives=count, ttb_backup_filesystem_size_bytes=capacity.total,
                      ttb_backup_filesystem_available_bytes=capacity.free)
    except (OSError, ValueError, OverflowError):
        # Never disguise a missing mount/bad marker as a successful empty/zero-sized pool.
        pass
    return ''.join(f'# TYPE {name} gauge\n{name} {value}\n' for name, value in values.items())

def make_handler(root, token, expected=True, renderer=None):
    if not 32 <= len(token) <= 512:
        raise ValueError('A separate aggregate collector credential is required.')
    token_hash = hashlib.sha256(token.encode('utf-8')).digest()
    if renderer is None:
        renderer = lambda: render(root, time.time(), expected)
    class Handler(http.server.BaseHTTPRequestHandler):
        timeout = 5
        def do_GET(self):
            status, body = 404, b''
            if self.path == '/health/live':
                status, body = 200, b'healthy\n'
            elif self.path == '/metrics':
                header = self.headers.get('Authorization', '')
                authorized = (len(header) <= 1024 and header.startswith('Bearer ') and
                              hmac.compare_digest(hashlib.sha256(header[7:].encode('utf-8')).digest(), token_hash))
                if authorized:
                    status, body = 200, renderer().encode('ascii')
                else:
                    status = 401
            self.send_response(status)
            self.send_header('Content-Type', 'text/plain; version=0.0.4; charset=utf-8')
            self.send_header('Content-Length', str(len(body)))
            self.send_header('Cache-Control', 'no-store')
            self.end_headers()
            self.wfile.write(body)
        def log_message(self, *args):
            pass  # Do not log credentials, request paths or filesystem names.
    return Handler

if __name__ == '__main__':
    if os.environ.get('TTB_BACKUP_ENVIRONMENT') != 'Uat':
        raise ValueError('This observer is UAT-only.')
    recipient_sha = os.environ.get('TTB_RECIPIENT_SHA256', '')
    if re.fullmatch('[a-f0-9]{64}', recipient_sha) is None:
        raise ValueError('A pinned public recovery recipient is required.')
    root = pathlib.Path('/backups')
    handler = make_handler(root, os.environ.get('Operations__MetricsToken', ''),
                           renderer=lambda: render(root, time.time(), recipient=pathlib.Path('/recipient/recipient.pem'), recipient_sha256=recipient_sha))
    with http.server.HTTPServer(('0.0.0.0', 8081), handler) as server:
        server.serve_forever()
