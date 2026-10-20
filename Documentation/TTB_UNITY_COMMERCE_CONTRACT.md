# Unity / HTML commerce integration

Implementation: `Assets/_TtbCommerce`, `Assets/Plugins/WebGL/MangoTtbCommerce.jslib`, `WebCommerce`. Native gallery and metaverse builds use the isolated `MANGOsTTB` template. The KKU deployment and its API are separate.

## Presentation and input

`window.MangoTtb.attachUnity(instance)` attaches the loader instance. `host.attached` means the canvas exists; `host.ready` comes from the scene's native bridge and means `SendMessage` can target `MangoTtbCommerceBridge.OnBrowserMessage`. A scene unload emits `host.stopped`; commands are withheld until the next native bridge is ready. Every command/event has `version:1`; unknown events and financial flags are rejected.

Canvas pointer-up sends bounded normalized coordinates to `world.tap`. Unity performs the actual world-camera raycast and finds a `TtbProduct` collider; browser coordinates never select a price or authorize a purchase. Multi-touch, cancelled gestures and drags beyond 8 CSS pixels do not select a product. This preserves short taps that can otherwise fall between native input frames. Non-WebGL input uses the Input System directly.

`product.open` carries a product GUID or a published model key. API resolution is global, not limited to the first catalog page; duplicate published model bindings require an explicit product GUID. Native input is suspended immediately while product metadata is fetched, and restored if the request fails or the user navigates away. Each preview has a request ID. Viewport, orbit, zoom, reset and close commands must match that ID. The viewport is measured against the actual canvas, including scrolling and resizing.

The preview copies render geometry only, including baked skinned meshes. It does not clone product scripts, colliders or network objects. A remote layer-31 stage and its camera render through the transparent HTML preview pane. Price, stock, variant selection, cart, checkout, QR, history and documents remain DOM UI. Layer 31 must remain reserved for the private preview in this project.

`TtbWorldInputGate` snapshots and suspends the existing walking/flying controllers and camera input while an overlay is open, restores their prior state, and handles a newly spawned owner. WebGL keyboard capture is released for HTML fields. Cursor state is restored after closing. The existing Cookie movement/input source is retained.

## Using an owned inventory item

1. The owning FishNet player has `TtbGameRelay` in both client and server builds. The server supplies a random 256-bit challenge, a request ID, entitlement ID and registered server ID through an owner-only TargetRPC. A different connection cannot submit the owner's ServerRPC.
2. The authenticated browser posts `/session/game-ticket` with CSRF. The commerce API derives shopper/session identity from its own session, checks the active inventory-item grant and paid, allocated, non-voided order, and returns a single-use credential valid for at most 60 seconds. Only its hash and challenge hash are stored; no service secret is returned.
3. The browser keeps that credential in memory and returns it over the owning native connection. The server verifies the pending challenge/request/entitlement and consumes it through `/game/tickets/consume`, authenticated with a separate game service key. Browser-supplied shopper IDs and payment states have no authority.
4. The server accepts only model keys in the authored `TtbGameModels` registry, equips the approved prefab and synchronizes only the model key to observers. It retains shopper/session identifiers privately for lease renewal. Native game usernames are not financial identity.
5. Authorization lasts at most 15 seconds and renews before expiry through `/game/entitlements/lease`. Expiry, a denied/failed renewal, a revoked session/grant, financial review or void removes equipment. Session/account changes clear pending challenges/equipment. No authorization persists merely because a client once displayed Paid.

Revocation takes effect by the next successful renewal or lease expiry. Changes in upstream MANGOs identity are detected when the commerce session is revalidated; this is not a claim of an instant global SSO logout. The browser revalidates on focus and at a bounded visible interval. The old service-only `/game/entitlements/validate` reports ownership, including download purchases, and must not substitute for the new connection-bound ticket/lease flow.

Server-only environment:

```
MANGOS_TTB_GAME_API=https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1
MANGOS_TTB_GAME_ID=mangos-ttb
MANGOS_TTB_GAME_KEY=<provision in server secret store, never in WebGL>
```

Only the two approved TTB HTTPS hosts/default TLS port and exact API prefixes are accepted. Local loopback HTTP port 5286 additionally requires `MANGOS_TTB_GAME_LOCAL=true`. The commerce API needs the same isolated profile as `Storage:GameServerId` and `Storage:GameServerKey`. The receipt worker does not receive either credential. Missing configuration disables the native use button; it does not silently simulate equipment.

## Scene and build integration

`TtbWorldCommerceInstaller` adds a scene-owned exhibition to `BaseWorld` from `TtbWorldCommerce` settings and leaves Bootstrap/Launcher and existing player/camera in control. Both existing `NetworkPlayer` prefabs receive the same relay; their GUIDs remain stable. `TtbWorldIntegration.Prepare` authors/validates the demo exhibition, render-only item prefab and registry. Production items need approved catalog/model bindings and authored registry entries.

- `Tools/TTB/build-local.ps1`: copy Assets/Packages/ProjectSettings into `Builds/TTB/BuildWorkspace`, build the isolated native gallery, return only owned generated assets and reviewed player integrations. It does not close the user's Editor or share Library. A concurrent user edit to either player prefab aborts copy-back.
- `Tools/TTB/build-world-local.ps1`: build the original Bootstrap/Launcher and an owned prepared copy of BaseWorld as Windows dedicated server and WebGL with a Local profile on port 7778. The copied BaseWorld retains the original runtime and adds the authored exhibition. It does not alter the original scene file, scene list or network config. See `TTB_BASEWORLD_SCENE.md` for the new scene's acceptance status.
- `Services/TtbCommerce/tools/local.ps1`: explicit Local+Mock database/seed, isolated published runtime and private asset root. This never enables the simulator on UAT/Production.
- `Tools/TTB/start-world-local.ps1`: start only that local server, reading the ignored local game profile without putting a key in arguments/logs.
- `node Tools/TTB/serve-gallery.mjs`: native gallery on 127.0.0.1:5186. `--world`: full metaverse on 127.0.0.1:5188. API proxy destination is fixed at loopback 5286.

Automated backend ticket/identity tests and the full 135-case suite passed in `commerce_net10.0_20261018160537.trx`. Browser security 8/8 passed. Compiled gallery desktop/mobile2/2 passed with zero page/console errors; the original-scene FishNet game-use regression1/1 passed, including a renewal beyond15 seconds and logout removal. Screenshots in `.artifacts/unity-qa` were viewed. This is Local+Mock evidence; bank UAT and physical-device/PWA checks remain separate.

Latest 19 October: the new authored BaseWorld gallery builds as a paired Windows server/WebGL client through the isolated scripts. Save-as rebuilds FishNet SceneIds once and the client reuses the exact scene/metadata fingerprints; the isolated WorldManager prefab points to that prepared scene. Its original source prefab remains unchanged. Backend full247/247, native gallery3/3 and full-world1/1 pass. The world case also picks native physical/digital models, checks out through the HTML cart/Mock QR/payment/history and saves a private receipt PDF, while testing lease/logout. See `TTB_BASEWORLD_SCENE.md` and `.artifacts/scene-qa/baseworld-final-20261019/result.json` for current scope and hashes; these do not certify bank/device/Production acceptance.
