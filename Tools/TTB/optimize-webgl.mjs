import { readFile, writeFile, lstat, realpath, unlink } from 'node:fs/promises';
import { resolve, sep } from 'node:path';
import { createHash } from 'node:crypto';
import { brotliCompressSync, brotliDecompressSync, constants } from 'node:zlib';

const repository = resolve(import.meta.dirname, '../..');
const cacheMarker = '/* mangos-runtime-cache */';
const cachePolicy = cacheMarker + 'cacheControl:function(url){return /(?:^|\\/)Build\\/[a-f0-9]{64}\\.data\\.br$/.test(url)?"immutable":/\\.bundle(?:[?#]|$)/.test(url)?"must-revalidate":"no-store"},';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

// Portal's existing static server serves .br with Content-Encoding: br and the underlying MIME.
// Keep this limited to public, compiled Unity files; API/private assets never enter this policy.
export async function optimizeWebgl(directory) {
  const root = await realpath(resolve(directory));
  if (!root.startsWith(repository + sep)) throw new Error('WebGL output must stay inside the workspace');
  const indexPath = resolve(root, 'index.html');
  let html = await readFile(indexPath, 'utf8');
  const pattern = /\b(dataUrl|codeUrl|frameworkUrl|loader\.src)\s*[:=]\s*(['"])(Build\/[A-Za-z0-9_.-]+)\2/g;
  const matches = [...html.matchAll(pattern)];
  if (!matches.length) return null; // Standalone HTML commerce has no native runtime.
  if (matches.length !== 4 || new Set(matches.map(m => m[1])).size !== 4)
    throw new Error('Expected one data, wasm, framework and loader URL in the TTB native template');
  if (!html.includes(cacheMarker) && !html.includes('var config={'))
    throw new Error('Unknown Unity configuration; runtime caching was not changed');
  const build = await realpath(resolve(root, 'Build'));
  if (build !== resolve(root, 'Build')) throw new Error('Linked Unity Build directories are not supported');
  const files = [];
  for (const match of matches) {
    const source = resolve(root, match[3]);
    if (!source.startsWith(build + sep) || (await lstat(source)).isSymbolicLink())
      throw new Error('Unity runtime path must be an ordinary file inside Build');
    const previous = await readFile(source);
    const encoded = match[1] !== 'loader.src';
    const decoded = match[3].endsWith('.br') ? brotliDecompressSync(previous) : previous;
    const extension = { dataUrl: '.data', codeUrl: '.wasm', frameworkUrl: '.framework.js', 'loader.src': '.loader.js' }[match[1]];
    const name = `Build/${hash(decoded)}${extension}${encoded ? '.br' : ''}`;
    const bytes = encoded ? brotliCompressSync(decoded, { params: { [constants.BROTLI_PARAM_QUALITY]: 6 } }) : decoded;
    if (encoded && !brotliDecompressSync(bytes).equals(decoded)) throw new Error('Unity compression integrity check failed');
    await writeFile(resolve(root, name), bytes);
    files.push({ role: match[1], original: match[3], path: name, decodedBytes: decoded.length, transferBytes: bytes.length, sha256: hash(decoded) });
    html = html.replace(match[0], match[0].replace(match[3], name));
  }
  if (!html.includes(cacheMarker)) html = html.replace('var config={', 'var config={' + cachePolicy);
  await writeFile(indexPath, html);
  // The new entry is durable before removing its now-unreferenced source files.
  for (const file of files) if (file.original !== file.path) await unlink(resolve(root, file.original));
  const report = { files: files.map(({ original, ...file }) => file),
    decodedBytes: files.reduce((n, f) => n + f.decodedBytes, 0), transferBytes: files.reduce((n, f) => n + f.transferBytes, 0) };
  await writeFile(resolve(root, 'webgl-transfer.json'), JSON.stringify(report, null, 2));
  return report;
}
