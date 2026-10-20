import { useEffect } from 'react';
import { Download, FileText, Printer, RefreshCw } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { date, isPhysical, money } from '../types';
import { Empty, MockNotice, PaymentStatus, RefreshButton } from '../components/Ui';
import { ProductImage, productImage } from '../components/ProductImage';
import { exactMoney } from '../admin/types';

const deliveryLabels: Record<string, string> = {
  Pending: 'รอจัดเตรียม',
  Preparing: 'กำลังจัดเตรียม',
  ReadyForPickup: 'พร้อมรับสินค้า',
  Shipped: 'จัดส่งแล้ว',
  Delivered: 'ส่งมอบแล้ว',
  Cancelled: 'ยกเลิกการส่งมอบ',
  Review: 'รอตรวจสอบ',
};
export default function HistoryScreen() {
  const { history, historyCursor, activity, busy, products } = useCommerce();
  const pending =
    !!activity &&
    (activity.order.paymentState === 'VoidPending' ||
      Boolean(activity.payment?.voided && !activity.documents.some((d) => d.kind === 'Void')) ||
      (['Paid', 'Voided'].includes(activity.order.paymentState) &&
        activity.documents.some((d) => d.state !== 'Ready')) ||
      (activity.order.paymentState === 'Paid' &&
        (activity.documents.length === 0 || activity.fulfillment.length < activity.order.lines.length)));
  useEffect(() => {
    if (!pending) return;
    let running = false,
      attempts = 0;
    const timer = setInterval(() => {
      if (running || document.visibilityState !== 'visible' || attempts >= 20) return;
      running = true;
      attempts++;
      void commerce
        .refreshActivity()
        .catch(() => {})
        .finally(() => {
          running = false;
        });
    }, 3000);
    return () => clearInterval(timer);
  }, [activity?.order.id, pending]);
  return (
    <div className="history-panel">
      <h1>ประวัติรายการ</h1>
      <MockNotice />
      <div className="history-grid">
        <div className="history-list">
          <div className="history-table-head">
            <span>คำสั่งซื้อ</span>
            <span>วันที่</span>
            <span>ยอดชำระ</span>
            <span>สถานะ</span>
          </div>
          {history.map((order) => (
            <button
              key={order.id}
              className={`history-row ${activity?.order.id === order.id ? 'selected' : ''}`}
              disabled={busy}
              onClick={() => void commerce.run(() => commerce.loadActivity(order.id))}
            >
              <strong>{order.number}</strong>
              <span>{date(order.createdAt)}</span>
              <b>{money(order.totalMinor)}</b>
              <PaymentStatus state={order.paymentState} />
            </button>
          ))}
          {!history.length ? <Empty>ยังไม่มีประวัติรายการ</Empty> : null}
          <div className="history-more">
            <RefreshButton onClick={() => void commerce.run(() => commerce.loadHistory())} />
            {historyCursor ? (
              <button
                className="button secondary"
                disabled={busy}
                onClick={() => void commerce.run(() => commerce.loadHistory(true))}
              >
                โหลดเพิ่มเติม
              </button>
            ) : null}
          </div>
        </div>
        <aside className="order-detail">
          {activity ? (
            <>
              <div className="order-heading">
                <div>
                  <h2>{activity.order.number}</h2>
                  <span className="muted">{date(activity.order.createdAt)}</span>
                </div>
                <PaymentStatus state={activity.order.paymentState} />
              </div>
              <h3>รายการสินค้า</h3>
              <div className="order-products">
                {activity.order.lines.map((line) => {
                  const product = products.find((p) => p.id === line.productId);
                  const item = activity.fulfillment.find((f) => f.orderLineId === line.id);
                  return (
                    <div key={line.id} className="order-product">
                      <ProductImage
                        src={line.thumbnailUrl || (product ? productImage(product) : null)}
                        name={line.name}
                      />
                      <div>
                        <strong>{line.name}</strong>
                        <p className="muted">
                          {line.variantName} · {isPhysical(line.kind) ? 'สินค้าจัดส่งจริง' : 'สินค้าดิจิทัล'}
                        </p>
                        <span className={`delivery-state ${item?.state === 'Delivered' ? 'success' : ''}`}>
                          {item
                            ? (deliveryLabels[item.state] ?? item.state)
                            : activity.order.paymentState === 'Paid'
                              ? 'กำลังเตรียมการส่งมอบ'
                              : ['Refunded', 'Voided', 'VoidPending'].includes(activity.order.paymentState)
                                ? 'พักหรือยกเลิกการส่งมอบ'
                                : 'รอยืนยันชำระ'}
                        </span>
                        {item?.trackingNumber ? (
                          <p className="muted">
                            {item.carrier} · {item.trackingNumber}
                          </p>
                        ) : null}
                      </div>
                      <span>× {line.quantity}</span>
                      <b>{money(line.totalMinor)}</b>
                    </div>
                  );
                })}
              </div>
              <div className="summary-row total">
                <span>ยอดชำระ</span>
                <strong>{money(activity.order.totalMinor)}</strong>
              </div>
              {activity.refund ? (
                <section className="message subtle order-refund" aria-label="ประวัติคืนเงินภายนอก">
                  <h3>คืนเงินภายนอกที่ผ่านการตรวจหลักฐาน</h3>
                  <p>ยอดคืนสะสม {exactMoney(activity.refund.amountMinor)}</p>
                  {activity.refund.latestApprovedAt ? (
                    <p>ตรวจหลักฐานล่าสุด {date(activity.refund.latestApprovedAt)}</p>
                  ) : null}
                  <p>
                    {activity.refund.goodsReviewPending
                      ? 'กำลังตรวจการคืนสินค้าและสิทธิ์ที่เกี่ยวข้อง'
                      : 'บันทึกการตรวจสินค้าและสิทธิ์แล้ว'}
                  </p>
                  {activity.refund.returnedLines.map((line) => (
                    <p key={line.orderLineId}>
                      {activity.order.lines.find((x) => x.id === line.orderLineId)?.name ?? 'สินค้า'} ·
                      รับคืนเข้าสต็อก {line.quantity} หน่วย
                    </p>
                  ))}
                  <p className="muted">
                    ใบเสร็จเดิมแสดงการรับเงินครั้งแรก
                    การคืนเงินนี้ดำเนินการภายนอกและผ่านการตรวจเอกสารของบริษัท
                  </p>
                </section>
              ) : null}
              {activity.order.paymentState === 'Unpaid' && activity.order.canCancel ? (
                <button
                  className="button primary"
                  disabled={busy}
                  onClick={() => void commerce.run(() => commerce.openOrder(activity.order.id))}
                >
                  กลับไปชำระเงิน
                </button>
              ) : null}
              {activity.order.paymentState === 'VoidPending' ? (
                <p className="message warning">
                  กำลังตรวจผลการยกเลิก ระหว่างนี้พักการส่งมอบและใช้สิทธิ์ของรายการนี้
                </p>
              ) : activity.payment?.voided ? (
                <p className="message warning">รายการรับเงินถูกยกเลิก โปรดอ่านเอกสารยกเลิกประกอบ</p>
              ) : null}
              <div className="documents">
                <h3>เอกสาร</h3>
                {activity.documents.map((document) => (
                  <div className="document-item" key={document.id}>
                    <div className="document-heading">
                      <FileText size={32} />
                      <div>
                        <strong>{document.kind === 'Receipt' ? 'ใบเสร็จรับเงิน' : 'เอกสารยกเลิก'}</strong>
                        <p className="muted">{document.number}</p>
                      </div>
                    </div>
                    {document.state === 'Ready' ? (
                      <>
                        <button
                          className="button primary"
                          disabled={busy}
                          onClick={() => void commerce.download(document)}
                        >
                          <Download size={20} />
                          {document.kind === 'Receipt' ? 'ดาวน์โหลดใบเสร็จ' : 'ดาวน์โหลดเอกสารยกเลิก'}
                        </button>
                        <button
                          className="button secondary"
                          disabled={busy}
                          onClick={() => void commerce.download(document, true)}
                        >
                          <Printer size={20} />
                          {document.kind === 'Receipt' ? 'พิมพ์ใบเสร็จ' : 'พิมพ์เอกสารยกเลิก'}
                        </button>
                      </>
                    ) : (
                      <p className="message subtle">กำลังจัดทำเอกสาร</p>
                    )}
                  </div>
                ))}
                {!activity.documents.length ? (
                  <p className="muted">
                    {activity.order.paymentState === 'Paid'
                      ? 'กำลังเตรียมใบเสร็จ'
                      : 'เอกสารจะออกหลังยืนยันรับเงิน'}
                  </p>
                ) : null}
                <button
                  className="text-button"
                  disabled={busy}
                  onClick={() => void commerce.run(() => commerce.loadActivity(activity.order.id))}
                >
                  <RefreshCw size={16} />
                  อัปเดตเอกสารและการส่งมอบ
                </button>
              </div>
              <button className="text-button" onClick={() => commerce.navigate('inventory')}>
                ไปที่สินค้าของฉัน
              </button>
            </>
          ) : (
            <Empty>เลือกรายการเพื่อดูข้อมูลและใบเสร็จ</Empty>
          )}
        </aside>
      </div>
    </div>
  );
}
