import { useState } from 'react';
import { FileText, MapPin, Phone, Play, UserRound } from 'lucide-react';
import { useCommerce } from '../store';
import { Empty, PaymentStatus } from '../components/Ui';
import { money, isPhysical } from '../types';
import { admin, useAdmin } from './store';
import { allowed, deliveryLabels, exactMoney, type AdminFulfillment } from './types';
import { Back, Documents, Field, Refresh, RowEnd, SearchField, State, run } from './Ui';
import { VoidInspector } from './Finance';
import { ManualStart, ManualCreate } from './Refunds';

export default function Orders() {
  const state = useAdmin();
  const { busy } = useCommerce();
  const [search, setSearch] = useState('');
  const detail = state.order;
  const rows = state.orders.filter((x) =>
    `${x.number} ${x.buyerName}`.toLocaleLowerCase().includes(search.toLocaleLowerCase()),
  );
  return (
    <div className={`admin-columns ${detail ? 'has-detail' : ''}`}>
      <section className="admin-queue">
        <h1>คำสั่งซื้อ</h1>
        <div className="admin-toolbar">
          <SearchField value={search} onChange={setSearch} />
          <select
            aria-label="สถานะชำระเงิน"
            value={state.orderPayment}
            disabled={busy}
            onChange={(e) => {
              admin.back();
              admin.patch({ orderPayment: e.target.value });
              run(() => admin.refresh());
            }}
          >
            <option value="">สถานะชำระทั้งหมด</option>
            <option value="Paid">ชำระสำเร็จ</option>
            <option value="Unpaid">รอชำระ</option>
            <option value="Review">รอตรวจสอบ</option>
            <option value="VoidPending">ตรวจผลยกเลิก</option>
            <option value="Voided">ยกเลิกรับชำระ</option>
            <option value="Refunded">คืนเงินภายนอกครบแล้ว</option>
          </select>
          <Refresh />
        </div>
        <div className="admin-table">
          <div className="admin-table-head order-row">
            <span>คำสั่งซื้อ</span>
            <span>ผู้ซื้อ</span>
            <span>ยอดชำระ</span>
            <span>สถานะ</span>
          </div>
          {rows.map((row) => (
            <button
              className={`admin-table-row order-row ${detail?.order.id === row.id ? 'selected' : ''}`}
              key={row.id}
              onClick={() => run(() => admin.chooseOrder(row.id))}
            >
              <span>{row.number}</span>
              <span>{row.buyerName}</span>
              <span>{exactMoney(row.totalMinor)}</span>
              <PaymentStatus state={row.paymentState} />
              <RowEnd />
            </button>
          ))}
          {state.loaded && !rows.length ? <Empty>ไม่พบคำสั่งซื้อตามเงื่อนไข</Empty> : null}
        </div>
        {state.orderCursor ? (
          <button className="text-button" disabled={busy} onClick={() => run(() => admin.refresh(true))}>
            ดูคำสั่งซื้อเพิ่มเติม
          </button>
        ) : null}
      </section>
      <aside className="admin-inspector">
        {detail ? (
          <OrderInspector key={detail.order.id} />
        ) : (
          <Empty>เลือกคำสั่งซื้อเพื่อดูข้อมูลและการส่งมอบ</Empty>
        )}
      </aside>
    </div>
  );
}
function OrderInspector() {
  const { order: detail, review } = useAdmin();
  const { session, busy } = useCommerce();
  if (!detail) return null;
  const order = detail.order;
  const finance = allowed(session, 'finance');
  return (
    <>
      <Back />
      <div className="admin-detail-heading">
        <h2>{order.number}</h2>
        <PaymentStatus state={order.paymentState} />
      </div>
      {order.state === 'Review' ? (
        <p className="message warning">พักคำสั่งซื้อเพื่อตรวจสอบทางการเงิน</p>
      ) : null}
      <section>
        <h3>รายการสินค้า</h3>
        {order.lines.map((line) => (
          <div className="admin-product-line" key={line.id}>
            <div>
              <strong>{line.name}</strong>
              <small>
                {line.variantName} · {line.sku}
              </small>
            </div>
            <span>× {line.quantity}</span>
            <b>{money(line.totalMinor)}</b>
          </div>
        ))}
        <div className="summary-row total">
          <span>รวมทั้งหมด</span>
          <strong>{money(order.totalMinor)}</strong>
        </div>
      </section>
      <section>
        <h3>การรับสินค้า</h3>
        <p className="admin-contact">
          <MapPin size={19} />
          {order.delivery.method === 'shipping'
            ? 'จัดส่งตามที่อยู่'
            : order.delivery.method === 'pickup'
              ? 'รับสินค้าที่ร้าน'
              : 'สินค้าดิจิทัล'}
        </p>
        <p className="admin-contact">
          <UserRound size={19} />
          {order.buyer.name}
        </p>
        {order.buyer.phone ? (
          <p className="admin-contact">
            <Phone size={19} />
            {order.buyer.phone}
          </p>
        ) : null}
        {order.delivery.address ? (
          <p>
            {[
              order.delivery.address.line1,
              order.delivery.address.subdistrict,
              order.delivery.address.district,
              order.delivery.address.province,
              order.delivery.address.postalCode,
            ].join(' ')}
          </p>
        ) : null}
      </section>
      <section>
        <h3>การส่งมอบ</h3>
        {detail.fulfillment.map((item) => (
          <Delivery key={`${item.id}:${item.version}`} item={item} />
        ))}
        {!detail.fulfillment.length ? (
          <p className="muted">
            {order.paymentState === 'Paid'
              ? 'กำลังเตรียมรายการส่งมอบ'
              : 'ส่งมอบได้หลังยืนยันรับเงินและตรวจเงื่อนไขแล้ว'}
          </p>
        ) : null}
        <button
          className="text-button"
          disabled={busy}
          onClick={() => run(() => admin.chooseOrder(order.id))}
        >
          อัปเดตการส่งมอบและเอกสาร
        </button>
      </section>
      {finance ? (
        <>
          <section>
            <h3>เอกสาร</h3>
            <Documents items={detail.documents} />
            {!detail.documents.length ? <p className="muted">ยังไม่มีเอกสารที่ออกแล้ว</p> : null}
          </section>
          <section>
            <h3>รายการรับเงิน</h3>
            {detail.credits.map((credit) => (
              <div key={credit.id} className="admin-credit">
                <strong>{exactMoney(credit.amountMinor)}</strong>
                <small>{credit.instructionId}</small>
                <p>
                  {credit.primary ? 'ยอดหลักของคำสั่งซื้อ' : 'ยอดอื่นที่ต้องตรวจสอบ'} ·{' '}
                  {credit.voided ? 'ยกเลิกแล้ว' : 'พบรับเงิน'}
                </p>
                <button
                  className="text-button"
                  disabled={busy}
                  onClick={() => run(() => admin.inspectVoid(credit.id))}
                >
                  ตรวจเงื่อนไขคืนเงิน
                </button>
                <ManualStart bank={credit} />
              </div>
            ))}
            {!detail.credits.length ? (
              <p className="muted">ยังไม่พบรายการรับเงินที่ตรง QR ของคำสั่งซื้อ</p>
            ) : null}
            <button
              className="button secondary"
              disabled={busy}
              onClick={() => run(() => admin.casesFromOrder())}
            >
              <FileText size={18} />
              ดูเคสการเงิน
            </button>
          </section>
          {review ? <VoidInspector /> : null}
          <ManualCreate />
        </>
      ) : null}
    </>
  );
}
function Delivery({ item }: { item: AdminFulfillment }) {
  const { session, busy } = useCommerce();
  const { order } = useAdmin();
  const [note, setNote] = useState(''),
    [carrier, setCarrier] = useState(item.carrier ?? ''),
    [tracking, setTracking] = useState(item.trackingNumber ?? '');
  const action =
    item.state === 'Pending'
      ? 'prepare'
      : item.state === 'Preparing'
        ? item.deliveryMethod === 'shipping'
          ? 'ship'
          : 'ready-pickup'
        : ['ReadyForPickup', 'Shipped'].includes(item.state)
          ? 'deliver'
          : null;
  const label =
    action === 'prepare'
      ? 'เริ่มจัดเตรียม'
      : action === 'ready-pickup'
        ? 'พร้อมรับสินค้า'
        : action === 'ship'
          ? 'ยืนยันส่งสินค้า'
          : 'ยืนยันส่งมอบ';
  const active =
    allowed(session, 'fulfillment') &&
    isPhysical(item.kind) &&
    order?.order.paymentState === 'Paid' &&
    ['Open', 'Completed'].includes(order.order.state);
  return (
    <div className="admin-delivery">
      <div className="admin-product-line">
        <div>
          <strong>{item.name}</strong>
          <small>
            {item.sku} × {item.quantity}
          </small>
        </div>
        <State
          value={item.state === 'Delivered' ? 'Complete' : item.state}
          label={deliveryLabels[item.state] ?? item.state}
        />
      </div>
      {item.trackingNumber ? (
        <p className="muted">
          {item.carrier} · {item.trackingNumber}
        </p>
      ) : null}
      {active && action ? (
        <>
          <Field label="หมายเหตุการจัดเตรียม">
            <textarea rows={2} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
          {action === 'ship' ? (
            <div className="admin-pair">
              <Field label="ผู้ให้บริการจัดส่ง">
                <input
                  required
                  value={carrier}
                  maxLength={100}
                  onChange={(e) => setCarrier(e.target.value)}
                />
              </Field>
              <Field label="เลขติดตาม / อ้างอิง">
                <input
                  required
                  value={tracking}
                  maxLength={100}
                  onChange={(e) => setTracking(e.target.value)}
                />
              </Field>
            </div>
          ) : null}
          <button
            className="button primary"
            disabled={busy || (action === 'ship' && (!carrier.trim() || !tracking.trim()))}
            onClick={() =>
              run(() =>
                admin.delivery(item.id, {
                  expectedVersion: item.version,
                  action,
                  note,
                  carrier: carrier || null,
                  trackingNumber: tracking || null,
                }),
              )
            }
          >
            <Play size={18} />
            {label}
          </button>
        </>
      ) : null}
    </div>
  );
}
