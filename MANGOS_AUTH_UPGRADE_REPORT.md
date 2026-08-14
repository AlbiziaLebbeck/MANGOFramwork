# MANGOs Authentication and Gameplay Upgrade Report

## Scope and Sources

This upgrade was performed on `Cookie-based_MANGOs` in the target repository. The legacy Unity project was inspected as a read-only reference and was not modified.

The following source-of-truth documents were read completely before implementation:

- `first-party-subdomain-cookie-sso.md` (205 lines, SHA-256 `F27827AC2082B1B93F066CC39F9CA506A02275182A43141E2648DA7147F28EC6`)
- `third-party-oauth-user-api.md` (519 lines, SHA-256 `7F3047B954EF27E40DD4443E74BDF391B0F25D2ED83A1D070933E33C38C1B9C9`)

The implementation uses `https://api.mangosgo.com/api/v1` as the default API base URL and `https://api.mangosgo.com` as the asset origin.

## Existing Target Authentication Architecture

The target used a single WebGL-oriented OAuth popup flow in `Launcher`. It stored OAuth access-token metadata and a user id in browser local storage through `WebAuth.jslib`. Startup trusted the local expiration timestamp and then requested `/api/v2/users/get-one/{userId}`. The target had no centralized deployment-mode selection and no credentialed Cookie SSO transport.

`ApiService` was a thin `UnityWebRequest` wrapper with duplicated GET/POST entry points. It did not validate the documented `status`/`time`/`data` envelope, distinguish timeout/cancellation/invalid JSON/missing data, or centrally clear invalid authentication state.

The serialized authentication configuration referenced a missing `AuthConfig` asset. The old `AuthConfig` type also contained a serialized client secret field.

### Existing Login Flow

1. WebGL JavaScript inspected `metaauth_*` local-storage values.
2. A popup opened the old OAuth site.
3. The authorization code was exchanged from the Unity client using a client secret from a Unity asset.
4. Tokens were persisted to local storage.
5. User data was fetched from the obsolete user-by-id route.
6. The authenticated user selected Continue in the existing authentication canvas.

### Relevant Existing Files and Classes

- `Assets/_Modules/Authentication/Launcher.cs`: startup and login orchestration.
- `Assets/_Modules/Authentication/AuthenticationCanvas.cs`: existing login/guest/continue UI.
- `Assets/_Modules/Authentication/AuthConfig.cs`: endpoint and OAuth configuration.
- `Assets/_Modules/Authentication/ApiService.cs`: shared HTTP helpers and models.
- `Assets/Plugins/WebGL/WebAuth.jslib`: popup, local-storage token, and logout bridge.
- `Assets/_Project/Scripts/Runtime/UserReferencePersistent.cs`: runtime user and avatar state.
- `Assets/_Project/Scripts/Runtime/UI/Canvases/UserDataCanvas.cs`: persistent user UI.

## Obsolete API Usage and Documentation Differences

The removed target flow used:

- `https://api.mangosgo.com/api/v2`
- `https://authenticate.mangosgo.com/api/v2`
- `https://auth.mangosgo.com/oauth-login`
- `/users/get-one/{userId}`
- `/public/generate-basic`
- browser local-storage keys named `metaauth_accessToken`, `metaauth_accessTokenExpiresAt`, and `metaauth_userId`

The new documentation instead requires the v1 envelope and the documented OAuth routes, uses `GET /users/me` for the current identity, and defines Cookie SSO as a separate first-party browser mode. A post-upgrade source scan found no remaining obsolete endpoint or `metaauth_*` storage references in C#, Unity YAML, JSON, Markdown, or `.jslib` files.

## Security and Breaking-Change Risks

- The old serialized client secret and WebGL token persistence exposed credentials to a downloadable client. These paths were removed.
- HttpOnly session cookies are never read by C# or JavaScript. Cookie SSO uses browser `fetch` with `credentials: "include"`.
- OAuth tokens are held in memory only. This avoids silently introducing insecure persistence but requires the application to obtain fresh tokens after restart.
- The documentation does not define a refresh endpoint. No refresh behavior was invented.
- OAuth code exchange requires HTTP Basic client credentials. A trusted exchange backend is therefore required for WebGL and is recommended for all distributed clients. The Unity API returns an explicit security-configuration error instead of embedding a secret.
- Marketplace routes reject OAuth access tokens under current backend behavior. The client prevents Marketplace calls in OAuth mode.
- Public legacy `AuthConfig` property names and basic `ApiService` GET/POST wrappers remain as compatibility members, but the `SECRET` compatibility property always returns an empty string and is marked obsolete.

