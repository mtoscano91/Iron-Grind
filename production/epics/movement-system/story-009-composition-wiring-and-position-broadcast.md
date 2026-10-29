# Story 009: Composition Wiring and Per-Tick Position Broadcast

> **Epic**: Movement System
> **Status**: Complete — 2026-10-29 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (7 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 8 hours (was 6; readiness added the Story 008 wiring, the respawn provider, the NavMesh flag and the no-allocation writer overload)

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-6 step 6; `design/gdd/networking-wire-protocol.md` — Path 2b-ii Position U-U packet (`TICK_BATCH_UU = 0x0102`: "`EntityPositionUpdate` sub-messages only — all zone entities other than the receiver", sorted by ascending EntityID, overflow drops highest-ID-first, at n=50 ⌊500/14⌋ = 35 delivered; buffers pre-allocated per connection at zone creation, no `ArrayPool`)
**Requirement**: `TR-mov-002` (broadcast step)

**ADR Governing Implementation**: ADR-014 Decision 6 (tick order: completion → relay → requests → simulation → outbound); ADR-002 (integration last in simulation); ADR-004 (U-U over NGO custom messages); ADR-012
**ADR Decision Summary**: the composition root lists the simulation entries in order; outbound flushes once per tick after simulation.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (first U-U batch packet sent through NGO)
**Engine Notes**: the Position packet (`PositionPacketWriter` output) is the 10-byte envelope followed by the body (count + sub-messages), which is exactly what `IClientMessageSendSeam.Send(clientId, message, channel)` takes; `NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.Unreliable)` = `NetworkDelivery.Unreliable`. Largest packet = 12 + 35 × 14 = 502 bytes, under the sender's 10 + 512 limit. No new NGO API.

**Control Manifest Rules (this layer)**:
- Required: `ZoneServerComposition` reaches NGO through seams so EditMode tests run without NGO
- Required: positions to all zone clients (`EntityPositionUpdate` routing row: `ServerToAllZoneClients`, U-U, `PositionPacket`)
- Forbidden: LINQ / per-tick allocation on the serialization path (CR-NET-7.8)

---

## Acceptance Criteria

- [ ] `ZoneServerComposition` builds one `MovementService` (8-arg ctor) at S5 over: a `MovementIntentBuffer` (tick loop, `ZoneSlotPoolEntityIndex(wiring.Pool)`, tuning), the zone `CharacterStats` (`CharacterStatsMovementSpeedSource`), `UnityNavMeshSampler`, the same slot index, **`MovementTuningValues.Defaults`** (no `MovementTuning` asset exists or is loaded; loading one at S2 is a follow-up), `LoggingMovementGeometryErrorSink`, the zone id and the respawn provider (below). It registers `MovementInboundRegistration` before `dispatcher.Seal()` and appends Movement to the simulation list **after Status Effects** (phase-3 list: `[ZoneInstancingWiring, StatusEffectsService, MovementService]`; integration last per ADR-002). The construction-order test (`ZoneServer_CompositionWiring_tests.cs`, `CONSTRUCTION_ORDER`) is updated.
- [ ] Movement is the `IMovementSystem` the composition hands to later systems (internal getter `Movement`, as `StatusEffects`).
- [ ] **Story 008 wiring** (all three, in the composed zone):
  - snapshot: the composition passes a `MovementZoneEntityStateSource` (inner = `LoggingZoneEntityStateSource`) through `ZoneInstancingWiringOptions.EntityState` — the snapshot builder is built inside the wiring ctor, before Movement exists — and calls `AttachMovement(movement)` once Movement is built;
  - entry: `ZoneInstancingWiring` gets an `internal` accessor for its `ZoneEntryCompletion`; the composition calls `SetEntryListener(movement)`;
  - release: `wiring.Pool.SetReleaseListener(statusEffects)` is replaced by `SetReleaseListener(new CompositeZoneSlotReleaseListener(statusEffects, movement))` (Status Effects first).
- [ ] **Respawn provider** (readiness decision): the `ZoneDefinition` lives in the host's `ZoneDefinitionSlot` and is set at S6, after S5 builds Movement. New `ZoneDefinitionSlotRespawnPointProvider(ZoneID ownZone, ZoneDefinitionSlot slot)` : `ITownRespawnPointProvider` in `src/ServerLogic/ZoneInstancing/` builds a `TownRespawnPointCache(ownZone, slot.Current)` on the first `GetTownRespawnPoint` call and reuses it (null slot → `ArgumentNullException`; `slot.Current` still null at a call → one error log, returns `Vector3.zero` (the interface's "none resolved" value; never throws), no cache built, retried next call). `ZoneServerHost` passes it (built over its existing slot) into the composition through a new optional ctor parameter; null (all test ctors) → Movement keeps its existing null-provider branch (error log, nothing registered) unless the test supplies a fake.
- [ ] **NavMesh flag**: in S6 (`StartupFinishScene`), right after `_scene.AddNavMeshData()`, the composition calls `Movement.SetNavMeshAvailable(instance.valid)` (Story 007 hand-off).
- [ ] **Send path** (readiness decision): new `PositionBroadcastStep` : `IZoneOutboundStep` in `src/ServerLogic/MovementSystem/`. Ctor (`MovementService`, `ZoneSlotPool`, `IClientMessageSendSeam`, `ClientMessageOutbox` for `TryGetSequenceCounter`). The composition calls `_network.CreateSendSeam()` once and hands the **same** seam to the outbox and the step. The pipeline's outbound step becomes a composite: outbox `Flush` first, then `PositionBroadcastStep.Flush`; the teardown 4b flush stays the outbox only (no positions at teardown). Each packet: `SequenceNumber` from the receiver's outbox counter (`IssueNext()`, CCR-1 one counter per connection; client unknown to the outbox → skipped, no log), sent with `NetworkChannel.Unreliable`. Buffers: one `ClientBufferSet` per player slot pre-allocated in the ctor; slot *s* is written into `_buffers[s].PositionBuffer` (GDD per-connection buffers; no `ZoneBufferPool` bookkeeping, no `ArrayPool`). A throwing seam → one error per client per flush, other clients still sent (as the outbox).
- [ ] **Receivers and contents** (readiness decision): receivers are the occupied player slots registered in Movement, each with the slot pool's client id. A receiver's packet holds an `EntityPositionUpdate` (Pos ×100 only — the type has no facing field; corrected at `/story-done`) for **every other registered player** — never its own entity (GDD Path 2b-ii) — sorted by ascending EntityID. A receiver with no other registered player gets **no packet** that tick.
- [ ] **No-allocation writer**: new overload `PositionPacketWriter.Write(Span<byte> destination, uint sequenceNumber, uint tickNumber, EntityPositionUpdate[] sortedEntries, int count, EntityID exclude)` — entries already sorted by the caller, writes the first ≤ 35 entries whose id ≠ `exclude`, never allocates, never logs (returns bytes written; reports truncation through an `out int droppedCount`). The existing overload is unchanged. `PositionBroadcastStep` fills one scratch array of all registered players per tick and sorts it with an allocation-free insertion sort (≤ 50 entries), then writes every receiver from it.
- [ ] **Overflow warning**: `BatchOverflow` is logged by `PositionBroadcastStep` **once per zone when overflow starts** (first tick where any receiver drops entries), not per client or per tick; the flag resets on the first tick with no drops, so the next overflow logs again. The message names the tick and the dropped count.
- [ ] The `IZoneEntityStateSource` used by the snapshot builder reads Movement's position and facing in the composed zone.
- [ ] **Allocation**: a tick with 50 moving players and 50 clients (overflowing) allocates nothing after warm-up (`Is.Not.AllocatingGCMemory()`, as Status Effects 009 — not `GC.GetAllocatedBytesForCurrentThread`, which reads 0 under Editor Mono; TD-087). The warm-up tick may log the overflow warning; the measured tick must not.
- [ ] An end-to-end EditMode test through the composition: a `MovementIntentMessage` delivered to the inbound transport seam on tick N → the position packet of tick N carries the moved position.
- [ ] Teardown: Movement and `PositionBroadcastStep` hold no resources beyond arrays; neither implements `IDisposable`.

