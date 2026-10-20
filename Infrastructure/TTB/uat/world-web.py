"""Serve only hash-verified public WebGL artifacts; no API, directory listing or uploads."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlsplit


def load_files(root):
    files = {}
    for entry in json.loads((root / 'public-files.json').read_text())['files']:
        name = entry['path']
        parts = PurePosixPath(name).parts
        if not parts or name.startswith('/') or '\\' in name or any(x in ('.', '..') or x.startswith('.') for x in parts):
            raise ValueError('Invalid public artifact name')
        path = root / name
        if path.is_symlink() or not path.is_file() or root not in path.resolve().parents:
            raise ValueError('Public artifact escaped its root')
        if path.stat().st_size != entry['bytes'] or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
            raise ValueError('Public artifact hash mismatch')
        files[name] = entry
    if 'index.html' not in files or 'ttb-commerce-config.js' not in files:
        raise ValueError('Missing public UAT shell')
    return files


def accepts_gzip(value):
    for part in value.split(','):
        pieces = part.strip().lower().split(';')
        if pieces[0].strip() != 'gzip':
            continue
        quality = 1.0
        try:
            for piece in pieces[1:]:
                key, val = piece.strip().split('=', 1)
                if key == 'q':
                    quality = float(val)
        except ValueError:
            return False
        return 0 < quality <= 1
    return False


def public_name(raw_path):
    path = unquote(urlsplit(raw_path).path)
    prefix = '/store-uat/'
    if not path.startswith(prefix) or '\\' in path or '\x00' in path:
        return None
    name = path[len(prefix):] or 'index.html'
    if any(x in ('', '.', '..') or x.startswith('.') for x in name.split('/')):
        return None
    return name


class PublicHandler(BaseHTTPRequestHandler):
    root = None
    files = None

    def do_GET(self):
        self.serve(False)

    def do_HEAD(self):
        self.serve(True)

    def serve(self, head):
        if self.path == '/health/live':
            content = b'{"status":"live","environment":"Uat","paymentsHosted":false}'
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(content)))
            self.send_header('Cache-Control', 'no-store')
            self.end_headers()
            if not head:
                self.wfile.write(content)
            return
        name = public_name(self.path)
        entry = self.files.get(name)
        if entry is None:
            self.send_error(404)
            return
        compressed = name + '.gz'
        gzip = accepts_gzip(self.headers.get('Accept-Encoding', '')) and compressed in self.files
        actual = compressed if gzip else name
        entry = self.files[actual]
        self.send_response(200)
        self.send_header('Content-Type', self.files[name]['contentType'])
        self.send_header('Content-Length', str(entry['bytes']))
        self.send_header('ETag', '"' + entry['sha256'] + '"')
        self.send_header('Vary', 'Accept-Encoding')
        self.send_header('Cache-Control', 'no-cache')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'same-origin')
        self.send_header('X-Frame-Options', 'SAMEORIGIN')
        if gzip:
            self.send_header('Content-Encoding', 'gzip')
        if name == 'mango-ttb-sw.js':
            self.send_header('Service-Worker-Allowed', '/store-uat/')
        self.end_headers()
        if not head:
            try:
                with (self.root / actual).open('rb') as file:
                    shutil.copyfileobj(file, self.wfile)
            except (BrokenPipeError, ConnectionResetError):
                pass

    def log_message(self, format, *args):
        # No query strings, headers, cookies or browser identity in access logs.
        pass


if __name__ == '__main__':
    PublicHandler.root = Path(os.environ.get('TTB_WEB_ROOT', '/web')).resolve(strict=True)
    PublicHandler.files = load_files(PublicHandler.root)
    print(json.dumps({'status': 'uat-public-files-verified', 'files': len(PublicHandler.files)}), flush=True)
    ThreadingHTTPServer(('0.0.0.0', 8080), PublicHandler).serve_forever()
