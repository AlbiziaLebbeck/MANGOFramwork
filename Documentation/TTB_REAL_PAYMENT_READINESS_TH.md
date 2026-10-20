# สถานะ TTB หลังคำยืนยันของธนาคาร — 20 ตุลาคม 2026

| รายการ | ค่าหรือเงื่อนไขที่ยืนยันแล้ว |
|---|---|
| Merchant ID | `PYA` |
| Biller | `075555900002101` เลขเดียวกันทั้ง UAT/Production; ลงทะเบียนวงทดสอบแล้ว |
| JWT issuer | `jwt-key-pya` |
| JWT | RS256/RSA2048, header ใช้ `type: JWT` ตามตัวอย่างล่าสุด; exp เป็น epoch seconds; ระบบใช้ token120วินาที |
| สแกน UAT | ให้คุณ bird/TTB สแกนเฉพาะวงทดสอบธนาคาร **แอปธนาคารทั่วไปใช้จ่าย UAT ไม่ได้** |
| Credit Notification | TTB รับ URL ที่เสนอแล้ว; merchant กำหนด authentication เอง, HTTPS/TLS1.2 |
| API | ธนาคารแจ้งเปิด Verify Slip, Enquiry QR, Merchant Void, Enquiry Void แล้ว |
| รายงาน | SFTP/PGP รอประสานแยก ไม่ใช่สิ่งที่ต้องรอเพื่อเริ่ม Verify/QR UAT |

อ้างอิงคำตอบธนาคารที่ผู้ใช้นำมาให้20Oct2026, PDFฉบับ3.01 หน้า4/18–20/30, `online JWT.xlsx`, `formatBPQR_ISO_V2.0.xlsx`. เวอร์ชันภายใน PDF คือ3.01 แม้ชื่อไฟล์ลงท้ายV3.1. ตัวอย่าง issuer `jwt-key-1` และ sample keys ใน Excel ไม่ใช่ค่าของบริษัท

## ระบบทำอะไรแล้ว

- UAT ใช้ Merchant/Biller/issuer ที่ยืนยันแล้ว; production example อัปเดตข้อมูล แต่ยังไม่เปิด Production
- QR สร้างในหลังบ้านด้วย EMV/TLV, Tag30 bill payment, THB764, Ref1/2, Tag62/07 `ZPYAEYYMMDDHHMMNNNNN`, expiry Bangkok และ CRC **การสร้างภาพไม่ต้องเรียก Create QR API หรือใช้ JWT**
- JWT ปรับชื่อฟิลด์ `typ` เป็น `type` ให้ตรงคำตอบล่าสุด ใช้ RSA key เดิมของบริษัท ไม่ใช้ sample key
- Basic callback เตรียมใน UAT Secret; สำเนา PC เข้ารหัส DPAPI ไม่อยู่ Git/WebGL
- callback ตรวจ IPจริงและ Basic เสมอ รับ callbackแรกเพื่อเก็บหลักฐานได้ แต่ **checkout ยังต้องรอ actual-bank-source acceptance** ไม่ตั้งธงว่าผ่านล่วงหน้า
- หน้าร้านไทย/อังกฤษระบุว่า UAT ต้องให้ TTB สแกนในวงธนาคาร

## สร้าง QR ให้ธนาคารทดสอบ

```powershell
./Infrastructure/TTB/uat/bank-diagnostics.ps1 -Mode Qr
# ส่ง Verify เพียงหนึ่งครั้ง ไม่มี retry/polling:
./Infrastructure/TTB/uat/bank-diagnostics.ps1 -Mode Verify
```

ไฟล์ PNG/JSON อยู่ `.artifacts/bank-update/<run>/`, QR1บาท อายุประมาณ15นาที แจ้งเวลาหมดอายุ Bangkokให้ธนาคารและสร้างใหม่ตอนถึงเวลานัด. ใช้ counter แบบ atomicร่วมกับ checkout จึงไม่ชน QRId ของคำสั่งซื้อ

QRนี้มี Ref1ขึ้นต้น`TTBUAT` เป็น **diagnostic ไม่ผูกคำสั่งซื้อ** ไม่มีใบเสร็จ/ส่งสินค้า หากมี callbackจะเก็บหลักฐานไว้ unmatched-payment review. ไม่ใช้ผลนี้แทนการตรวจรับการขายครบวงจร

Verify ค้นหา QRที่ยังไม่จ่าย จึงคาด no-data0002ตามสเปก. HTTP200อย่างเดียวไม่ถือว่าผ่าน ต้อง parse identity/codeถูกต้อง. ไม่ใช้ผล queryสร้างPaidหรือรับรอง keyอัตโนมัติ

## งานตรวจรับที่ยังต้องทำร่วมกับ TTB

1. **Verifyครั้งแรกได้HTTP401** ยังไม่ผ่านgatewayauth. ให้ธนาคารตรวจ issuer/public key/ข้อกำหนดgatewayจากRequest UIDในบันทึก ไม่เปลี่ยนกุญแจสุ่ม. `GatewayAuthenticationValidated=false`กั้นcheckoutแยกจากการเตรียมไฟล์key โดยยังอนุญาต Verifyครั้งแรก/หลังแก้profileได้
2. ส่ง Basic credentialsผ่านช่องทางปลอดภัย แล้วรับ callbackจริงเพื่อตรวจ source/auth/ACK/retry. การให้เราเลือก auth ไม่ได้แปลว่าธนาคารได้รับรหัสแล้ว
3. ตรวจชื่อผู้รับบนแอปธนาคารและตั้ง `BeneficiaryName` จากผลยืนยัน ไม่ใช้ชื่อบริษัททดสอบบนใบเสร็จแทนชื่อบัญชีจริง
4. ทดสอบ order1บาท UAT: scan → callback/Verify → allocationครั้งเดียว → สต็อก/สิทธิ์ → ใบเสร็จ → ประวัติ รวม duplicate/timeout/late credit
5. รับ SFTP/SSH fingerprint/PGPและตรวจรายงาน ก่อน Production sign-off; กุญแจ/Secret/DB/acceptanceแยกแม้ Billerเดียวกัน

ดูผลรอบนี้ใน `TTB_BANK_CONFIRMATION_20261020_TH.md`. Firewallเชื่อมได้หรือ decodeภาพได้ไม่แทนผลจ่ายครบวงจร
