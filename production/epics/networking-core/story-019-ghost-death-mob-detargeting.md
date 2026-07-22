# Story 019: Ghost Death & Mob De-Targeting

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-ghost-session.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR — `MobDeTargetCommand` and `GhostExpiredEvent` are network-boundary signals to the AI subsystem and zone clients respectively.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: on ghost death, issue `MobDeTargetCommand` for every mob targeting the ghost, processed within one AI tick — source: CR-GH-6, CR-GH-7
- Required: de-targeting completes within `DE_TARGET_DEADLINE_MS = ZONE_TICK_MS + 2×MOB_AI_TICK_MS` (250ms default) — source: F-GH-2

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-session.md`, scoped to this story:*

- [ ] **AC-GH-4** [BLOCKING]: Given `IsGhost=true` and ghost HP=1, when an attack deals ≥1 damage, then HP reaches zero, death processing begins, session transitions to `Disconnected_SessionExpired` reason `GHOST_DEATH`, pre-disconnect snapshot HP is persisted (no HP penalty), and post-disconnect party XP shares=0 are written. **Pass condition (restored from GDD, story-readiness fix):** `OnSessionStateTransitioned(accountId, Disconnected_SessionActive, Disconnected_SessionExpired, "GhostDeath")` fires; `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath)` fires.
- [ ] **AC-GH-5** [BLOCKING]: Given a mob with an `IsGhost=true` character as its current target, when `GHOST_COMBAT_TTL` expires, then within `DE_TARGET_DEADLINE_MS` (250ms default), the mob's target clears and it enters idle/patrol. **Pass condition (restored from GDD, story-readiness fix):** `OnGhostCombatTTLExpired(entityId, disconnectTickNumber, expiryTick)` fires at `expiryTick`; mob target field = null within 250ms of *that event* (1 zone tick + 2 AI ticks at defaults).
- [ ] **AC-GH-9** [BLOCKING]: Given a ghost entity that received D damage during the ghost period and then expired via TTL without reconnect, when state is written at cleanup, then persisted HP equals the disconnect-moment HP (not `disconnectHP - D`). **Pass condition (restored from GDD, story-readiness fix):** `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry)` fires; persisted HP = HP captured at the `Connected → Disconnected_SessionActive` transition. **Scope clarification (story-readiness fix):** this is the identical scenario Story 018's AC-CGS-1 already tests and owns via `GhostCleanupSequencer.CompleteTTLExpiryCleanup`. This story's own test for AC-GH-9 should be an integration-style composition test — proving this story's death/de-targeting flow correctly calls into Story 018's already-tested sequencer — not a duplicate re-derivation of the HP-persistence guarantee itself.
- [ ] **AC-GH-17** [BLOCKING]: Given a ghost whose TTL expires in the same server frame as a zone sync tick, when both are processed, then the zone tick delivered to clients does NOT contain the expired ghost entity, and `GhostExpiredEvent` is delivered before or in the same network batch as the tick. **Pass condition (restored from GDD, story-readiness fix):** `OnGhostCombatTTLExpired` fires at expiry tick T; the zone tick delivered to clients at T contains no entry for the expired ghost entity.

---

## Implementation Notes

*Derived from CR-GH-6, CR-GH-7, F-GH-2, EC-GH-6:*

```
DE_TARGET_DEADLINE_MS = ZONE_TICK_MS + 2 × MOB_AI_TICK_MS
                      = 50 + 2×100 = 250ms (defaults)
