# Finance operations — P7-03

Bank instructions in supplied specifications define protocol behavior, not authorization to execute a payment/refund. No live bank operation has been executed during development. UAT stays private and bank services remain unavailable until provisioned.

## Audited case workflow

Finance sessions may list/detail cases, claim a case, and append private notes. Every action requires CSRF, a stable UUID, the current case version and an explicit reason. Same-key retries return the committed result before checking a stale version; changed input conflicts. Lock order is operation → original bank instruction → order → case → stock. Bank/report resolutions increment case version, so an old admin screen cannot overwrite new evidence.

There is no generic set-Paid or dismiss-financial-discrepancy operation. Metadata-only dismissal is limited to invalid unbound notices or changed notification metadata. Fulfillment-only sessions may read orders but cannot read finance evidence/documents or mutate financial cases. Browser role filtering is presentation only; API role checks remain authoritative.

## Extra and unmatched credits

`POST /admin/bank-transactions/{id}/void` targets the complete amount of an existing credit belonging to this environment, merchant and biller. No client amount is accepted. Primary allocation is determined inside the original-instruction lock. Primary refunds hold the order and revoke goods after confirmed bank evidence. Extra/unallocated refunds leave primary allocation/payment and purchased goods intact; the associated order may remain on review until its finance cases are resolved. Unknown bank outcomes retain the send fence and require bounded explicit Enquiry Void; no replay of MerchantVoid.

Credits without an order require proven original ISO payment time to establish their void window. The bank's system TransDate, LOST debit time, and a new query timestamp never establish original credit time. Refund documents for unmatched credits are finance-only and have no invented purchase/order/receipt. Extra-credit documents do not claim that the purchase receipt covers the extra amount. One original receipt per order and one void document per bank credit allow multiple independent extra-credit refunds.

`GET /admin/bank-transactions/{id}/void-review` only inspects local evidence and eligibility; it never calls the bank. Final submission rechecks all conditions under locks. `resolve-voided-credit` closes a non-primary credit case only after trusted bank evidence marks that exact credit voided. It cannot close the primary payment's goods/return review. `resume-paid-order` is separate: require an intact non-void primary allocation, Paid status, every order case resolved and no pending/unknown void. It does not restore independently revoked entitlements or restock goods.

## Late payment acceptance

`accept-late-payment` is limited to LateOrClosedOrder/StockReview with an existing trusted domestic bank credit. Require exact merchant, biller, QR, both references and full order/attempt amount, no existing allocation/active void or other unresolved order case. It is an explicit finance decision after buyer consent, not a way to alter an amount or invent a bank instruction.

The transaction revalidates immutable lines and locks stock variants in ID order. Existing Reserved stock can be committed; Released stock can be consumed only from currently available units, excluding other orders' reservations. One immutable stock-commit effect, allocation, receipt job and per-line delivery job are written atomically. Insufficient stock or any identity conflict leaves the case open and money unallocated. Original callback ACK/evidence is retained.

## Stock adjustments

Admin stock adjustments require a stable UUID, nonzero delta and reason. Same key and identical variant/delta/reason/actor apply once. Different input conflicts. The stock ledger records the operation key and never reduces on-hand below reserved units. A lost response must be retried with its original key, never a generated replacement. This is separate from bank refund and return acceptance.

## Manual refunds, goods and real-environment gates

See `TTB_MANUAL_REFUND_DESIGN.md` for the implemented private evidence, two-officer attestation and explicit per-unit goods/stock workflow. Shopper activity exposes only approved primary refund totals and returned quantities; evidence hashes/files and external transfer references remain finance-only. Original receipts, allocation and bank evidence are retained. A manual attestation never changes BankTransaction.Voided or expects an invented D/V report row.

The catalogue now has bounded cursor paging and a dedicated admin selection read, so editing an older product is not limited to the latest100 rows. Local browser fixture replenishment is guarded by loopback+Local+Mock, uses the audited stock API and changes only the two named synthetic products.

Cross-border, partial and out-of-window refunds must not be represented as completed by the TTB API. Real company policy approval, identity/role provisioning, authoritative unmatched-credit identification and bank UAT remain external requirements. Private UAT policy flags remain closed by default. Reader contract2 is mandatory for every API/worker rollback; do not restore pre-manual binaries into current financial data.
