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

## Files Added

- `MANGOS_AUTH_UPGRADE_REPORT.md`
- `Assets/_Modules/Authentication/MangosApiClient.cs` and `.meta`
- `Assets/_Modules/Authentication/MangosAuthConfig.asset` and `.meta`
- `Assets/Plugins/RTC/Agora_Unity_WebGL_Refactor 10 Release/AgoraEngine/Plugins/WebGL/AgoraWebGLSDK.jslib` and its folder/plugin `.meta` files
- `Assets/_Project/Scripts/Runtime/PlayerTypeController.cs` and `.meta`
- The selected Flyable prefab, controller scripts, animation assets, controller, physics material, and `LockTarget` listed above

## Files Modified or Rewritten

- Rewritten: `AuthConfig.cs`, `ApiService.cs`, `Launcher.cs`, and `WebAuth.jslib`.
- Repository tracking: `.gitignore` now keeps the required Agora WebGL bridge while continuing to ignore the other platform-specific Agora plugin binaries.
- Merged/adapted: `NetworkedPlayerSpawner.cs`, `NetworkedPlayerComponent.cs`, `NetworkObjectSpecialMove.cs`, `UserReferencePersistent.cs`, and `UserDataCanvas.cs`.
- Scene/configuration: `BaseWorld.unity`, `Bootstrap.unity`, three FishNet prefab collections, and `InputManager.asset`.
- Adapted legacy copy: `BasicBehaviour.cs`, `ThirdPersonOrbitCamBasic.cs`, `CharacterController.controller`, and `NetworkPlayer_Flyable.prefab`.

## Components Deprecated

- The old OAuth popup and `metaauth_*` local-storage bridge.
- The v2 user-by-id startup flow.
- Client-side Basic credential generation and serialized OAuth secret.
- The obsolete mock authentication exchange.
- Legacy direct-buy coupling in the special-movement interaction.

## Backward Compatibility

- The existing authentication canvas events, guest flow, Continue-as-user flow, user singleton, avatar system, and network spawn lifecycle remain in place.
- `NetworkedPlayerSpawner.playerPrefab` is migrated to `standardPlayerPrefab` with `FormerlySerializedAs`.
- Legacy uppercase `AuthConfig` getters and the misspelled `GetAuthCodeReponse` type remain as obsolete compatibility members.
- Standard `NetworkPlayer` remains the default scene selection.

## Tests and Validation

Completed validation:

- Full C# solution build through the Unity-generated `Assembly-CSharp.csproj`: **0 errors**. Existing Unity/dependency assembly-version warnings remain; new browser-response DTO fields also produce expected `JsonUtility` assignment warnings.
- `.jslib` JavaScript syntax check: passed.
- Agora WebGL native-link coverage: all 429 active `DllImport` symbols in `IAgoraGamingRtcEngineNative.cs` have matching bridge exports; the bridge is byte-identical to the compatible legacy asset and its importer is enabled only for WebGL.
- Changed Scene/Prefab YAML duplicate object-id check: passed.
- Changed Scene/Prefab local fileID integrity check: passed.
- Missing script GUID check: passed.
- New asset GUID/reference check: passed. Two unrelated missing asset GUIDs already existed in `Bootstrap.unity` before this upgrade.
- Standard and Flyable prefab references are both assigned in `BaseWorld.unity`.
- Flyable prefab registration appears exactly once in each relevant FishNet collection.
- Required legacy input names are present without new duplicate entries.
- Obsolete authentication endpoint/storage scan: passed.
- Embedded authentication-secret assignment scan: passed for upgrade code and configuration.
- Credential logging scan: passed for upgrade code.
- Legacy source remained read-only.

Unity batch-mode compilation, EditMode tests, PlayMode tests, and interactive multiplayer validation could not run because this project was already open in another Unity Editor instance. Unity rejected a second process with `HandleProjectAlreadyOpenInAnotherInstance`. The successful C# solution build included the newly added scripts, but it does not replace PlayMode or build-target validation.

The first interactive WebGL build exposed 434 undefined Agora native symbols because the Agora plugin directory had been ignored and the required `AgoraWebGLSDK.jslib` was absent from the target clone. The compatible legacy bridge and its original WebGL-only importer metadata were restored, and static symbol and JavaScript syntax validation passed. A fresh interactive WebGL rebuild after Unity imports the restored bridge remains required.

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
- A fresh WebGL build after the Agora bridge restoration, a Standalone build, both PlayMode player configurations, and a multiplayer host/client session remain manual validation items.

## Manual Unity Editor Steps

1. Return focus to Unity, allow the restored Agora WebGL bridge to import, and confirm the Console has no script or import errors.
2. Open `Assets/_Project/Scenes/BaseWorld.unity`.
3. Select `PlayerTypeController`, disable **Flyable Player**, start a host/client session, and verify standard movement, camera, avatar loading, ownership, synchronization, and single-player spawning.
4. Stop Play Mode, enable **Flyable Player**, repeat the session, press F to enter/leave flight, and verify flight movement, camera, avatar loading, ownership, synchronization, and single-player spawning.
5. Inspect the active FishNet NetworkManager prefab collection after import and confirm both player prefabs are listed.
6. Open `Bootstrap.unity` and visually verify the MANGOs Gold label layout at supported aspect ratios.
7. Build WebGL into a fresh output directory. If the old undefined-symbol list persists, close Unity, remove the generated `Library/Bee` cache, reopen the project, and rebuild. Then deploy under `*.mangosgo.com` and verify anonymous startup, credentialed `/users/me`, explicit login redirect/return, local disconnect, global logout, avatar URL resolution, and wallet refresh using a test account.
8. Configure only public OAuth settings in `MangosAuthConfig`. Connect a trusted code-exchange backend before enabling a production third-party OAuth Login action.
9. Run Standalone/Mobile OAuth, EditMode, PlayMode, and production-backend integration tests before release.

## Cookie SSO Deployment Follow-up

The first deployed Cookie SSO check reached `GET https://api.mangosgo.com/api/v1/users/me` but returned `401` while the PWA service worker was controlling the request. The generated worker intercepted every request, cached cross-origin API responses without checking status, and attempted to cache non-GET requests. The browser console sequence was consistent with a cached anonymous `/users/me` response after login, and the same worker caused the observed `Cache.put` failure for POST requests.

The WebGL template now bypasses the service worker for cross-origin and non-GET requests, caches only successful same-origin GET responses, versions the static cache, deletes the legacy cache, and activates the updated worker immediately. Credentialed authentication fetches also use `cache: "no-store"`. A live preflight check confirmed that the API currently allows `https://plus.mangosgo.com` with credentials. If a fresh uncached request still returns `401`, the remaining issue is backend session-cookie scope or validity and must be verified by MANGOsAuth without exposing the HttpOnly cookie to Unity or JavaScript.
