# ข้อมูลยืนยันแล้วและงานประสาน TTB ที่เหลือ

อัปเดต20Oct2026 ไม่ต้องขอ Merchant/Biller/issuer/สิทธิ์ API/โครงสร้าง QR ซ้ำ. ยืนยันแล้ว: Merchant`PYA`, Biller`075555900002101`ทั้งUAT/Production, issuer`jwt-key-pya`, UATเฉพาะวงธนาคาร ให้คุณbirdสแกน; callbackใช้authที่merchantกำหนด

## เตรียมส่งให้ธนาคาร

- Server IP209.15.122.115
- UAT `https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification`, Basic over HTTPS; รับ NAT203.148.142.200และ203.148.143.101–105ตามสเปก
- Production URLที่ตกลง `https://ttb-api.mangosgo.com/ttb/v1/credit-notification` **ยังไม่เปิด Production**
- UAT public key `Provisioning/ttb-uat-jwt-5183ee1873474f4ab9aff6f1b530d588-public.pem`, DER-SPKI SHA256 `4b65641eaf164b3b2c80cc9b12178d58ab88d1bc5dd310ec185d0f1a97f3291c`; ไม่ส่ง private key
- Basic credentialsส่งผ่านช่องทางปลอดภัยตาม `TTB_BANK_CONFIRMATION_20261020_TH.md` ไม่อยู่ใน Gitหรือข้อความนี้
- QR1บาทสร้างใหม่ด้วย `Infrastructure/TTB/uat/bank-diagnostics.ps1 -Mode Qr` ตอนนัด มีอายุประมาณ15นาที เป็น diagnosticไม่ผูกคำสั่งซื้อ

## ประสานต่อเฉพาะสิ่งที่ยังขาด

1. นัดสแกน QR/ทดสอบ Basic callback และตรวจชื่อผู้รับที่ถูกต้องก่อนเปิด checkout
2. หาก Verifyไม่ผ่านauth ให้ตรวจ registrationของpublic keyข้างต้นกับ`jwt-key-pya`. คำตอบเรื่องสร้าง OpenSSL keyไม่ใช่หลักฐานregistration
3. SFTP/PGP: บัญชี/วิธีเข้า, port/path, SSH host key fingerprint, ช่องทางแลก public PGP keys, schedule/รายงานจริงตาม layoutที่มีแล้ว
4. หลังUATผ่าน ตรวจ Production key registration/callback credentials/report/ข้อมูลบริษัทและบัญชีจริง พร้อม controlled transaction/sign-offแยก ไม่ใช้ private key UATข้ามenvironment

ACK/retry, Verify no-data0002, Enquiry no-data0001, Void window/cutoffและ report layoutมีในเอกสารแล้ว ไม่ถามซ้ำหากยังไม่มีผลที่ขัดกัน. `sender_name`ใน Excelทั่วไปบังคับสำหรับตัวอย่างบางบริการ แต่ QR-specific tablesไม่ระบุ จึงเว้นไว้ ไม่เดาจากตัวอย่าง`TTB`/`xMerchaant`; ประสานเพิ่มเฉพาะเมื่อgatewayจริงแจ้งว่าต้องใช้
