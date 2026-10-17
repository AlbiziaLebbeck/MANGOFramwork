# ผลตรวจการปรับ MANGOs Framework

> Runtime ปัจจุบันคืน Avatar และ Controller จาก `MANGOFramework_Cookie` ตามคำขอของผู้ใช้ ผล adaptive controller/own-rig ด้านล่างเก็บเป็นประวัติและถูกแทนที่แล้ว รอบ rollback ตรวจด้วย `MangosCookieRestoreValidation.Run` และ build Unity 6 Server/WebGL ใหม่ การรองรับ rig และพฤติกรรม controller/orientation ใช้ตาม Cookie ข้ออ้างเดิมเรื่อง mobile/VRM/input อัตโนมัติไม่ใช่พฤติกรรมของ source ปัจจุบัน

ผลตรวจ rollback ผ่านบน Unity 6000.3.26f1: งาน Server/WebGL สุดท้าย `20261017-035242-ee29e9` ไม่มี C#/API Updater error ตรวจ resource Avatar/controller, input maps, prefab และเทียบ source กับ Cookie แล้ว เข้า guest และบัญชีจำลองที่ใช้ CasualPop GLB สาธารณะจริงใน Edge ได้ ทดสอบเลือก Avatar อื่นแล้ว Submit และปุ่มสลับ controller ของ Unity แล้ว ตรวจจอโทรศัพท์ 844×390 กับ 390×844 และคืน landscape guard เดิม แถบเสียง HTML อยู่กึ่งกลางบนจอสัมผัสเพื่อเว้น joystick เดิม Browser plugin ไม่พร้อมใช้งาน จึงใช้ Playwright/Edge ยังไม่ได้ตรวจ cookie บัญชี production เสียง/TURN สด อุปกรณ์จริง หรือเปลี่ยน deployment

[ภาษาไทย](VALIDATION.md) | [English](VALIDATION_EN.md) | [เอกสารทั้งหมด / All documentation](README.md)

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
| Controller | ทดสอบ Edge/Chromium จำลอง PC, iPhone, Android tablet, iPad desktop UA และ PC จอสัมผัส ผ่าน ตรวจ joystick+look+jump พร้อมกัน, หมุนจอขณะกด, focus/blur, keyboard auto mode, automatic input switching และ cleanup เมื่อออกจากเกม |
| Input ฝั่ง Unity | ตรวจการส่งแกนเดินและกล้องไป controller, ปุ่มกระโดด/บินหนึ่งครั้งต่อการกด, sprint/ขึ้นลง และการล้าง input เมื่อเปลี่ยนแนวหรือพิมพ์ข้อความ |
| Humanoid | สร้าง Avatar แยกตามโมเดล ทดสอบสองขนาด, A-pose และทิศทางกลับด้าน เล่น animation 60 เฟรม ตรวจว่ากระดูกถูก animation ขยับจริงและความยาวแขนไม่เปลี่ยน พร้อมปฏิเสธโมเดลไม่มี rig |
| GLB ตัวอย่างจริง | `RajPattern_Blue.glb` จาก MANGOs: พบและแก้กรณีแขนไขว้จากการใช้ท่าพักเป็น T-pose ตรวจตำแหน่งมือซ้าย/ขวาหลัง retarget ใน Editor และตรวจภาพยืน/เดินจาก WebGL หลังโหลด GLB สำเร็จ HTTP 200 |
| VRM | นำเข้า synthetic VRM 1.0 ด้วย raw skeleton และตรวจ Animator/Avatar ที่ valid ผ่านใน Editor เส้นทาง UniVRM รวมใน native builds แล้ว |
| .anim | ตรวจ 52 คลิป เป็น Humanoid และไม่พบ Transform position/scale curve ที่ใช้ยืดกระดูกโดยตรง มีเมนูส่งออก CSV สำหรับตรวจต่อ |
| Shader / UI | ปรับ TextMeshPro shader จาก Essential Resources ของ Unity 6 โดยคง GUID เดิม แก้ตัวอักษรเป็นสี่เหลี่ยม ตรวจภาพ Launcher และ world ทั้งแนวตั้ง/แนวนอนแล้ว |

