# TTB UAT และแผนเปิดใช้งานจริง

สถานะ 20 ตุลาคม 2026: ระบบ Local/Mock และ private UAT พัฒนาแล้ว แต่ **ยังไม่ผ่าน bank UAT และยังไม่เปิดรับเงินจริง**. Biller `075555900002101` เป็นค่าที่ผู้ใช้ยืนยันว่าเคยทดสอบผ่าน ใช้ใน UAT เท่านั้น; ยังไม่ทราบ Merchant ID, JWT issuer/การลงทะเบียน public key, callback authentication และ SFTP/PGP ที่ธนาคารออกให้. เตรียมRSA2048UAT/publickeyแล้วและgatewayTLSจากNTผ่าน แต่ยังไม่ผ่านbankregistration; ดู TTB_REAL_PAYMENT_READINESS_TH.md. ไม่มีการเดาค่าเหล่านี้หรือส่งคำขอการเงินจริงไป TTB ระหว่างพัฒนา

เอกสารนี้เป็นลำดับปฏิบัติงาน P8-02/P9-01 ที่เตรียมไว้ ไม่ใช่หลักฐานว่าได้ทำขั้นตอนที่ยังรอข้อมูลแล้ว. ผลอัตโนมัติและรายการค้างอยู่ใน [requirement matrix](TTB_REQUIREMENT_MATRIX.md); รายการขอ provisioning อยู่ใน [ร่างสำหรับ TTB](TTB_PROVISIONING_REQUEST_TH.md) ซึ่งยังไม่ได้ส่งให้ธนาคาร

## 1. สิ่งที่พร้อมและสิ่งที่ยังต้องตรวจรับ

| ส่วน | พร้อมใช้งานทดสอบภายใน | สิ่งที่ยังรอ |
|---|---|---|
| หน้าร้าน | Unity model click → HTML/React overlay, หมุน/ซูม/รีเซ็ต, variant/จำนวน, mixed cart/quote/QR จำลอง | โมเดล/ราคา/stock จริง, อุปกรณ์จริงและเว็บไซต์ MANGOs เป้าหมาย |
| หลังชำระ | durable payment/allocation, physical fulfillment, digital inventory/download/game lease, PDF/history | รายการชำระจากธนาคารจริงและนโยบายบริษัท |
| การเงิน | cases/audit/roles, full Void/Enquiry adapter, manual external-refund evidence + คนอนุมัติคนละคน, คืนสินค้ารายหน่วย | TTB-approved Void profile, policy คืนเงิน/สิทธิ์/stock; ระบบ manual ไม่ได้ส่งเงินออกให้ |
| Reports | encrypted archive/PGP/parser/version/replay/C-D-V/LOST/daily comparison; synthetic PGP rotation พร้อม immutable pins/archive-only trust | SFTP/host fingerprint/PGP และไฟล์จริง/ยอด statement, bank key-switch/escrow/legacy acceptance |
| Operations | private API/backup/TLS metrics, JWTkeyavailability/explicitdeadline/missingtelemetry, syntheticPGP/JWTrotation, freeze/readerfloor3, encryptedUATbackupทุก15นาที, actualUAT+populatedLocalSQL/file restore, runbooks | continuousoff-serverstorage/retention, load/actualbankkeypolicy/rotation/escrow, ผู้รับalertและhost-loss |

## 2. ตรวจ network และแยก environment ก่อนเชื่อมธนาคาร

ใช้ `ttb-api-test.mangosgo.com` สำหรับ UAT และ `ttb-api.mangosgo.com` สำหรับ Production แม้ทั้งคู่ชี้209.15.122.115. Routing ต้องยึด hostname/SNI และ service ของ environment; database, credentials, private files, job queue, identifiers และ bank profile ต้องแยกกัน. KKU namespace `mangos` และ deployment `kku-commerce-api` เป็นอีกระบบหนึ่ง ห้ามใช้ฐานหรือกุญแจร่วมกับ TTB

