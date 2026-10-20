# TTB requirement/evidence matrix

Generated from the plan, explicit selectors and completed TRX/Playwright JSON. Local verification does not certify TTB, company/device acceptance or Production.

Generated UTC: `2026-10-19T20:38:17.809746+00:00`. Local cases verified: **46/46**; requirements traced: **26/26**. Final acceptance complete: **5/46**.

## Requirements

| ID | Requirement | Cases | Local evidence | Final acceptance |
|---|---|---|---|---|
| R01 | คลิกโมเดลเปิดสินค้า | T38, T39 | PASS | PENDING |
| R02 | หมุน ซูม reset | T38 | PASS | PENDING |
| R03 | Catalog/variants | T39 | PASS | PENDING |
| R04 | MANGOs login | T25, T40 | PASS | PENDING |
| R05 | Cart | T16 | PASS | COMPLETE |
| R06 | Quote | T15, T16 | PASS | COMPLETE |
| R07 | Mixed order | T15, T18 | PASS | PENDING |
| R08 | Stock reservation | T17 | PASS | PENDING |
| R09 | Dynamic QR | T01, T02, T03, T42 | PASS | PENDING |
| R10 | Callback | T04, T05, T06, T07, T08, T09, T14, T27, T43 | PASS | PENDING |
| R11 | Verify/Enquiry | T10, T11, T12, T13, T14 | PASS | PENDING |
| R12 | Late/extra/mismatch payment | T07, T08, T13, T19, T20 | PASS | PENDING |
| R13 | Digital entitlement | T05, T20, T21, T22 | PASS | PENDING |
| R14 | Physical fulfillment | T18 | PASS | PENDING |
| R15 | Receipt PDF | T23, T24 | PASS | PENDING |
| R16 | History | T25, T41 | PASS | PENDING |
| R17 | Admin | T26 | PASS | COMPLETE |
| R18 | SFTP/PGP | T33, T34, T35 | PASS | PENDING |
| R19 | Reconciliation | T06, T35, T36, T37 | PASS | PENDING |
| R20 | Void | T12, T29, T30, T31, T32 | PASS | PENDING |
| R21 | Cross-border | T09, T32 | PASS | PENDING |
| R22 | Environment isolation | T02, T28 | PASS | PENDING |
| R23 | Cache/update recovery | T41 | PASS | PENDING |
| R24 | Backup/operations | T21, T43, T44, T45, T46 | PASS | PENDING |
| R25 | Security | T25, T26, T27, T45 | PASS | PENDING |
| R26 | Accessibility/device | T24, T38, T40, T42 | PASS | PENDING |

## Test cases

