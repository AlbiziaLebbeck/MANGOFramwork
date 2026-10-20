import { test, expect } from '@playwright/test';
import { mkdir, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';
const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-session-i18n');
test.beforeEach(async ({ request }) => {
  await mkdir(evidence, { recursive: true });
  const config = await (await request.get('/ttb-commerce/v1/storefront/config')).json();
  expect(config).toMatchObject({ environment: 'Local', paymentMode: 'Mock', mockSimulation: true });
});

test('an existing HttpOnly SSO cookie opens history and inventory without a second login (identity fixture)', async ({
  page,
  context,
  baseURL,
}) => {
  const errors: string[] = [],
    exchanges: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await context.addCookies([
    { name: 'fixture-mangos-sso', value: 'qa-cookie-not-a-real-credential', url: baseURL!, httpOnly: true },
  ]);
  let sessionExists = false;
  const user = { id: 'ssotest-buyer', displayName: 'SSO Browser QA', roles: [] };
  await page.route('**/ttb-commerce/v1/storefront/config', (route) =>
    route.fulfill({
      json: {
        environment: 'Uat',
        paymentMode: 'Bank',
        paymentsReady: false,
        mockSimulation: false,
        currency: 'THB',
        gameUseReady: false,
        verificationDelaySeconds: 60,
        verificationBudget: 3,
      },
    }),
  );
  await page.route('**/ttb-commerce/v1/session', async (route) => {
    if (route.request().method() === 'POST') {
      expect(route.request().headers().cookie).toContain('fixture-mangos-sso=');
      expect(route.request().postData()).toBeNull();
      exchanges.push('verified-cookie-exchange');
      sessionExists = true;
    }
    await route.fulfill({
      json: sessionExists
        ? { authenticated: true, user, csrfToken: 'fixture-csrf' }
        : { authenticated: false },
    });
  });
  for (const path of ['/cart', '/orders?limit=20', '/inventory?limit=20']) {
    await page.route('**/ttb-commerce/v1' + path.replace('?', '\\?'), (route) =>
      route.fulfill({
        json:
          path === '/cart' ? { version: 0, lines: [], subtotalMinor: 0 } : { items: [], nextCursor: null },
      }),
    );
  }
  // Match owned collection reads without altering any financial endpoint.
  await page.route(/\/ttb-commerce\/v1\/(orders|inventory)\?/, (route) =>
    route.fulfill({ json: { items: [], nextCursor: null } }),
  );
  await page.goto('/');
  await expect(page).toHaveTitle('MANGOs · Gallery');
  await expect(page.getByRole('button', { name: 'SSO Browser QA', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'ประวัติรายการ', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'ประวัติรายการ', exact: true })).toBeVisible();
  await expect(page.getByText('ยังไม่มีประวัติรายการ', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'เข้าสู่ระบบ MANGOs', exact: true })).toHaveCount(0);
  await page.getByRole('combobox', { name: 'เปลี่ยนภาษา', exact: true }).selectOption('en');
  await expect(page.getByRole('heading', { name: 'Order history', exact: true })).toBeVisible();
  await page.screenshot({ path: resolve(evidence, 'sso-history-en.png') });
  await page.getByRole('button', { name: 'My items', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'My items', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await expect(page.getByRole('button', { name: 'SSO Browser QA', exact: true })).toBeVisible();
  expect(exchanges).toHaveLength(1);
  expect(errors).toEqual([]);
});

test('Thai and English preserve buyer inputs, quote, Mock QR, history and receipt with real Local API', async ({
  page,
}) => {
  const errors: string[] = [],
    bankCalls: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (/\/verify$|\/enquiry$|tau2904|ttbbank/.test(request.url())) bankCalls.push(request.url());
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'เข้าสู่ระบบ', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill('locale-qa-' + Date.now());
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await page.getByRole('button', { name: /^Mango Star — Digital/ }).click();
  await page.getByRole('button', { name: 'เพิ่มลงตะกร้า', exact: true }).click();
  await page.getByRole('button', { name: 'ดูตะกร้า', exact: true }).click();
  await page.getByRole('button', { name: 'ดำเนินการสั่งซื้อ', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อผู้ซื้อ', exact: true }).fill('Language QA');
  await page.getByRole('textbox', { name: 'อีเมล (ไม่บังคับ)' }).fill('qa@example.test');
  await page.getByRole('combobox', { name: 'เปลี่ยนภาษา' }).selectOption('en');
  await expect(page.getByRole('heading', { name: 'Review order', exact: true })).toBeVisible();
  await expect(page.getByRole('textbox', { name: 'Buyer name', exact: true })).toHaveValue('Language QA');
  await expect(page.getByRole('textbox', { name: 'Email (optional)' })).toHaveValue('qa@example.test');
  await page.getByRole('button', { name: 'Check total', exact: true }).click();
  await expect(page.getByText('The total has been verified by the system', { exact: true })).toBeVisible();
  await page.screenshot({ path: resolve(evidence, 'checkout-en.png') });
  await page.getByRole('combobox', { name: 'Language' }).selectOption('th');
  await expect(page.getByRole('textbox', { name: 'ชื่อผู้ซื้อ', exact: true })).toHaveValue('Language QA');
  await expect(page.getByText('ยอดชำระได้รับการตรวจสอบจากระบบ', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'เปลี่ยนภาษา' }).selectOption('en');
  await page.getByRole('button', { name: 'Generate payment QR', exact: true }).click();
  await expect(
    page.getByRole('img', { name: 'Mock QR for testing; no real money is received', exact: true }),
  ).toBeVisible();
  const number = await page.locator('.payment-reference>strong').innerText();
  const qr = await page.locator('.qr-frame img').getAttribute('src');
  await page.getByRole('combobox', { name: 'Language' }).selectOption('th');
  expect(await page.locator('.qr-frame img').getAttribute('src')).toBe(qr);
  await page.getByRole('combobox', { name: 'เปลี่ยนภาษา' }).selectOption('en');
  await page.getByText('Local test tools', { exact: true }).click();
  await page.getByRole('button', { name: 'Simulate payment (no real money)', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Order history', exact: true })).toBeVisible();
  const download = page.getByRole('button', { name: 'Download receipt', exact: true });
  await expect(async () => {
    if (await page.getByRole('button', { name: 'Refresh documents and delivery', exact: true }).isVisible())
      await page.getByRole('button', { name: 'Refresh documents and delivery', exact: true }).click();
    await expect(download).toBeVisible();
  }).toPass({ timeout: 40000, intervals: [1000, 2000] });
  const receiptEvent = page.waitForEvent('download');
  await download.click();
  const receipt = await receiptEvent,
    path = await receipt.path();
  expect((await readFile(path!)).subarray(0, 5).toString()).toBe('%PDF-');
  await page.screenshot({ path: resolve(evidence, 'history-en.png') });
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await page.getByRole('button', { name: 'Order history', exact: true }).click();
  await expect(page.getByText(number, { exact: true }).first()).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('combobox', { name: 'Language' }).selectOption('th');
  await expect(page.getByRole('heading', { name: 'ประวัติรายการ', exact: true })).toBeVisible();
  await page.screenshot({ path: resolve(evidence, 'history-th-mobile.png') });
  expect(await page.locator('.commerce-dialog').evaluate((e) => e.scrollWidth <= e.clientWidth + 1)).toBe(
    true,
  );
  expect(bankCalls).toEqual([]);
  expect(errors).toEqual([]);
});
