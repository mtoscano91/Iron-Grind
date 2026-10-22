# Epic: Zone Instancing

> **Layer**: Core
> **GDD**: design/gdd/zone-instancing.md
> **Architecture Module**: Zone Instancing (zone-process side) — the zone `State` owner, player and mob slot pools, EntityID minting, the player entry sequence, `ZoneStateSnapshot` assembly, bounds validation, `ZoneDefinition` / `ZoneSceneRegistry`, the CR-ZI-12 close steps, and the mob, party and relevance interfaces
> **Status**: Ready
> **Stories**: 14 (001–014), written 2026-10-22 — see Stories below
> **Manifest Version**: 2026-10-19

## Overview

Zone Instancing is the server-side runtime of one zone instance: it owns the instance's lifecycle (Loading → Active → Draining → Closed), the authoritative roster of players and mobs in fixed slot pools with stable indices, the per-zone `EntityID` counter, the player entry sequence from `SessionHandshake` to the entity entering the tick loop, the `ZoneStateSnapshot` bulk transfer and its retransmit on `ZoneSnapshotRequest`, the walkable-bounds check, the town respawn point, and the ordered teardown that saves every session before slots are freed. Under ADR-007 and ADR-009 one Unity process runs one zone instance; this epic implements the zone-process side and replaces the Networking Core composition-root stand-ins (Stories 042–045) for the zone `State` owner, the bounds validator, the CR-ZI-12 close steps and the static zone data behind `IZoneScene.AddNavMeshData()`. Zone routing (CR-ZI-6), `ZoneID` allocation and the gateway client live in the gateway process (ADR-007) and are listed here as Blocked until that process has an ADR and an epic.

## Scope (user decision 2026-10-22, `/create-epics`)

**Zone process only.** The routing requirements and the gateway client are recorded in the table below and stay **Blocked** until a gateway ADR and a gateway epic exist (`/architecture-decision` for the gateway process and the zone → gateway registration IPC):

