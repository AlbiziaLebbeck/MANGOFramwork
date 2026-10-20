import { useSyncExternalStore } from 'react';
import { commerce, type CommerceStore } from '../store';
import { StaleSessionError } from '../api';
import type { CommerceDocument, Page } from '../types';
import {
  adminTabs,
  allowed,
  type AdminTab,
  type CatalogEntry,
  type CaseSummary,
  type CaseDetail,
  type OrderSummary,
  type AdminOrder,
  type FinanceSummary,
  type VoidReview,
  type VoidOperation,
  type BankReport,
  type ReportDetail,
  type ProductForm,
  type DigitalAsset,
  type BankCredit,
  type ManualRefund,
  type GoodsReview,
  type GoodsDecision,
  type FinanceEvidence,
  type IssuerProfile,
} from './types';

interface AdminState {
  issuer: IssuerProfile | null;
  tab: AdminTab;
  products: CatalogEntry[];
  productCursor: string | null;
  productId: string | null;
  creating: boolean;
  assets: DigitalAsset[];
  orders: OrderSummary[];
  orderCursor: string | null;
  order: AdminOrder | null;
  cases: CaseSummary[];
  caseCursor: string | null;
  detail: CaseDetail | null;
  caseState: string;
  caseOrderId: string | null;
  orderPayment: string;
  summary: FinanceSummary | null;
  reports: BankReport[];
  report: ReportDetail | null;
  reportCursor: string | null;
  review: VoidReview | null;
  manualDraft: { bank: BankCredit; sourceCaseId: string | null } | null;
  goods: GoodsReview | null;
  loaded: boolean;
}
const initial = (): AdminState => ({
  issuer: null,
  tab: 'orders',
  products: [],
  productCursor: null,
  productId: null,
  creating: false,
  assets: [],
  orders: [],
  orderCursor: null,
  order: null,
  cases: [],
  caseCursor: null,
  detail: null,
  caseState: 'Open',
  caseOrderId: null,
  orderPayment: '',
  summary: null,
  reports: [],
  report: null,
  reportCursor: null,
  review: null,
  manualDraft: null,
  goods: null,
  loaded: false,
});
const identity = (shop: CommerceStore) =>
  shop.state.session.authenticated
    ? `${shop.state.session.user?.id}:${[...(shop.state.session.user?.roles ?? [])].sort().join(',')}`
    : '';