1. ตรวจ DNS จากฝั่งบริษัทและฝั่งธนาคาร, HTTPS chain/hostname/SNI/expiry และ clock synchronization. หลักฐาน TCP443 connected เดิมไม่ยืนยัน TLS หรือ application callback
2. ตรวจ outbound จาก ntserver02 ไป gateway ของ UAT ตามเอกสาร: `uat-dgw.tau2904.com:9643`; SFTP4022/4028 ตาม profile ที่อนุมัติ. แยกหลักฐาน DNS/TCP/TLS ออกจาก JWT/business response
3. ตกลง full callback URL กับ TTB; path ที่เสนอคือ `/ttb/v1/credit-notification`. ยังไม่ apply public callback ingress ก่อน auth/TLS/real-peer checks ผ่าน. ผู้ใช้อนุญาตและเปิด operator-only shopper/game/API preview แล้วตามขอบเขตด้านล่าง; ไม่ใช่การเปิด callback หรือเงินจริง
4. พิสูจน์ source-IP จริงถึง application. SharedServiceLocal และ narrow controller-to-API handoff ผ่าน operator/forged-header probes แล้ว. ยังต้องตรวจ TTBNAT peer/auth จริง; อย่าเชื่อ`X-Forwarded-For`จากclientหรือถือว่าoperatorprobeเท่ากับbankcallback
5. ตรวจคำขอจากแหล่งไม่อนุญาต, auth ผิด, forwarded header ปลอม และ hostname ผิดว่าไม่เข้ากระบวนการยืนยันเงิน. ตรวจ readiness แยก bank/report/company flags; pod Ready อย่างเดียวไม่ใช่ผ่าน UAT

หลักฐาน19Oct08:47UTC: ผู้ใช้อนุญาตทดสอบCluster→Local/ตรวจKKU/rollbackหากผิดปกติแล้ว. ServiceจริงLocal/RV5847288/healthCheckNodePort31842; operatorhealth200, nodeที่ปลอมoperatorheaders403, correlatedlogsเห็นPCpeerจริงไม่ใช่CNIbridge. KKUหน้า+7UIassetsรวม9GETให้status/hashตรงbaselineและAPIhealth/deploymentปกติ. Monitoring3Up/32ruleshealthy; จึงคงLocalและเก็บversion-boundrollbackที่serverdry-runคืนfullspecเดิมได้. ยังไม่ได้ทดสอบKKUauthenticatedsession/activeWebSocket. Certificate/CA/hostnameผ่าน; มีเพียงoperator-onlyExacthealthrouteและshopper/callback/metricsยังปิด. รายละเอียด`TTB_NT_NETWORK_CHANGE_REQUEST_TH.md`และ`.artifacts/tls-qa/`

สถานะล่าสุด20Oct04:07BKK: ผู้ใช้อนุญาตเฉพาะ IP58.11.26.155/32 แล้ว. เปิด `/store-uat`, exact `/ttb-game-uat` และ `/ttb-commerce-test/v1` ด้วย Ingress แยก; callback/metricsยัง404และbankpaymentsfalse. Public HTTPS/WSS browser1/1ผ่าน28.0s รวมnative3D desktop/mobileviewport และคืนHUD; outside-IP/forgedXFF403ครบ6กรณี, UAT9Ready/financial+identity18counts0/KKU9GEThashเดิม. ไม่ทดสอบ authenticated MANGOs/โทรศัพท์จริง/publicPWA/เงินจริง. ดู `TTB_UAT_WORLD_DEPLOYMENT.md`

รายละเอียดhandoffอยู่ใน`TTB_PROXY_HANDOFF_ACCEPTANCE.md`: live CNI/application probes ผ่านแล้วหลังคำสั่งได้รับการตรวจอนุมัติ; UAT trustcontroller `10.42.0.7/32` ผ่าน optional `ttb-transport` และเอา observation window ออก. ข้อความ usage-limit/pending proof เดิมเป็นประวัติที่ถูกแทนแล้ว. การรับรอง TTBNAT/auth/callback จริงยังต้องตรวจร่วมกับ NT/TTB; operator handoff/preview ไม่ใช่ bank UAT

## 3. จัด provisioning ลงที่เก็บลับ

เจ้าของระบบระบุตำแหน่งที่เก็บบน NT/secret manager ให้ผู้ปฏิบัติงาน ไม่ส่ง password/private key ลงแชต. ตรวจ environment และสิทธิ์อ่านของ process ก่อน mount. แยก JWT signing, callback auth, SQL runtime/owner/backup, session/game service, SFTP/PGP และ backup recovery key ออกจากกัน

