# MANGOs build and runtime

[English](BUILD_AND_RUNTIME.md) | [ภาษาไทย](BUILD_AND_RUNTIME_TH.md) | [All documentation / เอกสารทั้งหมด](README.md)

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

## Avatars and animation

The Avatar system is restored from `MANGOFramework_Cookie`: glTFast loads the model, the original loader selects a reference Humanoid Avatar from `Resources/AvatarLoader` by its node structure, and the existing player-root Animator/controller is reused. The original model cache, thumbnail generation, selection window and five-second selection cooldown are restored, together with network, sitting and interaction bindings.

Compatibility is the same as the Cookie system and depends on matching one of its supported reference rigs. The experimental per-model Humanoid builder, Rig Profiles, VRM-specific importer, automatic height normalization and animation-audit menu have been removed. The original VRM/reference-rig path remains; it is not a guarantee for arbitrary GLB/VRM skeletons.


## Cookie controls

On touch browsers, the HTML voice bar sits at the bottom centre to leave the original joystick clear.

The original Unity controller is restored: Starter Assets, `PlayerMovementHandler`, `LocomotionManager`, Unity mobile controls, the controller-switch button, walking/click-to-move and fly movement. Device detection uses the original `CheckMobile`/`IsMobile` browser bridge. Cursor/pointer-lock behavior and the orientation guard are restored from Cookie, including its configured landscape requirement.

The HTML controller, automatic input-mode switching and its custom camera/input router have been removed. Input and Avatar code now follow the Cookie implementation; the remaining browser bridge only integrates the separate HTML voice system.


## Validation boundaries

Unity C# compilation and Linux IL2CPP/WebGL compatibility builds have run. The Linux binary has been launched in WSL. Browser overlay lifecycle checks use mock rooms and do not prove live microphone/media quality, account cookies, or production TURN connectivity. Physical iOS/Android testing and a production deployment rehearsal are still required before a release is declared validated.

The ScriptUpdater exit-130 errors from legacy Rigidbody APIs were fixed on 17 October 2026 by migrating physics calls to `linearVelocity`. `Tools/BuildBoth.ps1` now checks every phase log for ScriptUpdater failures and C# errors, including phases that exit to switch platforms. The pipeline rejects a BuildReport with a nonzero error count even if its result says Succeeded. Inspect `build-diagnostics.txt` and Editor logs; remaining warnings and historical failures are explained in [Validation](VALIDATION_EN.md). Headless browser GPU warnings must still be distinguished from visible shader/material failures.

## Repeat the WebGL screenshot check

The [Tools/Validation/check-integration-webgl.cjs](../Tools/Validation/check-integration-webgl.cjs) script defaults to the guest name **PoppyValidation**. This is QA data; it does not rename accounts or force a player name in the game. Override the fixture through the `MANGOS_VALIDATION_GUEST_NAME` environment variable if needed.

Requirements are Node.js, the `playwright` package resolvable by Node, and Microsoft Edge, launched with Playwright's `channel: 'msedge'`. First start the Server from the **local validation build** on 7788/WS, using the matching Server/Client pair. The script does not rebuild or deploy.

From the project root, substitute the actual local build path:

```powershell
node Tools/Validation/check-integration-webgl.cjs 'C:\path\to\Builds\validation\<build-id>'
```

The script temporarily serves Client at `http://127.0.0.1:8767/`, joins as a guest, and captures a desktop 1280×800 screenshot plus JSON in `Builds/Validation/Browser/`. An optional second argument selects another evidence directory. Voice is mocked and the account API is simulated as logged out; this does not verify real voice/account services.

Checks cover page identity, initial content, FishNet connection, Server confirmation of the guest name, world/avatar readiness, legacy world/avatar readiness and runtime exceptions. Visually inspect the upper-right nameplate before promoting images into documentation evidence.


## Restored Cookie validation

`Tools/BuildBoth.ps1 -Validation` runs `MangosCookieRestoreValidation.Run`, physics/TMP checks and profile checks before building Server/WebGL. Cookie validation verifies the reference Avatar assets, controller resource, keyboard/touch action maps and the network player's root Animator binding. The prior own-rig/input-router tests and HTML-controller harnesses have been removed with those implementations.

The portable integration script now checks the legacy runtime: guest login, avatar load, room readiness and the absence of the HTML controller. Original controller buttons are rendered by Unity. Account session and media services remain external.
