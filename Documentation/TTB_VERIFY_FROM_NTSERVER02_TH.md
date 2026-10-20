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

ผลที่ทดสอบจริงล่าสุด 20 ตุลาคม 2026 เวลา **14:22:54 น. ประเทศไทย**:

```json
{
  "merchantId": "PYA",
  "issuer": "jwt-key-pya",
  "httpStatus": 401,
  "requestUid": "41982f65-8fcd-4b8e-8f27-ea82f25080be",
  "authenticatedBankReply": false,
  "paymentConfirmed": false
}
```

Exit code `2` หมายถึงยังไม่ผ่านการยืนยันฝั่งธนาคาร เช่น HTTP401 ไม่ได้หมายถึง SSH ล้มเหลว หากแก้ configuration กับธนาคารแล้วจึงรันใหม่ด้วยตนเอง ไม่มี retry/polling อัตโนมัติ

การทดสอบสร้าง diagnostic QR ID ใหม่และจองลำดับ QR เพื่อไม่ชนกัน แต่ไม่สร้างออเดอร์/รับรอง Paid/ออกใบเสร็จ/ส่งสินค้า ไม่ใช่การสแกนจ่าย และไม่เรียก Void. แม้ Verify ได้คำตอบว่าไม่พบรายการอย่างถูกต้อง ก็ยังไม่ใช่หลักฐานว่าจ่ายเงินแล้ว

สร้างภาพ QR สดให้ TTB สแกน: `./Infrastructure/TTB/uat/bank-diagnostics.ps1 -Mode Qr` จากรากโปรเจกต์ ภาพมีอายุประมาณ 15 นาที ต้องให้เจ้าหน้าที่ TTB ใช้วงทดสอบธนาคารสแกน แอปธนาคารทั่วไปใช้จ่าย UAT ไม่ได้

Source ของ wrapper: `Infrastructure/TTB/uat/verify-slip.sh`. การลงทะเบียน public key กับ `jwt-key-pya`, callback Basic authentication และชื่อผู้รับเงินจริงยังต้องยืนยันกับ TTB ก่อนเปิด checkout ครบวงจร
