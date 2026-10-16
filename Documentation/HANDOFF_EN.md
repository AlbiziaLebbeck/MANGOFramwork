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

GLB creates a Humanoid Avatar from the model's own skeleton, calibrates relaxed/A-pose exports into a T-pose, and aligns its orientation before retargeting. VRM uses UniVRM and its own Humanoid Avatar. The system does not apply a reference Avatar's proportions to a different model. Walking, flying, FishNet, and interactions receive the active Animator.

The default uniformly normalizes visual height to 1.7 metres to match the existing collider policy; it does not stretch individual bones. Keeping natural heights requires a matching collider/gameplay policy. For Humanoid models with different bone names, create an Avatar Rig Profile to specify the mapping. Models without Humanoid skeletons cannot be guaranteed to use every existing Humanoid animation.

Export `.anim` and embedded model-clip information through **MANGOs Framework > Avatar > Export Animation Audit**. The report is written to `Library/MangosAnimationAudit.csv`.

## Controller

Auto selects keyboard/mouse on PC and touch on phones/tablets, and adapts the layout to orientation. It recognizes iPad's Mac-style user agent and does not force touchscreen PCs into touch mode. Players can choose Touch or Keyboard/mouse manually. In Auto on mobile, movement keys or mouse input switch to keyboard/mouse; touching the screen restores touch.

Touch controls provide a left movement joystick, right-side camera drag, Run, and Jump. Fly/Up/Down appear only for flyable characters. Unity interaction buttons remain tappable. Rotation, mode changes, lost focus, and disconnect clear held input; typing into an HTML field suspends game input.

## Before production deployment

See [Validation](VALIDATION_EN.md) for results and limitations, and [Build and runtime](BUILD_AND_RUNTIME.md) for technical details. Browser emulation does not replace physical iPhone/iPad/Android playtests. Mock voice checks do not replace real-account, microphone, media-backend, and TURN tests.

The screenshots use the guest name **PoppyValidation**. This is validation data; normal players continue to use their own chosen or account name.