## Missing Backend Information

The supplied documentation names wallet transaction and login-claim endpoints but does not provide their request and response schemas. Those calls were not added. The backend must provide verified schemas for:

- `GET /mgo-gold/get-my-transactions`
- `GET /mgo-gold/check-login-claim`
- `POST /mgo-gold/claim-login`

A trusted OAuth code-exchange service or another approved secret-free exchange design is also required before the existing Login button can complete third-party OAuth end to end. Current Marketplace OAuth support must not be enabled unless backend behavior changes and is independently verified.

The current public MANGOs frontend was observed retrying a `401` through `GET /auth/refresh-tokens`, but the supplied Cookie SSO guide does not document that endpoint or authorize subdomain clients to use it. The Unity client therefore does not call it. If `/users/me` still returns an uncached `401` while the main site appears logged in, MANGOsAuth must confirm whether the shared access cookie is expired, whether the visible main-site state is stale, and whether a documented Cookie SSO refresh operation will be supported.

## Upgrade Plan and Authentication Mode Design

The implementation uses one shared `MangosApiClient` and a centralized `AuthenticationMode` enum:

- `Auto`: selects Cookie SSO only for a WebGL page whose exact host is `mangosgo.com` or a subdomain ending in `.mangosgo.com`; otherwise selects third-party OAuth.
- `FirstPartyCookieSso`: manually forces credentialed browser requests.
- `ThirdPartyOAuth`: manually forces bearer-token requests.

Platform-specific browser behavior is isolated in `WebAuth.jslib` and the WebGL bridge portion of `MangosApiClient`. Endpoint definitions, models, envelope validation, URL resolution, HTTP errors, cancellation, and authentication state remain in the shared layer.

## Cookie SSO Implementation

- Startup checks the browser session with `GET /users/me` and does not force a login redirect.
- Missing, invalid, or expired authentication enters anonymous mode and clears stale local authentication state.
- Transient network failures do not discard a still-valid in-memory OAuth token.
- The explicit Login action redirects to `https://mangosgo.com/login` and passes the complete current WebGL URL in the `redirect` query parameter.
- Credentialed WebGL requests use `fetch` with `credentials: "include"`, an `AbortController`, and a bounded timeout.
- Local disconnect clears only application state. Global logout is a separate operation that calls `POST /auth/logout` with browser credentials.
- No application cookie or local-storage value named `accessToken` or `refreshToken` is created.

## OAuth Implementation

- Implements the documented authorization-code request using `POST /o-auth/get-auth-code`.
- Defines and safely guards `POST /o-auth/get-basic`; it cannot run in a distributed Unity client because it requires a client secret.
- Implements `POST /o-auth/auth-to-access` for a runtime Basic credential supplied from a secure source and disables direct WebGL exchange.
- Parses and validates access- and refresh-token expiration timestamps.
- Uses `Authorization: Bearer <oauthAccessToken>` only for documented OAuth-capable routes.
- Clears authentication state on expired access tokens and HTTP 401 responses.
- Does not log, persist, or automatically refresh OAuth credentials.

## Shared API and User Systems

The shared API layer supports:

- `GET /users/me`
- `GET /avatars`
- `GET /friends/get-my-friends`
- `GET /friends/get-friend-requests`
- `GET /friends/get-requests-of-me`
- `GET /friends/:userId/profile`
- `GET /mgo-gold/get-my-wallet`
- documented Marketplace list/item/buy models with a first-party-only guard

It handles HTTP 400/401/403/404, other protocol failures, network failures, timeout, cancellation, invalid JSON, missing endpoint data, unexpected envelopes, and invalid authentication. Relative avatar and Marketplace asset paths are resolved against `https://api.mangosgo.com`.

## Legacy PlayerTypeController Findings

The exact legacy script is `Assets/_Project/Scripts/Runtime/PlayerTypeController.cs`, class `PlayerTypeController`, with public serialized field `bool flyablePlayer`. Its GUID is `eea5b9b8bbce0314babfddaa05c08320`.

