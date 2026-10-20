import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, readdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { brotliDecompressSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { runInNewContext } from 'node:vm';
import { optimizeWebgl } from '../optimize-webgl.mjs';
const repository = resolve(import.meta.dirname, '../../..');

test('native package loads byte-identical content at immutable hash URLs and excludes private requests', async () => {
  await mkdir(resolve(repository, '.artifacts'), { recursive: true });
  const root = await mkdtemp(resolve(repository, '.artifacts/webgl-policy-'));
  await mkdir(resolve(root, 'Build'));
  const originals = { 'a.data': Buffer.from('UnityWebData1.0\0' + 'asset data'.repeat(1000)),
    'a.wasm': Buffer.from([0, 97, 115, 109, 1, 0, 0, 0]),
    'a.framework.js': Buffer.from('window.nativeFixture=true;'), 'a.loader.js': Buffer.from('window.loaderFixture=true;') };
  for (const [name, bytes] of Object.entries(originals)) await writeFile(resolve(root, 'Build', name), bytes);
  await writeFile(resolve(root, 'index.html'), `<script>var config={dataUrl:'Build/a.data',codeUrl:'Build/a.wasm',frameworkUrl:'Build/a.framework.js'};var loader={};loader.src='Build/a.loader.js';</script>`);
  const first = await optimizeWebgl(root);
  const html = await readFile(resolve(root, 'index.html'), 'utf8');
  const script = html.replace('<script>', '').replace('</script>', '');
  const context = {}; runInNewContext(script, context);
  const before = await readdir(resolve(root, 'Build'));
  for (const file of first.files) {
    const bytes = await readFile(resolve(root, file.path));
    const decoded = file.path.endsWith('.br') ? brotliDecompressSync(bytes) : bytes;
    assert.equal(createHash('sha256').update(decoded).digest('hex'), file.sha256);
    assert.ok(Object.values(originals).some(original => original.equals(decoded)));
    assert.ok(html.includes(file.path));
    assert.equal(context.config.cacheControl('https://shop.example/world/' + file.path), file.role === 'dataUrl' ? 'immutable' : 'no-store');
  }
  for (const path of ['/orders', '/session', '/inventory/file', '/Build/a.wasm', 'https://api.example/v1/receipt'])
    assert.equal(context.config.cacheControl(path), 'no-store');
  assert.equal(context.config.cacheControl(context.config.dataUrl + '?token=private'), 'no-store');
  await optimizeWebgl(root);
  assert.equal(await readFile(resolve(root, 'index.html'), 'utf8'), html);
  assert.deepEqual(await readdir(resolve(root, 'Build')), before);
  assert.equal(before.length, 4);
  assert.ok(first.transferBytes < first.decodedBytes);
});

test('unknown partial native templates fail before removing any compiled output', async () => {
  const root = await mkdtemp(resolve(repository, '.artifacts/webgl-invalid-'));
  await mkdir(resolve(root, 'Build'));
  await writeFile(resolve(root, 'Build/a.data'), 'keep native output');
  await writeFile(resolve(root, 'index.html'), `var config={dataUrl:'Build/a.data'};`);
  await assert.rejects(() => optimizeWebgl(root), /Expected one/);
  assert.equal(await readFile(resolve(root, 'Build/a.data'), 'utf8'), 'keep native output');
});
