# MANGOs Framework

[English](README.md) | [ภาษาไทย](README_TH.md) | [All documentation / เอกสารทั้งหมด](Documentation/README.md)

Multiplayer Unity framework for a **WebGL client** and **Linux Dedicated Server**, built with **Unity 6.3 LTS — 6000.3.26f1** and **Universal Render Pipeline (URP)**.

This version is maintained on the **`MANGOsFramework_U6`** branch.

[Getting started](Documentation/HANDOFF_EN.md) · **[คู่มือใช้งานภาษาไทย](Documentation/HANDOFF_TH.md)** · [Build and runtime guide](Documentation/BUILD_AND_RUNTIME.md) · [Validation results and limitations](Documentation/VALIDATION_EN.md)

## Features

- FishNet multiplayer with Bayou/SimpleWeb WebSocket transport for WebGL and Tugboat for native networking.
- **Build Center** for Server, Client, or Both, with independent network profiles and automatic platform selection.
- Browser voice overlay from `html-voice-tester`, included automatically in the WebGL template and linked to the current game world.
- Runtime GLB and VRM avatar loading with each model's own Humanoid rig, GLB T-pose calibration, and animation/controller rebinding.
- Adaptive keyboard/mouse and touch controls for PC, phones, and tablets, including portrait/landscape changes during play.
- Unity 6 TextMeshPro shaders and an animation-audit export tool.

## Requirements and setup

1. Install **Unity 6000.3.26f1** through Unity Hub, including **WebGL Build Support** and the **Linux Dedicated Server / IL2CPP build support** needed for the Server target.
2. Install Git and make it available on `PATH`; several Unity packages are resolved from Git repositories.
3. Clone this branch:

   ```bash
   git clone --branch MANGOsFramework_U6 https://github.com/AlbiziaLebbeck/MANGOFramwork.git
   ```

4. Add the cloned folder in Unity Hub and open it with **6000.3.26f1**.
5. Allow Unity Package Manager to restore packages and finish importing assets. Keep **Bootstrap** as the first enabled build scene.

Package versions and Git revisions are recorded in [Packages/manifest.json](Packages/manifest.json) and [Packages/packages-lock.json](Packages/packages-lock.json). Vendored tools such as FishNet, Starter Assets, and ParrelSync are included under `Assets/Plugins`.

| Dependency | Version / source | Purpose |
|---|---|---|
| URP | 17.3.0 | Rendering |
| Input System | 1.20.0 | Player input |
| Cinemachine | 2.10.7 | Cameras |
| glTFast | 6.4.0, pinned Git revision | Runtime GLB/glTF loading |
| UniVRM / UniGLTF | 0.128.0, pinned Git tag | VRM loading |
| Linux SDK and Windows-to-Linux toolchain | 1.1.0 packages | Linux IL2CPP builds from Windows |
| FishNet + Bayou / Tugboat | Vendored source | Multiplayer transport and synchronization |

## Build Server and Client

Open **MANGOs Framework > Build Center**, change **Client Path**, and select **Build Server**, **Build Client**, or **Build Both**. The build process selects the platforms, packages the browser overlay/controller, and restores temporary project settings when it finishes.

Server and Client network settings remain independently editable:

| Setting | Server default | Client default |
|---|---|---|
| Target | Linux Dedicated Server, IL2CPP, `.x86_64` | WebGL |
| Port | **7770** | **443** |
| WSS | **Off** | **On** |

The initial Client Host is `multiplayer-plus.mangosgo.com`, and Client Path is `/systemtester`, preserving the existing deployment settings. A blank Client Host uses the WebGL page's hostname. **Client Path is the WebSocket endpoint path.**

Use **Save Profile** to persist changes in [ProjectSettings/MangosBuildProfile.json](ProjectSettings/MangosBuildProfile.json). **Reset Network Defaults** resets the network fields explicitly; building does not reset saved overrides.

Each completed job produces:

```text
Builds/<deployment>/<build-id>/
  Server/
  Client/
  Deploy/
  build-manifest.json
```

`Builds/last-successful.json` identifies the latest complete output. Failed or cancelled jobs do not replace that pointer. Build outputs, local build logs, and Unity caches are generated locally and excluded from Git.

### Command-line Build Both on Windows

From the project root, run:

```powershell
.\Tools\BuildBoth.ps1
```

Optional arguments:

```powershell
.\Tools\BuildBoth.ps1 -Unity 'C:\path\to\Unity.exe' -Profile 'C:\path\to\profile.json'
```

The runner restarts Unity with the correct platform and resumes the same build job automatically. Logs are written to `Builds/Logs`. Separate batch entry points are documented in the [build guide](Documentation/BUILD_AND_RUNTIME.md).

## Deployment

With the default settings, a reverse proxy accepts **WSS on 443** and forwards WebSocket traffic to the **WS Server on 7770**. A sample Nginx configuration is generated in `Deploy/`; configure the real hostname, certificate, and document root before applying it.

