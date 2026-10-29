# Story 009: Composition Wiring and Per-Tick Position Broadcast

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-6 step 6; `design/gdd/networking-wire-protocol.md` — Path 2b-ii Position U-U packet (`TICK_BATCH_UU = 0x0102`, CR-NET-7.7: sorted by EntityID, truncated from the tail on overflow, `BatchOverflow` anomaly)
**Requirement**: `TR-mov-002` (broadcast step)

**ADR Governing Implementation**: ADR-014 Decision 6 (tick order: completion → relay → requests → simulation → outbound); ADR-002 (integration last in simulation); ADR-004 (U-U over NGO custom messages); ADR-012
**ADR Decision Summary**: the composition root lists the simulation entries in order; outbound flushes once per tick after simulation.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (NGO unreliable send path for a raw batch packet is new)
**Engine Notes**: the existing `IClientMessageSendSeam.Send(clientId, message, channel)` sends envelope + body; a batch packet (12-byte batch header + sub-messages) has no send path yet.

**Control Manifest Rules (this layer)**:
- Required: `ZoneServerComposition` reaches NGO through seams so EditMode tests run without NGO
- Required: positions to all zone clients (`EntityPositionUpdate` routing row: `ServerToAllZoneClients`, U-U, `PositionPacket`)
- Forbidden: LINQ / per-tick allocation on the serialization path (CR-NET-7.8)

---

## Acceptance Criteria

- [ ] `ZoneServerComposition` builds one `MovementService` over the zone `CharacterStats` (speed source), the zone NavMesh sampler, the slot pool and the tuning values, registers `MovementInboundRegistration` before `dispatcher.Seal()`, and appends Movement to the simulation list **after Status Effects** (phase-3 list: `[ZoneInstancingWiring, StatusEffectsService, MovementService]`; integration last per ADR-002). The construction-order test (`ZoneServer_CompositionWiring_tests.cs`, `CONSTRUCTION_ORDER`) is updated.
- [ ] Movement is the `IMovementSystem` the composition hands to later systems (internal getter, as `StatusEffects`).
- [ ] **Per-tick broadcast**: in the outbound step each tick, every connected zone client receives one Position packet (`PositionPacketWriter`, `TICK_BATCH_UU`) with an `EntityPositionUpdate` for every registered player (wire units ×100), sorted by EntityID; more than `MaxEntriesPerPacket` (35) entries → the writer's tail truncation and one `BatchOverflow` warning (existing behaviour, asserted here).
- [ ] **Send path — decided at readiness**: how a raw batch packet reaches NGO (extend the send seam with a batch-packet method, or a separate `IPositionPacketSender`), using `ZoneBufferPool` / `ClientBufferSet.PositionBuffer` per client so nothing is allocated per tick.
- [ ] The `IZoneEntityStateSource` used by the snapshot builder reads Movement's position and facing (Story 008's source) in the composed zone.
- [ ] **Allocation**: a tick with 50 moving players and 50 clients allocates nothing after warm-up (`Is.Not.AllocatingGCMemory()`, as Status Effects 009 — not `GC.GetAllocatedBytesForCurrentThread`, which reads 0 under Editor Mono; TD-087).
- [ ] An end-to-end EditMode test through the composition: a `MovementIntentMessage` delivered to the inbound transport seam on tick N → the position packet of tick N carries the moved position.
- [ ] Teardown: Movement holds no resources beyond arrays; if it implements `IDisposable`, it joins the Dispose pass.

---

## Implementation Notes

- Mob positions are not broadcast here (no mob movement yet); when Navigation lands, mobs join the same packet.
- *(From Story 005 code review, 2026-10-29)* `UnityNavMeshSampler` calls `NavMesh.SamplePosition`, which is main thread only (ADR-002 Decision 4). `ServerTickLoop` has no thread or `Update` code of its own — confirm at readiness that whatever drives it (and so `MovementService.Run`) runs on the Unity main thread.
- The per-tick packet is U-U to all zone clients, not relevance-filtered (routing row `ServerToAllZoneClients`; the relevance filter governs HP updates only).
- `SelfPositionUpdate` (R-U, owning client) belongs to Client-Side Prediction (CR-CSP-7) — not this story.

---

## Out of Scope

- `SelfPositionUpdate` and client reconciliation (Client-Side Prediction)
- Client receive/interpolation of positions (client networking epic)
- Mob position integration and broadcast (Navigation)

---

## QA Test Cases

- **Order**: Given the composed zone — Then the simulation list is `[ZoneInstancingWiring, StatusEffectsService, MovementService]` and the inbound registration happens before `Seal`.
- **Broadcast**: three players at known positions, two clients → each client gets one packet per tick with three entries sorted by EntityID, posX/Z = RoundToInt(pos × 100).
- **Overflow**: 36 registered players → 35 entries (the highest EntityID dropped), one `BatchOverflow` warning.
- **End-to-end**: intent (0, 0, 1) from client 1 on tick N → client 2's packet for tick N shows player 1 at +0.25 Z (wire +25).
- **Allocation**: 50 players, 50 clients, one tick, after a warm-up tick → `Is.Not.AllocatingGCMemory()`; positive control: a test-only allocating step makes the same assertion fail.
- **Snapshot**: `ZoneSnapshotBuilder` in the composed zone returns Movement's committed position.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs` (+ `ZoneServer_CompositionWiring_tests.cs` updates) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 004, 005, 006, 007, 008
- Unlocks: the server half of the epic is complete; Auto-Attack Combat range checks and Loot pickup can read positions
