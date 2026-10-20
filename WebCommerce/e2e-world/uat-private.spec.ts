import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';
import { launcherClick, launcherPixel, nativePixel } from './native-pixel';
import { clickRenderedProduct } from './rendered-product';

const publicMode = process.env.TTB_UAT_PUBLIC_QA === '1';
const portalMode = process.env.TTB_PORTAL_QA === '1';
const pageUrl = portalMode ? 'https://plus.mangosgo.com/ttb-portal-qa/v1/' : 'https://ttb-api-test.mangosgo.com/store-uat/';
const gameUrl = portalMode ? 'wss://multiplayer-plus.mangosgo.com/ttb-portal-qa/v1' : 'wss://ttb-api-test.mangosgo.com/ttb-game-uat';
const portalClient = resolve(process.env.TTB_PORTAL_QA_CLIENT ?? '../Builds/TTB/Portal/01e6564154604c78979d07bc55d669d6/Release/Client');
// Default: relay real private UAT bytes. Public mode uses native HTTPS/WSS without transport interception.
// Neither mode proves authenticated MANGOs login, bank payment or entitlement delivery.
test(`${portalMode ? 'private Portal package' : publicMode ? 'public HTTPS/WSS' : 'private'} UAT Linux world serves the UAT catalog and native previews without Local identities or financial mutations`, async ({ page }) => {
  const evidence = resolve(process.env.TTB_UAT_QA_OUTPUT ?? '../.artifacts/uat-world/private-browser');
  await mkdir(evidence, { recursive: true });
  const errors: string[] = [], messages: string[] = [], mutations: string[] = [], external: string[] = [], expectedAnonymousIdentityResponses: number[] = [];
  let clientFrames = 0, serverFrames = 0;
  const nativeWebSocketUrls: string[] = [];
  if (publicMode) page.on('websocket', socket => {
    nativeWebSocketUrls.push(socket.url());
    socket.on('framesent', () => clientFrames++);
    socket.on('framereceived', () => serverFrames++);
    socket.on('socketerror', error => errors.push(String(error)));
  });
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => { if (response.url() === 'https://api.mangosgo.com/api/v1/users/me' && response.status() === 401) expectedAnonymousIdentityResponses.push(401); });
  page.on('console', message => {
    messages.push(message.text());
    if (message.type() === 'error' && !(message.location().url === 'https://api.mangosgo.com/api/v1/users/me' && /401/.test(message.text()))) errors.push(message.text().slice(0, 800));
  });
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url());
    if (url.href === 'https://api.mangosgo.com/api/v1/users/me' && request.method() === 'GET') { await route.fallback(); return; }
    if (url.origin === 'https://www.mangosgo.com' && url.pathname.startsWith('/avatar/') && url.pathname.endsWith('.glb') && request.method() === 'GET') { await route.fallback(); return; }
    if (portalMode && url.origin === 'https://plus.mangosgo.com' && url.pathname.startsWith('/ttb-portal-qa/v1/') && request.method() === 'GET') {
      const relativePath = decodeURIComponent(url.pathname.slice('/ttb-portal-qa/v1/'.length)) || 'index.html';
      const file = resolve(portalClient, relativePath);
      if (!file.startsWith(portalClient + sep)) { external.push(url.pathname); await route.abort(); return; }
      const types: Record<string, string> = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ttf': 'font/ttf', '.svg': 'image/svg+xml' };
      await route.fulfill({ path: file, contentType: types[extname(file)] ?? 'application/octet-stream', headers: { 'cache-control': 'no-store' } }); return;
    }
    if (url.hostname !== 'ttb-api-test.mangosgo.com') { external.push(url.origin + url.pathname); await route.abort(); return; }
    if (!['GET', 'HEAD'].includes(request.method())) { mutations.push(url.pathname); await route.abort(); return; }
    if (publicMode || portalMode) { await route.continue(); return; }
    const port = url.pathname.startsWith('/ttb-commerce-test/v1/') ? 18742 : 18589;
    const response = await route.fetch({ url: `http://127.0.0.1:${port}${url.pathname}${url.search}`, headers: { ...request.headers(), host: 'ttb-api-test.mangosgo.com' }, timeout: 120000 });
    await route.fulfill({ response });
  });
  if (!publicMode) await page.routeWebSocket(gameUrl, socket => {
    if (portalMode) nativeWebSocketUrls.push(socket.url());
    const upstream = new WebSocket('ws://127.0.0.1:18780' + new URL(gameUrl).pathname, socket.protocols());
    upstream.binaryType = 'arraybuffer';
    const queued: (string | Buffer)[] = [];
    const forward = (message: string | Buffer) => upstream.send(typeof message === 'string' ? message : new Uint8Array(message));
    socket.onMessage(message => { clientFrames++; if (upstream.readyState === WebSocket.OPEN) forward(message); else queued.push(message); });
    upstream.addEventListener('open', () => { for (const message of queued) forward(message); queued.length = 0; });
    upstream.addEventListener('message', event => { serverFrames++; socket.send(typeof event.data === 'string' ? event.data : Buffer.from(event.data as ArrayBuffer)); });
    upstream.addEventListener('error', () => errors.push('Private UAT WebSocket transport failed'));
    socket.onClose(() => { if (upstream.readyState === WebSocket.OPEN) upstream.close(); });
  });
  const prefix = publicMode || portalMode ? 'https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1' : 'http://127.0.0.1:18742/ttb-commerce-test/v1';
  const config = await (await page.request.get(prefix + '/storefront/config')).json();
  expect(config).toMatchObject({ environment: 'Uat', paymentMode: 'Bank', paymentsReady: false, mockSimulation: false, gameUseReady: true });
  const catalog = await (await page.request.get(prefix + '/catalog/products?limit=100')).json();
  expect(catalog.items).toHaveLength(3);
  expect(catalog.items.flatMap((p: { variants: { priceMinor: number; sku: string }[] }) => p.variants.map(v => v.priceMinor)).sort((a: number, b: number) => a - b)).toEqual([100, 1000, 9900]);
  expect(catalog.items.every((p: { variants: { sku: string }[] }) => p.variants.every(v => v.sku.startsWith('UAT-')))).toBe(true);
  await page.goto('./');
  await expect(page).toHaveTitle(/MANGOs.*Gallery/);
  await expect(page).toHaveURL(pageUrl);
  await expect(page.getByRole('button', { name: 'เริ่มชมแกลเลอรี', exact: true })).toBeEnabled({ timeout: 120000 });
  await page.getByRole('button', { name: 'เริ่มชมแกลเลอรี', exact: true }).click();
  await expect.poll(() => messages.some(message => message.includes('MANGOS_CLIENT_STATE Started')), { timeout: 30000 }).toBe(true);
  await expect.poll(async () => { const glyph = await launcherPixel(page, 620, 456), gap = await launcherPixel(page, 640, 456); return glyph.every(v => v > 245) && gap[1] < 225; }, { timeout: 15000 }).toBe(true);
  await page.screenshot({ path: resolve(evidence, 'uat-login.png') });
  await launcherClick(page, 640, 456);
  await expect.poll(async () => (await nativePixel(page, 500, 467)).every(v => v < 150), { timeout: 15000 }).toBe(true);
  await page.mouse.click(640, 446, { delay: 120 }); await page.keyboard.type('TTBUAT' + Date.now().toString(36).slice(-5), { delay: 90 }); await page.keyboard.press('Tab');
  await expect.poll(async () => (await nativePixel(page, 520, 510)).every(v => v > 225), { timeout: 15000 }).toBe(true);
  await page.mouse.click(640, 523, { delay: 120 });
  await expect(page.locator('#unity-canvas')).toHaveAttribute('data-commerce-ready', 'true', { timeout: 60000 });
  await expect(page.locator('#unity-canvas')).toHaveAttribute('data-game-ready', 'true', { timeout: 60000 });
  // Walk to a clear line of sight using the real player controller: an avatar capsule can cover the lamp raycast at the random arrival position.
  await page.keyboard.down('KeyD'); await page.waitForTimeout(700); await page.keyboard.up('KeyD');
  await page.screenshot({ path: resolve(evidence, 'uat-world.png') });
  await clickRenderedProduct(page, 'lamp');
  await expect(page.getByRole('heading', { name: 'Orbit Lamp', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'ซูมเข้า', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'หมุนโมเดล', exact: true }).click();
  await page.getByRole('button', { name: 'ซูมเข้า', exact: true }).click();
  await page.screenshot({ path: resolve(evidence, 'uat-product-desktop.png') });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('heading', { name: 'Orbit Lamp', exact: true })).toBeInViewport();
  await expect(page.getByRole('button', { name: 'ปิดหน้าต่าง', exact: true })).toBeInViewport();
  await expect(page.getByRole('button', { name: 'ซูมเข้า', exact: true })).toBeInViewport();
  const previewPixels = async () => {
    const bytes = await page.locator('.preview-pane').screenshot();
    return page.evaluate(async encoded => {
      const image = new Image(); image.src = 'data:image/png;base64,' + encoded; await image.decode();
      const canvas = new OffscreenCanvas(image.width, image.height), context = canvas.getContext('2d')!; context.drawImage(image, 0, 0);
      const pixels = context.getImageData(0, 0, image.width, image.height).data;
      let orange = 0, light = 0;
      for (let i = 0; i < pixels.length; i += 4) { const r = pixels[i], g = pixels[i + 1], b = pixels[i + 2]; if (r > 190 && g > 60 && g < 190 && b < 110 && r - g > 40) orange++; if (r > 215 && g > 215 && b > 210) light++; }
      return { orange, light };
    }, bytes.toString('base64'));
  };
  await expect.poll(async () => { const pixels = await previewPixels(); return pixels.orange > 100 && pixels.light > 1000; }, { timeout: 10000 }).toBe(true);
  const mobilePreviewPixels = await previewPixels();
  await page.screenshot({ path: resolve(evidence, 'uat-product-mobile.png') });
  await page.getByRole('button', { name: 'เพิ่มลงตะกร้า', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'เข้าสู่ระบบเพื่อสั่งซื้อ', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'เชื่อมต่อบัญชี MANGOs', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true })).toHaveCount(0);
  await expect(page.getByText('เครื่องมือทดสอบ Local', { exact: true })).toHaveCount(0);
  await page.screenshot({ path: resolve(evidence, 'uat-commerce-signin.png') });
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.getByRole('button', { name: 'ปิดหน้าต่าง', exact: true }).click();
  await page.keyboard.down('KeyA'); await page.waitForTimeout(250); await page.keyboard.up('KeyA');
  await page.screenshot({ path: resolve(evidence, 'uat-world-restored.png') });
  expect(clientFrames).toBeGreaterThan(0); expect(serverFrames).toBeGreaterThan(0);
  if (publicMode || portalMode) expect(nativeWebSocketUrls).toEqual([gameUrl]);
  expect(mutations).toEqual([]); expect(external).toEqual([]); expect(errors).toEqual([]);
  await writeFile(resolve(evidence, 'result.json'), JSON.stringify({ verifiedAt: new Date().toISOString(), config, catalogIds: catalog.items.map((p: { id: string }) => p.id), clientFrames, serverFrames,
    httpTransportRelayedThroughPrivateSsh: !publicMode && !portalMode, staticClientServedFromLocalFinalPackage: portalMode,
    webSocketFramesUnmodified: true, webSocketTransportIntercepted: !publicMode, nativeWebSocketUrls, portalNativeSlugAndVersionRouting: portalMode,
    serviceWorkersBlockedForFreshAssetQa: true, publicTlsAccepted: publicMode, mangosAuthenticationTested: false,
    paymentOrGrantCreated: false, expectedAnonymousIdentityResponses, mobilePreviewPixels, mutations, external, errors }, null, 2));
});
