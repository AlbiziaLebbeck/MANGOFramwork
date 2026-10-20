import { t, useLanguage, setLanguage } from './i18n';
import { lazy, Suspense, useEffect, useRef } from 'react';
import { useCommerceChrome } from './chrome';
import { LogOut, ShoppingBag, Store, UserRound, X } from 'lucide-react';
import { bridge } from './bridge';
import { commerce, useCommerce } from './store';
import { Dialog, Empty, IconButton, MockNotice } from './components/Ui';
import { ProductImage, productImage } from './components/ProductImage';
import { money } from './types';
import { PwaActions } from './components/PwaActions';
import { usePwa } from './pwa';
import { canAdmin } from './admin/types';
import { AdminNavigation } from './admin/Navigation';
const ProductInspector = lazy(() => import('./features/ProductInspector'));
const Checkout = lazy(() => import('./features/Checkout').then((m) => ({ default: m.CheckoutScreen })));
const Cart = lazy(() => import('./features/Checkout').then((m) => ({ default: m.CartScreen })));
const Payment = lazy(() => import('./features/Payment'));
const History = lazy(() => import('./features/History'));
const Inventory = lazy(() => import('./features/Inventory'));
const Login = lazy(() => import('./features/Login'));
const Admin = lazy(() => import('./admin/Admin'));
const titles = {
  gallery: 'สำรวจสินค้า',
  product: 'ชมสินค้า',
  cart: 'ตะกร้าสินค้า',
  checkout: 'ตรวจสอบคำสั่งซื้อ',
  payment: 'ชำระด้วย QR',
  history: 'ประวัติรายการ',
  inventory: 'สินค้าของฉัน',
  login: 'เข้าสู่ระบบ',
  admin: 'จัดการร้านค้า',
};

function LanguageSwitch() {
  const language = useLanguage();
  return (
    <select
      className="language-switch"
      aria-label={t('เปลี่ยนภาษา')}
      value={language}
      onChange={(event) => setLanguage(event.target.value === 'en' ? 'en' : 'th')}
    >
      <option value="th">ไทย</option>
      <option value="en">English</option>
    </select>
  );
}