---

## Implementation Notes

- Mob positions are not broadcast here (no mob movement yet); when Navigation lands, mobs join the same scratch array.
- *(Resolved at readiness 2026-10-29)* Main thread: the tick is driven by `IZoneNetwork.SubscribeTick` (NGO `NetworkTickSystem`), so `ZoneTickPipeline` — and `MovementService.Run` → `NavMesh.SamplePosition` — runs on the Unity main thread (ADR-002 Decision 4).
- Truncation: each receiver gets the 35 lowest *other* EntityIDs, so a receiver whose own id is among the lowest 35 sees one more high id than a receiver outside it — still "highest-ID-first" per receiver, as the GDD states.
- The per-tick packet is U-U to all zone clients, not relevance-filtered (routing row `ServerToAllZoneClients`; the relevance filter governs HP updates only).
- `SelfPositionUpdate` (R-U, owning client) belongs to Client-Side Prediction (CR-CSP-7) — not this story.
- Follow-up (not this story): load a `MovementTuning` asset at S2 and pass `MovementTuningValues.FromAsset`.

---

## Out of Scope

- `SelfPositionUpdate` and client reconciliation (Client-Side Prediction)
- Client receive/interpolation of positions (client networking epic)
- Mob position integration and broadcast (Navigation)
- Loading a `MovementTuning` asset (follow-up)

