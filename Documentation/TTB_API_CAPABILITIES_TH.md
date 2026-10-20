# บัญชีความสามารถและข้อจำกัดของ TTB Merchant QR API

วันที่ตรวจ: 18 ตุลาคม 2026 (Asia/Bangkok)

สถานะ: ผลตรวจเอกสารสำหรับวางแผน ยังไม่ได้เรียก API ธนาคารหรือรับรองการเปิดใช้ของ Merchant

## 1. ขอบเขตและหลักฐาน

ตรวจครบตามบริการที่มี contract ในเอกสารที่ได้รับ ไม่ได้อ้างว่าครอบคลุม API ทุกผลิตภัณฑ์ของ TTB

| รหัสแหล่งข้อมูล | แหล่งข้อมูล | น้ำหนักในการตัดสิน |
|---|---|---|
| S1 | [สเปกหลัก 35 หน้า](<C:/Users/PC/OneDrive/Desktop/Pop/Chula/Metaverse/TTBPayment/API/ttb-QRapi_Interface_Specification_(cross border)_V3.1.pdf>) | ชื่อไฟล์ V3.1 แต่ปกระบุ Version 3.01 วันที่ 30 July 2026; ใช้ตารางบริการและ revision history ประกอบกัน |
| S2 | [รูปแบบรายงาน Excel](<C:/Users/PC/OneDrive/Desktop/Pop/Chula/Metaverse/TTBPayment/API/เอกสารประกอบ 2.38/formatBPQR_ISO_V2.0.xlsx>) | ฐาน layout รุ่นเดิม; ไม่มี Cross-border fields ที่เพิ่มใน S1 หน้า 23–24 |
| S3 | [คำอธิบาย JWT](<C:/Users/PC/OneDrive/Desktop/Pop/Chula/Metaverse/TTBPayment/API/เอกสารประกอบ 2.38/online JWT.xlsx>) | อธิบาย key exchange และ JWT; มีตัวอย่างจากบริการอื่น จึงไม่ใช้แทน contract ของ QR endpoint |
| S4 | ภาพ firewall, ผล TCP connection และบทสนทนาที่ผู้ใช้ให้ | หลักฐานการประสานงานและการทดสอบบางส่วน ไม่ใช่ผลตรวจระบบครบกระบวนการ |

คำว่า “ระบุ” หมายถึงมีข้อมูลในแหล่งอ้างอิง “ข้อเสนอ” หมายถึงการออกแบบของเรา “รอยืนยัน” หมายถึงข้อมูลเฉพาะโครงการหรือข้อขัดแย้งที่ยังไม่มีหลักฐานพอ

## 2. บัญชีความสามารถทั้งหมด

| รหัส | ความสามารถ | ผู้เริ่มทำงาน | ต้องมีตาม S1 หน้า 2 | ผลต่อระบบที่จะพัฒนา |
|---|---|---|---|---|
| TTB-01 | Generate QR | Merchant สร้างในระบบตนเอง | ต้องมี | สร้าง Thai QR payload และภาพ QR เอง ไม่มี remote Create QR API ในชุดนี้ |
| TTB-02 | Credit Notification | TTB POST เข้าระบบ Merchant | ต้องมี | ทางหลักสำหรับแจ้งรับเงิน; ต้องรับซ้ำได้ |
| TTB-03 | Verify Slip | Merchant POST ไป TTB | ต้องมีสำหรับ Dynamic QR | ค้นหาธุรกรรมด้วย QRId เมื่อ notification ขาดหาย |
| TTB-04 | Enquiry QR by range | Merchant POST ไป TTB | เลือกใช้ | ค้นหาหลายธุรกรรมของ QRId ภายใน 24 ชั่วโมงย้อนหลัง; รองรับ Static/Dynamic |
| TTB-05 | Merchant Void | Merchant POST ไป TTB | เลือกใช้ | คืนเงินตามเงื่อนไข Void; ไม่รองรับ Cross-border ตามสเปก |
| TTB-06 | Enquiry Void | Merchant POST ไป TTB | เลือกใช้ | ตรวจว่ารายการถูก Void แล้วหรือไม่ |
| TTB-07 | QR Report | Merchant ดึงไฟล์ผ่าน SFTP | ต้องมี | รายงานรับเงิน/Void และรายการ unmatched ที่ธนาคารปรับปรุง |

