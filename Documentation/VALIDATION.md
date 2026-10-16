# ผลตรวจการปรับ MANGOs Framework

วันที่: 16 ตุลาคม 2026 — Unity 6000.3.26f1

ฐานเดิม: `e461cdcad0b5bf7191f903afc828ea248b296a6f` / Unity 2022.3.62f3

ชุด Build ด้วยค่าเริ่มต้นที่ส่งมอบ: `Builds/mangos/20261016-161442-6eaf09` สำเร็จทั้ง Server และ Client โดยใช้ 7770/WS และ 443/WSS ตามที่กำหนด ดู manifest ที่เก็บใน `Validation/default-build-manifest.json` ชุดนี้เป็นผล Build พร้อมตั้ง deployment ต่อ ไม่ใช่ผลยืนยัน production service ที่ใช้งานจริง

## สิ่งที่ตรวจแล้ว

| ส่วน | ผลและหลักฐาน |
|---|---|
| Unity 6 / FishNet / glTFast / IL2CPP | Build Linux Dedicated Server แบบ IL2CPP และ WebGL ผ่านจริง จึงคง Unity 6 ตามแผน ไม่ต้องใช้เงื่อนไขข้าม upgrade |
| Build Both | งาน `20261016-160736-2dab27` สำเร็จทั้ง Server และ Client ใช้ตัวสั่งงานอัตโนมัติที่เริ่ม Unity ต่อด้วย platform ที่ถูกต้อง มี manifest และการคืนค่า resource config หลังจบงาน |
| เครือข่าย | เปิด Server Linux ใน WSL แล้วให้ Client WebGL ที่ Build จากงานเดียวกันเชื่อม `ws://localhost:7788/validation/game` ผ่าน FishNet และเข้า BaseWorld ด้วย guest สำเร็จ |
| ค่า Build | ตรวจค่าเริ่มต้น 7770/WS และ 443/WSS, การแก้ค่าแยกฝั่ง, การบันทึกค่า, การปฏิเสธ path/port ที่ไม่ถูกต้อง และเงื่อนไข certificate ของ Server WSS |
| Transport | แก้และตรวจการอ่าน Sec-WebSocket-Key โดยไม่ขึ้นกับตัวพิมพ์เล็ก/ใหญ่ ตรวจ handshake และการเชื่อมจาก WebGL จริง |
| Voice overlay | ทดสอบด้วย mock: รอห้อง, join, mute, เปลี่ยนห้องโดยคง mute, leave, เปลี่ยนห้องติดกัน, เปลี่ยนห้องระหว่าง join และ dispose ผ่าน ตรวจ Unity ส่ง world id เข้า overlay ใน Client จริงแล้ว |
| Controller | ทดสอบ Edge/Chromium จำลอง PC, iPhone, Android tablet, iPad desktop UA และ PC จอสัมผัส ผ่าน ตรวจ joystick+look+jump พร้อมกัน, หมุนจอขณะกด, focus/blur, keyboard auto mode, manual override และ cleanup เมื่อออกจากเกม |
| Input ฝั่ง Unity | ตรวจการส่งแกนเดินและกล้องไป controller, ปุ่มกระโดด/บินหนึ่งครั้งต่อการกด, sprint/ขึ้นลง และการล้าง input เมื่อเปลี่ยนแนวหรือพิมพ์ข้อความ |
| Humanoid | สร้าง Avatar แยกตามโมเดล ทดสอบสองขนาด, A-pose และทิศทางกลับด้าน เล่น animation 60 เฟรม ตรวจว่ากระดูกถูก animation ขยับจริงและความยาวแขนไม่เปลี่ยน พร้อมปฏิเสธโมเดลไม่มี rig |
| GLB ตัวอย่างจริง | `RajPattern_Blue.glb` จาก MANGOs: พบและแก้กรณีแขนไขว้จากการใช้ท่าพักเป็น T-pose ตรวจตำแหน่งมือซ้าย/ขวาหลัง retarget ใน Editor และตรวจภาพยืน/เดินจาก WebGL หลังโหลด GLB สำเร็จ HTTP 200 |
| VRM | นำเข้า synthetic VRM 1.0 ด้วย raw skeleton และตรวจ Animator/Avatar ที่ valid ผ่านใน Editor เส้นทาง UniVRM รวมใน native builds แล้ว |
| .anim | ตรวจ 52 คลิป เป็น Humanoid และไม่พบ Transform position/scale curve ที่ใช้ยืดกระดูกโดยตรง มีเมนูส่งออก CSV สำหรับตรวจต่อ |
| Shader / UI | ปรับ TextMeshPro shader จาก Essential Resources ของ Unity 6 โดยคง GUID เดิม แก้ตัวอักษรเป็นสี่เหลี่ยม ตรวจภาพ Launcher และ world ทั้งแนวตั้ง/แนวนอนแล้ว |