The legacy behavior did not actually select a player prefab. `Start` used `flyablePlayer` only to set cursor state, and `Update` toggled the cursor on the E key. The legacy `NetworkedPlayerSpawner` still had a single `playerPrefab` field, and the legacy `BaseWorld` scene hardwired that field to `NetworkPlayer_Flyable`. There was no runtime player replacement and no state transfer.

The target implementation preserves the field name and script GUID but completes the intended Editor-configured selection: `NetworkedPlayerSpawner` resolves either `NetworkPlayer` or `NetworkPlayer_Flyable` immediately before instantiation. The selection is read from the scene `PlayerTypeController`; an already spawned player is never replaced.

## NetworkPlayer and NetworkPlayer_Flyable Migration

The existing target `NetworkPlayer` remains the standard option. The following legacy assets were copied with their `.meta` files:

- `Assets/_Modules/Networking/Prefabs/NetworkPlayer_Flyable.prefab`
- `Assets/3rdPerson+Fly/Animations/**`
- `Assets/3rdPerson+Fly/Animators/CharacterController.controller`
- `Assets/3rdPerson+Fly/Materials/Character.physicMaterial`
- `Assets/3rdPerson+Fly/Scripts/PlayerScripts/BasicBehaviour.cs`
- `Assets/3rdPerson+Fly/Scripts/PlayerScripts/MoveBehaviour.cs`
- `Assets/3rdPerson+Fly/Scripts/PlayerScripts/FlyBehaviour.cs`
- `Assets/3rdPerson+Fly/Scripts/LevelScripts/ThirdPersonOrbitCamBasic.cs`
- `Assets/_Project/Scripts/Runtime/LockTarget.cs`

The copied prefab was adapted to the target networking and avatar systems. An unrelated legacy emote component and its missing UI dependency were not migrated. Legacy animator references to absent emote-only clips were replaced with the local idle clip so the migrated controller has no broken asset reference; locomotion, jump, and flight clips remain intact.

`NetworkedPlayerComponent` now detects controller components, enables input only for the owning client, disables movement for remote/server representations, initializes the appropriate camera path, and retains the shared avatar loader and network synchronization. Flyable camera state is reset when ownership ends. `NetworkObjectSpecialMove` supports both controller families without the legacy obsolete purchase dependency.

## Scene, Prefab, and Network Registration Changes

- `BaseWorld.unity` now contains `PlayerTypeController` with `flyablePlayer` disabled by default.
- Its `NetworkedPlayerSpawner` references both standard and flyable prefabs plus the selector.
- `NetworkPlayer_Flyable` is registered in `DefaultPrefabObjects.asset`, `MetaversePlatform_prefab.asset`, and `SinglePrefabObjects.asset`.
- `InputManager.asset` contains the required `Sprint`, `Fly`, `Analog X`, and `Analog Y` legacy input mappings.
- The existing standard player, AreaSpawner, WorldManager, FishNet spawn path, ownership assignment, avatar loader, and network transform path remain in use.

No package manifest change was required. No unrelated ProjectSettings were copied from the legacy project.

## MANGOs Gold Migration

The legacy implementation provided a wallet amount on `Launcher`, cached it in `UserReferencePersistent`, and displayed it in `UserDataCanvas`. Its direct-buy route used undocumented obsolete v2 behavior; no complete verified transaction-history or login-claim model was present.

The target now retrieves the documented wallet with the selected authentication strategy, validates `data.UserMgoGoldWallet`, caches the amount, exposes a change event, and updates a `MangosGoldText` element in the persistent user canvas. Anonymous or failed-wallet states do not create fake values. HTTP 401 follows the shared invalid-authentication handling. Unsupported transaction and claim actions were not connected or simulated.

## Agora Removal and WebGL Voice Communication

The Agora SDK, sample project, custom Agora integration layer, browser SDK bundle, video views, camera controls, screen-sharing controls, device settings, and Agora-specific WebGL build hooks are removed. The custom WebGL template is renamed from `PWAwithAgora` to `MANGOsPWA`, while retaining its authentication, pointer-lock, service-worker, and MANGOs UI behavior.

Voice communication is now a separate, user-initiated WebGL feature. `VoiceCommunicationManager` connects the existing microphone button to a browser bridge. The browser client joins and leaves the documented game-room REST API, uses the server-provided WHIP URL to publish the local microphone, uses WHEP URLs to subscribe to remote participants, and uses Socket.IO room events for membership updates. It does not add a second local microphone loopback or expose voice credentials in Unity.