Cross-border เป็นส่วนขยายของ notification, verify, enquiry และรายงาน ไม่ใช่ API สร้าง QR แยกอีกชุดหนึ่งในเอกสารนี้

## 3. เครือข่ายและ authentication ร่วม

### 3.1 API ขาออก

| Environment | HTTPS base URL | IP ตาม S1 |
|---|---|---|
| UAT | `https://uat-dgw.tau2904.com:9643` | `61.90.249.129` |
| Production | `https://dgw.ttbbank.com:9643` | `110.170.151.195` |

ทุก HTTP API ขาออกที่มี contract เป็น POST ใช้ TLS และ JWT; ส่ง `Content-Type: application/json; charset=UTF-8`, `Service-Name`, `Request-Datetime`, `Authorization: Bearer ...` ตามหน้าของบริการนั้น

JWT ใช้ RS256 กับ RSA 2048: เราสร้าง key pair เก็บ private key และให้ public key ธนาคารติดตั้ง ค่า `iss` ต้องตรง consumer ที่ TTB provision; `exp` เป็น epoch seconds ตาม profile ที่ตกลง ไม่มี OAuth token endpoint ระบุสำหรับ QR API ชุดนี้ และไม่มีการใช้ KKU client secret แทนกุญแจนี้

`Request-uid` เป็น UUID ตาม Appendix; กำหนดให้ adapter สร้างเพื่อ correlation. `sender_name` ปรากฏใน S3 แต่ไม่ครบใน S1 จึงเปิดเป็น config รอค่าที่ธนาคารให้ ไม่แต่งค่าขึ้นเอง. อายุตัว token สูงสุด การยอมรับ clock skew และขั้นตอนหมุน key ยังไม่มี contract ครบ

Header names ไม่แยกตัวพิมพ์ใหญ่เล็กตาม HTTP แต่ชื่อ JSON fields ต้องส่งตาม contract. ข้อมูลรหัสทุกชนิดเก็บเป็น string รักษาเลขศูนย์นำหน้า

### 3.2 Callback ขาเข้า

ผู้ใช้กำหนด UAT `ttb-api-test.mangosgo.com`, Production `ttb-api.mangosgo.com`, public server `209.15.122.115`. Path เต็มเป็นสิ่งที่ Merchant กำหนดและแจ้งธนาคาร ไม่ได้กำหนดตายตัวใน S1

ข้อเสนอ URL สำหรับลงทะเบียนภายหลัง:

- UAT: `https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification`
- Production: `https://ttb-api.mangosgo.com/ttb/v1/credit-notification`

S1 หน้า 5 ระบุ source IP ทั้งสอง environment: `203.148.142.200`, `203.148.143.101`, `.102`, `.103`, `.104`, `.105`. ภาพ firewall ไม่แสดง `.142.200` จึงต้องเทียบกฎที่อนุมัติจริง. IP `10.x` และ `172.x` ในแบบฟอร์มเป็นบริบทเครือข่ายภายใน ไม่ใช้เป็น public API host

วิธี authentication ของ notification ยังไม่ครบใน contract: ไม่พบข้อกำหนดทั่วไปที่ยืนยันว่าเป็น JWT, Basic Auth, mTLS หรือ body signature. Revision เรื่อง user/password สำหรับ SNC ไม่พอจะสรุปใช้กับ Merchant ทุกราย. ต้องยืนยันกับ TTB ก่อนเปิดรับเงินจริง และตรวจว่า ingress เห็น source IP ที่แท้จริงผ่าน proxy chain ที่เชื่อถือได้

รูป `curl telnet://...:443` พิสูจน์ TCP connectivity ณ เวลาทดสอบเท่านั้น ไม่ครอบคลุม TLS, SNI, path, schema หรือ callback authentication

## 4. TTB-01: สร้าง QR ภายใน Merchant

อ้างอิง S1 หน้า 3–4. ใช้ QR แบบ merchant-presented. ตารางนี้ระบุโครงสร้างข้อมูล ไม่ใช่การรับรองว่า Merchant ของเราเปิดใช้ทุกตัวเลือกแล้ว

