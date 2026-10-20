import { spawnSync } from 'node:child_process';
import { cp, mkdir, readFile, writeFile, access } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const web = resolve(repository, 'WebCommerce');
const options = { environment: 'Local', experience: 'gallery' };
for (let i = 2; i < process.argv.length; i += 2) {
  const name = process.argv[i];
  if (!['--environment', '--experience'].includes(name) || !process.argv[i + 1]) throw new Error('Expected --environment Local|Uat|Production and --experience gallery|world.');
  options[name.slice(2)] = process.argv[i + 1];
}
if (!['Local', 'Uat', 'Production'].includes(options.environment) || !['gallery', 'world'].includes(options.experience)) throw new Error('Invalid overlay profile.');
const require = createRequire(resolve(web, 'package.json'));
async function bin(packageName, command) {
  let path;
  try { path = require.resolve(packageName + '/package.json'); }
  catch { throw new Error(`The installed ${packageName} dependency is missing from WebCommerce/node_modules. Restore the project's frontend dependencies before building.`); }
  const pkg = JSON.parse(await readFile(path, 'utf8'));
  const relative = typeof pkg.bin === 'string' ? pkg.bin : pkg.bin?.[command];
  if (!relative) throw new Error(`The ${command} build command is missing.`);
  const executable = resolve(dirname(path), relative); await access(executable); return executable;
}
function run(script, args = []) {
  const result = spawnSync(process.execPath, [script, ...args], { cwd: web, stdio: 'inherit', windowsHide: true, timeout: 120000 });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Frontend build failed (${result.status ?? result.signal}). Unity build was not started.`);
}
console.log('TTB overlay: checking types and compiling the latest UI...');
run(await bin('typescript', 'tsc'), ['--noEmit']);
run(await bin('vite', 'vite'), ['build']);
run(resolve(web, 'scripts/standalone.mjs'));
const template = resolve(repository, 'Assets/WebGLTemplates/MANGOsTTB');
await mkdir(resolve(template, 'commerce'), { recursive: true });
await cp(resolve(web, 'dist'), resolve(template, 'commerce'), { recursive: true, force: true });
const apiBase = options.environment === 'Uat' ? 'https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1'
  : options.environment === 'Production' ? 'https://ttb-api.mangosgo.com/ttb-commerce/v1' : '/ttb-commerce/v1';
const config = { pwaEnabled: true, environment: options.environment, apiBase, experience: options.experience, assetBase: './commerce/' };
await writeFile(resolve(template, 'ttb-commerce-config.js'), `window.MANGOS_TTB_CONFIG=Object.freeze(${JSON.stringify(config)});\n`);
console.log(`TTB_OVERLAY_PREPARED: ${options.environment}; ${options.experience}; no credentials included.`);