The voice server was inspected read-only through the authorized `ntserver01` SSH target. The deployed backend uses Socket.IO 4.7.5, accepts `https://plus.mangosgo.com` in its CORS allowlist, exposes STUN/TURN configuration through `GET /video-streaming/api/v1/ice-servers`, and implements the following endpoints and events:

- `POST /video-streaming/api/v1/game-rooms/:roomId/join`
- `GET /video-streaming/api/v1/game-rooms/:roomId`
- `POST /video-streaming/api/v1/game-rooms/:roomId/leave`
- `join-game-room`, `leave-game-room`, participant snapshot/join/leave events, and join errors

The game-room API currently does not require MANGOsAuth or an API key by backend design. The client therefore sends no MANGOs cookie, bearer token, or secret to the voice protocol. If authenticated or access-controlled rooms are required later, that policy must be implemented and documented by the voice backend rather than inferred in Unity.

The pinned browser client and its license are bundled in the WebGL template so builds do not depend on a public CDN. The Service Worker uses a new cache generation and includes both voice scripts in the application shell. Voice starts only after the player clicks the microphone button, satisfying browser microphone permission and autoplay policies. Multiplayer microphone status remains synchronized through the existing generalized FishNet `OnMic` field; obsolete camera and projector fields remain serialized only for wire compatibility and are no longer connected to UI or Agora.

## Avatar Thumbnail Queue

The previous avatar UI created buttons immediately but only generated an image when the shared gameplay `AvatarLoadedEvent` happened. As a result, only an avatar that had already been selected showed a preview. The new `AvatarThumbnailService` uses a dedicated sequential queue. It imports one account avatar at a time into an isolated off-screen preview, disables animation, calculates renderer bounds, captures a consistently framed square image, releases the temporary model/import resources, and then proceeds to the next avatar.

This preview path does not use `AvatarLoader`, does not publish global gameplay avatar events, does not modify Humanoid mappings, and does not populate the gameplay model cache. Avatar selection, the active network avatar, animation setup, RajPattern detection, and pull/download behavior therefore remain on their existing code path. Generated textures are cached by normalized HTTPS asset URL and are also used for the active account profile image.

## Files Added

- `MANGOS_AUTH_UPGRADE_REPORT.md`
- `Assets/_Modules/Authentication/MangosApiClient.cs` and `.meta`
- `Assets/_Modules/Authentication/MangosAuthConfig.asset` and `.meta`
- `Assets/_Modules/VoiceCommunication/VoiceCommunicationConfig.cs`, `VoiceCommunicationManager.cs`, and metadata
- `Assets/Resources/VoiceCommunicationConfig.asset` and metadata
- `Assets/Plugins/WebGL/VoiceCommunication.jslib` and metadata
- `Assets/WebGLTemplates/MANGOsPWA/VoiceCommunication/VoiceCommunication.js`
- `Assets/WebGLTemplates/MANGOsPWA/VoiceCommunication/socket.io-4.7.5.min.js` and `SOCKET.IO-LICENSE.txt`
- `Assets/_Modules/AvatarSystem/Scripts/AvatarThumbnailService.cs` and metadata
- `Assets/_Project/Scripts/Runtime/PlayerTypeController.cs` and `.meta`
- The selected Flyable prefab, controller scripts, animation assets, controller, physics material, and `LockTarget` listed above
- Legacy Humanoid mappings and their `.meta` files: `FruitAvatar.asset`, `full_derssAvatar.asset`, `MonkAvatar.asset`, `PHAvatar.asset`, `ShortBone.asset`, and `VrmAvatar.asset` under `Assets/Resources/AvatarLoader`

## Files Modified or Rewritten

