import { t } from '../i18n';
import { useState, type FormEvent } from 'react';
import { ArrowLeft, Info, QrCode, Store, Trash2, Truck } from 'lucide-react';
import { commerce, useCommerce } from '../store';
import { isPhysical, money, type Address, type CartLine } from '../types';
import { Empty, IconButton, MockNotice, Quantity } from '../components/Ui';
import { ProductImage, productImage } from '../components/ProductImage';

function CartRow({ line }: { line: CartLine }) {
  const { busy, products } = useCommerce();
  const product = products.find((p) => p.id === line.productId);
  return (
    <div className="cart-row">
      <ProductImage src={line.thumbnailUrl || (product ? productImage(product) : null)} name={line.name} />
      <div className="cart-row-copy">
        <strong>{line.name}</strong>
        <span>{line.variantName}</span>
        <b>{money(line.unitPriceMinor)}</b>
        {!line.available ? <span className="danger">{t('รายการนี้ไม่พร้อมจำหน่าย')}</span> : null}
      </div>
      <Quantity
        value={line.quantity}
        disabled={busy}
        max={line.availableQuantity ?? 99}
        onChange={(n) => void commerce.setQuantity(line.variantId, n)}
      />
      <IconButton
        label={t('นำ {0} ออกจากตะกร้า', [line.name])}
        disabled={busy}
        onClick={() => void commerce.setQuantity(line.variantId, 0)}
      >
        <Trash2 size={20} />
      </IconButton>
    </div>
  );
}

export function CartScreen() {
  const { cart, busy } = useCommerce();
  return (
    <div className="simple-panel">
      <h1>{t('ตะกร้าสินค้า')}</h1>
      <MockNotice />
      {cart.lines.length ? (
        <>
          <div className="cart-lines">
            {cart.lines.map((line) => (
              <CartRow key={line.variantId} line={line} />
            ))}
          </div>
          <div className="cart-bottom">
            <p>
              {t('รวมค่าสินค้า')} <strong>{money(cart.subtotalMinor)}</strong>
            </p>
            <button
              className="button primary"
              disabled={busy || cart.lines.some((l) => !l.available)}
              onClick={() => void commerce.checkout()}
            >
              {t('ดำเนินการสั่งซื้อ')}
            </button>
          </div>
        </>
      ) : (
        <Empty>
          <h2>{t('ตะกร้ายังว่าง')}</h2>
          <p>{t('เลือกสินค้าที่สนใจ แล้วเพิ่มลงตะกร้า')}</p>
          <button className="button secondary" onClick={() => commerce.close()}>
            {t('สำรวจสินค้า')}
          </button>
        </Empty>
      )}
    </div>
  );
}

