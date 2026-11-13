# Smoke check: Navigation Story 010 (composition wiring + server-build frame-rate pin)

> Story: `production/epics/navigation-pathfinding/story-010-composition-wiring.md`
> Type: Integration. This note records the manual ACs (frame-rate pin, TR-nav-008; Story 008 smoke regression).
> Run date: 2026-11-13 (run 1 17:29 UTC, run 2 17:31 UTC), Windows 11, Unity 6000.3.10f1
> **Verdict: PASS** — agent simulation follows `Application.targetFrameRate = TICK_RATE_HZ`; no ADR-002 5a fallback needed.

## Setup

| Item | Value |
|------|-------|
| Build | `ZoneServer` build profile (Windows Dedicated Server, Development Build on), output `Builds/ZoneServer/IronGrind.exe`; `IronGrind.ServerLogic.dll` rebuilt 2026-11-13 14:23 local |
| Backend | `Builds/start-e2e.ps1` (portable PostgreSQL, Caddy dev, gateway `ZONE_LAUNCHER=none`) |
| Zone | `Builds/start-zone-probe.ps1` (gitignored): `IronGrind.exe -batchmode -nographics -zoneTemplate StartingZone -zoneId 1 -port 7777 -serverConfig Builds\ZoneServer\zone-config.json -navFrameRateProbe -logFile %LOCALAPPDATA%\IronGrind\zone-nav010.log` |
| Client | `Builds/ZoneEntryHarness/IronGrind.exe -screen-fullscreen 0`, account `zetest`, character `Juanperez`; log `%LOCALAPPDATA%\IronGrind\client-nav010.log` |
| Operator | Manuel Toscano (build, backend + zone start); Claude Code session (client launch, log reading) |

## Frame-rate pin (`-navFrameRateProbe`)

Probe mob `EntityID(4294967294)` spawned at the origin through the composed `MobEventBus`, destination (40, 0, 0), stand-in speed 3.5 m/s. 0.5 s warm-up excluded; `frames` = host Update calls in the second, `movingFrames` = frames whose agent `nextPosition` moved (> 1e-5 m); step stats over moving frames.

Run 1:

```
[NavProbe] t=1.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1761 totalAdvance=4.30
[NavProbe] t=2.0s frames=20 movingFrames=20 meanStep=0.1754 minStep=0.1750 maxStep=0.1765 totalAdvance=7.81
[NavProbe] t=3.0s frames=20 movingFrames=20 meanStep=0.1753 minStep=0.1750 maxStep=0.1768 totalAdvance=11.31
[NavProbe] t=4.0s frames=20 movingFrames=20 meanStep=0.1754 minStep=0.1750 maxStep=0.1783 totalAdvance=14.82
[NavProbe] t=5.0s frames=20 movingFrames=20 meanStep=0.1753 minStep=0.1750 maxStep=0.1766 totalAdvance=18.33
[NavProbe] t=6.0s frames=20 movingFrames=20 meanStep=0.1751 minStep=0.1750 maxStep=0.1760 totalAdvance=21.83
[NavProbe] t=7.0s frames=20 movingFrames=20 meanStep=0.1754 minStep=0.1750 maxStep=0.1821 totalAdvance=25.34
[NavProbe] t=8.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1758 totalAdvance=28.84
[NavProbe] t=9.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1761 totalAdvance=32.35
[NavProbe] t=10.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1765 totalAdvance=35.85
[NavProbe] done
```

Run 2:

```
[NavProbe] t=1.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1762 totalAdvance=4.30
[NavProbe] t=2.0s frames=20 movingFrames=20 meanStep=0.1753 minStep=0.1750 maxStep=0.1769 totalAdvance=7.80
[NavProbe] t=3.0s frames=20 movingFrames=20 meanStep=0.1753 minStep=0.1750 maxStep=0.1762 totalAdvance=11.31
[NavProbe] t=4.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1761 totalAdvance=14.81
[NavProbe] t=5.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1767 totalAdvance=18.32
[NavProbe] t=6.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1761 totalAdvance=21.82
[NavProbe] t=7.0s frames=20 movingFrames=20 meanStep=0.1752 minStep=0.1750 maxStep=0.1763 totalAdvance=25.32
[NavProbe] t=8.0s frames=20 movingFrames=20 meanStep=0.1750 minStep=0.1750 maxStep=0.1758 totalAdvance=28.82
[NavProbe] t=9.0s frames=20 movingFrames=20 meanStep=0.1751 minStep=0.1750 maxStep=0.1768 totalAdvance=32.33
[NavProbe] t=10.0s frames=20 movingFrames=20 meanStep=0.1750 minStep=0.1750 maxStep=0.1750 totalAdvance=35.83
[NavProbe] done
```

- 20/20 intervals: frames = movingFrames = 20 (pass band 18–22), meanStep 0.1750–0.1754 (expected 3.5 / 20 = 0.175). The agent simulation steps once per frame and the frame rate is pinned at 20 Hz.
- Advance ≈ 3.5 m per second (totalAdvance at t=1 includes the 0.5 s warm-up).
- Network tick probe in the same log: `[ZoneTickProbe] second N: ticks=20`, histogram `1=200` (one tick per frame).
- **Result: `Application.targetFrameRate = TICK_RATE_HZ` drives NavMeshAgent simulation in a Windows Dedicated Server build; the `Time.fixedDeltaTime` fallback is not applied.**

## Teardown in the server build (run 1, idle close)

No player joined run 1, so the zone closed by the existing idle rule (`[ZoneInstancingWiring] ZoneID(1) Closed(Idle) at tick 2400`). Teardown: steps 1, 2a, **3 (real `ZoneNavigationService.Teardown` + `DestroyPoolRoots`) done in 2 ms**, 4, 4b, 5, 6, 7, then `step 8: quit(0)`. No errors or exceptions.

## Story 008 smoke regression (run 2)

Zone log:

```
[ZoneInstancingWiring] ZoneID(1) Active at tick 0
[GatewayLinkClient] LinkUp: registered with the gateway.
[NavProbe] started: mob EntityID(4294967294) at the origin, destination (40, 0, 0).
[NavProbe] done
[AdmittedEntries] 2026-11-13T17:32:13.293Z Admit AccountID(8) CharacterID(13) at tick 312
[ZoneEntryAdmission] 2026-11-13T17:32:14.085Z LoadCharacter ok client 1 CharacterID(13) -> EntityID(1)
[ZoneEntryReclamation] ZoneEntryAck from client 1 for ZoneID(1) (snapshotVersion 327); slot EntityID(1) acked.
```

Client: `login ok zone=1 port=7777` → `handshake sent` → `snapshot player name=Juanperez` → `ack sent` → `entered` (17:32:13.389–14.198Z).

Zero lines matching `[StandIn] no zone NavMesh data`, `[Movement] NavMesh unavailable` or `Exception` in the zone log. The Story 008 line `[ZoneServer] navigation not implemented` (StandInZoneNavigation) is gone: the real service initialised at S6 with the baked StartingZone mesh.

## Results

| # | Criterion | Result |
|---|-----------|--------|
| AC8 | Frame-rate pin in a server build (TR-nav-008) | **PASS** — 20 Hz cadence, no fallback |
| AC9 | Probe flag (dev build) | **PASS** — started once Active, 10 per-second lines, OnMobDied, `done` |
| AC4/AC5 | Real S6 init + teardown step 3 in a build | **PASS** — S6 passed with the real service; step 3 done in 2 ms, quit(0) |
| AC7 | Story 008 smoke (zone start + login) | **PASS** |

Build side effects: none new (`Packages/manifest.json` unchanged); the pre-existing `ProjectSettings/*` and URP asset modifications stay out of the commit.
