export interface Page<T> {
  items: T[];
  nextCursor: string | null;
}
export interface Preview {
  modelKey: string;
  scale: number;
  initialRotation: number;
  minZoom: number;
  maxZoom: number;
}
export interface Variant {
  id: string;
  sku: string;
  name: string;
  priceMinor: number;
  active: boolean;
  availableQuantity: number | null;
  options: Record<string, string>;
}
import { locale } from './i18n';

export interface Product {
  id: string;
  slug: string;
  name: string;
  description: string;
  kind: string;
  version: number;
  thumbnailUrl: string;
  preview: Preview;
  variants: Variant[];
}
export interface CartLine {
  variantId: string;
  productId: string;
  sku: string;
  name: string;
  variantName: string;
  kind: string;
  thumbnailUrl: string;
  quantity: number;
  unitPriceMinor: number;
  totalMinor: number;
  available: boolean;
  availableQuantity: number | null;
}
export interface Cart {
  version: number;
  lines: CartLine[];
  subtotalMinor: number;
}
export interface Buyer {
  name: string;
  email?: string | null;
  phone?: string | null;
}
export interface Address {
  line1: string;
  subdistrict: string;
  district: string;
  province: string;
  postalCode: string;
  country: string;
}
export interface Delivery {
  method: string;
  address?: Address | null;
}
export interface CheckoutInput {
  cartVersion: number;
  buyer: Buyer;
  delivery: Delivery;
}
export interface Quote extends CheckoutInput {
  id: string;
  lines: CartLine[];
  subtotalMinor: number;
  shippingMinor: number;
  totalMinor: number;
  currency: string;
  expiresAt: string;
}
export interface OrderLine extends Omit<CartLine, 'available' | 'availableQuantity'> {
  id: string;
}
export interface Order {
  id: string;
  number: string;
  state: string;
  paymentState: string;
  currency: string;
  subtotalMinor: number;
  shippingMinor: number;
  totalMinor: number;
  lines: OrderLine[];
  buyer: Buyer;
  delivery: Delivery;
  createdAt: string;
  expiresAt: string;
  confirmedAt: string | null;
  canCancel: boolean;
}
export interface PaymentQr {
  attemptId: string;
  qrId: string;
  payload: string;
  imageDataUrl: string;
  mode: string;
  beneficiaryName: string;
  amountMinor: number;
  currency: string;
  expiresAt: string;
  issuedAt: string;
  verificationAvailableAt: string;
  checksRemaining: number;
}
export interface Verification {
  orderId: string;
  outcome: string;
  checksUsed: number;
  checksRemaining: number;
  nextCheckAt: string | null;
}
export interface Session {
  authenticated: boolean;
  user?: { id: string; displayName: string; roles: string[] };
  csrfToken?: string;
  expiresAt?: string;
}
export interface StorefrontConfig {
  environment: string;
  paymentMode: string;
  paymentsReady: boolean;
  currency: string;
  verificationDelaySeconds: number;
  verificationBudget: number;
  mockSimulation: boolean;
  gameUseReady: boolean;
}
export interface CheckoutOptions {
  pickupEnabled: boolean;
  shippingEnabled: boolean;
  shippingFeeMinor: number;
  maxCartLines: number;
  maxOrderMinor: number;
  currency: string;
}
export interface Fulfillment {
  id: string;
  orderLineId: string;
  sku: string;
  name: string;
  kind: string;
  quantity: number;
  deliveryMethod: string;
  state: string;
  carrier: string | null;
  trackingNumber: string | null;
  deliveredAt: string | null;
}
export interface CommerceDocument {
  id: string;
  kind: string;
  number: string;
  state: string;
  templateVersion: string;
  issuedAt: string;
  readyAt: string | null;
}
export interface OrderActivity {
  refund?: {
    amountMinor: string;
    source: string;
    latestApprovedAt: string | null;
    goodsReviewPending: boolean;
    returnedLines: { orderLineId: string; quantity: number }[];
  } | null;
  order: Order;
  fulfillment: Fulfillment[];
  documents: CommerceDocument[];
  payment: {
    reference: string;
    confirmedAt: string | null;
    voided: boolean;
    originalBankPaymentTimeKnown: boolean;
  } | null;
}
export interface Entitlement {
  id: string;
  orderId: string;
  orderLineId: string;
  productId: string;
  variantId: string;
  unitIndex: number;
  name: string;
  variantName: string;
  kind: string;
  state: string;
  canUse: boolean;
  canDownload: boolean;
  grantedAt: string;
}
export type Screen =
  'gallery' | 'product' | 'cart' | 'checkout' | 'payment' | 'history' | 'inventory' | 'login' | 'admin';
export const emptyCart: Cart = { version: 0, lines: [], subtotalMinor: 0 };
export const money = (minor: number) =>
  new Intl.NumberFormat(locale(), {
    style: 'currency',
    currency: 'THB',
    currencyDisplay: 'narrowSymbol',
    minimumFractionDigits: 2,
  }).format(minor / 100);
export const date = (iso: string) =>
  new Intl.DateTimeFormat(locale(), { dateStyle: 'medium', timeZone: 'Asia/Bangkok' }).format(new Date(iso));
export const isPhysical = (kind: string) => kind.toLowerCase() === 'physical';
export const hasOpenUnpaidOrder = (
  order: Pick<Order, 'state' | 'paymentState' | 'expiresAt'> | null,
  now = Date.now(),
) =>
  !!(order && order.state === 'Open' && order.paymentState === 'Unpaid' && Date.parse(order.expiresAt) > now);
