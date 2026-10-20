import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';

test('actual compiled Unity starts from compressed hash URLs and reuses public scene data on reload', async ({ page, request }) => {
  expect(await (await request.get('/ttb-commerce/v1/storefront/config')).json()).toMatchObject({ environment: 'Local', paymentMode: 'Mock' });
  const errors: string[] = [], financial: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => {
    if (request.method() !== 'GET' && /\/ttb-commerce\/v1\/(orders|qr|mock|void)/.test(request.url())) financial.push(request.url());
  });
  const report = await (await request.get('/webgl-transfer.json')).json();
  expect(report.transferBytes).toBeLessThan(report.decodedBytes / 2);
  const timings = [];
  for (const run of ['cold', 'warm']) {
    await page.goto('/');
    await expect(page.locator('#ttb-enter')).toBeEnabled({ timeout: 120000 });
    expect(await page.evaluate(() => !!(window as unknown as { unityInstance?: unknown }).unityInstance)).toBe(true);
    const info = await page.evaluate(() => ({ elapsedMs: performance.now(), assets: performance.getEntriesByType('resource')
      .filter(entry => /\/Build\//.test(entry.name)).map(entry => { const r = entry as PerformanceResourceTiming; return { path: new URL(r.name).pathname, transferBytes: r.transferSize, decodedBytes: r.decodedBodySize }; }) }));
    expect(info.assets.every(asset => /\/Build\/[a-f0-9]{64}\./.test(asset.path))).toBe(true);
    if (run === 'warm') expect(info.assets.filter(asset => /\.data\.br$/.test(asset.path)).reduce((n, asset) => n + asset.transferBytes, 0)).toBe(0);
    timings.push({ run, ...info });
  }
  const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-native-transfer');
  await mkdir(evidence, { recursive: true });
  await page.screenshot({ path: resolve(evidence, 'compressed-native-ready.png') });
  await writeFile(resolve(evidence, 'timings.json'), JSON.stringify({ report, timings }, null, 2));
  expect(errors).toEqual([]); expect(financial).toEqual([]);
});
