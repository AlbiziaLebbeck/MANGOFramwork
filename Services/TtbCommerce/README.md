# TTB commerce service

ASP.NET Core10 / PostgreSQL18. This source is independent of the deployed KKU service. Financial activation requires the TTB provisioning and UAT gates in `Documentation/TTB_IMPLEMENTATION_TASKS.md`.

## Development

Run from this directory with .NET SDK10.0.402. The helper keeps SDK/NuGet caches in the repository's ignored `.tools` directory.

```powershell
./tools/dotnet.ps1 restore TtbCommerce.slnx --locked-mode
./tools/dotnet.ps1 build TtbCommerce.slnx --no-restore
./tools/test.ps1
```

Database tests are explicitly skipped without `TTB_TEST_DATABASE`; a unit-only run does not prove database behavior. To run all tests:

For an isolated CI job, `Infrastructure/TTB/ci/check.ps1 -Component Backend` instead requires a loopback PostgreSQL `ttb_development` connection in `TTB_TEST_DATABASE`, rejects skipped/filtered/incomplete evidence before publishing, and records source/artifact hashes. `Documentation/TTB_CI_CHECKS.md` covers the separate Frontend/Unity commands, host prerequisites and pending coordinator/runner validation. This is separate from UAT deployment and bank acceptance.

1. Run `Infrastructure/TTB/dev/provision.ps1` from the repository root. This creates only namespace `ttb-commerce-dev`, a private DB/PVC, and generated development credentials. It never reads KKU credentials.
2. Run `./tools/test.ps1 -Postgres`. The harness opens a hidden SSH tunnel on a temporary loopback port and closes its own SSH process afterward. It does not reuse an idle tunnel.
3. Each integration test creates and removes its own random `ttb_test_<guid>` database. The configured admin database must be exactly `ttb_development`. To use a separately managed tunnel instead, pass `-ExternalTunnel`.

For a local server, supply `ConnectionStrings__Commerce` through a secret/local environment setting. Set `ASPNETCORE_ENVIRONMENT=Development`. `Commerce__LocalTestIdentityEnabled=true` enables explicitly labeled local identities only; it is rejected in UAT/Production. Set `Commerce__TrustedOrigins__0=http://localhost:5173` for the local frontend. Run the API once with `--migrate`, then normally. Do not use real bank credentials in Local mode.

## Identity and HTTP contract

- Session exchange: `POST /ttb-commerce/v1/session`, or the UAT prefix `/ttb-commerce-test/v1`. The server calls the fixed configured MANGOs `/api/v1/users/me` endpoint and reads `data.User.id`. Request-body user IDs/roles have no authority.
- `MangosCookieNames` is an exact cookie allowlist. An empty array permits bearer identity only. UAT uses `*` to forward existing MANGOs-domain SSO cookies to the official identity endpoint, excluding the TTB session/CSRF cookies. Narrow this allowlist when the MANGOs cookie contract is confirmed. The upstream handler refuses redirects and never stores cookies.
- A session is bound to the SSO credential fingerprint and revalidated against MANGOs (successful identity cache30seconds). Changed/missing/revoked credentials cannot reuse the old shopper identity. Tokens are random256bit; only SHA256 hashes are stored.
- `GET .../session` returns the authenticated shopper and CSRF token to an allowed frontend. All cookie mutations require an exact trusted Origin and `X-CSRF-Token`. Session exchange requires a trusted Origin before authentication.
- UAT/Production cookies use host-only `__Host-` names, Secure, HttpOnly, SameSite=Strict, Path=/. Local cookie names are separate. Do not put tokens in localStorage.
- Privileged subject keys are explicit `issuer:subject`, e.g. `mangosgo:<actual-id>`. No role is read from a browser or identity response body.
- `/health/live` reports process liveness; `/health/ready` reports DB readiness plus separate `paymentsReady`. Missing bank configuration does not prevent browsing. The payment service must reject real QR creation until provisioning is valid.