ภาพใน [Validation/](Validation/) มาจาก Client ที่ Build จริง ส่วน [browser-summary.json](Validation/browser-summary.json) ระบุ build id และผลที่บันทึกไว้ ชื่อ guest คือ **PoppyValidation** โดยถ่ายภาพ desktop, portrait และ landscape ใหม่ด้วยชื่อนี้ เป็นข้อมูลทดสอบ ไม่ใช่บัญชี production หรือชื่อผู้เล่นเริ่มต้นที่บังคับใช้

## ขอบเขตที่ยังไม่ได้ยืนยันกับระบบจริง

- ยังไม่ได้เล่นบนเครื่อง iPhone/iPad/Android จริง การจำลองทั้งหมดใช้ Chromium ไม่ใช่ Safari บนอุปกรณ์จริง
- ยังไม่ได้ทดสอบเสียงสดด้วยบัญชีของผู้ใช้, ไมโครโฟนจริง, cookie/CORS ของ production และ TURN/คุณภาพเครือข่าย
- ตรวจ GLB ตัวอย่างสาธารณะหนึ่งตัว และ synthetic VRM 1.0 ไม่ได้ตรวจ Avatar ทุกตัวในบัญชี หรือ VRM 0.x/1.0 จริงทุกชนิด ต้องตรวจ spring bones, cloth, extreme proportions และท่านั่ง/ปฏิสัมพันธ์ด้วยโมเดลจริงเพิ่มเติม
- ไม่สามารถรับประกันว่า GLB ที่ไม่มี Humanoid skeleton หรือมี skin weights/bind pose ผิดจะใช้ animation เดิมได้ทุกตัว มี Rig Profile สำหรับแก้ mapping และมี fallback เมื่อโหลดไม่ผ่าน
- เส้นทางหลักของ Build ถูกทดสอบผ่าน batch runner ปุ่มและการสลับ platform แบบ GUI ใน Editor ยังไม่ได้คลิกตรวจบนหน้าต่างจริง
- ไม่มีการ deploy หรือเปลี่ยน reverse proxy ของระบบจริง การเผยแพร่ source ผ่าน Git แยกจากการยืนยัน deployment และ runtime

## Diagnostics ที่ยังมี

Log ของ Build เดิมวันที่ 16 ตุลาคมมี Mono/Cecil `Argument null or empty (name)` จาก ScriptUpdater แม้ BuildReport สำเร็จ เก็บ log เก่าไว้เป็นหลักฐาน ส่วนการแก้วันที่ 17 ตุลาคมด้านล่างถอดการเรียก Rigidbody API เก่าที่เป็นตัวกระตุ้นแล้ว ผล Build ซ้ำไม่มี ScriptUpdater หรือ C# error คำเตือนอื่นที่อธิบายไว้ยังคงมีอยู่

Headless browser มีคำเตือน shader ภายในบางตัวและการคลาย ASTC texture ที่ GPU จำลองไม่รองรับ ตรวจภาพแล้วว่าฉาก, GLB ตัวอย่าง และข้อความหลักแสดงได้ แต่ยังต้องตรวจ GPU/เครื่องเป้าหมายจริง ส่วนคำเตือนเสียงที่ยังโหลดไม่ครบและ JobTempAlloc ตอนปิด Editor ก็ยังปรากฏใน log

## วิธีตรวจซ้ำ

- `Tools/BuildBoth.ps1 -Validation` รัน checks ฝั่ง Editor และสร้างคู่ local validation ที่ 7788/WS
- `MangosAvatarValidation.Run` ตรวจ synthetic Humanoid/VRM และ animation audit
- `MangosRealAvatarValidation.Run` พร้อม `-mangosAvatarFixture <path-to-glb>` ตรวจ fixture Humanoid จริง โดยไม่ดาวน์โหลดหรือ bundle โมเดลให้อัตโนมัติ
- [สคริปต์ WebGL integration ที่นำไปรันซ้ำได้](../Tools/Validation/check-integration-webgl.cjs) ใช้ชื่อ **PoppyValidation** เริ่มต้น ดูวิธีใช้ใน [คู่มือ Build/runtime](BUILD_AND_RUNTIME_TH.md)
- สคริปต์ browser QA ชุดเดิมและ log เต็มอยู่ในโฟลเดอร์ `validation` ของ workspace `html-voice-tester-main`

