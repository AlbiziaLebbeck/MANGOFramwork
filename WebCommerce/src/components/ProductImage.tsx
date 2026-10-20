import { t } from '../i18n';
import { useState } from 'react';
import { Package } from 'lucide-react';
import type { Product } from '../types';

const scriptAssetBase = new URL(
  '.',
  document.currentScript instanceof HTMLScriptElement ? document.currentScript.src : document.baseURI,
).href;
export function assetUrl(path: string) {
  const base = window.MANGOS_TTB_CONFIG?.assetBase ?? scriptAssetBase;
  return new URL(path, new URL(base, location.href)).href;
}
export const demoImage = (key: string) =>
  ({
    'orbit-lamp': 'orbit-lamp.png',
    'mango-sculpture': 'mango-sculpture.png',
    'mango-star': 'mango-star.png',
  })[key];
export function productImage(product: Pick<Product, 'thumbnailUrl' | 'preview'>) {
  return (
    product.thumbnailUrl ||
    (demoImage(product.preview.modelKey) ? assetUrl('products/' + demoImage(product.preview.modelKey)) : null)
  );
}
export function ProductImage({
  src,
  name,
  className = '',
}: {
  src: string | null | undefined;
  name: string;
  className?: string;
}) {
  const [failed, setFailed] = useState(false);
  return src && !failed ? (
    <img
      className={`product-image ${className}`}
      src={src}
      alt={name}
      loading="lazy"
      onError={() => setFailed(true)}
    />
  ) : (
    <div
      className={`product-image image-unavailable ${className}`}
      role="img"
      aria-label={t('ยังไม่มีภาพ {0}', [name])}
    >
      <Package />
      <span>{t('ยังไม่มีภาพสินค้า')}</span>
    </div>
  );
}
