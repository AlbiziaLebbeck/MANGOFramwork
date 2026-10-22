# คู่มือ Admin Dashboard สำหรับ MANGOs × TTB

เปิดที่ **https://plus.mangosgo.com/mangoxttb/dashboard/** แล้วเข้าสู่ระบบด้วยบัญชี MANGOs เดิมที่ใช้ทดสอบซื้อ Mango Star (`Sirapop Poppy`) ไม่ต้องเปิด SSH tunnel ก่อนใช้งาน

Dashboard นี้อ่านข้อมูล UAT จาก Backend บน ntserver02 โดยตรง บัญชี MANGOs ทั่วไปไม่มีสิทธิ์ดูข้อมูลการชำระเงินของร้าน รายชื่อผู้ดูถูกกำหนดฝั่ง server เป็นรายบัญชี และให้สิทธิ์ดู Dashboard เท่านั้น ไม่ได้เพิ่มสิทธิ์แก้สินค้า คืนเงิน หรือสั่งจ่ายเงิน

## เวลาคุณ Bird สแกน QR ต้องดูตรงไหน

1. เปิดเมนู **ภาพรวม** หรือ **Notification** ไว้ก่อนสแกน
2. เปิด Auto refresh เพื่ออ่านข้อมูลใหม่ทุก 30 วินาที หรือกด **รีเฟรช** หลังธนาคารแจ้งว่าจ่ายแล้ว
3. ใน Notification ให้ดูเลขออเดอร์ เวลา ผลลัพธ์ และ ACK หากยังไม่จับคู่ออเดอร์ ระบบจะแสดงข้อความว่า **ยังไม่จับคู่ออเดอร์** เพื่อให้ตรวจต่อ
4. คลิกรายการ ดู Request UID, ต้นทาง, QRId และ InstructionId ประกอบเวลา สามารถเปิดรายละเอียด JSON แล้วคัดลอกหรือบันทึกไฟล์ไว้ส่งให้ผู้พัฒนา/ธนาคารได้
5. ดูออเดอร์ที่เกี่ยวข้องว่า **ชำระแล้ว** และแผงด้านขวาแสดงขั้นตอนใดสำเร็จแล้ว การรับ noti การยืนยันเงิน และการส่งสินค้าเป็นคนละขั้นตอน

ทุกเวลาแสดงเป็นเวลาไทย UTC+7 และปี ค.ศ. จึงเทียบกับ ISO8601 ใน log ได้โดยบวก 7 ชั่วโมงจาก UTC

## เมนูต่าง ๆ

| เมนู | ใช้ทำอะไร |
|---|---|
| ภาพรวม | จำนวนคำสั่งซื้อ ออเดอร์ที่ชำระสำเร็จ จำนวนเหตุการณ์ callback ที่บันทึก รายการออเดอร์ และ noti ล่าสุด |
| รายการชำระเงิน | ค้นหาเลขออเดอร์ QRId InstructionId หรือ Ref และกรองรายการชำระแล้ว/รอชำระ |
| Notification | อ่านเหตุการณ์ callback ที่บันทึกในฐานข้อมูล รวมรายการที่ยังไม่จับคู่และรายการที่ตรวจไม่ผ่าน |
| รายการ TEST | ดู QR ที่ Ref1=TEST และประวัติ Notification, Verify, Void ของการทดสอบ ซึ่งแยกจากการซื้อสินค้า |

จำนวน Notification คือจำนวนเหตุการณ์ `Source=Callback` ในระบบซื้อสินค้า อาจมากกว่าจำนวนผู้จ่าย เพราะธนาคารสามารถส่งรายการเดิมซ้ำได้ รายการ TEST มีประวัติของตนเองและไม่รวมในยอดนี้

ตารางออเดอร์และ Notification อ่านครั้งละไม่เกิน 50 รายการและมีปุ่มเปลี่ยนหน้า รายการ TEST แสดง 50 เคสล่าสุด ส่วนเหตุการณ์ TEST มีปุ่มอ่านเหตุการณ์เก่ากว่า เลือกเคสเพื่ออ่านประวัติของเคสนั้น

