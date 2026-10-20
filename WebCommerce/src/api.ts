import { pwa } from './pwa';
export interface OverlayConfiguration {
  apiBase?: string;
  environment?: 'Local' | 'Uat' | 'Production';
  experience?: 'gallery' | 'world';
  pwaEnabled?: boolean;
  assetBase?: string;
  loginUrl?: string;
}
declare global {
  interface Window {
    MANGOS_TTB_CONFIG?: OverlayConfiguration;
  }
}

export function resolveApiBase(value: string | undefined, origin: string): string {
  const url = new URL(value ?? '/ttb-commerce/v1', origin);
  const allowed = new Set([origin, 'https://ttb-api-test.mangosgo.com', 'https://ttb-api.mangosgo.com']);
  if (
    !allowed.has(url.origin) ||
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    !/^\/(ttb-commerce|ttb-commerce-test)\/v1\/?$/.test(url.pathname)
  )
    throw new Error('Invalid commerce API configuration');
  return url.href.replace(/\/$/, '');
}
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}
export class StaleSessionError extends Error {}
async function fetchApi(input: RequestInfo | URL, options: RequestInit) {
  try {
    const response = await fetch(input, options);
    pwa.networkSucceeded();
    return response;
  } catch (error) {
    if (error instanceof TypeError) pwa.networkFailed();
    throw error;
  }
}
export class CommerceApi {
  private epoch = 0;
  private csrf = '';
  private pending = new Set<AbortController>();
  onUnauthorized: () => void = () => {};
  constructor(readonly base: string) {}
  setSession(csrf: string) {
    this.csrf = csrf;
  }
  reset() {
    this.epoch++;
    this.csrf = '';
    for (const controller of this.pending) controller.abort();
    this.pending.clear();
  }
  async request<T>(
    path: string,
    method = 'GET',
    body?: unknown,
    owned = true,
    headers: Record<string, string> = {},
  ): Promise<T> {
    if (method !== 'GET' && pwa.state.applying) throw new ApiError(409, 'app_updating');
    const stamp = this.epoch;
    const controller = new AbortController();
    if (owned) this.pending.add(controller);
    try {
      const response = await fetchApi(this.base + path, {
        method,
        credentials: 'include',
        cache: 'no-store',
        signal: controller.signal,
        headers: {
          Accept: 'application/json',
          ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
          ...(method === 'GET' ? {} : { 'X-CSRF-Token': this.csrf }),
          ...headers,
        },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (owned && stamp !== this.epoch) throw new StaleSessionError();
      if (!response.ok) {
        let code = 'request_failed';
        try {
          const error = await response.json();
          code = error.code ?? error.extensions?.code ?? code;
        } catch {}
        if (response.status === 401 && owned) this.onUnauthorized();
        throw new ApiError(response.status, code);
      }
      const result = response.status === 204 ? undefined : await response.json();
      if (owned && stamp !== this.epoch) throw new StaleSessionError();
      return result as T;
    } finally {
      this.pending.delete(controller);
    }
  }
  async file(path: string): Promise<{ blob: Blob; filename: string | null }> {
    const stamp = this.epoch;
    const controller = new AbortController();
    this.pending.add(controller);
    try {
      const response = await fetchApi(this.base + path, {
        credentials: 'include',
        cache: 'no-store',
        signal: controller.signal,
      });
      if (!response.ok) {
        if (response.status === 401) this.onUnauthorized();
        throw new ApiError(response.status, 'document_unavailable');
      }
      const blob = await response.blob();
      if (stamp !== this.epoch) throw new StaleSessionError();
      return { blob, filename: downloadFilename(response.headers.get('Content-Disposition')) };
    } finally {
      this.pending.delete(controller);
    }
  }
  async upload<T>(path: string, file: File): Promise<T> {
    if (pwa.state.applying) throw new ApiError(409, 'app_updating');
    if (!file.size || file.size > 104857600) throw new ApiError(413, 'asset_too_large');
    const stamp = this.epoch,
      controller = new AbortController();
    this.pending.add(controller);
    try {
      const response = await fetchApi(this.base + path, {
        method: 'POST',
        credentials: 'include',
        cache: 'no-store',
        signal: controller.signal,
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/octet-stream',
          'X-CSRF-Token': this.csrf,
          'X-File-Name': encodeURIComponent(file.name),
        },
        body: file,
      });
      if (stamp !== this.epoch) throw new StaleSessionError();
      if (!response.ok) {
        if (response.status === 401) this.onUnauthorized();
        throw new ApiError(response.status, 'asset_upload_failed');
      }
      const result = await response.json();
      if (stamp !== this.epoch) throw new StaleSessionError();
      return result as T;
    } finally {
      this.pending.delete(controller);
    }
  }
}
export function downloadFilename(header: string | null): string | null {
  if (!header) return null;
  let value: string | undefined;
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  try {
    value = utf8
      ? decodeURIComponent(utf8[1])
      : (/filename="([^"]+)"/i.exec(header)?.[1] ?? /filename=([^;]+)/i.exec(header)?.[1]);
  } catch {
    return null;
  }
  if (!value) return null;
  const name = value.replace(/\\/g, '/').split('/').at(-1)?.trim();
  return name && name.length <= 250 && !/[\x00-\x1f\x7f]/.test(name) ? name : null;
}
const errors: Record<string, string> = {
  recovery_frozen: 'ระบบกำลังตรวจข้อมูลหลังการกู้คืน ขณะนี้เปิดดูได้อย่างเดียว กรุณาลองภายหลัง',
  manual_refund_credit_exhausted: 'ยอดนี้ถูกกันไว้หรือคืนแล้ว กรุณาอัปเดตรายการรับเงิน',
  manual_refund_self_approval: 'ต้องให้เจ้าหน้าที่ฝ่ายการเงินอีกคนตรวจและอนุมัติ',
  manual_refund_policy_unapproved: 'ยังไม่มีนโยบายรับรองการคืนเงินภายนอกที่อนุมัติ',
  manual_refund_evidence_invalid: 'กรุณาตรวจไฟล์หลักฐานและข้อมูลการโอนอีกครั้ง',
  goods_policy_unapproved: 'ยังไม่มีนโยบายคืนสินค้าและเพิกถอนสิทธิ์ที่อนุมัติ',
  goods_consumed_review_required: 'ต้องยืนยันการตรวจสินค้าที่ดาวน์โหลดหรือใช้แล้ว',
  goods_digital_restock_requires_revocation: 'จำนวนคืนเข้าสต็อกต้องไม่เกินหน่วยดิจิทัลที่เลือกเพิกถอน',
  goods_return_quantity_invalid: 'จำนวนรับคืนเกินยอดซื้อที่ยังไม่เคยรับคืน',
  finance_evidence_too_large: 'ไฟล์หลักฐานต้องไม่เกิน 20 MB',
  offline: 'ต้องเชื่อมต่ออินเทอร์เน็ตเพื่อดำเนินการ',
  app_updating: 'กำลังอัปเดตแอป กรุณารอสักครู่',
  login_required: 'กรุณาเข้าสู่ระบบ MANGOs ก่อนดำเนินการ',
  csrf_invalid: 'เซสชันเปลี่ยน กรุณาเข้าสู่ระบบอีกครั้ง',
  insufficient_stock: 'สินค้าบางรายการหมด กรุณาตรวจตะกร้าอีกครั้ง',
  cart_changed: 'ตะกร้ามีการเปลี่ยนแปลง กรุณาตรวจรายการล่าสุด',
  issuer_profile_changed: 'ข้อมูลบริษัทมีการเปลี่ยนแปลง กรุณากดอัปเดตรายการก่อนบันทึกใหม่',
  invalid_issuer_profile: 'กรอกข้อมูลบริษัทให้ครบ และใช้เลขผู้เสียภาษี 13 หลัก',
  issuer_deployment_managed: 'ข้อมูลบริษัท Production ต้องแก้ผ่านค่าตั้งระบบที่รับรองแล้ว',
  quote_changed: 'ข้อมูลสินค้าหรือการรับสินค้าเปลี่ยน กรุณาตรวจสอบยอดใหม่',
  quote_expired: 'ยอดยืนยันหมดอายุ กรุณาตรวจสอบยอดใหม่',
  bank_unavailable: 'ระบบชำระเงินยังไม่พร้อม กรุณาลองใหม่ภายหลัง',
  bank_circuit_open: 'บริการธนาคารขัดข้องชั่วคราว กรุณารอสักครู่แล้วตรวจสอบใหม่',
  verification_too_early: 'ยังไม่ถึงเวลาตรวจสอบ กรุณารอสักครู่',
  verification_budget_exhausted: 'ใช้สิทธิ์ตรวจสอบครบแล้ว กรุณารอระบบหรือแจ้งผู้ดูแล',
  document_pending: 'กำลังจัดทำใบเสร็จ กรุณาลองอีกครั้ง',
  document_unavailable: 'เอกสารยังไม่พร้อม กรุณาลองอีกครั้ง',
  cannot_cancel: 'รายการนี้ต้องให้ผู้ดูแลตรวจสอบ',
  idempotency_conflict: 'ข้อมูลคำสั่งซื้อเปลี่ยน กรุณาเปิดประวัติรายการ',
};
export const errorMessage = (error: unknown) =>
  error instanceof ApiError
    ? (errors[error.code] ??
      (error.status === 401
        ? 'กรุณาเข้าสู่ระบบอีกครั้ง'
        : error.status === 409
          ? 'ข้อมูลเปลี่ยน กรุณาตรวจรายการล่าสุด'
          : error.status === 503
            ? 'บริการยังไม่พร้อม กรุณาลองใหม่ภายหลัง'
            : 'ดำเนินการไม่สำเร็จ กรุณาลองอีกครั้ง'))
    : 'เชื่อมต่อไม่สำเร็จ กรุณาตรวจเครือข่ายแล้วลองอีกครั้ง';
