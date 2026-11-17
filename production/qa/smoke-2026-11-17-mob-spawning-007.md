# Smoke check: Mob Spawning Story 007 (starting-zone spawn table) and Story 006 (zone composition wiring)

> Story: `production/epics/mob-spawning/story-007-starting-zone-spawn-table.md` (AC-6, QA 6)
> Type: Config/Data. This note records the real zone host start with the shipped table.
> Run date: 2026-11-17 00:26 UTC (2026-11-16 21:26 local), Windows 11, Unity 6000.3.10f1
> **Verdict: PASS**: the zone reaches Active with the shipped table, and the T-2 sweep spawns all 48 points.

## Setup

| Item | Value |
|------|-------|
| Build | `ZoneServer` build profile (Windows Dedicated Server, Development Build on), output `Builds/ZoneServer/IronGrind.exe`; `IronGrind.ServerLogic.dll` rebuilt 2026-11-16 21:23 local (includes Stories 006 and 007) |
| Backend | `Builds/start-e2e.ps1` (portable PostgreSQL, Caddy dev, gateway `ZONE_LAUNCHER=none`) |
| Zone | `Builds/start-zone-smoke.ps1` (gitignored): `IronGrind.exe -batchmode -nographics -zoneTemplate StartingZone -zoneId 1 -port 7777 -serverConfig Builds\ZoneServer\zone-config.json -logFile %LOCALAPPDATA%\IronGrind\zone-ms007.log` (no probe flags) |
| Client | none (zone start only) |
| Operator | Manuel Toscano (build, backend and zone start); Claude Code session (log reading) |

## Zone log

```
[ZoneInstancingWiring] ZoneID(1) Active at tick 0
[MobSpawning] zone ZoneID(1): 48 of 48 points Live, 0 Deferred
[ZoneServer] tick log: .../tick-log-zone1.csv
[GatewayLinkClient] LinkUp: registered with the gateway.
[ZoneTickProbe] second 1: ticks=20
...
```

- No `startup failed` line: S6 built the spawner from `ZoneDefinition_StartingZone`'s table on the real baked NavMesh (0 points excluded, no `Spawn point off the NavMesh` or `CRITICAL` line), and S9 ran the T-2 sweep right after `Active`.
- The only `[MobSpawning]` line is the T-2 summary: no `SpawnTableOverCapacity`, template, mob type or bounds error, no `UnexpectedTryAddMobFailure`.
- Ticks: `[ZoneTickProbe]` 20 ticks per second after the warm-up second (one second at 21); ticks-per-frame histogram `0=2 1=195 2=3` over the first 200 frames. The 48 Dormant mobs did not disturb the tick cadence.
- Zero `Exception` lines. The two `Built-in Resource Error: dereference potentially before BuiltinResourceManager::InitializeAllResources()` lines are Unity engine start-up messages, also present in `zone-nav010.log` (2026-11-13); not from this change.

## Teardown (idle close)

No player joined, so the zone closed by the existing idle rule with all 48 mobs alive:

```
[ZoneInstancingWiring] ZoneID(1) Closed(Idle) at tick 2400
[ZoneServer] teardown step 1 done in 7 ms
[ZoneServer] teardown step 2a done in 314 ms
[ZoneServer] teardown step 3 done in 1 ms
[ZoneServer] teardown step 4 done in 5 ms
[ZoneServer] teardown step 4b done in 0 ms
[ZoneServer] teardown step 5 done in 0 ms
[ZoneServer] teardown step 6 done in 0 ms
[ZoneServer] teardown step 7 done in 2 ms
[ZoneServer] teardown step 8: quit(0)
```

- Step 1 (`CancelAllSpawnTimers`, through the Story 006 forwarder) completed, and no `[MobSpawning]` line followed: no spawn or roster call after the close.
- Every step completed, the process exited with `quit(0)`, and no `Exception` line appears in the whole log (2865 lines).
- Ticks over the full run: histogram `0=17 1=2334 2=21`; every second after warm-up logged 20 ticks (once 21).

Build side effects: none. `git status` after the build matches the pre-build baseline (41 entries; `Packages/manifest.json` unchanged); the `ProjectSettings/*` and URP asset modifications predate this run and stay out of the commit.

## Results

| # | Criterion | Result |
|---|-----------|--------|
| 007 AC-6 | Real host start with the shipped table; zone log shows `48 of 48 points Live, 0 Deferred`, no S6 failure | **PASS** |
| 007 AC-1 / AC-3 | Shipped table reached the build through the definition reference; all 48 points on the real mesh | **PASS** (no S6 failure, 48 Live) |
| 006 AC-1 / AC-3 | Spawner built at S6, T-2 after Active, in a real build | **PASS** |
| 006 AC-2 / AC-7 | Close step 1 reaches the spawner; nothing spawns after it; clean exit | **PASS** (idle close, steps 1–8, quit(0)) |
