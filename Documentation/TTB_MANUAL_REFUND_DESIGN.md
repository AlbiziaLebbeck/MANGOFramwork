# Manual refund and goods disposition — P7-03 implementation contract

Status: implemented and verified with synthetic Local/Mock data and isolated PostgreSQL tests; private UAT release `20261019-p7-finance` installed with real bank/company policy gates closed. This is implementation acceptance, not bank certification or evidence of a real refund. Required by system plan§13.2/T32. Partial returns, cross-border credits and credits outside Void eligibility use a manual case with private evidence and an approver. This service never sends a payment, invents a payout API, changes a bank credit to Voided, or claims a human attachment is a bank API confirmation.

## Evidence-backed manual case

1. Finance selects one existing credit belonging to this environment/merchant/biller, an exact integer minor amount and a reason. Amount is bounded by that credit less all active/attested manual cases. A pending/unknown/confirmed Void blocks a manual request; any active/attested manual request blocks a new Void. Both paths use the same original-instruction lock, then the order lock. UUID/request hash protects every mutation.
2. Create a ManualRefund case and keep its associated order on review. No bank call, allocation, stock change or claim of money returned occurs at this step. Unmatched report instructions without an authoritative complete bank identity remain unresolved; never map a35-character prefix to a50-character API ID.
3. Finance uploads private evidence into its own finance area, not the catalogue's downloadable assets. Pin files to the case, bound size/type, hash immutable bytes and require finance on upload/download. Evidence must never appear in shopper history, public previews or catalogue asset choices.
4. An officer submits a declared external-transfer reference/time and selected case evidence. That declaration is not payment execution. It cannot be silently cancelled to permit another refund; rejected evidence keeps the credit reserved for investigation.
5. A different officer approves the external-refund attestation only under the explicitly approved company policy. The creator/submitter cannot approve their own record, including when they have the admin role. Default policy is closed. Approval preserves bank/original allocation/receipt evidence and records who approved, when, why and which file hashes. It is labelled manual attestation, never TTB Void.
6. Full approved external refund of the primary credit sets a distinct Refunded summary, pauses physical delivery and revokes primary digital rights. Partial primary refunds retain a goods-review case until explicit line decisions; non-primary refunds preserve the legitimate primary purchase and rights. Never restock automatically. Manual records do not expect a TTB D/V row in the bill-payment report.

## Explicit goods decisions

Confirmed bank Void and approved external-refund cases have separate goods review. The operator selects affected digital entitlement units, cancels undelivered physical lines when appropriate, and records physically returned units separately from money. Restocking requires an explicit return reason and an idempotent per-line effect, bounded by originally committed quantities less prior returns. Shipped/delivered history and original stock movements remain immutable. Partial-return decisions must not revoke all units indiscriminately. Company return/revocation policy is required before enabling real decisions.

Extra-credit cases can be closed only when their exact credit is fully returned by bank Void or approved manual evidence. Resuming the primary order is a separate guarded action after every blocking case is resolved; no free Paid toggle. Worker jobs, game tickets/leases and private downloads continue to check the resulting order/entitlement state.

## Verified acceptance and external gates

- Two concurrent intents/approvals cannot over-refund; same-key retry is one record/effect; changed input conflicts.
- Partial/cross/out-of-window case creation does not call MerchantVoid or any invented endpoint.
- Self-approval, forged roles/CSRF, foreign credit/file IDs and evidence from another case are rejected.
- Evidence is private, immutable/hash-checked and absent from catalogue/shopper data.
- A report/late callback cannot resurrect a fully refunded order. Pending/manual and Void races cannot produce two refunds.
- Full primary, partial primary, extra-credit and no-order cases have distinct goods effects; stock returns and digital unit decisions are bounded/idempotent/audited.
- Shopper history identifies approved external refunds without exposing private financial attachments. Bank report reconciliation keeps original credit evidence and does not fabricate D/V entries.
- Local simulations only until company/bank inputs permit UAT. Final deployment/requirement matrix remains in the task ledger.

Evidence: full backend205/205 (`commerce_net10.0_20261019031910.trx`), frontend security17/17, full browser8/8 (1.9m), compiled Gallery2/2 and FishNet world1/1 after copying the current HTML overlay. Backend tests cover same-key concurrency, manual/Void races, two-officer approval, corrupt/foreign proof, offset timestamps, full/partial/extra cases, pre-worker revocation and explicit bounded stock returns. Browser tests use actual Local APIs and two officer sessions; the proof text is synthetic. Private UAT has no public ingress, bank readinessfalse and no production funds.

## Reader compatibility and recovery

`Refunded` is a new persisted payment state. All API, commerce worker and PDF worker readers must support reader contract2 before enabling manual/goods policy flags. Published artifacts declare the contract in `release-compatibility.json`; the owned UAT deployment tool maintains a monotonic minimum-reader file and refuses missing/older declarations. Do not use raw `kubectl rollout undo` to a pre-contract2 binary. Financial/evidence/return migrations refuse destructive Down while records exist. Use a compatible forward fix and preserve the original database/files; restoring an older backup requires the operations freeze/reconciliation procedure.

An unmatched report whose shortened instruction cannot be joined to a complete authoritative bank credit stays an investigation case. Claim/note it and obtain authoritative identification through the bank process; do not fabricate a BankTransaction or infer a50-character identity from a35-character prefix to enable a refund.
