# Epic: Movement System

> **Layer**: Core
> **GDD**: design/gdd/movement-system.md
> **Architecture Module**: Movement — position integration, intent messages, movement state (`architecture.md` module table)
> **Status**: Ready
> **Stories**: Not yet created — run `/create-stories movement-system`
> **Manifest Version**: 2026-10-26

## Overview

The Movement System governs player locomotion in a zone. On the client, touch input (a floating virtual joystick in the left half of the safe-area screen, camera drag in the right half) becomes a camera-relative direction, and a client-only auto-run flag keeps a forward intent going. The client sends one `MovementIntentMessage` (U-U, 10-byte body) per 20 Hz tick. On the server, the Movement System is the only writer of player positions (CR-NET-1). Each tick it reads the latest intent for each entity, discards it while the entity is Prohibited (facing still applies), integrates `newPos = oldPos + normalize(inputDir) × GetEffectiveStat(MovementSpeed) × 0.050`, constrains the candidate to the zone NavMesh with `NavMesh.SamplePosition` (radius 0.5), and commits the result to `EntityState` for the per-tick broadcast. Auto-Attack Combat can replace one tick's direction with `InjectFacingOverride`. Committed positions feed Auto-Attack range checks, loot pickup, Navigation and Client-Side Prediction.

## Scope (user decision 2026-10-29, `/create-epics`)

**Server and client input.** The client stories (TR-mov-008, TR-mov-009) go in `IronGrind.Client`. Their logic (zone claim, dead zone, F-MOV-3/4/5, auto-run flag, send cadence) is kept apart from the touch and UI code so EditMode tests can cover it. AC-MOV-22 (0.5 ms per frame on a reference device) waits for an iOS build. Client-side prediction is not in this epic: CR-MOV-6 renders the last server position at MVP, and prediction belongs to `client-side-prediction.md`.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-002: NavMesh Service Execution Contract | Canonical tick ordering: position integration is phase 4; process-global NavMesh, one zone per process | HIGH — `SamplePosition` / NavMesh in a `UNITY_SERVER` build still to verify |
| ADR-014: Inbound Request Dispatch and Tick Order | `MovementIntentMessage` is a U-U request, not held, 10-byte body; the intake drops a stale `SequenceNumber` per connection and type; game systems tick inside ADR-002's phases | LOW |
| ADR-004: Networking — NGO Substrate | `NetworkManager.ServerTime.Tick` is the authoritative tick counter, including `MovementIntentMessage.TickNumber`; U-U channel over the project envelope | HIGH — NGO 2.13.3 |
| ADR-009: Scene / Zone-Load Management (+ Amendment 1) | `ZoneDefinition` holds walkable bounds and the baked `NavMeshData`, loaded at startup S6; spawn position via `GetTownRespawnPoint` | MEDIUM |
| ADR-010: Event and Messaging Architecture | `IMovementSystem.InjectFacingOverride` is a direct call on an injected interface, no event | LOW |
| ADR-012: Server/Client Assembly Boundary | Server movement code in `IronGrind.ServerLogic`; client input in `IronGrind.Client`; the shared codec in `IronGrind.Foundation` | HIGH (build verification items) |
| ADR-005: HUD UI Framework — UI Toolkit | The joystick and the auto-run toggle are UI Toolkit elements | MEDIUM |

## GDD Requirements

