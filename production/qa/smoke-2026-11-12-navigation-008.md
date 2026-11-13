# Smoke check: Navigation Story 008 (starting-zone floor and NavMesh bake)

> Story: `production/epics/navigation-pathfinding/story-008-starting-zone-navmesh-bake.md`
> Type: Config/Data. This note is the story's required evidence.
> Run date: 2026-11-12 (bake 18:30 UTC; Play-mode run 21:17 UTC), Windows 11, Unity 6000.3.10f1
> **Verdict: PASS** (AC5 closed on bake-time evidence, with a carry; user decision 2026-11-12)

## Setup

| Item | Value |
|------|-------|
| Bake | `Assets/Editor/Navigation/StartingZoneNavMeshBake.cs`, batch mode (`-executeMethod IronGrind.EditorTools.Navigation.StartingZoneNavMeshBake.BakeBatch`), exit 0 |
| Backend | `Builds/start-e2e.ps1` (portable PostgreSQL, Caddy dev, gateway `ZONE_LAUNCHER=none`), as in Authentication Story 017 |
| Zone | Editor Play mode, `Assets/Scenes/Server/ZoneServer.unity`, `Builds/start-editor.ps1` arguments (`-zoneTemplate StartingZone -zoneId 1 -port 7777`) |
| Client | `Builds/ZoneEntryHarness/IronGrind.exe`, account `zetest`, character `Juanperez`; log `%LOCALAPPDATA%\IronGrind\client-nav008b.log` |
| Operator | Manuel Toscano (Editor, screenshots); Claude Code session (bake, client launch, log reading) |

## Bake report (from the bake log)

```
[NavMeshBake] OK: Assets/NavMesh/Zone0_StartingZone.asset (9433f31d87399bb489f441ae4129a966)
[NavMeshBake] agent type 0: radius 0.5, height 2, slope 45, climb 0.75; geometry PhysicsColliders; surface enabled False
[NavMeshBake] added once: 64 vertices, 32 triangles, bounds (-49.333, 0.083, -49.333) .. (49.333, 0.083, 49.333)
[NavMeshBake] SamplePosition (0.0, 0.0, 0.0) r0.5: hit (0.000, 0.083, 0.000)
[NavMeshBake] SamplePosition (25.0, 0.0, -25.0) r0.5: hit (25.000, 0.083, -25.000)
[NavMeshBake] SamplePosition (49.4, 0.0, 0.0) r0.5: hit (49.333, 0.083, 0.000)
[NavMeshBake] SamplePosition (60.0, 0.0, 0.0) r0.5: miss
[NavMeshBake] SamplePosition (0.0, 0.0, -60.0) r0.5: miss
```

- Floor: `PlaceholderGround` (Unity Plane, scale 10 × 1 × 10, origin) = x and z from −50 to 50 at y = 0.
- Mesh edge at ±49.333: 0.667 in from the floor edge (agent radius 0.5 plus voxel rounding).
- Mesh surface at y = 0.083 (voxel height above the floor).

## Results

| # | Criterion | Check | Result |
|---|-----------|-------|--------|
| AC1 | Floor + `NavMeshSurface` in `ZoneTemplate_Empty` | Scene YAML: `PlaceholderGround`, `NavMeshSurface_Zone0` at origin; screenshot | **PASS** |
| AC2 | Asset saved and assigned | `Assets/NavMesh/Zone0_StartingZone.asset`; `ZoneDefinition_StartingZone._bakedNavMesh` → guid 9433f31d… | **PASS** |
| AC3 | Surface disabled; one copy loaded | Saved scene `m_Enabled: 0`; in Play mode the Inspector shows the NavMeshSurface checkbox off and data `Zone0_StartingZone` (operator) → its `OnEnable` never runs, so the only copy is the loader's | **PASS** (see note 1) |
| AC4 | Zone start + login regression | See log extract below | **PASS** |
| AC5 | Real `SamplePosition` on the mesh | Bake-time checks above (same call and 0.5 radius as `UnityNavMeshSampler`): inside hits, outside misses, edge at ±49.333 | **PASS on bake-time evidence; in-game movement carried** (note 2) |

### AC4 log extract (Editor log, Play session 2, from the zone scene load)

```
Loaded scene 'Assets/Scenes/Zones/ZoneTemplate_Empty.unity'
[ZoneServer] navigation not implemented                       (StandInZoneNavigation.Initialize — S6 passed with the instance)
[ZoneInstancingWiring] ZoneID(1) Active at tick 0             (S10)
[GatewayLinkClient] LinkUp: registered with the gateway.
[AdmittedEntries] 2026-11-12T21:17:09.587Z Admit AccountID(8) CharacterID(13) at tick 399
[ZoneEntryAdmission] 2026-11-12T21:17:10.687Z LoadCharacter ok client 1 CharacterID(13) -> EntityID(1)
[ZoneEntryReclamation] ZoneEntryAck from client 1 for ZoneID(1) (snapshotVersion 420); slot EntityID(1) acked.
```

Client: `login ok zone=1 port=7777` → `handshake sent` → `snapshot player name=Juanperez` → `ack sent` → `entered` (21:17:09.766–21:17:10.765Z).

From the scene load to about 15 s after `entered`: **0** lines matching `[StandIn] no zone NavMesh data`, `[Movement] NavMesh unavailable` or `Exception`. Play session 1 (21:02) showed the same startup lines with neither stand-in line.

### Notes

1. **NavMesh overlay inconclusive.** With the AI Navigation overlay ("Show NavMesh" on, "Show Only Selected" off) the Scene view showed a uniform blue floor that did not change when the overlay was toggled and showed no edge strip at a zoomed corner (`production/qa/evidence/nav-008-navmesh.png`, `nav-008-navmesh-edge.png`). The blue is the floor's `Lit` shading from above; the overlay did not draw the runtime-added data. AC3 rests on the disabled surface (no self-add) plus the loader's add (no stand-in line); the single-copy count comes from the bake report.
2. **AC5 movement carry (user decision 2026-11-12).** The client cannot send movement yet (`LoggingMovementIntentSink` only; the zone registers `MovementIntentMessage`), so "a player moves on the floor and stops near ±49.333" cannot be run end to end. Carried to Story 009 (real-sampler PlayMode checks) and to the first story that sends movement intents from the client.
3. **Unrelated to this story:** first entry showed two tick durations of 70–74 ms (`LoadCharacter`, Editor JIT warm-up, as in Story 017 AC-3) and tick gaps of 100–105 ms while the Editor window was unfocused. The first login attempt (21:15) failed because Play mode had been exited and no zone was registered; the gateway's repeated `ZoneDefinitionLoadFailed` lines are the warm-spare keeper with `ZONE_LAUNCHER=none`.

## Sign-off

PASS. Signed off by: Manuel Toscano, 2026-11-12 (recorded by Claude Code at the user's instruction)
