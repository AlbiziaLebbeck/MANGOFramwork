import { test, expect, type APIResponse } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

test('bounded Local Mock burst and PDF backlog preserve eight purchases while real Unity remains open', async ({ page }) => {
  const prefix = '/ttb-commerce/v1'; const count = 8;
  const configuration = await (await page.request.get(prefix + '/storefront/config')).json();
  expect(configuration.environment).toBe('Local'); expect(configuration.paymentMode).toBe('Mock'); expect(configuration.mockSimulation).toBe(true);
  expect(new URL(test.info().project.use.baseURL!).hostname).toBe('127.0.0.1');
  const errors: string[] = []; let bankChecks = 0;
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (/\/(verify|enquiry)(\/|$)/.test(new URL(request.url()).pathname)) bankChecks++; });
  await page.goto('/');
  await page.getByRole('button', { name: 'เริ่มชมแกลเลอรี', exact: true }).click({ timeout: 120000 });
  await expect(page.locator('#unity-canvas')).toHaveAttribute('data-commerce-ready', 'true', { timeout: 20000 });
  const origin = 'http://127.0.0.1:5186';
  const login = await page.request.post(prefix + '/session/local', { data: { subject: 'bounded-load-' + crypto.randomUUID() }, headers: { Origin: origin } });
  expect(login.ok()).toBe(true); const session = await login.json(); const headers = { Origin: origin, 'X-CSRF-Token': session.csrfToken };
  const product = await (await page.request.get(prefix + '/catalog/by-model/mango-star')).json();
  const orders: string[] = [];
  async function json(response: APIResponse) { expect(response.ok(), 'Local fixture HTTP ' + response.status()).toBe(true); return response.json(); }
  for (let index = 0; index < count; index++) {
    const cart = await json(await page.request.get(prefix + '/cart'));
    const changed = await json(await page.request.put(prefix + '/cart/lines/' + product.variants[0].id, { data: { quantity: 1, expectedVersion: cart.version }, headers }));
    const quote = await json(await page.request.post(prefix + '/checkout/quote', { data: { cartVersion: changed.version, buyer: { name: 'Synthetic bounded load' }, delivery: { method: 'digital' } }, headers }));
    const order = await json(await page.request.post(prefix + '/orders', { data: { quoteId: quote.id }, headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } }));
    const qr = await json(await page.request.post(`${prefix}/orders/${order.id}/qr`, { headers }));
    expect(qr.mode).toBe('Mock'); expect(qr.payload).toMatch(/^MANGO-MOCK:/); orders.push(order.id);
  }
  const started = performance.now();
  // Browser rAF opportunity timing is not Unity FPS or a physical-device/production capacity result.
  const frameSample = page.evaluate(() => new Promise<{ intervals: number[] }>(resolveSample => {
    const intervals: number[] = []; let last = performance.now(); const until = last + 20000;
    const frame = (now: number) => { intervals.push(now - last); last = now; if (now < until) requestAnimationFrame(frame); else resolveSample({ intervals }); };
    requestAnimationFrame(frame);
  }));
  const acknowledgements = await Promise.all(orders.flatMap(id => {
    const requestKey = crypto.randomUUID();
    return [0, 1].map(async () => {
      const before = performance.now();
      const response = await page.request.post(`${prefix}/orders/${id}/mock-pay`, { data: { requestKey, scenario: 'duplicate' }, headers });
      expect(response.ok()).toBe(true); return performance.now() - before;
    });
  }));
  let readyDocuments = 0;
  await expect(async () => {
    const activities = await Promise.all(orders.map(async id => json(await page.request.get(`${prefix}/orders/${id}/activity`))));
    readyDocuments = 0;
    for (const activity of activities) {
      expect(activity.order.paymentState).toBe('Paid'); expect(activity.documents).toHaveLength(1);
      if (activity.documents[0].state === 'Ready') readyDocuments++;
    }
    expect(readyDocuments).toBe(count);
    const inventory = await json(await page.request.get(prefix + '/inventory'));
    expect(inventory.items).toHaveLength(count); expect(new Set(inventory.items.map((item: { id: string }) => item.id)).size).toBe(count);
    expect(inventory.items.every((item: { orderId: string; state: string }) => orders.includes(item.orderId) && item.state === 'Active')).toBe(true);
  }).toPass({ timeout: 90000, intervals: [1000, 2000, 4000] });
  const allDeliveryReadyMs = performance.now() - started;
  const frames = await frameSample;
  const percentile = (values: number[], fraction: number) => [...values].sort((a, b) => a - b)[Math.min(values.length - 1, Math.ceil(values.length * fraction) - 1)];
  const evidence = resolve('../.artifacts/acceptance'); await mkdir(evidence, { recursive: true });
  const result = { checkedAt: new Date().toISOString(), environment: 'Local', paymentMode: 'Mock', boundedExperiment: true,
    uniquePurchases: count, concurrentHttpRequests: count * 2, scenario: 'duplicate', readyDocuments, activeEntitlements: count,
    localMockHttpMs: { p50: percentile(acknowledgements, .5), p95: percentile(acknowledgements, .95), maximum: Math.max(...acknowledgements) },
    allDeliveryReadyMs, browserRafSample: { milliseconds: 20000, callbacks: frames.intervals.length, p95IntervalMs: percentile(frames.intervals, .95) },
    automaticBankChecks: bankChecks, bankCallbackBenchmark: false, productionCapacityCertified: false, realMoney: false, pageErrors: errors.length };
  expect(bankChecks).toBe(0); expect(errors).toEqual([]);
  await writeFile(resolve(evidence, 'bounded-local-load.json'), JSON.stringify(result, null, 2));
  await page.screenshot({ path: resolve(evidence, 'bounded-local-load.png') });
  console.log(JSON.stringify(result));
});