| Tag | ค่า/ข้อกำหนด |
|---|---|
| 00 | Payload Format Indicator `01` |
| 01 | `11` Static หรือ `12` Dynamic |
| 30/00 | AID `A000000677010112` |
| 30/01 | Biller ID 15 หลักจาก TTB |
| 30/02 | Ref1 สูงสุด 20 ตัว `A–Z`, `0–9`; เป็น mandatory ในตาราง QR |
| 30/03 | Ref2 สูงสุด 20 ตัว `A–Z`, `0–9`; ตาราง QR ระบุ optional แต่ callback ระบุ mandatory จึงเสนอให้ระบบนี้ใส่เสมอ |
| 53 | THB `764` |
| 54 | จำนวนเงินตามรูปแบบข้อความ; ระบบซื้อสินค้านี้ใช้สองตำแหน่งทศนิยม ไม่ใช้ float |
| 58 | `TH` |
| 59 | ชื่อ Merchant สูงสุด 25 ตัวตามตาราง; แอป PromptPay อาจแสดงชื่อจากธนาคาร จึงใช้ field นี้ยืนยันผู้รับไม่ได้ |
| 62/07 | QRId 20 ตัว |
| 63 | CRC สี่อักขระฐานสิบหก; polynomial `1021`, initial `FFFF`, รวม `6304` และไม่รวมค่า CRC เอง |

หน้า 4 แสดง `ZXXXEYYMMDDHHMMZZZZZ`: Z สำหรับตรวจจ่ายซ้ำ, XXX คือ Merchant ID, E คือ expire flag, วันเวลาระดับนาทีและ running number 5 ตัว. หน้า 3 ยังใช้รูปแบบ D และหน้า 17 ยอมรับ D/Z สำหรับ Verify. ข้อเสนอคือ Dynamic แบบ Z พร้อม E แต่ต้องให้ TTB รับรอง test vector และ Merchant ID ก่อน bank UAT

การตรวจตัวอย่างแบบ offline ในการวิเคราะห์ครั้งก่อน: payload แยก TLV ได้และ CRC ตรง `63CF`; คำอธิบายความยาวบางบรรทัดผิด เช่น Ref1 ตัวอย่างมี 20 ตัว, `199.78` ยาว 6 ตัว, `ABC` ยาว 3 ตัว. ให้ builder คำนวณความยาวจากข้อมูลจริง

ข้อจำกัดและผลต่อระบบ:

- Static ที่ขึ้นต้น S ใช้ Verify Slip ไม่ได้; ใช้ Enquiry range ได้ภายใต้ข้อจำกัดของบริการ
- ธนาคารล้างข้อมูล QR ช่วงคืนวันที่ 15–16 และสิ้นเดือน; duplicate check ของธนาคารไม่ใช่ประวัติถาวรของร้านค้า
- ควรใช้ QR อายุสั้นและหมดอายุก่อนช่วงล้างข้อมูลที่ธนาคารกำหนด โดยเวลาแน่นอนของการล้างยังต้องยืนยัน
- QRId ต้องไม่ซ้ำในระบบเรา; จัดสรร running number แบบ atomic รวมทุก replica สำหรับ Merchant และวันเวลาหมดอายุเดียวกัน ถ้าเลขเต็มต้องเลือกเวลาที่เหมาะสมใหม่หรือปฏิเสธอย่างชัดเจน ห้ามวนกลับมาทับ
- ไม่มี API cancel/revoke QR ระบุไว้ การปิด UI หรือ cancel order จึงยังต้องรองรับเงินเข้าภายหลัง
- หลายสินค้า/หนึ่งผู้ขายใช้ Biller เดียวได้; รายการสินค้าอยู่ใน order snapshot. ไม่ใส่ address หรือข้อมูลส่วนตัวลง Ref1/Ref2
- Sub-biller มีหมายเหตุ prefix 4 ตัวใน Ref1 แต่ไม่มี endpoint และ contract สำหรับสร้าง/จัดการ Sub-biller ในฉบับที่ได้รับ

## 5. TTB-02: Credit Notification

อ้างอิง S1 หน้า 5–9. TTB เรียก URL ที่ Merchant ให้ด้วย `Service-Name: credit-notification`

