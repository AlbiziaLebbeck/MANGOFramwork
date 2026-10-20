import { test, expect, type Page } from '@playwright/test';

const evidence = 'C:/Users/PC/.codex/visualizations/2026/10/17/01a14abb-1984-7073-9312-25b2c6e8f444';
async function openProfile(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'เข้าสู่ระบบ', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill('demo-admin');
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await page.getByRole('button', { name: 'จัดการร้านค้า', exact: true }).click();
  await page.getByRole('button', { name: 'ข้อมูลบริษัท', exact: true }).click();
  await expect(page.getByLabel('ชื่อบริษัท', { exact: true })).toBeVisible();
}
async function clearChrome(page: Page) {
  const header = (await page.locator('.commerce-header').boundingBox())!;
  const dialog = (await page.locator('.commerce-dialog').boundingBox())!;
  expect(dialog.y).toBeGreaterThanOrEqual(header.y + header.height - 1);
  const message = page.locator('.commerce-toast');
  const toast = await message.count() ? await message.boundingBox() : null;
  if (toast) expect(dialog.y + dialog.height).toBeLessThanOrEqual(toast.y - 1);
  expect(await page.locator('.commerce-dialog').evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
}

test('company test fields persist from desktop to mobile and notices do not cover the editor', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
  await openProfile(page);
  await page.getByLabel('ชื่อบริษัท', { exact: true }).fill('บริษัท MANGOs ทดสอบ จำกัด (ข้อมูลจำลอง)');
  await page.getByLabel('เลขประจำตัวผู้เสียภาษี (ทดสอบ)', { exact: true }).fill('0000000000000');
  await page.getByLabel('สาขา', { exact: true }).fill('สำนักงานใหญ่ (ทดสอบ)');
  await page.getByRole('textbox', { name: 'ที่อยู่บริษัท', exact: true }).fill('เลขที่ 1 ถนนทดสอบ แขวงทดสอบ เขตทดสอบ กรุงเทพมหานคร 10000 (ที่อยู่จำลอง)');
  await page.getByLabel('ข้อมูลติดต่อ', { exact: true }).fill('test@example.invalid');
  await page.getByRole('button', { name: 'บันทึกข้อมูลบริษัททดสอบ', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'บันทึกข้อมูลบริษัททดสอบแล้ว' })).toBeVisible();
  await clearChrome(page);
  await page.screenshot({ path: `${evidence}/ttb-company-desktop.png` });
  await page.reload();
  await page.getByRole('button', { name: 'จัดการร้านค้า', exact: true }).click();
  await page.getByRole('button', { name: 'ข้อมูลบริษัท', exact: true }).click();
  await expect(page.getByLabel('ชื่อบริษัท', { exact: true })).toHaveValue('บริษัท MANGOs ทดสอบ จำกัด (ข้อมูลจำลอง)');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('button', { name: 'บันทึกข้อมูลบริษัททดสอบ', exact: true })).toBeVisible();
  await clearChrome(page);
  await page.screenshot({ path: `${evidence}/ttb-company-mobile.png` });
  const row = await page.request.get('/ttb-commerce/v1/admin/issuer-profile');
  const profile = await row.json(); expect(profile.approved).toBe(false); expect(profile.editable).toBe(true);
  const catalog = await (await page.request.get('/ttb-commerce/v1/catalog/products')).json();
  const prices = catalog.items.filter((product: { slug: string }) => ['mango-orbit-lamp', 'mango-sculpture', 'mango-digital-star'].includes(product.slug))
    .flatMap((product: { variants: { priceMinor: number }[] }) => product.variants.map(variant => variant.priceMinor));
  expect(prices.sort((a: number, b: number) => a - b)).toEqual([100, 1000, 9900]);
  expect(errors).toEqual([]);
});
