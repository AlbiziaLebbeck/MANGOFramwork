# MANGOs Framework

[ภาษาไทย](README_TH.md) | [English](README.md) | [เอกสารทั้งหมด / All documentation](Documentation/README.md)

Framework เกมหลายผู้เล่นสำหรับ **WebGL Client** และ **Linux Dedicated Server** ใช้ **Unity 6.3 LTS — 6000.3.26f1** และ **Universal Render Pipeline (URP)**

รุ่นปัจจุบันอยู่บน branch **`MANGOsFramework_U6`** โดยคืนระบบ Avatar และ Controller จาก **`MANGOFramework_Cookie`** ตามคำขอของผู้ใช้แล้ว

**[คู่มือใช้งาน](Documentation/HANDOFF_TH.md)** · [การ Build และ runtime](Documentation/BUILD_AND_RUNTIME_TH.md) · [ผลตรวจและข้อจำกัด](Documentation/VALIDATION.md)

## ความสามารถ

- FishNet multiplayer พร้อม Bayou/SimpleWeb WebSocket สำหรับ WebGL และ Tugboat สำหรับ native networking
- **Build Center** สร้าง Server, Client หรือทั้งคู่ แยก network profile และเลือก platform ให้อัตโนมัติ
- Browser voice overlay จาก `html-voice-tester` รวมใน WebGL template และผูกกับ world ปัจจุบัน
- ระบบ Avatar ของ Cookie พร้อม reference rigs, cache และ UI เลือกเดิม
- Controller ของ Unity แบบ Cookie สำหรับเดิน click-to-move, touch และบิน พร้อมปุ่มสลับเดิม
- TextMeshPro shaders สำหรับ Unity 6

## สิ่งที่ต้องติดตั้งและการเริ่มต้น

1. ติดตั้ง **Unity 6000.3.26f1** ผ่าน Unity Hub พร้อม **WebGL Build Support** และ **Linux Dedicated Server / IL2CPP build support** สำหรับ Server
2. ติดตั้ง Git และให้เรียกผ่าน `PATH` ได้ เพราะ Unity packages หลายตัวมาจาก Git
3. Clone branch นี้:

   ```bash
   git clone --branch MANGOsFramework_U6 https://github.com/AlbiziaLebbeck/MANGOFramwork.git
   ```

4. เพิ่มโฟลเดอร์ใน Unity Hub และเปิดด้วย **6000.3.26f1**
5. รอ Package Manager restore และ import ให้เสร็จ ให้ **Bootstrap** เป็น Scene แรกที่เปิดใช้งานสำหรับ Build

รุ่น packages และ Git revisions อยู่ใน [manifest.json](Packages/manifest.json) และ [packages-lock.json](Packages/packages-lock.json) เครื่องมือที่เก็บ source ในโปรเจกต์ เช่น FishNet, Starter Assets และ ParrelSync อยู่ใต้ `Assets/Plugins`

| Dependency | รุ่น / แหล่งที่มา | หน้าที่ |
|---|---|---|
| URP | 17.3.0 | Rendering |
| Input System | 1.20.0 | Input ผู้เล่น |
| Cinemachine | 2.10.7 | กล้อง |
| glTFast | 6.4.0 ตรึง Git revision | โหลด GLB/glTF ขณะเล่น |
| UniVRM / UniGLTF | 0.128.0 ตรึง Git tag | package ที่ติดตั้งอยู่; runtime ที่คืนมาใช้ glTFast แบบ Cookie |
| Linux SDK และ Windows-to-Linux toolchain | packages 1.1.0 | Build Linux IL2CPP จาก Windows |
| FishNet + Bayou / Tugboat | Source ในโปรเจกต์ | Multiplayer transport และ synchronization |

## Build Server และ Client

เปิด **MANGOs Framework > Build Center** แก้ **Client Path** แล้วเลือก **Build Server**, **Build Client** หรือ **Build Both** ระบบเลือก platform รวม browser voice overlay และคืนค่า project settings ชั่วคราวเมื่อเสร็จ

ค่าของ Server และ Client แก้ได้แยกกัน:

