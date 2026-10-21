# TTB infrastructure

TTB has its own namespaces, databases, credentials and private document storage. Existing KKU deployments, PVCs and ingress paths are not deployment targets. The user-authorized Portal Unity deployment `mangos/metaverse-server-ttb-uat` has the narrow game identity integration described below.

## Development database (already provisioned)

`dev/provision.ps1` creates namespace `ttb-commerce-dev`, a 2Gi PVC and PostgreSQL18 ClusterIP. Generated credentials are kept only in its Kubernetes Secret and ignored `.tools/local/postgres-development.local.json` on the workstation. Re-running does not rotate an initialized DB password.

Start a remote loopback forward in a terminal:

```powershell
ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 -L 127.0.0.1:15439:127.0.0.1:15439 ntserver02 'kubectl -n ttb-commerce-dev port-forward service/postgres 15439:5432 --address=127.0.0.1'
```

If the remote forward survives an SSH disconnect and already listens on15439, reconnect with `ssh -N` and the same `-L`/keepalive options. Do not start a second remote forward or kill an unidentified listener. No database port is exposed publicly. Test databases have random names; cleanup never targets UAT/Production.

## UAT deployment contract

Namespace `ttb-uat`; Production uses `ttb-prod` with independently generated secrets/PVCs. Templates are committed for review before applying. Public DNS does not itself establish a working TLS/callback/API service.

Required secret names/keys (values never in source):

| Secret | Keys/use |
|---|---|
| ttb-database-owner | connection / migrations; regular UAT backups use the separate readonly role |
| ttb-commerce-runtime | ConnectionStrings__Commerce / least-privilege app role |
| ttb-bank | Ttb__MerchantId, Ttb__Issuer, Ttb__CallbackUsername, Ttb__CallbackPassword and future SFTP fields from approved provisioning |
| ttb-signing-key | private.pem / bank-approved RSA key, mounted read-only |
| ttb-reports | worker-only Reports__* transport/profile/passphrase and explicit archive-only retired fingerprint arrays |
| ttb-report-keys | complete provisioned PGP secret/public keyrings, mounted only in the commerce worker |
| ttb-backup-credentials | password / readonly ttb_backup for ttb_uat only; no owner privilege |
| ttb-operations | scoped aggregate collector token; no financial/browser authorization |
| ttb-api-test-tls | cert-manager TLS certificate/key for UAT ingress; observers project only tls.crt |

Runtime must not receive the database-owner secret. Public/private evidence and logs must not include credentials, authorization headers or private keys. Live/ready probes are separate; a service can serve catalog and authentication while `paymentsReady=false`.

JWT key lifecycle metrics use the API/worker's existing signing mount; Prometheus receives only fixed aggregates. Optional `Operations__BankSigningKeyRotationDueUtc` belongs to an approved bank/company schedule in owned configuration, not an inferred PEM/token/TLS expiry. No date is configured in UAT. See `Documentation/TTB_BANK_KEY_ROTATION.md` for atomic replacement, missing-key/telemetry alerts, no previous-key fallback and actual bank acceptance gates.

Only `ttb-api-test.mangosgo.com` is an inbound callback host for UAT. Production callback host is `ttb-api.mangosgo.com`. Callback ingress allows the bank NAT addresses203.148.142.200 and203.148.143.101–105, separately from shopper routes. On21October11:02 TTB confirmed its common notification sender does not use username/password; the user authorized the explicit `BankSourceIp` profile. UAT therefore uses HTTPS plus the existing ingress and exact application source allowlists and a pinned trusted proxy, without Authorization. Basic remains available only when explicitly selected; unknown/unapproved profiles or empty/invalid source lists stay closed. Basic credentials prepared earlier are obsolete for this UAT integration. Actual bank-source acceptance stays false until a real TTB callback. After TTB registered the existing public key, Verify on21October returnedHTTP200/0002/NotFound with authenticatedBankReply=true. `uat/bank-acceptance.yaml` records this evidence and explicitly enables UAT test checkout while first-callback acceptance remains pending; Production stays strict and is not deployed. Its non-secret ConfigMap reference must follow the original bank Secret in envFrom so accepted values override closed defaults. Deploy a release with `bank-source-ip-callback-v1` first, then run `uat/activate-bank-source-callback.py` on the server (dry-run, then --apply). Activation checks actual proxy/ingress topology and binary capability and preserves before/after state. See `Documentation/TTB_UAT_GATEWAY_ACCEPTANCE_20261021_TH.md` and `Documentation/Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md`.

