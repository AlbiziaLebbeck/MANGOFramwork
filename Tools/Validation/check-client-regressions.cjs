// Matching local Server/Client on 7788/WS. Account cases require Cookie SSO in
// the isolated test build; all account/API responses are synthetic fixtures.
// node check-client-regressions.cjs <build-dir> <humanoid.glb> [evidence-dir]
const { chromium } = require('playwright');
const fs = require('node:fs'), http = require('node:http'), path = require('node:path');
const assert = require('node:assert/strict');
const output = path.resolve(process.argv[2] || ''), fixture = path.resolve(process.argv[3] || '');
const evidence = path.resolve(process.argv[4] || 'Builds/Validation/ClientRegressions');
const client = path.join(output, 'Client');
assert(fs.existsSync(path.join(client, 'index.html')), 'Pass the compiled build directory');
assert(fs.statSync(fixture).isFile(), 'Pass a known humanoid GLB fixture');
fs.mkdirSync(evidence, { recursive: true });
const bytes = fs.readFileSync(fixture);
const accountModel = 'https://api.mangosgo.com/api/v1/avatar-files/qa-account.glb';
const unavailableModel = 'https://api.mangosgo.com/api/v1/avatar-files/qa-unavailable.glb';
const server = http.createServer((req, res) => {
  let pathname;
  try { pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname); }
  catch { res.writeHead(400); return res.end(); }
  if (pathname === '/') pathname = '/index.html';
  const file = path.resolve(client, '.' + pathname);
  if (!file.startsWith(client + path.sep)) { res.writeHead(403); return res.end(); }
  const type = { '.html': 'text/html', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json' }[path.extname(file)] || 'application/octet-stream';
  fs.readFile(file, (error, data) => { res.writeHead(error ? 404 : 200, { 'Content-Type': type }); res.end(error ? 'Missing' : data); });
});
const result = { passed: false, buildId: path.basename(output), accountMode: 'mocked Cookie SSO API', cases: [] };
const envelope = data => JSON.stringify({ status: 'success', data });
const pause = (page, ms = 200) => page.waitForTimeout(ms);
async function diagnostic(page) {
  await page.evaluate(() => { window.MANGOS_DIAGNOSTICS = null; unityInstance.SendMessage('MangosBrowserBridge', 'OnRequestDiagnostics', ''); });
  await page.waitForFunction(() => !!window.MANGOS_DIAGNOSTICS);
  return page.evaluate(() => window.MANGOS_DIAGNOSTICS);
}
async function untilDiagnostic(page, predicate, message, timeout = 45000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) { const data = await diagnostic(page); if (predicate(data)) return data; await pause(page, 300); }
  throw new Error(message + ': ' + JSON.stringify(await diagnostic(page)));
}
function angle(a, b) { return Math.abs(((a - b + 540) % 360) - 180); }
async function mouseCamera(page) {
  await page.mouse.move(850, 350); await pause(page, 500);
  const before = await diagnostic(page);
  await page.mouse.move(1050, 430, { steps: 12 }); await pause(page, 500);
  const idle = await diagnostic(page);
  assert(angle(before.cameraYaw, idle.cameraYaw) < .2 && angle(before.cameraPitch, idle.cameraPitch) < .2, 'Camera moved without right mouse button');
  await page.mouse.down({ button: 'right' });
  await page.mouse.move(850, 360, { steps: 15 }); await pause(page, 300);
  await page.mouse.up({ button: 'right' }); await pause(page, 400);
  const drag = await diagnostic(page);
  assert(angle(idle.cameraYaw, drag.cameraYaw) > 1, 'Right-button drag did not rotate camera');
  await page.mouse.move(1050, 420, { steps: 12 }); await pause(page, 500);
  const released = await diagnostic(page);
  assert(angle(drag.cameraYaw, released.cameraYaw) < .2, 'Camera kept moving after releasing right button');
  return { idleYawDelta: angle(before.cameraYaw, idle.cameraYaw), dragYawDelta: angle(idle.cameraYaw, drag.cameraYaw), releasedYawDelta: angle(drag.cameraYaw, released.cameraYaw) };
}
(async () => {
  let browser;
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(8767, '127.0.0.1', resolve); });
    browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--enable-unsafe-swiftshader'] });
    for (const spec of [
      { name: 'guest-desktop', account: false, viewport: { width: 1280, height: 800 } },
      { name: 'account-desktop', account: true, viewport: { width: 1280, height: 800 } },
      { name: 'account-phone', account: true, viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true, userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1' },
      { name: 'account-tablet', account: true, viewport: { width: 820, height: 1180 }, isMobile: true, hasTouch: true, userAgent: 'Mozilla/5.0 (Linux; Android 14; SM-X700) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36' }
    ].filter(spec => !process.env.MANGOS_QA_CASE || spec.name === process.env.MANGOS_QA_CASE)) {
      const { name, account, ...options } = spec;
      const context = await browser.newContext(options), page = await context.newPage();
      const logs = [], errors = [], fetches = [], modelRequests = [];
      const entry = { name, passed: false, logs, errors, fetches }; result.cases.push(entry);
      page.on('console', message => { logs.push(message.text()); if (message.type() === 'error') errors.push(message.text()); });
      page.on('pageerror', error => errors.push(error.message));
      page.on('request', request => { if (/\.(glb|vrm)(\?|$)/i.test(request.url())) modelRequests.push(request.url()); });
      await page.addInitScript(() => {
        localStorage.setItem('mangos.inputMode', 'touch');
        window.QA_FETCHES = [];
        const original = window.fetch;
        window.fetch = function (url, init) { if (String(url).includes('avatar-files/')) QA_FETCHES.push({ credentials: init?.credentials }); return original.apply(this, arguments); };
      });
      await page.route('**/api/v1/users/me', route => route.fulfill({ status: account ? 200 : 401, contentType: 'application/json', body: account ? envelope({ User: { id: 'qa-user', firstName: 'PoppyAccount', email: 'qa@example.invalid' } }) : '{}' }));
      await page.route('**/api/v1/avatars', route => route.fulfill({ contentType: 'application/json', body: envelope({ Count: 3, Avatars: [
        { id: 3, url: 'https://www.mangosgo.com/avatar/CasualPop.glb', isDefault: true, userId: 'qa-user', avatarType: 'glb' },
        { id: 1, url: accountModel, isDefault: false, userId: 'qa-user', avatarType: 'glb' },
        { id: 2, url: unavailableModel, isDefault: false, userId: 'qa-user', avatarType: 'glb' }
      ] }) }));
      await page.route('**/api/v1/mgo-gold/get-my-wallet', route => route.fulfill({ contentType: 'application/json', body: envelope({ UserMgoGoldWallet: { id: 1, userId: 'qa-user', amount: 120 } }) }));
      await page.route('**/api/v1/avatar-files/**', async route => {
        fetches.push({ available: route.request().url() === accountModel });
        if (route.request().url() !== accountModel) { await pause(page, 1500); return route.fulfill({ status: 404, body: 'Unavailable QA model' }); }
        return route.fulfill({ contentType: 'model/gltf-binary', body: bytes });
      });
      await page.route('**/mangos-config.js', route => route.fulfill({ contentType: 'application/javascript', body: fs.readFileSync(path.join(client, 'mangos-config.js'), 'utf8') + '\nwindow.VOICE_APP_CONFIG.mock=true;' }));
      await page.goto('http://127.0.0.1:8767/', { waitUntil: 'domcontentloaded' });
      assert.match(await page.title(), /mangos/i); assert.equal(page.url(), 'http://127.0.0.1:8767/');
      if (options.isMobile) await page.locator('#startOverlay').tap();
      else await page.locator('#startOverlay').click();
      await page.waitForFunction(() => !!window.unityInstance, null, { timeout: 120000 });
      await page.waitForFunction(() => document.querySelector('#unity-loading-bar').style.display === 'none');
      const connected = Date.now() + 30000;
      while (!logs.some(line => line.includes('MANGOS_CLIENT_STATE Started')) && Date.now() < connected) await pause(page, 300);
      assert(logs.some(line => line.includes('MANGOS_CLIENT_STATE Started')), 'FishNet did not connect');
      await pause(page, account ? 3000 : 8000);
      await page.evaluate(isAccount => isAccount ? unityInstance.SendMessage('Launcher', 'AuthCanvas_OnContinueAsUser') : unityInstance.SendMessage('Launcher', 'AuthCanvas_OnLoginAsGuest', 'PoppyValidation'), account);
      await page.waitForFunction(() => MangosController.getState().ready, null, { timeout: 45000 });
      const loaded = await untilDiagnostic(page, d => d.avatarChoices === (account ? 4 : 2) && d.avatarLoaded && !d.avatarLoading, 'Avatar catalog/model did not become ready');
      assert.equal(loaded.selectableAvatars, loaded.avatarChoices, 'Preview generation blocked selecting avatars');
      assert(!loaded.avatarError, 'Selected avatar failed to load');
      assert.equal(await page.locator('#mangos-controls select').count(), 0, 'Controls selector still exists');
      assert.equal((await page.evaluate(() => MangosController.getState())).touch, !!options.isMobile);
      await page.screenshot({ path: path.join(evidence, name + '-world.png') });
      if (!options.isMobile) entry.camera = await mouseCamera(page);
      // Open through the same button users press, rather than calling the panel directly.
      if (options.isMobile) await page.touchscreen.tap(26, 26);
      else await page.mouse.click(40, 40, { delay: 100 });
      await untilDiagnostic(page, d => d.avatarPanelOpen && d.inputBlocked, 'Avatar button did not open its panel');
      assert(await page.locator('#mangos-stick').isHidden(), 'Touch controller overlaps avatar modal');
      await pause(page, 2500);
      assert.equal((await diagnostic(page)).selectableAvatars, account ? 4 : 2, 'A failed thumbnail disabled selection');
      await page.screenshot({ path: path.join(evidence, name + '-avatars.png') });
      if (account && !options.isMobile) {
        // Change to the first public model, then select the account model through
        // actual catalog buttons. Preview requests have already finished.
        const publicBefore = modelRequests.filter(url => url.includes('RajPattern_Blue.glb')).length;
        await page.mouse.click(82, 205, { delay: 100 }); await pause(page, 150); await page.mouse.click(445, 486, { delay: 100 });
        await untilDiagnostic(page, d => !d.avatarPanelOpen && d.avatarLoaded && !d.avatarLoading, 'Public avatar selection failed');
        assert(modelRequests.filter(url => url.includes('RajPattern_Blue.glb')).length > publicBefore, 'Submit did not load the selected public avatar');
        await pause(page, 5500);
        await page.mouse.click(40, 40, { delay: 100 }); await pause(page, 400);
        const accountBefore = fetches.filter(request => request.available).length;
        await page.mouse.click(320, 205, { delay: 100 }); await pause(page, 150); await page.mouse.click(445, 486, { delay: 100 });
        await untilDiagnostic(page, d => !d.avatarPanelOpen && d.avatarLoaded && !d.avatarLoading, 'Account avatar selection failed');
        assert(fetches.filter(request => request.available).length > accountBefore, 'Submit did not load the selected account avatar');
        entry.accountSelection = true;
      }
      await page.evaluate(() => unityInstance.SendMessage('AvatarSystem', 'OnClick_Cancel'));
      await untilDiagnostic(page, d => !d.avatarPanelOpen && !d.inputBlocked, 'Cancel did not restore controls');
      if (options.isMobile) {
        assert(await page.locator('#mangos-stick').isVisible());
        const cdp = await context.newCDPSession(page), stick = await page.locator('#mangos-stick').boundingBox();
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: stick.x + stick.width * .7, y: stick.y + stick.height * .3, id: 1 }] });
        await pause(page, 500);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        const lookBefore = await diagnostic(page);
        const lookPoint = { x: options.viewport.width * .8, y: options.viewport.height * .55, id: 3 };
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [lookPoint] });
        await pause(page, 100); lookPoint.x -= 60; lookPoint.y -= 20;
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [lookPoint] });
        await pause(page, 200);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] }); await pause(page, 400);
        const lookAfter = await diagnostic(page);
        entry.touchYawDelta = angle(lookBefore.cameraYaw, lookAfter.cameraYaw);
        assert(entry.touchYawDelta > 1, 'Touch camera drag did not rotate');
        await page.setViewportSize({ width: options.viewport.height, height: options.viewport.width }); await pause(page, 500);
        assert.equal((await page.evaluate(() => MangosController.getState())).orientation, 'landscape');
        await page.screenshot({ path: path.join(evidence, name + '-landscape.png') });
        await page.keyboard.press('w');
        await untilDiagnostic(page, d => !d.touch, 'Keyboard did not switch controller automatically');
        // Exercise the exact regression: touch -> keyboard, then mouse without RMB.
        await page.setViewportSize({ width: 1280, height: 800 }); await pause(page, 500);
        entry.cameraAfterTouch = await mouseCamera(page);
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: 600, y: 500, id: 2 }] });
        await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
        await untilDiagnostic(page, d => d.touch, 'Touch did not restore mobile controller');
        await cdp.detach();
      }
      if (account) {
        entry.credentials = await page.evaluate(() => QA_FETCHES);
        assert(entry.credentials.length && entry.credentials.every(f => f.credentials === 'include'), 'Account asset download did not include Cookie SSO credentials');
      }
      assert(![...errors, ...logs].some(line => /RuntimeError|TypeError|ReferenceError|NullReferenceException|EntryPointNotFoundException|DllNotFoundException/.test(line)), 'Runtime exception in compiled WebGL');
      entry.final = await diagnostic(page); entry.passed = true;
      await context.close();
    }
    result.passed = true;
  } catch (error) { result.failure = error.stack || error.message; process.exitCode = 1; }
  finally {
    if (browser) await browser.close(); server.close();
    fs.writeFileSync(path.join(evidence, 'client-regressions.json'), JSON.stringify(result, null, 2) + '\n');
    console.log(JSON.stringify({ passed: result.passed, buildId: result.buildId, cases: result.cases.map(({ name, passed, camera, cameraAfterTouch, errors }) => ({ name, passed, camera, cameraAfterTouch, errors: errors.slice(-5) })), failure: result.failure }, null, 2));
  }
})().catch(error => { console.error(error); server.close(); process.exitCode = 1; });