```
- CR-GH-6 death-during-ghost sequence: (1) process death normally (HP set to respawn value via CGS-5's write-ordering, `wasKilledWhileDisconnected=true`); (2) issue `MobDeTargetCommand` for every mob targeting the ghost, processed within one AI tick; (3) ghost period ends immediately; (4) session → `Disconnected_SessionExpired` reason `GHOST_DEATH`; `GhostExpiredEvent(reason: GhostDeath)` emitted after persistence confirmed; (5) post-disconnect party XP shares forfeited; (6) reconnect after ghost death enters respawn flow, not reconnect flow — client receives `ZoneSessionEnded(reason: GhostDeath)`.
- CR-GH-7 de-targeting deadline rationale (F-GH-2): worst-case three-step propagation — one zone tick for the AI subsystem to detect the expiry event, one AI tick for `MobDeTargetCommand` to process, one more AI tick for the de-target state to confirm and broadcast. Log a warning if any mob remains targeted on an expired ghost after the deadline.
- EC-GH-6 (simultaneous expiry + zone tick): the server must process ghost cleanup BEFORE generating that tick's zone-state message — clients must never receive a tick containing an already-expired ghost entity.
- This story depends on an AI subsystem interface (`MobDeTargetCommand` receiver) that likely doesn't fully exist yet (Enemy AI epic not yet created) — implement against a mock/stub AI subsystem interface for this story's own tests; wire the real AI subsystem when that epic exists.
- **Test-observability resolution (story-readiness fix)**: `INetworkTestObserver`'s full callback surface has no hook for `MobDeTargetCommand` issuance — confirmed by reading the interface directly. Do not add a new observer callback for this. Model it as a caller-supplied delegate parameter (e.g. `issueMobDeTargetCommand: Action<uint>`, called once per mob targeting the ghost), the same delegate-seam resolution already used repeatedly this epic (`GhostExpiredEvent`, `ZoneSessionEnded` in Story 018).

---

## Out of Scope

*Handled by neighbouring stories:*

- Ghost promotion/freeze — Story 017
- Pre-disconnect snapshot/write-ordering mechanics — Story 018 (this story calls into it)
- XP forfeit policy detail — Story 020
- Real Enemy AI targeting/pathing logic — future AI epic (this story only defines the `MobDeTargetCommand` contract and its latency budget)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs`

- **AC-GH-4**: Given ghost HP=1 taking ≥1 damage, then death sequence fires exactly as described.
- **AC-GH-5**: Given TTL expiry with a mob targeting the ghost, then de-targeting completes within 250ms (mock AI tick simulation).
- **AC-GH-9**: Given D ghost-period damage then TTL expiry, then persisted HP = disconnect-moment HP.
- **AC-GH-17**: Given simultaneous TTL expiry + zone tick, then the tick excludes the expired ghost and `GhostExpiredEvent` precedes/accompanies it.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 017 (promotion), Story 018 (write-ordering)
- Unlocks: Story 021 (cleanup sequence composes this death path)

**Note**: `GHOST_COMBAT_TTL` constant — this story uses `networking-ghost-session.md`'s F-GH-1 formula (`GHOST_COMBAT_TTL_MIN_S` + OWL scaling, 30s baseline) as authoritative, per the cross-doc inconsistency flagged at epic creation time. **Resolved (story-readiness fix)**: this is already tracked at the epic level (`EPIC.md`'s known-inconsistencies list: "Stories 019/021 use the ghost-session formula as authoritative pending a design decision") — no need to re-raise with the user/design lead; use that interim resolution. Neither of this story's 4 ACs depends on the exact numeric value regardless (TTL expiry is an externally-triggered event here via `OnGhostCombatTTLExpired`, not something this story computes).

---

## Completion Notes
**Completed**: 2026-07-18
**Criteria**: 4/4 passing (AC-GH-4, AC-GH-5, AC-GH-9, AC-GH-17) — none deferred
**Deviations**: None blocking. Advisory: (1) TR-net-006 registry gap (systemic, pre-existing); (2) AC-GH-4's "post-disconnect party XP shares=0" clause is explicitly out of scope (Story 020 owns it) — documented in-test rather than silently omitted; (3) AC-GH-17's ordering proof is structural/test-constructed (no real zone-tick serializer exists yet to compose against) — honestly documented in the test file's class remarks, same accepted class of limitation as Story 018's AC-CGS-3.
**Test Evidence**: Logic — `tests/EditMode/Networking/GhostSession_DeathDeTargeting_tests.cs` (10 tests, all 4 blocking ACs covered)
**Code Review**: Complete (performed directly, both specialist agents unavailable due to session rate limit; reused deep verification from implementation plus targeted symmetry checks against Story 018) — 3 suggestions raised, 2 applied (AC-GH-17 honest caveat, AC-GH-4 XP-scope note), 1 declined by user (symmetric test addition to Story 018's closed suite — marginal value, out of scope)
