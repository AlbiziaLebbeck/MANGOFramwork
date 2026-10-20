# TTB Void and Void document operations

The bank feature defaults disabled, profileunapproved and windowUnknown. No actual bank Void, EnquiryVoid or bank acceptance has been performed. Single-merchant full refunds use the bank's original full InstructionId; there is no partial-amount request field. Public shoppers cannot initiate the finance endpoints.

## Eligibility and policy

`Voids.Enabled` and `ProfileApproved` require explicit provisioned permission. Select `SameCalendarDayBefore2256` or `BankingDay2256` only after bank confirmation of the prose/diagram discrepancy. Default safety margin is30seconds before the22:56Bangkok window end. `Unknown` is unavailable. Cross-borderY and original instructions beginningQ are excluded. Request/worker recheck original payment eligibility; a queue delay past cutoff makes the intentRejected before network.

Original proof is the ISO report's credited-payment timestamp (`PaymentTimeSource=ReportIsoPayment`), or the complete unique-QR issuance→first trusted credit-observation bound. Both endpoints of the bound must be within the selected window, no later than now, with consistent issuance/original/observation chronology. Callback/Verify `TransDate` is system time and cannot extend the window. LOST debit/operation timestamps do not qualify as original-credit timestamps.

The entire allocated order amount is voided, including its original charged shipping. No browser amount changes this. Already shipped/delivered, downloaded or used rights require `FulfilledRefundPolicyApproved` plus an explicit finance delivery acknowledgment. Defaults areclosed. Confirmation preserves stock and delivered history; return/restock decisions remain explicit finance/operations work. Already received files cannot be recalled.

## Durable state machine

Finance/admin supplies a stable UUID operation ID, reason and required acknowledgment with a valid session, Origin and CSRF. The operation key is locked before the original instruction→order locks; the original IDs, full amount and policy proof are saved. The order becomesVoidPending/Review before the send job exists, stopping new dispatch/download/game leases.

TransactionId is17characters: merchant3 plusBangkokyyyyMMddHHmmss. A persisted merchant/second guard permits one such ID per second. Concurrent retries of the same request return the same intent; a changed request under its key conflicts. Only one unresolved/confirmed operation exists per original bank payment. A definitivelyRejected operation is retained; a later finance-authorized attempt has a new operation and a new TransactionId, never an automatic resend.

The worker commits a `SendFencedAt` timestamp, immutable request body/UID andUnknown state before calling the bank. A retried job after this fence never calls MerchantVoid again, including the ambiguous crash-before-send case. Timeout/cancellation/signing/transport/malformed/mismatched responses remainUnknown and keep the money/delivery hold. Raw bounded bank responses and hashes are stored privately; credentials and JWTs are excluded.

Strict echoed TransactionId/InstructionId/QRId/Biller and flat valid JSON are required. Merchant0000 confirms,0002 confirms alreadyvoided with observation wording.0003/0004/0008/0009 are explicit rejections;0001/0005/9999/unknown codes do not authorize automatic retry. Enquiry0000 confirms the original credit was voided; notfound/error is not proof a mutation may be resent. Descriptions alone never set financial state.

`POST /admin/voids/{id}/enquiry` is an explicit bounded finance check using the original TransactionId and a new request UID/current system timestamp. It consumes a persisted count/lease and the shared merchant read-rate budget before network. Default3checks,30secondcooldown, singleflight. No interval bank polling or hidden mutation retry exists. Exhaustion goes to report/bank-support reconciliation. Confirmed or definitivelyRejected operations return their state without a new bank call.

The shared reducer for direct/enquiry and reportD/V proof sets one original creditVoided, revokes rights, holds pending delivery and queues one Void document. A later report can confirm an uncertain intent. Original allocation, receipt, stock movements and shipped/delivered records stay intact. Later callback/Verify cannot resurrect a voided credit. A resolved uncertainty case is separate from any remaining fulfillment/return review.

## Void document and shopper UI

`void-document.ensure` preserves/creates the original allocated receipt first, then creates one immutable Void number/snapshot for the full primary payment. `document.render` renders offline with the same Thai font/private storage/hash/reprint protections. Document kindVoid uses template`void-v1`; an optional omitted-when-null snapshot extension preserves old receipt canonical hashes. No new receipt number is issued by reprinting.

The document includes company/buyer, original receipt/order/credit reference, QR/ref1/ref2, full amount, known void request reference/operator/reason and observed confirmation. Report-origin voids do not invent a transaction ID or operator. Confirmation observation time is labeled as such, not as the time funds reached the payer. Company documents are not represented as bank slips or tax invoices. Local/UAT proofs display test wording.

Owned history distinguishesVoidPending fromVoided, keeps both documents and uses separate Void download/print labels. Bounded visible activity refresh queries only our backend; it does not poll TTB. Inventory no longer offers use/download for revoked rights. Original delivered history remains visible alongside the void status.

Local+Mock has a separate persisted simulator-side void result, so a response can be lost after simulated bank commit and recovered through Enquiry. It cannot run outsideLocal. The local launcher explicitly enables its proposed simulation policy and `local:demo-admin`; no bank/production permission is implied. Default deployed UAT flags remainfalse/Unknown.

## Evidence

- Backend full182/182 without skips, `commerce_net10.0_20261018213248.trx` (2m54s), including concurrency/send fence/crash/cancel, code/echo/JSON, cutoff/time proof, fulfilled policy, rejection/new ID, budget/lease, finance HTTP/CSRF and Locallostreply→Enquiry→no resurrection.
- Real Chromium Thai Void PDF `.artifacts/pdf-qa/void-thai.pdf` is oneA4page and was rendered toPNG/visually checked. Original receipt hash/number remains unchanged after concurrent Void issuance. The new Void snapshot's issue time follows observed confirmation.
- Local real-API mobile390×844 browser1/1 passed19.5s: purchase→Mockcredit→financeVoid→owned history→actual VoidPDF download→revoked digital right, with zero automaticVerify/pageerrors/horizontaloverflow. Both document cards and the full Void download/print controls were viewed. Interactive in-app desktop inspection also verified two documents and explicit PWA update; test tabs were closed.
- Frontend TypeScript/build and11security tests passed; latest IIFE294.56KB(gzip89.43KB). Native gallery/world HTML/PWA packages updated without rebuilding their unchanged tested Unity binaries.

Primary full-order cancellation is implemented here. Unknown/unallocated credits, extra credits and case-driven recovery/refunds require the finance case workflows inP7-03, not an invented primary allocation. Bank UAT and the actual bank/company policy remain external acceptance gates.

Private UAT release`20261018-p7-void` is deployed with real Void disabled/Unknown and no public ingress; KKU remainsready1/1. Its Linux offline/nonroot/read-only Void renderer probe generated115037bytes, SHA256`7f29a0e1739cb8a6b9bb16ae02db30315bd3ba9436b6f7f0349e5989f867ac1a`; downloaded hash and onepage Thai PNG were checked. UATorders/voids/mockvoids remainzero. Existing shopper3/3 regression passed30.7s after local runtime refresh. These are implementation checks, not bank acceptance.
