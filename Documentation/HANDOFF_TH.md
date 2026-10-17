# วิธีใช้ MANGOs Framework หลังปรับระบบ

[ภาษาไทย](HANDOFF_TH.md) | [English](HANDOFF_EN.md) | [เอกสารทั้งหมด / All documentation](README.md)

เปิดโปรเจกต์ด้วย **Unity 6000.3.26f1** แล้วเปิดเมนู **MANGOs Framework > Build Center**

โค้ดรุ่นปัจจุบันอยู่บน branch `MANGOsFramework_U6` ส่วนฐาน Unity 2022 เดิมยังอ้างอิงได้จาก Git history ที่ commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`

## Build Server และ Client

1. เปลี่ยน **Client Path** เป็น path ของ WebSocket deployment ที่ต้องการ
2. กด **Build Both** หรือเลือก Build Server / Build Client แยกได้
3. ใช้โฟลเดอร์ที่แสดงในผลสำเร็จ หรือดู `Builds/last-successful.json`

| ค่าที่แก้ได้ | Server | Client |
|---|---|---|
| Port เริ่มต้น | 7770 | 443 |
| WSS เริ่มต้น | ปิด | เปิด |
| Build target | Linux Dedicated Server, IL2CPP, x86_64 | WebGL |

ค่า Port/WSS ของแต่ละฝั่งเป็นอิสระและบันทึกใน Profile ได้ ไม่ต้องแก้ prefab หรือสลับ platform เอง ระบบรวม voice overlay และ controller เข้า WebGL template ให้อัตโนมัติ Host/path เริ่มต้นคง deployment เดิมไว้ที่ `multiplayer-plus.mangosgo.com` และ `/systemtester`

การใช้ค่าเริ่มต้นนี้ต้องให้ reverse proxy รับ WSS ที่ 443 แล้วส่งต่อเข้า Server แบบ WS ที่ 7770 มีไฟล์ตัวอย่าง Nginx ในโฟลเดอร์ Deploy ของผล Build ต้องตั้ง hostname, certificate และ document root ให้ตรงเครื่องจริงก่อนใช้งาน ระบบ Build ไม่ได้เปลี่ยน deployment บนเซิร์ฟเวอร์จริง

ต้องนำโฟลเดอร์ Server ไปทั้งชุด ไม่ใช่เฉพาะไฟล์ `.x86_64` จากนั้นเรียก `sh start-server.sh` บน Linux ส่วน Client ให้บริการผ่าน HTTPS พร้อม MIME type สำหรับ JavaScript และ WebAssembly

หากต้องใช้ command line บน Windows เรียก `Tools/BuildBoth.ps1` ได้ ตัวสั่งงานจะเลือก platform และเปิด Unity ต่อให้ครบทั้งสองฝั่งเอง ดูรายละเอียดและตัวเลือกใน [คู่มือ Build/runtime](BUILD_AND_RUNTIME_TH.md)

## ระบบเสียงใหม่

โค้ด VoiceCommunication เดิมถูกแทนด้วย `html-voice-tester` ที่อยู่ใน WebGL template ผู้เล่นกดเข้าร่วมเสียงและอนุญาตไมโครโฟนผ่าน browser overlay ระบบรอให้ Unity เข้าห้องก่อน โดยแยกห้องเสียงตาม deployment และ world id เปลี่ยน world หรือ disconnect จะออกจาก session เดิม

Account API, voice/media backend และ TURN ยังเป็นบริการภายนอกตาม config ของระบบ HTML เดิม จึงต้องตรวจ cookie/CORS, HTTPS, สิทธิ์ไมโครโฟน และ TURN กับ deployment จริงด้วย

## Avatar และ animation

คืนระบบ Avatar จาก `MANGOFramework_Cookie` แล้ว: glTFast โหลดโมเดล ตัว loader เลือก Humanoid Avatar อ้างอิงจาก `Resources/AvatarLoader` ตามโครงสร้าง node และใช้ Animator/controller เดิมบน root ของผู้เล่น คืน cache โมเดล การสร้าง thumbnail หน้าต่างเลือก Avatar และ cooldown ห้าวินาที รวมถึงจุดเชื่อม network การนั่งและ interaction เดิม

การรองรับเหมือนระบบ Cookie และขึ้นกับ rig อ้างอิงที่ระบบเดิมรู้จัก ถอดระบบสร้าง Humanoid รายโมเดล, Rig Profiles, importer เฉพาะ VRM, การปรับความสูงอัตโนมัติ และเมนู audit animation ที่เพิ่มใหม่ออกแล้ว เส้นทาง VRM/reference rig แบบเดิมยังอยู่ แต่ไม่ได้รับประกัน GLB/VRM ทุก skeleton


## Controller จาก Cookie

บน browser จอสัมผัส แถบเสียง HTML อยู่กึ่งกลางด้านล่างเพื่อเว้นพื้นที่ joystick เดิม

คืน controller ของ Unity แบบเดิม: Starter Assets, `PlayerMovementHandler`, `LocomotionManager`, mobile controls ของ Unity, ปุ่มสลับ controller, การเดิน/click-to-move และการบิน การตรวจอุปกรณ์ใช้ `CheckMobile`/`IsMobile` เดิม คืนพฤติกรรม cursor/pointer lock และ orientation guard ของ Cookie รวมข้อกำหนด landscape ตามที่ตั้งไว้ใน prefab

ถอด HTML controller, การสลับ input อัตโนมัติ และ camera/input router ที่เพิ่มใหม่ออกแล้ว โค้ด Avatar และ input ใช้การทำงานของ Cookie ส่วน browser bridge ที่เหลือใช้เชื่อมระบบ HTML voice แยกต่างหาก


หลังอัปเดตให้ build และ deploy Client ใหม่ทั้งโฟลเดอร์ รุ่น cache ของ PWA เปลี่ยนแล้ว

## ก่อนนำขึ้นระบบจริง

ดูผลตรวจและข้อจำกัดใน [ผลตรวจ](VALIDATION.md) และรายละเอียดเทคนิคใน [คู่มือ Build/runtime](BUILD_AND_RUNTIME_TH.md) การทดสอบ browser จำลองไม่แทนการเล่นบน iPhone/iPad/Android จริง และการทดสอบเสียงแบบ mock ไม่แทนการทดสอบบัญชีจริงกับ microphone, media backend และ TURN

ชื่อ guest ในภาพประกอบคือ **PoppyValidation** เป็นข้อมูลทดสอบ ผู้เล่นทั่วไปยังใช้ชื่อที่เลือกหรือชื่อบัญชีของตนเอง
