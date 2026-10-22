# Story 045: ZoneServerHost, the Unity Seams, the Server Scene and Local Verification

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 15 hours (confirmed at `/story-readiness` 2026-10-22: 14 h + 1 h for AC-15 in the composition)

*Added 2026-10-22 (`/create-stories networking-core`, ADR-009 Amendment 1 Migration Plan step 2, fourth of four). Implements the seams of Stories 042–044 over Unity and NGO, adds the host `MonoBehaviour`, the server scene, an empty zone template scene and a Build Profile, and runs the checks this machine can run: A1-3, A1-6 and a first A1-4 measurement on the Windows Dedicated Server (Mono) build, A1-5 and ADR-015 Verification 6 in the Editor, ADR-015 Verification 4 on the Windows build. A1-1, A1-2, A1-7 and the final A1-4 are carried by the epic's Linux IL2CPP headless-build gate (ADR-009 A1 review 2026-10-19, as Story 041 did for its VR-2). Windows has no SIGTERM, so V4 is triggered by a development-build-only `-teardownAfterSeconds <n>` argument.*

## Context

**GDD**: `design/gdd/networking-core.md` (20 Hz tick; the server process); `design/gdd/zone-instancing.md` (one process per zone; routable only when running). No rule or value of a GDD changes.
**Requirement**: `TR-net-002` (`NetworkManager` tick at 20 Hz, `Application.targetFrameRate = 20` on the server — first measurement here); `TR-net-008` (tick start/stop ordering, now over the real engine)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: **ADR-009 Amendment 1** — Decision 2 S1, S6, S8, S9; Decision 3 steps 1, 6, 8, Quit requests, Editor and `OnDestroy`; Engine Compatibility (A1-1 to A1-7).
Also: **ADR-004** (NGO substrate; `NetworkManager.NetworkTickSystem.Tick`; Amendment 1 unnamed custom messages — the transport shell of Story 038); **ADR-015** Decision 3 and Verification 4, 6 (listener stopped by teardown and by the host's `OnDestroy`); **ADR-012** (host in `IronGrind.ServerLogic`; server-only scenes are not in the client build profile's scene list); **ADR-009** Decision 1 (`SceneManager.LoadSceneAsync(…, LoadSceneMode.Single)`).

**ADR Decision Summary**: one thin `ZoneServerHost` forwards `Start`, `Update`, `OnDestroy` and `Application.wantsToQuit` to `ZoneServerComposition`; NGO, the scene load and `Application` are reached through seam implementations; the process exits only through `Application.Quit(int)`; leaving Play mode in the Editor runs teardown 1–7 from `OnDestroy`.

**Engine**: Unity 6.3 LTS (6000.3.10f1) | **Risk**: HIGH — `Application.wantsToQuit` / `Application.Quit(int)` in a Dedicated Server player, `DontDestroyOnLoad` across a Single-mode load, `NetworkTickSystem.Tick` (can fire more than once per frame), `NetworkManager.StartServer()` return value, `ShutdownInProgress`, `OnDestroy` order when leaving Play mode — all listed as post-cutoff / unverified by ADR-009 A1.
**Engine Notes**: Engine facts in `docs/engine-reference/unity/modules/networking.md` (read from NGO 2.13.3 source 2026-10-09). NGO's `DontDestroy = true` (default) already makes the root `NetworkManager` object `DontDestroyOnLoad` — S1 only keeps it a root object. `NetworkConfig.TickRate` is `uint`. This machine has only the Windows Dedicated Server (Mono) module for 6000.3.10f1 (Story 041); the Linux IL2CPP module is not installed. Building a Windows Server profile adds `com.unity.dedicated-server` and flips settings — revert those and commit only the profile (memory: server build side effects).