## อ่านผลอย่างไร

- `Source=Callback` หมายถึงหลักฐานที่มาจากการเรียก Credit Notification ไม่ใช่การสอบถาม Verify
- `Outcome=Confirmed` หมายถึงระบบตรวจข้อมูลและยืนยันผลตามกติกาแล้ว
- `ACK 0000` เป็นรหัสตอบรับที่เก็บไว้หลังบันทึกข้อมูลสำเร็จ ให้ดูสถานะออเดอร์และยอดเงินประกอบด้วย
- ถ้าคอลัมน์ ACK เป็นขีด แปลว่าแถวนั้นไม่มี receipt ที่ผูกโดยตรง ไม่ควรสรุปว่าเป็น ACK0000 จาก HTTP200 อย่างเดียว
- `จำนวนครั้งที่ส่ง` ใน receipt เป็นจำนวน delivery ของ InstructionId เดิม การส่งซ้ำไม่ควรสร้างสินค้าหรือยอดเงินซ้ำ
- Verify HTTP200 พร้อม bank code0002 หมายถึงติดต่อสำเร็จแต่ยังไม่พบการชำระ ไม่ใช่จ่ายสำเร็จ

Dashboard ปกปิด raw bank payload, ข้อมูลบัญชี/ชื่อผู้จ่าย, buyer snapshot, cookie, JWT และ private key รายละเอียด JSON เป็นข้อมูลที่คัดเฉพาะสำหรับตรวจระบบ ไม่ใช่ raw log ทั้งเครื่อง

## TEST และการสั่ง Verify/Void

Dashboard หน้านี้เป็นหน้าตรวจข้อมูล การอ่านหรือกดรีเฟรชไม่ส่งคำขอ Verify/Void ไปธนาคาร

การสร้าง QR และสั่งงานธนาคารยังใช้คอนโซลทดสอบส่วนตัวเดิม เปิด PowerShell จาก Unity project แล้วรัน:

```powershell
.\Infrastructure\TTB\uat\open-bank-test-console.ps1
```

เปิด `http://127.0.0.1:18765/internal/bank-test-console/` บนเครื่องเดียวกัน แล้วทำตามขั้นตอนยืนยันในคอนโซล เมื่อกลับมา Dashboard จะอ่านประวัติที่บันทึกใหม่ได้

## ดู log ด้วย SSH เพิ่มเติม

```powershell
.\Infrastructure\TTB\uat\get-uat-log.ps1 -View Api -Since 30m -Follow
.\Infrastructure\TTB\uat\get-uat-log.ps1 -View Order -OrderNumber T20261021000002
```

คำสั่งแรกดูข้อความและข้อผิดพลาดของบริการที่กำลังทำงาน คำสั่งที่สองอ่านหลักฐานออเดอร์ย้อนหลังจากฐานข้อมูล การไม่มีข้อความ callback ใน stdout ไม่ได้แปลว่าไม่เคยได้รับ noti

## โครงสร้างสำหรับผู้ดูแลระบบ

