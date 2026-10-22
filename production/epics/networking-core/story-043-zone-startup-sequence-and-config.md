# Story 043: Zone Startup S0–S10, S-fail and ZoneServerConfig

> **Epic**: Networking Core
> **Status**: Ready — `/story-readiness` 2026-10-22: NEEDS WORK (4 gaps), all fixed the same day at the user's request (AC-7 drain/flush gate `_tickStarted` set by S9 via `MarkTickStarted()`, a change to Story 042's composition; AC-12 names `IZoneBackendSteps`; performance note; estimate confirmed).
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 11 hours

*Added 2026-10-22 (`/create-stories networking-core`, ADR-009 Amendment 1 Migration Plan step 2, second of four). User decisions 2026-10-22: server config is a JSON file named by `-serverConfig <path>`, parsed with `JsonUtility` into `ZoneServerConfig`, with defaults and validation at S0; the password never goes in it (`IRONGRIND_PG_PASSWORD`, Story 044). S2 static data goes through an `IZoneStaticDataLoader` seam with a logging stand-in; production loaders and the game systems are wired by later stories of their own epics (EPIC Known Gap).*

## Context

**GDD**: `design/gdd/networking-core.md` (20 Hz tick, tick start after zone ready); `design/gdd/zone-instancing.md` (zone `Active` T-2 before routing); `design/gdd/navigation-pathfinding.md` (nav initialised before the tick). No rule or value of a GDD changes.
**Requirement**: `TR-net-008` (tick start gated on `ZoneNavigationService.Initialize()`; tick active before gateway registration); `TR-net-002` (tick rate 20 Hz, `Application.targetFrameRate = 20` on the server — the values handed to the NGO seam at S8)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: **ADR-009 Amendment 1** — Decision 2 (replaced, startup S0–S10, S-fail, coroutine structure, systems not built yet), Decision 5 (configuration values are server config).
Also: **ADR-006 Amendment 3** (the pool values and connection fields come from server config; password from the environment); **ADR-004** Decision 5 (one `AdvanceTick` per NGO tick event); **ADR-007** (no retry inside the process; supervision decides).

**ADR Decision Summary**: startup is one coroutine that calls eleven non-yielding step methods in a fixed order, each in its own `try`/`catch`; any failure tears down from the first step with something to undo and exits `StartupFailed`; a quit request during startup stops at the next step boundary and exits `Clean`. The zone becomes routable (S10) only when everything else runs.

**Engine**: Unity 6.3 LTS | **Risk**: LOW for this story — plain C# against the seams; `JsonUtility.FromJson` (stable API, available in `IronGrind.ServerLogic`).
**Engine Notes**: `JsonUtility` needs `[Serializable]` with public fields (no properties, no dictionaries); missing fields keep the field initialiser value, which is how the defaults apply. `NetworkConfig.TickRate` is a `uint` — the S8 seam call casts `ServerTickLoop.TICK_RATE_HZ` explicitly (ADR-009 A1 review note).

**Control Manifest Rules (Foundation, v2026-10-19)**:
- Required: startup strict order S0 → S10 as listed in the manifest's Scene/zone-load management section — ADR-009 A1
- Required: each step S0–S10 is a non-yielding method inside its own `try`/`catch`; the coroutine yields only between steps; a caught exception goes to S-fail — ADR-009 A1
- Required: S-fail logs the failing step (never the connection string), tears down from the first step with something to undo (skip the drain if no tick ran), exits `ZoneExitCode.StartupFailed`; a quit request during startup stops at the next step boundary and tears down with `Clean` — ADR-009 A1
- Required: `DB_WARMUP_TIMEOUT_MS` (5 000), `NGO_SHUTDOWN_WAIT_FRAMES` (100) and the ADR-006 Amendment 3 connection fields are server config, not constants — ADR-009 A1, ADR-006 A3
- Forbidden: `yield return` inside a `try` that has a `catch` in the startup coroutine — ADR-009 A1
- Forbidden: retrying a failed startup step inside the process — ADR-009 A1, ADR-007
- Forbidden: putting the database password in server config, or logging the connection string — ADR-006 A3, ADR-009 A1
- Guardrail: `Application.targetFrameRate = TICK_RATE_HZ`, `QualitySettings.vSyncCount = 0` (TR-net-002, ADR-002) — through the NGO seam at S8

---

## Acceptance Criteria

*From ADR-009 Amendment 1 Decision 2, scoped to this story:*

