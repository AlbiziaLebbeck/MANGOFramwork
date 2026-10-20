import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
const root = '/ttb-commerce/v1',
  evidence = resolve('../.artifacts/admin-qa');
test.use({ actionTimeout: 15000 });
async function login(page: Page, subject: string) {
  await page.goto('/');
  await page.getByRole('button', { name: 'เข้าสู่ระบบ', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill(subject);
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await page.getByRole('button', { name: 'จัดการร้านค้า', exact: true }).click();
  await expect(page.getByRole('navigation', { name: 'เมนูจัดการร้านค้า' })).toBeVisible();
}
async function shot(page: Page, name: string) {
  await expect(page.locator('.commerce-progress')).toHaveCount(0);
  const close = page.getByRole('button', { name: 'ปิดข้อความ', exact: true });
  if (await close.isVisible()) await close.click();
  const region = page.getByRole('region', {
    name: name.startsWith('goods') ? 'ตรวจสินค้าและสิทธิ์หลังคืนเงิน' : 'หลักฐานคืนเงินภายนอก',
    exact: true,
  });
  await region.evaluate((e) => e.scrollIntoView({ block: 'start' }));
  expect(await page.locator('.commerce-dialog').evaluate((e) => e.scrollWidth <= e.clientWidth + 1)).toBe(
    true,
  );
  await page.screenshot({ path: resolve(evidence, name), fullPage: true });
}
async function paidMixed(buyer: APIRequestContext) {
  const config = await (await buyer.get(root + '/storefront/config')).json();
  expect(config.environment).toBe('Local');
  expect(config.mockSimulation).toBe(true);
  const session = await (
    await buyer.post(root + '/session/local', { data: { subject: 'qa-refund-' + Date.now() } })
  ).json();
  const headers = { 'X-CSRF-Token': session.csrfToken };
  const catalog = await (await buyer.get(root + '/catalog/products?limit=100')).json();
  const lamp = catalog.items.find((x: any) => x.slug === 'mango-orbit-lamp');
  const digital = catalog.items.find((x: any) => x.slug === 'mango-digital-star');
  expect(lamp).toBeTruthy();
  expect(digital).toBeTruthy();
  let cart = await (await buyer.get(root + '/cart')).json();
  for (const product of [lamp, digital]) {
    const updated = await buyer.put(root + '/cart/lines/' + product.variants[0].id, {
      headers,
      data: { quantity: 2, expectedVersion: cart.version },
    });
    expect(updated.ok()).toBe(true);
    cart = await updated.json();
  }
  const quoted = await buyer.post(root + '/checkout/quote', {
    headers,
    data: {
      cartVersion: cart.version,
      buyer: { name: 'ผู้ทดสอบคืนสินค้าบางส่วน', phone: '0812345678' },
      delivery: { method: 'pickup' },
    },
  });
  expect(quoted.ok()).toBe(true);
  const quote = await quoted.json();
  const created = await buyer.post(root + '/orders', {
    headers: { ...headers, 'Idempotency-Key': randomUUID() },
    data: { quoteId: quote.id },
  });
  expect(created.ok()).toBe(true);
  const order = await created.json();
  expect(order.lines).toHaveLength(2);
  expect((await buyer.post(root + `/orders/${order.id}/qr`, { headers })).ok()).toBe(true);
  expect(
    (
      await buyer.post(root + `/orders/${order.id}/mock-pay`, { headers, data: { requestKey: randomUUID() } })
    ).ok(),
  ).toBe(true);
  return order;
}

test('two officers attest a partial external refund and explicitly return only selected goods; private proof stays out of shopper history', async ({
  page,
  browser,
  playwright,
}) => {
  test.setTimeout(180000);
  await mkdir(evidence, { recursive: true });
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  const buyer = await playwright.request.newContext({
    baseURL: 'http://127.0.0.1:5176',
    extraHTTPHeaders: { Origin: 'http://127.0.0.1:5176' },
  });
  const checkerContext = await browser.newContext({ viewport: { width: 1536, height: 1024 } }),
    checker = await checkerContext.newPage();
  checker.on('pageerror', (e) => errors.push(e.message));
  try {
    const order = await paidMixed(buyer);
    await login(page, 'demo-admin');
    await page.locator('.admin-table-row').filter({ hasText: order.number }).click();
    await page.getByRole('button', { name: 'เปิดเคสคืนเงินภายนอก', exact: true }).click();
    const create = page.getByRole('region', { name: 'เปิดเคสคืนเงินภายนอก', exact: true });
    await create.getByLabel('ยอดคืนเงินภายนอก (บาท)', { exact: true }).fill('1.00');
    await create
      .getByLabel('เหตุผลเปิดเคส', { exact: true })
      .fill('Local partial refund of one physical and one digital unit');
    await create.getByRole('checkbox').check();
    const operationIds: string[] = [];
    let lost = false;
    await page.route('**/admin/bank-transactions/*/manual-refunds', async (route) => {
      operationIds.push(route.request().postDataJSON().operationId);
      if (!lost) {
        lost = true;
        const response = await route.fetch();
        expect(response.ok()).toBe(true);
        await route.abort('failed');
      } else await route.continue();
    });
    await create.getByRole('button', { name: 'สร้างเคสตรวจสอบ', exact: true }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await create.getByRole('button', { name: 'สร้างเคสตรวจสอบ', exact: true }).click();
    expect(operationIds).toHaveLength(2);
    expect(new Set(operationIds).size).toBe(1);
    await page.unroute('**/admin/bank-transactions/*/manual-refunds');
    const manual = page.getByRole('region', { name: 'หลักฐานคืนเงินภายนอก', exact: true });
    await expect(manual).toBeVisible();
    const filename = 'proof-' + Date.now() + '.txt';
    const file = resolve(evidence, filename);
    await writeFile(file, 'Synthetic Local external-transfer proof; no money sent.');
    await manual.locator('input[type=file]').setInputFiles(file);
    await manual.getByRole('checkbox', { name: filename, exact: true }).check();
    await manual.getByLabel('เลขอ้างอิงการโอนภายนอก', { exact: true }).fill('LOCAL-SYNTHETIC-TRANSFER');
    await manual
      .getByLabel('วันและเวลาโอนภายนอก (เวลาของอุปกรณ์)', { exact: true })
      .fill(new Date(Date.now() - new Date().getTimezoneOffset() * 60000 - 1000).toISOString().slice(0, 19));
    await manual
      .getByLabel('เหตุผลตรวจหลักฐาน', { exact: true })
      .fill('Synthetic transfer declaration for a second officer');
    await manual.getByRole('checkbox', { name: /ยืนยันตรวจข้อมูลและหลักฐาน/ }).check();
    await manual.getByRole('button', { name: 'ส่งหลักฐานให้เจ้าหน้าที่อีกคนตรวจ', exact: true }).click();
    await expect(
      manual.getByRole('button', { name: 'อนุมัติหลักฐานการคืนเงินภายนอก', exact: true }),
    ).toBeDisabled();
    await shot(page, 'manual-submitted-desktop.png');
    await page.setViewportSize({ width: 390, height: 844 });
    await shot(page, 'manual-submitted-mobile.png');
    await login(checker, 'demo-finance');
    await checker.getByRole('button', { name: 'การเงิน', exact: true }).click();
    await checker
      .locator('.admin-table-row')
      .filter({ hasText: order.number })
      .filter({ hasText: 'คืนเงินภายนอก' })
      .click();
    const approval = checker.getByRole('region', { name: 'หลักฐานคืนเงินภายนอก', exact: true });
    await approval
      .getByLabel('เหตุผลตรวจหลักฐาน', { exact: true })
      .fill('Second officer checked amount and private proof');
    await approval.getByRole('checkbox', { name: /ยืนยันตรวจข้อมูลและหลักฐาน/ }).check();
    await shot(checker, 'manual-approval-desktop.png');
    await approval.getByRole('button', { name: 'อนุมัติหลักฐานการคืนเงินภายนอก', exact: true }).click();
    await expect(
      approval.getByRole('heading', { name: 'คืนเงินภายนอก · อนุมัติหลักฐานแล้ว', exact: true }),
    ).toBeVisible();
    const activity = await (await buyer.get(root + `/orders/${order.id}/activity`)).json();
    expect(activity.order.paymentState).toBe('Paid');
    expect(activity.refund.amountMinor).toBe('100');
    expect(activity.refund.goodsReviewPending).toBe(true);
    expect(JSON.stringify(activity)).not.toContain(filename);
    expect(JSON.stringify(activity)).not.toContain('LOCAL-SYNTHETIC-TRANSFER');
    const privateDetail = await (
      await checker.request.get(root + '/admin/reconciliation?orderId=' + order.id)
    ).json();
    const manualCase = privateDetail.items.find((c: any) => c.kind === 'ManualRefund');
    const manualDetail = await (
      await checker.request.get(root + '/admin/reconciliation/' + manualCase.id)
    ).json();
    expect(
      (
        await buyer.get(
          root + '/admin/finance-evidence/' + manualDetail.manualRefund.files[0].id + '/download',
        )
      ).status(),
    ).toBe(403);
    await checker.getByRole('button', { name: 'การเงิน', exact: true }).click();
    await checker
      .locator('.admin-table-row')
      .filter({ hasText: order.number })
      .filter({ hasText: 'ตรวจสินค้าหลังคืนเงินภายนอก' })
      .click();
    await checker.getByRole('button', { name: 'ตรวจสินค้าและสิทธิ์หลังคืนเงิน', exact: true }).click();
    const goods = checker.getByRole('region', { name: 'ตรวจสินค้าและสิทธิ์หลังคืนเงิน', exact: true });
    const digitalUnits = goods.locator('.admin-unit-check');
    await expect(digitalUnits).toHaveCount(2);
    await digitalUnits.nth(0).getByRole('checkbox').check();
    await goods
      .getByRole('spinbutton', { name: /^รับคืนเข้าสต็อก:/ })
      .first()
      .fill('1');
    await goods
      .getByLabel('เหตุผลจัดการสินค้า', { exact: true })
      .fill('One returned physical unit counted; revoke one digital unit');
    await goods.getByRole('checkbox', { name: 'ยืนยันจำนวนรับคืนและสิทธิ์ที่เลือก', exact: true }).check();
    await shot(checker, 'goods-review-desktop.png');
    await checker.setViewportSize({ width: 390, height: 844 });
    await shot(checker, 'goods-review-mobile.png');
    await goods.getByRole('button', { name: 'ยืนยันการจัดการสินค้า', exact: true }).click();
    await expect(goods).toHaveCount(0);
    const after = await (await buyer.get(root + `/orders/${order.id}/activity`)).json();
    expect(after.refund.goodsReviewPending).toBe(false);
    expect(after.refund.returnedLines).toHaveLength(1);
    expect(after.refund.returnedLines[0].quantity).toBe(1);
    await expect(async () => {
      const inventory = await (await buyer.get(root + '/inventory')).json();
      const units = inventory.items.filter((x: any) => x.orderId === order.id);
      expect(units.filter((x: any) => x.state === 'Revoked')).toHaveLength(1);
      expect(units.filter((x: any) => x.state === 'Active')).toHaveLength(1);
    }).toPass({ timeout: 35000 });
    const ownerContext = await browser.newContext({
      storageState: await buyer.storageState(),
      viewport: { width: 390, height: 844 },
    });
    try {
      const owner = await ownerContext.newPage();
      await owner.goto('/');
      await expect(owner.getByRole('button', { name: 'ออกจากระบบซื้อสินค้า', exact: true })).toBeVisible();
      await expect(owner.locator('.commerce-progress')).toHaveCount(0);
      await owner.getByRole('button', { name: 'ประวัติรายการ', exact: true }).click();
      await expect(owner.getByRole('heading', { name: 'ประวัติรายการ', exact: true })).toBeVisible();
      await owner.locator('.history-row').filter({ hasText: order.number }).click();
      const refund = owner.getByRole('region', { name: 'ประวัติคืนเงินภายนอก', exact: true });
      await expect(refund).toContainText('ยอดคืนสะสม ฿1.00');
      await expect(refund).toContainText('รับคืนเข้าสต็อก 1 หน่วย');
      await expect(owner.locator('body')).not.toContainText(filename);
      await refund.scrollIntoViewIfNeeded();
      await owner.screenshot({
        path: resolve(evidence, 'shopper-partial-refund-mobile.png'),
        fullPage: true,
      });
    } finally {
      await ownerContext.close();
    }
    expect(errors).toEqual([]);
  } finally {
    await buyer.dispose();
    await checkerContext.close();
  }
});