- Rewritten: `AuthConfig.cs`, `ApiService.cs`, `Launcher.cs`, and `WebAuth.jslib`.
- Repository tracking: `.gitignore` no longer contains obsolete Agora plugin exceptions.
- Merged/adapted: `NetworkedPlayerSpawner.cs`, `NetworkedPlayerComponent.cs`, `NetworkObjectSpecialMove.cs`, `UserReferencePersistent.cs`, and `UserDataCanvas.cs`.
- Scene/configuration: `BaseWorld.unity`, `Bootstrap.unity`, three FishNet prefab collections, and `InputManager.asset`.
- WebGL/voice: `ChatCanvas.cs`, `PersistentCanvas.cs`, `EventHandler.cs`, `NetworkedPlayerComponent.cs`, `MANGOsPWA/index.html`, `MANGOsPWA/ServiceWorker.js`, and `ProjectSettings.asset`.
- Avatar preview: `AvatarSystem.cs`, `AvatarIcon.cs`, and `AvatarImageGenerator.cs`.
- Adapted legacy copy: `BasicBehaviour.cs`, `ThirdPersonOrbitCamBasic.cs`, `CharacterController.controller`, and `NetworkPlayer_Flyable.prefab`.

## Components Deprecated

- The old OAuth popup and `metaauth_*` local-storage bridge.
- The v2 user-by-id startup flow.
- Client-side Basic credential generation and serialized OAuth secret.
- The obsolete mock authentication exchange.
- Legacy direct-buy coupling in the special-movement interaction.
- Agora RTC, Agora video/device/screen-sharing UI, and the `PWAwithAgora` template.

## Backward Compatibility

- The existing authentication canvas events, guest flow, Continue-as-user flow, user singleton, avatar system, and network spawn lifecycle remain in place.
- `NetworkedPlayerSpawner.playerPrefab` is migrated to `standardPlayerPrefab` with `FormerlySerializedAs`.
- Legacy uppercase `AuthConfig` getters and the misspelled `GetAuthCodeReponse` type remain as obsolete compatibility members.
- Standard `NetworkPlayer` remains the default scene selection.

## Tests and Validation

Completed validation:

- Unity 2022.3.62f3 batch-mode script compilation completed successfully, including FishNet IL post-processing and the WebGL player assemblies.
- All enabled build scenes (`Bootstrap`, `Launcher`, and `BaseWorld`) opened in batch mode with no missing `MonoBehaviour` script references.
- The runtime prefab scan found one pre-existing missing component repeated on the `Spinner` children in `Assets/_Project/Prefabs/UIs/Prefabs/SpinnerCanvas.prefab`. This unchanged prefab is not instantiated in an enabled build scene and did not prevent the WebGL build; it remains a maintenance warning.
- A clean-output WebGL player build completed with `BuildResult.Succeeded` at `Builds/MANGOsPWA_Validation_20260814` (67,180,445 bytes). The build produced the expected data, framework JavaScript, loader, and WebAssembly files with no C# compiler errors, linker failures, undefined symbols, or Agora native references.
- The only error-class messages in the validation log were Unity Licensing token-refresh messages during startup. Unity subsequently resolved the entitlement and completed the player build successfully. The two build warnings were glTF shader `pow` warnings for GLES3.
- The generated build contains the local Socket.IO 4.7.5 client, its license, and `VoiceCommunication.js`. SHA-256 checks confirmed that both generated JavaScript files are byte-identical to their template sources.
- JavaScript syntax checks passed for the source and generated voice client and for the generated Service Worker. The source Service Worker contains Unity template directives and is validated after template expansion.
- Project-source and generated-build scans found no Agora or `PWAwithAgora` runtime references.
- `.jslib` JavaScript syntax check: passed.
- Changed Scene/Prefab YAML duplicate object-id check: passed.
- Changed Scene/Prefab local fileID integrity check: passed.
- New asset GUID/reference check: passed. Two unrelated missing asset GUIDs already existed in `Bootstrap.unity` before this upgrade.
- Standard and Flyable prefab references are both assigned in `BaseWorld.unity`.
- Flyable prefab registration appears exactly once in each relevant FishNet collection.
- Required legacy input names are present without new duplicate entries.
- Obsolete authentication endpoint/storage scan: passed.
- Embedded authentication-secret assignment scan: passed for upgrade code and configuration.
- Credential logging scan: passed for upgrade code.
- Legacy source remained read-only.

The WebGL build validates compilation, linking, template expansion, and generated-file inclusion. Browser microphone permission, WHIP/WHEP media flow, deployed CORS behavior, avatar thumbnail rendering, and multiplayer behavior still require the planned deployed interactive test.

### Player Configuration A: Flyable Player Disabled

Static validation confirms `NetworkPlayer` is selected by the resolver, the standard prefab reference remains assigned, and its existing camera/input/avatar/network components compile. Runtime spawn, movement, ownership, and synchronization still require PlayMode host/client validation.

