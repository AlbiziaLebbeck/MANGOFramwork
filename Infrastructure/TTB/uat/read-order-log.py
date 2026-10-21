"""Read-only, selected-field UAT order evidence. No passwords or payer details."""
import json
import re
import subprocess
import sys

number = sys.argv[1] if len(sys.argv) == 2 else ''
if not re.fullmatch(r'T[0-9]{14}', number):
    raise SystemExit('Supply one UAT order number: T +14 digits')
sql = '''BEGIN READ ONLY;
SELECT json_build_object('environment', (SELECT "Environment" FROM ttb."Deployments" WHERE "Key"='singleton'),
'order', (SELECT row_to_json(o) FROM (SELECT "Id", "Number", "PaymentState", "State", "TotalMinor", "CreatedAt"
 FROM ttb."Orders" WHERE "Number"='NUMBER') o),
'payments', (SELECT coalesce(json_agg(p), '[]') FROM (SELECT b."InstructionId", b."QrId", b."AmountMinor", b."Voided", b."CreatedAt"
 FROM ttb."BankTransactions" b JOIN ttb."Allocations" a ON a."BankTransactionId"=b."Id"
 JOIN ttb."Orders" o ON o."Id"=a."OrderId" WHERE o."Number"='NUMBER') p),
'callbacks', (SELECT coalesce(json_agg(c), '[]') FROM (SELECT r."InstructionId", r."AckCode", r."DeliveryCount"
 FROM ttb."CallbackReceipts" r JOIN ttb."BankTransactions" b ON b."InstructionId"=r."InstructionId"
 JOIN ttb."Allocations" a ON a."BankTransactionId"=b."Id" JOIN ttb."Orders" o ON o."Id"=a."OrderId"
 WHERE o."Number"='NUMBER') c),
'fulfillment', (SELECT coalesce(json_agg(f), '[]') FROM (SELECT f."State", f."Kind" FROM ttb."Fulfillments" f
 JOIN ttb."Orders" o ON o."Id"=f."OrderId" WHERE o."Number"='NUMBER') f),
'documents', (SELECT coalesce(json_agg(d), '[]') FROM (SELECT d."Number", d."Kind", d."State" FROM ttb."Documents" d
 JOIN ttb."Orders" o ON o."Id"=d."OrderId" WHERE o."Number"='NUMBER') d),
'evidence', (SELECT coalesce(json_agg(e), '[]') FROM (SELECT p."Source", p."Outcome", p."CorrelationId" AS "RequestUid", p."CreatedAt", p."MetadataJson"->>'peerIp' AS "PeerIp"
 FROM ttb."Observations" p JOIN ttb."Attempts" a ON a."Id"=p."AttemptId" JOIN ttb."Orders" o ON o."Id"=a."OrderId"
 WHERE o."Number"='NUMBER' ORDER BY p."CreatedAt") e),
'entitlements', (SELECT coalesce(json_agg(e), '[]') FROM (SELECT e."State" FROM ttb."Entitlements" e
 JOIN ttb."Orders" o ON o."Id"=e."OrderId" WHERE o."Number"='NUMBER') e));
ROLLBACK;'''.replace('NUMBER', number)
result = subprocess.run(['kubectl', '-n', 'ttb-uat', 'exec', '-i', 'deployment/ttb-postgres', '--',
                         'psql', '-X', '-q', '-A', '-t', '-v', 'ON_ERROR_STOP=1', '-U', 'ttb_owner', '-d', 'ttb_uat'],
                        input=sql, text=True, capture_output=True, timeout=25)
if result.returncode:
    raise SystemExit('Cannot read order evidence; inspect UAT schema/DB availability. No data was changed.')
data = json.loads(result.stdout.strip())
if data['environment'] != 'Uat':
    raise SystemExit('Unexpected database environment')
print(json.dumps(data, ensure_ascii=False, indent=2))