export default function App() {
  useLanguage();
  const state = useCommerce();
  const network = usePwa();
  const { screen, session, cart, busy, error, notice, hostAttached } = state;
  const header = useRef<HTMLElement>(null);
  const toast = useRef<HTMLDivElement>(null);
  useCommerceChrome(header, toast, screen === 'admin', !!(error || notice));
  useEffect(() => {
    const unsubscribe = bridge.subscribe((event) => {
      if (event.type === 'host.attached') commerce.patch({ hostAttached: true });
      if (event.type === 'host.ready') {
        commerce.patch({ hostAttached: true });
        void commerce.reconnectSession();
        bridge.send('overlay.state', { open: commerce.state.screen !== 'gallery' });
        bridge.send('game.probe');
      }
      if (event.type === 'host.stopped') commerce.patch({ gameReady: false });
      if (event.type === 'product.open') void commerce.inspect(event.productId ?? event.modelKey!);
      if (event.type === 'game.ready') commerce.patch({ gameReady: event.ready === true });
      if (event.type === 'game.challenge') void commerce.authorizeGame(event);
      if (event.type === 'game.result')
        commerce.patch({
          notice:
            event.result === 'equipped'
              ? 'ใช้สินค้าในเมตาเวิร์สแล้ว'
              : event.result === 'removed'
                ? 'หยุดใช้สินค้าแล้ว'
                : event.result === 'unavailable'
                  ? 'ยังไม่เชื่อมต่อบริการสิทธิ์ของเกม'
                  : 'ไม่สามารถใช้สินค้านี้ได้ กรุณาตรวจสอบสิทธิ์อีกครั้ง',
        });
      if (event.type === 'session.changed') void commerce.reconnectSession();
      if (event.type === 'session.end') {
        commerce.patch({ gameReady: false });
        commerce.endSession();
        commerce.close();
      }
    });
    bridge.send('game.probe');
    const focus = () => {
      if (document.visibilityState === 'visible') void commerce.refreshSession();
    };
    window.addEventListener('focus', focus);
    document.addEventListener('visibilitychange', focus);
    const timer = setInterval(focus, 30000);
    return () => {
      unsubscribe();
      clearInterval(timer);
      window.removeEventListener('focus', focus);
      document.removeEventListener('visibilitychange', focus);
    };
  }, []);
  useEffect(() => {
    bridge.send('overlay.state', { open: screen !== 'gallery' });
  }, [screen]);
  useEffect(() => {
    if (!notice) return;
    const timer = setTimeout(() => commerce.patch({ notice: null }), 6000);
    return () => clearTimeout(timer);
  }, [notice]);
  return (
    <>
      <header ref={header} className={`commerce-header${screen === 'admin' ? ' admin-header' : ''}`}>
        <button
          className="brand"
          data-commerce-home
          onClick={() => commerce.close()}
          aria-label={t('MANGOs สำรวจสินค้า')}
        >
          MANGOs
        </button>
        {screen === 'admin' ? (
          <AdminNavigation languageSwitch={<LanguageSwitch />} />
        ) : (
          <>
            <nav aria-label={t('เมนูซื้อสินค้า')}>
              <button
                aria-current={screen === 'gallery' ? 'page' : undefined}
                onClick={() => commerce.close()}
              >
                {t('สำรวจสินค้า')}{' '}
              </button>
              <button
                aria-current={screen === 'history' ? 'page' : undefined}
                onClick={() => commerce.navigate('history')}
              >
                {t('ประวัติรายการ')}{' '}
              </button>
              <button
                aria-current={screen === 'inventory' ? 'page' : undefined}
                onClick={() => commerce.navigate('inventory')}
              >
                {t('สินค้าของฉัน')}{' '}
              </button>
            </nav>
            <div className="header-actions">
              <LanguageSwitch />
              <PwaActions />
              {canAdmin(session) ? (
                <IconButton
                  label={t('จัดการร้านค้า')}
                  disabled={busy}
                  onClick={() => commerce.navigate('admin')}
                >
                  <Store size={20} />
                </IconButton>
              ) : null}
              <button
                className="icon-button bag-button"
                aria-label={t('ตะกร้าสินค้า {0} ชิ้น', [cart.lines.reduce((sum, l) => sum + l.quantity, 0)])}
                onClick={() => commerce.navigate('cart')}
              >
                <ShoppingBag />
                {cart.lines.length ? <span>{cart.lines.reduce((sum, l) => sum + l.quantity, 0)}</span> : null}
              </button>
              <IconButton
                label={
                  session.authenticated ? (session.user?.displayName ?? t('บัญชีของฉัน')) : t('เข้าสู่ระบบ')
                }
                onClick={() => commerce.navigate('login')}
              >
                <UserRound />
              </IconButton>
              {session.authenticated ? (
                <IconButton
                  label={t('ออกจากระบบซื้อสินค้า')}
                  disabled={busy}
                  onClick={() => void commerce.signOut()}
                >
                  <LogOut size={18} />
                </IconButton>
              ) : null}
            </div>
          </>
        )}
      </header>
      {!hostAttached && screen === 'gallery' ? (
        <main className="standalone-gallery">
          <div className="gallery-heading">
            <h1>MANGOs Gallery</h1>
            <p>{t('เลือกชิ้นที่ชอบ แล้วสำรวจรายละเอียดสินค้า')}</p>
            <MockNotice />
          </div>
          <div className="gallery-products">
            {state.products.map((product) => (
              <button
                key={product.id}
                data-commerce-product={product.id}
                className="gallery-product"
                onClick={() => void commerce.inspect(product.id)}
              >
                <ProductImage src={productImage(product)} name={product.name} />
                <div>
                  <strong>{product.name}</strong>
                  <span>
                    {product.variants[0] ? money(product.variants[0].priceMinor) : t('ยังไม่วางจำหน่าย')}
                  </span>
                </div>
              </button>
            ))}
          </div>
          {!state.products.length && !busy ? (
            <Empty>
              {network.offline ? t('เชื่อมต่ออินเทอร์เน็ตเพื่อดูสินค้า') : t('ยังไม่มีสินค้าที่เผยแพร่')}
            </Empty>
          ) : null}
          {state.catalogCursor ? (
            <button className="button secondary" disabled={busy} onClick={() => void commerce.loadCatalog()}>
              {t('ดูสินค้าเพิ่มเติม')}{' '}
            </button>
          ) : null}
        </main>
      ) : null}
      {screen !== 'gallery' ? (
        <Dialog title={t(titles[screen])} className={`screen-${screen}`}>
          <Suspense fallback={<Empty>{t('กำลังเปิดหน้าต่าง…')}</Empty>}>
            {screen === 'product' ? (
              <ProductInspector />
            ) : screen === 'cart' ? (
              <Cart />
            ) : screen === 'checkout' ? (
              <Checkout />
            ) : screen === 'payment' ? (
              <Payment />
            ) : screen === 'history' ? (
              <History />
            ) : screen === 'inventory' ? (
              <Inventory />
            ) : screen === 'admin' ? (
              <Admin />
            ) : (
              <Login />
            )}
          </Suspense>
        </Dialog>
      ) : null}
      {error || notice ? (
        <div
          ref={toast}
          role={error ? 'alert' : 'status'}
          className={`commerce-toast ${error ? 'error' : ''}`}
        >
          <span>{t(error ?? notice)}</span>
          <IconButton label={t('ปิดข้อความ')} onClick={() => commerce.patch({ error: null, notice: null })}>
            <X size={18} />
          </IconButton>
        </div>
      ) : null}
      {busy ? <div className="commerce-progress" role="status" aria-label={t('กำลังดำเนินการ')} /> : null}
    </>
  );
}