| ID | Scenario | Local result | Backend cases | Browser suites | Outstanding acceptance |
|---|---|---|---:|---|---|
| T01 | QR example TLV/CRC และค่าที่มีเลขศูนย์นำหน้า | PASS | 2 | — | BANK_QR |
| T02 | QRId generator พร้อมกันหลาย replica/เลข running เต็ม | PASS | 2 | — | BANK_QR |
| T03 | QR expiry ระดับนาที/เที่ยงคืน/15–16/สิ้นเดือน | PASS | 5 | — | BANK_QR |
| T04 | callback success ปกติ | PASS | 2 | — | BANK_PROFILE |
| T05 | callback ซ้ำทั้งก่อน/หลัง worker และ ACK สูญหาย | PASS | 2 | — | BANK_PROFILE |
| T06 | callback กับ Verify/range/report มาพร้อมกัน | PASS | 2 | — | BANK_PROFILE, BANK_REPORTS |
| T07 | same InstructionId แต่ amount/refs เปลี่ยน | PASS | 1 | — | BANK_PROFILE |
| T08 | wrong Biller/QRId/amount/refs | PASS | 5 | — | BANK_PROFILE |
| T09 | payer fields หาย/0000000000/Cross-border | PASS | 1 | — | CROSS_BORDER, BANK_PROFILE |
| T10 | GET status จำนวนมาก | PASS | 1 | browser | BANK_PROFILE |
| T11 | manual verify หลายแท็บ/หลายactor/เกินbudget | PASS | 3 | — | BANK_PROFILE |
| T12 | Verify 0000/0002/0003/9001/legacy0001/9999 | PASS | 6 | — | BANK_PROFILE |
| T13 | range หลายรายการ/ซ้ำ/หน้าต่างผิด/ข้อมูลขัดกัน | PASS | 2 | — | BANK_PROFILE |
| T14 | ฝั่งธนาคาร timeout/HTTP200 businesserror/malformed | PASS | 7 | — | BANK_PROFILE |
| T15 | idempotency key ซ้ำหรือซ้ำแต่payloadต่าง | PASS | 2 | browser | — |
| T16 | ราคาเปลี่ยน/quoteหมดอายุ/cartแก้หลายแท็บ | PASS | 3 | browser | — |
| T17 | คำสั่งซื้อแข่งซื้อ stock ชิ้นสุดท้าย | PASS | 1 | — | COMPANY |
| T18 | physical-only/digital-only/mixed และ pickup/shipping | PASS | 4 | browser | COMPANY |
| T19 | เงินเข้าหลัง expiry/cancel/stock release | PASS | 2 | — | BANK_PROFILE |
| T20 | สองเครดิตใหม่ต่อorderเดียว/สองorderยอดเท่ากัน | PASS | 2 | — | BANK_PROFILE |
| T21 | workerล้มก่อน/หลัง external grant | PASS | 4 | — | RESTORE |
| T22 | digital inventory quantityมากกว่า1/ซื้อซ้ำจริง | PASS | 1 | world | — |
| T23 | Paidแล้วPDFล้ม/สร้างซ้ำ/ดาวน์โหลดซ้ำ | PASS | 8 | browser | — |
| T24 | ไทยชื่อยาวหลายหน้า/mixed items/Void slip/มือถือprint | PASS | 2 | browser | COMPANY, DEVICE |
| T25 | ผู้ใช้Aร้องorder/receipt/assetของBและเปลี่ยนบัญชีกลางrequest | PASS | 3 | browser, world | MANGOS |
| T26 | CSRF/origin/role/forgedPaid/ID tampering | PASS | 3 | browser | — |
| T27 | bankcallbackผิดauth/source/forwardedheaderปลอม | PASS | 15 | — | BANK_PROFILE |
| T28 | UAT payload/keys/refs ถูกส่งสู่Prod | PASS | 8 | — | RELEASE |
| T29 | Void success/0001/0002/0008/0009/Q-prefix | PASS | 11 | browser | BANK_VOID |
| T30 | Void timeoutแล้วEnquiryสำเร็จ/ไม่พบ/error | PASS | 3 | — | BANK_VOID |
| T31 | Void ตรงก่อน/หลัง22:56/หลังเที่ยงคืน | PASS | 2 | — | BANK_VOID |
| T32 | Partial return/Cross-border/นอกVoid | PASS | 3 | browser | COMPANY, CROSS_BORDER |
| T33 | SFTP hostkeyผิด/PGPผิด/ไฟล์ยังไม่ครบ | PASS | 2 | — | BANK_REPORTS |
| T34 | reportไทยUTF8/padding/lineending/500ตำแหน่ง | PASS | 7 | — | BANK_REPORTS |
| T35 | H/D/T totalsผิด/ไฟล์เดิมซ้ำ/ชื่อเดิมhashใหม่ | PASS | 8 | — | BANK_REPORTS |
| T36 | credit+D+V/LOST credit dateต่างpayer date | PASS | 3 | — | BANK_REPORTS |
| T37 | callbackหายแต่พบreport / localpaidไม่ตรงreport | PASS | 3 | — | BANK_REPORTS |
| T38 | Bridgeclick/mouse/pinch/wheel/resize/scroll/fullscreen | PASS | 0 | browser, unity | DEVICE |
| T39 | modelโหลดไม่ได้/ผิดID/assetversionเก่า | PASS | 3 | browser, unity | DEVICE |
| T40 | Logout/worldchange/error/overlayclose | PASS | 2 | browser, unity, world | MANGOS, DEVICE |
| T41 | PWAupdate/reload/tabclose/อีกอุปกรณ์ | PASS | 1 | browser, pwa | DEVICE, MANGOS |
| T42 | desktop/mobileธนาคารจริงและsaveQR | PASS | 0 | browser, unity | BANK_QR, DEVICE |
| T43 | callbackburst+PDFqueue+Unityload | PASS | 2 | unity, load | LOAD |
| T44 | DB/worker/เครื่องrestartและrestoreจากbackup | PASS | 8 | — | RESTORE |
| T45 | certificate/JWT/PGP rotation | PASS | 15 | — | ROTATION |
| T46 | schema rollout/rollback ขณะมีpending orders | PASS | 2 | — | RELEASE, RESTORE |

