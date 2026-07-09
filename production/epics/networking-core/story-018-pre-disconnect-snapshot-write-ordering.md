# Story 018: Pre-Disconnect Snapshot & Write-Ordering

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-ghost-character-state.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR — this story's write-ordering guarantee is the ghost-specific instance of CR-NET-5's commit-before-broadcast principle (Story 011), applied to the ghost promotion/cleanup/death boundary.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: pre-disconnect snapshot written to an external WAL keyed on `(characterId, disconnectTickNumber)` before ghost promotion continues — source: CGS-3
- Required: persistence committed before resource release / event emission, on both TTL expiry and ghost death — source: CGS-4, CGS-5 (mirrors CR-NET-5)
- Required: idempotent WAL writes — a re-submitted identical snapshot is a no-op, not a second write — source: EC-CGS-2

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-character-state.md`, scoped to this story:*

- [ ] **AC-CGS-1** [BLOCKING]: Given a ghost entity that receives N damage during the ghost period and then expires via TTL without reconnect, when character state is written to persistence, then the persisted HP equals the pre-disconnect snapshot HP — not the ghost-period-reduced HP.
- [ ] **AC-CGS-2** [BLOCKING]: Given a ghost entity whose HP reaches zero, when death processing completes and state is persisted, then the persisted HP equals the pre-disconnect snapshot HP (not respawn HP — no HP penalty on ghost death, GD-CGS-2); persisted position equals the zone-entry respawn position.
- [ ] **AC-CGS-3** [BLOCKING]: Given a ghost entity whose HP reaches zero AND a reconnect acknowledgment queued in the same server tick, when the tick processes both, then death processing wins (single-tick priority rule) — session transitions to `Disconnected_SessionExpired` reason `GHOST_DEATH`, the reconnecting client receives `ZoneSessionEnded(reason: GhostDeath)`.
- [ ] **AC-CGS-4** [BLOCKING]: Given a ghost TTL expiry, when the cleanup sequence runs, then `OnPersistenceWriteCompleted` fires before the session transitions to `Disconnected_SessionExpired` and before `GhostExpiredEvent` is emitted — verified by sequence-index ordering, not timestamps (events within the same 50ms tick have no meaningful timestamp ordering).

---

## Implementation Notes

*Derived from CGS-3 through CGS-6:*

- **CGS-3 pre-disconnect snapshot** (taken at `Connected→Disconnected_SessionActive`, i.e. hooked into Story 017's promotion step 1): (Step 1) record `disconnectTickNumber` as the idempotency key; (Step 2) snapshot ALL required fields — `currentHP`, `maxHP`, `currentMP`, `maxMP`, `Position`, `Rotation`, `Inventory`, `XP`, `Level`, `heldFreePoints`, `allocatedStats`, `equipmentAppearanceFlags`, `disconnectTickNumber`, `activeBuffs`, `skillCooldowns` — no field may be omitted; (Step 3) write to an external WAL keyed on `(characterId, disconnectTickNumber)` before ghost promotion continues.
- **CGS-4 (TTL expiry, no death)**: apply WAL entry (idempotent) → on confirm, remove entity/release party slot/transition state → emit `GhostExpiredEvent(reason: GhostTtlExpired)`. HP persisted = snapshot HP; ghost-period damage never persisted.
- **CGS-5 (ghost death)**: record `wasKilledWhileDisconnected=true` → apply respawn position ONLY (HP not modified — snapshot HP preserved) → persist (WAL + respawn position substituted) before any zone event → emit `GhostExpiredEvent(reason: GhostDeath)` after persistence confirms → transition to `Disconnected_SessionExpired` → reconnect after ghost death is rejected via `ZoneSessionEnded(reason: GhostDeath)`.
- **CGS-6 (death-vs-reconnect race)**: single-tick priority — if a killing blow and reconnect ACK are queued the same tick, process the death queue before the reconnect queue (the tick loop's single-threaded dispatch is the serialization point, no lock needed).
- **EC-CGS-2 idempotency**: WAL writes are keyed on `(characterId, disconnectTickNumber)` — a re-submitted identical snapshot (e.g. after a crash mid-write) is a no-op, not a duplicate write. Character ID must be write-locked during persistence to prevent concurrent double-writes.

---

## Out of Scope

*Handled by neighbouring stories:*

- Ghost promotion trigger/freeze itself — Story 017
- Mob de-targeting on death/expiry — Story 019
- XP forfeit policy — Story 020
- Full cleanup sequence orchestration (this story owns the *write-ordering guarantee*; Story 021 owns the *sequence steps around it*) — Story 021

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs`

- **AC-CGS-1**: Given N ghost-period damage then TTL expiry, then persisted HP = snapshot HP.
- **AC-CGS-2**: Given ghost death, then persisted HP = snapshot HP, persisted position = respawn position.
- **AC-CGS-3**: Given same-tick death + reconnect ACK, then death wins, reconnect gets `ZoneSessionEnded(GhostDeath)`.
- **AC-CGS-4**: Given TTL expiry cleanup, then `OnPersistenceWriteCompleted` precedes both the state transition and `GhostExpiredEvent` in sequence-index order.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 017 (promotion trigger this snapshot hooks into), Story 011 (shares the commit-before-broadcast reasoning pattern)
- Unlocks: Story 019 (ghost death uses this write-ordering), Story 021 (cleanup sequence uses this write-ordering)