การถ่ายภาพรอบนี้ไม่มี Browser plugin/skill ชื่อ browser จึงใช้ Playwright/Edge workflow เดิม เส้นทางตรวจคือเปิด WebGL local → กดเริ่ม → เข้า guest ชื่อ PoppyValidation → BaseWorld → ถ่าย desktop/portrait/landscape ระบบเสียงใช้ mock รอบนี้จึงไม่เพิ่มข้ออ้างว่าผ่านบริการสดจากขอบเขตข้างต้น

## แก้ Unity 6 API Updater — 17 ตุลาคม 2026

เปิดโปรเจกต์แล้วทำซ้ำ ScriptUpdater exit 130 ของ FishNet.Runtime และ Assembly-CSharp ได้ จุดเรียก `Rigidbody.velocity` / `Rigidbody2D.velocity` เดิมสร้าง UnityUpgradable warnings แล้วตัว updater ล้มเหลวขณะ resolve metadata ของสมาชิกทดแทนผ่าน Mono.Cecil เป็นการย้าย API ที่ยังไม่ครบ ไม่ใช่ DLL ของ FishNet หาย

แก้เจ็ดไฟล์ให้ใช้ [Rigidbody.linearVelocity](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody-linearVelocity.html) และ [Rigidbody2D.linearVelocity](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody2D-linearVelocity.html) ของ Unity 6 รวมการเก็บ/คืน prediction state และ controller เดิน/บิน ชื่อ serialized fields และลำดับ packet ของ FishNet คงเดิม ส่วน CharacterController.velocity ยังเป็น API ที่ถูกต้องจึงไม่เปลี่ยน API Updater ยังเปิดใช้งาน ไม่ซ่อนหรือ suppress error

การตรวจ BuildReport ที่เข้มขึ้นพบอีกสาม error จาก TextMeshPro พยายามเปิดหน้าต่างนำเข้า resource ระหว่าง headless build เพราะ TMP Settings ยังเป็น schema เก่าแม้ปรับ shader แล้ว จึงย้ายตาม TMP Essential Resources ของ Unity 6 ที่ติดตั้งเป็น schema version 2 โดยคงค่าที่มีอยู่ font/sprite/style references และ asset GUID ทั้งหมด เพิ่มการตรวจ schema/default font ร่วมกับ physics checks แล้ว ผล Build ซ้ำไม่มี error จากหน้าต่าง importer นี้

ผลตรวจ: คอมไพล์ Editor ด้วย Unity 6000.3.26f1 ผ่าน แล้ว Build Linux IL2CPP Server และ WebGL ในงาน `20261016-172024-192144` ผ่านทั้งคู่ โดย BuildReport errors=0 และไม่มี ScriptUpdater/C# error `MangosUnity6PhysicsValidation.Run` ผ่านการตรวจ 3D/2D state capture, restore, angular state และ prediction velocity โดยเรียกก่อน Build ของ `Tools/BuildBoth.ps1 -Validation` และเปิดผ่าน **MANGOs Framework > Validation > Unity 6 Physics** ได้ด้วย ตรวจ failure guard ของตัวสั่ง Build กับ log เดิมแล้วว่าปฏิเสธ log ที่มี error ได้

ทดสอบในสำเนาแยกเพื่อรักษา Editor ที่ผู้ใช้เปิดและ project settings ที่แก้ไว้ Profile deployment เริ่มต้นยังเป็น 7770/WS และ 443/WSS ส่วน Build รอบนี้ใช้ local validation 7788/WS การแก้นี้ตรวจ compile/build และ physics state ไม่ใช่การทดสอบเสียงสดหรือมือถือจริงรอบใหม่ ดู build id, log hashes และ source hashes ใน [หลักฐานแบบ JSON](Validation/unity6-api-updater-fix.json)

