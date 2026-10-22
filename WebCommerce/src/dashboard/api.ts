const root = '/mangoxttb/dashboard/api';
export class DashboardError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}
export async function request<T>(
  path: string,
  signal?: AbortSignal,
  method = 'GET',
  csrf?: string,
): Promise<T> {
  const response = await fetch(root + path, {
    method,
    signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(15000)]) : AbortSignal.timeout(15000),
    credentials: 'include',
    cache: 'no-store',
    headers: { Accept: 'application/json', ...(csrf ? { 'X-CSRF-Token': csrf } : {}) },
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new DashboardError(response.status, error.code ?? error.extensions?.code ?? 'request_failed');
  }
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}
export const money = (minor: number) =>
  new Intl.NumberFormat('th-TH', { style: 'currency', currency: 'THB' }).format(minor / 100);
export const date = (value?: string | null, timeOnly = false) =>
  value
    ? new Intl.DateTimeFormat('th-TH', {
        timeZone: 'Asia/Bangkok',
        calendar: 'gregory',
        ...(timeOnly ? {} : { day: 'numeric', month: 'short', year: 'numeric' }),
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hourCycle: 'h23',
      }).format(new Date(value))
    : '—';
export const labels: Record<string, string> = {
  Paid: 'ชำระแล้ว',
  Unpaid: 'รอชำระ',
  Review: 'รอตรวจสอบ',
  Open: 'เปิดรายการ',
  Completed: 'เสร็จสมบูรณ์',
  Cancelled: 'ยกเลิกแล้ว',
  CancelRequested: 'ขอยกเลิก',
  Confirmed: 'ยืนยันแล้ว',
  Duplicate: 'รับซ้ำ',
  AwaitingPayment: 'รอชำระ',
  Expired: 'หมดอายุ',
  Delivered: 'ส่งสินค้าสำเร็จ',
  Ready: 'พร้อมใช้งาน',
  Found: 'พบรายการ',
  NotFound: 'ไม่พบรายการ',
  Unknown: 'ยังไม่ทราบผล',
  Rejected: 'ไม่ผ่านการตรวจ',
  Observed: 'บันทึกเพื่อทดสอบ',
  Voided: 'ยกเลิกรับชำระแล้ว',
  VoidUnknown: 'รอตรวจผล Void',
  Refunded: 'คืนเงินแล้ว',
  VoidPending: 'กำลังยกเลิกรับชำระ',
};
export const label = (value: string) => labels[value] ?? value;
export const good = (value: string) =>
  ['Paid', 'Confirmed', 'Delivered', 'Ready', 'Found', 'Completed'].includes(value);
