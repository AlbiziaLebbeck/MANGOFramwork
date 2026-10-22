export interface DashboardSession {
  authenticated: boolean;
  canRead?: boolean;
  displayName?: string;
  csrfToken?: string;
  environment?: string;
}
export interface OrderRow {
  id: string;
  number: string;
  amountMinor: number;
  paymentState: string;
  state: string;
  createdAt: string;
  confirmedAt: string | null;
}
export interface Notice {
  id: string;
  orderNumber: string | null;
  orderId: string | null;
  source: string;
  outcome: string;
  requestUid: string;
  peerIp: string | null;
  qrId: string | null;
  instructionId: string | null;
  ackCode: string | null;
  deliveryCount: number | null;
  createdAt: string;
}
export interface Overview {
  environment: string;
  fetchedAt: string;
  summary: { orders: number; paid: number; notifications: number; tests: number };
  orders: OrderRow[];
  nextCursor: string | null;
}
export interface NoticePage {
  items: Notice[];
  nextCursor: string | null;
  fetchedAt: string;
}
export interface Evidence {
  id: string;
  source: string;
  outcome: string;
  requestUid: string;
  createdAt: string;
  peerIp: string | null;
}
export interface OrderDetail {
  order: OrderRow;
  attempts: { id: string; qrId: string; ref1: string; ref2: string; state: string; expiresAt: string }[];
  evidence: Evidence[];
  callbacks: {
    instructionId: string;
    ackCode: string;
    deliveryCount: number;
    lastReceivedAt: string;
    observationId: string;
  }[];
  payments: { instructionId: string; qrId: string; amountMinor: number; voided: boolean }[];
  fulfillment: { state: string; kind: string; createdAt: string; deliveredAt: string | null }[];
  documents: { number: string; state: string; kind: string }[];
  fetchedAt: string;
}
export interface TestCase {
  id: string;
  ref1: string;
  ref2: string;
  qrId: string;
  amountMinor: number;
  state: string;
  origin: string;
  instructionId: string | null;
  createdAt: string;
  expiresAt: string | null;
}
export interface TestEvent {
  id: string;
  caseId: string | null;
  kind: string;
  outcome: string;
  ref1: string;
  qrId: string;
  instructionId: string;
  amountMinor: number | null;
  requestUid: string;
  httpStatus: number | null;
  bankCode: string | null;
  createdAt: string;
  payloadJson: string;
  responseJson: string;
}
export interface TestPage {
  cases: TestCase[];
  events: TestEvent[];
}
