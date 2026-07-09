# Story 021: Ghost Cleanup, Zone Crash & Voluntary Dismissal

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-ghost-session.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR — this story closes the Ghost Session cluster with the orchestrated cleanup sequence and its two failure/exit paths (zone crash, voluntary dismissal).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: cleanup sequence order — de-target mobs → wait one AI tick → persist (confirmed) → remove entity → release party slot → transition session → emit `GhostExpiredEvent` — source: CR-GH-10
- Required: zone crash handler enumerates all ghost/reconnecting sessions and writes their pre-disconnect snapshots before instance termination — source: CR-GH-11
- Required: voluntary dismissal by any party member cancels the TTL timer and executes the same cleanup sequence with `reason=GHOST_DISMISSED` — source: CR-GH-12

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-session.md`, scoped to this story:*

- [ ] **AC-GH-10** [BLOCKING]: Given a ghost entity in a zone configured with `IServerCrashInjector.RegisterCrashAt(CrashStep.AfterGhostCleanupPersistenceWrite)`, when the crash fires after cleanup step 3, then the character persistence record exists after restart and equals the pre-disconnect snapshot.
- [ ] **AC-GH-11** [BLOCKING]: Given a party member sending `GhostDismissRequest` for a ghost in `Disconnected_SessionActive`, when the server processes it, then: `GHOST_COMBAT_TTL` timer cancels; ghost entity removed; party slot released; `GhostExpiredEvent(reason: GHOST_DISMISSED)` emits; persisted XP = pre-disconnect XP (banked); session → `Disconnected_SessionExpired` reason `GHOST_DISMISSED`.
- [ ] **AC-GH-14** [BLOCKING]: Given a ghost in `Disconnected_SessionActive` (TTL started at T=0), and a failed reconnect at T=15s, when the session returns to `Disconnected_SessionActive` at T=20s, then the TTL timer continues from T=20s (not reset) — expiry occurs at `T=0 + GHOST_COMBAT_TTL_S`, never re-armed.
- [ ] **AC-GH-16** [BLOCKING]: Given a zone at capacity N-1 (one slot remaining), when a player disconnects and becomes a ghost, and a new player attempts to join, then the join is rejected — the ghost occupies the final slot.
- [ ] **AC-GH-20** [BLOCKING]: Given a zone with two ghost sessions (one `Disconnected_SessionActive`, one `Reconnecting`) and a configured crash before cleanup completes, when the crash handler runs, then both characters' pre-disconnect snapshots are written to persistence before zone termination.

---

## Implementation Notes

*Derived from CR-GH-10, CR-GH-10.1, CR-GH-11, CR-GH-12, CR-GH-12.1:*

- **CR-GH-10 cleanup sequence**, strict order: (1) issue `MobDeTargetCommand` for all mobs targeting the ghost (Story 019's mechanism); (2) wait one AI tick for de-targeting completion; (3) persist the CGS-3 pre-disconnect snapshot (Story 018's WAL) to external persistence — MUST confirm before proceeding; (4) remove ghost entity from zone instance state; (5) release party slot (Story 020); (6) transition session to `Disconnected_SessionExpired` with the caller-supplied reason (`GHOST_TTL_EXPIRED` or `GHOST_DISMISSED`); (7) emit `GhostExpiredEvent` with the matching reason, AFTER session close (clients must not receive a ghost event for an already-closed session).
- **CR-GH-11 zone crash**: crash handler must enumerate ALL sessions in `Disconnected_SessionActive` OR `Reconnecting` before terminating the instance; for each, write the pre-disconnect snapshot (equivalent to cleanup step 3); the CGS-3 idempotency guarantee (keyed on `characterId + disconnectTickNumber`) prevents double-writes if a write is already in progress; sessions transition to `Disconnected_SessionExpired` reason `ZONE_CRASH`; ghost cleanup *events* (`GhostExpiredEvent`, `MobDeTargetCommand`) are NOT emitted — the zone instance is already gone, there's no one to deliver them to.
- **CR-GH-12 voluntary dismissal**: any party member (including leader) may trigger dismissal any time during the ghost period via `GhostDismissRequest` (C→S, R-OD). Server validates: requester is a valid party member of the ghost's party, and the ghost session is `Disconnected_SessionActive`. On valid request: (1) validate; (2) cancel the TTL timer (prevents a race with concurrent TTL expiry); (3) execute the CR-GH-10 cleanup sequence with `reason=GHOST_DISMISSED` substituted at steps 6 and 7 — otherwise identical to the TTL-expiry path.
- **EC-GH-14/double-disconnect**: a failed re-auth attempt during `Reconnecting` returns to `Disconnected_SessionActive` WITHOUT resetting the TTL — it continues counting from the original disconnect tick, never re-armed.

---

## Out of Scope

*Handled by neighbouring stories:*

- Promotion, snapshot, death, and reward-forfeit mechanics — Stories 017–020 (this story is the orchestration layer on top)
- Real Zone Instancing crash-recovery/replacement-instance routing — future Zone Instancing epic

---

## QA Test Cases

*Test file*: `tests/PlayMode/Networking/GhostSession_Cleanup_Crash_Dismissal_tests.cs`

- **AC-GH-10**: Given a crash after cleanup step 3, then the persisted record survives and matches the snapshot.
- **AC-GH-11**: Given a valid `GhostDismissRequest`, then cleanup runs with `GHOST_DISMISSED` reason and banked XP only.
- **AC-GH-14**: Given a failed reconnect mid-TTL, then the TTL never resets — expiry tick is unchanged from the original disconnect.
- **AC-GH-16**: Given a ghost occupying the last capacity slot, then a new join is rejected.
- **AC-GH-20**: Given two ghost sessions and a crash before cleanup, then both snapshots are durably persisted.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Networking/GhostSession_Cleanup_Crash_Dismissal_tests.cs` OR documented playtest evidence

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 017 (promotion), Story 018 (snapshot/write-ordering), Story 019 (death/de-targeting), Story 020 (reward forfeit), Story 001 (`IServerCrashInjector`)
- Unlocks: None — this closes the Ghost Session cluster

**Note**: `GHOST_COMBAT_TTL` constant inconsistency (see Story 019's note) applies here too, since this story's TTL-continuity test (AC-GH-14) depends on the same constant.