ข้อมูล TTB ที่ต้องยืนยัน: Merchant3/Biller/account/issuer, public-key registration และ RS256 key, QR Z+E profile, headers/service names, callback scheme/NAT sources/retries/ACK, Verify/Enquiry codes/limits, Void banking day/cutoff, SFTP suffix/path/port/fingerprint, report encoding/layout/totals convention, PGP recipient/integrity และ Cross-border scope. ตัวอย่างใน PDF และรูปทดสอบไม่ใช่ credential ที่นำมาแทนค่าจริงได้

ข้อมูลบริษัทที่ต้องยืนยัน: ชื่อ/ที่อยู่/เลขผู้เสียภาษี/เลขเอกสาร, เจ้าของ stock/การรับหรือส่งสินค้า/ค่าขนส่ง, นโยบาย digital grant/revoke/ของที่ใช้แล้ว, การคืนเงิน/สองเจ้าหน้าที่, retention/backup custody และ on-call. ใบเสร็จในระบบเป็นใบรับเงินตาม profile ไม่ประกาศว่าเป็นใบกำกับภาษีโดยอัตโนมัติ

## 4. ชุด bank UAT ตามความสามารถทั้งหมด

บันทึกทุกกรณีด้วย environment/release/config revision, UTC+BKK time, correlation/operation ID ในระบบ, expected/actual ACK/result, ยอด satang, ledger/stock/job counts, document hash และ report/statement reference. หลักฐานส่งต่อใช้ข้อมูลปกปิดบุคคล/บัญชี/keys; raw callbacks/reports คงใน private storage ตามสิทธิ์

| ความสามารถ TTB | กรณีที่ต้องทดสอบกับธนาคาร | ผลที่ยอมรับ |
|---|---|---|
| QR สร้างที่ backend | payload/CRC/Biller/refs/amount/QRId, สแกนและนำเข้ารูป, expiry นาที/ข้ามวัน/สิ้นเดือน, QR ที่หมดอายุ | แอปธนาคารแสดงผู้รับ/ยอด/refs ถูก; UI/payload/reservation สอดคล้อง; ไม่สมมติว่ามี Create QR endpoint |
| Credit Notification | credit ปกติ, ซ้ำก่อน/หลัง worker, ACK สูญหาย, malformed/auth ผิด, refs/amount ผิด, late/extra credit | commit ถาวรก่อน ACK, allocation/stock/entitlement อย่างละครั้ง; discrepancy เปิด case ไม่แจกสินค้าทันที |
| Verify Slip | success/no data/already void/business error/timeout, Ref2 optional, manual delay/cooldown/limits | ใช้ผล service+payment ตาม contract; status GET ไม่เรียกธนาคาร; ไม่ใช้ OCR/รูปสลิปเป็นคำยืนยัน |
| Enquiry QR | หลาย transaction, static/dynamic, duplicate/conflict, ช่วงเวลาถูก/เกิน24ชม | exact identity ไม่จับคู่จากยอด; budget ร่วม Verify; ไม่ retry อัตโนมัติเมื่อผลไม่แน่นอน |
| Merchant Void | full amount ของเครดิตที่มีสิทธิ์, primary/extra/unmatched ที่พิสูจน์เวลาได้, cutoff22:56/ข้ามวัน, timeout | transaction ID เดิม/ส่งครั้งเดียว; primary ปิดสิทธิ์ตาม policy; extra ไม่ทำลายสิทธิ์จากเงินหลัก; ไม่รองรับ partial/Cross-border ผ่าน Void |
| Enquiry Void | pending/unknown → success/not-found/error/duplicate response | รักษา send fence และ original ID; unknown จนมีหลักฐาน; ใบ Void แยกจากใบเสร็จเดิม |
| SFTP reports | pinned host/PGP, ISO/LOST, UTF8/500ตำแหน่ง, H/D/T, C/D/V, missing/reissue/replay | hash/recipient/totals/layout ตรงไฟล์จริง; V ไม่หักเงินซ้ำ; LOST ไม่สลับ payer-debit กับ operation-credit time; ยอดกระทบ statement |

Cross-border ต้องมีกรณีเฉพาะ Amount/Currency/fees/settlement/conditional payer fields และการคืนเงินที่ TTB+บริษัทอนุมัติ. จนกว่าจะผ่าน ให้คง capability นี้ปิดและเก็บ credit ที่พบเพื่อ review ตาม pipeline

