# MANGOs build and runtime

Open this project with Unity **6000.3.26f1**. The original 2022 project is preserved until the verified changes are transferred back. The migration baseline is Git commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`.

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

Deploy the **entire Server directory**, including `_Data`, `GameAssembly.so` and `UnityPlayer.so`. On Linux run `sh start-server.sh`. Serve the Client directory using HTTPS and the correct JavaScript/WebAssembly MIME types. Configure `Deploy/nginx-game.conf.example` for the actual host, certificates and document root before applying it. Build Center generates this example; it does not deploy it.

Command-line builds are also available. Pass `-buildTarget Linux64 -standaloneBuildSubtarget Server -executeMethod MangosBuildPipeline.BuildServerBatch` or `-buildTarget WebGL -executeMethod MangosBuildPipeline.BuildClientBatch`, together with `-batchmode -nographics -quit -projectPath <project>`. Optional `-mangosProfile <json>` selects another profile. These validation entry points write `Builds/Validation/<target>`.

## Browser communication

The WebGL template contains `voice-app.js` and `voice-overlay/`; there is no manual copying step after Build. The original C# voice manager, voice `.jslib`, JS implementation and bundled Socket.IO were removed. Existing scene UI callbacks remain as hidden compatibility entry points; the browser overlay owns communication UI.

The overlay waits for a game room. Room keys combine the deployment namespace and the world/session id supplied by Unity. Changing worlds closes the previous media session before joining the new one and preserves the user's mute preference. Disconnect/logout closes the media session. Microphone capture still requires the user to press Join Voice and grant permission.

The account/voice/media services remain external. Their existing default URLs are in `voice-overlay/config.js`. Deployment must permit the correct cookie domain/SameSite policy and credentialed CORS. The media service must return usable ICE/TURN configuration. These services are not bundled into the Unity Server. Do not place account tokens or TURN secrets in a public build file.

Source overlay modules are supplied in the template, so the Unity project builds independently of the sibling voice-tester directory. Keep the template copy synchronized when updating the original overlay source.

## Validation boundaries

Unity C# compilation and Linux IL2CPP/WebGL compatibility builds have run. The Linux binary has been launched in WSL. Browser overlay lifecycle checks use mock rooms and do not prove live microphone/media quality, account cookies, or production TURN connectivity. Physical iOS/Android testing and a production deployment rehearsal are still required before a release is declared validated.

Unity 6.3's automatic script updater currently emits Mono/Cecil diagnostics while scanning some existing assemblies. Builds can still report Succeeded; inspect `build-diagnostics.txt` and the Editor log instead of treating an exit code alone as proof of a clean build. Headless browser GPU warnings must also be distinguished from visible shader/material failures.
