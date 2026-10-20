import { afterEach, expect, it, vi } from 'vitest';
import { english } from '../src/i18n-messages';
import { getLanguage, setLanguage, t, message } from '../src/i18n';
import { money, date } from '../src/types';

afterEach(() => {
  setLanguage('th');
  vi.unstubAllGlobals();
});
it('provides English translations with matching placeholders for every catalog entry', () => {
  expect(Object.keys(english).length).toBeGreaterThan(550);
  const tokens = (value: string) => [...value.matchAll(/\{\d+\}/g)].map((x) => x[0]).sort();
  for (const [thai, en] of Object.entries(english)) {
    expect(en.trim(), thai).not.toBe('');
    expect(en, thai).not.toMatch(/[\u0E01-\u0E3A\u0E40-\u0E5B]/);
    expect(tokens(en), thai).toEqual(tokens(thai));
  }
});
it('switches language, persists only the preference and updates the document language', () => {
  const setItem = vi.fn();
  vi.stubGlobal('localStorage', { setItem });
  vi.stubGlobal('document', { documentElement: { lang: 'th' } });
  setLanguage('en');
  expect(t('ประวัติรายการ')).toBe('Order history');
  expect(document.documentElement.lang).toBe('en');
  expect(setItem).toHaveBeenCalledWith('mangos-ttb-language', 'en');
  setLanguage('th');
  expect(t('ประวัติรายการ')).toBe('ประวัติรายการ');
});
it('keeps numeric amounts and Bangkok time stable while formatting the chosen language', () => {
  setLanguage('en');
  expect(money(100)).toBe('฿1.00');
  expect(date('2026-10-19T23:00:00Z')).toContain('20 Oct 2026');
  setLanguage('th');
  expect(money(100)).toBe('฿1.00');
  expect(date('2026-10-19T23:00:00Z')).toContain('2569');
});
it('retains placeholder data as text and translates pending notices when language changes', () => {
  const notice = message('เพิ่ม {0} ลงตะกร้าแล้ว', ['<b>$&</b>']);
  setLanguage('en');
  expect(t(notice)).toBe('Added <b>$&</b> to cart');
  setLanguage('th');
  expect(t(notice)).toBe('เพิ่ม <b>$&</b> ลงตะกร้าแล้ว');
  expect(t('ชื่อสินค้าของผู้ขาย')).toBe('ชื่อสินค้าของผู้ขาย');
});
it('works with blocked storage and ignores an unsupported language', () => {
  vi.stubGlobal('localStorage', {
    setItem: () => {
      throw new Error('blocked');
    },
  });
  setLanguage('en');
  expect(getLanguage()).toBe('en');
  setLanguage('fr' as never);
  expect(getLanguage()).toBe('en');
});