## Database and migrations

`ttb` is the only owned schema. API/worker replicas must use the same environment marker. The migration CLI checks an existing environment marker **before** applying changes; runtime refuses a DB marked for another environment. Never point UAT at Production/KKU connections.

Generate migrations with the pinned `dotnet-ef10.0.12` tool, not Unity tooling. Deploy migrations as a separate job/credential; runtime DB credentials should have no schema ownership. Do not run downgrade migrations against financial data; the automated down/up test uses empty disposable databases only.

Jobs use an atomic PostgreSQL `UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING`, unique effect keys, expiring lease tokens, conditional renewal/completion and durable retry. A lease alone does not guarantee exactly-once external effects: each fulfillment/document handler must commit its own idempotency record in the same transaction as its business effect.

## Bank status

User-confirmed previously tested Biller: `075555900002101`, recorded only in `appsettings.Uat.json`. Merchant ID, JWT issuer/key exchange, approved callback authentication, QR profile and SFTP credentials are still unavailable. No real-bank payment test has been claimed. Local mocks must never be enabled in Production.

## Payment implementation contract

- QR creation is `POST .../orders/{id}/qr`, protected by shopper ownership, CSRF and an order lock. Retries return the same persisted attempt/payload. The encoded Bangkok expiry minute is no later than the order's reservation expiry. `MANGO-MOCK:<id>` QR content is deliberately nonfinancial.
- Real order/QR creation requires the bank profile, issuer/signing key, beneficiary name and callback source-IP sign-off. Callback authentication currently implements an **explicitly approved Basic profile only**. The bank's actual mechanism is still unconfirmed; no default approval flag is enabled.
- Callback `/ttb/v1/credit-notification` first verifies mode, approved profile, actual/trusted-proxy-derived bank peer IP and Basic credentials. It never uses a shopper session or trusts a client-supplied forwarded header. Proxy CIDRs default empty; apply API NetworkPolicy and prove source preservation before configuring them.
- `CallbackProxyTests` exercises real one-hop forwarding with PostgreSQL: rightmost peer/forged left entries, untrusted and mapped peers, missing/invalid XFF, duplicate ACK/evidence identity and universal-trust rejection. `Documentation/TTB_PROXY_HANDOFF_ACCEPTANCE.md` separates that synthetic middleware proof from generated-ingress settings, pending live CNI/application handoff and actual bank NAT/auth acceptance. Tests do not configure UAT proxy trust.
- Each authenticated delivery has exact raw bytes in `Observations.RawPayload` (bytea), raw SHA256 and safe request metadata. `PayloadJson` is a small envelope, because malformed JSON, invalid Unicode and NUL must not be forced into PostgreSQL jsonb. Authorization/cookies are excluded.
- An instruction lock plus order/attempt/inventory locks serializes financial effects. Commit bank transaction, evidence, exact payment allocation, stock commit and durable document/fulfillment jobs before ACK. PDF/delivery never runs inside the callback HTTP request.
- Notification acknowledgement is separate from knowing payment through Verify. The first callback after Verify is ACK0000; a successfully acknowledged retry is0004. Metadata differences create a case; contradictory financial fields pause pending delivery and never replace the original allocation. A new instruction for an already paid QR is retained as extra credit with its own case.
- Amount/ref/Biller/QR mismatch returns the documented ACK and retains evidence. Late/closed-order and unapproved cross-border credits are accepted as evidence into Review with no automatic delivery. Hiding or cancelling a local QR never revokes it at the bank.
- Callback/Verify `TransDate` is retained as **system time**, not invented as original payment time. `PaymentOccurredAt` stays unknown until authoritative evidence resolves it; Void eligibility must respect that uncertainty.
- If persistence fails, no success ACK is returned. Bank retry on HTTP errors is not assumed; operators must recover through verified bank queries/reports. Lease recovery does not erase committed money.

## Manual bank recovery