---

## QA Test Cases

- **Order**: Given the composed zone — Then the simulation list is `[ZoneInstancingWiring, StatusEffectsService, MovementService]` and the inbound registration happens before `Seal`.
- **Wiring**: composed zone → the snapshot source is the Movement decorator with Movement attached; the entry listener is Movement; a slot release runs Status Effects then Movement.
- **Respawn provider**: slot empty at first call → one error, no cache; slot set → cache built once and reused (two calls, one build); null slot → `ArgumentNullException`.
- **NavMesh flag**: S6 with an invalid `NavMeshDataInstance` → `SetNavMeshAvailable(false)` called; valid → true.
- **Broadcast**: three registered players (clients 1–3) at known positions → each client gets one packet per tick with the **two other** entries sorted by EntityID, posX/Z = RoundToInt(pos × 100), sent `Unreliable`, sequence numbers from that client's outbox counter (an outbox message and the position packet on the same tick get consecutive numbers).
- **Solo**: one registered player → no packet sent.
- **Overflow**: 37 registered players → every receiver gets 35 entries (its own excluded, the highest other EntityIDs dropped); one `BatchOverflow` warning that tick; a second overflowing tick → no new warning; drop to 36 players (no drops) then back to 37 → one new warning.
- **Writer overload**: `exclude` skipped, first 35 written, `droppedCount` correct, existing overload's tests unchanged.
- **Seam throws** for one client → one error, other clients still receive.
- **End-to-end**: intent (0, 0, 1) from client 1 on tick N → client 2's packet for tick N shows player 1 at +0.25 Z (wire +25).
- **Allocation**: 50 players, 50 clients, one tick, after a warm-up tick → `Is.Not.AllocatingGCMemory()`; positive control: a test-only allocating step makes the same assertion fail.
- **Snapshot**: `ZoneSnapshotBuilder` in the composed zone returns Movement's committed position.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs` (+ `ZoneServer_CompositionWiring_tests.cs` updates, + writer overload cases in the existing `PositionPacketWriter` tests) — must exist and pass

**Status**: [x] Created — `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs`, 20 methods, + 5 writer-overload tests in `WireProtocol_BatchFraming_tests.cs`, all passing (EditMode 5275 / 5146 passed / 0 failed / 129 skipped, 2026-10-29)

---

## Dependencies

- Depends on: Stories 004, 005, 006, 007, 008 (all Complete)
- Unlocks: the server half of the epic is complete; Auto-Attack Combat range checks and Loot pickup can read positions

---

## Completion Notes
**Completed**: 2026-10-29
**Criteria**: 14/14 (partially verified below composition level: snapshot source in the composed zone — no accessor and the inner stand-in always returns false, Story 008 unit tests cover the decorator; S6 valid NavMesh → `SetNavMeshAvailable(true)` — the scene fake only yields an invalid instance; real-intent end-to-end — EditMode has no NavMesh, covered at buffer + `MovementService` + `PositionBroadcastStep` level with a fake sampler; release order — covered by the Story 008 composite test)
**Deviations** (advisory): (1) the host passes its `ZoneDefinitionSlot` (zone id unknown in `Awake`); the composition builds `ZoneDefinitionSlotRespawnPointProvider` at S5, the `RespawnProviderOverride` test hook wins when set; (2) with no provider (test compositions) Movement is built with the 6-arg ctor (null-provider branch); (3) packets carry positions only (`EntityPositionUpdate` has no facing).
**Scope**: + `ZoneSlotPool.TryGetPlayerAtSlot` (allocation-free slot read); test updates in `ZoneServer_CompositionWiring_tests.cs` (`CONSTRUCTION_ORDER`, recording send seam) and `StatusEffects_PipelineWiring_tests.cs` (simulation list now 3 entries).
**Test Evidence**: Integration — `tests/EditMode/MovementSystem/MovementSystem_PipelineWiring_tests.cs`
**Code Review**: Complete — APPROVED WITH SUGGESTIONS; applied: `Flush` split (`CollectSorted`), ghost-broadcast comment, facing wording fixed
