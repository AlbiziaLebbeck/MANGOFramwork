import { t } from '../i18n';
import { Download, Box } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { date } from '../types';
import { Empty, MockNotice, RefreshButton } from '../components/Ui';
import { ProductImage, productImage } from '../components/ProductImage';

export default function InventoryScreen() {
  const { inventory, inventoryCursor, busy, products, gameReady, config } = useCommerce();
  return (
    <div className="simple-panel inventory-panel">
      <h1>{t('สินค้าของฉัน')}</h1>
      <MockNotice />
      <p className="muted">{t('สิทธิ์สินค้าดิจิทัลที่ได้รับจากคำสั่งซื้อ')}</p>
      <div className="inventory-list">
        {inventory.map((item) => {
          const product = products.find((p) => p.id === item.productId);
          return (
            <article key={item.id} className="inventory-row">
              <ProductImage src={product ? productImage(product) : null} name={item.name} />
              <div>
                <h2>{item.name}</h2>
                <p>
                  {item.variantName} {t('· หน่วยที่')} {item.unitIndex + 1}
                </p>
                <p className="muted">
                  {t('ได้รับ')} {date(item.grantedAt)}
                </p>
                <span className={`status ${item.canUse ? 'success' : ''}`}>
                  {item.canUse
                    ? t('พร้อมใช้งาน')
                    : item.state === 'Revoked'
                      ? t('สิทธิ์ถูกเพิกถอน')
                      : t('รอตรวจสอบ')}
                </span>
              </div>
              <div className="inventory-actions">
                {item.canDownload ? (
                  <button
                    className="button secondary"
                    disabled={busy}
                    onClick={() => void commerce.downloadItem(item)}
                  >
                    <Download size={20} />
                    {t('ดาวน์โหลดไฟล์')}
                  </button>
                ) : null}
                {item.canUse && item.kind === 'InventoryItem' ? (
                  <button
                    className="button secondary"
                    disabled={!gameReady || !config?.gameUseReady || busy}
                    title={
                      !gameReady || !config?.gameUseReady
                        ? t('เชื่อมต่อเซิร์ฟเวอร์เมตาเวิร์สเพื่อใช้สินค้า')
                        : undefined
                    }
                    onClick={() => commerce.useItem(item)}
                  >
                    <Box size={20} />
                    {t('ใช้ในเมตาเวิร์ส')}
                  </button>
                ) : null}
                <button
                  className="text-button"
                  disabled={busy}
                  onClick={() => void commerce.run(() => commerce.loadActivity(item.orderId))}
                >
                  {t('ดูคำสั่งซื้อ')}
                </button>
              </div>
            </article>
          );
        })}
      </div>
      {!inventory.length ? (
        <Empty>
          <h2>{t('ยังไม่มีสินค้าดิจิทัล')}</h2>
          <p>{t('หลังชำระสำเร็จ สินค้าจะปรากฏที่นี่')}</p>
        </Empty>
      ) : null}
      <div className="history-more">
        <RefreshButton onClick={() => void commerce.run(() => commerce.loadInventory())} />
        {inventoryCursor ? (
          <button
            className="button secondary"
            disabled={busy}
            onClick={() => void commerce.run(() => commerce.loadInventory(true))}
          >
            {t('โหลดเพิ่มเติม')}
          </button>
        ) : null}
      </div>
    </div>
  );
}