- Frontend: `WebCommerce/src/dashboard/`, build ด้วย `pnpm run build:dashboard`
- ไฟล์ build ถูกเก็บใน `Services/TtbCommerce/src/TtbCommerce.Api/Dashboard/` ซึ่งไม่ commit เป็น source แต่รวมและตรวจ hash ใน Backend CI
- Backend: `PaymentDashboardEndpoints.cs` ตรวจ session/สิทธิ์และเสิร์ฟหน้าเว็บ; `PaymentDashboardQueries.cs` อ่านข้อมูลแบบไม่แก้ไข พร้อมคัดเฉพาะ fields ที่อนุญาต
- เส้นทางข้อมูลทั้งหมดอยู่ใต้ `/mangoxttb/dashboard/api/` ใช้ HTTPS host เดียวกับหน้าเว็บ
- ConfigMap `ttb-dashboard-access` เปิด feature และกำหนด `Dashboard__Subjects__0` เป็น `mangosgo:<subject UUID>` ของบัญชีที่ตรวจแล้ว
- การเพิ่มบัญชีต้องตรวจ subject จากบัญชี MANGOs จริงก่อน แล้วเพิ่ม allowlist ฝั่ง server ห้ามใช้ชื่อที่ browser ส่งมาเป็นสิทธิ์เอง
- การเปลี่ยน config ต้อง rollout API เพื่อโหลดค่าใหม่ สิทธิ์ตรวจที่ server ทุกคำขอ มี session revalidation และ rate limit
- Default ปิดไว้ และปิดใน Production/worker roles ไม่เปิดทับระบบ Production จากการ deploy UAT
- Ingress เพิ่มเฉพาะ Prefix `/mangoxttb/dashboard` บน plus.mangosgo.com โดยไม่มี regex/rewrite ที่เปลี่ยนพฤติกรรมของ route อื่น
- ไม่เปลี่ยน callback URL, bank source IP allowlist, proxy trust, signing key, ฐานข้อมูล schema หรือ worker releases

## การ build และ deploy

รัน frontend build ก่อน Backend CI เพื่อให้ CI ตรวจและบรรจุ Dashboard assets ชุดเดียวกับที่นำขึ้นจริง ใช้ artifact `api-linux` ของ Backend CI ที่ผ่านครบและ `inputFilesUnchanged=true` เท่านั้น

สคริปต์ `Infrastructure/TTB/uat/deploy-dashboard.ps1` ตรวจ checksum ของ artifact, reader contract, UID/resourceVersion และ path release เดิมก่อนเปลี่ยนเฉพาะ API release และเพิ่ม config/ingress ของ Dashboard สคริปต์นี้ใช้สำหรับเปิดครั้งแรก หากมี config เดิมแล้วจะหยุดให้ตรวจแทนการเขียนทับ

การอัปเดตแบบ delta ใช้ `update-dashboard-release.py` เมื่อชื่อไฟล์ใน artifact เหมือน release ฐานทั้งหมด: สร้างโฟลเดอร์ release ใหม่จากฐานเดิม วางเฉพาะไฟล์ที่เปลี่ยน และตรวจ SHA-256 ของไฟล์ทั้งชุดกับ manifest ของ CI ใหม่ก่อน patch API แบบตรวจ UID/resourceVersion ไม่มีการเขียนทับโฟลเดอร์ release ที่กำลังทำงาน

หากต้องปิดการเข้าจากเว็บ ให้ลบเฉพาะ ingress `ttb-payment-dashboard-uat` ใน namespace `ttb-uat` การถอย API ต้องใช้ release ที่อ่าน contract4 ได้ และตรวจ deployment patch เดิมก่อน ไม่ลด reader floor หรือแก้ข้อมูลออเดอร์เพื่อให้ binary เก่าเริ่มทำงาน

## การทดสอบและข้อจำกัด

มี integration tests บน PostgreSQL development ที่แยกจาก UAT สำหรับสิทธิ์บัญชี, CSRF/logout, การถอน session, callback chain, pagination, callback ที่ไม่จับคู่ออเดอร์ และการปกปิดข้อมูล ตรวจหน้าเว็บด้วย Playwright Chromium เนื่องจากไม่มี Browser plugin ใน session นี้ รวม desktop1505×1045 และ mobile390×844

ภาพหน้าจอทดสอบที่ใช้ข้อมูลจำลองมีไว้ตรวจ UI เท่านั้น ไม่ถือเป็นหลักฐานว่าธนาคารสแกน QR ใหม่ ส่วนผล deploy และข้อมูลเดิมบน UAT จะบันทึกแยกในโฟลเดอร์ `TTBPayment/04_Evidence/dashboard`
