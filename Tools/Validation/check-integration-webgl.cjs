// Run against a matching local validation Server/Client pair (7788, WS).
// Requires Node.js, Playwright, and Microsoft Edge. See Documentation/BUILD_AND_RUNTIME.md.
const { chromium } = require('playwright');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const assert = require('node:assert/strict');

const project = path.resolve(__dirname, '../..');
assert(process.argv[2], 'Usage: node Tools/Validation/check-integration-webgl.cjs <build-directory> [evidence-directory]');
const output = path.resolve(process.argv[2]);
const client = path.join(output, 'Client');
const evidence = path.resolve(process.argv[3] || path.join(project, 'Builds/Validation/Browser'));
const guestName = process.env.MANGOS_VALIDATION_GUEST_NAME || 'PoppyValidation';
assert(fs.existsSync(path.join(client, 'index.html')), `Missing compiled Client: ${client}`);
fs.mkdirSync(evidence, { recursive: true });
const logs = [], errors = [], warnings = [], network = [];
const result = { buildId: path.basename(output), guestName, output, passed: false, logs, errors, warnings, network };

const server = http.createServer((req, res) => {
  let pathname;
  try { pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname); }
  catch { res.writeHead(400); res.end(); return; }
  if (pathname === '/') pathname = '/index.html';
  const file = path.resolve(client, '.' + pathname);
  if (!file.startsWith(client + path.sep)) { res.writeHead(403); res.end(); return; }
  const mime = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json' }[path.extname(file)] || 'application/octet-stream';
  fs.readFile(file, (error, data) => { res.writeHead(error ? 404 : 200, { 'Content-Type': mime }); res.end(error ? 'Missing' : data); });
});

(async () => {
  let browser;
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(8767, '127.0.0.1', resolve); });
    browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--enable-unsafe-swiftshader'] });
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 }, hasTouch: true });
    const page = await context.newPage();
    page.on('console', message => {
      logs.push(message.text());
      if (message.type() === 'error') errors.push(message.text());
      if (message.type() === 'warning') warnings.push(message.text());
    });
    page.on('pageerror', error => errors.push(error.stack || error.message));
    page.on('websocket', socket => {
      network.push({ type: 'websocket', url: socket.url() });
      socket.on('socketerror', error => network.push({ type: 'socketerror', error }));
    });
    page.on('requestfailed', request => network.push({ type: 'requestfailed', url: request.url(), error: request.failure()?.errorText }));
    page.on('response', response => {
      if (/\.(glb|vrm|gltf)(\?|$)/i.test(response.url())) network.push({ type: 'avatar-response', url: response.url(), status: response.status() });
    });
    await page.route('**/api/v1/users/me', route => route.fulfill({ status: 401, contentType: 'application/json', body: '{}' }));
    await page.route('**/mangos-config.js', route => route.fulfill({ contentType: 'application/javascript', body: fs.readFileSync(path.join(client, 'mangos-config.js'), 'utf8') + '\nwindow.VOICE_APP_CONFIG.mock=true;' }));

    await page.goto('http://127.0.0.1:8767/', { waitUntil: 'domcontentloaded' });
    result.page = { url: page.url(), title: await page.title() };
    assert.equal(result.page.url, 'http://127.0.0.1:8767/');
    assert.match(result.page.title, /mangos/i);
    assert(await page.locator('#startOverlay').isVisible(), 'Missing initial start UI');
    await page.locator('#startOverlay').click();
    await page.waitForFunction(() => !!window.unityInstance, null, { timeout: 120000 });
    await page.waitForFunction(() => document.querySelector('#unity-loading-bar').style.display === 'none');
    const deadline = Date.now() + 30000;
    while (!logs.some(line => line.includes('MANGOS_CLIENT_STATE Started')) && Date.now() < deadline) await page.waitForTimeout(500);
    assert(logs.some(line => line.includes('MANGOS_CLIENT_STATE Started')), 'Client did not connect to the local FishNet Server');
    await page.waitForTimeout(1500);
    await page.screenshot({ path: path.join(evidence, 'integration-launcher.png'), timeout: 15000 });
    await page.evaluate(name => unityInstance.SendMessage('Launcher', 'AuthCanvas_OnLoginAsGuest', name), guestName);
    await page.waitForFunction(() => MangosController.getState().ready, null, { timeout: 45000 });
    await page.waitForTimeout(10000);
    assert(logs.some(line => line.includes(`Your name is ${guestName},`)), 'Server did not confirm the requested guest name');
    result.voiceState = await page.evaluate(() => MangosVoiceOverlay.getState());
    result.controllerState = await page.evaluate(() => MangosController.getState());
    assert(result.voiceState.ready, 'Unity did not supply its joined world to the overlay');
    await page.screenshot({ path: path.join(evidence, 'integration-world-desktop.png'), timeout: 15000 });
    // Account, real mobile contexts, rotation and touch -> keyboard camera
    // regressions are covered by check-client-regressions.cjs.
    assert(![...errors, ...logs].some(line => /RuntimeError|TypeError|ReferenceError|NullReferenceException|EntryPointNotFoundException|DllNotFoundException/.test(line)), 'Runtime exception in compiled WebGL');
    result.passed = true;
  } catch (error) {
    result.failure = error.stack || error.message;
    process.exitCode = 1;
  } finally {
    if (browser) await browser.close();
    server.close();
    fs.writeFileSync(path.join(evidence, 'integration-webgl.json'), JSON.stringify(result, null, 2) + '\n');
    console.log(JSON.stringify({ passed: result.passed, guestName, buildId: result.buildId, page: result.page, errors: errors.slice(-8), failure: result.failure }));
  }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