export class AdminStore {
  state = initial();
  private listeners = new Set<() => void>();
  private signature: string;
  private epoch = 0;
  private selection = 0;
  private listRequest = 0;
  private keys = new Map<string, { body: string; key: string }>();
  constructor(readonly shop: CommerceStore) {
    this.signature = identity(shop);
    shop.subscribe(() => {
      const next = identity(shop);
      if (next !== this.signature) {
        this.signature = next;
        this.epoch++;
        this.selection++;
        this.listRequest++;
        this.keys.clear();
        this.state = initial();
        this.emit();
      }
    });
  }
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };
  snapshot = () => this.state;
  private emit() {
    for (const listener of this.listeners) listener();
  }
  patch(value: Partial<AdminState>) {
    this.state = { ...this.state, ...value };
    this.emit();
  }
  private async request<T>(path: string, method = 'GET', body?: unknown) {
    const epoch = this.epoch;
    const result = await this.shop.api.request<T>(path, method, body);
    if (epoch !== this.epoch) throw new StaleSessionError();
    return result;
  }
  operation(scope: string, body: unknown) {
    const encoded = JSON.stringify(body);
    let value = this.keys.get(scope);
    if (!value || value.body !== encoded) {
      value = { body: encoded, key: crypto.randomUUID() };
      this.keys.set(scope, value);
    }
    return value.key;
  }
  async start() {
    const tabs = adminTabs.filter((t) => allowed(this.shop.state.session, t.role));
    if (!tabs.length) return;
    await this.selectTab(tabs.find((t) => t.id === this.state.tab)?.id ?? tabs[0].id);
  }
  async selectTab(tab: AdminTab, caseOrderId: string | null = null) {
    if (!allowed(this.shop.state.session, adminTabs.find((t) => t.id === tab)!.role)) return;
    this.selection++;
    this.patch({
      tab,
      caseOrderId,
      caseState: caseOrderId ? '' : this.state.caseState,
      order: null,
      detail: null,
      review: null,
      manualDraft: null,
      goods: null,
      report: null,
      productId: null,
      creating: false,
      loaded: false,
    });
    await this.refresh();
  }
  async refresh(more = false) {
    const sequence = ++this.listRequest;
    const { tab } = this.state;
    const epoch = this.epoch;
    let patch: Partial<AdminState>;
    if (tab === 'catalog') {
      const cursor = more ? this.state.productCursor : null;
      const [products, assets] = await Promise.all([
        this.request<CatalogEntry[]>(
          `/admin/catalog?limit=50${cursor ? '&before=' + encodeURIComponent(cursor) : ''}`,
        ),
        this.request<DigitalAsset[]>('/admin/digital-assets'),
      ]);
      const selected = this.state.productId;
      const list = more ? [...this.state.products, ...products] : [...products];
      if (selected && !products.some((x) => x.product.id === selected)) {
        list.push(await this.request<CatalogEntry>('/admin/catalog/' + selected));
      }
      patch = {
        products: [...new Map(list.map((x) => [x.product.id, x])).values()],
        assets,
        productCursor: products.length === 50 ? products.at(-1)!.product.id : null,
      };
    } else if (tab === 'orders') {
      const cursor = more ? this.state.orderCursor : null;
      const page = await this.request<Page<OrderSummary>>(
        `/admin/orders?limit=50${cursor ? '&before=' + encodeURIComponent(cursor) : ''}${this.state.orderPayment ? '&payment=' + encodeURIComponent(this.state.orderPayment) : ''}`,
      );
      patch = {
        orders: more ? [...this.state.orders, ...page.items] : page.items,
        orderCursor: page.nextCursor,
      };
    } else if (tab === 'finance') {
      const cursor = more ? this.state.caseCursor : null;
      const [page, summary] = await Promise.all([
        this.request<Page<CaseSummary>>(
          `/admin/reconciliation?limit=50${this.state.caseState ? '&state=' + this.state.caseState : ''}${this.state.caseOrderId ? '&orderId=' + encodeURIComponent(this.state.caseOrderId) : ''}${cursor ? '&before=' + encodeURIComponent(cursor) : ''}`,
        ),
        this.request<FinanceSummary>('/admin/finance/summary'),
      ]);
      patch = {
        cases: more ? [...this.state.cases, ...page.items] : page.items,
        caseCursor: page.nextCursor,
        summary,
      };
    } else if (tab === 'issuer') {
      patch = { issuer: await this.request<IssuerProfile>('/admin/issuer-profile') };
    } else {
      const cursor = more ? this.state.reportCursor : null;
      const [rows, summary] = await Promise.all([
        this.request<BankReport[]>(`/admin/reports${cursor ? '?before=' + encodeURIComponent(cursor) : ''}`),
        this.request<FinanceSummary>('/admin/finance/summary'),
      ]);
      patch = {
        reports: more ? [...this.state.reports, ...rows] : rows,
        reportCursor: rows.length === 50 ? rows.at(-1)!.id : null,
        summary,
      };
    }
    if (epoch === this.epoch && sequence === this.listRequest && tab === this.state.tab)
      this.patch({ ...patch, loaded: true });
  }
  async saveIssuer(input: Pick<IssuerProfile, 'name' | 'address' | 'taxId' | 'branch' | 'contact'> & { expectedVersion: number }) {
    const issuer = await this.request<IssuerProfile>('/admin/issuer-profile', 'PUT', input);
    if (this.state.tab === 'issuer') this.patch({ issuer });
  }
  async chooseCase(id: string) {
    const sequence = ++this.selection;
    this.patch({ detail: null, review: null, manualDraft: null, goods: null });
    const detail = await this.request<CaseDetail>('/admin/reconciliation/' + id);
    if (sequence === this.selection) this.patch({ detail });
  }
  async chooseOrder(id: string) {
    const sequence = ++this.selection;
    this.patch({ order: null, review: null, manualDraft: null, goods: null });
    const order = await this.request<AdminOrder>('/admin/orders/' + id);
    if (sequence === this.selection) this.patch({ order });
  }
  async chooseReport(id: string) {
    const sequence = ++this.selection;
    this.patch({ report: null });
    const report = await this.request<ReportDetail>('/admin/reports/' + id);
    if (sequence === this.selection) this.patch({ report });
  }
  back() {
    this.selection++;
    this.patch({
      detail: null,
      order: null,
      report: null,
      review: null,
      manualDraft: null,
      goods: null,
      productId: null,
      creating: false,
    });
  }
  async createManual(amountMinor: string, reason: string) {
    const draft = this.state.manualDraft;
    if (!draft) return;
    const body = { amountMinor, reason, sourceCaseId: draft.sourceCaseId };
    const scope = 'manual-create:' + draft.bank.id;
    const row = await this.request<ManualRefund>(
      `/admin/bank-transactions/${draft.bank.id}/manual-refunds`,
      'POST',
      { ...body, operationId: this.operation(scope, body) },
    );
    this.keys.delete(scope);
    await this.selectTab('finance', row.orderId);
    await this.chooseCase(row.caseId);
    this.shop.patch({ notice: 'เปิดเคสคืนเงินภายนอกแล้ว ยังไม่มีการโอนเงินจากระบบ' });
  }
  async manualAction(
    action: string,
    reason: string,
    declaration?: { externalReference: string; externalPaidAt: string; evidenceIds: string[] },
  ) {
    const row = this.state.detail?.manualRefund;
    if (!row) return;
    const body = { expectedVersion: row.version, action, reason, ...declaration };
    const scope = 'manual:' + row.id + ':' + action;
    await this.request(`/admin/manual-refunds/${row.id}/actions`, 'POST', {
      ...body,
      operationId: this.operation(scope, body),
    });
    this.keys.delete(scope);
    await this.chooseCase(row.caseId);
    await this.refresh();
    this.shop.patch({ notice: 'บันทึกผลตรวจหลักฐานคืนเงินภายนอกแล้ว' });
  }
  async uploadEvidence(file: File) {
    const row = this.state.detail?.manualRefund;
    if (!row) return;
    if (file.size > 20 * 1024 * 1024) throw new Error('finance_evidence_too_large');
    const epoch = this.epoch;
    await this.shop.api.upload<FinanceEvidence>(`/admin/reconciliation/${row.caseId}/evidence`, file);
    if (epoch !== this.epoch) throw new StaleSessionError();
    await this.chooseCase(row.caseId);
    await this.refresh();
  }
  async downloadEvidence(file: FinanceEvidence) {
    const epoch = this.epoch;
    const result = await this.shop.api.file(`/admin/finance-evidence/${file.id}/download`);
    if (epoch !== this.epoch) throw new StaleSessionError();
    const url = URL.createObjectURL(result.blob);
    Object.assign(document.createElement('a'), { href: url, download: file.fileName }).click();
    setTimeout(() => URL.revokeObjectURL(url), 120000);
  }
  async inspectGoods() {
    const id = this.state.detail?.case.id,
      sequence = ++this.selection;
    if (!id) return;
    const goods = await this.request<GoodsReview>(`/admin/reconciliation/${id}/goods-review`);
    if (sequence === this.selection) this.patch({ goods });
  }
  async goodsDecision(body: GoodsDecision) {
    const id = this.state.goods?.caseId;
    if (!id) return;
    const scope = 'goods:' + id;
    await this.request(`/admin/reconciliation/${id}/goods-disposition`, 'POST', {
      ...body,
      operationId: this.operation(scope, body),
    });
    this.keys.delete(scope);
    await this.chooseCase(id);
    await this.refresh();
    this.shop.patch({ notice: 'บันทึกการคืนสินค้าและจัดการสิทธิ์แล้ว' });
  }
  async caseAction(action: string, reason: string) {
    const detail = this.state.detail;
    if (!detail) return;
    const body = { expectedVersion: detail.case.version, action, reason };
    const scope = 'case:' + detail.case.id + ':' + action;
    const operationId = this.operation(scope, body);
    await this.request('/admin/reconciliation/' + detail.case.id + '/actions', 'POST', {
      ...body,
      operationId,
    });
    this.keys.delete(scope);
    await this.chooseCase(detail.case.id);
    await this.refresh();
    this.shop.patch({ notice: 'บันทึกการดำเนินการแล้ว' });
  }
  async inspectVoid(bankId: string) {
    const sequence = ++this.selection;
    const review = await this.request<VoidReview>(`/admin/bank-transactions/${bankId}/void-review`);
    if (sequence === this.selection) this.patch({ review });
  }
  async sendVoid(reason: string, deliveryReviewed: boolean) {
    const review = this.state.review;
    if (!review) return;
    const scope = 'void:' + review.bankTransactionId;
    const body = { reason, deliveryReviewed };
    const operationId = this.operation(scope, body);
    await this.request(`/admin/bank-transactions/${review.bankTransactionId}/void`, 'POST', {
      ...body,
      operationId,
    });
    this.keys.delete(scope);
    await this.inspectVoid(review.bankTransactionId);
    this.shop.patch({ notice: 'บันทึกคำขอคืนเงินแล้ว กรุณาตรวจผลยกเลิก' });
  }
  async enquireVoid(operation: VoidOperation) {
    const bankId = this.state.review?.bankTransactionId;
    await this.request(`/admin/voids/${operation.id}/enquiry`, 'POST');
    if (this.state.order) await this.chooseOrder(this.state.order.order.id);
    if (bankId) await this.inspectVoid(bankId);
    await this.refresh();
  }
  async orderFromCase() {
    const id = this.state.detail?.case.orderId;
    if (!id) return;
    await this.selectTab('orders');
    await this.chooseOrder(id);
  }
  async casesFromOrder() {
    const id = this.state.order?.order.id;
    if (id) await this.selectTab('finance', id);
  }
  async delivery(id: string, input: unknown) {
    await this.request('/admin/fulfillment/' + id + '/actions', 'POST', input);
    if (this.state.order) await this.chooseOrder(this.state.order.order.id);
    await this.refresh();
    this.shop.patch({ notice: 'บันทึกการส่งมอบแล้ว' });
  }
  async saveProduct(form: ProductForm) {
    const entry = this.state.products.find((x) => x.product.id === this.state.productId);
    const result = entry
      ? await this.request<{ id: string }>('/admin/products/' + entry.product.id, 'PUT', {
          ...form,
          expectedVersion: entry.product.version,
        })
      : await this.request<{ id: string }>('/admin/products', 'POST', form);
    await this.refresh();
    if (!this.state.products.some((x) => x.product.id === result.id)) {
      const entry = await this.request<CatalogEntry>('/admin/catalog/' + result.id);
      this.patch({ products: [...this.state.products, entry] });
    }
    this.patch({ productId: result.id, creating: false });
    this.shop.patch({ notice: 'บันทึกสินค้าแล้ว' });
  }
  async publication() {
    const entry = this.state.products.find((x) => x.product.id === this.state.productId);
    if (!entry) return;
    await this.request('/admin/products/' + entry.product.id + '/publication', 'POST', {
      expectedVersion: entry.product.version,
      published: !entry.product.published,
    });
    await this.refresh();
  }
  async saveVariant(id: string | null, input: unknown) {
    const productId = this.state.productId;
    if (!productId) return;
    await this.request(
      `/admin/products/${productId}/variants${id ? '/' + id : ''}`,
      id ? 'PUT' : 'POST',
      input,
    );
    await this.refresh();
    this.shop.patch({ notice: 'บันทึกรุ่นสินค้าแล้ว' });
  }
  async stock(variantId: string, onHandDelta: number, reason: string) {
    const body = { onHandDelta, reason };
    const scope = 'stock:' + variantId;
    const operationId = this.operation(scope, body);
    await this.request(`/admin/variants/${variantId}/stock`, 'POST', { ...body, operationId });
    this.keys.delete(scope);
    await this.refresh();
    this.shop.patch({ notice: 'บันทึกการปรับสต็อกแล้ว' });
  }
  async uploadAsset(file: File) {
    const epoch = this.epoch;
    const saved = await this.shop.api.upload<DigitalAsset>('/admin/digital-assets', file);
    if (epoch !== this.epoch) throw new StaleSessionError();
    this.patch({ assets: await this.request<DigitalAsset[]>('/admin/digital-assets') });
    this.shop.patch({ notice: 'จัดเก็บไฟล์ส่วนตัวแล้ว' });
    return saved;
  }
  async reprocess(reason: string) {
    const id = this.state.report?.report.id;
    if (!id) return;
    await this.request('/admin/reports/' + id + '/reprocess', 'POST', { reason });
    await this.chooseReport(id);
    await this.refresh();
  }
  async download(item: CommerceDocument) {
    const epoch = this.epoch;
    const file = await this.shop.api.file(`/admin/documents/${item.id}/download`);
    if (epoch !== this.epoch) throw new StaleSessionError();
    const url = URL.createObjectURL(file.blob);
    Object.assign(document.createElement('a'), { href: url, download: item.number + '.pdf' }).click();
    setTimeout(() => URL.revokeObjectURL(url), 120000);
  }
}
export const admin = new AdminStore(commerce);
export const useAdmin = () => useSyncExternalStore(admin.subscribe, admin.snapshot, admin.snapshot);
