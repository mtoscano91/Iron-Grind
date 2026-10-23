# Story 007: ZoneStateSnapshot Assembly and Fragmentation (F-ZI-1)

> **Epic**: Zone Instancing
> **Status**: Complete — 2026-10-23 (`/story-done`, COMPLETE WITH NOTES); readiness 2026-10-23: NEEDS WORK (7 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 6 hours

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-8 step 6, F-ZI-1, AC-ZI-8; `networking-wire-protocol.md` — `totalFragments` computation (REQUIRED)
**Requirement**: `TR-zi-008` *(epic placeholder)*

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: the snapshot is a bulk transfer of R-OD fragments, exempt from the priority-path cap.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (no NGO call here — output goes to the outbox seam)
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` — ADR-012
- Required: outbound messages go through the existing client message outbox — ADR-004 / ADR-014
- Outbox bulk range (this story): `ClientMessageOutbox` accepts 0xE000–0xEFFF and 0xF000–0xFFFF (bulk); every type still needs a routing row
- Guardrail: reuse one serialization buffer per zone; no per-entity allocation

---

## Acceptance Criteria

- [x] `ZoneSnapshotBuilder.SendSnapshot(uint clientId)` (main thread; tick read from `IServerTickSource`) writes one `EntityState` per occupied slot: players first in `ZoneSlotPool.GetPlayerEntityIDsInZone` order, then mobs in the order of the new `ZoneSlotPool.GetMobEntityIDsInZone`. Player entries carry all player fields; mob entries carry `MobTypeId` from `IZoneMobRoster.TryGetMob`. The joining client's own slot is included.
- [x] `isInDeadState = true` exactly when the slot's `ZoneSessionState` is `Dead` or `Respawning`; then `respawnTicksRemaining = max(0, RespawnTick − currentTick)` with `RespawnTick` from the seam; otherwise false / 0. `isGhost = (state == Disconnected_SessionActive)`.
- [x] **Serialize first**: the whole entry stream is written into one per-zone buffer preallocated at `maxPlayers × 77 + maxMobs × 35` bytes (9,100 at defaults); then `totalFragments = max(1, ⌈actualBytes / 504⌉)`; never a worst-case estimate.
- [x] `snapshotVersion` = the current server tick, the same on every fragment of one snapshot.
- [x] **AC-ZI-8**: with P = 50 players and M = 150 mobs, `INetworkTestObserver.GetOutboundMessageCount(clientId, ZoneStateSnapshotFragment.MessageTypeId)` equals 17 with 11-byte names and 19 with names that encode to 24 bytes.
- [x] **Empty zone**: P = M = 0 sends exactly 1 fragment, `totalFragments = 1`, empty payload (dual-gate needs a complete snapshot).
- [x] **Contiguous split**: every fragment except the last carries exactly 504 payload bytes; concatenating the payloads in `fragmentIndex` order equals the serialized stream byte for byte, and walking it with `EntityStateCodec.TryReadBody` yields every entry (client reassembles before parsing — wire protocol step 3).
- [x] **Seam miss**: an entity the seam has no data for is skipped with one warning per call; the fragment count is computed from the bytes actually written.
- [x] **Outbox bulk range**: `ClientMessageOutbox.Enqueue` of 0xF000 (routing row present) is accepted; 0xF001 (no routing row) is refused; 0xDFFF is still refused. Existing `ClientMessageOutbox_tests` stay green.
- [x] **Observer counter**: the outbox calls `NetworkTestObserver.RecordOutboundMessage(clientId, messageTypeId)` once per accepted `Enqueue`, never for a refused one. The hook is set through a setter compiled only under `#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD` (the observer's own guard); with no observer set, nothing is recorded.
- [x] **Allocation**: a second `SendSnapshot` call at P = 50 / M = 150 passes `Is.Not.AllocatingGCMemory` (names cached by the seam double).

---

## Implementation Notes

- Read seam `IZoneEntityStateSource` (fields the zone process does not own):
  - `bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)` — position, rotation, facing angle, HP/MP (current and max), level, `isInCombat`, `cycleTimer`, appearance flags, character name, `RespawnTick`.
  - `bool TryGetMobState(EntityID id, out ZoneMobStateView view)` — current position, rotation, facing angle, HP (current and max), `isInCombat`, `cycleTimer`.
  - Test double in this story; the real sources (Character Stats, combat, Equipment, Death & Respawn) are wired in Story 014 as far as they exist, the rest stay stand-ins.
- Mob enumeration: add `IReadOnlyList<EntityID> GetMobEntityIDsInZone(ZoneID zone)` to `ZoneSlotPool`, built like the player roster (read-only view over a preallocated list, swap-remove, no allocation; foreign zone → empty list + warning, as for players).
- Encode each entry with `EntityStateCodec.WriteBody` at the running offset (`GetBodySize` exact sizes), then slice at 504-byte offsets and encode each `ZoneStateSnapshotFragment`; enqueue every fragment with `isCapExempt: true`.
- Outbox: widen the type guard in `ClientMessageOutbox.TryAcceptMessage` (now 0xE000–0xEFFF only, line ~369) to also accept 0xF000–0xFFFF; update the log text and the `PRIORITY_RANGE_*` docs (add `BULK_RANGE_FIRST` / `BULK_RANGE_LAST` constants). Call `RecordOutboundMessage` after the message is queued.
- AC-ZI-8's "22 chars" wording in the GDD is the 24-byte UTF-8 cap — use names that encode to exactly 24 bytes for the worst case. 11-byte names give 64-byte player entries (the GDD's "avg ~65" is one byte high; the count is still 17).

