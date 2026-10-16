# MANGOs build and runtime

Open this project with Unity **6000.3.26f1** on branch `MANGOsFramework_U6`. The original Unity 2022 baseline remains available in Git history at commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`.

## Build Center

Open **MANGOs Framework > Build Center**. Edit Client Path and press **Build Server**, **Build Client**, or **Build Both**.

Server and Client settings are independent and editable:

| Setting | Server default | Client default |
|---|---|---|
| Port | 7770 | 443 |
| WSS | off | on |

The initial host/path preserve the existing Bootstrap deployment: `multiplayer-plus.mangosgo.com`, `/systemtester`. A blank host uses the WebGL page's hostname. **Save Profile** persists settings in `ProjectSettings/MangosBuildProfile.json`; **Reset Network Defaults** explicitly resets them. Build does not silently reset overrides.

The default topology terminates TLS at the reverse proxy on port 443 and forwards WebSocket traffic to the Server on port 7770. Server WSS controls the Server's TLS listener; Client WSS controls the Client's connection scheme. If Server WSS is enabled, configure a PFX path on the deployment host. `MANGOS_TLS_CERTIFICATE` can override this path. The password comes from the environment variable named in the profile (default `MANGOS_TLS_PASSWORD`). Certificates and password values are never copied into WebGL.

Build Both runs the platforms sequentially and resumes after Editor script reloads. Each job gets a unique `Builds/<deployment>/<build-id>/` directory with Server, Client, deployment examples and a manifest. Incomplete/cancelled jobs retain a `.staging` directory and cannot replace the last successful release. `Builds/last-successful.json` identifies the last complete job. Generated resource configuration and temporary Editor settings are restored when a job finishes or recovers from interruption.

On Windows, a build helper can keep a directory handle open after Build succeeds. If renaming the staging directory fails, the pipeline copies the completed output and publishes the success manifest only after copying finishes. A staging copy may remain; it is not the directory named by `last-successful.json`. A finalization failure is reported as Failed and cannot replace the previous successful release.

Deploy the **entire Server directory**, including `_Data`, `GameAssembly.so` and `UnityPlayer.so`. On Linux run `sh start-server.sh`. Serve the Client directory using HTTPS and the correct JavaScript/WebAssembly MIME types. Configure `Deploy/nginx-game.conf.example` for the actual host, certificates and document root before applying it. Build Center generates this example; it does not deploy it.

Command-line builds are also available. Pass `-buildTarget Linux64 -standaloneBuildSubtarget Server -executeMethod MangosBuildPipeline.BuildServerBatch` or `-buildTarget WebGL -executeMethod MangosBuildPipeline.BuildClientBatch`, together with `-batchmode -nographics -quit -projectPath <project>`. Optional `-mangosProfile <json>` selects another profile. These validation entry points write `Builds/Validation/<target>`.

For automatic command-line **Build Both** on Windows, run `Tools/BuildBoth.ps1` (optionally `-Unity <Unity.exe>` and `-Profile <json>`). It restarts Unity between Linux and WebGL, resumes the same saved job, and writes logs to `Builds/Logs`. Batchmode cannot reload the platform-specific Editor API modules in the same process. The graphical Build Center switches platforms and resumes through normal Editor reloads. Use `-Validation` only for the local test profile (port 7788, WS, localhost); it also runs the input/avatar/settings checks.

## Browser communication

The WebGL template contains `voice-app.js` and `voice-overlay/`; there is no manual copying step after Build. The original C# voice manager, voice `.jslib`, JS implementation and bundled Socket.IO were removed. Existing scene UI callbacks remain as hidden compatibility entry points; the browser overlay owns communication UI.

The overlay waits for a game room. Room keys combine the deployment namespace and the world/session id supplied by Unity. Changing worlds closes the previous media session before joining the new one and preserves the user's mute preference. Disconnect/logout closes the media session. Microphone capture still requires the user to press Join Voice and grant permission.

The account/voice/media services remain external. Their existing default URLs are in `voice-overlay/config.js`. Deployment must permit the correct cookie domain/SameSite policy and credentialed CORS. The media service must return usable ICE/TURN configuration. These services are not bundled into the Unity Server. Do not place account tokens or TURN secrets in a public build file.

Source overlay modules are supplied in the template, so the Unity project builds independently of the sibling voice-tester directory. Keep the template copy synchronized when updating the original overlay source.

## Avatar loading and animation

VRM 0.x/1.0 use pinned UniVRM 0.128.0. The imported model's own Humanoid Avatar and raw skeleton drive its meshes. Ordinary GLB uses glTFast, aligns the body's up/right basis, and rotates joints into a T-pose before building its own Unity Avatar. This calibration handles relaxed/A-pose exports without changing bone lengths, skin weights or inverse bind matrices. Existing reference Avatar assets contribute bone names only; their Avatar objects and skeleton dimensions are not reused on a different model.

The current movement/fly Animator Controller is retained, with state/parameters transferred to the new model. FishNet NetworkAnimator, Starter Assets, fly movement and interactions are rebound to the active Animator. Each loaded model owns its imported native assets and disposes them when replaced. Live models are no longer cloned into the old hidden 100-model cache. Thumbnails use the same importer, so VRM rendering takes the same material route as the player model.

AvatarLoader defaults to uniform visual height normalization at 1.7m, preserving body proportions and the game's existing authoritative collider policy. Change `normalizeHeight`/`targetHeight` on the player/preview prefab if another visual policy is needed; retaining arbitrary natural heights also requires a consistent gameplay/collision policy. Camera/nameplate offsets follow the resulting visual height. Bone lengths, skin weights and inverse bind matrices are not stretched to match a reference rig.

For an unmapped humanoid GLB, create **MANGOs Framework > Avatar Rig Profile** from the asset creation menu. Map Humanoid roles to node paths and either assign the profile to AvatarLoader or place it under `Resources/AvatarRigProfiles` with a matching URL prefix. Incomplete/ambiguous rigs are rejected with `LastLoadError` and keep the previous/default model. A file without a humanoid rig cannot automatically use humanoid animation.

**MANGOs Framework > Avatar > Export Animation Audit** writes `Library/MangosAnimationAudit.csv`, including clips embedded in imported model assets. The initial audit found 52 Humanoid clips and no Transform position/scale curves. Synthetic rigs with different proportions are tested over 60 animation frames; a synthetic VRM 1.0 is also imported with its own valid Humanoid. Real account avatars, cloth, spring bones, extreme proportions and all interaction poses still need visual checks using the actual model files.

## Adaptive controller

The WebGL template includes `mangos-controller.js` and its stylesheet automatically. Auto mode uses keyboard/mouse for PC and touch for phones/tablets, including iPad's desktop user agent. A touch-enabled PC remains a PC. The Controls selector can override Auto with Touch or Keyboard/mouse; the preference is stored locally. In Auto on a mobile device, a movement key or mouse switches to keyboard/mouse, and touching the screen restores touch controls.

The touch layout changes between portrait and landscape without requiring a reload. A left joystick moves, dragging the right part of the canvas turns the camera, and Run/Jump support simultaneous touches. Fly/Up/Down are shown only for a flyable player. Existing Unity interaction buttons remain tappable; a camera gesture that starts over a Unity UI element is ignored by the camera bridge. Safe-area offsets are included, and the mobile voice bar is compact to leave room for controls.

The same input bridge drives Starter Assets walking and the existing fly controller. Rotating, changing input mode, losing focus, cancelling a pointer, typing in browser chat, or disconnecting clears held controls. While an HTML field has focus, the bridge disables [Unity keyboard capture](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/WebGLInput-captureAllKeyboardInput.html). The touch UI appears only after local player setup. Both screen orientations are allowed; the old forced-landscape blocker is bypassed for adaptive WebGL controls.

Browser automation covers PC, phone, Android tablet, iPad desktop UA, and PC touchscreen; it checks simultaneous joystick/look/jump, rotation while holding the joystick, focus, release, controller overrides, and voice-bar spacing. These emulated browser checks do not replace a physical iOS/Android playtest.

## Validation boundaries

Unity C# compilation and Linux IL2CPP/WebGL compatibility builds have run. The Linux binary has been launched in WSL. Browser overlay lifecycle checks use mock rooms and do not prove live microphone/media quality, account cookies, or production TURN connectivity. Physical iOS/Android testing and a production deployment rehearsal are still required before a release is declared validated.

Unity 6.3's automatic script updater currently emits Mono/Cecil diagnostics while scanning some existing assemblies. Builds can still report Succeeded; inspect `build-diagnostics.txt` and the Editor log instead of treating an exit code alone as proof of a clean build. Headless browser GPU warnings must also be distinguished from visible shader/material failures.
