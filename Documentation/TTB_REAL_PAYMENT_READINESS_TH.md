# สถานะการทดสอบจ่ายด้วยแอปธนาคารจริง

20 ตุลาคม 2026: **ยังจ่ายจริงผ่านระบบนี้ไม่ได้**. ระบบ UAT จงใจปฏิเสธการสร้าง order/QR เมื่อ bank profile ไม่ครบ จึงไม่แจก QR ที่ใช้ Merchant ID เดาหรืออ้างว่าเงินเข้าจากผลจำลอง

หน้า [UAT preview](https://ttb-api-test.mangosgo.com/store-uat/) เปิดเฉพาะ IP58.11.26.155/32 ตามคำอนุญาตแล้ว และผ่าน actual HTTPS/WSS/3D desktop+mobile viewport. คำอนุญาตนี้ไม่เปิด payment/callback และไม่ทดแทน bank provisioning ด้านล่าง

| สิ่งที่ตรวจจริงแล้ว | ผลและหลักฐาน |
|---|---|
| รูป QR จาก renderer ของ Backend release ที่ผ่าน268tests | ถอดอ่าน PNGต้นฉบับ5รูปและภาพ Chromium15รูป รวมราคา1/10/99/100/199.78บาท จอ600/390/320px ตรงpayloadทุกอักขระ; ตรวจTLV/THB/Biller/refs/amountและCRCด้วยdecoderอิสระ20/20. ใช้ Biller/QRIdตัวอย่างในสเปกที่หมดอายุปี2023 ไม่ใช้Billerจริงของผู้ใช้; ไม่ใช่bank-app acceptance |
| เครือข่ายจาก API pod บน ntserver02 → TTB UAT | TLS1.2 ที่ `uat-dgw.tau2904.com:9643` ผ่าน CA+hostname; certificateหมดอายุ3Apr2027. เชื่อมTLSเท่านั้น ไม่มีHTTP/JWT/payment request |
| กุญแจ JWT ของ UAT | สร้างRSA2048ใหม่, offline signatureผ่าน, public keyตรงกับSecretและไฟล์ที่API mount; สำเนาprivateบนPCเข้ารหัสDPAPIและทดสอบเปิดคืนในหน่วยความจำผ่าน. **ยังไม่ได้ลงทะเบียนกับTTB** |
| ข้อมูลที่ธนาคารออกให้ | Biller `075555900002101` ตรงค่าที่ผู้ใช้ยืนยัน; Merchant ID/issuer/ชื่อผู้รับที่ยืนยันแล้วและQR/callback approvalsยังไม่มี |

หลักฐาน: `.artifacts/qr-real-readiness/render-20261020-1/decode-result.json`, `uat-bank-gates-20261020.json` และ `.tools/bank-signing/Uat/5183ee1873474f4ab9aff6f1b530d588/state.json`. Private keyไม่อยู่ในเอกสารหรือWebGL

## ข้อความที่ส่งให้ผู้ประสาน TTB ได้

ขอเปิดทดสอบ QR สำหรับผู้ขายรายเดียวบน server `209.15.122.115`, Billerที่เคยทดสอบ `075555900002101`, UAT host `ttb-api-test.mangosgo.com` โดยขอข้อมูล/ยืนยัน4กลุ่มดังนี้:

1. **Merchant ID3ตัว, Billerและชื่อผู้รับเงิน** ที่ถูกต้องของUAT. ขอคำยืนยันว่า Billerนี้ทดสอบด้วยแอปธนาคารปกติได้หรือจำเป็นต้องใช้แอป/บัญชีทดสอบ และเงินจริงจะเข้าบัญชีใด ไม่ถือว่าUATแยกเงินจริงโดยอัตโนมัติ
2. **JWT issuer (`iss`) และลงทะเบียน public key** ที่แนบสำหรับRS256/RSA2048; ขอtoken lifetime/headersที่ต้องใช้. Public keyคือ `Provisioning/ttb-uat-jwt-5183ee1873474f4ab9aff6f1b530d588-public.pem`, SHA256ของDER SubjectPublicKeyInfo `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`. ไม่ส่งprivate key
3. **Credit Notification**: ยืนยันURL `https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification`, วิธีauth/การแลกcredentials, NAT sources และเวลาทดสอบconnectivity/callbackจริง. URLนี้ยังไม่เปิดรับจากภายนอกจนprofileพร้อมและตรวจsourceจริงแล้ว
4. **QR profile/การทดสอบ1บาท**: ยืนยันQRId `Z{MerchantId}E{YYMMDDHHmm}{running5}` และexpiryตามBangkok, test vectorและขั้นตอนทดสอบ scan/import → จ่าย → callback/Verify. ไม่ใช้ค่าXXXจากตัวอย่างเป็นMerchantจริง

เมื่อได้ข้อมูลครบ จะติดตั้งค่าผ่านprivate store, ตรวจcallbackจากTTBจริง แล้วจึงให้ผู้ซื้อสแกนQR1บาทที่ผูกกับorderจริงในUAT. ต้องตรวจผู้รับ/ยอดบนแอป, bank transaction+allocation, stock/entitlement, ใบเสร็จและประวัติครบก่อนนับว่าผ่าน. การสแกนอ่านpayloadได้อย่างเดียวไม่ยืนยันการรับชำระครบวงจร

SFTP/PGP/รายงาน/VoidและProductionยังมีขั้นตรวจรับแยกตาม `TTB_PROVISIONING_REQUEST_TH.md` และ `TTB_UAT_AND_RELEASE_ACCEPTANCE.md`. บริษัท/สินค้าทดสอบที่ผู้ใช้อนุญาตไม่ทดแทนbank provisioning