### Player Configuration B: Flyable Player Enabled

Static validation confirms `NetworkPlayer_Flyable` is selected before instantiation, the prefab is registered, its movement/camera/avatar/network dependencies resolve, and the migrated code compiles. Runtime flight, camera, input, avatar loading, ownership, and synchronization still require PlayMode host/client validation.

## Known Limitations and Remaining Blockers

- Third-party OAuth cannot complete securely until a trusted service exchanges authorization codes without exposing the client secret to Unity.
- Automated authenticated backend integration tests were not run because no test credentials or isolated integration environment were supplied. A user-run deployed Cookie SSO check reached the production `/users/me` endpoint and exposed the service-worker caching issue documented below.
- Browser Cookie SSO requires production CORS and cookie attributes to allow credentialed requests from the deployed first-party subdomain.
- Wallet transaction and login-claim UI/actions remain unimplemented pending documented schemas.
- Marketplace remains intentionally unavailable in OAuth mode.
- Legacy emote-only animation layers use the local idle clip instead of migrating the unrelated emote package.
- EditMode tests, PlayMode tests, a Standalone build, both interactive player configurations, a multiplayer host/client session, and deployed WebGL voice/media validation remain manual validation items.
- `SpinnerCanvas.prefab` contains a pre-existing missing component on its spinner children. It is not referenced by the enabled build scenes and did not block the successful WebGL build, but it should be repaired or removed before that prefab is used again.

## Manual Unity Editor Steps

1. Open the project in Unity 2022.3.62f3, allow the removed Agora assets and new voice assets to import, and confirm the Console has no script or import errors.
2. Open `Assets/_Project/Scenes/BaseWorld.unity`.
3. Select `PlayerTypeController`, disable **Flyable Player**, start a host/client session, and verify standard movement, camera, avatar loading, ownership, synchronization, and single-player spawning.
4. Stop Play Mode, enable **Flyable Player**, repeat the session, press F to enter/leave flight, and verify flight movement, camera, avatar loading, ownership, synchronization, and single-player spawning.
5. Inspect the active FishNet NetworkManager prefab collection after import and confirm both player prefabs are listed.
6. Open `Bootstrap.unity` and visually verify the MANGOs Gold label layout at supported aspect ratios.
7. Deploy the validation output from `Builds/MANGOsPWA_Validation_20260814` under `*.mangosgo.com`. Verify anonymous startup, credentialed `/users/me`, explicit login redirect/return, local disconnect, global logout, avatar URL resolution, and wallet refresh using a test account.
8. In the deployed build, click the microphone button, grant permission, verify join/mute/unmute/leave behavior, and confirm local and remote audio through the production Socket.IO and WHIP/WHEP services. Verify that voice starts only after explicit interaction and stops on disconnect.
9. Open the avatar selector on first entry and confirm every account avatar thumbnail appears before the selector becomes interactive. Select at least one VRoid/VRM avatar and one non-Avaturn legacy rig, then verify idle and movement animation, repeated avatar switching, local ownership, and remote-client animation synchronization.
10. Configure only public OAuth settings in `MangosAuthConfig`. Connect a trusted code-exchange backend before enabling a production third-party OAuth Login action.
11. Run Standalone/Mobile OAuth, EditMode, PlayMode, and production-backend integration tests before release.

## Cookie SSO Deployment Follow-up

The first deployed Cookie SSO check reached `GET https://api.mangosgo.com/api/v1/users/me` but returned `401` while the PWA service worker was controlling the request. The generated worker intercepted every request, cached cross-origin API responses without checking status, and attempted to cache non-GET requests. The browser console sequence was consistent with a cached anonymous `/users/me` response after login, and the same worker caused the observed `Cache.put` failure for POST requests.

The WebGL template now bypasses the service worker for cross-origin and non-GET requests, caches only successful same-origin GET responses, versions the static cache, deletes the legacy cache, and activates the updated worker immediately. Credentialed authentication fetches also use `cache: "no-store"`. A live preflight check confirmed that the API currently allows `https://plus.mangosgo.com` with credentials. If a fresh uncached request still returns `401`, the remaining issue is backend session-cookie scope or validity and must be verified by MANGOsAuth without exposing the HttpOnly cookie to Unity or JavaScript.

