# การ Build และระบบขณะเล่นของ MANGOs

[ภาษาไทย](BUILD_AND_RUNTIME_TH.md) | [English](BUILD_AND_RUNTIME.md) | [เอกสารทั้งหมด / All documentation](README.md)

เปิดโปรเจกต์ด้วย Unity **6000.3.26f1** บน branch `MANGOsFramework_U6` ฐาน Unity 2022 เดิมยังอยู่ใน Git history ที่ commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`

## Build Center

เปิด **MANGOs Framework > Build Center** แก้ Client Path แล้วกด **Build Server**, **Build Client** หรือ **Build Both**

ค่า Server และ Client แก้ได้แยกจากกัน:

| ค่า | Server เริ่มต้น | Client เริ่มต้น |
|---|---|---|
| Port | 7770 | 443 |
| WSS | ปิด | เปิด |

Host/path เริ่มต้นคง deployment ของ Bootstrap เดิมไว้ที่ `multiplayer-plus.mangosgo.com`, `/systemtester` หากเว้น host ว่าง จะใช้ hostname ของหน้า WebGL กด **Save Profile** เพื่อบันทึกใน `ProjectSettings/MangosBuildProfile.json` และ **Reset Network Defaults** เพื่อคืนค่าเครือข่ายโดยชัดแจ้ง การ Build ไม่เขียนทับค่าที่แก้ไว้กลับเป็น default โดยเงียบ ๆ

โครงสร้างเริ่มต้นให้ reverse proxy รับ TLS ที่พอร์ต 443 แล้วส่ง WebSocket ต่อเข้า Server ที่ 7770 ค่า Server WSS ควบคุม TLS listener ของ Server ส่วน Client WSS ควบคุม scheme การเชื่อมต่อของ Client หากเปิด Server WSS ให้ตั้ง path ของ PFX บนเครื่อง deployment ตัวแปร `MANGOS_TLS_CERTIFICATE` ใช้แทน path นี้ได้ ส่วนรหัสผ่านอ่านจาก environment variable ที่ระบุใน profile ซึ่งเริ่มต้นเป็น `MANGOS_TLS_PASSWORD` ระบบไม่คัดลอก certificate หรือค่ารหัสผ่านเข้า WebGL

Build Both ทำทีละ platform และกลับมาทำต่อหลัง Editor reload script แต่ละงานได้โฟลเดอร์เฉพาะ `Builds/<deployment>/<build-id>/` ที่มี Server, Client, ตัวอย่าง deployment และ manifest งานที่ไม่ครบหรือถูกยกเลิกจะเหลือ `.staging` และแทนที่ release ล่าสุดที่สำเร็จไม่ได้ `Builds/last-successful.json` ระบุงานล่าสุดที่ครบทั้งชุด resource config ที่สร้างขึ้นและ Editor settings ชั่วคราวจะถูกคืนค่าเมื่องานจบหรือกู้คืนจากการขัดจังหวะ

บน Windows ตัวช่วย Build อาจยังถือ directory handle หลัง Build สำเร็จ หากเปลี่ยนชื่อ staging ไม่ได้ pipeline จะคัดลอก output ที่เสร็จแล้ว และเผยแพร่ success manifest หลังคัดลอกครบ อาจเหลือ staging อีกชุด ซึ่งไม่ใช่ directory ที่ `last-successful.json` ระบุ หาก finalization ล้มเหลว งานจะเป็น Failed และไม่แทนที่ release ที่สำเร็จก่อนหน้า

Deploy **โฟลเดอร์ Server ทั้งชุด** รวม `_Data`, `GameAssembly.so` และ `UnityPlayer.so` บน Linux เรียก `sh start-server.sh` ให้บริการ Client ผ่าน HTTPS พร้อม MIME ของ JavaScript/WebAssembly ที่ถูกต้อง ตั้งค่า `Deploy/nginx-game.conf.example` ให้ตรง host, certificate และ document root จริงก่อนนำไปใช้ Build Center สร้างตัวอย่างนี้แต่ไม่ได้ deploy ให้

มี batch entry point ด้วย: ใช้ `-buildTarget Linux64 -standaloneBuildSubtarget Server -executeMethod MangosBuildPipeline.BuildServerBatch` หรือ `-buildTarget WebGL -executeMethod MangosBuildPipeline.BuildClientBatch` ร่วมกับ `-batchmode -nographics -quit -projectPath <project>` และเลือก profile อื่นด้วย `-mangosProfile <json>` ได้ entry point เหล่านี้เขียน output ลง `Builds/Validation/<target>`

สำหรับ **Build Both** อัตโนมัติบน Windows เรียก `Tools/BuildBoth.ps1` โดยระบุ `-Unity <Unity.exe>` และ `-Profile <json>` เพิ่มได้ ตัวสั่งงานเปิด Unity ใหม่ระหว่าง Linux กับ WebGL ทำงานเดิมต่อจากสถานะที่บันทึกไว้ และเขียน log ลง `Builds/Logs` เพราะ batchmode โหลด Editor API modules ของ platform ใหม่ใน process เดียวไม่ได้ ส่วน Build Center แบบ GUI สลับ platform และทำต่อผ่าน Editor reload ปกติ ใช้ `-Validation` เฉพาะ profile ทดสอบในเครื่องที่พอร์ต 7788, WS, localhost ซึ่งรัน input/avatar/settings checks ด้วย

## การสื่อสารผ่าน browser

WebGL template มี `voice-app.js` และ `voice-overlay/` จึงไม่ต้องคัดลอกเองหลัง Build ระบบถอด C# voice manager, voice `.jslib`, JS เดิม และ Socket.IO ที่แนบมากับระบบเก่าแล้ว UI callbacks ของ Scene เดิมคงไว้เป็น compatibility entry point ที่ซ่อน ส่วน browser overlay ดูแล UI สื่อสาร

Overlay รอให้เข้าห้องเกมก่อน room key ประกอบจาก deployment namespace กับ world/session id ที่ Unity ส่งมา การเปลี่ยนโลกปิด media session เก่าก่อนเข้าห้องใหม่และคง mute preference ของผู้ใช้ Disconnect/logout ปิด media session การจับเสียงยังต้องให้ผู้ใช้กด Join Voice และอนุญาตไมโครโฟน

บริการ account/voice/media ยังอยู่ภายนอก ค่า URL เริ่มต้นอยู่ใน `voice-overlay/config.js` deployment ต้องรองรับ cookie domain/SameSite และ credentialed CORS ที่ถูกต้อง media service ต้องส่ง ICE/TURN config ที่ใช้งานได้ บริการเหล่านี้ไม่รวมใน Unity Server ห้ามใส่ account tokens หรือ TURN secrets ในไฟล์ Build สาธารณะ

Template เก็บ source ของ Overlay ไว้ ทำให้ Unity project Build ได้โดยไม่พึ่งโฟลเดอร์ voice-tester ข้างกัน เมื่อแก้ source Overlay ต้นฉบับให้ปรับสำเนาใน template ให้ตรงกันด้วย

## การโหลด Avatar และ animation

VRM 0.x/1.0 ใช้ UniVRM ที่ตรึงไว้รุ่น 0.128.0 โดย Humanoid Avatar และ raw skeleton ของโมเดลที่นำเข้าขับ mesh ของตัวเอง GLB ทั่วไปใช้ glTFast จัดแกนขึ้น/ขวาของร่างกาย และหมุนข้อต่อเป็น T-pose ก่อนสร้าง Unity Avatar ของโมเดลนั้น การ calibrate รองรับท่าพัก/A-pose โดยไม่เปลี่ยนความยาวกระดูก skin weights หรือ inverse bind matrices Avatar อ้างอิงเดิมใช้เป็นแหล่งชื่อกระดูกเท่านั้น ไม่ใช้ Avatar object หรือขนาด skeleton ของมันครอบโมเดลอื่น

ระบบเก็บ movement/fly Animator Controller เดิม และย้าย state/parameters ไปโมเดลใหม่ FishNet NetworkAnimator, Starter Assets, การบิน และ interaction ผูกกับ Animator ที่ใช้งานจริง แต่ละโมเดลเป็นเจ้าของ native assets ที่นำเข้าและ dispose เมื่อถูกแทนที่ ไม่ clone live model เข้า cache ซ่อน 100 ตัวแบบเดิม Thumbnail ใช้ importer เดียวกัน ทำให้ VRM ใช้เส้นทางวัสดุเหมือนโมเดลผู้เล่น

AvatarLoader เริ่มต้นปรับความสูงภาพแบบ uniform เป็น 1.7 เมตร คงสัดส่วนร่างกายและนโยบาย collider ที่มีอำนาจตัดสินฝั่งเกมเดิม หากต้องการนโยบายภาพอื่น แก้ `normalizeHeight`/`targetHeight` บน player/preview prefab การเก็บความสูงจริงที่หลากหลายต้องมีนโยบาย gameplay/collision ที่สอดคล้องกัน Camera/nameplate offsets ปรับตามความสูงภาพที่ได้ ไม่ยืดความยาวกระดูก skin weights หรือ inverse bind matrices ให้ตรง reference rig

สำหรับ Humanoid GLB ที่ mapping ไม่ตรง สร้าง **MANGOs Framework > Avatar Rig Profile** จากเมนูสร้าง asset กำหนด Humanoid role เป็น node path แล้วใส่ profile ให้ AvatarLoader หรือวางใต้ `Resources/AvatarRigProfiles` พร้อม URL prefix ที่ตรงกัน Rig ที่ไม่ครบหรือกำกวมจะถูกปฏิเสธพร้อม `LastLoadError` และเก็บโมเดลก่อนหน้า/default ไว้ ไฟล์ที่ไม่มี Humanoid rig ไม่สามารถใช้ Humanoid animation อัตโนมัติได้

รายการ Avatar ของบัญชีและ guest เลือกได้ก่อน thumbnail โหลดเสร็จ ภาพตัวอย่างที่โหลดไม่สำเร็จไม่ปิดปุ่มเลือก เมื่อเปิดรายการจะเลื่อนกลับด้านบนและปรับ panel ให้พอดี safe area พร้อมพัก input เกมและซ่อน touch controls การโหลดโมเดล WebGL จาก MANGOs API origin ที่ตั้งค่าไว้ส่ง Cookie SSO ส่วนโมเดลสาธารณะภายนอกยังใช้ anonymous CORS ฝั่ง API ยังต้องอนุญาต credentialed CORS ของเว็บที่ deploy

**MANGOs Framework > Avatar > Export Animation Audit** เขียน `Library/MangosAnimationAudit.csv` รวมคลิปที่ฝังใน model assets ผลตรวจแรกพบ Humanoid 52 คลิปและไม่มี Transform position/scale curves Synthetic rigs ต่างสัดส่วนถูกเล่น animation 60 เฟรม และ synthetic VRM 1.0 นำเข้าพร้อม Humanoid ของตนเองที่ valid แล้ว Avatar บัญชีจริง cloth, spring bones, สัดส่วนสุดขั้ว และท่า interaction ทั้งหมดยังต้องตรวจภาพด้วยไฟล์จริง

## Controller อัตโนมัติ

ระบบตรวจอุปกรณ์อัตโนมัติและเลือก keyboard/mouse บน PC หรือ touch บนโทรศัพท์/tablet รวม iPad desktop user agent ส่วน PC จอสัมผัสยังใช้ desktop controls เอาเมนู Controls และปุ่มสลับ controller เดิมออก พร้อมล้าง override ที่บันทึกจาก build เก่า บน mobile การกดปุ่มเดินหรือใช้ mouse จะเลือก keyboard/mouse และการแตะหน้าจอจะกลับเป็น touch

Touch layout เปลี่ยนแนวตั้ง/แนวนอนได้โดยไม่ reload Joystick ซ้ายใช้เดิน ลากด้านขวาของ canvas หมุนกล้อง และ Run/Jump รองรับหลายนิ้วพร้อมกัน Fly/Up/Down แสดงเฉพาะผู้เล่นที่บินได้ ปุ่ม interaction ของ Unity ยังแตะได้ หากเริ่มลากบน Unity UI กล้องจะไม่รับ gesture นั้น Layout รวม safe-area offsets และย่อ voice bar บน mobile เพื่อเว้นที่ให้ปุ่มควบคุม

Input bridge เดียวกันขับทั้ง Starter Assets walking และ fly controller เดิม การหมุนจอ เปลี่ยนโหมด หลุด focus ยกเลิก pointer พิมพ์ใน browser chat หรือ disconnect จะล้างปุ่มที่ค้าง เมื่อ HTML field มี focus bridge จะปิด [Unity keyboard capture](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/WebGLInput-captureAllKeyboardInput.html) Touch UI แสดงหลังตั้ง local player แล้ว อนุญาตทั้งสองแนวหน้าจอและข้ามตัวบังคับ landscape เดิมสำหรับ adaptive WebGL controls

Browser automation ครอบคลุม PC, โทรศัพท์, Android tablet, iPad desktop UA และ PC จอสัมผัส ตรวจ joystick/look/jump พร้อมกัน หมุนจอขณะกด joystick, focus, release, automatic input switching และระยะห่าง voice bar การจำลองเหล่านี้ไม่แทนการเล่นบน iOS/Android จริง

## ขอบเขตการยืนยันผล

รัน Unity C# compilation และ Build ตรวจ Linux IL2CPP/WebGL แล้ว และเปิด Linux binary ใน WSL แล้ว การตรวจ lifecycle ของ browser overlay ใช้ mock room จึงไม่ยืนยัน microphone/media สด account cookies หรือ production TURN ต้องทดสอบอุปกรณ์ iOS/Android จริงและซ้อม production deployment ก่อนรับรอง release

แก้ ScriptUpdater exit 130 ที่เกิดจาก Rigidbody API เดิมเมื่อ 17 ตุลาคม 2026 โดยย้ายไปใช้ `linearVelocity` แล้ว `Tools/BuildBoth.ps1` ตรวจ log ของทุก phase ว่ามี ScriptUpdater failure หรือ C# error หรือไม่ รวม phase ที่ออกเพื่อสลับ platform ส่วน pipeline ปฏิเสธ BuildReport ที่มี error แม้ result ระบุ Succeeded ให้ตรวจ `build-diagnostics.txt` และ Editor log ด้วย ดูคำเตือนที่เหลือและข้อผิดพลาดของ Build เก่าใน [ผลตรวจ](VALIDATION.md) และยังต้องแยกคำเตือน GPU ของ headless browser ออกจาก shader/material ที่แสดงผลเสียจริง

## ตรวจภาพ WebGL ซ้ำ

สคริปต์ [Tools/Validation/check-integration-webgl.cjs](../Tools/Validation/check-integration-webgl.cjs) ใช้ชื่อ guest **PoppyValidation** เป็นค่าเริ่มต้น ชื่อนี้ใช้เฉพาะ QA ไม่เปลี่ยนชื่อบัญชีหรือบังคับชื่อผู้เล่นในเกม สามารถแก้ fixture ผ่าน environment variable `MANGOS_VALIDATION_GUEST_NAME`

ต้องมี Node.js, package `playwright` ที่ Node resolve ได้ และ Microsoft Edge ที่ Playwright เปิดด้วย `channel: 'msedge'` ให้เปิด Server จาก **local validation build** ที่ 7788/WS ก่อน โดย Client กับ Server ต้องเป็นชุดเดียวกัน ไม่มีการสั่ง Build ใหม่หรือ deploy อัตโนมัติจากสคริปต์นี้

จาก project root เรียกโดยแทน path ให้ตรงชุด Build ในเครื่อง:

```powershell
node Tools/Validation/check-integration-webgl.cjs 'C:\path\to\Builds\validation\<build-id>'
```

สคริปต์ให้บริการ Client ชั่วคราวที่ `http://127.0.0.1:8767/` เข้า guest และถ่าย desktop 1280×800 รวมผล JSON ลง `Builds/Validation/Browser/` ระบุ output directory อื่นเป็น argument ที่สองได้ ใช้ mock voice และจำลอง account API เป็น logged-out จึงไม่ตรวจบริการเสียง/account จริง