| ค่า | Server เริ่มต้น | Client เริ่มต้น |
|---|---|---|
| Target | Linux Dedicated Server, IL2CPP, `.x86_64` | WebGL |
| Port | **7770** | **443** |
| WSS | **ปิด** | **เปิด** |

Client Host เริ่มต้นคือ `multiplayer-plus.mangosgo.com` และ Client Path คือ `/systemtester` ตาม deployment เดิม หากเว้น Host ว่างจะใช้ hostname ของหน้า WebGL **Client Path คือ path ของ WebSocket endpoint**

กด **Save Profile** เพื่อบันทึกใน [ProjectSettings/MangosBuildProfile.json](ProjectSettings/MangosBuildProfile.json) และ **Reset Network Defaults** เพื่อคืนค่าเครือข่ายโดยชัดแจ้ง การ Build ไม่ reset ค่าที่บันทึกเอง

แต่ละงานที่สำเร็จสร้าง:

```text
Builds/<deployment>/<build-id>/
  Server/
  Client/
  Deploy/
  build-manifest.json
```

`Builds/last-successful.json` ระบุ output ล่าสุดที่ครบ งานล้มเหลว/ยกเลิกไม่แทนที่ pointer นี้ Build outputs, local logs และ Unity caches สร้างในเครื่องและไม่เก็บใน Git

### Build Both ด้วย command line บน Windows

เรียกจาก project root:

```powershell
.\Tools\BuildBoth.ps1
```

ตัวเลือกเพิ่มเติม:

```powershell
.\Tools\BuildBoth.ps1 -Unity 'C:\path\to\Unity.exe' -Profile 'C:\path\to\profile.json'
```

ตัวสั่งงานเปิด Unity ใหม่ด้วย platform ที่ถูกต้องและทำงานเดิมต่ออัตโนมัติ log อยู่ใน `Builds/Logs` ดู batch entry points แยกใน [คู่มือ Build](Documentation/BUILD_AND_RUNTIME_TH.md)

## Deployment

ค่าเริ่มต้นใช้ reverse proxy รับ **WSS ที่ 443** แล้วส่ง WebSocket ต่อเข้า **WS Server ที่ 7770** ตัวอย่าง Nginx อยู่ใน `Deploy/` ต้องตั้ง hostname, certificate และ document root จริงก่อนใช้

- Deploy **โฟลเดอร์ Server ทั้งชุด** รวม `_Data`, `GameAssembly.so` และ `UnityPlayer.so` บน Linux เรียก `sh start-server.sh`
- ให้บริการ **โฟลเดอร์ Client ผ่าน HTTPS** พร้อม MIME type ของ JavaScript และ WebAssembly
- หากเปิด Server WSS ให้เตรียม PFX บน deployment host ใช้ `MANGOS_TLS_CERTIFICATE` แทน certificate path ได้ รหัสผ่านอ่านจาก environment variable ใน profile ซึ่งเริ่มต้นเป็น `MANGOS_TLS_PASSWORD`

ระบบไม่ฝัง certificate หรือรหัสผ่านลง WebGL เครื่องมือ Build สร้าง output และตัวอย่าง config ส่วนการนำไปใช้กับระบบจริงเป็นขั้นตอนแยก

## Browser voice และบริการบัญชี

WebGL template รวม `voice-app.js` และ `voice-overlay/` ให้อัตโนมัติ และถอด Unity `VoiceCommunication` เดิมแล้ว

Overlay รอให้เข้า world ก่อนอนุญาต join voice room key รวม deployment namespace กับ world id ของ Unity การเปลี่ยนโลก/disconnect ออกจาก media session เก่า ผู้เล่นต้องกดเข้าร่วมและอนุญาตไมโครโฟนด้วยตนเอง

Account API, voice/media backend, Socket.IO และ ICE/TURN ยังเป็นบริการภายนอก public endpoints อยู่ใน [voice-overlay/config.js](Assets/WebGLTemplates/MANGOsPWA/voice-overlay/config.js) Production ต้องรองรับ HTTPS, account cookies, credentialed CORS และ media/TURN ที่ใช้งานได้ ห้ามใส่ account tokens รหัสผ่าน certificate หรือ private credentials ในไฟล์ Build สาธารณะ