ภาพใน `Validation/` มาจาก Client ที่ Build จริง ไม่ใช่ภาพ UI mock ส่วน `browser-summary.json` ระบุ build id และผลที่บันทึกไว้

## ขอบเขตที่ยังไม่ได้ยืนยันกับระบบจริง

- ยังไม่ได้เล่นบนเครื่อง iPhone/iPad/Android จริง การจำลองทั้งหมดใช้ Chromium ไม่ใช่ Safari บนอุปกรณ์จริง
- ยังไม่ได้ทดสอบเสียงสดด้วยบัญชีของผู้ใช้, ไมโครโฟนจริง, cookie/CORS ของ production และ TURN/คุณภาพเครือข่าย
- ตรวจ GLB ตัวอย่างสาธารณะหนึ่งตัว และ synthetic VRM 1.0 ไม่ได้ตรวจ Avatar ทุกตัวในบัญชี หรือ VRM 0.x/1.0 จริงทุกชนิด ต้องตรวจ spring bones, cloth, extreme proportions และท่านั่ง/ปฏิสัมพันธ์ด้วยโมเดลจริงเพิ่มเติม
- ไม่สามารถรับประกันว่า GLB ที่ไม่มี Humanoid skeleton หรือมี skin weights/bind pose ผิดจะใช้ animation เดิมได้ทุกตัว มี Rig Profile สำหรับแก้ mapping และมี fallback เมื่อโหลดไม่ผ่าน
- เส้นทางหลักของ Build ถูกทดสอบผ่าน batch runner ปุ่มและการสลับ platform แบบ GUI ใน Editor ยังไม่ได้คลิกตรวจบนหน้าต่างจริง
- ไม่มีการ deploy หรือเปลี่ยน reverse proxy ของระบบจริง การเผยแพร่ source ผ่าน Git แยกจากการยืนยัน deployment และ runtime

## Diagnostics ที่ยังมี

Unity API Updater พิมพ์ Mono/Cecil `Argument null or empty (name)` เมื่อสแกน assembly เดิมบางส่วน แม้ BuildReport จะสำเร็จ จึงยังไม่เรียกว่า build ที่ไม่มี diagnostics ให้ดู `build-diagnostics.txt` ในผล Build และ log ใน `Builds/Logs` ด้วย

Headless browser มีคำเตือน shader ภายในบางตัวและการคลาย ASTC texture ที่ GPU จำลองไม่รองรับ ตรวจภาพแล้วว่าฉาก, GLB ตัวอย่าง และข้อความหลักแสดงได้ แต่ยังต้องตรวจ GPU/เครื่องเป้าหมายจริง ส่วนคำเตือนเสียงที่ยังโหลดไม่ครบและ JobTempAlloc ตอนปิด Editor ก็ยังปรากฏใน log

## วิธีตรวจซ้ำ

- `Tools/BuildBoth.ps1 -Validation` รัน checks ฝั่ง Editor และสร้างคู่ local validation ที่ 7788/WS
- `MangosAvatarValidation.Run` ตรวจ synthetic Humanoid/VRM และ animation audit
- `MangosRealAvatarValidation.Run` พร้อม `-mangosAvatarFixture <path-to-glb>` ตรวจ fixture Humanoid จริง โดยไม่ดาวน์โหลดหรือ bundle โมเดลให้อัตโนมัติ
- สคริปต์ browser QA และ log เต็มอยู่ในโฟลเดอร์ `validation` ของ workspace `html-voice-tester-main`