ผลตรวจรวม page identity, เนื้อหาเริ่มต้น, การเชื่อมต่อ FishNet, ชื่อ guest ใน server response, world/controller readiness และ runtime exceptions ให้เปิดภาพตรวจชื่อที่มุมขวาบนด้วยก่อนนำไปเป็นหลักฐานในเอกสาร


## ตรวจ regression ของบัญชีและ controller

ใช้ `node Tools/Validation/check-controller.cjs` เพื่อตรวจ controller ห้ารูปแบบโดยไม่เปิด Unity และใช้คำสั่งนี้กับคู่ Server/Client validation ที่ตั้ง authentication mode เป็น FirstPartyCookieSso เฉพาะสำเนาทดสอบ:

```powershell
node Tools/Validation/check-client-regressions.cjs <build-directory> <humanoid-glb-fixture> [evidence-directory]
```

สคริปต์จำลองบัญชีและ API รวมCasualPop จาก URL จริง โมเดลจำลองที่ส่ง cookie และไฟล์ที่ตอบ 404 ตรวจ guest/account, cookie mode ของการโหลดไฟล์, การเลือกก่อนภาพโหลด, การเปิด/ปิดรายการ, โทรศัพท์/tablet แนวตั้ง/แนวนอน และกล้องหลัง touch → keyboard ต้องกดคลิกขวาค้างจึงหมุนกล้องได้ ผลทดสอบนี้ไม่ยืนยัน cookie หรือโมเดลของบัญชีจริง และไม่เปลี่ยน production auth config