**Control Manifest Rules (Foundation, v2026-10-19)**:
- Required: one `ZoneServerHost` `MonoBehaviour` in `IronGrind.ServerLogic`, in a scene only the Dedicated Server build includes, owning one `ZoneServerComposition`; it holds no game logic and forwards `Start` (coroutine), `Update`, `OnDestroy`, `Application.wantsToQuit` — ADR-009 A1
- Required: S1 `NetworkManager` on a root object outside the zone scene, `DontDestroy = true`, `NetworkConfig.EnableSceneManagement = false` — ADR-009 A1
- Required: S8 `NetworkConfig.TickRate = (uint)ServerTickLoop.TICK_RATE_HZ`, `Application.targetFrameRate = TICK_RATE_HZ`, `QualitySettings.vSyncCount = 0`; `StartServer()` false → S-fail — ADR-009 A1, TR-net-002
- Required: teardown step 6 — `NgoInboundTransportShell.Stop()`, `NetworkManager.Shutdown()`, yield while `ShutdownInProgress` at most `NGO_SHUTDOWN_WAIT_FRAMES` frames — ADR-009 A1
- Required: the host's `OnDestroy` runs steps 1–7 synchronously unless the teardown already finished; `NpgsqlRelayListener.Stop()` reached from there — ADR-009 A1, ADR-015
- Required: server-only components live in server-only scenes not in the client build profile's scene list — ADR-012
- Forbidden: `Process.Exit` / `Environment.Exit`; teardown in `OnApplicationQuit`, a tick or the `wantsToQuit` handler; `true` from `OnWantsToQuit` in a player before step 8 — ADR-009 A1
- Forbidden: a server `MonoBehaviour` in client content — ADR-012
- Guardrail: listener stop ≤ `RELAY_LISTENER_STOP_TIMEOUT_MS` (2 000); NGO shutdown cap 100 frames — ADR-015, ADR-009 A1

---

## Acceptance Criteria

*From ADR-009 Amendment 1 (Decisions 2, 3, Engine Compatibility A1) and ADR-015 Verification 4 and 6, scoped to this story:*