- Deploy the **entire Server directory**, including `_Data`, `GameAssembly.so`, and `UnityPlayer.so`. On Linux, run `sh start-server.sh`.
- Serve the **Client directory over HTTPS** with appropriate JavaScript and WebAssembly MIME types.
- If Server WSS is enabled, provide its PFX certificate on the deployment host. The certificate path can be overridden with `MANGOS_TLS_CERTIFICATE`; the password is read from the environment variable selected in the profile, defaulting to `MANGOS_TLS_PASSWORD`.

Certificates and password values are not embedded in WebGL. The build tools generate output and configuration examples; applying them to a live deployment is a separate step.

## Browser voice and account services

The WebGL template includes `voice-app.js` and `voice-overlay/` automatically. The original Unity `VoiceCommunication` implementation has been removed.

The overlay waits for a game world before allowing voice join. Voice room keys combine the deployment namespace with Unity's world id. Changing worlds or disconnecting leaves the previous media session. Joining voice requires the user's action and browser microphone permission.

Account API, voice/media backend, Socket.IO, and ICE/TURN services remain external dependencies. Their public endpoints are configured in [voice-overlay/config.js](Assets/WebGLTemplates/MANGOsPWA/voice-overlay/config.js). Production setup must support HTTPS, the intended account cookies and credentialed CORS, and working media/TURN connectivity. Do not put account tokens, certificate passwords, or private service credentials in public build files.

## Avatars and animation

Ordinary GLB avatars use glTFast and build a Humanoid Avatar from their own skeleton. The loader calibrates relaxed/A-pose exports into a T-pose and aligns the body basis before retargeting. VRM uses UniVRM and the model's own Humanoid Avatar/raw skeleton. Movement, flying, FishNet animation synchronization, and interactions are rebound to the active Animator.

Visual height is normalized uniformly to **1.7 m** by default to match the existing collider policy. Bone lengths, skin weights, and inverse bind matrices are not stretched to match a reference character.

For an unmapped Humanoid GLB, create an **Avatar Rig Profile** through **Assets > Create > MANGOs Framework > Avatar Rig Profile**. Assign it to AvatarLoader or place it under `Resources/AvatarRigProfiles` with a matching URL prefix. Invalid rigs retain the previous/default model and report `LastLoadError`.

Support depends on a valid Humanoid skeleton and skinning; arbitrary non-Humanoid GLB models cannot automatically use the existing Humanoid animations. Changing natural avatar heights also requires a consistent gameplay/collider policy.

Use **MANGOs Framework > Avatar > Export Animation Audit** to write `Library/MangosAnimationAudit.csv`.

## Adaptive controls

**Auto** selects keyboard/mouse on PC and touch controls on phones/tablets, including iPad's desktop user agent. A touchscreen PC remains in desktop mode. Players can override Auto with **Touch** or **Keyboard / mouse**.

Touch controls include a left movement joystick, right-side camera drag, Run, and Jump. Fly/Up/Down appear for flyable players. Layouts adapt to portrait/landscape and safe areas. Rotation, input-mode changes, focus loss, pointer cancellation, typing in an HTML field, and disconnect clear held input.

In Auto mode on mobile, a movement key or mouse switches to keyboard/mouse; touching the screen restores touch controls.

## Validation and known limits

Linux IL2CPP and WebGL builds have succeeded. A compiled WebGL client connected to a Linux Server in WSL, entered BaseWorld as a guest, and loaded a public GLB avatar. Browser tests cover five device configurations, simultaneous touch input, orientation changes, and controller overrides. Avatar checks include different sizes, T/A-pose calibration, a real GLB regression, and a synthetic VRM 1.0 import.

Physical iOS/Android playtests, live microphone/account/TURN verification, and broader real-world VRM/avatar pose checks are still required. The graphical Editor buttons have not been clicked in an automated UI test; the build pipeline has been exercised through its batch runner.

Unity's API Updater can emit Mono/Cecil diagnostics even when BuildReport succeeds. Inspect the Editor logs and each output's `build-diagnostics.txt`. The [validation report](Documentation/VALIDATION_EN.md) records these diagnostics and the limits of the tests.

To run local Editor checks and build the localhost validation profile on Windows:

```powershell
.\Tools\BuildBoth.ps1 -Validation
```

This profile uses port **7788**, WS, and localhost, so it is separate from the default deployment profile.

The screenshots use the guest name **PoppyValidation**. See the [build guide](Documentation/BUILD_AND_RUNTIME.md#repeat-the-webgl-screenshot-check) to repeat the capture.

![WebGL validation: PoppyValidation with a loaded GLB avatar](Documentation/Validation/integration-world-desktop.png)

## Documentation and migration history

- [All documentation in English and Thai](Documentation/README.md)
- [Getting started](Documentation/HANDOFF_EN.md)
- [คู่มือใช้งานภาษาไทย](Documentation/HANDOFF_TH.md)
- [Build and runtime details](Documentation/BUILD_AND_RUNTIME.md)
- [Validation evidence and limitations](Documentation/VALIDATION_EN.md)
- [Original migration plan](Documentation/MIGRATION_PLAN_EN.md)

The Unity 2022 baseline remains available in Git history at commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`.
