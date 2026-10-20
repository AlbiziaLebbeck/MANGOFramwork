import { createServer, request } from 'node:http';
import { createReadStream } from 'node:fs';
import { cp, mkdir, mkdtemp, readFile, writeFile, stat } from 'node:fs/promises';
import { resolve, sep, extname } from 'node:path';
import { packagePwa } from '../package-pwa.mjs';
const repository = resolve(import.meta.dirname, '../../..');
const latest = JSON.parse(await readFile(resolve(repository, 'Builds/last-successful.json'), 'utf8'));
const source = resolve(latest.output, 'Client');
if (!source.startsWith(resolve(repository, 'Builds') + sep)) throw new Error('Invalid compiled client');
await mkdir(resolve(repository, '.artifacts'), { recursive: true });
const root = await mkdtemp(resolve(repository, '.artifacts/webgl-native-qa-'));
await cp(source, root, { recursive: true });
await cp(resolve(repository, 'WebCommerce/dist'), resolve(root, 'commerce'), { recursive: true, force: true });
await writeFile(resolve(root, 'ttb-commerce-config.js'), 'window.MANGOS_TTB_CONFIG={pwaEnabled:true,environment:"Local",experience:"world",apiBase:"/ttb-commerce/v1",assetBase:"./commerce/"};');
await packagePwa(root);
console.log('Optimized exact compiled Unity client: ' + JSON.stringify(JSON.parse(await readFile(resolve(root, 'webgl-transfer.json'), 'utf8'))));
const types = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.webmanifest': 'application/manifest+json', '.png': 'image/png', '.svg': 'image/svg+xml', '.ttf': 'font/ttf' };
createServer(async (req, res) => {
  if (req.url.startsWith('/ttb-commerce/v1/')) {
    const upstream = request({ hostname: '127.0.0.1', port: 5286, path: req.url, method: req.method, headers: { ...req.headers, host: '127.0.0.1:5286' } }, response => { res.writeHead(response.statusCode ?? 502, response.headers); response.pipe(res); });
    upstream.on('error', () => { if (!res.headersSent) res.writeHead(502); res.end(); }); req.pipe(upstream); return;
  }
  try {
    const pathname = decodeURIComponent(new URL(req.url, 'http://127.0.0.1:5198').pathname);
    let path = resolve(root, '.' + pathname);
    if (path !== root && !path.startsWith(root + sep)) { res.writeHead(403).end(); return; }
    if ((await stat(path)).isDirectory()) path = resolve(path, 'index.html');
    const encoding = path.endsWith('.br') ? 'br' : null;
    const decoded = encoding ? path.slice(0, -3) : path;
    // Same Content-Encoding and MIME handling as ntserver02's existing Portal static server.
    res.writeHead(200, { 'Content-Type': types[extname(decoded)] ?? 'application/octet-stream', ...(encoding ? { 'Content-Encoding': encoding } : {}),
      'Content-Length': (await stat(path)).size, 'Cache-Control': path.endsWith('index.html') ? 'no-store' : 'public, max-age=3600' });
    if (req.method === 'HEAD') res.end(); else createReadStream(path).pipe(res);
  } catch { res.writeHead(404).end(); }
}).listen(5198, '127.0.0.1', () => console.log('Native transfer QA ready on loopback 5198'));
