# สถานะการทดสอบจ่ายด้วยแอปธนาคารจริง

## สร้างภาพ QR กับเปิดรับชำระเป็นคนละขั้นตอน (20 ตุลาคม 09:20 น.)

**ตัวสร้าง Thai QR และภาพ PNG ทำได้แล้ว และการสร้างภาพไม่ต้องเรียก TTB หรือใช้ JWT** `ThaiQr.Build` ประกอบ payload/CRC ในระบบเรา ส่วน `QrService` เชื่อม QR กับ order ยอดเงินและเวลาหมดอายุ ปุ่มหน้า checkout ถูกปิดจาก `paymentsReady=false` ซึ่งเป็น gate ที่ระบบเราตั้งให้พร้อมยืนยันการรับเงินก่อนเปิดขาย ไม่ใช่ข้อผิดพลาดของไลบรารีสร้าง QR

| ขั้นตอน | ต้องใช้อะไร | สถานะของโครงการ |
|---|---|---|
| สร้างภาพ QR ทั่วไป | ข้อมูลผู้รับที่ถูกต้อง, payload และ CRC ตาม profile | ตัวสร้างและ renderer มีแล้ว; ไฟล์ใน `Logo TQRC` เป็นโลโก้ประกอบ ไม่ใช่ข้อมูลบัญชีหรือ Merchant ID |
| Dynamic QR ตามสเปก TTB หน้า 3–4 | Biller 15 หลัก, Ref1/Ref2, ยอดเงิน, Merchant ID 3 ตัวใน `QRId` รูป `ZXXXEYYMMDDHHMMZZZZZ` | มี Biller `075555900002101`; **ยังไม่มี Merchant ID ที่ TTB ออกให้** ห้ามอนุมานจากเลขท้าย Biller หรือ OAuth Client ID |
| เรียก Verify/Enquiry | JWT `iss`, private RSA key, public key ที่ TTB ลงทะเบียน และสิทธิ์บริการ | private RSA2048 ของ UAT เตรียมแล้ว; ยังไม่มี `iss` และผลลงทะเบียน public key/สิทธิ์ที่ยืนยันแล้ว JWT ไม่ใช่ข้อมูลในภาพ QR |
| รับและยืนยันผลจ่าย | callback URL ที่ธนาคารลงทะเบียน, authentication ที่ตกลงกัน, source IP จริง และการทดสอบร่วม | callback profile/authentication/actual-bank-source ยังไม่ผ่าน; public callback ยังปิด |
| เปิด checkout ของเรา | profile ที่อนุมัติ, ชื่อผู้รับที่ยืนยันแล้ว และช่องทางยืนยันผลพร้อม | gate ยังปิด ข้อมูลบริษัทบนใบเสร็จแบบทดสอบไม่แทนชื่อบัญชีรับเงินจริง |

ตรวจ config ที่ใช้งานจริงจาก `appsettings.Uat.json` ร่วมกับ base settings และ deployment แล้ว: Biller ตรงค่าที่ผู้ใช้ให้; Merchant ID, `iss`, beneficiary ยังว่าง; QR approval, callback approval และ actual-bank-source flags ยัง false; callback credentials ยังไม่มี การอ่านไฟล์ base เพียงไฟล์เดียวจะเห็น Local defaults ซึ่งไม่ใช่ environment ที่รันจริง

มาตรฐาน Thai QR สามารถใช้ทำ Bill Payment ทั่วไปได้ แต่การตัด Merchant ID/QRId ออกจาก QR ของ checkout นี้เท่ากับเปลี่ยน profile จากสเปก TTB ที่ระบบรับ callback/Verify ไว้ จึงไม่แก้ด้วยการเดาค่า `XXX`, ใช้เลขท้าย Biller หรือเอา UUID ของ MANGOs มาแทน ต้องยืนยันกับ TTB ด้วยว่า Biller นี้อยู่ UAT หรือ Production และ UAT รับสแกนด้วยแอปธนาคารจริงอย่างไร ภาพตัวอย่างการจ่ายสำเร็จเดิมไม่ได้พิสูจน์ว่า profile ของ server/API นี้เปิดครบแล้ว

SFTP/PGP, Void และรายงานเป็นขั้นตรวจรับเพิ่มเติม โดยรายงานเป็น gate สำหรับ Production ในโค้ดปัจจุบัน **ไม่ใช่สิ่งที่ใช้วาดภาพ QR หรือข้อกำหนดว่าต้องรอ Void/SFTP จึงสร้างรูป QR ได้**

OAuth Client ID และ Redirect URL ใน `MangosAuthConfig.asset` ใช้กับบัญชี MANGOs ไม่ใช่ Merchant ID หรือ JWT issuer ของ TTB การแก้ Auth ดังกล่าวจึงไม่ทำให้ `paymentsReady` ของธนาคารเปลี่ยนเอง

รายละเอียดเกณฑ์และหลักฐานเดิมด้านล่างยังใช้ได้ ไม่ได้เปิดรับเงินจริงในรอบแก้ SSO/ภาษา

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
2. **JWT issuer (`iss`) ของ Merchant UAT และช่องทางลงทะเบียน public key** ที่แนบสำหรับRS256/RSA2048; ค่า `sender_name` ขอเฉพาะกรณีที่ profile ของโครงการต้องส่งและธนาคารเป็นผู้กำหนดค่า. Public keyคือ `Provisioning/ttb-uat-jwt-5183ee1873474f4ab9aff6f1b530d588-public.pem`, SHA256ของDER SubjectPublicKeyInfo `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`. ไม่ส่งprivate key
3. **Credit Notification**: ยืนยันURL `https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification`, วิธีauth/การแลกcredentials, NAT sources และเวลาทดสอบconnectivity/callbackจริง. URLนี้ยังไม่เปิดรับจากภายนอกจนprofileพร้อมและตรวจsourceจริงแล้ว
4. **นัดทดสอบ1บาทร่วมกัน**: แจ้งขั้นตอน/ช่วงเวลาทดสอบ scan/import → จ่าย → callback/Verify และวิธียืนยันผลรับเงินของ UAT; โครงสร้าง QRId/expiry/CRC ใช้ตามสเปกที่ได้รับแล้ว ส่วน Merchant ID ใช้ค่าจริงจากข้อ1

เมื่อได้ข้อมูลครบ จะติดตั้งค่าผ่านprivate store, ตรวจcallbackจากTTBจริง แล้วจึงให้ผู้ซื้อสแกนQR1บาทที่ผูกกับorderจริงในUAT. ต้องตรวจผู้รับ/ยอดบนแอป, bank transaction+allocation, stock/entitlement, ใบเสร็จและประวัติครบก่อนนับว่าผ่าน. การสแกนอ่านpayloadได้อย่างเดียวไม่ยืนยันการรับชำระครบวงจร

SFTP/PGP/รายงาน/VoidและProductionยังมีขั้นตรวจรับแยกตาม `TTB_PROVISIONING_REQUEST_TH.md` และ `TTB_UAT_AND_RELEASE_ACCEPTANCE.md`. บริษัท/สินค้าทดสอบที่ผู้ใช้อนุญาตไม่ทดแทนbank provisioning