`POST .../orders/{id}/verify` with `{ "method": "verify" }` or `{ "method": "enquiry" }` checks only that owner's stored QR. The client cannot supply Biller/QRId. Both methods share a durable maximum3 requests per attempt, an initial60second wait, cooldown, per-request lease token and merchant rate window (default30/minute; obtain the bank's capacity agreement before tuning). Each outgoing request has its own UUID and fresh short RS256 token. There is no automatic HTTP retry or recurring Verify/Enquiry job.

The request budget/lease commits before network I/O. Timeouts and cancellations consume a check. A cancelled request cannot clear another request's newer lease. Expired leases permit bounded recovery; they do not reset the historical count. Successful payment processing reloads locked rows after the network call so concurrent callbacks/finance changes are not overwritten by stale tracked entities.

Verify requires ResCode0000 and payment ResultCode0000. Its current no-data code is0002; legacy example0001 stays Unconfirmed pending bank clarification. ResCode9001 opens a finance review rather than inventing a refund. Range uses ResCode0000 with QTransaction members ResultCode000, and no-data0001. A malformed response, unexpected Biller/QRId, unsupported result, transport error or oversized response cannot mark an order paid. Range is limited to the previous24hours, including Static QR contract support; static credits do not become dynamic order allocations without an owned matching attempt.

Recovery-query authentication is separate from new-sales readiness: valid outgoing credentials can query an existing attempt while callback availability is being repaired. Incoming callback approval is still required before new real QR orders. No credentials are persisted with request/response evidence.

## Local bank simulator

Local+Mock is a separate pair; UAT/Production use Bank. `POST .../orders/{id}/mock-pay` requires that shopper's session/CSRF, a stable `requestKey` and one of `success`, `lost-callback`, `duplicate`, `amount-mismatch`, `late`, `extra-credit`. The server constructs all financial fields from its saved attempt; client amounts/paid states have no authority. This route is unavailable outside Local+Mock.

Simulated credits are durable and idempotent before callback delivery. The lost-callback scenario can be recovered through the same bounded Verify/Enquiry flow, using the local provider and distinct MockVerify/MockEnquiry evidence. It cannot call the real gateway or pretend a notification was already acknowledged. Normal success and retry use the exact allocation/stock/outbox rules used for bank evidence. `GET .../storefront/config` reports the server's environment/mode/readiness; the UI must display the mock state visibly. Simulation is not bank UAT.

For an explicitly local demo DB, run the API once with `--migrate --seed-demo`. The seeder creates three labeled demo products only in Local+Mock and leaves existing products/stock unchanged. It cannot seed UAT/Production. Demo model keys (`orbit-lamp`, `mango-sculpture`, `mango-star`) are the stable Unity preview binding keys; they are not private purchased assets.

## Fulfillment and private assets

Delivery jobs require a primary confirmed allocation and an order without finance hold. Physical delivery is one record per purchased line: Pending→Preparing→ReadyForPickup→Delivered, or Pending→Preparing→Shipped→Delivered with carrier/tracking required. Operator actions use role+CSRF+version+audit; they do not deduct stock again. An order completes only when every physical/digital line is delivered.

Digital jobs grant one entitlement per order-line unit, with unique grant operation IDs and immutable purchase metadata. Purchasing the same SKU again is a new set of units. Downloads require an active owned grant and a still-deliverable paid order. Full files live under private storage, never under the public preview path; upload records a SHA256 and immutable key, and fulfillment/download verifies the bytes. Failed files leave the payment Paid for operator repair/retry. A download request is recorded before streaming; it is not proof the buyer finished receiving the file.

Finance/admin revocation has a stable operation ID, reason and audit. It disables future download/game checks without fabricating a refund or erasing delivery history. Already received file copies cannot be recalled. Void/refund policy is handled separately.

`POST .../game/entitlements/validate` requires a separate server credential (`Storage__GameServerKey`), never a browser Paid flag. The game server must obtain ShopperId through its trusted identity handshake; it must not forward a client-claimed identity as authority. The Unity server integration/handshake is verified in P6. This server credential must never enter a WebGL build or public configuration.