- [ ] **AC-1** `ZoneStartupArgs.Parse(string[] args)` reads `-zoneTemplate <name>`, `-zoneId <uint>`, `-port <ushort>` and `-serverConfig <path>`; a missing, repeated or unparsable value returns a failure naming the argument (no exception escapes).
- [ ] **AC-2** `ZoneServerConfig` is a `[Serializable]` class with public fields: database `Host`, `Port`, `Database`, `Username`; pool values `Timeout` 5, `CommandTimeout` 5, `MinPoolSize` 2, `MaxPoolSize` 10, `ConnectionIdleLifetime` 60; `DbWarmupTimeoutMs` 5000; `NgoShutdownWaitFrames` 100. Fields absent from the JSON keep these defaults.
- [ ] **AC-3** `ZoneServerConfig` validation at S0 rejects: empty `Host`, `Database` or `Username`; `Port` outside 1–65535; any timeout, wait or frame count ≤ 0; `MinPoolSize` < 0 or > `MaxPoolSize`; `MaxPoolSize` < 1; any field named like a password (`Password` present in the JSON) — the error names the field, never its value.
- [ ] **AC-4** `Start(args)` calls the steps in exactly this order: S0 args + config → S1 quit subscription, `DontDestroyOnLoad`, NGO object prepared (`EnableSceneManagement = false`) → S2 static data → S3 random → S4 database → S5 construct → S6 scene + nav + bounds → S7 listener start → S8 NGO start (tick rate, target frame rate, vSync 0, `StartServer`, shell start, outbound sender) → S9 zone `Active`, tick subscription → S10 gateway registration.
- [ ] **AC-5** The coroutine yields only between steps and while the S6 scene seam reports the load in progress; every step method is non-yielding and wrapped in its own `try`/`catch`.
- [ ] **AC-6** A step that throws or reports failure (S0 invalid input, S2 loader failure, S4 failure, S6 invalid bounds — zone `T-3`, S8 `StartServer()` returning `false`, any other exception) ends startup at once: one log line names the step (`"[ZoneServer] startup failed at S<n>: <ExceptionType>"`), no later step runs, `RequestTeardown(StartupFailed)` is called, and the teardown runs from the first step that has something to undo.
- [ ] **AC-7** If S-fail happens before S9 has completed the tick subscription, the teardown skips the outbound flush (step 4b) and the drain (step 5). Once S9 has completed, it runs them. The gate is `_tickStarted`, set through `MarkTickStarted()` by S9.
- [ ] **AC-8** A quit request (`OnWantsToQuit` in a player, or `RequestTeardown(Clean)`) received during startup lets the current step finish, runs no further step, and tears down with `Clean`; a later `StartupFailed` cannot override it (first code wins, Story 042).
- [ ] **AC-9** No step is retried: each step method is called at most once per process.
- [ ] **AC-10** S8 passes `(uint)ServerTickLoop.TICK_RATE_HZ` as the NGO tick rate and `ServerTickLoop.TICK_RATE_HZ` as the target frame rate, and vSync 0, to the NGO seam before calling start; S9's tick handler calls `ServerTickLoop.AdvanceTick(measured seconds)` exactly once per tick event.
- [ ] **AC-11** S10 (`IGatewayRegistration.RegisterZone(zoneId, port)`) is called only after the tick subscription of S9; S9 only after `IZoneNavigation.Initialize` and `IZoneBoundsValidator.Validate` succeeded in S6 (TR-net-008).
- [ ] **AC-12** In this story S3, S4, S5 and S7 run through `internal interface IZoneBackendSteps` with `bool InitializeRandom()` (S3), `bool OpenDatabase()` (S4), `bool ConstructObjects()` (S5) and `bool StartListener()` (S7). Each method may throw or return `false`, and either one is S-fail. Tests fake it. Story 044 supplies the real implementation without changing the step order.

---

## Implementation Notes

*Derived from ADR-009 Amendment 1 Decision 2 and Decision 5:*

