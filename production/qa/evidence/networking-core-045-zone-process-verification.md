# Story 045 - Zone Server Process Verification (ADR-009 A1-3, A1-4 first, A1-5, A1-6; ADR-015 V4, V6)

> **Story**: `production/epics/networking-core/story-045-zone-server-host-unity-seams-verification.md`
> **Host under test**: `src/ServerLogic/ZoneServer/ZoneServerHost.cs` on the root GameObject `ZoneServerHost` in `Assets/Scenes/Server/ZoneServer.unity`
> **Code under test**: `ZoneServerComposition` (startup S0-S10, teardown 1-8), `NgoZoneNetwork`, `UnitySceneLoader`, `UnityZoneApplication`, `FileZoneConfigSource`, `NgoZoneInboundTransport`, `NpgsqlRelayListener` (`src/ServerLogic/ZoneServer/`, `src/ServerLogic/Networking/CrossZoneRelay/`)
> **Date**: 2026-10-22 (first build 15:44 and first runs after it; rebuild 16:21, reruns ending 16:23; Editor cycles ending 16:28)
> **Platform**: Windows Dedicated Server, Mono, Development Build (Build Profile `Assets/Settings/Build Profiles/ZoneServer.asset`, Subtarget Server, scenes `ZoneServer` and `ZoneTemplate_Empty`), `-batchmode -nographics`; Unity 6000.3.10f1, NGO 2.13.3. Deviation from the IL2CPP Linux server of ADR-007, as in Story 041.
> **Database**: portable PostgreSQL 16.15, 127.0.0.1:5432, database and role `irongrind`; password from `IRONGRIND_PG_PASSWORD`, set per process from the secrets file and never printed
> **Status**: RUN. AC-5 PASS, AC-13 PASS, AC-7 PASS, AC-8 PASS, AC-9 PASS with a note (first measurement), AC-10 PASS, AC-11 PASS. A1-1, A1-2, A1-7 and the final A1-4 NOT RUN (AC-12).

## Build and run procedure

1. Build: Build Profile `ZoneServer`, Development, output `Builds/ZoneServer/IronGrind.exe` (git-ignored). The first build was made in the Editor; the rebuild after the evidence logging was made in batch mode with `-activeBuildProfile "Assets/Settings/Build Profiles/ZoneServer.asset" -build ...` ("Build Finished, Result: Success."). `IronGrind_Data/Managed` holds `IronGrind.ServerLogic.dll` and `Npgsql.dll`, so the build compiled with `UNITY_SERVER`.
2. Side effects of the Server build (AC-13) were reverted after each build: `com.unity.dedicated-server` 2.0.2 in `Packages/manifest.json` and `packages-lock.json`, `ProjectSettings/MultiplayerManager.asset` (Multiplayer Roles on), `ProjectSettings.asset` (Standalone batching entry), `UnityConnectSettings.asset` (`m_Enabled: 1`), URP prefiltering in `Assets/Settings/PC_RPAsset.asset` and `UniversalRenderPipelineGlobalSettings.asset`. Generated `ProjectSettings/Packages/com.unity.dedicated-server/` and `ProjectSettings/SceneTemplateSettings.json` are not committed.
3. Config `Builds/ZoneServer/zone-config.json`: `{ "Host": "127.0.0.1", "Port": 5432, "Database": "irongrind", "Username": "irongrind" }`.
4. Three runs from one PowerShell script, one after another, each with `Start-Process -Wait -PassThru` for the exit code:

```
IronGrind.exe -batchmode -nographics -logFile <run>.log -zoneTemplate ZoneTemplate_Empty -zoneId 1 -port 7777 -serverConfig <config> -teardownAfterSeconds <n>
```

| Run | Config | `-teardownAfterSeconds` | Exit | Wall time |
|-----|--------|-------------------------|------|-----------|
| A | `zone-config.json` | 75 | 0 | 76.7 s |
| B | `zone-config.json` | 30 | 0 | 31.2 s |
| C | same, `Port` 5499 (nothing listens), `Timeout` 2, `DbWarmupTimeoutMs` 3000 | 30 | 1 | 4.1 s |