The follow-up WebGL deployment confirmed that an older worker had cached authenticated API routes and attempted to cache Cloudflare POST telemetry. The first cache correction used `static-v3` and removed the original and `static-v2` caches during activation.

Avatar loading logs also showed documented relative asset paths being converted to `file:///api/v1/...` by `System.Uri` on Windows. Asset resolution now handles root-relative paths before absolute-URI parsing, accepts only HTTP or HTTPS absolute assets, and resolves other relative paths against the configured API origin. The reported VRM asset was independently verified to return `200`, `model/gltf-binary`, and the correct credentialed CORS headers. Avatar selection now iterates over a stable, deduplicated URL snapshot so removing a repeatedly failing URL cannot invalidate the active coroutine index.

A subsequent deployed-client check proved that the origin server contained the corrected WebAssembly binary, but the live service worker was still `static-v2` and could continue returning its previously cached binary. The WebGL template now appends an explicit revision to every Unity build URL, uses a `static-v4` cache, deletes all earlier static cache generations, and applies network-first loading to navigation and `Build/` assets with cached fallback only on network failure. The avatar loader also normalizes asset URLs at the final load boundary and safely repairs only the known legacy `file:///api/v1/avatar-files/...` form. Arbitrary local-file URLs remain rejected.

Follow-up validation passed the targeted diff check, full C# solution build with zero errors, Service Worker template syntax check, and WebGL index template syntax check. The reported avatar asset returned `200`, `model/gltf-binary`, the expected content length, and credentialed CORS headers for `https://plus.mangosgo.com`. A fresh Unity WebGL build and complete deployment are still required before the `static-v4` worker and versioned build URLs can replace the currently deployed `static-v2` worker in user browsers.

## Cookie Session Restore and Legacy Humanoid Rig Follow-up

A browser-restart trace showed that the WebGL bridge sent a credentialed, no-store `GET /users/me` request and received a real backend `401` while the visible MANGOs UI appeared signed in. This is consistent with the API access session not yet being restored or the visible site state being stale; the client cannot distinguish those cases because the cookie is HttpOnly. The client still does not call the undocumented refresh route or read HttpOnly cookies. First-party startup now retries the same documented session check up to four total attempts over six seconds, allowing an independently restored shared cookie to become valid without forcing a login redirect. A final `401` still clears local authentication state and leaves the application anonymous as required. If all checks continue returning `401` while the MANGOs site has a verified server-side session, MANGOsAuth must provide backend evidence about shared access-cookie lifetime and restoration behavior.

The legacy V2 avatar loader contained additional hierarchy detection and Humanoid `Avatar` mappings that were absent from the target. The migration restores mappings for Mixamo/full-dress, Fruit, ShortBone, Monk, VRoid/VRM, and PH rigs while retaining target support for Avaturn, Ready Player Me, Masque, RajPattern, and the generic armature fallback. The copied files are standalone Unity Humanoid `Avatar` assets with their original `.meta` GUIDs; no legacy models, obsolete authentication code, or unrelated assets were copied. The existing target `AvatarController` already references animation clips whose GUIDs resolve in the target project.

The WebGL build revision is updated to `20260723-session-rigs-v1` so the rebuilt `.data`, `.framework.js`, loader, and `.wasm` URLs cannot reuse the earlier Unity IndexedDB entries.

Follow-up static validation passed with zero C# compilation errors. All twelve runtime Humanoid resource paths resolve, each of the six copied assets contains serialized skeleton, human, and transform-path data, both animation clip GUIDs used by `AvatarController` resolve in the target, and the updated WebGL template JavaScript parses successfully. Browser session-restoration timing and animated VRoid/VRM runtime behavior still require validation in a newly rebuilt and deployed WebGL client.

## Avatar Preview Rollback and RajPattern Rig Hotfix

The deployed `20260723-avatar-ui-rigs-v2` build proved that eagerly loading and cloning every account avatar for thumbnail capture was unsafe in WebGL. Production logs showed concurrent avatar downloads repeatedly failing with HTTP/2 protocol errors, followed by continuous `Bone weights do not match bones` errors and loss of the active player avatar. The complete thumbnail/profile/eye-stabilizer change was reverted instead of retaining partially connected capture behavior.

