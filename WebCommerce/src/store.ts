import { useSyncExternalStore } from 'react';
import { message, type TranslationMessage } from './i18n';
import { CommerceApi, ApiError, StaleSessionError, errorMessage, resolveApiBase } from './api';
import { bridge, type HostEvent } from './bridge';
import {
  emptyCart,
  type Cart,
  type CheckoutInput,
  type CheckoutOptions,
  type CommerceDocument,
  type Entitlement,
  type Order,
  type OrderActivity,
  type Page,
  type PaymentQr,
  type Product,
  type Quote,
  type Screen,
  type Session,
  type StorefrontConfig,
  type Verification,
} from './types';

export interface CommerceState {
  screen: Screen;
  config: StorefrontConfig | null;
  products: Product[];
  catalogCursor: string | null;
  selected: Product | null;
  session: Session;
  sessionSyncing: boolean;
  cart: Cart;
  checkoutOptions: CheckoutOptions | null;
  quote: Quote | null;
  order: Order | null;
  qr: PaymentQr | null;
  verification: Verification | null;
  history: Order[];
  historyCursor: string | null;
  activity: OrderActivity | null;
  inventory: Entitlement[];
  inventoryCursor: string | null;
  busy: boolean;
  error: string | null;
  notice: string | TranslationMessage | null;
  hostAttached: boolean;
  previewReady: boolean;
  gameReady: boolean;
}
interface PendingCheckout {
  owner: string;
  quoteId: string;
  key: string;
}
export class CommerceStore {
  private listeners = new Set<() => void>();
  private busyCount = 0;
  private sessionGeneration = 0;
  private sessionRequest = 0;
  private sessionSync: Promise<void> | null = null;
  private sessionSyncRequest = 0;
  private automaticSignInAllowed = true;
  private nextAutomaticSignInAt = 0;
  private inspection = 0;
  private actionKeys = new Map<string, string>();
  private afterLoginScreen: Screen = 'gallery';
  state: CommerceState = {
    screen: 'gallery',
    config: null,
    products: [],
    catalogCursor: null,
    selected: null,
    session: { authenticated: false },
    sessionSyncing: false,
    cart: emptyCart,
    checkoutOptions: null,
    quote: null,
    order: null,
    qr: null,
    verification: null,
    history: [],
    historyCursor: null,
    activity: null,
    inventory: [],
    inventoryCursor: null,
    busy: false,
    error: null,
    notice: null,
    hostAttached: bridge.attached,
    previewReady: false,
    gameReady: false,
  };
  constructor(readonly api: CommerceApi) {
    api.onUnauthorized = () => this.applySession({ authenticated: false });
  }
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };
  snapshot = () => this.state;
  patch = (patch: Partial<CommerceState>) => {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  };
  async run(action: () => Promise<void>) {
    const generation = this.sessionGeneration;
    this.busyCount++;
    this.patch({ busy: true, error: null });
    try {
      await action();
    } catch (error) {
      if (
        !(error instanceof StaleSessionError) &&
        !(error instanceof DOMException && error.name === 'AbortError') &&
        generation === this.sessionGeneration
      )
        this.patch({ error: errorMessage(error) });
    } finally {
      this.busyCount--;
      this.patch({ busy: this.busyCount > 0 });
    }
  }
  applySession(session: Session) {
    const changed =
      (session.user?.id ?? null) !== (this.state.session.user?.id ?? null) ||
      [...(session.user?.roles ?? [])].sort().join(',') !==
        [...(this.state.session.user?.roles ?? [])].sort().join(',');
    if (changed) {
      bridge.send('game.session.clear');
      this.sessionGeneration++;
      this.api.reset();
      this.actionKeys.clear();
      this.patch({
        cart: emptyCart,
        checkoutOptions: null,
        quote: null,
        order: null,
        qr: null,
        verification: null,
        history: [],
        activity: null,
        inventory: [],
        historyCursor: null,
        inventoryCursor: null,
        ...(!session.authenticated && this.state.screen !== 'gallery' && this.state.screen !== 'product'
          ? { screen: 'login' as Screen }
          : {}),
      });
    }
    this.api.setSession(session.csrfToken ?? '');
    this.patch({ session });
  }
  async bootstrap() {
    const initialInspection = this.inspection;
    await this.run(async () => {
      const configuration = this.api.request<StorefrontConfig>('/storefront/config', 'GET', undefined, false);
      const [config, catalog] = await Promise.all([
        configuration,
        this.api.request<Page<Product>>('/catalog/products?limit=24', 'GET', undefined, false),
        this.refreshSession(configuration),
      ]);
      this.patch({ config, products: catalog.items, catalogCursor: catalog.nextCursor });
      if (this.state.session.authenticated) {
        await this.loadCart();
        const saved = this.readStorage('order');
        if (initialInspection === this.inspection && saved && /^[-a-f0-9]{36}$/i.test(saved))
          await this.openOrder(saved, true);
        else if (this.state.screen === 'login') this.navigate(this.afterLoginScreen);
      }
    });
  }
  refreshSession(configuration?: Promise<StorefrontConfig>): Promise<void> {
    if (!this.automaticSignInAllowed) return Promise.resolve();
    if (this.sessionSync)
      return this.sessionSyncRequest === this.sessionRequest
        ? this.sessionSync
        : this.sessionSync.then(() => this.refreshSession());
    const sequence = ++this.sessionRequest;
    this.sessionSyncRequest = sequence;
    this.patch({ sessionSyncing: true });
    const pending = (async () => {
      try {
        const previousOwner = this.state.session.user?.id;
        const previousRoles = [...(this.state.session.user?.roles ?? [])].sort().join(',');
        let session = await this.api.request<Session>('/session', 'GET', undefined, false);
        if (sequence !== this.sessionRequest) return;
        if (!session.authenticated) {
          // POST exchanges the existing HttpOnly MANGOs cookie. Only the
          // backend's /users/me verification establishes shopper identity.
          this.applySession(session);
          const config = configuration ? await configuration : this.state.config;
          if (sequence !== this.sessionRequest) return;
          const localSimulation = config?.environment === 'Local' && config.mockSimulation;
          if (
            this.automaticSignInAllowed &&
            !localSimulation &&
            (previousOwner || Date.now() >= this.nextAutomaticSignInAt)
          ) {
            this.nextAutomaticSignInAt = Date.now() + 60000;
            try {
              session = await this.api.request<Session>('/session', 'POST', undefined, false);
            } catch (error) {
              if (!(error instanceof ApiError && error.status === 401)) throw error;
            }
          }
        }
        if (sequence !== this.sessionRequest) return;
        this.applySession(session);
        if (
          session.authenticated &&
          (previousOwner !== session.user?.id ||
            previousRoles !== [...(session.user?.roles ?? [])].sort().join(','))
        ) {
          await this.loadCart();
          if (sequence === this.sessionRequest && this.state.screen === 'login')
            this.navigate(this.afterLoginScreen);
        }
      } catch {
        // A guest remains a guest. A failed GET retains a valid visible
        // session; known-invalid credentials above have already cleared it.
      }
    })();
    const completed = pending.finally(() => {
      if (this.sessionSync === completed) {
        this.sessionSync = null;
        this.patch({ sessionSyncing: false });
      }
    });
    this.sessionSync = completed;
    return completed;
  }
  reconnectSession() {
    this.automaticSignInAllowed = true;
    this.nextAutomaticSignInAt = 0;
    return this.refreshSession();
  }
  endSession() {
    this.sessionRequest++;
    this.automaticSignInAllowed = false;
    this.applySession({ authenticated: false });
  }
  private owner() {
    return this.state.session.user?.id ?? '';
  }
  private readStorage(key: string) {
    try {
      return sessionStorage.getItem(`mango-ttb:${this.owner()}:${key}`);
    } catch {
      return null;
    }
  }
  private saveStorage(key: string, value: string | null) {
    try {
      const scoped = `mango-ttb:${this.owner()}:${key}`;
      if (value === null) sessionStorage.removeItem(scoped);
      else sessionStorage.setItem(scoped, value);
    } catch {
      /* storage may be disabled; server idempotency still applies */
    }
  }
  pendingCheckout(): PendingCheckout | null {
    try {
      const raw = this.readStorage('checkout');
      const pending = raw ? (JSON.parse(raw) as PendingCheckout) : null;
      return pending?.owner === this.owner() &&
        /^[-a-f0-9]{36}$/i.test(pending.quoteId) &&
        /^[-a-f0-9]{36}$/i.test(pending.key)
        ? pending
        : null;
    } catch {
      return null;
    }
  }
  async signInLocal(subject: string) {
    await this.run(async () => {
      if (!this.state.config?.mockSimulation || this.state.config.environment !== 'Local')
        throw new Error('Local identity unavailable');
      this.sessionRequest++;
      this.applySession(await this.api.request<Session>('/session/local', 'POST', { subject }, false));
      await this.loadCart();
      if (this.state.screen === 'login') this.navigate(this.afterLoginScreen);
      this.patch({ notice: 'เข้าสู่ระบบทดสอบแล้ว' });
    });
  }
  async signIn() {
    await this.run(async () => {
      this.automaticSignInAllowed = true;
      try {
        this.sessionRequest++;
        this.applySession(await this.api.request<Session>('/session', 'POST', undefined, false));
        await this.loadCart();
        if (this.state.screen === 'login') this.navigate(this.afterLoginScreen);
      } catch (error) {
        if (error instanceof ApiError && error.status === 401 && bridge.attached) {
          bridge.requestLogin();
          this.patch({ notice: 'กรุณาเข้าสู่ระบบ MANGOs ในเมตาเวิร์ส แล้วกดเชื่อมต่ออีกครั้ง' });
          return;
        }
        throw error;
      }
    });
  }
  async signOut() {
    await this.run(async () => {
      this.sessionRequest++;
      const previouslyAllowed = this.automaticSignInAllowed;
      this.automaticSignInAllowed = false;
      try {
        await this.api.request('/session/logout', 'POST');
      } catch (error) {
        this.automaticSignInAllowed = previouslyAllowed;
        throw error;
      }
      this.applySession({ authenticated: false });
      this.patch({ screen: 'gallery', selected: null, notice: 'ออกจากระบบซื้อสินค้าแล้ว' });
    });
  }
  navigate(screen: Screen) {
    this.inspection++;
    if (
      !this.state.session.authenticated &&
      ['cart', 'checkout', 'payment', 'history', 'inventory'].includes(screen)
    ) {
      this.afterLoginScreen = screen;
      screen = 'login';
    } else if (screen === 'login')
      this.afterLoginScreen = this.state.screen === 'product' ? 'product' : 'gallery';
    this.patch({ screen, error: null });
    bridge.send('overlay.state', { open: screen !== 'gallery' });
    if (screen === 'history') void this.run(() => this.loadHistory());
    if (screen === 'inventory') void this.run(() => this.loadInventory());
    if (screen === 'cart') void this.run(() => this.loadCart());
  }
  close() {
    this.navigate('gallery');
    bridge.send('preview.close');
    this.patch({ previewReady: false });
  }
  async loadCatalog() {
    if (!this.state.catalogCursor) return;
    await this.run(async () => {
      const page = await this.api.request<Page<Product>>(
        `/catalog/products?limit=24&cursor=${encodeURIComponent(this.state.catalogCursor!)}`,
        'GET',
        undefined,
        false,
      );
      this.patch({ products: [...this.state.products, ...page.items], catalogCursor: page.nextCursor });
    });
  }
  async inspect(idOrKey: string) {
    const sequence = ++this.inspection;
    await this.run(async () => {
      const path = /^[-a-f0-9]{36}$/i.test(idOrKey)
        ? '/catalog/products/' + idOrKey
        : '/catalog/by-model/' + encodeURIComponent(idOrKey);
      const product = await this.api.request<Product>(path, 'GET', undefined, false);
      if (sequence !== this.inspection) throw new StaleSessionError();
      this.patch({ selected: product, screen: 'product', previewReady: false });
      bridge.send('overlay.state', { open: true });
    });
    if (this.state.screen === 'gallery') bridge.send('overlay.state', { open: false });
  }
  async loadCart() {
    this.patch({ cart: await this.api.request<Cart>('/cart') });
  }
  async setQuantity(variantId: string, quantity: number) {
    await this.run(async () => {
      try {
        this.patch({
          cart: await this.api.request<Cart>(`/cart/lines/${variantId}`, 'PUT', {
            quantity,
            expectedVersion: this.state.cart.version,
          }),
          quote: null,
        });
      } catch (error) {
        if (error instanceof ApiError && error.status === 409) await this.loadCart();
        throw error;
      }
    });
  }
  async add(product: Product, variantId: string, quantity: number) {
    if (!this.state.session.authenticated) {
      this.navigate('login');
      return;
    }
    const current = this.state.cart.lines.find((l) => l.variantId === variantId)?.quantity ?? 0;
    await this.setQuantity(variantId, Math.min(99, current + quantity));
    if (!this.state.error) this.patch({ notice: message('เพิ่ม {0} ลงตะกร้าแล้ว', [product.name]) });
  }
  async checkout() {
    await this.run(async () => {
      const [cart, options] = await Promise.all([
        this.api.request<Cart>('/cart'),
        this.api.request<CheckoutOptions>('/checkout/options'),
      ]);
      this.patch({ cart, checkoutOptions: options, quote: null, screen: 'checkout' });
    });
  }
  async quote(input: CheckoutInput) {
    await this.run(async () => {
      this.patch({ quote: await this.api.request<Quote>('/checkout/quote', 'POST', input) });
    });
  }
  clearQuote() {
    if (this.state.quote) this.patch({ quote: null });
  }
  async createOrder(recover = false) {
    await this.run(async () => {
      const pending = this.pendingCheckout();
      const quote = this.state.quote;
      if (pending && !recover && quote && quote.id !== pending.quoteId)
        throw new ApiError(409, 'checkout_recovery_required');
      if (!pending && (!quote || new Date(quote.expiresAt).getTime() <= Date.now()))
        throw new ApiError(409, 'quote_expired');
      const operation = pending ?? { owner: this.owner(), quoteId: quote!.id, key: crypto.randomUUID() };
      this.saveStorage('checkout', JSON.stringify(operation));
      let order: Order;
      try {
        order = await this.api.request<Order>('/orders', 'POST', { quoteId: operation.quoteId }, true, {
          'Idempotency-Key': operation.key,
        });
      } catch (error) {
        if (
          error instanceof ApiError &&
          ['quote_expired', 'quote_changed', 'insufficient_stock', 'quote_consumed'].includes(error.code)
        ) {
          this.saveStorage('checkout', null);
          this.patch({ quote: null });
          await this.loadCart();
        }
        throw error;
      }
      this.saveStorage('order', order.id);
      this.saveStorage('checkout', null);
      this.patch({ order, quote: null, screen: 'payment', notice: null });
      await this.loadCart();
      const qr = await this.api.request<PaymentQr>(`/orders/${order.id}/qr`, 'POST');
      this.patch({ qr, verification: null });
    });
  }
  async openOrder(orderId: string, resume = false) {
    const order = await this.api.request<Order>(`/orders/${orderId}`);
    this.patch({ order, qr: null, verification: null });
    if (resume || (order.paymentState === 'Unpaid' && order.state === 'Open')) {
      this.patch({ screen: 'payment' });
      if (order.canCancel && new Date(order.expiresAt).getTime() > Date.now())
        this.patch({ qr: await this.api.request<PaymentQr>(`/orders/${orderId}/qr`, 'POST') });
    } else await this.loadActivity(orderId);
  }
  async refreshOrder() {
    const id = this.state.order?.id;
    if (!id) return;
    const order = await this.api.request<Order>(`/orders/${id}`);
    if (this.state.order?.id !== id) return;
    this.patch({ order });
    if (order.paymentState !== 'Unpaid' || order.state !== 'Open') {
      this.saveStorage('order', null);
      if (order.paymentState === 'Paid')
        this.patch({ notice: 'ระบบยืนยันการชำระเงินแล้ว กำลังจัดเตรียมสินค้าและเอกสาร' });
      if (this.state.screen === 'payment') await this.loadActivity(id);
    }
  }
  async verify(method = 'verify') {
    await this.run(async () => {
      if (!this.state.order) return;
      const result = await this.api.request<Verification>(`/orders/${this.state.order.id}/verify`, 'POST', {
        method,
      });
      this.patch({
        verification: result,
        notice:
          result.outcome === 'Found'
            ? 'พบรายการชำระเงินแล้ว'
            : 'ยังไม่พบผลยืนยัน กรุณารอหรือตรวจประวัติอีกครั้ง',
      });
      await this.refreshOrder();
    });
  }
  async simulate(scenario: string) {
    await this.run(async () => {
      const order = this.state.order;
      if (!order || !this.state.config?.mockSimulation || this.state.config.environment !== 'Local') return;
      const scope = `${order.id}:${scenario}`;
      let requestKey = this.actionKeys.get(scope);
      if (!requestKey) {
        requestKey = crypto.randomUUID();
        this.actionKeys.set(scope, requestKey);
      }
      await this.api.request(`/orders/${order.id}/mock-pay`, 'POST', { requestKey, scenario });
      await this.refreshOrder();
    });
  }
  async cancel() {
    await this.run(async () => {
      if (!this.state.order) return;
      const order = await this.api.request<Order>(`/orders/${this.state.order.id}/cancel`, 'POST');
      this.saveStorage('order', null);
      this.patch({ order, qr: null, notice: 'ยกเลิกคำสั่งซื้อแล้ว' });
      await this.loadActivity(order.id);
    });
  }
  async loadHistory(more = false) {
    const cursor = more ? this.state.historyCursor : null;
    if (more && !cursor) return;
    const page = await this.api.request<Page<Order>>(
      '/orders?limit=20' + (cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''),
    );
    this.patch({
      history: more ? [...this.state.history, ...page.items] : page.items,
      historyCursor: page.nextCursor,
    });
  }
  async loadActivity(orderId: string) {
    const activity = await this.api.request<OrderActivity>(`/orders/${orderId}/activity`);
    this.patch({ activity, screen: 'history' });
    await this.loadHistory();
  }
  async refreshActivity() {
    const id = this.state.activity?.order.id;
    if (!id || this.state.screen !== 'history') return;
    const activity = await this.api.request<OrderActivity>(`/orders/${id}/activity`);
    if (this.state.screen === 'history' && this.state.activity?.order.id === id) this.patch({ activity });
  }
  async loadInventory(more = false) {
    const cursor = more ? this.state.inventoryCursor : null;
    if (more && !cursor) return;
    const page = await this.api.request<Page<Entitlement>>(
      '/inventory?limit=50' + (cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''),
    );
    this.patch({
      inventory: more ? [...this.state.inventory, ...page.items] : page.items,
      inventoryCursor: page.nextCursor,
    });
  }
  async download(document: CommerceDocument, print = false) {
    const target = print ? window.open('about:blank', '_blank') : null;
    if (target) target.opener = null;
    await this.run(async () => {
      try {
        const file = await this.api.file(`/documents/${document.id}/download`);
        const url = URL.createObjectURL(file.blob);
        if (print && target) target.location.href = url;
        else {
          const link = Object.assign(window.document.createElement('a'), {
            href: url,
            download: `${document.number}.pdf`,
          });
          link.click();
        }
        setTimeout(() => URL.revokeObjectURL(url), 120000);
      } catch (error) {
        target?.close();
        throw error;
      }
    });
  }
  async downloadItem(item: Entitlement) {
    await this.run(async () => {
      const file = await this.api.file(`/inventory/${item.id}/download`);
      const url = URL.createObjectURL(file.blob);
      const link = Object.assign(document.createElement('a'), {
        href: url,
        download: file.filename ?? item.name,
      });
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 120000);
    });
  }
  useItem(item: Entitlement) {
    if (
      !this.state.gameReady ||
      !this.state.config?.gameUseReady ||
      !item.canUse ||
      item.kind !== 'InventoryItem'
    )
      return;
    this.patch({ notice: 'กำลังตรวจสิทธิ์กับเซิร์ฟเวอร์เกม' });
    bridge.send('inventory.use', { entitlementId: item.id });
  }
  async authorizeGame(event: HostEvent) {
    await this.run(async () => {
      if (
        event.type !== 'game.challenge' ||
        !this.state.gameReady ||
        !this.state.config?.gameUseReady ||
        !this.state.session.authenticated
      )
        return;
      const ticket = await this.api.request<{ ticket: string; expiresAt: string }>(
        '/session/game-ticket',
        'POST',
        { serverId: event.serverId, challenge: event.challenge, entitlementId: event.entitlementId },
      );
      // One-use, short-lived credential stays in memory and goes only to the owning native game connection.
      bridge.send('game.ticket', {
        requestId: event.requestId,
        entitlementId: event.entitlementId,
        ticket: ticket.ticket,
      });
    });
  }
}
export const commerce = new CommerceStore(
  new CommerceApi(resolveApiBase(window.MANGOS_TTB_CONFIG?.apiBase, location.origin)),
);
export const useCommerce = () =>
  useSyncExternalStore(commerce.subscribe, commerce.snapshot, commerce.snapshot);