---

## Out of Scope

- Story 008: retransmit on `ZoneSnapshotRequest`
- Story 010: when in the entry sequence the snapshot is sent
- Story 014: wiring the real `IZoneEntityStateSource` sources
- Client-side reassembly (already in `ZoneSnapshotReassemblyTracker`)
- GDD F-ZI-1 "avg ~65 B" wording (follow-up note only)

---

## QA Test Cases

- **AC-ZI-8 average** — 50 players with 11-byte names + 150 mobs → 17; observer count matches.
- **AC-ZI-8 worst** — 50 players with 24-byte names + 150 mobs → 19.
- **Empty** — P = M = 0 → 1 fragment, `totalFragments = 1`, empty payload.
- **Dead player** — `Dead`, RespawnTick 140 at tick 100 → `respawnTicksRemaining = 40`; at tick 150 → 0; `Respawning` → dead flag true; `Connected` → false / 0.
- **Ghost** — `Disconnected_SessionActive` → `isGhost = true`.
- **Version** — all fragments carry the build tick.
- **Contiguous** — reassembled payload equals the stream; all but the last fragment are 504 B; decoding walks every entry in order.
- **Seam miss** — one player missing from the double → skipped, one warning, count from actual bytes.
- **Outbox range** — 0xF000 accepted and counted; 0xF001 / 0xDFFF refused and not counted.
- **Allocation** — second call does not allocate.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**:
- `tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotAssembly_tests.cs` — must exist and pass.
- `tests/EditMode/Networking/ClientMessageOutbox_tests.cs` — bulk-range and observer-counter cases added, all passing.

**Status**: [x] Created and passing (SnapshotAssembly 25 tests; outbox +7, 1 rewritten; SlotPools +2)

---

## Dependencies

- Depends on: Stories 002, 003, 006 (all Complete)
- Unlocks: Stories 008, 010

---

## Completion Notes
**Completed**: 2026-10-23
**Criteria**: 11/11 passing, all by automated test. None deferred.
**Deviations** (advisory, notes only — not logged as tech debt):
1. Added at code review: a mob whose `MobTypeID.RawValue` is above `ushort.MaxValue` (wire field is 16-bit, OQ-ZI-2) is skipped with the one-per-call `SnapshotEntitySkipped` warning instead of being silently narrowed.
2. `IClientMessageOutbox.Enqueue` returns nothing, so the builder cannot see a refused fragment; `SendSnapshot` returns `totalFragments` handed to the outbox. A refusal leaves the client without a complete snapshot; the outbox logs it and, on a full queue, requests the disconnect (documented on the class).
3. The `ZoneSlotPool.TryGetPlayer` miss branch in the builder is unreachable today (roster and slot table move together) and has no test.
4. GDD follow-ups (unchanged from readiness): AC-ZI-8 "22 chars" is the 24-byte UTF-8 cap; F-ZI-1 "avg ~65 B" is 64 B with 11-byte names.
5. `TR-zi-008` remains a placeholder until `/architecture-review`.
**Scope**: within the story (outbox, `ZoneSlotPool`, four new files in `src/ServerLogic/ZoneInstancing/`, three test files).
**Notes for other stories**:
- Story 008: reuse `ZoneSnapshotBuilder.SendSnapshot(clientId)` for the retransmit; the snapshot version is the tick at send time, so a retransmit gets a new version.
- Story 010: call `SendSnapshot` at CR-ZI-8 step 6; the builder needs the zone's `IZoneMobRoster` and an `IZoneEntityStateSource`.
- Story 014: wire the real `IZoneEntityStateSource` (Character Stats, combat, Equipment, Death & Respawn); it must not allocate, and character names must be cached strings, or the allocation guarantee breaks.
- Wiring: call `ClientMessageOutbox.SetTestObserver` only under `UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`.
**Test Evidence**: Logic — `tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotAssembly_tests.cs`, `tests/EditMode/Networking/ClientMessageOutbox_tests.cs`, `tests/EditMode/ZoneInstancing/ZoneInstancing_SlotPools_tests.cs` (EditMode batch: 3850 total, 0 failed, 70 skipped Npgsql).
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, all 6 applied (`46ac08e`).
