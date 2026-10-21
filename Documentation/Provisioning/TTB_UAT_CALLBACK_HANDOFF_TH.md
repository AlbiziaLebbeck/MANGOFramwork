# Credit Notification UAT — โปรไฟล์ที่ TTB ยืนยัน 21 ตุลาคม 2026

คุณ Bird ยืนยันเวลา11:02น. ว่าระบบส่ง notification ของ TTB ไม่ใช้ username/password. ผู้ใช้อนุญาตให้ปรับ callback ของระบบให้ตรงกับวิธีนี้. ข้อมูล Basic ที่ส่งก่อนหน้านี้เลิกใช้สำหรับ UAT หลังเปิด profile BankSourceIp; ไม่ต้องตั้ง Authorization header.

```text
POST https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification
Content-Type: application/json; charset=UTF-8
Service-Name: credit-notification
Request-Datetime: <วันเวลาตามสเปก TTB เช่น 2026-10-21T11:02:00.123456+07:00>
```

Body ใช้ Credit Notification ตามสเปกธนาคารเดิม. ระบบตรวจ Biller, QRId, references, ยอดเงิน และสถานะรายการ พร้อมเก็บหลักฐานและป้องกัน notificationซ้ำก่อนตอบ ACK. Headers3รายการไม่ได้ใช้แทนการยืนยันต้นทาง.

HTTPS และ callback allowlist ทั้งที่ ingress และ application ยังรับเฉพาะ IPธนาคาร203.148.142.200,203.148.143.101–105. Application อ่านต้นทางผ่าน trusted proxy ที่pinเฉพาะ ingresscontrollerจริง; X-Forwarded-For/X-Real-IPจากแหล่งอื่นไม่สามารถยืนยันตัวตนธนาคารได้.

ให้แจ้งก่อนสแกนเพื่อสร้างQRของคำสั่งซื้อใหม่ในวงUAT เนื่องจากQRมีอายุ10นาที. หลังสแกนต้องตรวจ callback/source/ACK และสถานะออเดอร์จริง จึงบันทึก callback acceptance. Production ยังไม่เปิดรับและไม่ได้เปลี่ยน credentials/configuration ในงานนี้.

ข้อความส่งต่อคุณ Bird:

> ปรับฝั่ง UAT ให้รับ notification ตาม headers ทั้ง3รายการในสเปกแล้วครับ ไม่ต้องส่ง username/password หรือ Authorization headerครับ URLยังเป็น https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification ครับ รบกวนแจ้งก่อนสแกนเพื่อผมจะสร้างQRใหม่ให้ แล้วตรวจสถานะรับชำระร่วมกันครับ
