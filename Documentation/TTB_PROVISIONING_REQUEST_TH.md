# ข้อมูลที่ต้องยืนยันกับ TTB ก่อนเปิดทดสอบจริง

ร่างสำหรับส่งให้ผู้ประสานงานธนาคาร — ยังไม่ได้ส่งข้อความให้บุคคลภายนอก

บริษัทกำลังพัฒนาระบบขายสินค้าจริงและดิจิทัลในเมตาเวิร์ส ผู้ขายรายเดียว รับเงินเข้าบัญชีบริษัทเดียว. Biller ที่เคยทดสอบผ่านตามข้อมูลบริษัทคือ **075555900002101**. Server public IP **209.15.122.115**, UAT callback host **ttb-api-test.mangosgo.com**, Production **ttb-api.mangosgo.com**; แยก service/DB/secrets ภายใน server เดียวกัน

สถานะเตรียม19Oct2026: HTTPSของUATตรวจCA/hostnameผ่านแล้ว และมีhealth-onlypreflightแยกจากAPIการเงิน. หลังทดสอบsharedServiceLocalตามคำอนุญาตบริษัท ingressเห็นpublicpeerจริงและoperatorallowlistผ่าน; KKUที่ตรวจยังปกติ. Application proxy handoff ของ operator ผ่านแล้วด้วย health-only HMAC proof และ narrow controller /32. CallbackยังปิดรอTTBNATpeerจริงและauth/profileจากTTB จึงยังไม่ส่งURLให้ธนาคารยิงธุรกรรมจริง. บันทึกนี้ไม่ใช่การรับรองว่าcallbackพร้อมหรือผ่านbankUAT

อัปเดต20Oct2026: เตรียมUATpublickeyRSA2048สำหรับลงทะเบียนแล้ว: `Provisioning/ttb-uat-jwt-5183ee1873474f4ab9aff6f1b530d588-public.pem`, DER-SPKI SHA256 `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`. Privatekeyอยู่UATSecret/สำเนาPCเข้ารหัส ไม่อยู่ในเอกสาร. จากAPIpodเชื่อมgatewayUAT9643ด้วยTLS1.2และCA/hostnameผ่านโดยไม่มีHTTP/payment request. Bankregistration/issuer/Merchant/QR/callbackprofileยังไม่ครบ; ร่างย่อ4กลุ่มสำหรับเริ่มทดสอบแอปจริงอยู่ `TTB_REAL_PAYMENT_READINESS_TH.md`

ขอข้อมูล/ยืนยันดังนี้ โดยส่ง secrets ผ่านช่องทางที่ปลอดภัยตามมาตรฐานธนาคาร ไม่ส่งลง Git หรือเอกสารสาธารณะ:

1. Merchant ID3ตัวอักษรของ UAT และ Production, Biller15หลักของแต่ละ environment, บัญชีรับเงินและชื่อผู้รับเงินที่ผู้ซื้อจะเห็น. Biller ข้างต้นเป็นของ environmentใด และใช้รับชำระสำหรับระบบนี้ได้ตามข้อตกลงใด
2. JWT RS256: ค่า `iss`, อายุ token/clock skew ที่ยอมรับ, ขั้นตอนลงทะเบียน public keyของบริษัท และตัวอย่าง headers ที่ตรง gatewayปัจจุบัน โดยเฉพาะ Request-uid/Service-Name/sender_name. ขอ policy วันเปลี่ยนกุญแจ/วันหมดอายุ registration หรือ certificate หากมี, ช่วงที่ยอมรับ public keyเก่า/ใหม่พร้อมกัน, ขั้นตอนเพิกถอนและตรวจรับการเปลี่ยนใน UAT. ไม่ขอ private keyของธนาคาร
3. Credit Notification: อนุมัติ full URL `https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification`; authentication ใช้ Basic, mTLS, signature หรือวิธีใด และรูปแบบ key rotation. ยืนยัน NAT sources203.148.142.200 และ203.148.143.101–105. ขอ callback sample ที่ปกปิดข้อมูลบุคคล/บัญชีและ ACK/retry behaviorล่าสุด
4. Dynamic QR: อนุมัติ QRIdรูปแบบ Z+Merchant3+E+YYMMDDHHMM+running5, biller/ref rules, วันหมดอายุตาม Asia/Bangkok, ตัวอย่าง payloadที่สแกนผ่าน และ QR merchant display name/brandingล่าสุด
5. Verify Slip/Enquiry: ยืนยัน no-data codeตาม spec3.01 (Verify0002 เทียบตัวอย่างเก่า0001), successful ResultCodeตามแต่ละ service, สิทธิ์/อัตราเรียก/ช่วงเวลา, วิธีทำ UATจำลองชำระและ callback รวมกรณี timeout/duplicate/เงินเข้าหลัง QRหมดอายุ
6. Merchant Void/Enquiry Void: ยืนยัน TransactionId17หลัก, banking-day cutoff22:56 และกรณีชำระหลังcutoff, response timeout reconciliation, การจัดการกรณีที่ไม่มีInstructionIdใช้Voidได้ และ testcasesที่ธนาคารกำหนด. ไม่พบpartial refund APIในชุดเอกสารนี้
7. SFTP/PGP: host/port4022หรือ4028, username/authentication, SSH host key fingerprint, ขั้นตอนแลกPGP keys, path/report schedule, ไฟล์BPQR_ISO/BPQRLOSTตัวอย่างแบบปกปิดข้อมูล, 500ตำแหน่งนับbyteหรือcharacterในUTF-8, code C/D/V และยอดรวมที่ต้องกระทบยอด. ขอ exact encryption/signing fingerprints, วันหมดอายุ/วันสลับกุญแจ, การจัดการรายงานค้างที่เข้ารหัสด้วยกุญแจเก่า, การส่งใหม่ด้วยกุญแจปัจจุบัน และขั้นตอนแจ้งเพิกถอน/ตรวจรับ rotation ใน UAT
8. Cross-border: ต้องเปิดสิทธิ์เพิ่มเติมหรือไม่, Amount/Currencyของcallback/reportมีความหมายใดเมื่อQRเป็นTHB, อัตราแลกเปลี่ยน/ค่าธรรมเนียม/settlement และวิธีคืนเงิน เนื่องจากMerchantVoidไม่รองรับ. จะยังไม่เปิดส่งมอบอัตโนมัติสำหรับกรณีนี้จนยืนยัน
9. UAT sign-off/Production onboarding: case checklist, ผู้ประสานงาน, วันเวลาทดสอบ, การอนุมัติ test amountจริง และหลักฐานที่ต้องใช้ก่อนเปลี่ยนเป็นProduction

สิ่งที่บริษัทเตรียมให้ธนาคารได้: public key, callback URL/TLS certificate details, IP209.15.122.115, ตัวอย่าง request/response ที่ปิดข้อมูลลับ และผลทดสอบ contract/duplicate/idempotency. การ allow firewall หรือ TCP443 connected เพียงอย่างเดียวยังไม่ยืนยันการทำงานของ HTTPS/JWT/callback/report ครบวงจร