กรณีที่มีเงินจริงต้องระบุบริษัท/บัญชีผู้รับ, ผู้ปฏิบัติ, จำนวนต่อรายการ/เพดานรวม, วันเวลา และกรณี Void ที่อนุมัติไว้ก่อน. การทำตามแผนพัฒนาไม่ได้กำหนดวงเงินเหล่านี้แทนผู้ใช้. ขณะนี้ยังไม่มีการจ่าย/คืน/โอนเงินจริง

## 5. ตรวจรับอุปกรณ์และงานปฏิบัติการ

ตรวจบนอุปกรณ์จริงตามที่ประกาศรองรับ: desktop Chrome/Edge, Android/iOS, portrait/landscape/fullscreen, keyboard/focus, touch/pinch/wheel, save/import QR และ save/print Thai PDFs. ระบุแอปธนาคารและรุ่นที่ทดสอบจริง; browser mobile emulation ไม่แทนโทรศัพท์จริง

ทดสอบ MANGOs login/cookie/session จาก domain เป้าหมาย, reload/เปลี่ยนเครื่อง/เปลี่ยนบัญชี/ออกจากโลก, เจ้าของข้อมูล/PDF/private asset, network หาย, PWA อัปเดตหลายแท็บโดย QR เดิมไม่หาย. Unity เฉพาะการเลือก/แสดงผลไม่เป็นผู้กำหนดราคา ผู้ซื้อ หรือ Paid

ทดสอบโหลดด้วย Mock เท่านั้น: callback burst + PDF backlog + Unity พร้อมกัน; วัด P50/P95/P99, durable ACK/error/duplicates, queue age/retries/lease recovery, CPU/memory/disk และจำนวนผลทางการเงินก่อน/หลัง. เป้าความจุ/จำนวนผู้ใช้ต้องตกลงกับ NT; ไม่ส่ง load Verify หรือ Void ไปธนาคารเพื่อ benchmark

มี bounded Local baseline แล้ว:8ออเดอร์ดิจิทัล/16HTTPพร้อมคำขอซ้ำ, Unityเปิดอยู่จริง, สิทธิ์8รายการ/PDF8ใบครบ. รอบ19Oct02:44UTCได้ LocalMock HTTP P50≈1.10s/P95≈1.33s และงานพร้อมครบ≈9.52s. ไม่มีautomaticbankcheck/pageerror. นี่ไม่ใช่การวัด callback ผ่าน network ของธนาคารหรือการรับรองความจุProduction; browser rAF timing ก็ไม่ใช่UnityFPSหรือผลโทรศัพท์จริง. หลักฐาน `.artifacts/acceptance/bounded-local-load.json`

ซ้อมสำรอง/กู้คืนตาม [operations runbook](TTB_OPERATIONS_RUNBOOK.md) ใน target ใหม่ที่ปิด egress/ไม่มี workers. ต้องทดสอบทั้ง UAT schema และ dataset จำลองที่มีเงิน/stock/allocations/documents/private proofs/jobs/send fences/unknown Void จริงในฐานทดสอบ. เทียบทุกแถว/sequence และทุก private hash ก่อนเริ่ม frozen readers; reconcile loss interval ก่อน resume. ผลเข้ารหัส/แตกไฟล์สำเร็จอย่างเดียวไม่ถือว่า SQL restore ผ่าน

ต้องมี off-server destination/retention/key recovery ที่เจ้าของระบบรับรอง, วัด archive age และ restore time, ทดสอบ rotation/expiry และผู้รับ alert ที่ได้รับอนุญาต. ปัจจุบัน collector ภายในมี3targetsUp/32ruleshealthy และJWTkeyavailability/explicitdeadline/missingtelemetryผ่านsyntheticQA แต่ยังไม่มีexternalreceiver. UATยังไม่มีissuer/privatebankkey/approvedrotationdeadline จึงไม่สร้างวันหมดอายุจากtoken/TLS/filemtime. Actualbankkeyregistration/overlap, PGPexpiry/custody และการส่งalertจริงยังรอตรวจรับ; rulesที่โหลดได้ไม่เท่ากับทดสอบdependencyครบ

## 6. ลำดับ Production เมื่อทุก gate ผ่าน