## Receipts and history

`receipt.ensure` creates one immutable company receipt snapshot/number per allocated order, then queues `document.render`. An independent `ReceiptWorker` renders PDF offline with embedded OFL Noto Sans Thai; it has no bank signing key or bank network access. Production startup requires a complete approved `Receipts` issuer profile. Local mock and UAT documents display test wording; these are seller receipts, not bank slips or tax invoices. Confirmation time is explicitly the system's time, not an invented original bank payment timestamp.

Rendering retries keep the original snapshot/number. The renderer verifies the canonical typed JSON snapshot hash after PostgreSQL jsonb normalization, writes a content-addressed immutable file atomically before setting Ready, and never overwrites a ready document. A crash can leave an unreferenced file for later reconciliation, not a Ready database record pointing at unwritten bytes. Downloads verify size/SHA256, use no-store, require ownership or finance role, and record the request before streaming. A later void retains the original receipt as evidence; its separate void document/status is shown alongside it.

`GET .../orders` provides paginated owned history; `GET .../orders/{id}/activity` assembles immutable order details, payment confirmation, delivery status and Pending/Ready documents. `GET .../orders/{id}/documents` and `GET .../documents/{id}/download` are shopper-owned. The explicit `/admin/documents/{id}/download` route requires finance/admin. Reprinting downloads the same stored bytes; it does not issue another number.

The operator-only `tools/ReceiptProbe` project has no database or bank connection. Publish it against the same dependency versions and include its `ReceiptProbe*` host files beside the self-contained API. `/app/ReceiptProbe /tmp/proof.pdf` inside the receipt worker verifies Linux Chromium/font rendering using visibly synthetic content without creating an order or financial document. Linux publication from Windows must make `.playwright/node/linux-x64/node` executable; the private deployment script does this. CI receipt tests require the matching official Playwright Chromium runtime installed first (`playwright.ps1 install chromium`).

## Bank reports

Report transport defaults disabled, with unknown parser/date/totals profiles. Worker-only SFTP uses a pinned SHA256 host key and mandatory PGP AES256 integrity checks; optional bank signatures are pinned and verified. Complete files pass beneficiary,500-position layout, sequence and totals checks before rows are staged. Encrypted originals, hashes, revisions, applied profiles and audit evidence are retained privately. Replay is idempotent; content changes require a finance reason before reprocessing. C/D/V and reverse ISO comparison preserve money history and avoid truncating35/50-column instruction IDs. See [TTB_REPORT_OPERATIONS.md](../../../Documentation/TTB_REPORT_OPERATIONS.md) for configuration, schedules, missing-file states, exception/recovery boundaries and the bank acceptance gate. Production checkout requires explicit report acceptance metadata; the API does not receive the worker's SFTP/PGP secrets.

New archive profiles also capture exact PGP recipient/signing pins. `Reports__ArchiveRecipientFingerprints__0` / `Reports__ArchiveSigningFingerprints__0` may explicitly retain old pins for matching existing archives; they never broaden trust for newly received files. Keep the provisioned old/new keyrings in the worker-only key mount and follow the report runbook before rotating. Legacy snapshots use current keys only; a stronger current signing policy cannot be bypassed by an unsigned historical profile.

## Bank signing key lifecycle

The JWT signer reads the existing mounted RSA2048 private key for every explicit gateway request and never falls back to an old key. Private metrics expose expected/usable/scheduled status and an optional agreed `Operations__BankSigningKeyRotationDueUtc`; no path, issuer, key bytes or invented expiry date is exported. These signals survive SQL failure, and monitoring detects missing telemetry after an incompatible rollback. No new observer receives a private key. See `Documentation/TTB_BANK_KEY_ROTATION.md` in the workspace for synthetic rotation evidence and the still-pending bank registration/overlap/deadline/custody requirements.
