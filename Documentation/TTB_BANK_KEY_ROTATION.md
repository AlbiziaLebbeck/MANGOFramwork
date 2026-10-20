# TTB JWT signing key lifecycle

This procedure covers the company's RSA2048 key used to sign outbound TTB JWTs. It does not rotate TLS, OpenPGP, SSH/SFTP, MANGOs sessions, game credentials or the backup CMS recipient. No real bank signing key/issuer or bank-approved rollover window is currently available; no bank key has been generated, registered or replaced by this work.

## Availability and scheduling

`BankJwtSigner` reads the mounted PEM on every explicitly requested gateway call. It keeps no previous-key cache or fallback. Missing, malformed, public-only or incorrectly sized material prevents signing and returns a sanitized unavailable error. A failure does not trigger an HTTP retry. The gateway's fixed host, JWT issuer, RS256 algorithm, token lifetime and request identities remain unchanged.

Private metrics expose only these fixed aggregate names:

| Metric | Meaning |
|---|---|
| `ttb_bank_signing_key_expected` | Bank UAT/Production mode with a nonempty issuer and key path; Local/Mock and unprovisioned profiles remain0 |
| `ttb_bank_signing_key_probe_success` | The expected file currently imports as an RSA2048 private key; not proof that TTB registered its public key |
| `ttb_bank_signing_key_rotation_scheduled` | An explicit positive UTC deadline was supplied for an expected key |
| `ttb_bank_signing_key_rotation_due_timestamp_seconds` | That agreed operational deadline, omitted when unknown |

`Operations__BankSigningKeyRotationDueUtc` is optional ISO8601 metadata from the bank/company's approved key policy. It is **not** inferred from token `exp`, file modification time, the TLS certificate or backup recipient. A bare RSA key does not supply the X.509 certificate validity window. The private-key syntax and certificate lifetimes are separate concepts. See [PKCS#8 private-key syntax](https://www.rfc-editor.org/rfc/rfc5208#section-5) and [.NET RSA PEM import](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.importfrompem?view=net-10.0).

An unknown deadline is visible, rather than represented as an invented date. No operational deadline is configured in UAT yet. These metrics remain available when PostgreSQL is down and never export private/public key bytes, PEM paths, issuer values, fingerprints or per-key labels. The normal API/worker already owns the signing-key mount; no key is mounted into Prometheus or a new observer. Exported private RSA parameter copies used to validate usability are zeroed immediately.

The collector rules detect expected-key failure after2minutes, missing schedule after30minutes, a known deadline less than30days away after5minutes, and overdue deadlines after5minutes. Near-deadline warnings and overdue critical alerts are distinct. Unprovisioned profiles do not raise those lifecycle alerts. A separate2-minute telemetry-completeness rule detects an old/partial emitter or a claimed schedule without its date; an intentionally unknown deadline with explicit scheduled0 is valid. Rules never replace keys, resend a bank command, revoke credentials or infer payment status. Deadline metadata does not itself change payment readiness. No external notification receiver has been authorized/configured.

## Coordinated bank switch

1. Confirm the exact environment, issuer, RSA2048/RS256 contract, bank registration procedure, public-key identity, allowed old/new overlap and clock skew through the bank's approved channel. Establish an explicit rotation deadline and custody/recovery owner. If TTB supplies a certificate-based registration profile, record its real validity separately; do not infer one from a PEM file.
2. Prepare the new company private key only through the agreed secure process. Transfer/register **public** material with the bank; keep private material in the environment-specific signing Secret and approved recovery custody. Never paste keys or Secret bodies into terminal output, Git, chat or a browser build.
3. Rehearse atomic replacement with synthetic keys first. The same running signer/gateway must use the new key on its next explicit request; a broken/missing/public-only/wrong-sized replacement must fail closed and must not reuse the prior key. Independently verify old and new JWT signatures and original claim lifetimes. A mathematical old-signature check does not prove bank acceptance after deregistration.
4. Agree the bank's live cutover/overlap before replacing real material. Account for already-issued tokens up to the configured `Ttb.TokenSeconds`, approved clock skew and in-flight HTTP requests. If the bank cannot provide overlap, obtain a specific maintenance/reconciliation procedure before live acceptance. `RecoveryFreeze` deliberately returns503 for inbound callbacks and is not a callback-preserving key maintenance mode.
5. Atomically project the approved new signing file, update the approved deadline in the owned configuration and restart the affected owned API/commerce processes for environment-variable changes. The signer rereads a projected file, but environment variables are process snapshots. Preserve callback evidence, request budgets, durable Void send fences and Unknown outcomes. Never resend MerchantVoid to compensate for an authentication failure.
6. Run the bank-authorized UAT checks and inspect safe key/gateway/circuit metrics. Verify the bank accepted the new public key, caller/source authentication remains intact and no report/callback is missing. Record sanitized UTC/release/public-key identity evidence. Retire/revoke old bank registration only after the agreed overlap and custody rules permit it; do not let the application silently fall back.

## Acceptance still required

Synthetic RSA/gateway rotation and private-metrics tests provide local implementation evidence. Production requires actual issuer/public-key registration, bank-approved overlap/deadline/expiry policy, custody/compromise response and an authorized notification route. Tests with locally generated keys never grant permission to call TTB, rotate a bank key or claim a real payment succeeded. Historical PGP recovery follows `TTB_REPORT_OPERATIONS.md`; backup key recovery follows `TTB_UAT_RECOVERY_EVIDENCE.md`.

## Verified implementation,19October2026

Full backend243/243 passed without skips (`commerce_net10.0_20261019151208.trx`,4m15s). Six lifecycle cases cover real old/new RSA signatures through the same gateway, original issuer/epoch/lifetime claims, no HTTP call on a broken replacement, missing/public-only/wrong-size failures, unknown versus explicit deadline, and private key metrics during an actual disposable PostgreSQL loss. All46local acceptance cases trace to that full TRX, with actual bank/Production acceptance stillfalse.

Promtool validated32rules and18simulated scenarios, including incomplete telemetry after rollback, deliberately unknown dates, unprovisioned suppression, deadline warning and overdue critical separation. Private UAT release`20261019-p7-jwt-lifecycle` uses reader3 and capability`bank-signing-key-lifecycle-v1`, archive SHA256`141377945fb1791414025ac0680ffe98d9b9a5404a8f27b809d5c291aa8c3698`. Three owned roles are ready with payments/reports disabled; eight financial aggregates remain zero. Monitoring has threeUp targets/32healthy rules and no external receiver; expected/usable/scheduled are0 and no rotation deadline is emitted. The Linux synthetic receipt renderer passed. Shared ingress resourceVersion999/Cluster, KKUready1/HTTP200 and local preview reachability are unchanged. See `.artifacts/jwt-key-qa/` and the task ledger. No bank key or scheduling metadata was invented or changed.