- TR-zi-001 — `ZoneID` allocation (the zone process only reads its `ZoneID` from `-zoneId`, which Story 043 already parses; that part is not blocked)
- TR-zi-005 — CR-ZI-6 fill-first routing, party co-location, coalescing (AC-ZI-1, 2, 3, 16, 19, 20)
- TR-zi-018 — the real `IGatewayRegistration` at S10

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-009: Scene / Zone-Load Management (+ Amendment 1) | Single-mode async zone scene load; `ZoneDefinition` ScriptableObject holds bounds, respawn point and baked `NavMeshData`; `ZoneSceneRegistry` maps `ZoneTemplateID` → scene; startup S0–S10 (S6 load → NavMesh → nav init → bounds; S9 `zone.State = Active`; S10 gateway register last); teardown steps 1–8 with CR-ZI-12 at step 5; the composition root is the only place that constructs server systems | HIGH — `SceneManager.LoadSceneAsync`, `NavMesh.AddNavMeshData` |
| ADR-007: Hosting Backend | One Unity process per zone instance; the gateway (C# .NET 8) holds the routing table and spawns zone processes; zones register with the gateway on startup | HIGH — IL2CPP Linux headless |
| ADR-004: Networking — NGO Substrate | Zone messages (`ZoneFullResponse`, `ZoneStateSnapshotFragment`, `ZoneSessionEnded`, `PlayerJoinedZone`) over unnamed custom messages and the project envelope | HIGH — NGO 2.13.3 |
| ADR-014: Inbound Request Dispatch and Tick Order | `SessionHandshake` and `ZoneSnapshotRequest` are connection-level messages (not gated, never held); `AddConnection` / `RemoveConnection` per connection slot | LOW |
| ADR-006: Persistence Layer | `LoadCharacter` on entry; `SaveSession(ZoneClosed[, hpOverride])` on teardown | MEDIUM |
| ADR-011: Asynchronous Persistence in the Tick Loop | Persistence tasks are tracked, never awaited on the tick; zone state mutated only on the main thread | LOW |
| ADR-010: Event and Messaging Architecture | Direct calls on injected interfaces for Party, Mob Spawning, Relevance Filter, Ghost Session | LOW |
| ADR-012: Server/Client Assembly Boundary | All of this code is in `IronGrind.ServerLogic`; server-only scenes stay out of client profiles | HIGH (build verification items) |
| ADR-002 / ADR-003: NavMesh Service / Agent Lifecycle | `ZoneNavigationService.Initialize` / `Teardown` around the zone lifecycle — owned by the Navigation / Pathfinding system, consumed here | HIGH |

## GDD Requirements

*The TR registry (`docs/architecture/tr-registry.yaml`) has no entry for this system; the IDs below are this epic's placeholders, as in the other epics, to be registered by the next `/architecture-review`.*

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-zi-001 | CR-ZI-1 — `ZoneTemplateID : byte` (`StartingZone = 0`) and `ZoneID : uint` (0 = Invalid), allocated from a per-run monotonic counter that skips 0 and never reuses a value | ⚠️ ADR-007 / ADR-009: the gateway allocates and passes `-zoneId`; allocator **Blocked** (gateway) |
| TR-zi-002 | CR-ZI-2 + States and Transitions — Loading → Active → Draining → Closed, T-1 to T-7 (T-7 idle close after `ZONE_IDLE_CLOSE_SECONDS`), `ZoneSessionState` enum; the only zone state machine (ST-NET-2 drives it, 2026-10-22) | ✅ ADR-009 A1 (S9 Active, teardown) + dated note 2026-10-22 (a quit request starts T-4; a second one is T-6) |
| TR-zi-003 | CR-ZI-3 / CR-ZI-4 — `MAX_PLAYERS_PER_ZONE` = 50 counting all slot-holding states, capacity check atomic with allocation; player (`byte`) and mob (`ushort`) slot pools with free-lists and stable indices; OWL index offset for mobs | design rule (main-thread atomicity, GDD Runtime Model) |
| TR-zi-004 | CR-ZI-5 + EC-ZI-12 — per-zone `EntityID` counter from 1, never 0; counter exhaustion → critical alert + T-6 | design rule |
| TR-zi-005 | CR-ZI-6 — fill-first routing (rules 1–5), party co-location, per-template `loadingInProgress` coalescing bounded by `ZONE_LOAD_TIMEOUT_SECONDS` | ⚠️ ADR-007 (gateway holds the routing table); **Blocked** (gateway) |
| TR-zi-006 | CR-ZI-7 — `ZoneFullResponse { zoneId, reason, retryAfterSeconds }`, no slot, no handshake response, no queue | ✅ ADR-004 |
| TR-zi-007 | CR-ZI-8 steps 3–10 — token validation → `LoadCharacter` → capacity + slot → EntityID → snapshot → `SessionReady` → party notify → `PlayerJoinedZone` → tick loop | ✅ ADR-014, ADR-011, ADR-006 |
| TR-zi-008 | CR-ZI-8 step 6 + F-ZI-1 — `ZoneStateSnapshot` as tagged EntityState entries (player / mob, dead-state fields), `F = max(1, ⌈(P·S_player + M·S_mob) / 504⌉)` fragments | ✅ ADR-004 |
| TR-zi-009 | CR-ZI-9 + EC-ZI-6/7 — `ZoneSnapshotRequest`: fresh snapshot at current tick, coalesced per (connection, zone), at most 1 per `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS`, forged versions ignored | ✅ ADR-014 (connection-level) |
| TR-zi-010 | CR-ZI-10 — walkable half-extent ≤ 250 m on every axis, else T-3 with `ZoneBoundsExceedEncoding` | ✅ ADR-009 (S6 `ZoneBoundsValidator.Validate`) |
| TR-zi-011 | CR-ZI-11 — `GetTownRespawnPoint(ZoneID)`: template-keyed cache, O(1), zero allocation; fallback authored → bounds centroid (warning) → `Vector3.zero` (critical) | ✅ ADR-009 (`ZoneDefinition`) |
| TR-zi-012 | CR-ZI-12 — teardown steps 1–8 (spawn timers, `SaveSession` with HP 0 for Dead/Respawning, ghost notify, `ZoneSessionEnded`, `NotifyZoneExit` after save, slots freed, `ZoneID` retired) | ✅ ADR-009 A1 (teardown step 5), ADR-006, ADR-011 |
| TR-zi-013 | CR-ZI-13 + EC-ZI-10 — `TryAddMob` / `RemoveMob` / `GetMobCount`; `EntityID(0)` at cap; `MobSlotExhausted` once per zone per minute | ✅ ADR-010 |
| TR-zi-014 | CR-ZI-14 + EC-ZI-11 — `NotifyZoneEntry` / `NotifyZoneExit` on every entry and exit cause, exactly one exit call per exit | ✅ ADR-010 |
| TR-zi-015 | CR-ZI-15 — `GetPlayerEntityIDsInZone(ZoneID) : IReadOnlyList<EntityID>`, not mutable through a cast | ✅ ADR-010 |
| TR-zi-016 | CR-ZI-17 + AC-ZI-14 + F-ZI-2 — stale slot reclamation on a duplicate-`CharacterID` handshake; entry stall frees the slot after `MaxZoneEntryStall` | design rule |
| TR-zi-017 | `ZoneDefinition` ScriptableObject (fields only) + `ZoneSceneRegistry`; S6 adds the template's baked `NavMeshData` (replaces the `AddNavMeshData()` stand-in of Story 045) | ✅ ADR-009 |
| TR-zi-018 | S10 `Gateway.RegisterZone(zoneId, port)` — the zone → gateway registration protocol | ❌ No ADR (ADR-007 names a Unix domain socket only); **Blocked** (gateway) |

## Known Gaps at Creation (2026-10-22)

`/create-stories` and `/story-readiness` must take these into account.

1. **Gateway process has no ADR or epic.** ADR-007 places routing, `ZoneID` allocation and zone-process spawning in a C# .NET 8 gateway; the registration IPC is unspecified. TR-zi-001 (allocator), TR-zi-005 and TR-zi-018 are Blocked; the zone process keeps the `IGatewayRegistration` stand-in. Resolve with `/architecture-decision` (gateway process + registration protocol), then a gateway epic.
2. **Callers and callees not implemented**, reached through interfaces until their epics ship: session-token validation (Authentication epic, no stories yet), Party System (`NotifyZoneEntry` / `NotifyZoneExit`), Mob Spawning (`CancelAllSpawnTimers`), Navigation / Pathfinding (`ZoneNavigationService`, no epic — the `IZoneNavigation` stand-in stays), Networking Relevance Filter and Ghost Session (check the Networking Core code for what exists before writing stories).
3. **T-4 drain trigger — resolved 2026-10-22** (lifecycle reconciliation, user decisions): CR-ZI-2 owns the zone lifecycle and `networking-session.md` ST-NET-2 became occupancy rules that drive it; a quit request starts T-4 (drain with `ZONE_DRAIN_GRACE_SECONDS` = 60, the only grace constant), a second one or a quit while Loading is T-6; empty Active zones close by T-7 after `ZONE_IDLE_CLOSE_SECONDS` = 120 (ADR-009 dated note 2026-10-22). The operator drain command and routing disable stay with the gateway (gap 1). **Code impact for `/create-stories`** (one story, or part of the TR-zi-002 lifecycle story): rewrite `src/ServerLogic/Networking/ZoneSessionStateMachine/ZoneSessionStateMachine.cs` (Networking Core Story 014) to the CR-ZI-2 states — drop population Draining, `Draining → Active`, `ReinitializeFromClosed` and the immediate `Active → Closed` on explicit disconnect; add the idle timer and the drain — and its tests (`tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs`, `GhostSession_Cleanup_Crash_Dismissal_tests.cs`, `ZoneTestConfigurator`); rename `ZoneState.Empty` to `Loading` in `src/Foundation/Networking/ZoneState.cs` (same value 0; not on the wire or persisted); change `ZoneServerComposition.OnWantsToQuit` from `RequestTeardown(Clean)` to starting T-4. The state machine is not yet wired into the composition (only tests and the test harness call it), so no running behaviour changes until that story.
4. **Stand-ins this epic replaces** (Networking Core Stories 042–045): `IZoneStateOwner` (the zone `State` owner), `IZoneBoundsValidator`, `IZoneCloseSteps` (CR-ZI-12, including the step 5 ordering note of ADR-009 A1), and the static zone data behind `IZoneScene.AddNavMeshData()` (`ZoneDefinition`). `IConnectionCharacterDirectory` / `IConnectionActivitySink` are filled by the entry sequence (TR-zi-007) — until then every request is dropped as `NoCharacter`.
5. **No zone content exists.** `ZoneTemplate_Empty.unity` (Story 045) has no `ZoneDefinition`, bounds or baked NavMesh; stories that need real content use crafted test assets (AC-ZI-5).
6. **GDD header** said "Needs Revision" while the review log (2026-05-30) and `systems-index.md` said Approved; header corrected 2026-10-22 (no rule change).

## Stories

*Written 2026-10-22 (`/create-stories`, lean — QL-STORY-READY skipped). User decisions: `SessionHandshake` / `SessionReady` codecs stay out (schemas pending OQ-NC-SER-2) — Stories 009 / 010 use a decoded entry request and a `SessionReady` seam; the zone-entry token validator stand-in rejects every token and logs once. Not covered here: the gateway-blocked TR-zi-001 / 005 / 018 (AC-ZI-1, 2, 3, 16, 19, 20) and AC-ZI-6 (client dual-gate, client app).*

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Zone lifecycle state machine](story-001-zone-lifecycle-state-machine.md) | Logic | Complete (2026-10-22) | ADR-009 A1 |
| 002 | [Slot pools, EntityID minting, player roster view](story-002-slot-pools-and-entity-ids.md) | Logic | Complete (2026-10-22) | design rule, ADR-010 |
| 003 | [Mob slot interface](story-003-mob-slot-interface.md) | Logic | Ready | ADR-010 |
| 004 | [ZoneDefinition, ZoneSceneRegistry, bounds validation](story-004-zone-definition-and-bounds-validation.md) | Integration | Ready | ADR-009 |
| 005 | [GetTownRespawnPoint](story-005-town-respawn-point.md) | Logic | Ready | ADR-009 |
| 006 | [Zone wire messages and codecs](story-006-zone-wire-messages.md) | Logic | Ready | ADR-004 |
| 007 | [Snapshot assembly and fragmentation](story-007-snapshot-assembly-and-fragmentation.md) | Logic | Ready | ADR-004 |
| 008 | [ZoneSnapshotRequest retransmit](story-008-snapshot-request-retransmit.md) | Logic | Ready | ADR-014 |
| 009 | [Entry part 1 — admission](story-009-entry-admission.md) | Integration | Ready | ADR-014, ADR-011, ADR-006 |
| 010 | [Entry part 2 — snapshot, SessionReady, party, PlayerJoinedZone](story-010-entry-completion.md) | Integration | Ready | ADR-014, ADR-010 |
| 011 | [Stale slot reclamation and entry stall](story-011-stale-slot-reclamation.md) | Logic | Ready (open decision 1 at readiness) | design rule |
| 012 | [Teardown close steps](story-012-teardown-close-steps.md) | Integration | Ready | ADR-009 A1, ADR-006, ADR-011 |
| 013 | [Player exit and slot release](story-013-player-exit-and-slot-release.md) | Integration | Ready | ADR-010, ADR-011 |
| 014 | [Composition wiring, quit-request drain](story-014-composition-wiring-and-drain.md) | Integration | Ready | ADR-009 A1 |

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/zone-instancing.md` that are in scope are verified (the routing ACs AC-ZI-1, 2, 3, 16, 19, 20 move with the gateway work)
- All Logic and Integration stories have passing test files in `tests/`
- All Visual/Feel and UI stories have evidence docs with sign-off in `production/qa/evidence/`

## Next Step

Stories 001 and 002 are Complete (2026-10-22). Next: `/story-readiness production/epics/zone-instancing/story-003-mob-slot-interface.md` (builds on Story 002), then `/dev-story`. Stories 004 and 006 have no dependencies and can go in any order.