## Avatar และ animation

คืนระบบ Avatar จาก `MANGOFramework_Cookie` แล้ว: glTFast โหลดโมเดล ตัว loader เลือก Humanoid Avatar อ้างอิงจาก `Resources/AvatarLoader` ตามโครงสร้าง node และใช้ Animator/controller เดิมบน root ของผู้เล่น คืน cache โมเดล การสร้าง thumbnail หน้าต่างเลือก Avatar และ cooldown ห้าวินาที รวมถึงจุดเชื่อม network การนั่งและ interaction เดิม

การรองรับเหมือนระบบ Cookie และขึ้นกับ rig อ้างอิงที่ระบบเดิมรู้จัก ถอดระบบสร้าง Humanoid รายโมเดล, Rig Profiles, importer เฉพาะ VRM, การปรับความสูงอัตโนมัติ และเมนู audit animation ที่เพิ่มใหม่ออกแล้ว เส้นทาง VRM/reference rig แบบเดิมยังอยู่ แต่ไม่ได้รับประกัน GLB/VRM ทุก skeleton


## Controller จาก Cookie

บน browser จอสัมผัส แถบเสียง HTML อยู่กึ่งกลางด้านล่างเพื่อเว้นพื้นที่ joystick เดิม

คืน controller ของ Unity แบบเดิม: Starter Assets, `PlayerMovementHandler`, `LocomotionManager`, mobile controls ของ Unity, ปุ่มสลับ controller, การเดิน/click-to-move และการบิน การตรวจอุปกรณ์ใช้ `CheckMobile`/`IsMobile` เดิม คืนพฤติกรรม cursor/pointer lock และ orientation guard ของ Cookie รวมข้อกำหนด landscape ตามที่ตั้งไว้ใน prefab

ถอด HTML controller, การสลับ input อัตโนมัติ และ camera/input router ที่เพิ่มใหม่ออกแล้ว โค้ด Avatar และ input ใช้การทำงานของ Cookie ส่วน browser bridge ที่เหลือใช้เชื่อมระบบ HTML voice แยกต่างหาก


## ผลตรวจและข้อจำกัด

เปิดด้วย Unity **6000.3.26f1** โค้ดการเคลื่อนที่ของ Cookie ปรับเฉพาะ `Rigidbody.velocity` เป็น `linearVelocity` ของ Unity 6 และคงชุดแก้ compatibility ของ FishNet/physics และ TMP ไว้ ตรวจเทียบ source กับโปรเจกต์ Cookie ในเครื่อง ตัว build runner ตรวจ resource Avatar, การเชื่อม Animator/controller, input maps, physics และค่าการ build

```powershell
.\Tools\BuildBoth.ps1 -Validation
```

Validation profile ใช้ localhost พอร์ต **7788**, WS ให้ build ด้วย production profile ก่อน deploy และนำ Client ใหม่ขึ้นทั้งโฟลเดอร์ รวม HTML/JS กับไฟล์ Unity ที่ตรงชุดกัน รุ่น cache ของ PWA เปลี่ยนสำหรับการคืนระบบครั้งนี้แล้ว

ภาพและผลตรวจ adaptive controller/rig รายโมเดลเดิมในเอกสารเป็นประวัติ ไม่ใช่ runtime ที่คืนมา ดู [ผลตรวจ](Documentation/VALIDATION.md) สำหรับผล rollback ปัจจุบันและข้อจำกัด ส่วน cookie บัญชีจริง เสียง/TURN และโทรศัพท์/tablet จริงต้องตรวจใน deployment


## เอกสารและประวัติ migration

- [เอกสารภาษาไทยและภาษาอังกฤษทั้งหมด](Documentation/README.md)
- [คู่มือใช้งาน](Documentation/HANDOFF_TH.md)
- [การ Build และ runtime](Documentation/BUILD_AND_RUNTIME_TH.md)
- [ผลตรวจและข้อจำกัด](Documentation/VALIDATION.md)
- [แผน migration เดิม](Documentation/MIGRATION_PLAN_TH.md)

ฐาน Unity 2022 เดิมยังอยู่ใน Git history ที่ commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`
