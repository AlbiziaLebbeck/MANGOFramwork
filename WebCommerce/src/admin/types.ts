import type { CommerceDocument, Fulfillment, Order, Preview, Session } from '../types';
import { t, locale } from '../i18n';
export type AdminTab = 'catalog' | 'orders' | 'finance' | 'reports' | 'issuer';
export const adminTabs: { id: AdminTab; label: string; role: string }[] = [
  { id: 'catalog', label: 'สินค้า', role: 'admin' },
  { id: 'orders', label: 'คำสั่งซื้อ', role: 'operations' },
  { id: 'finance', label: 'การเงิน', role: 'finance' },
  { id: 'reports', label: 'รายงาน', role: 'finance' },
  { id: 'issuer', label: 'ข้อมูลบริษัท', role: 'admin' },
];
export interface IssuerProfile {
  version: number;
  name: string;
  address: string;
  taxId: string;
  branch: string;
  contact: string;
  editable: boolean;
  approved: boolean;
}
export const allowed = (session: Session, role: string) =>
  !!session.authenticated &&
  !!session.user &&
  (session.user.roles.includes('admin') ||
    (role === 'operations'
      ? session.user.roles.some((r) => ['finance', 'fulfillment'].includes(r))
      : session.user.roles.includes(role)));
export const canAdmin = (session: Session) => adminTabs.some((tab) => allowed(session, tab.role));
export interface AdminProduct {
  id: string;
  slug: string;
  name: string;
  description: string;
  kind: string;
  version: number;
  published: boolean;
  thumbnailUrl: string;
  previewJson: string;
  taxCode: string;
}
export interface AdminVariant {
  id: string;
  productId: string;
  sku: string;
  name: string;
  priceMinor: number;
  active: boolean;
  trackStock: boolean;
  onHand: number;
  reserved: number;
  optionsJson: string;
  digitalKind: string;
  digitalAssetKey: string | null;
}
export interface CatalogEntry {
  product: AdminProduct;
  variants: AdminVariant[];
}
export interface CaseSummary {
  id: string;
  version: number;
  kind: string;
  state: string;
  orderId: string | null;
  orderNumber: string | null;
  bankTransactionId: string | null;
  amountMinor: string | null;
  instructionId: string | null;
  assignee: string | null;
  createdAt: string;
  resolvedAt: string | null;
}
export interface BankCredit {
  id: string;
  instructionId: string;
  qrId: string;
  billerNo: string;
  ref1: string;
  ref2: string;
  amountMinor: string;
  voided: boolean;
  crossBorderFlag: string | null;
  currencyCode: string | null;
  originalCreditAt: string | null;
  timeSource: string;
  allocatedOrderId: string | null;
  primary: boolean | null;
  manualAttestedMinor: string;
  manualReservedMinor: string;
}
export interface CaseDetail {
  case: CaseSummary;
  bank: BankCredit | null;
  evidenceJson: string;
  resolutionJson: string | null;
  notes: { id: string; actor: string; body: string; createdAt: string }[];
  manualRefund: ManualRefund | null;
}
export interface FinanceEvidence {
  id: string;
  fileName: string;
  byteLength: number;
  sha256: string;
  uploadedBy: string;
  uploadedAt: string;
}
export interface ManualRefund {
  id: string;
  caseId: string;
  bankTransactionId: string;
  orderId: string | null;
  version: number;
  state: 'Review' | 'EvidenceSubmitted' | 'Attested' | 'Cancelled';
  amountMinor: string;
  originalCreditMinor: string;
  affectsOrderPayment: boolean;
  createdBy: string;
  reason: string;
  submittedBy: string | null;
  submittedAt: string | null;
  externalReference: string | null;
  externalPaidAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  decisionReason: string | null;
  policyApproved: boolean;
  canApprove: boolean;
  files: FinanceEvidence[];
}
export interface GoodsReview {
  caseId: string;
  caseVersion: number;
  orderId: string;
  source: string;
  fullRefund: boolean;
  policyApproved: boolean;
  paymentState: string;
  lines: {
    id: string;
    name: string;
    kind: string;
    purchasedQuantity: number;
    deliveryState: string | null;
    deliveryQuantity: number | null;
    trackStock: boolean;
    restockedQuantity: number;
    maximumRestockQuantity: number;
  }[];
  digitalUnits: {
    orderLineId: string;
    unitIndex: number;
    name: string;
    kind: string;
    entitlementId: string | null;
    state: string;
    alreadyDisposed: boolean;
    downloadRequested: boolean;
    gameUsed: boolean;
  }[];
}
export interface GoodsDecision {
  expectedCaseVersion: number;
  reason: string;
  cancelUndelivered: boolean;
  revokeDigitalUnits: { orderLineId: string; unitIndex: number }[];
  restock: { orderLineId: string; quantity: number }[];
  consumedGoodsReviewed: boolean;
}
export interface OrderSummary {
  id: string;
  number: string;
  state: string;
  paymentState: string;
  buyerName: string;
  totalMinor: string;
  createdAt: string;
}
export interface AdminFulfillment extends Fulfillment {
  version: number;
  shippedAt: string | null;
}
export interface AdminOrder {
  order: Order;
  fulfillment: AdminFulfillment[];
  documents: CommerceDocument[];
  credits: BankCredit[];
}
export interface FinanceSummary {
  environment: string;
  paymentMode: string;
  paymentsReady: boolean;
  openCases: number;
  unknownVoids: number;
  reportsState: string;
  waitingFiles: number | null;
  voidsAvailable: boolean;
}
export interface VoidOperation {
  id: string;
  orderId: string | null;
  transactionId: string;
  state: string;
  resultCode: string | null;
  enquiriesUsed: number;
  enquiriesRemaining: number;
  requestedAt: string;
  confirmedAt: string | null;
  confirmationSource: string | null;
}
export interface VoidReview {
  bankTransactionId: string;
  orderId: string | null;
  amountMinor: string;
  affectsOrderPayment: boolean;
  deliveryReviewRequired: boolean;
  fulfilledPolicyApproved: boolean;
  eligibility: {
    eligible: boolean;
    code: string;
    earliestPaymentAt: string | null;
    latestPaymentAt: string | null;
    windowEndsAt: string | null;
  };
  operations: VoidOperation[];
  documents: CommerceDocument[];
}
export interface BankReport {
  id: string;
  fileName: string;
  fileDate: string;
  kind: string;
  revision: number;
  state: string;
  failureCode: string | null;
  encryptedSha256: string;
  plaintextSha256: string | null;
  replayOfId: string | null;
  createdAt: string;
  completedAt: string | null;
}
export interface ReportDetail {
  report: Pick<
    BankReport,
    | 'id'
    | 'fileName'
    | 'state'
    | 'revision'
    | 'failureCode'
    | 'encryptedSha256'
    | 'plaintextSha256'
    | 'replayOfId'
  > & { totalsJson: string; comparisonJson: string; parserProfileJson: string; appliedProfileJson: string };
  outcomes: { outcome: string; count: number }[];
}
export interface DigitalAsset {
  id: string;
  key: string;
  fileName: string;
  byteLength: number;
  sha256: string;
  ready: boolean;
}
export interface ProductForm {
  slug: string;
  name: string;
  description: string;
  kind: string;
  thumbnailUrl: string;
  taxCode: string;
  preview: Preview;
}
export const exactMoney = (minor: string | number) => {
  const value = BigInt(minor),
    negative = value < 0n,
    absolute = negative ? -value : value;
  return `${negative ? '-' : ''}฿${(absolute / 100n).toLocaleString(locale())}.${(absolute % 100n).toString().padStart(2, '0')}`;
};
export const minorInput = (baht: string) => {
  if (!/^\d{1,10}(\.\d{1,2})?$/.test(baht)) throw new Error('invalid_amount');
  const [whole, fraction = ''] = baht.split('.');
  const value = Number(BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0')));
  if (!Number.isSafeInteger(value) || value < 1 || value > 1_000_000_000_000)
    throw new Error('invalid_amount');
  return value;
};
export const caseLabels: Record<string, string> = {
  ExtraCredit: 'รับเงินซ้ำ',
  LateOrClosedOrder: 'ชำระหลังหมดเวลา',
  StockReview: 'ตรวจสต็อก',
  UnknownQr: 'ไม่พบคำสั่งซื้อ',
  AmountMismatch: 'ยอดเงินไม่ตรง',
  Ref1Mismatch: 'อ้างอิง 1 ไม่ตรง',
  Ref2Mismatch: 'อ้างอิง 2 ไม่ตรง',
  InstructionConflict: 'หลักฐานขัดแย้ง',
  VoidOperationReview: 'ตรวจผลยกเลิก',
  VoidFulfillmentReview: 'ตรวจการคืนสินค้า',
  ReportedVoidFulfillmentReview: 'ตรวจการคืนสินค้าจากรายงาน',
  CrossBorderReview: 'ตรวจการชำระข้ามพรมแดน',
  NotificationPayloadChanged: 'ข้อมูลแจ้งเตือนเปลี่ยน',
  InvalidNotice: 'ข้อมูลแจ้งเตือนไม่ถูกต้อง',
  BankReportMissing: 'ยังไม่พบไฟล์ธนาคาร',
  ReportContentRevision: 'ไฟล์ธนาคารเปลี่ยน',
  ReportUnallocatedCredit: 'ยอดรับเงินยังไม่จัดสรร',
  ManualRefund: 'คืนเงินภายนอก',
  ManualRefundFulfillmentReview: 'ตรวจสินค้าหลังคืนเงินภายนอก',
  ManualRefundBankVoidConflict: 'หลักฐานคืนเงินภายนอกและธนาคารขัดแย้ง',
};
export const caseLabel = (kind: string) => t(caseLabels[kind] ?? kind);
export const deliveryLabels: Record<string, string> = {
  Pending: 'รอจัดเตรียม',
  Preparing: 'กำลังจัดเตรียม',
  ReadyForPickup: 'พร้อมรับสินค้า',
  Shipped: 'ส่งสินค้าแล้ว',
  Delivered: 'ส่งมอบแล้ว',
  Review: 'พักตรวจสอบ',
  Cancelled: 'ยกเลิก',
};
