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

รายการ Avatar ของบัญชีและ guest เลือกได้ก่อน thumbnail โหลดเสร็จ ภาพตัวอย่างที่โหลดไม่สำเร็จไม่ปิดปุ่มเลือก เมื่อเปิดรายการจะเลื่อนกลับด้านบนและปรับ panel ให้พอดี safe area พร้อมพัก input เกมและซ่อน touch controls การโหลดโมเดล WebGL จาก MANGOs API origin ที่ตั้งค่าไว้ส่ง Cookie SSO ส่วนโมเดลสาธารณะภายนอกยังใช้ anonymous CORS ฝั่ง API ยังต้องอนุญาต credentialed CORS ของเว็บที่ deploy

GLB สร้าง Humanoid Avatar จากกระดูกของโมเดลนั้น โดยปรับท่าพัก/A-pose เป็น T-pose สำหรับตั้งต้นและจัดทิศทางก่อน retarget ส่วน VRM ใช้ importer ของ UniVRM และ Humanoid Avatar ของตัวเอง ระบบไม่เอาสัดส่วนของ Avatar อ้างอิงไปครอบโมเดลใหม่ และส่ง Animator ที่ใช้งานอยู่ให้ระบบเดิน บิน FishNet และ interaction

ค่าเริ่มต้นปรับขนาดภาพแบบ uniform ให้สูง 1.7 เมตรเพื่อให้สอดคล้องกับ collider เดิม ไม่ยืดกระดูกเป็นรายชิ้น ถ้าต้องการคงความสูงจริง ให้ปรับนโยบาย collider/เกมร่วมด้วย โมเดล Humanoid ที่ชื่อกระดูกไม่ตรงสามารถสร้าง Avatar Rig Profile เพื่อระบุ mapping ได้ โมเดลที่ไม่มีโครงกระดูก Humanoid ไม่สามารถรับประกันการใช้ animation Humanoid เดิมได้ทุกตัว

ส่งออกข้อมูล `.anim` และคลิปในโมเดลได้ที่ **MANGOs Framework > Avatar > Export Animation Audit** รายงานอยู่ที่ `Library/MangosAnimationAudit.csv`

## Controller

ระบบตรวจอุปกรณ์อัตโนมัติและเลือก keyboard/mouse บน PC หรือ touch บนโทรศัพท์/tablet รวม iPad desktop user agent ส่วน PC จอสัมผัสยังใช้ desktop controls เอาเมนู Controls และปุ่มสลับ controller เดิมออก พร้อมล้าง override ที่บันทึกจาก build เก่า บน mobile การกดปุ่มเดินหรือใช้ mouse จะเลือก keyboard/mouse และการแตะหน้าจอจะกลับเป็น touch

Touch ใช้ joystick ซ้าย เดิน, ลากด้านขวา หมุนกล้อง, Run และ Jump ส่วน Fly/ขึ้น/ลงจะแสดงเฉพาะตัวละครที่บินได้ ปุ่ม interaction ของ Unity ยังแตะได้ ระบบล้าง input ที่ค้างเมื่อหมุนจอ เปลี่ยนโหมด หลุด focus หรือ disconnect และพัก input ระหว่างพิมพ์ในช่อง HTML

บน PC ต้องกดคลิกขวาค้างแล้วลากจึงหมุนกล้อง หลังอัปเดตต้อง Build Client ใหม่และนำ Client ทั้งโฟลเดอร์ขึ้นเว็บ รวม HTML/JS และไฟล์ Unity ที่ตรงชุดกัน ให้ cache/service worker อัปเดตเป็น build ใหม่ด้วย

## ก่อนนำขึ้นระบบจริง

ดูผลตรวจและข้อจำกัดใน [ผลตรวจ](VALIDATION.md) และรายละเอียดเทคนิคใน [คู่มือ Build/runtime](BUILD_AND_RUNTIME_TH.md) การทดสอบ browser จำลองไม่แทนการเล่นบน iPhone/iPad/Android จริง และการทดสอบเสียงแบบ mock ไม่แทนการทดสอบบัญชีจริงกับ microphone, media backend และ TURN

ชื่อ guest ในภาพประกอบคือ **PoppyValidation** เป็นข้อมูลทดสอบ ผู้เล่นทั่วไปยังใช้ชื่อที่เลือกหรือชื่อบัญชีของตนเอง
