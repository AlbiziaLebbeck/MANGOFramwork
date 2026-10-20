# TTB implementation decisions

วันที่: 18 ตุลาคม 2026. ผู้ใช้อนุมัติให้ดำเนินแผนทั้งหมดแล้ว รายการนี้ล็อก default ทางเทคนิคเพื่อเดินงาน โดยไม่แทนข้อมูลที่ธนาคารหรือบริษัทต้องออกให้จริง

| Decision | ค่า/แนวทาง |
|---|---|
| Scope | ผู้ขายรายเดียว มี Physical/Digital และ mixed order |
| Currency | THB จำนวนเงิน integer satang; ไม่รับ floating point จาก client |
| Stack | ASP.NET Core net10.0 + PostgreSQL; React/TypeScript/Vite; Unity6000.3.26f1 |
| Source | สร้าง Services/TtbCommerce ใหม่ แยกจาก KKU; อ้างอิงพฤติกรรม/API/bridge ที่ตรวจแล้ว |
| Session | ต้องตรวจ MANGOs identity ที่ Backend; guest ดูสินค้าได้ แต่ไม่สร้างออเดอร์เงินจริง |
| Payment | local Dynamic QR, profile Z+E เป็นข้อเสนอสำหรับ UAT;ธนาคารต้องรับรองก่อนเปิดจริง |
| Expiry | default10นาที ปรับ config;หนึ่ง active attempt ต่อ order;เก็บ attempt เก่าไว้ |
| Verify | manual เมื่อรอประมาณ60วินาที; shared budget Verify+Enquiryสูงสุด3/attempt + cooldown/single-flight/merchant limits |
| Callback | environment จาก deployment/host; durable commitก่อนACK; no PDF/delivery in request |
| Outbox | durable PostgreSQL jobs, lease, retry, effect idempotency |
| Fulfillment | physical จัดส่ง/รับสินค้าผ่านadmin; digital kindกำหนดต่อSKU;ไม่รวมcarrier APIภายนอกในbaseline |
| Delivery defaults | Pickupเปิด; Shippingปิดจนบริษัทกำหนดค่าขนส่ง. ทดสอบShippingด้วยpolicyเฉพาะtest. ไม่สมมติว่าจัดส่งฟรีในระบบจริง |
| Documents | receiptบริษัท+Void slip;เลข/snapshotคงเดิม; issuerยังไม่ยืนยันต้องไม่ออกเอกสาร production |
| Bank provisioning | Merchant/Biller/iss/keys/SFTP/callback auth ที่ขาดต้องคง unavailable ไม่สร้างค่าปลอมสำหรับ bank mode |
| Biller ที่ผู้ใช้ยืนยัน | `075555900002101` เคยทดสอบผ่าน; ใช้อ้างอิง UAT เท่านั้น จนธนาคารยืนยัน scope/Production. ผู้ใช้ยังไม่มี Merchant ID/JWT/SFTP |
| Mock | development/test เท่านั้น ไม่เปิด dev-paid route ใน bank/production mode |
| Isolation | ttb-uat/ttb-prod แยก DB/secrets/files;ไม่ใช้ KKU DB/session secret |
| Route proposal | Shopper /ttb-commerce/v1 และ UAT /ttb-commerce-test/v1; callback /ttb/v1/credit-notification บนhostที่ผู้ใช้กำหนด |
| Production | ปิดเงินจริงจนผ่านconfiguration/UAT/operations gates; codeและmock workเดินต่อได้ระหว่างรอ |
| UAT backup approval 19Oct2026 | ผู้ใช้อนุญาตเฉพาะreadonly`ttb_backup`บน`ttb_uat`, Secret, encrypted20Gi pool, ทุก15นาทีหลังซ้อมผ่าน และกู้คืนฐาน/namespaceใหม่; private recovery keyอยู่PC. ไม่ครอบคลุมKKU/Production |
| Bank resilience | circuitแยก4services ค่าเริ่มต้น5failures/30seconds/oneexplicittrial; ไม่มีautomaticbankretry. Callbackไม่ผ่านoutboundcircuit; UnknownVoid/sendfenceและbudgetยังdurable |
| Recovery evidence | หลังเพิ่มissuer table: UAToff-serverSQL44tables/21rowsผ่าน11.318s; populatedLocal44tables/4067rows/127filesผ่าน12.653s รวมcompanyprofile1row. CronUATเปิดแล้ว; continuousoff-serverRPO/retention/Productionescrow/alertreceiverยังรอยืนยัน |
| TLS UAT preflight | DNS01CertificateUATพร้อมและHTTPSCA/hostnameผ่าน โดยมีเฉพาะoperator-onlyExacthealthroute; publicshopper/callbackยังปิด. TLSkeyแยกจากJWT/PGP/backupkey และobserverอ่านเฉพาะpubliccert |
| Shared ingress source IP | ผู้ใช้อนุญาตทดสอบCluster→Local/ตรวจKKU/rollbackหากผิดปกติแล้ว. เปลี่ยน19Oct08:44UTCเป็นLocal/RV5847288/healthport31842; correlatedlogsเห็นPCpeerจริง, allowlist200/forgednode403, KKU9publicGETstatus+hashตรงbaseline/APIhealthปกติ. คงLocalไว้พร้อมversion-boundrollback; ไม่ได้ทดสอบactiveKKUsession/WebSocket. Callbackยังปิดรอapplicationproxyhandoff/TTBNAT/authจริง |
| PGP archive rotation | เก็บexactrecipient/signerfingerprintsต่อarchive; retiredpinsใช้ได้เฉพาะimmutablearchiveที่ตรงและallowlistชัดเจน ไม่เปิดรับไฟล์ใหม่ด้วยoldkeys. RealPGP/PostgreSQLsyntheticrotationผ่าน แต่actualbankcutover/escrow/expiryยังรอ; legacy/unsignedpolicyไม่เดาหรือdowngrade |
| JWT key lifecycle | อ่านRSA2048privatekeyต่อexplicitrequest ไม่มีoldkeyfallback/retry; privateprobeไม่ส่งkey/path/issuerและยังทำงานเมื่อSQLdown. วันเปลี่ยนใช้optionalapprovedOperations__BankSigningKeyRotationDueUtcเท่านั้น ไม่เดาจากtoken/TLS/filemtime. Missingtelemetryจากolder/partialreleaseมีalert; actualbankregistration/overlap/deadline/notificationยังรอ |

รายละเอียดแผน: [TTB_SYSTEM_DEVELOPMENT_PLAN_TH.md](<C:/CosmoPC/Work/Unity/MANGOxTTB/Documentation/TTB_SYSTEM_DEVELOPMENT_PLAN_TH.md>)

