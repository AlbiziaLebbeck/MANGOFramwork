# MANGOs Framework

[English](README.md) | [ภาษาไทย](README_TH.md) | [All documentation / เอกสารทั้งหมด](Documentation/README.md)

Multiplayer Unity framework for a **WebGL client** and **Linux Dedicated Server**, built with **Unity 6.3 LTS — 6000.3.26f1** and **Universal Render Pipeline (URP)**.

This version is maintained on the **`MANGOsFramework_U6`** branch. Avatar and Controller behavior has been restored from **`MANGOFramework_Cookie`** at the user's request.

[Getting started](Documentation/HANDOFF_EN.md) · **[คู่มือใช้งานภาษาไทย](Documentation/HANDOFF_TH.md)** · [Build and runtime guide](Documentation/BUILD_AND_RUNTIME.md) · [Validation results and limitations](Documentation/VALIDATION_EN.md)

## Features

- FishNet multiplayer with Bayou/SimpleWeb WebSocket transport for WebGL and Tugboat for native networking.
- **Build Center** for Server, Client, or Both, with independent network profiles and automatic platform selection.
- Browser voice overlay from `html-voice-tester`, included automatically in the WebGL template and linked to the current game world.
- Original Cookie Avatar loader, reference rigs, model cache and selection UI.
- Original Unity walking, click-to-move, touch and fly controllers with their controller-switch UI.
- Unity 6 TextMeshPro shaders.

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
| UniVRM / UniGLTF | 0.128.0, pinned Git tag | Installed package; restored runtime uses Cookie glTFast path |
| Linux SDK and Windows-to-Linux toolchain | 1.1.0 packages | Linux IL2CPP builds from Windows |
| FishNet + Bayou / Tugboat | Vendored source | Multiplayer transport and synchronization |

## Build Server and Client

Open **MANGOs Framework > Build Center**, change **Client Path**, and select **Build Server**, **Build Client**, or **Build Both**. The build process selects the platforms, packages the browser voice overlay, and restores temporary project settings when it finishes.

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

The Avatar system is restored from `MANGOFramework_Cookie`: glTFast loads the model, the original loader selects a reference Humanoid Avatar from `Resources/AvatarLoader` by its node structure, and the existing player-root Animator/controller is reused. The original model cache, thumbnail generation, selection window and five-second selection cooldown are restored, together with network, sitting and interaction bindings.

Compatibility is the same as the Cookie system and depends on matching one of its supported reference rigs. The experimental per-model Humanoid builder, Rig Profiles, VRM-specific importer, automatic height normalization and animation-audit menu have been removed. The original VRM/reference-rig path remains; it is not a guarantee for arbitrary GLB/VRM skeletons.


## Cookie controls

On touch browsers, the HTML voice bar sits at the bottom centre to leave the original joystick clear.

The original Unity controller is restored: Starter Assets, `PlayerMovementHandler`, `LocomotionManager`, Unity mobile controls, the controller-switch button, walking/click-to-move and fly movement. Device detection uses the original `CheckMobile`/`IsMobile` browser bridge. Cursor/pointer-lock behavior and the orientation guard are restored from Cookie, including its configured landscape requirement.

The HTML controller, automatic input-mode switching and its custom camera/input router have been removed. Input and Avatar code now follow the Cookie implementation; the remaining browser bridge only integrates the separate HTML voice system.


## Validation and known limits

Open with Unity **6000.3.26f1**. The Cookie movement code only changes `Rigidbody.velocity` to Unity 6's `linearVelocity`; the earlier FishNet/physics and TMP compatibility fixes are retained. Source parity is checked against the local Cookie project, and the local build runner verifies reference Avatar resources, Animator/controller links, input maps, physics and build settings.

```powershell
.\Tools\BuildBoth.ps1 -Validation
```

The validation profile uses localhost, port **7788**, WS. Build with your production profile before deployment. Deploy the entire newly generated Client directory, including HTML/JS and its matching Unity files. The PWA cache generation is updated for this rollback.

Previous adaptive-controller/own-rig screenshots and results in the documentation are historical and do not describe the restored runtime. See [Validation](Documentation/VALIDATION_EN.md) for the current rollback checks and limitations. Live account cookies, voice/TURN and physical phones/tablets require deployment testing.


## Documentation and migration history

- [All documentation in English and Thai](Documentation/README.md)
- [Getting started](Documentation/HANDOFF_EN.md)
- [คู่มือใช้งานภาษาไทย](Documentation/HANDOFF_TH.md)
- [Build and runtime details](Documentation/BUILD_AND_RUNTIME.md)
- [Validation evidence and limitations](Documentation/VALIDATION_EN.md)
- [Original migration plan](Documentation/MIGRATION_PLAN_EN.md)

The Unity 2022 baseline remains available in Git history at commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`.