*The TR registry (`docs/architecture/tr-registry.yaml`) has no entries for this system; the IDs below are this epic's placeholders, as in the other epics, to be registered by the next `/architecture-review`.*

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-mov-001 | Server position integration F-MOV-1/F-MOV-2: fixed dt = 1 / TICK_RATE_HZ (never wall-clock), `GetEffectiveStat(MovementSpeed)` read every tick with no extra clamp, zero-input guard (never normalize a zero vector); EC-MOV-19 last valid speed on a bad read (CR-MOV-4) | ADR-002 ✅ (phase 4), ADR-014 ✅ |
| TR-mov-002 | Per-tick authority pipeline: latest intent → Prohibited check → candidate → `NavMesh.SamplePosition(candidate, 0.5, AllAreas)` (hit → hit.position, none → hold) → commit `EntityState.posX/Y/Z` + facing → per-tick broadcast; no runtime bake (CR-MOV-6, CR-MOV-7, AC-MOV-21) | ADR-002 ✅, ADR-009 ✅ |
| TR-mov-003 | `MovementIntentMessage` codec (dirX, dirZ, facingAngle ×10, tickNumber; 10-byte body, U-U) and server intake: SenderEntityID mismatch → discard + one security log (EC-MOV-16); F-MOV-7 stale discard with the startup guard `serverCurrentTick ≤ STALE_TICK_TOLERANCE` (EC-MOV-22); last message per entity per tick wins (EC-MOV-17); a missing message = zero input (CR-MOV-10) | ADR-004 ✅, ADR-014 ✅ |
| TR-mov-004 | Prohibited state: movement direction discarded, position held, facing angle still applied; Prohibited exit → Idle; does not re-arm auto-face (CR-MOV-8, States table) | ❌ No ADR (design-only, LOW risk) |
| TR-mov-005 | `IMovementSystem.InjectFacingOverride(entityId, direction)` replaces `inputDir` for exactly one tick, then the client intent resumes; does not cancel auto-run (CR-MOV-9, EC-MOV-8/9) | ADR-010 ✅ |
| TR-mov-006 | NavMesh failure handling: `NAVMESH_STALL_RECOVERY_TICKS` consecutive misses → recovery teleport with radius 5.0, still no hit → flag to Zone Instancing (EC-MOV-11); NavMesh unavailable at zone load → reject all intents (EC-MOV-12); ±327.67 guard before encoding (F-MOV-6, EC-MOV-20) | ADR-009 ✅, ADR-002 ✅ |
| TR-mov-007 | Zone exit / entry: flush buffered intents and any pending auto-face override on exit; Prohibited treated as cleared on entry unless re-asserted; facing initialised non-zero before the first tick (EC-MOV-13, 14, 18) | ADR-009 ✅ |
| TR-mov-008 | Client input: zone claim by touch-start position on the safe-area width, floating joystick at the touch point, F-MOV-5 dead zone, F-MOV-4 camera-relative direction (fallback `Vector3.forward`), camera yaw drag at TK-MOV-5, facing follows the camera only while the joystick is at rest; multi-touch rules EC-MOV-1–6 (CR-MOV-1–3) | ADR-005 ✅ (UI elements); input logic design-only |
| TR-mov-009 | Client auto-run flag (never persisted; cancel on re-press, F-MOV-3 rear pull `dot < 0`, Prohibited entry) and send cadence of one intent per tick while input is active; a facing-only intent (dir 0) while Prohibited (CR-MOV-5, CR-MOV-10, AC-MOV-14) | ADR-004 ✅ (send path); AC-MOV-22 device budget — waits for an iOS build |
| TR-mov-010 | Tuning knobs TK-MOV-1–6 exposed through a ScriptableObject, changeable without code changes | ❌ No ADR (design-only, LOW risk) |

> **Open decision for `/create-stories` (user decision 2026-10-29: decide then): base MovementSpeed conflict.** `movement-system.md` TK-MOV-3 / F-MOV-2 and registry `BASE_MOVE_SPEED` say **6.0** u/s; `character-stats.md` (StatSchema table and examples), `character-persistence.md` (starting values), `CharacterStubDefaults.MOVEMENT_SPEED` and the ADR-006 `movement_speed` column default say **5.0**. AC-MOV-8 (0.3 u/tick) depends on the answer. Resolve it before the TR-mov-001 story is written.
>
> **Notes for `/create-stories`:**
> - Nothing sets `Prohibited` yet (Status Effects has no stun or root); TR-mov-004 needs a seam that a later epic (Auto-Attack Combat, Skill System) fills in.
> - ADR-014's intake already drops a stale `SequenceNumber` per connection; F-MOV-7's `TickNumber` tolerance is a second check in the Movement handler. Say which drop each test exercises.
> - Movement's place inside phase 4, relative to mob position integration (Navigation), is not fixed yet; ADR-014 says the composition root lists the order.
> - `IZoneNavigation` (`src/ServerLogic/ZoneServer/`) has `Initialize` / `Teardown` only, with a stand-in until the Navigation epic; `SamplePosition` needs its own seam so EditMode tests run without a baked NavMesh.
> - GDD housekeeping (not done here): the header still says "Designed — Pending Review" (systems-index says Approved, Pass 2 lean, 2026-05-26); the Dependencies table and OQ-MOV-2/3 still call Zone Instancing and Status Effects "Not Started" (both epics are now Complete; ADR-009 covers NavMesh provision and bounds).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/movement-system.md` are verified, except AC-MOV-22, which is explicitly deferred to on-device profiling rather than silently dropped
- Logic and Integration stories have passing test files in `tests/EditMode/MovementSystem/` (and `tests/EditMode/Integration/` for the pipeline wiring)
- Client input stories have EditMode tests for the extracted logic and a manual walkthrough doc in `production/qa/evidence/` for the touch and UI behaviour

## Next Step

Run `/create-stories movement-system` to break this epic into implementable stories. Resolve the base MovementSpeed conflict first.
