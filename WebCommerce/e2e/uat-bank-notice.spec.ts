import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';

test('UAT bank-only instructions remain readable in Thai and English and cannot bypass closed checkout', async ({ page }) => {
  const errors: string[] = [], payments: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (message.type() === 'error' || message.type() === 'warning') errors.push(message.text());
  });
  page.on('request', request => {
    if ((request.method() === 'POST' && /\/orders$|\/qr$|\/verify$/.test(request.url())) || /tau2904|ttbbank/.test(request.url())) payments.push(request.url());
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'เข้าสู่ระบบ', exact: true }).click();
  const buyer = 'bank-notice-' + Date.now();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill(buyer);
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await expect(page.getByRole('button', { name: buyer, exact: true })).toBeVisible();
  // Display fixture only. Every cart/quote remains on the isolated Local API; no bank access.
  await page.route('**/ttb-commerce/v1/storefront/config', route => route.fulfill({ json: {
    environment: 'Uat', paymentMode: 'Bank', paymentsReady: false, mockSimulation: false,
    currency: 'THB', gameUseReady: false, verificationDelaySeconds: 60, verificationBudget: 3,
  } }));
  await page.reload();
  await expect(page).toHaveTitle('MANGOs · Gallery');
  await page.getByRole('button', { name: /^Mango Star — Digital/ }).click();
  await page.getByRole('button', { name: 'เพิ่มลงตะกร้า', exact: true }).click();
  await page.getByRole('button', { name: 'ดูตะกร้า', exact: true }).click();
  await page.getByRole('button', { name: 'ดำเนินการสั่งซื้อ', exact: true }).click();
  await page.getByRole('textbox', { name: 'ชื่อผู้ซื้อ', exact: true }).fill('Bank notice QA');
  await page.getByRole('button', { name: 'ตรวจสอบยอดชำระ', exact: true }).click();
  await expect(page.getByText('UAT · ให้ TTB สแกนในวงทดสอบธนาคารเท่านั้น', { exact: true })).toBeVisible();
  await expect(page.getByText('กำลังเตรียมทดสอบรับชำระร่วมกับ TTB', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'สร้าง QR เพื่อชำระเงิน', exact: true })).toBeDisabled();
  const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-bank-notice');
  await mkdir(evidence, { recursive: true });
  await page.screenshot({ path: resolve(evidence, 'uat-bank-notice-th.png') });
  await page.getByRole('combobox', { name: 'เปลี่ยนภาษา' }).selectOption('en');
  await expect(page.getByText('UAT · TTB must scan using the bank test network', { exact: true })).toBeVisible();
  await expect(page.getByText('Preparing joint payment testing with TTB', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Generate payment QR', exact: true })).toBeDisabled();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByText('Preparing joint payment testing with TTB', { exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: resolve(evidence, 'uat-bank-notice-en-mobile.png') });
  expect(await page.locator('.commerce-dialog').evaluate(e => e.scrollWidth <= e.clientWidth + 1)).toBe(true);
  expect(errors).toEqual([]); expect(payments).toEqual([]);
});