หลังอัปเดตขณะที่ Editor เปิดอยู่ ให้กด **Assets > Refresh** แล้วรอคอมไพล์เสร็จ จากนั้นล้างรายการ Console เดิมเพื่อดูผลรอบใหม่ รายการจากก่อนแก้ source ไม่หายเอง การเปิดโปรเจกต์ครั้งใหม่จะใช้ source ที่แก้แล้ว ยังอาจมี deprecation/package/shader warnings อื่น ต้องตรวจต่อหากมี error ใหม่


## แก้ Avatar บัญชีและ controller อัตโนมัติ — 17 ตุลาคม 2026

เดิมการเปิดเมนู Avatar ต้องรอ thumbnail ทุกโมเดล จึงอาจซ่อนเมนูทั้งชุดเมื่อโมเดลของบัญชีโหลดช้าหรือล้มเหลว ตอนนี้รายการเลือกใช้ได้ทันที สร้าง thumbnail เฉพาะรายการที่มองเห็น ถ้าภาพเสียยังเลือกได้และแสดงชื่อไฟล์ เมื่อเปิดรายการจะเลื่อนกลับด้านบน ปรับ panel ให้พอดี safe area พัก input เกมและซ่อน touch controls ชั่วคราว การโหลด binary จาก MANGOs API origin ส่ง Cookie SSO ส่วนโมเดลสาธารณะภายนอกใช้ anonymous CORS ตามเดิม

เอาเมนู Controls และปุ่มสลับเดิมออก ล้าง override เก่า แล้วเลือก controller อัตโนมัติสำหรับ desktop โทรศัพท์ tablet iPad desktop UA แนวหน้าจอ และ keyboard/mouse ที่ต่อเพิ่ม Desktop หมุนกล้องจาก browser pointer event ระหว่างกดคลิกขวาค้าง การได้ focus ของ canvas ไม่ยกเลิก drag แรกอีกต่อไป และ Starter Assets หยุดเขียนมุมกล้องทับ browser controller ใน LateUpdate ทำให้ touch และ mouse ที่ต่อกับ mobile ควบคุมกล้องได้

[CasualPop.glb ที่ผู้ใช้ส่ง](https://www.mangosgo.com/avatar/CasualPop.glb) ผ่านการตรวจ Humanoid และ animation 60 เฟรมใน Editor ชุดทดสอบ browser ใช้ URL สาธารณะนี้เป็น default ของบัญชีจำลอง และใช้ไฟล์ที่ดาวน์โหลดเดียวกันเป็น fixture ของ API ตัวบัญชีและ API เป็น mock จึงไม่ยืนยัน cookie/session production ของผู้ใช้ ไม่ได้รวมไฟล์โมเดลเข้า Git

รันทดสอบซ้ำด้วย `Tools/Validation/check-controller.cjs` และ `Tools/Validation/check-client-regressions.cjs` ดู [คู่มือ Build/runtime](BUILD_AND_RUNTIME_TH.md) ผลตรวจแบบอ่านด้วยโปรแกรมและภาพล่าสุดอยู่ใน [Validation/client-regressions.json](Validation/client-regressions.json) ผล QA รอบกลางที่ไม่ผ่านเก็บใน workspace และไม่นับเป็นหลักฐาน release สำเนาทดสอบใช้ 7788/WS และ override Cookie SSO แยกจากโปรเจกต์จริง โดยคง scene, auth asset และค่าการ deploy ที่ผู้ใช้แก้ไว้

ผลตรวจสุดท้าย: Build `20261017-023307-2194a9` ผ่านทั้งสอง target โดยไม่มี build/C#/ScriptUpdater error ผ่านครบสี่กรณี: guest desktop, บัญชี desktop (รวมคลิกเลือก/Submit จริง), บัญชีบนโทรศัพท์ และบัญชีบน tablet การลากกล้อง touch หมุนจอ สลับ keyboard/mouse และกล้องที่ต้องกดคลิกขวาค้างผ่านบน mobile contexts ส่วน HTTP 401 ของ guest และ 404 ของ thumbnail เสียเป็น fixture ที่ตั้งใจสร้าง

![รายการ Avatar ของบัญชีจำลอง](Validation/account-desktop-avatars.png)

![บัญชีจำลองบนโทรศัพท์ ซ่อน touch controls ขณะเลือกรายการ](Validation/account-phone-avatars.png)