| Field | M/O/C ตาม S1 | ความยาว/ความหมาย |
|---|---|---|
| InstructionId | M | 50; unique bank transaction identifier |
| BillerNo | M | 15 |
| Ref1, Ref2 | M | รายการละ 20 |
| QRId | M | 20 |
| PayerAccount | O | 20 |
| PayerName | O | 40 |
| PayerBank | O | 4; ตัวอย่างมีรูปแบบรหัสธนาคารต่างกัน เก็บ raw string |
| Amount | M | 20; parse จำนวนเงินอย่างเคร่งครัด |
| ResultCode | M | ความยาว 4; success ตัวอย่าง `000` |
| ResultDesc | M | 100 |
| TransDate | M | `YYYYMMDDHHMMSS`; ตารางอธิบาย system date/time ไม่รับรองเป็นเวลาชำระต้นทางทุกกรณี |
| CrossBorderFlag | O | 1; ปรากฏเป็น `Y` สำหรับ Cross-border |
| SwitchCode | C | 6 เมื่อ CrossBorderFlag = Y |
| CurrencyCode | C | 3 เมื่อ CrossBorderFlag = Y |
| SwitchFullName | C | 100 เมื่อ CrossBorderFlag = Y |

Merchant ตอบ HTTP 200 พร้อม Content-Type, Service-Name, Request-Datetime และ JSON: InstructionId จาก request, ResCode, ResDesc, TransDate ของ response. ไม่พบ contract HTTP error ทุกกรณี; authentication/transport failure ต้องตกลงเพิ่มเติม

| ResCode ที่ Merchant ตอบ | ความหมาย |
|---|---|
| 0000 | Successful |
| 9999 | Exception error |
| 0001 | Invalid Input |
| 0002 | Invalid QrID |
| 0003 | Invalid BillerNo |
| 0004 | Duplicate Instruction ID เมื่อ notification เดิมสำเร็จแล้ว |
| 0005 | Amount mismatch สำหรับ Dynamic |
| 0006 | Ref1 mismatch สำหรับ Dynamic |
| 0007 | Ref2 mismatch สำหรับ Dynamic |

ธนาคารระบุ retry เมื่อ timeout ห่างประมาณ 15–60 วินาที จำนวน 6 ครั้งด้วย InstructionId เดิม; ไม่รับรองว่าจะ retry business error ทุกชนิด. ต้อง durable commit ก่อนตอบ success และไม่รอสร้าง PDF หรือส่งของใน HTTP request นี้

ข้อเสนอในการประมวลผล: ตรวจ authentication, environment, Biller, QRId, refs, amount และผลชำระ; บันทึก evidence + bank transaction + payment allocation + outbox ใน DB transaction เดียว. Notification ซ้ำที่เคยตอบสำเร็จแล้วใช้ 0004 โดยไม่ส่งมอบซ้ำ. ถ้า Verify ยืนยันก่อน แต่เป็น callback แรกที่ถูกต้อง ยังรับ callback แรกเป็น 0000 ได้โดยเพิ่ม evidence อย่างเดียว. InstructionId เดิมแต่ข้อมูลสำคัญเปลี่ยนต้องเข้า investigation ไม่กลบด้วย duplicate ปกติ

การตอบ mismatch ไม่ได้เป็น API คืนเงิน และห้ามตีความว่าธนาคารไม่ได้รับเงินจริง

## 6. TTB-03: Verify Slip

อ้างอิง S1 หน้า 17–21

- POST `/qrapiservices/verify/verifyqr/{MerchantId}`
- Service-Name: `verify-qr`
- Request: `BillerNo` 15, `QRId` 20, `TransDate` 14 (`YYYYMMDDHHMMSS`)
- ค้นหาด้วย QRId ของ Dynamic D/Z เมื่อ notification ขาดหาย ไม่รับรูปภาพสลิป
- แนะนำรอประมาณ 30–60 วินาที, ควรเป็น manual callback, ทำซ้ำไม่ควรเกิน 3 ครั้ง และห้ามยิงเป็น interval ตลอดเวลา