1. Finance/ผู้ขาย/NT/TTB ลงผลตรวจรับและปิดข้อค้างที่เกี่ยวกับ feature ที่จะเปิด. Feature ที่ยังไม่ผ่านคงปิดและไม่แสดงว่าใช้งานได้
2. เตรียม `ttb-prod`/ฐาน `ttb_production`/private PVC/Secret/release directory ของ Production แยกจาก UAT และ KKU. Template config ที่เตรียมไว้ยังไม่ใช่ provisioning หรือสิทธิ์สร้างบัญชีอ่านข้อมูลเพิ่ม
3. ยืนยัน Production Merchant/Biller/account/keys/report profile/TLS/identity origins ใหม่ทั้งหมด. ไม่ copy Biller UAT หรือ credential UAT มาใช้. ตรวจ NT network/auth/source-IP ตามหัวข้อ2
4. บันทึก immutable release SHA256, compatibility contract≥3, migrations และ recovery point ที่กู้ได้. ใช้ role migration แยกจาก runtime; ตรวจ database environment marker ก่อน migrate
5. เริ่ม private/frozen inspection ก่อน public route. ทุก API/worker ต้องรู้จัก `operations-freeze-v1`; ตรวจ mutation/callback503 และ workers ไม่ lease งาน. เปิด monitoring ที่มี receiver/capacity/expiry/backup coverage ตามที่ตรวจรับแล้ว
6. ทำ compatible rollout/rollback rehearsal ขณะมี pending order; คง order/attempt/numbering/effect keys/void fences. Down migration ที่ลบข้อมูลเงินถูกปฏิเสธ; code rollback ต้องไม่ต่ำกว่า reader floor. ห้ามใช้ backup เก่าเป็น routine rollback
7. เปิด exact hostname/routes ที่ผ่านตรวจรับ และปลด freeze/เปิด capability ตามลำดับที่ตกลง. ตรวจ KKU readiness และ TTB health/profile flags ทุกขั้น; ไม่แก้ shared controller/DNS/route ของ KKU เป็นส่วนหนึ่งของ release นี้
8. ผู้ปฏิบัติทำ controlled transaction ภายในวงเงินที่อนุมัติ. ตรวจ callback/BankTransaction/Allocation/stock/digital delivery/PDF/history และการส่งมอบจริง; จากนั้นรอ report/statement รอบที่เกี่ยวข้องเพื่อยืนยัน settlement
9. Finance/ผู้ขาย/NT ยืนยันผลสุดท้าย รวมวิธีรับ incident/unknown Void/manual case. หากพบ discrepancy ให้ปิด capability ที่กระทบ รักษาหลักฐานและทำ reconciliation ตาม runbook; ไม่ resend money command

ขณะนี้ P9 เป็น **NO-GO** เพราะ bank UAT, provisioning, company/device/operations acceptance และวงเงิน controlled transaction ยังไม่ครบ. ยังไม่ได้สร้าง Production namespace/credentials/public ingress หรือเปิดรับเงินจริง

## การทำซ้ำผล Local

รันจาก repository root โดย PostgreSQL wrapper จะสร้างฐานชื่อสุ่มเฉพาะ development และลบทิ้งเฉพาะฐานของตัวเอง:

```powershell
& Services/TtbCommerce/tools/test.ps1 -Postgres
```

จาก `WebCommerce` ใช้ `pnpm run check`, `pnpm run test`, `pnpm run build` และ `pnpm exec playwright test`. Compiled Gallery/World ใช้ config `playwright.unity.config.ts`/`playwright.world.config.ts` โดยต้องเปิด Local servers ตาม contract. PWA config `playwright.pwa.config.ts` จัด fixture server5176 ของตัวเอง; ให้หยุดเฉพาะ owned static server ก่อนแล้วเปิดกลับหลังทดสอบ. อย่าปิด Unity Editor หรือ server ของผู้ใช้เพื่อแทนขั้นตอนนี้

`Infrastructure/TTB/acceptance/check-matrix.py --trx <completed TRX files>` สร้าง matrix จากหลักฐานจริงและ JSON ของทั้งห้า Playwright suites. ผลที่ใหม่กว่าทับผลเก่าสำหรับ test เดิม; failed/skipped/missing ทำให้ตรวจไม่ผ่าน. Matrix ไม่ยกระดับ Mock เป็น bank acceptance

เพิ่มเติม `playwright.load.config.ts` สำหรับ boundedLocalbaseline8orders/16HTTP; configทั้งห้าและtest foldersทั้งหมดอยู่ในTypeScriptcheck. อย่าเปลี่ยนจำนวนโหลด/host/profileเพื่อทำ benchmark ต่อธนาคารจริง
