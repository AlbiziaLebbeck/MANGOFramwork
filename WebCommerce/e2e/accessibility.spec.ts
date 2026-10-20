import { test, expect } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';

test('keyboard focus stays inside the product dialog and returns to the selected product after Escape', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  const product = page.getByRole('button', { name: /Orbit Lamp Orbit Lamp/ });
  await product.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'ชมสินค้า', exact: true });
  await expect(dialog.getByRole('heading', { name: 'Orbit Lamp', exact: true })).toBeVisible();
  await expect(dialog).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.getByRole('button', { name: 'ดูตะกร้า', exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'ปิดหน้าต่าง', exact: true })).toBeFocused();
  for (let index = 0; index < 14; index++) {
    await page.keyboard.press('Tab');
    expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden(); await expect(product).toBeFocused();
  expect(errors).toEqual([]);
});

test('product fallback and reachable controls fit portrait, landscape and narrow viewports', async ({ page }) => {
  await mkdir(resolve('../.artifacts/ui-qa'), { recursive: true });
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.setViewportSize({ width: 390, height: 844 }); await page.goto('/');
  await page.getByRole('button', { name: /Orbit Lamp Orbit Lamp/ }).click();
  const dialog = page.getByRole('dialog', { name: 'ชมสินค้า', exact: true });
  await expect(dialog.getByRole('heading', { name: 'Orbit Lamp', exact: true })).toBeVisible();
  await expect(dialog.getByText('ภาพสินค้า · เปิดในเมตาเวิร์สเพื่อหมุน 3D', { exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'ซูมเข้า', exact: true })).toBeDisabled();
  for (const viewport of [{ width: 390, height: 844 }, { width: 844, height: 390 }, { width: 320, height: 568 }]) {
    await page.setViewportSize(viewport);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    expect(await page.locator('html').evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    const add = dialog.getByRole('button', { name: 'เพิ่มลงตะกร้า', exact: true });
    await add.scrollIntoViewIfNeeded(); await expect(add).toBeEnabled();
    const box = await add.boundingBox(); expect(box).not.toBeNull();
    expect(box!.y).toBeGreaterThanOrEqual(0); expect(box!.y + box!.height).toBeLessThanOrEqual(viewport.height + 1);
    await page.screenshot({ path: resolve(`../.artifacts/ui-qa/accessibility-${viewport.width}x${viewport.height}.png`) });
  }
  await page.keyboard.press('Escape'); await expect(dialog).toBeHidden(); expect(errors).toEqual([]);
});
