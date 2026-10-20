# TTB infrastructure

TTB has its own namespaces, databases, credentials and private document storage. Existing namespace `mangos`, KKU deployments, PVCs and ingress paths are not deployment targets.

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

Only `ttb-api-test.mangosgo.com` is an inbound callback host for UAT. Production callback host is `ttb-api.mangosgo.com`. Callback ingress allows the bank NAT addresses203.148.142.200 and203.148.143.101–105, separately from shopper routes. On20October2026 TTB accepted the URLs and merchant-defined authentication: UAT now uses Basic over HTTPS with credentials in `ttb-bank` and a DPAPI-encrypted PC handoff. Actual bank-source acceptance stays false until a real TTB callback. First outgoing Verify returned401; gateway acceptance must pass before checkout opens. See `Documentation/TTB_BANK_CONFIRMATION_20261020_TH.md`.

Before applying public ingress: validate its TLS certificate, host/path routing, real client IP handling, callback authentication, request body/time limits and bank source network. Do not trust a client-supplied X-Forwarded-For header to authorize callbacks. Outbound bank requests use the documented DNS host and TLS on9643; SFTP4022/4028 is separate. A successful TCP443 test alone does not validate any of these contracts.

The separate `uat/tls-certificate.yaml` and `provision-tls-certificate.ps1` prepare the exact UAT DNS01 certificate without creating a financial route. `tls-health-check.yaml` is an operator-only Exact `/health/live` preflight. On19Oct08:44UTC, explicit human authorization permitted the shared Service's Cluster→Local test with KKU checks/rollback. The confirmed Service is nowLocal/resourceVersion5847288, healthCheckNodePort31842. Correlated logs prove the real PC peer at ingress; allowed health200, unallowed node forging operator headers403, financial routes404. KKU public page/seven UI assets match their before status/hash and its APIhealth/deployment remain healthy; authenticated sessions/active WebSockets were not tested. The version-bound rollback passed server dry-run, with no actual rollback needed. See `Documentation/TTB_NT_NETWORK_CHANGE_REQUEST_TH.md` and `.artifacts/tls-qa/`. Controller deployment, KKU routes/deployment and firewall were not modified. Application proxy handoff, real bank NAT/authentication and bank acceptance remain pending; do not enable callbacks from this transport evidence alone.
