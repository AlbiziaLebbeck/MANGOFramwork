# MANGOs Framework validation results

[English](VALIDATION_EN.md) | [ภาษาไทย](VALIDATION.md) | [All documentation / เอกสารทั้งหมด](README.md)

Date: 16 October 2026 — Unity 6000.3.26f1

Baseline: `e461cdcad0b5bf7191f903afc828ea248b296a6f` / Unity 2022.3.62f3

The delivered default-profile build, `Builds/mangos/20261016-161442-6eaf09`, succeeded for Server and Client using 7770/WS and 443/WSS. Its manifest is preserved in [Validation/default-build-manifest.json](Validation/default-build-manifest.json). This output is ready for deployment configuration; it does not establish that a production service is running correctly.

## Completed checks

| Area | Result and evidence |
|---|---|
| Unity 6 / FishNet / glTFast / IL2CPP | Linux Dedicated Server IL2CPP and WebGL builds succeeded. Unity 6 was retained; the upgrade-skip condition was not needed. |
| Build Both | Job `20261016-160736-2dab27` completed both targets using the automatic runner that restarts Unity with the correct platform. Manifests were produced and generated resource configuration was restored. |
| Networking | The Linux Server ran in WSL. The matching compiled WebGL Client connected through FishNet at `ws://localhost:7788/validation/game` and entered BaseWorld as a guest. |
| Build settings | Checked default 7770/WS and 443/WSS, independent overrides, persistence, rejection of invalid paths/ports, and Server WSS certificate requirements. |
| Transport | Fixed case-insensitive `Sec-WebSocket-Key` header reading and checked the handshake with an actual WebGL connection. |
| Voice overlay | Mock tests passed for waiting for a room, join, mute, room changes retaining mute, leave, rapid room changes, changes during join, and dispose. The actual Client supplied Unity's world id to the overlay. |
| Controller | Edge/Chromium emulation passed for PC, iPhone, Android tablet, iPad desktop UA, and touchscreen PC. Checks covered simultaneous joystick/look/jump, rotation while holding input, focus/blur, keyboard Auto mode, manual overrides, and cleanup after leaving. |
| Unity input | Checked movement/camera axes, one-shot jump/fly actions, sprint/vertical movement, and clearing input after orientation changes or text entry. |
| Humanoid | Built a separate Avatar per model. Tested two sizes, A-pose, reversed orientation, and 60 animation frames. Bones were animated and arm lengths remained constant; a model without a rig was rejected. |
| Real GLB example | MANGOs `RajPattern_Blue.glb`: identified and fixed crossed arms caused by treating the resting pose as a T-pose. Checked left/right hand positions after retargeting in Editor and standing/walking visuals in WebGL after HTTP 200 GLB loading. |
| VRM | A synthetic VRM 1.0 imported with its raw skeleton and valid Animator/Avatar in Editor. The UniVRM path is included in native builds. |
| .anim | Audited 52 Humanoid clips. No Transform position/scale curves directly stretching bones were found. A CSV export menu is available. |
| Shader / UI | Updated TextMeshPro shaders from Unity 6 Essential Resources while retaining GUIDs, fixing rectangular glyph rendering. Checked Launcher and world screenshots in portrait/landscape. |

The images under [Validation/](Validation/) come from the compiled Client. [browser-summary.json](Validation/browser-summary.json) records the build id and captured results. The guest name is **PoppyValidation**; desktop, portrait, and landscape screenshots were captured again using that name. It is a test identity, not a fixed production account or default player name.

## Not yet verified against real services/devices

- Physical iPhone/iPad/Android playtests have not run. All mobile emulation used Chromium, not Safari on a real device.
- Live voice with the user's accounts, physical microphones, production cookies/CORS, TURN, and network quality has not been tested.
- Checks cover one public GLB and a synthetic VRM 1.0, not every account avatar or real VRM 0.x/1.0 variant. Spring bones, cloth, extreme proportions, sitting, and interaction poses need additional real-model checks.
- GLB without a Humanoid skeleton, or with invalid skin weights/bind pose, cannot be guaranteed to use existing animations. Rig Profiles support mapping corrections and failures retain a fallback.
- The build pipeline was exercised through the batch runner. Editor GUI buttons and platform switching have not been clicked in an automated UI test.
- The live deployment and reverse proxy have not been changed. Publishing source through Git is separate from deployment/runtime verification.

## Remaining diagnostics

Unity API Updater prints Mono/Cecil `Argument null or empty (name)` while scanning some existing assemblies, even when BuildReport succeeds. These builds still have diagnostics; inspect `build-diagnostics.txt` in each output and logs in `Builds/Logs`.

The headless browser reports warnings for some internal shaders and software decompression of ASTC textures unsupported by its emulated GPU. Screenshots show the scene, sample GLB, and main text rendering, but real target GPUs/devices still need checks. Logs also include incomplete-audio-loading warnings and JobTempAlloc messages when Editor exits.

## Repeating the checks

- `Tools/BuildBoth.ps1 -Validation` runs Editor checks and builds the local 7788/WS pair.
- `MangosAvatarValidation.Run` checks synthetic Humanoid/VRM and the animation audit.
- `MangosRealAvatarValidation.Run -mangosAvatarFixture <path-to-glb>` checks a real Humanoid fixture without automatically downloading or bundling a model.
- The portable [WebGL integration script](../Tools/Validation/check-integration-webgl.cjs) uses **PoppyValidation** by default; see [Build and runtime](BUILD_AND_RUNTIME.md#repeat-the-webgl-screenshot-check).
- The original wider browser QA scripts and full logs are in the `validation` directory of the `html-voice-tester-main` workspace.

For this screenshot refresh, the Browser plugin/`browser` skill was not available, so the existing Playwright/Edge workflow was used. The flow was: local WebGL entry → start → guest login as PoppyValidation → BaseWorld → desktop/portrait/landscape capture. Voice used a mock service; this run does not extend the live-service claims above.
