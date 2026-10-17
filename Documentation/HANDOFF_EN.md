# Using the updated MANGOs Framework

[English](HANDOFF_EN.md) | [ภาษาไทย](HANDOFF_TH.md) | [All documentation / เอกสารทั้งหมด](README.md)

Open the project with **Unity 6000.3.26f1**, then open **MANGOs Framework > Build Center**.

The current source is on branch `MANGOsFramework_U6`. The original Unity 2022 baseline remains in Git history at commit `e461cdcad0b5bf7191f903afc828ea248b296a6f`.

## Build Server and Client

1. Change **Client Path** to the WebSocket path for your deployment.
2. Press **Build Both**, or build Server / Client separately.
3. Use the folder shown on success, or read `Builds/last-successful.json`.

| Editable setting | Server | Client |
|---|---|---|
| Default port | 7770 | 443 |
| Default WSS | Off | On |
| Build target | Linux Dedicated Server, IL2CPP, x86_64 | WebGL |

Each side's Port/WSS settings are independent and can be saved in the profile. There is no need to edit a prefab or switch platforms manually. The WebGL template automatically includes the voice overlay and controller. The initial host/path preserve the existing deployment at `multiplayer-plus.mangosgo.com` and `/systemtester`.

These defaults require a reverse proxy that accepts WSS on 443 and forwards WS to the Server on 7770. The build's Deploy directory contains an Nginx example. Configure its hostname, certificates, and document root for the real host before use. Building does not change the live server deployment.

Deploy the whole Server directory, including the files accompanying `.x86_64`, then run `sh start-server.sh` on Linux. Serve Client over HTTPS with JavaScript and WebAssembly MIME types.

For the Windows command line, run `Tools/BuildBoth.ps1`. The runner selects the platform and restarts Unity to complete both targets. See [Build and runtime](BUILD_AND_RUNTIME.md) for details and options.

## Browser voice

The old VoiceCommunication code has been replaced by `html-voice-tester` in the WebGL template. Players join voice and grant microphone permission through the browser overlay. The overlay waits until Unity joins a room, and separates voice rooms by deployment and world id. Changing world or disconnecting leaves the previous session.

Account API, voice/media backend, and TURN remain external services configured by the HTML system. Verify cookies/CORS, HTTPS, microphone permission, and TURN with the actual deployment.

## Avatars and animation

The Avatar system is restored from `MANGOFramework_Cookie`: glTFast loads the model, the original loader selects a reference Humanoid Avatar from `Resources/AvatarLoader` by its node structure, and the existing player-root Animator/controller is reused. The original model cache, thumbnail generation, selection window and five-second selection cooldown are restored, together with network, sitting and interaction bindings.

Compatibility is the same as the Cookie system and depends on matching one of its supported reference rigs. The experimental per-model Humanoid builder, Rig Profiles, VRM-specific importer, automatic height normalization and animation-audit menu have been removed. The original VRM/reference-rig path remains; it is not a guarantee for arbitrary GLB/VRM skeletons.


## Cookie controls

On touch browsers, the HTML voice bar sits at the bottom centre to leave the original joystick clear.

The original Unity controller is restored: Starter Assets, `PlayerMovementHandler`, `LocomotionManager`, Unity mobile controls, the controller-switch button, walking/click-to-move and fly movement. Device detection uses the original `CheckMobile`/`IsMobile` browser bridge. Cursor/pointer-lock behavior and the orientation guard are restored from Cookie, including its configured landscape requirement.

The HTML controller, automatic input-mode switching and its custom camera/input router have been removed. Input and Avatar code now follow the Cookie implementation; the remaining browser bridge only integrates the separate HTML voice system.


Rebuild and deploy the complete Client after updating; its PWA cache version has changed.

## Before production deployment

See [Validation](VALIDATION_EN.md) for results and limitations, and [Build and runtime](BUILD_AND_RUNTIME.md) for technical details. Browser emulation does not replace physical iPhone/iPad/Android playtests. Mock voice checks do not replace real-account, microphone, media-backend, and TURN tests.

The screenshots use the guest name **PoppyValidation**. This is validation data; normal players continue to use their own chosen or account name.