Response มี ResCode/ResDesc ระดับคำขอ และเมื่อพบรายการมี InstructionId, BillerNo, refs, QRId, payer fields, Amount, ResultCode/ResultDesc, TransDate และ Cross-border fields ตามเงื่อนไข. Ref2 ระบุ optional ใน response; ตรวจ field ที่มีและใช้ stored attempt ตรวจจับความไม่สอดคล้อง. ไม่พบรายการอาจไม่มี fields ที่ตารางรวมระบุ M จึงแยก schema ตามผลลัพธ์

| ResCode | ความหมายตามตารางล่าสุด |
|---|---|
| 0000 | Successful; ยังต้องตรวจผลธุรกรรมและข้อมูลจับคู่ออเดอร์ |
| 9999 | Exception error |
| 0002 | QRId not found |
| 0003 | Invalid parameter / QRId และ Biller ไม่ตรง |
| 9001 | QR has already been void |

ตัวอย่าง success ใช้ ResultCode `0000`. ตัวอย่าง no-credit หน้า 20 ใช้ ResCode `0001` แต่ revision 2.36 ระบุเอาออกแล้ว. จัด 0001 ที่พบเป็น legacy unconfirmed outcome, เก็บ evidence และไม่ mark paid. ตารางและ revision มีน้ำหนักมากกว่าตัวอย่างเก่า

ไม่มี endpoint ในชุดนี้สำหรับค้นหาด้วยเลขออเดอร์ร้านค้าโดยไม่รู้ QRId หรือใช้เป็น statement API ย้อนหลังไม่จำกัด

## 7. TTB-04: Enquiry QR by range

อ้างอิง S1 หน้า 25–29

- POST `/qrapiservices/enquiry/enquiryqr-range/{MerchantId}`
- Service-Name: `enquiry-qr-range`
- Request: `BillerNo`, `QRId`, `StartFindDateTime`, `EndFindDateTime`, `TransDate`
- วันเวลาใน body เป็น `YYYYMMDDHHMMSS`; Start ไม่ย้อนหลังเกิน 24 ชั่วโมง และ End ไม่เกินเวลาปัจจุบัน
- ใช้ได้ทั้ง Dynamic และ Static; เป็นการค้นหาของ QRId ที่ระบุ ไม่ใช่ค้นธุรกรรมทั้งหมดของ Merchant
- ข้อแนะนำรอ 30–60 วินาที, manual callback, ไม่ควรเกิน 3 ครั้ง และห้าม constant interval เช่นเดียวกับ Verify

Response มี ResCode, ResDesc, BillerNo, QRId, ช่วงเวลา, TransDate และ `QTransaction` เป็น array เมื่อพบข้อมูล. สมาชิกประกอบด้วย InstructionId, BillerNo, refs, QRId, payer fields, Amount, ResultCode/ResultDesc, TransDate และ Cross-border fields. สมาชิกแต่ละรายการต้องประมวลผลแยกตาม InstructionId

| ResCode | ความหมาย |
|---|---|
| 0000 | Successful |
| 0001 | No data found |
| 9999 | Exception error |

ตัวอย่าง ResultCode ของสมาชิกใช้ `000`. InstructionId ที่ขึ้นต้น Q มีหมายเหตุปิด Void. ตัวอย่าง array ใช้ InstructionId ซ้ำและ QRId บางตำแหน่งไม่ตรงกัน จึงไม่คัดลอกเป็น fixture ที่ถือว่าทุกข้อมูลถูกต้อง และต้องตรวจจริงก่อน allocate เงิน

## 8. TTB-05: Merchant Void

อ้างอิง S1 หน้า 10–13 และ 35

- POST `/qrapiservices/void/merchantvoid/{MerchantId}`
- Service-Name: `merchant-void`
- Request: `TransactionId`, `InstructionId`, `QRId`, `BillerNo`, `TransDate`
- TransactionId request ยาว 17 รูปแบบ `XXXYYYYMMDDHHMMSS`; ตาราง response ระบุ 15 แต่ตัวอย่าง echo 17 จึงไม่ตัดค่า
- ไม่มี Amount ใน request และไม่มี contract partial refund ระบุไว้

Response echo transaction identifiers พร้อม ResCode, ResDesc, TransDate