The table is the rerun after the rebuild. A first set of runs (same exit codes 0, 0, 1) had no per-step teardown lines and counted the startup second; see AC-9 and AC-10.

## AC-5 Scenes and Build Profile

**PASS** if `ZoneServer.unity` holds one root GameObject with `ZoneServerHost`, `ZoneTemplate_Empty.unity` is empty, the `ZoneServer` profile lists only those two scenes and no client profile lists either.

**Result**: PASS (checked 2026-10-22 from the asset files).

- `Assets/Scenes/Server/ZoneServer.unity`: one root GameObject (`m_Father: {fileID: 0}` once), named `ZoneServerHost`, with the `ZoneServerHost` component and its 8 `_editorArgs`.
- `Assets/Scenes/Zones/ZoneTemplate_Empty.unity`: no GameObject (only the scene settings objects).
- `Assets/Settings/Build Profiles/ZoneServer.asset`: `m_Subtarget: 1` (Server), `m_Development: 1`, `m_OverrideGlobalSceneList: 1`, scenes `Assets/Scenes/Server/ZoneServer.unity` and `Assets/Scenes/Zones/ZoneTemplate_Empty.unity` only.
- Other scene lists: `ProjectSettings/EditorBuildSettings.asset` (the platform default list) holds `Assets/Scenes/SampleScene.unity` only; the only other profile, `RelayVerification.asset` (Story 041, a server verification profile), holds `Assets/Scenes/Verification/RelayVerification.unity` only. Neither server scene is in a client list.

## AC-7 (A1-3) Host and NetworkManager survive `LoadSceneAsync(..., Single)`

**PASS** if the instance ids logged before and after the zone template load are equal for both objects.

**Result**: PASS. Runs A and B: `[ZoneServer] A1-3 before load: host object id=9680, NetworkManager object id=-98` and `after load:` with the same ids. First set of runs: 9686 / -98 before and after. Editor (AC-11), 3 cycles: host 50236, NetworkManager -2458, -3292, -4122, each equal before and after.

## AC-8 (A1-6) Plain scene load with NGO scene management off

**PASS** if S6 completes, NGO starts afterwards (S8) and the player log has no NGO scene-management warning or error.

**Result**: PASS. In runs A and B the scene load finished (the `after load` line, then the S6 stand-in lines), `StartServer` succeeded (tick probe lines follow) and the log has no `[Netcode]` warning or error and no scene-management message. The only error-level lines in the player logs are Unity's two `Built-in Resource Error: dereference potentially before BuiltinResourceManager::InitializeAllResources()` lines at process start, before any project code runs (headless player, not NGO).

## AC-9 (A1-4, first measurement) Tick rate and ticks per frame

**PASS** if over at least 60 s of server time `NetworkTickSystem.Tick` fires 20 +/- 1 times per second and frames show only 0, 1 or 2 ticks.

**Result**: PASS with a note (user decision 2026-10-22). Run A after the rebuild:

- Warm-up second excluded: 22 ticks (`[ZoneTickProbe] warm-up second excluded: ticks=22`).
- 73 measured seconds, 1464 ticks (20.05 per second). Per-second counts: 20 in 70 seconds, 21 in 2, 22 in 1 (second 50; second 51 had 21).
- Final histogram: `0=11 1=1442 2=13 3=0 4=0 5=0 6=0 >=7=0`.

Run B: warm-up 22 excluded, 28 seconds all 20-21, histogram `0=3 1=560 2=4 3=0`.

**Note**: read strictly, one second in 73 (second 50, 22 ticks) is outside 20 +/- 1. The 2-tick frames around it show one late frame and NGO catching up, not a drift; the rate over server time is 20.05. The first set of runs, before the warm-up exclusion, showed 23 and 22 ticks in the first second and one 3-tick frame inside the first 10 s (run A histogram `0=8 1=1458 2=11 3=1`): NGO catches up once after `StartServer`. User decision 2026-10-22: the probe excludes the first second as a warm-up, and AC-9 is recorded as passed with the second-50 deviation noted. The final A1-4 stays with the Linux IL2CPP gate (AC-12).

`ZoneTickProbe` itself (warm-up exclusion, per-second count, histogram) has no automated test: it reads `Time.frameCount` and `Time.realtimeSinceStartupAsDouble` directly and is compiled out of release builds, so it is verified by these runs only (code review 2026-10-22).

