# Story 007: ZoneStateSnapshot Assembly and Fragmentation (F-ZI-1)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 3 hours (provisional — confirm at `/story-readiness`)

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
- Guardrail: reuse one serialization buffer per zone; no per-entity allocation

---

## Acceptance Criteria

- [ ] A snapshot builder reads Story 002's player slots and Story 003's mob slots and writes one `EntityState` per occupied slot: player entries with all player fields, mob entries with `MobTypeID`; empty slots skipped.
- [ ] Dead players: `isInDeadState = true`, `respawnTicksRemaining = max(0, RespawnTick − currentTick)`; otherwise false / 0.
- [ ] **Serialize first**: the whole `EntityState[]` is serialized before splitting; `totalFragments = max(1, ⌈actualBytes / 504⌉)`; never a worst-case estimate.
- [ ] `snapshotVersion` = the current server tick, the same on every fragment of one snapshot.
- [ ] **AC-ZI-8**: with P = 50 players and M = 150 mobs and known names, the number of `ZoneStateSnapshotFragment` messages sent to the joining client equals F-ZI-1 for those names (17 with average-length names, 19 with 24-byte names), asserted through `INetworkTestObserver.GetOutboundMessageCount(clientId, ZoneStateSnapshotFragment.MessageTypeId)`.
- [ ] **Empty zone**: P = M = 0 sends exactly 1 fragment with an empty payload (dual-gate needs a complete snapshot).
- [ ] An entry never straddles a fragment boundary in a way the client cannot reassemble: the payload is the contiguous byte stream split at 504-byte offsets (client reassembles before parsing — wire protocol step 3).

---

## Implementation Notes

- Fields this zone process does not own yet (HP, MP, level, rotation, `isInCombat`, `cycleTimer`, appearance flags) come through a read seam (for example `IZoneEntityStateSource`) with a test double; the real sources (Character Stats, combat, Equipment) are wired in Story 014 as far as they exist, the rest stay stand-ins.
- `isGhost` comes from the slot's `ZoneSessionState == Disconnected_SessionActive`.
- AC-ZI-8's "22 chars" wording in the GDD is the 24-byte UTF-8 cap — use names that encode to exactly 24 bytes for the worst case.

---

## Out of Scope

- Story 008: retransmit on `ZoneSnapshotRequest`
- Story 010: when in the entry sequence the snapshot is sent

---

## QA Test Cases

- **AC-ZI-8 average** — 50 players with 11-byte names + 150 mobs → expected F computed from actual bytes (17); observer count matches.
- **AC-ZI-8 worst** — 50 players with 24-byte names + 150 mobs → 19.
- **Empty** — P = M = 0 → 1 fragment, `totalFragments = 1`.
- **Dead player** — RespawnTick 140 at tick 100 → `respawnTicksRemaining = 40`; at tick 150 → 0.
- **Version** — all fragments carry the build tick.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_SnapshotAssembly_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 003, 006
- Unlocks: Stories 008, 010