| ResCode | ความหมาย |
|---|---|
| 0000 | Void successful |
| 9999 | Exception error |
| 0001 | Duplicate TransactionId |
| 0002 | InstructionId ถูก Void แล้ว |
| 0003 | ไม่พบ InstructionId |
| 0004 | QRId/MerchantId ไม่ตรงกับ InstructionId |
| 0005 | TTB internal error ให้ลองอีกครั้งตามข้อความ |
| 0008 | Merchant มีเงินไม่เพียงพอ |
| 0009 | Cross-border transaction ไม่อนุญาต Void |

เงื่อนไข: ยึดรอบ cutoff 22:56 ตามแผนภาพและตัวอย่าง แต่ข้อความ same day และ boundary ต้องยืนยัน. รายการหลัง cutoff มีตัวอย่าง Void ได้ถึง cutoff วันถัดไป. InstructionId ขึ้นต้น Q ไม่เปิด Void. ต้องออกหลักฐาน Void ให้ผู้ซื้อเมื่อยืนยันสำเร็จ

ข้อเสนอ: บันทึกคำขอคืนเงินและ identifiers ก่อนเรียกธนาคาร; lock ธุรกรรมและป้องกันคำขอคู่ขนาน. รูปแบบ TransactionId มีความละเอียดหนึ่งวินาที จึงต้องจัดสรรคิวกลางและรอเวลาจริงถัดไปเมื่อชน ห้ามเติม suffix หรือใช้เวลาอนาคตโดยไม่มีสเปกรองรับ. หาก timeout ให้สถานะ Unknown แล้วใช้ Enquiry Void ไม่ยิงคำขอใหม่ด้วย id ใหม่ทันที. ไม่สามารถสมมติกติกา reuse TransactionId ของ Enquiry/Retry จากเอกสารนี้ได้

## 9. TTB-06: Enquiry Void

อ้างอิง S1 หน้า 14–16

- POST `/qrapiservices/void/enquiryvoid/{MerchantId}`
- Service-Name: `enquiry-void`
- Request: `TransactionId`, `InstructionId`, `QRId`, `BillerNo`, `TransDate` ตามรูปแบบเดียวกับ Void
- Response echo identifiers และ ResCode/ResDesc/TransDate

| ResCode | ความหมาย |
|---|---|
| 0000 | Void successfully / InstructionId ถูก Void แล้ว |
| 9999 | Exception error |
| 0003 | ไม่พบ InstructionId |
| 0004 | Key ไม่ตรง |

0000 ในบริการนี้หมายถึงผลยกเลิกแล้ว ไม่ใช่รับชำระสำเร็จ. ผลไม่พบ/error ยังไม่ใช่หลักฐานพอให้ส่ง Void ซ้ำแบบไร้เงื่อนไข. เอกสารกล่าวถึง void notification ในคำอธิบาย แต่ไม่มี contract callback Void แยก จึงออกแบบจาก response, Enquiry และ report ที่มี contract เท่านั้น

## 10. TTB-07: QR Report ผ่าน SFTP

อ้างอิง S1 หน้า 22–24 และ 35; เทียบ S2 ทั้งสอง sheet

Host ใช้ UAT/Production เช่นเดียวกับ gateway แต่ SFTP ports ระบุ 4022, 4028. Directory `/QRTRAN/report/`. User/password ให้ภายหลัง. File encryption ระบุ PGP with AES256 และ revision ระบุ 4028 with encryption ไม่ใช่ optional จึงห้ามสมมติว่าเลือกข้าม encryption ได้ ต้องตกลง workflow, filename/extension, พอร์ต และ key exchange จริง

| ไฟล์ | เนื้อหา | เวลาตามเอกสาร |
|---|---|---|
| BPQR_ISO_YYMMDD.TXT | Credit และ Debit/Void ในรอบบัญชี | D0 23:45 |
| BPQRLOST_YYMMDD.TXT | Unmatched และ adjustments ของ TTB operation | D+1 tentative 13:00–19:00 |

Record layout ยาว 500 ตำแหน่ง ประกอบด้วย H, D, T. Header: type 1, sequence 2–7, Merchant ID 8–10, report date 11–18 เป็น DDMMYYYY, spare 19–500

Detail ส่วนร่วม:

| ตำแหน่ง | Field |
|---|---|
| 1; 2–7 | Record type; sequence |
| 8–42; 43–77 | InstructionId; OriginalInstructionId เฉพาะ Void |
| 78–80; 81–100 | Payer bank; payer account |
| 101–140; 141–180 | Payer name; payer name TH (UTF-8) |
| 181–200 | Merchant account |
| 201–208; 209–214 | วัน/เวลา Payment สำหรับ ISO หรือ Payer debit สำหรับ LOST |
| 215–234; 235–254; 255–274 | Ref1; Ref2; QRId |
| 275–278; 279; 280 | Teller; C/D/V; Lumpsum Y/N |
| 281–295; 296–299; 300–319 | Amount; fee; Biller ID |

Amount/fee รวมสองตำแหน่งทศนิยมโดยไม่ใส่จุด เช่น 11175 = 111.75 บาท. C คือ credit, D คือ debit/void, V คือรายการเดิมที่ void แล้ว. กติกา netting และ totals ของ C/D/V ต้องยืนยันจากไฟล์จริง ไม่เดาว่า D และ V เป็นการคืนเงินสองครั้ง

| Field | ISO | LOST |
|---|---|---|
| Credit date / time ของ operation | ไม่มีส่วนนี้ | 320–327 / 328–333 |
| Cross-border flag | 320 | 334 |
| Switch code | 321–326 | 335–340 |
| Currency code | 327–329 | 341–343 |
| Switch full name | 330–429 | 344–443 |
| Spare | 430–500 | 444–500 |

Total: type 1, sequence 2–7, debit amount 8–22, debit count 23–28, credit amount 29–43, credit count 44–49, spare 50–500

ต้องขอไฟล์จริงเพื่อตัดสิน byte/character slicing สำหรับภาษาไทย, line endings, padding, วันที่ไฟล์ตรงรอบบัญชี, เขตเวลา, กติกา reissue และอายุไฟล์บน SFTP. อย่า truncate API InstructionId 50 ตัวให้เหลือ report field 35 เพื่อจับคู่แบบเดา

## 11. ขอบเขต Cross-border

Notification, Verify, Enquiry และรายงานมี CrossBorderFlag/SwitchCode/CurrencyCode/SwitchFullName. QR หลักยังเป็น THB 764. ตัวอย่าง CNY ไม่ใช่ FX quote หรือหลักฐานเปิดทุกเครือข่ายให้ Biller เรา

รองรับการเก็บ metadata ได้ตั้งแต่ต้น แต่ก่อนเปิด Cross-border จริงต้องยืนยันความหมาย Amount, currency, fee, ผู้ให้บริการที่เปิดใช้ และขั้นตอนคืนเงินที่ไม่ใช่ Merchant Void. Payer account อาจเป็น 0000000000 และ payer data เป็น optional จึงไม่ใช้เป็นตัวระบุผู้ใช้ MANGOs

การปิด feature flag ใน Merchant app ไม่ใช่ API ปิดการรับ Cross-border ที่ธนาคาร เพราะไม่มี contract ดังกล่าว. ธุรกรรมที่มาถึงโดยไม่คาดหมายยังต้องบันทึกและตรวจสอบ ไม่ทิ้งเพียงเพราะหน้าเว็บไม่ได้แสดงโลโก้ Cross-border

## 12. สิ่งที่ไม่มี contract ในชุดเอกสารนี้

| ความต้องการ | ผลตรวจ |
|---|---|
| สินค้า ราคา ตะกร้า สต็อก order history | ต้องทำในระบบเรา |
| ออกใบเสร็จสินค้าหรือใบกำกับภาษี | ต้องทำ/เชื่อมระบบเอกสารของบริษัทเอง |
| ให้ไอเทม สิทธิ์ดิจิทัล ดาวน์โหลด หรือจัดส่ง | ต้องทำในระบบเรา |
| Create QR จากธนาคาร / Cancel QR endpoint | ไม่พบ; QR สร้างใน Merchant และหมดอายุด้วยข้อมูลใน QR |
| อัปโหลดภาพสลิปเพื่อตรวจ OCR/ความแท้ | ไม่พบ; Verify ใช้ QRId |
| Partial refund / refund หลังพ้นรอบ / Cross-border refund API | ไม่พบในฉบับนี้ |
| Split payment / payout ผู้ขาย / escrow / wallet / ถอนเงิน | ไม่พบ |
| Subscription / ตัดเงินอัตโนมัติ / direct debit | ไม่พบ; ตัวอย่าง direct-debit ใน S3 เป็นบริบทอื่น |
| อ่าน statement หรือธุรกรรม Merchant ทุกตัวตามอำเภอใจ | ไม่พบ; range enquiry ต้องระบุ QRId และมีหน้าต่าง 24 ชั่วโมง |
| เปิดบัญชี เพิ่ม Biller หรือจัดการ Sub-biller ผ่าน API | มีเพียง historical mention/prefix; ไม่มี endpoint contract ให้พัฒนา |
| FX rate API / เลือกสกุลเงินต่างประเทศสำหรับตะกร้า | ไม่พบ |