const initialAddress: Address = {
  line1: '',
  subdistrict: '',
  district: '',
  province: '',
  postalCode: '',
  country: 'TH',
};
export function CheckoutScreen() {
  const { cart, checkoutOptions: options, quote, session, busy, config } = useCommerce();
  const [name, setName] = useState(session.user?.displayName ?? '');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [method, setMethod] = useState(options?.pickupEnabled ? 'pickup' : 'shipping');
  const [address, setAddress] = useState<Address>(initialAddress);
  const physical = cart.lines.some((l) => isPhysical(l.kind));
  const edit = () => commerce.clearQuote();
  const field = (key: keyof Address, label: string) => (
    <label key={key}>
      {label}
      <input
        value={address[key]}
        required={physical && method === 'shipping'}
        maxLength={key === 'postalCode' ? 5 : 200}
        inputMode={key === 'postalCode' ? 'numeric' : 'text'}
        pattern={key === 'postalCode' ? '[0-9]{5}' : undefined}
        onChange={(e) => {
          setAddress({ ...address, [key]: e.target.value });
          edit();
        }}
      />
    </label>
  );
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (quote) void commerce.createOrder();
    else
      void commerce.quote({
        cartVersion: cart.version,
        buyer: { name, email: email || null, phone: phone || null },
        delivery: {
          method: physical ? method : 'digital',
          address: physical && method === 'shipping' ? address : null,
        },
      });
  };
  if (!cart.lines.length) return <CartScreen />;
  return (
    <form className="checkout-panel" onSubmit={submit}>
      <div className="panel-title">
        <IconButton label={t('กลับไปตะกร้า')} onClick={() => commerce.navigate('cart')}>
          <ArrowLeft />
        </IconButton>
        <h1>{t('ตรวจสอบคำสั่งซื้อ')}</h1>
      </div>
      <div className="checkout-grid">
        <div>
          <h2>
            {t('รายการสินค้า (')}
            {cart.lines.length})
          </h2>
          <div className="cart-lines compact">
            {cart.lines.map((line) => (
              <CartRow key={line.variantId} line={line} />
            ))}
          </div>
          <h2>{t('ข้อมูลผู้ซื้อ')}</h2>
          <div className="buyer-fields" onChange={edit}>
            <label>
              {t('ชื่อผู้ซื้อ')}
              <input
                required
                maxLength={200}
                autoComplete="name"
                value={name}
                onChange={(e) => setName(e.target.value)}
              />
            </label>
            <label>
              <span>
                {t('อีเมล')} <small className="muted">{t('(ไม่บังคับ)')}</small>
              </span>
              <input
                type="email"
                maxLength={200}
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </label>
            <label className="full">
              <span>
                {t('เบอร์โทรศัพท์')}
                {!physical ? <small className="muted"> {t('(ไม่บังคับ)')}</small> : null}
              </span>
              <input
                type="tel"
                autoComplete="tel"
                maxLength={25}
                required={physical}
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
              />
            </label>
          </div>
          {physical ? (
            <>
              <h2>{t('วิธีรับสินค้า')}</h2>
              <div className="delivery-options">
                {options?.pickupEnabled ? (
                  <label className={method === 'pickup' ? 'selected' : ''}>
                    <input
                      type="radio"
                      name="delivery"
                      value="pickup"
                      checked={method === 'pickup'}
                      onChange={() => {
                        setMethod('pickup');
                        edit();
                      }}
                    />
                    <Store />
                    <span>{t('รับสินค้าที่ร้าน')}</span>
                  </label>
                ) : null}
                {options?.shippingEnabled ? (
                  <label className={method === 'shipping' ? 'selected' : ''}>
                    <input
                      type="radio"
                      name="delivery"
                      value="shipping"
                      checked={method === 'shipping'}
                      onChange={() => {
                        setMethod('shipping');
                        edit();
                      }}
                    />
                    <Truck />
                    <span>
                      {t('จัดส่งตามที่อยู่ ·')} {money(options.shippingFeeMinor)}
                    </span>
                  </label>
                ) : null}
              </div>
              {method === 'shipping' && options?.shippingEnabled ? (
                <div className="address-fields">
                  {field('line1', t('ที่อยู่'))}
                  {field('subdistrict', t('แขวง/ตำบล'))}
                  {field('district', t('เขต/อำเภอ'))}
                  {field('province', t('จังหวัด'))}
                  {field('postalCode', t('รหัสไปรษณีย์'))}
                </div>
              ) : null}
            </>
          ) : null}
          {cart.lines.some((l) => !isPhysical(l.kind)) ? (
            <p className="message subtle">
              <Info size={20} />
              {t('สินค้าดิจิทัลจะอยู่ในสินค้าของฉัน หลังชำระสำเร็จ')}
            </p>
          ) : null}
        </div>
        <aside className="checkout-summary">
          <h2>{t('สรุปคำสั่งซื้อ')}</h2>
          <div className="summary-row">
            <span>{t('ค่าสินค้า')}</span>
            <span>{money(quote?.subtotalMinor ?? cart.subtotalMinor)}</span>
          </div>
          <div className="summary-row">
            <span>{t('ค่าจัดส่ง')}</span>
            <span>{quote ? money(quote.shippingMinor) : t('รอตรวจสอบยอด')}</span>
          </div>
          <hr />
          <div className="summary-row total">
            <span>{t('ยอดชำระ')}</span>
            <strong>{quote ? money(quote.totalMinor) : '—'}</strong>
          </div>
          <MockNotice />
          {quote ? (
            <p className="quote-confirmation">{t('ยอดชำระได้รับการตรวจสอบจากระบบ')}</p>
          ) : (
            <p className="muted">{t('กรอกข้อมูล แล้วตรวจสอบยอดก่อนสร้าง QR')}</p>
          )}
          <button
            className="button primary"
            type="submit"
            disabled={busy || (!!quote && !config?.paymentsReady) || cart.lines.some((l) => !l.available)}
          >
            <QrCode size={22} />
            {quote ? t('สร้าง QR เพื่อชำระเงิน') : t('ตรวจสอบยอดชำระ')}
          </button>
          {!config?.paymentsReady ? <p className="danger">{t('ระบบชำระเงินยังไม่พร้อม')}</p> : null}
          {commerce.pendingCheckout() ? (
            <button
              type="button"
              className="button secondary"
              disabled={busy}
              onClick={() => void commerce.createOrder(true)}
            >
              {t('กู้คืนคำสั่งซื้อที่ส่งค้าง')}
            </button>
          ) : null}
        </aside>
      </div>
    </form>
  );
}
