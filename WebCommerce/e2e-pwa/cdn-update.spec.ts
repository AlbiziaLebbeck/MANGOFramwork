import { test, expect, type Page } from '@playwright/test';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { mkdir } from 'node:fs/promises';
const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-cdn-pwa');
async function ready(page: Page) {
  await page.goto('/');
  await expect(page).toHaveTitle('MANGOs · Gallery');
  await expect(page.getByRole('combobox', { name: 'เปลี่ยนภาษา' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true);
}
test('transformed HTML and stale CDN aliases still install a verified update without reloading another tab', async ({ page, context }) => {
  const failures: string[] = [], mutations: string[] = [];
  let offline = false;
  const observe = (p: Page) => {
    p.on('pageerror', e => failures.push(e.message));
    p.on('console', m => {
      if (m.type() !== 'error' && m.type() !== 'warning') return;
      if (offline && /ERR_INTERNET_DISCONNECTED/.test(m.text())) return;
      failures.push(m.text());
    });
  };
  context.on('page', observe); observe(page);
  context.on('request', r => { if (r.method() !== 'GET' && r.method() !== 'HEAD' && !r.url().endsWith('/__qa/release2')) mutations.push(r.url()); });
  await ready(page);
  // The network entry has injected analytics; the exact built shell remains separately verifiable.
  const network = await (await page.request.get('/index.html')).text();
  expect(network).toContain('__hostingAnalyticsInjected');
  const release1 = await page.locator('meta[name="mango-ttb-release"]').getAttribute('content');
  const other = await context.newPage(); await ready(other);
  expect((await page.request.post('/__qa/release2')).ok()).toBe(true);
  const manifest = await (await page.request.get('/pwa-release.json')).json();
  expect(manifest.release).not.toBe(release1);
  const stale = await (await page.request.get('/mango-ttb.js')).text();
  expect(stale).not.toContain('PWA browser fixture version 2');
  const current = await (await page.request.get('/mango-ttb.js?__mangos_release=' + manifest.release)).text();
  expect(current).toContain('PWA browser fixture version 2');
  // Even the previous worker URL is stale at the edge. A fresh entry uses the new release in asset/worker URLs.
  await other.goto('/?refresh=cdn-regression');
  await expect.poll(() => other.evaluate(async () => !!(await navigator.serviceWorker.getRegistration())?.waiting)).toBe(true);
  await expect(other.getByRole('button', { name: 'อัปเดตแอป', exact: true })).toBeEnabled();
  await other.getByRole('button', { name: 'อัปเดตแอป', exact: true }).click();
  await expect.poll(() => other.locator('meta[name="mango-ttb-release"]').getAttribute('content')).toBe(manifest.release);
  expect(await page.locator('meta[name="mango-ttb-release"]').getAttribute('content')).toBe(release1);
  await mkdir(evidence, { recursive: true });
  await other.getByRole('combobox', { name: 'เปลี่ยนภาษา' }).selectOption('en');
  await expect(other.getByRole('button', { name: 'Order history', exact: true })).toBeVisible();
  await other.screenshot({ path: resolve(evidence, 'verified-cdn-update-en.png') });
  // Temporary navigation queries deliberately stay network-only. Return to the canonical app URL for offline use.
  await other.goto('/');
  await expect(other.getByRole('combobox', { name: 'Language' })).toBeVisible();
  offline = true; await context.setOffline(true); await other.reload();
  await expect(other.getByRole('combobox', { name: 'Language' })).toBeVisible();
  expect(await other.evaluate(() => '__hostingAnalyticsInjected' in window)).toBe(false);
  const cached = await other.evaluate(async () => {
    const names: string[] = [];
    for (const key of await caches.keys()) for (const req of await (await caches.open(key)).keys()) names.push(req.url);
    return names;
  });
  expect(cached.some(url => /ttb-commerce|\/Build\/|\/StreamingAssets\//.test(new URL(url).pathname))).toBe(false);
  expect(failures).toEqual([]); expect(mutations).toEqual([]);
});
