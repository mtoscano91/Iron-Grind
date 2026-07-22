# Story 018: Pre-Disconnect Snapshot & Write-Ordering

> **Epic**: Networking Core
> **Status**: Complete
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
**Performance Notes**: No performance impact expected — CGS-6's same-tick priority ordering is structural (synchronous method-call order within one tick's single-threaded dispatch), not a new per-tick scan.

**Control Manifest Rules (Foundation layer)**:
- Required: pre-disconnect snapshot written to an external WAL keyed on `(characterId, disconnectTickNumber)` before ghost promotion continues — source: CGS-3
- Required: persistence committed before resource release / event emission, on both TTL expiry and ghost death — source: CGS-4, CGS-5 (mirrors CR-NET-5)
- Required: idempotent WAL writes — a re-submitted identical snapshot is a no-op, not a second write — source: EC-CGS-2

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-character-state.md`, scoped to this story:*

- [x] **AC-CGS-1** [BLOCKING]: Given a ghost entity that receives N damage during the ghost period and then expires via TTL without reconnect, when character state is written to persistence, then the persisted HP equals the pre-disconnect snapshot HP — not the ghost-period-reduced HP. **Pass condition (restored from GDD, story-readiness fix):** `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry)` fires before `OnSessionStateTransitioned(_, Disconnected_SessionExpired)`; character persistence record HP = disconnect-moment HP.
- [x] **AC-CGS-2** [BLOCKING]: Given a ghost entity whose HP reaches zero, when death processing completes and state is persisted, then the persisted HP equals the pre-disconnect snapshot HP (not respawn HP — no HP penalty on ghost death, GD-CGS-2); persisted position equals the zone-entry respawn position. **Pass condition (restored from GDD, story-readiness fix):** `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath)` fires; character persistence record HP = pre-disconnect snapshot HP; character persistence record position = respawn position; `wasKilledWhileDisconnected = true` in session record.
- [x] **AC-CGS-3** [BLOCKING]: Given a ghost entity whose HP reaches zero AND a reconnect acknowledgment queued in the same server tick, when the tick processes both, then death processing wins (single-tick priority rule) — session transitions to `Disconnected_SessionExpired` reason `GHOST_DEATH`, the reconnecting client receives `ZoneSessionEnded(reason: GhostDeath)`. **Pass condition (restored from GDD, story-readiness fix — the `fromState` matters):** `OnSessionStateTransitioned(accountId, Reconnecting, Disconnected_SessionExpired, "GhostDeath")` fires — note the `fromState` is `Reconnecting`, NOT `Disconnected_SessionActive`; this scenario is specifically a death racing a reconnect already in progress, a transition row no prior story has built (`ConnectionStateMachine`'s existing `Reconnecting`-adjacent methods — `EnterReconnecting`, `CompleteReAuthSuccess`, `RecordFailedReAuthAttempt`, `HandleReconnectSessionSteal` — none represent this case). Reconnect client receives `ZoneSessionEnded` with `reason = DisconnectReason.GhostDeath` (already an existing enum value, `= 3`). **Test-technique resolution (story-readiness fix, same class of issue as Story 017's AC-GH-13):** the GDD's own text says "Automatable via `ITransportFaultInjector` (inject reconnect ACK in same tick as injected killing blow)" — confirmed by direct interface read that this is imprecise: `ITransportFaultInjector`'s entire API (`DropNextOutbound`/`DelayNextOutbound`/`ReorderNext`/`DropSnapshotFragment`/`SetSequenceNumber`) is outbound-fault-injection only, with no capability to inject an inbound reconnect ACK. Do not attempt to use it for this. Instead, drive both code paths (death processing, reconnect-ACK processing) directly against whatever new ordering-guarantee method this story builds, proving the single-tick priority rule structurally — same resolution shape as Story 017's `ShouldRejectCommand`.
- [x] **AC-CGS-4** [BLOCKING]: Given a ghost TTL expiry, when the cleanup sequence runs, then `OnPersistenceWriteCompleted` fires before the session transitions to `Disconnected_SessionExpired` and before `GhostExpiredEvent` is emitted — verified by sequence-index ordering, not timestamps (events within the same 50ms tick have no meaningful timestamp ordering). **Pass condition (restored from GDD, story-readiness fix):** callback order verified by sequence index (a monotonically incrementing counter reset per tick). Automatable via `IServerCrashInjector.AfterGhostCleanupPersistenceWrite` (already an existing crash step — crash after step 1, character state must be durable on recovery).

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

**Status**: [x] Created — 18 test cases, all 4 blocking ACs COVERED with traceability

---

## Dependencies

- Depends on: Story 017 (promotion trigger this snapshot hooks into), Story 011 (shares the commit-before-broadcast reasoning pattern)
- Unlocks: Story 019 (ghost death uses this write-ordering), Story 021 (cleanup sequence uses this write-ordering)

---

## Completion Notes
**Completed**: 2026-07-18
**Criteria**: 4/4 passing (AC-CGS-1, AC-CGS-2, AC-CGS-3, AC-CGS-4)
**Deviations**: ADVISORY — TR-net-006 not in `docs/architecture/tr-registry.yaml` (systemic, pre-existing gap). ADVISORY — TD-019 logged: no `CrashStep` exists for the CGS-3 snapshot-WAL write specifically, so EC-CGS-2's crash-recovery narrative is proven at the logic level only, not end-to-end via a real crash injection. ADVISORY — AC-CGS-2's `wasKilledWhileDisconnected = true` clause is unverifiable at this layer (no real persisted-record type exists yet); documented honestly rather than asserted vacuously. ADVISORY — AC-CGS-3's "single-tick priority" is proven as caller-discipline-enforced ordering (both directions tested symmetrically), not system-arbitrated — no per-tick dispatcher exists yet to arbitrate independently of call order; that arbitration is a future orchestration story's job.
**Test Evidence**: Logic: `tests/EditMode/Networking/GhostSession_SnapshotWriteOrdering_tests.cs` (18 test cases)
**Code Review**: Complete — `/code-review` (lean mode, unity-specialist + qa-tester parallel): APPROVED WITH SUGGESTIONS. unity-specialist: CLEAN — mechanically verified every claim including EC-CGS-2 idempotency, call orders against the actual GDD source, and crash-simulation soundness. qa-tester: GAPS — found the mechanical correctness didn't fully match the AC's semantic claims (a vacuous test assertion, a design-consistency gap in `HandleGhostDeathWhileReconnecting`'s parameter list, and an honest reframing of what the AC-CGS-3 tests can prove absent a real dispatcher). All 4 suggestions fixed: `HandleGhostDeathWhileReconnecting` now derives `characterId` structurally from the account record; the vacuous assertion was removed and honestly documented; a reverse-order symmetry test was added; cross-character isolation is now tested; TD-019 logged. Final test count: 18 (16 + 2 new).

**Second story in the Ghost Session cluster (017-021) — the write-ordering guarantee Stories 019 and 021 will both build on.**
