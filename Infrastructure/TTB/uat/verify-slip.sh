#!/usr/bin/env bash
# One UAT bank read. No retry, payment, Void, order, receipt, or key export.
set -euo pipefail
command -v kubectl >/dev/null
command -v python3 >/dev/null
kubectl -n ttb-uat exec deployment/ttb-commerce-api -- /app/TtbCommerce.Api --uat-bank-probe |
python3 -c '
import json, sys
rows = [line for line in sys.stdin if line.startswith("{\"status\":\"uat-bank-diagnostic\"")]
if len(rows) != 1:
    sys.exit("Expected one UAT diagnostic result; no automatic retry was made.")
result = json.loads(rows[0])
if result.get("environment") != "Uat" or result.get("financialRecordsCreated") is not False or result.get("bankRequestCount") != 1:
    sys.exit("Unexpected diagnostic contract.")
output = {key: result.get(key) for key in ("environment", "merchantId", "billerNo", "issuer", "issuedAt", "checkoutReady", "missingCheckoutRequirements", "bankAcceptanceComplete", "missingBankAcceptanceRequirements", "probe", "bankRequestCount", "financialRecordsCreated")}
output["qrId"] = result["qr"]["QrId"]
output["endpoint"] = "https://uat-dgw.tau2904.com:9643/qrapiservices/verify/verifyqr/PYA"
print(json.dumps(output, indent=2))
sys.exit(0 if result["probe"].get("authenticatedBankReply") else 2)
'
