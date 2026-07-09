# Story 019: Ghost Death & Mob De-Targeting

> **Epic**: Networking Core
> **Status**: Ready
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

- [ ] **AC-GH-4** [BLOCKING]: Given `IsGhost=true` and ghost HP=1, when an attack deals ≥1 damage, then HP reaches zero, death processing begins, session transitions to `Disconnected_SessionExpired` reason `GHOST_DEATH`, pre-disconnect snapshot HP is persisted (no HP penalty), and post-disconnect party XP shares=0 are written.
- [ ] **AC-GH-5** [BLOCKING]: Given a mob with an `IsGhost=true` character as its current target, when `GHOST_COMBAT_TTL` expires, then within `DE_TARGET_DEADLINE_MS` (250ms default), the mob's target clears and it enters idle/patrol.
- [ ] **AC-GH-9** [BLOCKING]: Given a ghost entity that received D damage during the ghost period and then expired via TTL without reconnect, when state is written at cleanup, then persisted HP equals the disconnect-moment HP (not `disconnectHP - D`).
- [ ] **AC-GH-17** [BLOCKING]: Given a ghost whose TTL expires in the same server frame as a zone sync tick, when both are processed, then the zone tick delivered to clients does NOT contain the expired ghost entity, and `GhostExpiredEvent` is delivered before or in the same network batch as the tick.

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

**Note**: `GHOST_COMBAT_TTL` constant — this story uses `networking-ghost-session.md`'s F-GH-1 formula (`GHOST_COMBAT_TTL_MIN_S` + OWL scaling, 30s baseline) as authoritative, per the cross-doc inconsistency flagged at epic creation time. Confirm with the user/design lead before final implementation if `networking-session.md`'s flat `GHOST_COMBAT_TTL_MINUTES` (60s) was intended instead.