## Pending acceptance gates

- **BANK_PROFILE**: TTB-issued UAT identity/keys, HTTPS/authentication and actual callback peer/source verification. Status: pending; no inferred approval.
- **BANK_QR**: Bank-approved QR profiles, expiry/purge behavior and scans/imports with actual banking apps. Status: pending; no inferred approval.
- **BANK_REPORTS**: Actual pinned SFTP/PGP files, parser profile, C/D/V/LOST and settlement comparison. Status: pending; no inferred approval.
- **BANK_VOID**: Authorized UAT Void/Enquiry outcomes and Bangkok banking-date/cutoff boundaries. Status: pending; no inferred approval.
- **CROSS_BORDER**: Bank/corporate approval of cross-border fields/currencies/refund policy; feature remains closed. Status: pending; no inferred approval.
- **MANGOS**: Real MANGOs identity/cookie/session acceptance on the intended domain. Status: pending; no inferred approval.
- **COMPANY**: Approved seller/receipt/fulfillment/return/stock and independent-refund policies. Status: pending; no inferred approval.
- **DEVICE**: Physical Android/iOS/browser, banking-app, fullscreen/orientation and save/print acceptance. Status: pending; no inferred approval.
- **RESTORE**: Authorized UAT and populated Local SQL/private-file drills passed; continuous off-server RPO, production key recovery/RTO and bank loss-window reconciliation remain pending. Status: pending; no inferred approval.
- **ROTATION**: Environment-specific TLS/JWT/PGP/SFTP/session/backup custody and rehearsed rotation/recovery. Status: pending; no inferred approval.
- **LOAD**: Measured simultaneous callback/PDF/Unity workload against agreed capacity and latency targets. Status: pending; no inferred approval.
- **RELEASE**: Compatible rollout/rollback rehearsal with pending orders and final production acceptance. Status: pending; no inferred approval.

## Evidence and reproduction

Mappings: `Infrastructure/TTB/acceptance/matrix.json`. Checker: `check-matrix.py --trx <completed TRX> [<additional TRX> ...]`. Latest outcome wins for each exact parameterized test; an older pass cannot mask a newer failure. Every selector must match actual tests. Missing/failed/skipped results and incomplete browser suites make the local matrix incomplete.

Sanitized machine report: `.artifacts/acceptance/requirement-matrix.json` includes artifact SHA256/timestamps and exact selectors. Run the documented PostgreSQL test wrapper and the five Playwright configurations to reproduce. The bounded Local Mock load experiment is not a bank-callback or production-capacity benchmark. Source and build artifacts are separate from bank/device/operations acceptance. No bank credential, private data or fabricated external acceptance is stored in this report.

- `.artifacts\test-results\commerce-ci-full_net10.0_20261019233520.trx` — 268 results; SHA256 `b6cde81009499c4c1a8481d90a300955e8bdfa2efa7aa904b4b5252ad8853a0b`.
