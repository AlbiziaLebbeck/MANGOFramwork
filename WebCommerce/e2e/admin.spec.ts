import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const root = '/ttb-commerce/v1',
  evidence = resolve('../.artifacts/admin-qa');
test.use({ actionTimeout: 15000 });
test.beforeAll(async ({ request }) => {
  await mkdir(evidence, { recursive: true });
  const config = await (await request.get(root + '/storefront/config')).json();
  expect(config.environment).toBe('Local');
  expect(config.paymentMode).toBe('Mock');
  expect(config.mockSimulation).toBe(true);
});
async function login(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'เข้าสู่ระบบ', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill('demo-admin');
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await page.getByRole('button', { name: 'จัดการร้านค้า', exact: true }).click();
  await expect(page.getByRole('navigation', { name: 'เมนูจัดการร้านค้า' })).toBeVisible();
}
async function getCatalog(request: APIRequestContext) {
  return (await (await request.get(root + '/admin/catalog')).json()) as any[];
}
async function screenshot(page: Page, name: string) {
  await expect(page.locator('.commerce-progress')).toHaveCount(0);
  const dismiss = page.getByRole('button', { name: 'ปิดข้อความ', exact: true });
  if (await dismiss.isVisible()) await dismiss.click();
  await page.locator('.screen-admin').evaluate((element) => {
    element.scrollTop = 0;
  });
  expect(await page.locator('.commerce-dialog').evaluate((e) => e.scrollWidth <= e.clientWidth + 1)).toBe(
    true,
  );
  await page.screenshot({ path: resolve(evidence, name), fullPage: true });
}

test('catalogue editing, publication and a lost stock response use the same audited adjustment once', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await login(page);
  await page.getByRole('button', { name: 'สินค้า', exact: true }).click();
  await page.getByRole('button', { name: 'เพิ่มสินค้า', exact: true }).click();
  const suffix = Date.now().toString();
  const name = 'Orbit Lamp · QA ' + suffix,
    sku = 'QA-LAMP-' + suffix;
  await page.getByLabel('ชื่อสินค้า', { exact: true }).fill(name);
  await page.getByLabel('รหัส URL สินค้า', { exact: true }).fill('qa-lamp-' + suffix);
  await page.getByLabel('รายละเอียด', { exact: true }).fill('สินค้าสำหรับทดสอบหลังบ้าน Local เท่านั้น');
  await page.getByLabel('Model key', { exact: true }).fill('qa-lamp-' + suffix);
  await page.getByRole('button', { name: 'บันทึกการแก้ไข', exact: true }).click();
  await expect(page.getByLabel('SKU', { exact: true })).toBeVisible();
  await page.getByLabel('SKU', { exact: true }).fill(sku);
  await page.getByLabel('ชื่อรุ่น', { exact: true }).fill('รุ่นทดสอบ');
  await page.getByLabel('ราคา (บาท)', { exact: true }).fill('129.50');
  await page.getByRole('button', { name: 'บันทึกรุ่นสินค้า', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'เลือกรุ่นสินค้า', exact: true })).toContainText(sku);
  await page.getByText('ปรับสต็อก', { exact: true }).click();
  await page.getByLabel('เพิ่ม / ลดจำนวน', { exact: true }).fill('3');
  await page.getByLabel('เหตุผลการปรับสต็อก', { exact: true }).fill('Local QA counted three units');
  const operationIds: string[] = [];
  let lost = false;
  await page.route('**/admin/variants/*/stock', async (route) => {
    operationIds.push(route.request().postDataJSON().operationId);
    if (!lost) {
      lost = true;
      const response = await route.fetch();
      expect(response.ok()).toBe(true);
      await route.abort('failed');
    } else await route.continue();
  });
  await page.getByRole('button', { name: 'ยืนยันปรับสต็อก', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'ยืนยันปรับสต็อก', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'ยืนยันปรับสต็อก', exact: true }).click();
  await expect(page.getByLabel('จำนวนพร้อมขาย', { exact: true })).toHaveValue('3');
  expect(operationIds).toHaveLength(2);
  expect(new Set(operationIds).size).toBe(1);
  const entry = (await getCatalog(page.request)).find((x) => x.product.name === name);
  expect(entry.variants[0].onHand).toBe(3);
  await page.unroute('**/admin/variants/*/stock');
  await page.getByRole('button', { name: 'เผยแพร่สินค้า', exact: true }).click();
  await expect(page.getByRole('button', { name: 'พักเผยแพร่สินค้า', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'พักเผยแพร่สินค้า', exact: true }).click();
  await expect(page.getByRole('button', { name: 'เผยแพร่สินค้า', exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'การเผยแพร่สินค้า', exact: true }).selectOption('published');
  await page.getByRole('button', { name: /^Orbit Lamp สินค้าจริง/ }).click();
  await screenshot(page, 'catalog-desktop.png');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('heading', { name: 'แก้ไขสินค้า', exact: true }).scrollIntoViewIfNeeded();
  await screenshot(page, 'catalog-mobile.png');
  await page.setViewportSize({ width: 1536, height: 1024 });
  await page.getByRole('button', { name: 'เพิ่มสินค้า', exact: true }).click();
  await page.getByLabel('ชื่อสินค้า', { exact: true }).fill('คู่มือโมเดล · QA ' + suffix);
  await page.getByLabel('รหัส URL สินค้า', { exact: true }).fill('qa-guide-' + suffix);
  await page.getByRole('combobox', { name: 'ประเภทสินค้า', exact: true }).selectOption('digital');
  await page.getByLabel('Model key', { exact: true }).fill('qa-guide-' + suffix);
  await page.getByRole('button', { name: 'บันทึกการแก้ไข', exact: true }).click();
  await page.getByLabel('SKU', { exact: true }).fill('QA-GUIDE-' + suffix);
  await page.getByLabel('ชื่อรุ่น', { exact: true }).fill('ไฟล์ทดสอบ');
  await page.getByLabel('ราคา (บาท)', { exact: true }).fill('49.00');
  await page.getByRole('combobox', { name: 'การส่งมอบดิจิทัล', exact: true }).selectOption('download');
  const guidePath = resolve(evidence, 'guide-' + suffix + '.txt');
  await writeFile(guidePath, 'Synthetic Local QA file. No private customer or bank data.');
  await page.getByLabel('เพิ่มไฟล์ส่วนตัว', { exact: true }).setInputFiles(guidePath);
  await expect(page.getByRole('combobox', { name: 'ไฟล์สำหรับผู้ซื้อ', exact: true })).toHaveValue(
    /[a-f0-9]{32}/,
  );
  await page.getByRole('button', { name: 'บันทึกรุ่นสินค้า', exact: true }).click();
  await expect(page.locator('.commerce-progress')).toHaveCount(0);
  const digital = (await getCatalog(page.request)).find((x) => x.product.slug === 'qa-guide-' + suffix);
  expect(digital.product.published).toBe(false);
  expect(digital.variants[0].digitalKind).toBe('download');
  expect(digital.variants[0].digitalAssetKey).toBeTruthy();
  expect(errors).toEqual([]);
});