The remaining RajPattern regression was isolated to a rig-detection collision. `RajPattern_Blue.glb` and the legacy full-dress avatar both contain `Armature/mixamorig:Hips`, so the generic Mixamo check selected `full_derssAvatar` before the RajPattern-specific `Armature/Outfit` check. The overlapping generic bone path is no longer used to identify full-dress models. Full-dress selection now requires its unique `Cloth.001` and `avaturn_body.001` mesh markers, while RajPattern continues to use its unique `Armature/Outfit` marker. The WebGL build revision is `20260723-raj-rig-hotfix-v3` so the corrected binary cannot reuse the failed deployment's Unity cache entry.

## Flyable Player Camera Hotfix

The BaseWorld Editor configuration can select `NetworkPlayer_Flyable`. That mode intentionally starts with an unlocked cursor, but the legacy `ThirdPersonOrbitCamBasic` implementation returned from its entire update whenever the cursor was unlocked. Consequently, the camera remained at its pre-spawn position after the owned player was teleported to the world spawn point. Camera position and collision tracking now run regardless of cursor state; cursor locking only controls orbit input. Flyable camera initialization also clears stale Cinemachine Follow and LookAt targets and validates the local-player references before enabling the orbit component. The WebGL build revision is `20260723-flyable-camera-v4`.

An HTTP 500 response from `GET /users/me` is an API or upstream server failure. The First-party Cookie SSO request path still uses the documented GET route with browser `credentials: include` and `cache: no-store`; no client-side token or cookie extraction is attempted.

## Player Cursor Configuration

The Editor-selected player type now controls the initial cursor state consistently. `NetworkPlayer_Flyable` starts with a hidden cursor, while the standard `NetworkPlayer` starts with a visible cursor for pointer-based movement. Both modes use the `E` key to toggle the cursor. Browser pointer lock requires a user gesture, so authenticated and guest Continue actions acquire pointer lock synchronously from their button click before BaseWorld loads. Flyable initialization preserves that browser lock, while standard initialization releases it. The WebGL bridge still arms the next canvas click or key press if a browser rejects the initial request. This prevents an invisible but unfrozen pointer from reaching the screen edge at Flyable startup. Flyable camera orbit input follows cursor visibility while camera position tracking remains active in both states.

## MANGOs Gold HUD Layout Follow-up

The target Bootstrap scene contained a zero-size `MangosGoldText` RectTransform directly under the top HUD horizontal layout. Its text overflow rendered as a narrow white strip at the right edge, and the legacy MANGOs Gold logo asset and fixed-size group were absent. The legacy layout was reproduced as a `390 x 75` group containing a `75 x 75` MANGOs Gold logo and a `300 x 75` amount label. `MANGOs_Gold.png` and its original metadata were copied from V2 without modifying the legacy project. The UserData canvas now matches the legacy height-based CanvasScaler configuration so the full HUD remains within wide WebGL viewports, and the wallet amount continues to be populated from the documented wallet response through `UserReferencePersistent`.

The WebGL build revision is `20260723-pointer-gold-ui-v7`. Static validation passed for unique Bootstrap scene file IDs, the restored logo GUID and scene reference, JavaScript syntax, and C# compilation with zero errors. A fresh WebGL build and deployed-browser visual check remain required for pointer-lock behavior and responsive HUD placement.

## Avatar Switch and Gold HUD Reference Fix

The deployed `20260723-pointer-gold-ui-v7` build failed after a previously used avatar was selected again. The WebGL log showed successful glTF loads immediately before `RuntimeError: null function`, followed by recursive PlayerLoop warnings. The Bootstrap scene inspection found that the profile image component had accidentally been converted from `RawImage` to `Image`, while `UserDataCanvas.avatarImage` still required a `RawImage` and Unity cleared the incompatible serialized reference. Returning to a cached avatar produced a non-null thumbnail, called `SetAvatarImage`, and then dereferenced the missing UI component. The profile component and serialized binding are restored, and `SetAvatarImage` now guards a missing scene reference so a UI configuration fault cannot terminate the WebGL player.

The MANGOs Gold logo object was also still a `RawImage` with no texture assigned, which caused its default white rendering. It now references the verified `MANGOs_Gold.png` texture directly. The Gold group is no longer a top-level HUD layout item; it is a `300 x 42` child below the username area, with a `42 x 42` logo and a separate amount label. This keeps the wallet display close to the name/profile row without overlapping either control. The WebGL build revision is `20260723-avatar-switch-gold-ui-v8`.