- Files in `src/ServerLogic/ZoneServer/`: `ZoneStartupArgs.cs`, `ZoneServerConfig.cs` (with a static `TryLoad(string json, out ZoneServerConfig, out string error)` — file reading is a seam so tests pass JSON text, coding standard: no file I/O in unit tests), additions to `ZoneServerComposition.cs` and to the seams of Story 042 (`IZoneNetwork` gets `PrepareObject()`, `Configure(uint tickRate, int targetFrameRate, int vSyncCount)`, `bool StartServer()`; `IZoneApplication` gets `KeepAcrossSceneLoads()`; `IZoneScene` is new: `BeginLoad(string template)`, `bool IsDone`, `bool Succeeded`).
- Coroutine skeleton (matches the manifest's forbidden pattern):
  ```
  for each step in [S0..S10]:
      if (TeardownRequested) { break }            // quit during startup → Clean
      bool ok = RunStep(step)                     // non-yielding; try/catch inside; logs on failure
      if (!ok) { RequestTeardown(StartupFailed); break }
      while (step == S6 && !scene.IsDone) yield return null
  ```
  The S6 scene load is started inside `RunStep(S6a)` and its result checked inside `RunStep(S6b)`, so the wait loop has no `try` around it.
- **First step with something to undo.** S-fail calls the same `RunTeardown`. Story 042's null-guards skip every step whose object was never built.
- **Extra gate for the drain and flush.** Null-guards are not enough here, because Story 044 builds the queue and the outbound step in S5, before the S6–S8 failure points. Add `private bool _tickStarted` to `ZoneServerComposition`. An `internal void MarkTickStarted()` sets it, and S9 calls it once the tick subscription succeeds. Teardown steps 4b (flush) and 5 (drain) run only when the flag is `true`.
- **Change to Story 042's code (Complete).** This touches `ZoneServerComposition`. `ZsRig` in `ZoneServer_CompositionTeardown_tests.cs` calls `MarkTickStarted()`, so the 52 existing tests stay green unchanged. One new test there checks that a teardown without the flag has no `Flush` and no `Drain`.
- **Performance:** startup only (S0–S10 run once per process). On the tick path, `OnNetworkTick` adds one `Stopwatch` read and no allocation, so there is no impact on the 50 ms server tick budget at 20 Hz.
- The tick handler is a named method (`OnNetworkTick`), not a lambda, so step 1 can unsubscribe it (manifest: no lambda captures for persistent subscriptions). It measures the time since the previous tick with a `Stopwatch` and passes seconds to `AdvanceTick`.
- Zone `State` (T-2 `Active`, T-3 on invalid bounds) is set through a stand-in (`IZoneStateOwner`, logs once) — the owner is Zone Instancing.
- Config validation error messages carry the field name only.
- Doc comments on every public type and member.

---

## Out of Scope

- Story 042: teardown steps, exit codes, `OnWantsToQuit`, `TeardownNow`, stand-ins' log-once behaviour.
- Story 044: the real S3 random, S4 connection string, password and warm-up, S5 object graph, S7 listener string; the Database test.
- Story 045: Unity implementations of the seams (real `NetworkManager`, `SceneManager`, `Application`), the host, reading the config file from disk in the player.
- Static data loaders and game systems in S5 (later stories in their own epics — EPIC Known Gap).

---

## QA Test Cases

*Written at story creation (lean mode — QL-STORY-READY skipped). The developer implements against these.*

- **AC-1**: argument parsing
  - Given: `-zoneTemplate Forest -zoneId 7 -port 7777 -serverConfig cfg.json`. Then: parsed values equal.
  - Edge cases: each argument missing (4 cases) → failure names it; `-port 70000`, `-port -1`, `-zoneId abc` → failure; duplicate `-port` → failure; a flag with no value at the end → failure; unknown extra flags (e.g. Unity's `-batchmode`, `-nographics`, `-logFile`) → ignored.
- **AC-2**: defaults
  - Given: JSON with only `Host`, `Database`, `Username`. Then: every other field equals the listed default.
- **AC-3**: validation
  - Given: valid JSON with one field broken per case (empty `Host`, `Port` 0 and 65536, `DbWarmupTimeoutMs` 0, `NgoShutdownWaitFrames` -1, `MinPoolSize` 11 with `MaxPoolSize` 10, `MaxPoolSize` 0, a `Password` key). Then: failure naming that field; the error string does not contain the field's value.
  - Edge cases: malformed JSON → failure, no exception; `MinPoolSize` = `MaxPoolSize` → valid.
- **AC-4**: startup order
  - Given: every seam faked, appending to one call log. When: `Start` driven to the end. Then: the log matches S0…S10 in order, including the S8 sub-order (configure, start server, shell start, outbound sender).
- **AC-5**: yields
  - Given: scene fake not done for 3 polls. Then: exactly 3 yields, all inside S6; with an instant scene, zero yields — no yield outside S6.
- **AC-6**: S-fail per step
  - Given: a fault injected at each of S0–S10 in turn (11 cases, throw and, where a step returns a result, return failure). Then: one log naming the step and exception type; no later step in the call log; `Quit(1)`; teardown calls only for objects built before the failing step.
  - Edge cases: S8 `StartServer` returns `false` → S-fail at S8; S6 bounds invalid → zone state T-3 recorded, S-fail.
- **AC-7**: drain skipped before the first tick
  - Given: fault at S7. Then: no `Drain`, no `Flush` in the call log. Given: fault at S10. Then: `Flush` and `Drain` present.
  - Given: Story 042's teardown fixture without `MarkTickStarted()`. Then: no `Flush`, no `Drain`; with it, the existing 52 tests unchanged.
- **AC-8**: quit during startup
  - Given: `OnWantsToQuit()` called (player) while S4 runs. Then: S4 completes; S5 not called; teardown; `Quit(0)`.
  - Edge cases: quit during the S6 wait → no S7; quit, then a later step fault → still `Quit(0)`.
- **AC-9**: no retry
  - Given: a step fake that fails once then would succeed. Then: called once; `Quit(1)`.
- **AC-10**: tick values
  - Given: NGO fake. Then: `Configure(20u, 20, 0)` before `StartServer`; three tick events → three `AdvanceTick` calls with positive durations.
- **AC-11**: gating
  - Covered by AC-4's order; plus: nav `Initialize` throws → no tick subscription, no `RegisterZone`.
- **AC-12**: backend seam
  - Given: the backend fake. Then: S3, S4, S5, S7 reach it in order; nothing in this story touches Npgsql.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/Networking/ZoneServer_CompositionStartup_tests.cs` and `tests/EditMode/Networking/ZoneServer_Config_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 042 (Done)
- Unlocks: Story 044