## AC-10 (ADR-015 Verification 4) Teardown in a player and exit codes

**PASS** if, with `-teardownAfterSeconds 30`, teardown 1-8 runs in order with one log line per step, the listener stops within `RELAY_LISTENER_STOP_TIMEOUT_MS` (2000 ms) and the exit code is 0; and an unreachable database gives exit code 1.

**Result**: PASS.

- Run B (30 s): `-teardownAfterSeconds 30 reached: requesting a Clean teardown`, then `teardown step 1 done in 5 ms`, `2a` 99 ms, `3` 0, `4` 0, `4b` 0, `5` 0, `6` 0, `7` 1 ms, `teardown step 8: quit(0)`; exit 0.
- Run A (75 s): same order, `2a` 109 ms, `quit(0)`; exit 0.
- Run C (unreachable): `S4: database warm-up failed: NpgsqlException`, `startup failed at S4: StepReturnedFalse`, steps 1-7 then `teardown step 8: quit(1)`; exit 1.

No connection string in any log: `grep -c -i -E "Host=|Username=|Password=|Database="` gives 0 for runs A, B and C.

Step 2a is `NpgsqlRelayListener.Stop()` (wake `pg_notify` then join): 90-109 ms in every run, well under 2000 ms. The step lines exist only in Editor and development builds (`ZoneServerComposition.LogTeardownSteps`, set by the host; added 2026-10-22 because the first runs had no per-step evidence).

## AC-11 (A1-5, ADR-015 Verification 6) Leaving Play mode in the Editor

**PASS** if, with the zone running in Play mode on the real listener, leaving Play mode is not blocked, the host's `OnDestroy` runs teardown 1-7 without step 8, the listener stops within 2000 ms and no `NullReferenceException` appears, also when `NetworkManager.OnDestroy` ran first (order logged).

**Procedure**: Editor started from PowerShell with `IRONGRIND_PG_PASSWORD` set for that process only; `ZoneServer.unity` with `_editorArgs` `-zoneTemplate ZoneTemplate_Empty -zoneId 1 -port 7777 -serverConfig <Builds/ZoneServer/zone-config.json>`. Three cycles: Play, wait for the warm-up line, Stop. Evidence from `%LOCALAPPDATA%\Unity\Editor\Editor.log`.

**Result**: PASS. Each cycle logged the A1-3 lines, the warm-up line (31-32 ticks: the Editor catches up more after entering Play mode), then on Stop `host OnDestroy: NetworkManager alive=...`, teardown steps 1 to 7 in order and no step 8:

| Cycle | Order observed | Step 2a (listener stop) |
|-------|----------------|-------------------------|
| 1 | `alive=False`: `NetworkManager.OnDestroy` ran first | 103 ms |
| 2 | `alive=True`: host first | 103 ms |
| 3 | `alive=True`: host first | 90 ms |

Both orders occurred, and neither produced a `NullReferenceException` or any exception, warning or error from project code (the user saw no red Console entries; the Editor log has none). Stop was not blocked: each cycle ended and the next Play started.

## AC-13 Build side effects reverted

**PASS** if `git status` after the builds shows only the intended files and `Packages/manifest.json` has no `com.unity.dedicated-server`.

**Result**: PASS. After each build the changed files listed under "Build and run procedure" step 2 were restored with `git checkout`. `grep -c dedicated-server Packages/manifest.json` gives 0. `git status` before the commit shows only the story code, tests, scenes, the `ZoneServer` profile and docs; the generated `ProjectSettings/Packages/`, `ProjectSettings/SceneTemplateSettings.json` and a stray `bash.exe.stackdump` stay untracked and are not committed.

## AC-12 NOT RUN, carried by the Linux IL2CPP headless-build gate

| Check | Why not here |
|-------|--------------|
| A1-1 | Needs the Linux IL2CPP headless server build; no IL2CPP on the dev machine |
| A1-2 | Same |
| A1-7 | Same |
| A1-4 final | The Windows Mono numbers above are a first measurement only; the final measurement is on the Linux IL2CPP build |

The EPIC's engine-risk gate note lists them (2026-10-22).