- [ ] **AC-1** `ZoneServerHost` (`src/ServerLogic/ZoneServer/ZoneServerHost.cs`) builds the composition in `Awake` with `ZoneServerComposition.CreateWithRealBackend(application, network, navigation, closeSteps, scene, bounds, staticData, zoneState, gateway, configSource, new ProcessZoneEnvironment(), null)` — Unity seams of AC-2 to AC-4 plus the Story 044 stand-ins for navigation, close steps, bounds, static data, zone state and gateway — and forwards only: `Start` → starts the composition's `Start(args)` coroutine; `Update` → starts `RunTeardown()` once when a teardown was requested; `OnDestroy` → `TeardownNow()`; `Application.wantsToQuit` → `OnWantsToQuit()` (subscribed by S1 through the seam, unsubscribed at teardown). `args` is `Environment.GetCommandLineArgs()` in a player; in the Editor it is the host's `[SerializeField] private string[] _editorArgs` (field and its use inside `#if UNITY_EDITOR`, used only when `Application.isEditor`). It contains no other logic (a review check plus a source search: no `new` of a type from `IronGrind.ServerLogic.Networking`, `CharacterPersistence` or `Randomness` in the host file).
- [ ] **AC-2** `UnityZoneApplication : IZoneApplication` (constructor takes the host's `GameObject`) implements `IsEditor` (`Application.isEditor`), `Quit(int)` (`Application.Quit(code)`), `SubscribeWantsToQuit` / `UnsubscribeWantsToQuit` (`Application.wantsToQuit += / -=`), `KeepAcrossSceneLoads()` (`Object.DontDestroyOnLoad(hostObject)`). `FileZoneConfigSource : IZoneConfigSource` reads the `-serverConfig` file with `File.ReadAllText` (the only file I/O). Environment variables use the existing `ProcessZoneEnvironment` (Story 044); no new environment seam. The development-build check of AC-6 is the compile symbol, not a seam member.
- [ ] **AC-3** `NgoZoneNetwork : IZoneNetwork` implements S1 `PrepareObject()` (root `NetworkManager` object outside the zone scene with `UnityTransport` as `NetworkConfig.NetworkTransport`, `DontDestroy = true`, `EnableSceneManagement = false`), S8 `Configure(tickRate, targetFrameRate, vSyncCount)` (`NetworkConfig.TickRate`, `Application.targetFrameRate`, `QualitySettings.vSyncCount`) and `StartServer()` (result returned), `SubscribeTick` / `UnsubscribeTick` on `NetworkManager.NetworkTickSystem.Tick` (unsubscribe null-safe), `CreateSendSeam()` → `new NgoClientMessageSender(manager)`, `CreateInboundTransport(intake, dispatcher, clientListener)` → `NgoZoneInboundTransport : IZoneInboundTransport`, which wraps `new NgoInboundTransportShell(manager, intake, dispatcher, clientListener)` and forwards `Start()` / `Stop()`, and step 6 (`Shutdown()`, `IsAlive`, `ShutdownInProgress`), null-guarding a destroyed manager.
- [ ] **AC-4** `UnitySceneLoader : IZoneScene` loads `-zoneTemplate` with `SceneManager.LoadSceneAsync(name, LoadSceneMode.Single)` and reports `IsDone` / `Succeeded`; an unknown scene name (`LoadSceneAsync` returns null) reports done and not succeeded, which is S-fail at S6. `AddNavMeshData()` returns `default(NavMeshDataInstance)` and logs `[StandIn] no zone NavMesh data` once (no zone definition or baked NavMesh exists yet). Nav and bounds stay stand-ins.
- [ ] **AC-5** Scenes and profile: `Assets/Scenes/Server/ZoneServer.unity` holds one root GameObject with `ZoneServerHost`; `Assets/Scenes/Zones/ZoneTemplate_Empty.unity` is an empty zone scene; Build Profile `Assets/Settings/Build Profiles/ZoneServer.asset` (Windows Server, development) lists only those two scenes; neither scene is in any client profile's scene list.
- [ ] **AC-6** A development-build-only `-teardownAfterSeconds <n>` argument calls `RequestTeardown(Clean)` after `n` seconds of server time; in a release build the argument is ignored (compiled out with `#if DEVELOPMENT_BUILD || UNITY_EDITOR`).
- [ ] **AC-7** (A1-3, Windows Dedicated Server build) The host object and the `NetworkManager` object both survive `LoadSceneAsync(…, Single)` of the zone template: logged instance ids before and after match.
- [ ] **AC-8** (A1-6, Windows Dedicated Server build) With `EnableSceneManagement = false`, the plain `LoadSceneAsync` of S6 completes, NGO starts afterwards (S8) and the player log has no NGO scene-management warning or error.
- [ ] **AC-9** (A1-4 first measurement, Windows Dedicated Server build, batch mode) Over at least 60 s of server time, `NetworkTickSystem.Tick` fires 20 ± 1 times per second of server time; ticks per frame observed are only 0, 1 or 2 (histogram logged). Recorded as a first measurement; the final A1-4 stays with the Linux IL2CPP gate.
- [ ] **AC-10** (ADR-015 Verification 4, Windows Dedicated Server build, portable PostgreSQL) Started with `-teardownAfterSeconds 30`, the process runs teardown 1–8 in order (one log line per step), the listener thread stops within `RELAY_LISTENER_STOP_TIMEOUT_MS`, and the process exits with status 0; a second run with a config whose database is unreachable exits with status 1.
- [ ] **AC-11** (A1-5 and ADR-015 Verification 6, Editor, portable PostgreSQL) With the zone running in Play mode on the real listener, leaving Play mode is not blocked by the `wantsToQuit` handler, the host's `OnDestroy` runs teardown 1–7 without step 8, the listener thread stops within `RELAY_LISTENER_STOP_TIMEOUT_MS`, and no `NullReferenceException` appears also when `NetworkManager.OnDestroy` ran first (the order observed is logged).
- [ ] **AC-12** A1-1, A1-2, A1-7 and the final A1-4 are recorded in the evidence document as NOT RUN — carried by the epic's Linux IL2CPP headless-build gate — and the EPIC's engine-risk gate note lists them.
- [ ] **AC-13** After the Build Profile is created, the side effects of a Windows Server build (`com.unity.dedicated-server` in `Packages/manifest.json`, changed `ProjectSettings`) are reverted; only the profile asset, the scenes and the code are committed.
- [ ] **AC-14** The host never interrupts a running `RunTeardown()` coroutine: it does not call `StopAllCoroutines` / `StopCoroutine`, never deactivates or disables itself (`SetActive(false)`, `enabled = false`) and never destroys itself or its GameObject (`Destroy`); a source search of `ZoneServerHost.cs` (comments stripped) finds none of these. *(Added 2026-10-22, Story 042 `/code-review`, user decision: `ZoneServerComposition.OnWantsToQuit()` keeps returning `false` until step 8, so a host killed mid-teardown in a player would leave `TeardownNow()` done and quit blocked; the host must not reach that state.)*
- [ ] **AC-15** `ZoneServerComposition.OnNetworkTick` catches any exception from `AdvanceTick` so none escapes into NGO's `Tick` event: it logs `[ZoneServer] tick failed: <exception type name>` once (no message, no stack) and calls `RequestTeardown(ZoneExitCode.Fatal)`; later ticks that throw log nothing more and request nothing more. *(Added 2026-10-22, `/story-readiness`, user decision: the catch sits in the composition, not the host.)*

---

## Implementation Notes

*Derived from ADR-009 Amendment 1 and ADR-015 Decision 3:*

- Seam implementations in `src/ServerLogic/ZoneServer/Unity/` (still `IronGrind.ServerLogic`, compiled under `UNITY_SERVER || UNITY_EDITOR` like the rest of the assembly).
- The host builds the composition with the Unity seams and the stand-ins in `Awake` or the first line of `Start` — that construction is wiring of seams, not of server systems; all system construction stays inside `ZoneServerComposition` (Story 044's source search must still pass).
- Tick handler: `NetworkTickSystem.Tick` is `event Action` and may fire twice in one frame; each event is one `AdvanceTick` (ADR-004 Decision 5). Measure with a `Stopwatch` between events.
- Step 6 in a player: yield `null` per frame while `IsAlive && ShutdownInProgress`, cap from config. In `TeardownNow` (`OnDestroy`): call `Shutdown()` only, no wait.
- `OnWantsToQuit` in the Editor returns `true` (ADR-009 A1 unity-specialist BLOCKING fix) — the Editor path is `OnDestroy`.
- Keep the teardown coroutine alive to step 8 (AC-14). In a player, `OnWantsToQuit()` returns `true` only after step 8 sets the quit flag; if the coroutine is cut short (host stopped, disabled, deactivated or destroyed) while it waits in step 6, `OnDestroy` → `TeardownNow()` finishes steps 1–7 but nothing ever calls `Quit`, and later quit requests are refused. The host lives on a `DontDestroyOnLoad` root (S1) and starts the coroutine on itself. Story 042 test `test_teardown_now_while_suspended_in_ngo_wait_runs_dispose_pass_only_and_never_quits` pins the composition side of this.
- `IsAlive` in `NgoZoneNetwork` uses Unity's overloaded `==` on the `NetworkManager` (`manager != null`), never `?.` or `is null`, so a destroyed manager reads as not alive (Story 042 `/code-review`). The NGO shutdown cap counts frames, so its wall time depends on `targetFrameRate` set at S8; log the observed wait in the AC-10 run.
- Verification runs follow Story 041's pattern: build manually from the Build Profile to git-ignored `Builds/ZoneServer/`; two command lines (V4 happy path, V4 unreachable DB) and one Editor session; logs captured with `-logFile`; exit status read in PowerShell (`$LASTEXITCODE` after `Start-Process -Wait -PassThru` → `.ExitCode`). Portable PostgreSQL started via PowerShell `Start-Process` (memory: portable dev PostgreSQL). `IRONGRIND_PG_PASSWORD` set per process, never echoed.
- Server config for the runs: a JSON file under `Builds/ZoneServer/` (git-ignored), host `127.0.0.1`, the portable PG port.
- Evidence document: `production/qa/evidence/networking-core-045-zone-process-verification.md` with, per check, the command line (password redacted), the relevant log excerpt, the exit status and PASS / FAIL / NOT RUN.
- **Notes from Story 043 `/code-review` (2026-10-22):**
  - Tick exceptions: caught in `ZoneServerComposition.OnNetworkTick` (AC-15; user decision at `/story-readiness`). This changes a Story 043 file; it is in the Files list.
  - `NgoZoneNetwork.PrepareObject()` (S1) attaches `UnityTransport` to `NetworkConfig.NetworkTransport`, keeps the `NetworkManager` a root object with NGO's `DontDestroy = true`, and sets `EnableSceneManagement = false`; `Configure` sets `NetworkConfig.TickRate` (`uint`) before `StartServer`, while the manager is not listening.
  - `UnsubscribeTick` must be null-safe: `NetworkTickSystem` exists only after `StartServer` and may be gone after a shutdown or a failed S8.
  - The real `IZoneNavigation.Teardown` removes the `NavMeshDataInstance` passed to `Initialize` and tolerates a partial `Initialize` (the composition calls it whenever `Initialize` was called, also when it threw).
  - `JsonUtility` ignores a type mismatch (`"Port":"abc"` keeps the default) — the `IZoneConfigSource` file is hand-written, so the AC-10 run's config is checked by eye.
- Doc comments on every public type and member.
- **Performance**: the host adds one `Update` check per frame and the tick handler a try/catch (no allocation on the non-throwing path); the AC-9 probe runs only in development builds. No other runtime cost.

### Files

- New `src/ServerLogic/ZoneServer/ZoneServerHost.cs`
- New `src/ServerLogic/ZoneServer/Unity/UnityZoneApplication.cs`, `FileZoneConfigSource.cs`, `NgoZoneNetwork.cs`, `NgoZoneInboundTransport.cs`, `UnitySceneLoader.cs` (+ `.meta`)
- Changed `src/ServerLogic/ZoneServer/ZoneServerComposition.cs` (AC-15 only)
- New `Assets/Scenes/Server/ZoneServer.unity`, `Assets/Scenes/Zones/ZoneTemplate_Empty.unity`, `Assets/Settings/Build Profiles/ZoneServer.asset`
- New `tests/PlayMode/Networking/ZoneServer_UnitySeams_tests.cs`; new `tests/EditMode/Networking/ZoneServer_CompositionTick_tests.cs` (AC-15); EditMode source-search tests for AC-1, AC-6, AC-14 in the same file or `ZoneServer_HostSource_tests.cs`
- New `production/qa/evidence/networking-core-045-zone-process-verification.md`; EPIC.md engine-risk note (AC-12)

---

## Out of Scope

- Stories 042–044: composition logic, startup and teardown order, config, object graph, database test (Done before this story).
- Linux Dedicated Server IL2CPP build and A1-1, A1-2, A1-7, final A1-4 (the epic's headless-build gate; needs the Linux IL2CPP module).
- systemd unit, `TimeoutStopSec`, `Restart=always`, exit-code handling by supervision (ADR-007 C1 amendment).
- Real nav, bounds, CR-ZI-12, gateway, character directory, activity sink, static data loaders and game systems (their epics); the zone's baked NavMesh behind `AddNavMeshData()` (Zone Instancing / Navigation epic).
- Client build profile changes beyond confirming the server scenes are absent.

---

## QA Test Cases

*Written at story creation (lean mode — QL-STORY-READY skipped). Automated where an EditMode or PlayMode test can reach it; the engine checks are manual runs recorded in the evidence document.*

- **AC-1**: host forwards only
  - Given: the host file. Then: source search finds no construction of a type from `IronGrind.ServerLogic.Networking`, `CharacterPersistence` or `Randomness`; review confirms the four forwards.
- **AC-2 / AC-3 / AC-4**: seam implementations
  - PlayMode test (Editor, loopback, as Story 038/039): `NgoZoneNetwork` S1 + S8 + `StartServer()` true; three `Tick` events observed → three handler calls; step 6 `Shutdown()` then `ShutdownInProgress` false within the cap. `UnitySceneLoader` with an unknown name reports failure.
- **AC-5**: profile scene list
  - Manual: open the ZoneServer Build Profile → scene list = the two scenes. Search client profiles' scene lists → neither scene present. Record in the evidence doc.
- **AC-14**: host never interrupts the teardown
  - Given: `ZoneServerHost.cs` with comments stripped. Then: no `StopAllCoroutines`, `StopCoroutine`, `SetActive(false)`, `enabled = false`, `Destroy(`.
- **AC-6**: development-only argument
  - Given: source. Then: the parsing branch sits inside `#if DEVELOPMENT_BUILD || UNITY_EDITOR`.
- **AC-7** (A1-3) — Manual check
  - Setup: Windows Dedicated Server development build, portable PG running, `-zoneTemplate ZoneTemplate_Empty`.
  - Verify: log lines with the host's and `NetworkManager`'s instance ids before S6 and after S6.
  - Pass condition: both ids identical before and after; both objects non-null after the load.
- **AC-8** (A1-6) — Manual check
  - Verify: player log from S6 to S9.
  - Pass condition: scene loaded, `StartServer()` true, no line containing `NetworkSceneManager` warnings or errors.
- **AC-9** (A1-4, first) — Manual check
  - Setup: `-batchmode -nographics`, 60 s run.
  - Verify: the per-second tick count summary and the ticks-per-frame histogram logged by a development-only probe.
  - Pass condition: every full second 19–21 ticks; histogram has keys only 0, 1, 2.
- **AC-10** (V4) — Manual check
  - Setup: same build, `-teardownAfterSeconds 30`; second run with an unreachable database port.
  - Verify: teardown step lines 1, 2a, 3, 4, 4b, 5, 6, 7, 8 in order; listener stop time; exit status.
  - Pass condition: order exact; listener stopped ≤ 2 000 ms; status 0 for the first run, 1 for the second; no connection string in either log.
- **AC-11** (A1-5, V6) — Manual check
  - Setup: open `ZoneServer.unity` in the Editor; the host's `_editorArgs` field holds `-zoneTemplate ZoneTemplate_Empty -zoneId <n> -port <p> -serverConfig <Builds/ZoneServer/ config path>`; `IRONGRIND_PG_PASSWORD` set in the Editor's environment; portable PG running.
  - Verify: press Stop; Console shows `OnDestroy` teardown lines 1–7, the listener stop time, the order of `NetworkManager.OnDestroy` vs the host's.
  - Pass condition: Play mode exits at once; steps 1–7 logged, no step 8; listener stopped ≤ 2 000 ms; no exceptions. Repeat 3 times.
- **AC-15**: tick exception contained (EditMode)
  - Given: a composition whose tick loop's `AdvanceTick` throws (a registered tick-driven fake). When: the tick handler is called twice. Then: no exception escapes; one `tick failed: <type>` line (`LogAssert.Expect`); `RequestTeardown(Fatal)` recorded once; the exit code is Fatal.
- **AC-4** (scene seam, PlayMode): `AddNavMeshData()` returns `default` and logs the stand-in line once over two calls.
- **AC-12**: carried checks
  - Pass condition: the evidence doc lists A1-1, A1-2, A1-7 and final A1-4 as NOT RUN with the reason; EPIC engine-risk note updated.
- **AC-13**: side effects reverted
  - Pass condition: `git status` after the builds shows only the intended files; `Packages/manifest.json` has no `com.unity.dedicated-server`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/PlayMode/Networking/ZoneServer_UnitySeams_tests.cs` — PlayMode loopback test of the NGO and scene seams; must pass
- `tests/EditMode/Networking/ZoneServer_CompositionTick_tests.cs` — AC-15, plus the AC-1 / AC-6 / AC-14 source searches; must pass
- `production/qa/evidence/networking-core-045-zone-process-verification.md` — the A1-3, A1-4 (first), A1-5, A1-6, ADR-015 V4 and V6 runs, plus the NOT RUN list

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 044 (Done); Stories 038, 039 (Complete — the inbound shell and the send seam it constructs); Windows Dedicated Server Build Support module for 6000.3.10f1 (installed for Story 041)
- Unlocks: Zone Instancing / Navigation stories that replace the stand-ins; the epic's Linux IL2CPP headless-build gate (adds A1-1, A1-2, A1-7, final A1-4)
