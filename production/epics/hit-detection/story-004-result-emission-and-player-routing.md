# Story 004: Result Emission and Player Routing

> **Epic**: Hit Detection
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/hit-detection.md` — CR-HD-10, CR-HD-11, AC-HD-11a, AC-HD-11b
**Requirement**: `TR-hd-006` (placeholder)

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture; ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: callers use direct calls on injected interfaces; the authoritative tick comes from the tick source.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: dependency injection over singletons
- Forbidden: allocation per emitted result
- Guardrail: `CheckHit` stays pure; emission is a separate call

---

## Acceptance Criteria

*From GDD `design/gdd/hit-detection.md`, scoped to this story:*

- [ ] `IHitResultEmitter.Emit(EntityID attacker, EntityID target, HitResult result)` lets a caller report one `CheckHit` result.
- [ ] Attacker holds a player slot: one `HitCheckResult` is queued in the R-U batch for the attacker's client, for every result value; `Result`, both entity ids and `ServerTick` match the inputs and the current tick (AC-HD-11a). This also covers a player attacking a player: only the attacker is notified.
- [ ] Attacker holds no player slot and target does: one `HitCheckResult` is queued for the target's client, never for the monster (AC-HD-11b).
- [ ] Neither holds a player slot: nothing is queued, nothing throws. A ghost player's client (gone from the outbox) gets nothing queued in `RUBatchOutboundStep`, nothing throws.
- [ ] `HitResultEmitter` is built in the zone composition and exposed as an internal `HitResultEmitter` property, ready for the Auto-Attack and Enemy AI epics to inject.

---

## Implementation Notes

- `CheckHit` does not emit (it stays pure, AC-HD-17). Callers call `CheckHit`, then the emitter. Auto-Attack and Enemy AI own making that call on every attack.
- Files: new `IHitResultEmitter.cs` and `HitResultEmitter.cs` in `src/ServerLogic/HitDetection/`; `ZoneServerComposition.cs`; expected list in `tests/EditMode/Networking/ZoneServer_CompositionWiring_tests.cs:214`.
- `HitResultEmitter` takes `ZoneSlotPool`, `IRUBatchCollector` and `IServerTickSource`; it reads `ServerTickNumber` at call time and writes the body to a `stackalloc` buffer of `HitCheckResult.BodySize` with `BatchSubMessageCodec.WriteHitCheckResultBody`. It enqueues with `RUBatchCategory.HitCheckResult` and `HitCheckResult.MessageTypeId`.
- Player versus mob: `ZoneSlotPool` uses one monotonic EntityID counter for players and mobs, so no id range separates them. `pool.TryGetPlayer(id, out ZonePlayerSlot slot)` is true only for players.
- Client lookup: `slot.ClientId`. `ZoneConnectionCharacterDirectory` is not used.
- Routing: attacker holds a player slot → attacker's client; otherwise target holds a player slot → target's client; otherwise nothing. The mob pool is not consulted, so a mob despawned mid-swing still reports to its target. A player who left the zone before emission gets nothing when the target is a mob.
- Ghost players: a ghost keeps its slot and stale `ClientId`. The emitter does not check `ZoneSessionState`; it calls `Enqueue` and `RUBatchOutboundStep` drops the message silently.
- Composition: build in `BuildOutboundAndPipeline` right after `ruBatch`, with an `Observe(nameof(HitResultEmitter))` call.

---

## Out of Scope

- Auto-Attack Combat and Enemy AI calling the emitter (AC-HD-12, AC-HD-13, AC-HD-15)
- `AutoFaceEvent` and `FacingAngle` updates (AC-HD-18, AC-HD-19)
- Client miss and hit feedback

---

## QA Test Cases

- **AC-HD-11a**: player attacker, for each of `Hit`, `MissOutOfRange`, `MissFacing` → one message to that client, all fields match, `ServerTick` equals the tick source.
- **AC-HD-11b**: monster attacker → one message to the defending player's client, none to any other client.
- **Other clients**: a third player in the zone receives nothing.
- **No player slot**: neither entity holds a player slot → `Enqueue` is not called, no throw (fake collector).
- **Ghost player**: the recipient holds a slot but its client is gone from the outbox → no packet on flush, no throw. Use the real `RUBatchOutboundStep` and outbox; a fake collector would record a message.
- **Player attacks player**: only the attacker's client receives a message.
- **Despawned mob**: attacker holds no slot in either pool, target is a player → one message to the target's client.
- **Composition**: the zone composition builds the emitter after `RUBatchOutboundStep` (wiring test list).
- **Two attackers, one tick**: two players hit one monster → each gets their own message (EC-HD-6).
- **End to end**: emit, flush the Story 003 step, decode the packet → `HitCheckResult` fields match.
- **No allocation**: emit inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/HitDetection/HitDetection_ResultEmission_tests.cs` — must exist and pass

**Status**: [x] Created and passing

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003
- Unlocks: Auto-Attack Combat and Enemy AI epics (not yet created)

---

## Completion Notes
**Completed**: 2026-10-30
**Criteria**: 5/5 passing
**Deviations**: Advisory only. `Emit` drops a result with an `EntityID.Invalid` id and logs at most one error per tick, because `WireIdCodec.SerializeEntityId` throws on it; the story did not specify this. TR-hd-006 is not in `tr-registry.yaml`, so the GDD text was checked directly. Extra file touched: `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs` (composition test for AC 5; it has the rig).
**Test Evidence**: Integration: `tests/EditMode/HitDetection/HitDetection_ResultEmission_tests.cs` (21 cases) and 1 composition test; EditMode 5550 / 5421 passed / 0 failed / 129 skipped
**Code Review**: Complete (CHANGES REQUIRED, all applied)