test('operators deliver a Mock purchase, explicitly refund it, record a case once, and clear private UI on account change', async ({
  page,
  playwright,
}) => {
  test.setTimeout(180000);
  const errors: string[] = [];
  let bankChecks = 0;
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('request', (r) => {
    if (/\/(verify|enquiry)(\?|$)/.test(r.url())) bankChecks++;
  });
  const buyer = await playwright.request.newContext({
    baseURL: 'http://127.0.0.1:5176',
    extraHTTPHeaders: { Origin: 'http://127.0.0.1:5176' },
  });
  let order: any;
  try {
    const session = await (
      await buyer.post(root + '/session/local', { data: { subject: 'qa-admin-buyer-' + Date.now() } })
    ).json();
    const mutationHeaders = { 'X-CSRF-Token': session.csrfToken };
    const catalog = await (await buyer.get(root + '/catalog/products?limit=24')).json();
    const lamp = catalog.items.find((x: any) => x.slug === 'mango-orbit-lamp');
    let cart = await (await buyer.get(root + '/cart')).json();
    cart = await (
      await buyer.put(root + '/cart/lines/' + lamp.variants[0].id, {
        headers: mutationHeaders,
        data: { quantity: 1, expectedVersion: cart.version },
      })
    ).json();
    const quote = await (
      await buyer.post(root + '/checkout/quote', {
        headers: mutationHeaders,
        data: {
          cartVersion: cart.version,
          buyer: { name: 'ผู้ทดสอบการจัดเตรียม', phone: '0812345678' },
          delivery: { method: 'pickup' },
        },
      })
    ).json();
    const created = await buyer.post(root + '/orders', {
      headers: { ...mutationHeaders, 'Idempotency-Key': randomUUID() },
      data: { quoteId: quote.id },
    });
    expect(created.ok()).toBe(true);
    order = await created.json();
    expect((await buyer.post(root + `/orders/${order.id}/qr`, { headers: mutationHeaders })).ok()).toBe(true);
    expect(
      (
        await buyer.post(root + `/orders/${order.id}/mock-pay`, {
          headers: mutationHeaders,
          data: { requestKey: randomUUID() },
        })
      ).ok(),
    ).toBe(true);
  } finally {
    await buyer.dispose();
  }
  await login(page);
  await page.locator('.admin-table-row').filter({ hasText: order.number }).click();
  await expect(async () => {
    await page.getByRole('button', { name: 'อัปเดตการส่งมอบและเอกสาร', exact: true }).click();
    await expect(page.getByRole('button', { name: 'เริ่มจัดเตรียม', exact: true })).toBeVisible();
  }).toPass({ timeout: 35000, intervals: [1000, 2000] });
  await page.getByLabel('หมายเหตุการจัดเตรียม', { exact: true }).fill('Local QA verified pickup');
  await page.getByRole('button', { name: 'เริ่มจัดเตรียม', exact: true }).click();
  await expect(page.getByRole('button', { name: 'พร้อมรับสินค้า', exact: true })).toBeVisible();
  await screenshot(page, 'orders-desktop.png');
  await page.getByRole('button', { name: 'พร้อมรับสินค้า', exact: true }).click();
  await page.getByRole('button', { name: 'ยืนยันส่งมอบ', exact: true }).click();
  await expect(page.getByText('ส่งมอบแล้ว', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'ตรวจเงื่อนไขคืนเงิน', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'ตรวจเงื่อนไขคืนเงิน', exact: true })).toBeVisible();
  await page
    .getByLabel('เหตุผลการคืนเงิน', { exact: true })
    .fill('Local QA full return of synthetic purchase');
  await page
    .getByRole('checkbox', {
      name: 'ตรวจการส่งมอบ / ดาวน์โหลด / ใช้งาน และนโยบายคืนสินค้าที่อนุมัติแล้ว',
      exact: true,
    })
    .check();
  await page.getByRole('checkbox', { name: /ยืนยันคืนยอดนี้เต็มจำนวน/ }).check();
  await page.getByRole('button', { name: 'ยืนยันคืนเงินเต็มจำนวน', exact: true }).click();
  await expect(async () => {
    await page.getByRole('button', { name: 'อัปเดตผลจากหลังบ้าน', exact: true }).click();
    await expect(page.getByText('ธนาคารยืนยันยกเลิก', { exact: true })).toBeVisible();
  }).toPass({ timeout: 35000, intervals: [1000, 2000] });
  await page.getByRole('button', { name: 'ดูเคสการเงิน', exact: true }).click();
  await expect(page.getByText('เคสของคำสั่งซื้อที่เลือก', { exact: true })).toBeVisible();
  const caseRow = page
    .locator('.admin-table-row')
    .filter({ hasText: order.number })
    .filter({ hasText: 'ตรวจการคืนสินค้า' });
  await expect(caseRow).toHaveCount(1);
  await caseRow.click();
  await expect(page.getByLabel('เหตุผล / บันทึก', { exact: true })).toBeVisible();
  const note = 'Local QA note with uncertain response ' + Date.now();
  await page.getByLabel('เหตุผล / บันทึก', { exact: true }).fill(note);
  const keys: string[] = [];
  let lost = false;
  await page.route('**/admin/reconciliation/*/actions', async (route) => {
    keys.push(route.request().postDataJSON().operationId);
    if (!lost) {
      lost = true;
      expect((await route.fetch()).ok()).toBe(true);
      await route.abort('failed');
    } else await route.continue();
  });
  await page.getByRole('button', { name: 'บันทึกหมายเหตุ', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'บันทึกหมายเหตุ', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'บันทึกหมายเหตุ', exact: true }).click();
  await expect(page.locator('.admin-timeline').getByText(note, { exact: true })).toHaveCount(1);
  expect(new Set(keys).size).toBe(1);
  expect(keys).toHaveLength(2);
  await page.unroute('**/admin/reconciliation/*/actions');
  await screenshot(page, 'finance-desktop.png');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('heading', { name: 'ตรวจการคืนสินค้า', exact: true }).scrollIntoViewIfNeeded();
  await screenshot(page, 'finance-mobile.png');
  await expect(page.getByRole('button', { name: 'คิวเคสการเงิน', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'คิวเคสการเงิน', exact: true }).click();
  await page.getByRole('button', { name: 'รายงาน', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'ยังไม่เปิดรับไฟล์ธนาคาร', exact: true })).toBeVisible();
  await screenshot(page, 'reports-mobile-unconfigured.png');
  await page.setViewportSize({ width: 1536, height: 1024 });
  await screenshot(page, 'reports-desktop-unconfigured.png');
  await page.getByRole('button', { name: 'กลับหน้าร้าน', exact: true }).click();
  await page.getByRole('button', { name: 'demo-admin', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill('qa-viewer-' + Date.now());
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await expect(page.getByRole('button', { name: 'จัดการร้านค้า', exact: true })).toHaveCount(0);
  await expect(page.getByText(note, { exact: true })).toHaveCount(0);
  expect(bankChecks).toBe(0);
  expect(errors).toEqual([]);
  await writeFile(
    resolve(evidence, 'local-fixture.json'),
    JSON.stringify({ mode: 'Local+Mock', orderId: order.id, orderNumber: order.number }),
  );
});

test('report inspector renders synthetic metadata and never treats staged rows as a bank settlement confirmation', async ({
  page,
}) => {
  // Read-only visual fixture. No file import, bank transport or financial mutation is replaced by a fake success.
  const id = '11111111-2222-4333-8444-555555555555',
    second = '11111111-2222-4333-8444-666666666666';
  let mutations = 0;
  page.on('request', (r) => {
    if (r.method() !== 'GET' && r.url().includes('/admin/reports')) mutations++;
  });
  const rows = [
    {
      id,
      fileName: 'BPQR_ISO_261018.TXT',
      fileDate: '2026-10-18',
      kind: 'iso',
      revision: 1,
      state: 'Complete',
      failureCode: null,
      encryptedSha256: 'a'.repeat(64),
      plaintextSha256: 'b'.repeat(64),
      replayOfId: null,
      createdAt: '2026-10-19T00:00:00Z',
      completedAt: '2026-10-19T00:01:00Z',
    },
    {
      id: second,
      fileName: 'BPQR_ISO_261017.TXT',
      fileDate: '2026-10-17',
      kind: 'iso',
      revision: 2,
      state: 'Quarantined',
      failureCode: 'report_totals_mismatch',
      encryptedSha256: 'c'.repeat(64),
      plaintextSha256: null,
      replayOfId: null,
      createdAt: '2026-10-19T00:00:00Z',
      completedAt: null,
    },
  ];
  await page.route('**/admin/finance/summary', (route) =>
    route.fulfill({
      json: {
        environment: 'Local',
        paymentMode: 'Mock',
        paymentsReady: true,
        openCases: 1,
        unknownVoids: 0,
        reportsState: 'Configured',
        waitingFiles: 1,
        voidsAvailable: true,
      },
    }),
  );
  await page.route('**/admin/reports', (route) => route.fulfill({ json: rows }));
  await page.route('**/admin/reports/*', (route) => {
    const report = route.request().url().endsWith(id) ? rows[0] : rows[1];
    return route.fulfill({
      json: {
        report: {
          ...report,
          totalsJson: JSON.stringify({
            creditCount: 3,
            creditMinor: 100000,
            debitCount: 1,
            debitMinor: 19900,
          }),
          comparisonJson: JSON.stringify({
            scope: 'CalendarDay',
            absentCount: 0,
            unknownOriginalTimeAcrossLedger: 1,
            timeAttributionComplete: false,
            syntheticVisualFixture: true,
          }),
          parserProfileJson: '{"synthetic":true}',
          appliedProfileJson: '{"synthetic":true}',
        },
        outcomes: [
          { outcome: 'MatchedCredit', count: 2 },
          { outcome: 'ReportUnallocatedCredit', count: 1 },
        ],
      },
    });
  });
  await login(page);
  await page.getByRole('button', { name: 'รายงาน', exact: true }).click();
  await page.locator('.admin-table-row').filter({ hasText: rows[0].fileName }).click();
  await expect(page.getByRole('button', { name: 'ส่งตรวจสอบไฟล์เดิม', exact: true })).toBeDisabled();
  await expect(
    page.getByText('การผ่านรูปแบบไฟล์และยอดรวม ไม่ใช่การยืนยันว่าเทียบรายการรับเงินครบแล้ว', { exact: true }),
  ).toBeVisible();
  await screenshot(page, 'reports-desktop-fixture.png');
  await page.locator('.admin-table-row').filter({ hasText: rows[1].fileName }).click();
  await page
    .getByLabel('เหตุผลที่ตรวจสอบซ้ำ', { exact: true })
    .fill('Synthetic visual fixture — do not submit');
  await expect(page.getByRole('button', { name: 'ส่งตรวจสอบไฟล์เดิม', exact: true })).toBeEnabled();
  expect(mutations).toBe(0);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('heading', { name: rows[1].fileName, exact: true }).scrollIntoViewIfNeeded();
  await screenshot(page, 'reports-mobile-fixture.png');
});