The same UAT release connects Portal's `mangos/metaverse-server-ttb-uat` through `uat/configure-portal-game.py`: dry-run first, then `--apply` only for that slug. It copies only the existing UAT game identity into an immutable game-only Secret and allows that pod label/namespace to reach API8080 via a separate NetworkPolicy. It does not copy bank/SQL credentials. Recheck this binding after a Portal redeploy that replaces deployment env.

Before applying public ingress: validate its TLS certificate, host/path routing, real client IP handling, callback authentication, request body/time limits and bank source network. Do not trust a client-supplied X-Forwarded-For header to authorize callbacks. Outbound bank requests use the documented DNS host and TLS on9643; SFTP4022/4028 is separate. A successful TCP443 test alone does not validate any of these contracts.

On20Oct2026 evening, the user authorized shopper access from changing/public client IPs for the deployed Portal site. `uat/shopper-ingress.yaml` creates `ttb-shopper-public-uat` with explicit shopper paths, HTTPS, 256KiB body limit, 30s upstream timeouts and per-IP request/connection limits. It leaves the existing operator-only preview/API fallback, admin/game-service/local-session paths, callback allowlist, API NetworkPolicy and shared controller untouched. Do not replace it with a public prefix for the entire commerce API. Run `python uat/verify-public-shopper.py` against the live host from a non-operator network to verify public catalog/CORS, anonymous ownership gates and restricted operator/bank paths. Roll back by deleting only this new Ingress. This connectivity change does not enable bank payments (`paymentsReady=false` at validation).

The separate `uat/tls-certificate.yaml` and `provision-tls-certificate.ps1` prepare the exact UAT DNS01 certificate without creating a financial route. `tls-health-check.yaml` is an operator-only Exact `/health/live` preflight. On19Oct08:44UTC, explicit human authorization permitted the shared Service's Cluster→Local test with KKU checks/rollback. The confirmed Service is nowLocal/resourceVersion5847288, healthCheckNodePort31842. Correlated logs prove the real PC peer at ingress; allowed health200, unallowed node forging operator headers403, financial routes404. KKU public page/seven UI assets match their before status/hash and its APIhealth/deployment remain healthy; authenticated sessions/active WebSockets were not tested. The version-bound rollback passed server dry-run, with no actual rollback needed. See `Documentation/TTB_NT_NETWORK_CHANGE_REQUEST_TH.md` and `.artifacts/tls-qa/`. Controller deployment, KKU routes/deployment and firewall were not modified. Application proxy handoff, real bank NAT/authentication and bank acceptance remain pending; do not enable callbacks from this transport evidence alone.

## Private UAT bank test console (Ref1=TEST)

After deploying `isolated-bank-test-console-v1` (reader contract4), run `uat/activate-bank-tests.py --apply` on ntserver02. It checks the real UAT callback evidence and compatible API/worker releases before enabling the console, updates the confirmed beneficiary name, and advances the backup reader manifest. It preserves bank source/proxy restrictions and Secrets.

Open `uat/open-bank-test-console.ps1` on the workstation, then visit `http://127.0.0.1:18765/internal/bank-test-console/`. The tunnel stays in the foreground until Ctrl+C. There is no public ingress for this console. It uses existing SSH operator authority, original-loopback socket and Host guards, and same-origin mutation headers.

Workstation compatibility: the console and log helpers support Windows PowerShell5.1 and PowerShell7. Preserve their UTF-8 BOM so5.1 reads Thai messages correctly; `uat/.editorconfig` declares this encoding. The log helper explicitly enumerates JSON events and preserves JSON arrays for zero, one and multiple results across both versions. Reuse the original commands from the project root.

Notification TEST records, Verify and explicitly confirmed Void/Enquiry Void are isolated in four BankTest tables. No shopper order, inventory allocation, fulfillment or receipt is created. Merchant Void is fenced before send and cannot be retried; use the original Enquiry Void identity for an unknown result. Limits default to100THB, ten Verify calls/case, three Void enquiries/case, and the common bank read budget.

Use `uat/get-uat-log.ps1 -View Tests` for durable events; add `-AsJson`, `-CaseId` or `-Before` to export/filter/page. `-Follow` reads current API stdout events. `-View Order -OrderNumber T20261021000002` reads selected order/payment/fulfillment/document evidence without payer details or secrets. The beginner Thai guide is `Documentation/TTB_PAYMENT_IMPLEMENTATION_AND_TEST_CONSOLE_TH.md`. Delivered reports are archived in the user's `TTBPayment/01_Reports` folder; see `Documentation/TTB_FILE_ORGANIZATION_TH.md`. `output/pdf/` remains a temporary generation directory.
