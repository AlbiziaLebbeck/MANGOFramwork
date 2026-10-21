# ทดสอบ Verify Slip จาก ntserver02

บน ntserver02 รันคำสั่งนี้ได้เลย (SSH ด้วยบัญชีดูแลเดิม):

```bash
bash /opt/ttb-commerce/uat/tools/verify-slip.sh
```

หรือจากเครื่องที่ตั้ง SSH alias ไว้:

```powershell
ssh ntserver02 "bash /opt/ttb-commerce/uat/tools/verify-slip.sh"
```

คำสั่งส่ง Verify **หนึ่งครั้ง** จากบริการ UAT บน ntserver02 โดยใช้ issuer/private key ที่ติดตั้งไว้แล้ว แสดงเฉพาะผลที่ส่งให้ TTB ตรวจได้ ไม่แสดง JWT, private key หรือรหัสผ่าน ไม่ต้องใส่ข้อมูลเหล่านี้เอง

ภายในเรียก:

```bash
kubectl -n ttb-uat exec deployment/ttb-commerce-api -- /app/TtbCommerce.Api --uat-bank-probe
```

wrapper ตัดภาพ QR/base64 และ log ออกจากผล จึงแนะนำให้ใช้ wrapper ข้างต้น หาก `httpStatus=401` ส่ง `issuedAt`, `endpoint`, `merchantId`, `issuer` และ `probe.requestUid` ให้ TTB ตรวจ registration/public key และเงื่อนไข gateway ได้เลย ไม่ต้องแนบ token หรือ private key

ผลล่าสุดหลัง TTB ลงทะเบียน public key: 21 ตุลาคม 2026 เวลา **10:05:45 น. ประเทศไทย** (Verify หนึ่งครั้ง ไม่มี retry):

```json
{
  "merchantId": "PYA",
  "issuer": "jwt-key-pya",
  "httpStatus": 200,
  "requestUid": "4f4bc3c5-c49d-4ea0-97c7-d721031ff2c3",
  "resCode": "0002",
  "outcome": "NotFound",
  "authenticatedBankReply": true,
  "paymentConfirmed": false
}
```

ธนาคารรับ authentication และตอบ Verify ตาม contract แล้ว. NotFound เป็นผลของ diagnostic QR ที่ยังไม่มีการจ่าย ไม่ใช่การยืนยัน Paid. ระบบเปิดสร้างคำสั่งซื้อ/QR สำหรับทดสอบ UAT ด้วย flag เฉพาะ UAT และชื่อแสดงผลชั่วคราว `inam cafe` ตามผู้ใช้ระบุ โดยยังเก็บ callback acceptance เป็นfalse. Production ยังคงต้องผ่านทุกเงื่อนไข. รายละเอียดและหลักฐาน deploy อยู่ที่ [TTB_UAT_GATEWAY_ACCEPTANCE_20261021_TH.md](TTB_UAT_GATEWAY_ACCEPTANCE_20261021_TH.md).

## ประวัติก่อนลงทะเบียน public key

20 ตุลาคม 2026 เวลา **20:48:23 น. ประเทศไทย** ได้ผลดังนี้:

```json
{
  "merchantId": "PYA",
  "issuer": "jwt-key-pya",
  "httpStatus": 401,
  "requestUid": "d452ae10-af8e-4904-a54f-09d772d93415",
  "authenticatedBankReply": false,
  "paymentConfirmed": false
}
```

ผลนี้ยังไม่ยืนยันว่า authentication กับธนาคารผ่าน และไม่สามารถสรุปจาก401เพียงอย่างเดียวว่าเกิดจาก private key, issuer หรือเงื่อนไข gateway ข้อใด. ผลก่อนหน้าเวลา14:22:54น.ได้401เช่นกัน (Request UID `41982f65-8fcd-4b8e-8f27-ea82f25080be`). ไม่มีการสร้างคำสั่งซื้อหรือรายการการเงินจากการทดสอบนี้.

ขณะทดสอบวันที่20ตุลาคม backend ระบุ checkout requirements ที่ยังขาด3รายการ:

- `Ttb.GatewayAuthenticationValidation`
- `Ttb.CallbackSourceIpValidation`
- `Ttb.BeneficiaryName`

ตอนนั้นจึงส่ง `paymentsReady=false` และหน้าcheckoutปิดปุ่มสร้างQRเมื่อคำนวณยอดสำเร็จแล้ว. นี่คือ readiness gate ในโค้ด ไม่ใช่ผลล้มเหลวจากการเรียก Create QR API: QR ใช้ EMV/TLV และสร้างภาพภายในbackend. Operator diagnostic ไม่ใช่ QR ของคำสั่งซื้อ. ห้ามอ้างหลักฐาน Verify ผ่านแทน callback/การชำระเงินจริง.

Exit code `2` หมายถึงยังไม่ผ่านการยืนยันฝั่งธนาคาร เช่น HTTP401 ไม่ได้หมายถึง SSH ล้มเหลว หากแก้ configuration กับธนาคารแล้วจึงรันใหม่ด้วยตนเอง ไม่มี retry/polling อัตโนมัติ

การทดสอบสร้าง diagnostic QR ID ใหม่และจองลำดับ QR เพื่อไม่ชนกัน แต่ไม่สร้างออเดอร์/รับรอง Paid/ออกใบเสร็จ/ส่งสินค้า ไม่ใช่การสแกนจ่าย และไม่เรียก Void. แม้ Verify ได้คำตอบว่าไม่พบรายการอย่างถูกต้อง ก็ยังไม่ใช่หลักฐานว่าจ่ายเงินแล้ว

สร้างภาพ QR สดให้ TTB สแกน: `./Infrastructure/TTB/uat/bank-diagnostics.ps1 -Mode Qr` จากรากโปรเจกต์ ภาพมีอายุประมาณ 15 นาที ต้องให้เจ้าหน้าที่ TTB ใช้วงทดสอบธนาคารสแกน แอปธนาคารทั่วไปใช้จ่าย UAT ไม่ได้

Source ของ wrapper: `Infrastructure/TTB/uat/verify-slip.sh`. การลงทะเบียน public key กับ `jwt-key-pya` ตรวจผ่านแล้ววันที่21ตุลาคม; ยังต้องส่ง Basic credentials ให้ TTB และทดสอบ callback จริง รวมทั้งยืนยันชื่อบัญชีก่อนตรวจรับครบวงจร.
