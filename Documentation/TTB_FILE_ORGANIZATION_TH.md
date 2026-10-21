# ตำแหน่งไฟล์ TTB Payment

ปรับปรุงวันที่ 22 ตุลาคม 2569

Repository หลักคือ `AlbiziaLebbeck/MANGOFramwork` สาขา `MANGOxTTB` ใช้เก็บ source ที่จำเป็นต่อการพัฒนาและติดตั้งระบบทั้งหมด: Unity, WebCommerce, backend ใน Services, Infrastructure และเอกสารทางเทคนิคใน Documentation

ไฟล์รายงานที่สร้างเสร็จ หลักฐานทดสอบ และแพ็ก release จัดเก็บใน:

```text
C:\Users\sirap\OneDrive\Desktop\Pop\Chula\Metaverse\TTBPayment
```

## โครงสร้างโฟลเดอร์สำหรับใช้งาน

| โฟลเดอร์ | สิ่งที่เก็บ |
|---|---|
| API | เอกสารต้นฉบับจากธนาคาร คงชื่อและตำแหน่งเดิม |
| 01_Reports | รายงาน PDF, HTML และต้นฉบับ Markdown พร้อมสรุปส่งมอบ |
| 02_Documentation | สำเนาเอกสารของโปรเจกต์ ณ commit ที่ส่งมอบ |
| 03_Operations | ตัวเปิดคอนโซล/ดู log และสำเนาสคริปต์กับ manifest สำหรับอ้างอิง |
| 04_Evidence | ผล CI, screenshot, log และหลักฐานตรวจ UAT ที่คัดเลือกไว้ |
| 05_Releases | แพ็ก UAT ที่เคย deploy พร้อม checksum และข้อมูลรุ่น |
| 06_Keys | Public key และสำเนา DPAPI ที่เข้ารหัสแล้ว พร้อมคำอธิบายการกู้คืน |
| 07_Inventory | รายการไฟล์ SHA-256 และข้อมูลการส่งขึ้น GitHub |

เริ่มจาก `README_TH.md` ในโฟลเดอร์ TTBPayment เพื่อดูสารบัญและ commit ที่สำเนาเหล่านี้อ้างอิง สำเนาเอกสารไม่ใช่ฐานข้อมูลสดของ server หลักฐานเก่าคงวันเวลาและสถานะตามตอนที่ทดสอบไว้

## หลักการเก็บไฟล์

- เอกสารทางเทคนิคยังอยู่ใน Git เพื่อให้ทีมพัฒนาตรวจการเปลี่ยนแปลงย้อนหลังได้ และมีสำเนาสำหรับเปิดอ่านใน TTBPayment
- PDF/HTML ที่สร้างเสร็จอยู่ `01_Reports` ส่วน `output/` และ `tmp/` ในโปรเจกต์ใช้เป็นพื้นที่สร้างไฟล์ชั่วคราวและไม่ขึ้น Git
- Backend และสคริปต์ deploy เป็นส่วนหนึ่งของระบบ Payment จึงคงอยู่ใน repository สคริปต์ใน `03_Operations` ใช้เรียก source หลัก เพื่อให้ทำงานกับโปรเจกต์รุ่นปัจจุบัน
- Cache, node_modules, NuGet, Chromium และ bin/obj สร้างใหม่ได้ จึงไม่รวมในชุดเอกสารส่งมอบ
- ไม่เก็บ password, JWT หรือ private key ที่อ่านได้ใน Git และไม่คัดลอกไฟล์ส่งต่อ credentials แบบ plaintext ไปไว้ในชุด OneDrive
- สำเนา DPAPI ใช้ได้กับบัญชี Windows/เครื่องที่เข้ารหัสไว้เท่านั้น ไม่ใช่ private key ที่กู้บนเครื่องใหม่ได้ทันที ต้นฉบับใน `.tools` และข้อมูล key บน server ยังคงอยู่ที่เดิม
- แพ็กที่ใช้ reader contract3 เป็นประวัติ ไม่ใช่ตัว rollback โดยตรงสำหรับฐานข้อมูลปัจจุบันที่ต้องการ contract4

รายงานอธิบายการทำงานอยู่ใน [คู่มือ Payment และคอนโซล UAT](TTB_PAYMENT_IMPLEMENTATION_AND_TEST_CONSOLE_TH.md) ส่วน PDF ฉบับส่งมอบอยู่ `TTBPayment\01_Reports\MANGOs_TTB_UAT_Implementation_TH.pdf`
