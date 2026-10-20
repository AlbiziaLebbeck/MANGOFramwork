import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { tmpdir } from 'node:os';

test('shop navigation is screen-centered and language stays beside install on desktop and mobile', async ({ page, request }) => {
  const config = await (await request.get('/ttb-commerce/v1/storefront/config')).json();
  expect(config).toMatchObject({ environment: 'Local', paymentMode: 'Mock', mockSimulation: true });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => {
    if (['error', 'warning'].includes(message.type())) errors.push(message.text());
  });
  await page.goto('/');
  await expect(page).toHaveTitle('MANGOs · Gallery');
  await page.getByRole('button', { name: 'เข้าสู่ระบบ', exact: true }).click();
  const buyer = 'header-layout-' + Date.now();
  await page.getByRole('textbox', { name: 'ชื่อบัญชีทดสอบ', exact: true }).fill(buyer);
  await page.getByRole('button', { name: 'เข้าสู่ระบบทดสอบ Local', exact: true }).click();
  await expect(page.getByRole('button', { name: buyer, exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'MANGOs สำรวจสินค้า', exact: true }).click();
  // Browser install availability is a platform event; never emulate payment or identity acceptance.
  await page.evaluate(() => {
    const event = new Event('beforeinstallprompt', { cancelable: true });
    Object.assign(event, { prompt: async () => {}, userChoice: Promise.resolve({ outcome: 'dismissed' }) });
    window.dispatchEvent(event);
  });
  await expect(page.getByRole('button', { name: 'ติดตั้งแอป', exact: true })).toBeVisible();
  const evidence = process.env.TTB_UI_EVIDENCE ?? resolve(tmpdir(), 'mangos-ttb-header');
  await mkdir(evidence, { recursive: true });
  for (const width of [1920, 1360, 1024, 800, 390, 320]) {
    await page.setViewportSize({ width, height: width < 760 ? 844 : 900 });
    for (const language of ['en', 'th']) {
      await page.locator('.language-switch').selectOption(language);
      await expect(page.locator('html')).toHaveAttribute('lang', language);
      await page.evaluate(() => document.fonts.ready);
      const geometry = await page.locator('.commerce-header').evaluate(header => {
        const rect = (selector: string) => {
          const { left, right, top, bottom, width } = header.querySelector(selector)!.getBoundingClientRect();
          return { left, right, top, bottom, width };
        };
        return { brand: rect('.brand'), nav: rect('nav'), actions: rect('.header-actions'),
          language: rect('.language-switch'), install: rect('.language-switch + button'),
          viewport: window.innerWidth, overflow: document.documentElement.scrollWidth > window.innerWidth };
      });
      expect(geometry.overflow).toBe(false);
      expect(geometry.language.left).toBeGreaterThanOrEqual(geometry.brand.right + 2);
      expect(geometry.actions.right).toBeLessThanOrEqual(width);
      expect(geometry.install.left - geometry.language.right).toBeGreaterThanOrEqual(0);
      expect(geometry.install.left - geometry.language.right).toBeLessThanOrEqual(8);
      if (width > 760) {
        expect(Math.abs((geometry.nav.left + geometry.nav.right) / 2 - width / 2)).toBeLessThanOrEqual(1);
        expect(geometry.language.left).toBeGreaterThanOrEqual(geometry.nav.right + 2);
      } else {
        expect(geometry.nav.top).toBeGreaterThan(geometry.actions.bottom);
      }
      if ([1920, 390].includes(width))
        await page.screenshot({ path: resolve(evidence, `header-${width}-${language}.png`) });
    }
  }
  await page.getByRole('button', { name: 'ประวัติรายการ', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'ประวัติรายการ', exact: true })).toBeVisible();
  expect(errors).toEqual([]);
});
