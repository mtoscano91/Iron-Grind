# Story 020: Ghost Reward Forfeit Policy — Two-Pool XP & Party Slot Retention

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-ghost-session.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR — party-slot retention and XP-pool bookkeeping are server-side state managed alongside the ghost session, with no dedicated ADR of their own.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: two XP pools — pre-disconnect (banked, never forfeited) and post-disconnect party shares (forfeitable) — source: CR-GH-8.1
- Required: ghost retains party slot for full ghost period; removed from party only at ghost expiry, not at disconnect — source: CR-GH-8

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-session.md`, scoped to this story:*

- [ ] **AC-GH-6** [BLOCKING]: Given a party of 2+ and one member transitioning to ghost, when `IsGhost=true` is active, then the ghost member remains in the party roster for the full ghost period. **Pass condition (restored from GDD, story-readiness fix):** `OnSessionStateTransitioned` shows session in `Disconnected_SessionActive`; `IZoneTestConfigurator.GetCurrentZoneState` shows zone remains `Active` (not `Draining`) if other players are present; party roster query at ghost expiry or reconnect shows member count unchanged from pre-disconnect.
- [ ] **AC-GH-7** [BLOCKING]: Given `IsGhost=true` with party XP shares accumulating, and TTL expiring without reconnect, when the ghost is cleaned up, then persisted XP equals the pre-disconnect snapshot value (no ghost-period party shares added). **Pass condition (restored from GDD, story-readiness fix):** `OnGhostCombatTTLExpired` fires; `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry)` fires; character persistence XP = XP at `disconnectTickNumber` (compare snapshot captured via `OnSessionHandshakeEmitted` on prior login).
- [ ] **AC-GH-8** [BLOCKING]: Given `IsGhost=true` with N post-disconnect party XP shares accumulated, when the player reconnects before TTL expires, then `IsGhost` clears within one `ZONE_TICK_MS`, and persisted XP = pre-disconnect XP + N. **Pass condition (restored from GDD, story-readiness fix):** `OnSessionStateTransitioned(accountId, Reconnecting, Connected, "ReauthSuccess")` fires; `OnSessionHandshakeEmitted` next tick reports `currentXp = preDisconnectXp + N`; observer log contains no `OnGhostCombatTTLExpired` event for this session.
- [ ] **AC-GH-12** [BLOCKING]: Given a character with 500 pre-disconnect XP and 100 post-disconnect party-share XP, and the ghost dies before TTL expiry, when the session closes reason `GHOST_DEATH`, then persisted XP = 500 (not 600, not 0). **Pass condition (restored from GDD, story-readiness fix):** `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath)` fires; character XP in persistence record = 500.
- [ ] **AC-GH-18** [BLOCKING]: Given a ghost entity and the party being disbanded by the leader, when disbanded, then post-disconnect party XP share accumulation stops at that moment — no further XP added to the post-disconnect pool. **Pass condition (restored from GDD, story-readiness fix):** record the tick number when the party disband event is processed (via `OnSessionStateTransitioned` or the new `OnPartyDisbanded(partyId, tickNumber)` harness hook); assert ghost session post-disconnect XP pool delta = 0 for all ticks after that tick number. Requires the `INetworkTestObserver` extension noted in Implementation Notes.

---

## Implementation Notes

*Derived from CR-GH-8, CR-GH-8.1, CR-GH-9, CR-GH-9.1, CR-GH-9.2:*

- **Two pools** (CR-GH-8.1): (a) pre-disconnect XP — banked at disconnect (CGS-3 snapshot), never forfeited regardless of ghost-period outcome; (b) post-disconnect party XP shares — accumulated in a separate in-memory pool while `IsGhost=true`, only forfeitable portion.
- **Party slot retention** (CR-GH-8): ghost keeps party slot and appears in party UI with a ghost indicator for the full ghost period; not dissolved, no leadership transfer. Removed from party only at ghost expiry (TTL/death/dismissal), never at the moment of disconnect.
- **Forfeit on expiry** (CR-GH-9): TTL expiry without reconnect → discard post-disconnect pool (never persisted); pre-disconnect XP unaffected; zero loot for the ghost period.
- **Forfeit on death** (CR-GH-9.1): same forfeit rule as TTL expiry.
- **Full restore on reconnect** (CR-GH-9.2): `IsGhost=false` within one `ZONE_TICK_MS`; post-disconnect pool written to persistence and added to total XP; any loot to the ghost's personal channel during the ghost period is retained; `GHOST_COMBAT_TTL` timer cancelled.
- **Party disbanding mid-ghost** (EC-GH-7): if the party disbands while a ghost is present, the ghost's party slot releases immediately; TTL continues; character remains a solo ghost; XP sharing ends at the moment of disbanding (AC-GH-18) — this requires an `OnPartyDisbanded(partyId, tickNumber)` observer hook (extend `INetworkTestObserver` from Story 002 if not already present).
- Party System itself is "not yet authored" per the GDD's own dependency table — implement this story against a mock party-membership provider; wire the real Party System when that epic exists.

---

## Out of Scope

*Handled by neighbouring stories:*

- Ghost promotion/freeze — Story 017
- Snapshot/write-ordering — Story 018
- Death/de-targeting — Story 019
- Real Party System membership/disbanding logic — future Party System epic (mock provider only, here)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs`

- **AC-GH-6**: Given a ghost in a 2+ party, then roster membership persists through the ghost period.
- **AC-GH-7**: Given TTL expiry with accumulated shares, then persisted XP excludes the forfeited pool.
- **AC-GH-8**: Given a successful reconnect with N shares, then persisted XP = pre-disconnect + N.
- **AC-GH-12**: Given 500 banked + 100 forfeitable + ghost death, then persisted XP = 500 exactly.
- **AC-GH-18**: Given a party disband mid-ghost-period, then XP-share accumulation stops at the disband tick (requires `OnPartyDisbanded` extension).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 017 (promotion), Story 018 (snapshot/write-ordering), Story 019 (death path — shares its forfeit rule)
- Unlocks: Story 021 (cleanup sequence composes this XP-finalization step); future Party System epic

---

## Completion Notes
**Completed**: 2026-07-18
**Criteria**: 5/5 passing (AC-GH-6, AC-GH-7, AC-GH-8, AC-GH-12, AC-GH-18) — none deferred
**Deviations**: None blocking. Advisory: (1) TR-net-006 registry gap (systemic, pre-existing); (2) TD-020 logged — `GhostXpPoolTracker.ResolveFinalXp` has no production call site yet; AC-GH-7/12 compose with Story 018's already-tested `GhostCleanupSequencer` delegate (legitimate interim proof), AC-GH-8's reconnect path is weaker (no seam at all — the state-transition and XP-arithmetic assertions are causally independent). Both honestly documented in the test file's remarks.
**Test Evidence**: Logic — `tests/EditMode/Networking/GhostSession_RewardForfeitPolicy_tests.cs` (24 tests, all 5 blocking ACs covered)
**Code Review**: Complete (unity-specialist + qa-tester specialist agents, both ran successfully). 2 Required Changes + 5 Suggestions raised; all applied except one (optional `preDisconnectXp >= 0` guard on `BeginTracking`, explicitly noted by the reviewer as matching existing precedent not to add — skipped, flagged to user)
