import { useSyncExternalStore } from 'react';
import { english } from './i18n-messages';

export type Language = 'th' | 'en';
export interface TranslationMessage {
  source: string;
  values: readonly unknown[];
}
export const message = (source: string, values: readonly unknown[]): TranslationMessage => ({
  source,
  values,
});
const storageKey = 'mangos-ttb-language';
function initialLanguage(): Language {
  try {
    return globalThis.localStorage?.getItem(storageKey) === 'en' ? 'en' : 'th';
  } catch {
    return 'th';
  }
}
let language = initialLanguage();
const listeners = new Set<() => void>();
export const getLanguage = () => language;
export const locale = () => (language === 'en' ? 'en-GB' : 'th-TH');
export function setLanguage(next: Language) {
  if (next !== 'th' && next !== 'en') return;
  language = next;
  try {
    globalThis.localStorage?.setItem(storageKey, next);
  } catch {
    /* Preference is optional. */
  }
  if (typeof document !== 'undefined') document.documentElement.lang = next;
  for (const listener of listeners) listener();
}
export function useLanguage() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getLanguage,
    getLanguage,
  );
}
export function t(
  source: string | TranslationMessage | null | undefined,
  values: readonly unknown[] = [],
): string {
  const key = typeof source === 'object' && source !== null ? source.source : (source ?? '');
  if (typeof source === 'object' && source !== null) values = source.values;
  const text = language === 'en' ? (english[key] ?? key) : key;
  return text.replace(/\{(\d+)\}/g, (placeholder, index: string) =>
    Number(index) < values.length ? String(values[Number(index)]) : placeholder,
  );
}