คำว่าไม่พบหมายถึงเอกสารชุดนี้ไม่ให้ contract ไม่ใช่ข้อสรุปว่า TTB ไม่มีบริการนั้นในผลิตภัณฑ์อื่น

## 13. ประเด็นค้างและเกณฑ์ตัดสิน

| รหัส | ประเด็น | สิ่งที่ทำได้แล้ว | หลักฐานก่อนเปิดใช้งานจริง |
|---|---|---|---|
| Q01 | Merchant/Biller/account/iss เฉพาะ environment | ออกแบบ typed config แยก UAT/Prod | provisioning sheet/คำยืนยันจาก TTB |
| Q02 | Callback authentication และ source IP | กำหนด ingress/path และ schema | วิธี auth ที่ตกลงและ callback UAT ผ่านจริง |
| Q03 | D/Z และ expiry/ช่วง purge | builder แบบเลือก profile; default proposal Z+E | test QR ที่ธนาคารตรวจและ UAT ช่วง boundary |
| Q04 | ผล Verify 0001 เก่า / ResultCode แยกบริการ | mapping แบบ conservative และ unknown outcome | response fixtures จริง |
| Q05 | sender_name, TTL, key rotation | config รองรับ | accepted header/JWT profile ของ Merchant |
| Q06 | Report encoding/layout/ids/C-D-V | parser ตาม PDF Cross-border แยก version | ไฟล์จริงที่มีภาษาไทย Credit/Void/LOST/Cross-border |
| Q07 | SFTP ports/PGP/workflow | worker และ archive design | credentials, SSH fingerprint, PGP exchange, encrypted sample |
| Q08 | Void cutoff/id reuse/timeout | state machine และ single-flight design | bank UAT และ boundary results |
| Q09 | Fees/limits/cross-border Amount | เก็บยอด/fee/metadata แยกกัน | ข้อตกลง Merchant และผล settlement |
| Q10 | Certificate recommendation 365 วัน | renewal design และ monitoring | TTB ยอมรับ lifecycle ของ public CA ปัจจุบัน |

S1 แนะนำ certificate อย่างน้อย 365 วัน แต่ [CA/Browser Forum](https://cabforum.org/working-groups/server/baseline-requirements/requirements/) §6.3.2 กำหนด public TLS certificates ที่ออกตั้งแต่ 15 March 2026 ถึงก่อน 15 March 2027 สูงสุด 200 วัน. ต้องแยกกติกานี้จาก JWT key/PGP key/client certificate และตกลงวิธีต่ออายุที่ไม่ทำให้ callback หยุด

## 14. การนำไปวางแผน

ใช้ครบทั้งเจ็ดหน้าที่ในแผน แต่ Static checkout ไม่ใช่ flow หลักของเมตาเวิร์สนี้. Void และ Cross-border เปิดตาม feature flags หลังผ่านเงื่อนไขที่เกี่ยวข้อง. งานพัฒนา mock และ domain สามารถกำหนดได้ก่อนมี credentials; การทดสอบธนาคารและ Production ต้องผ่าน Q ที่เกี่ยวข้อง

รายละเอียดระบบ ข้อมูล API ภายใน แผนทดสอบ และลำดับส่งมอบอยู่ใน [แผนพัฒนาระบบทั้งหมด](<C:/CosmoPC/Work/Unity/MANGOxTTB/Documentation/TTB_SYSTEM_DEVELOPMENT_PLAN_TH.md>)
