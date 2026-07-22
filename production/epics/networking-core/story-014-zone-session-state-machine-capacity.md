# Story 014: Zone Session State Machine & Capacity Enforcement

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-session.md` (AC-NC-14, AC-NC-24, AC-NC-41 — corrected below) plus `design/gdd/networking-core.md` (AC-NC-22, "Zone Capacity" section — this story's Context originally cited only `networking-session.md` for all 4 ACs; AC-NC-22 is actually defined in `networking-core.md`, wording matches verbatim, source-only correction)
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR as Story 012/013 — zone-level session bookkeeping sits alongside the per-player connection state machine, both driven by the same tick loop's connection-driven category.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None beyond the shared tick-loop dependency.

**Control Manifest Rules (Foundation layer)**:
- Required: `ST-NET-2` four states (`Empty`, `Active`, `Draining`, `Closed`) — source: `networking-session.md`
- Required: zone capacity (10-50 players) enforced at `Empty→Active` and every join; ghost sessions count toward capacity — source: `networking-session.md`, EC-NET-3

---

## Acceptance Criteria

*From `design/gdd/networking-session.md`, scoped to this story:*

- [x] **AC-NC-14** [BLOCKING]: Given a zone with one connected player who disconnects, when the player enters `Disconnected_SessionActive`, then the zone transitions to `Draining` (tick loop continues) — not `Closed`.
- [x] **AC-NC-22** [BLOCKING] (source: `design/gdd/networking-core.md`, "Zone Capacity" section — not `networking-session.md`): Given a zone at capacity (50 players), when a 51st player attempts to join, then the server returns an overflow response, the 51st player is not added, and all 50 existing sessions are unaffected.
- [x] **AC-NC-24** [BLOCKING]: Given a zone with 49 connected players and 1 player in `Disconnected_SessionActive`, when a new player attempts to join, then the server returns an overflow response — the ghost session counts toward the 50-player cap; the disconnected player's slot is not relinquished until TTL expires.
- [x] **AC-NC-41** [BLOCKING] (Logic — Active→Closed via explicit disconnect, last session; fuller text below, corrected against the GDD's current `networking-session.md` lines 368-369 — the original story text omitted the last two clauses): Given a zone with exactly 1 connected player and no `Disconnected_SessionActive` sessions, when the player sends an explicit disconnect, then: `OnZoneStateTransitioned(zoneId, Active, Closed)` fires — no intermediate `Draining` state is entered; `OnPersistenceWriteCompleted(characterId, ExplicitDisconnect)` fires; `IZoneTestConfigurator.GetCurrentZoneState(zoneId)` returns `Closed` on the same tick boundary.

---

## Implementation Notes

*Derived from ST-NET-2:*

| From | To | Trigger |
|---|---|---|
| `Empty` | `Active` | First player enters `Connected` |
| `Active` | `Draining` | Last remaining player enters `Disconnected_SessionActive` |
| `Active` | `Active` | Any connect/reconnect/disconnect while others remain |
| `Active` | `Draining` | Last `Connected` player explicitly disconnects while ≥1 ghost remains |
| `Active` | `Closed` | Last session of any kind enters `Disconnected_SessionExpired` via explicit disconnect (zero sessions remain) — **no intermediate Draining** |
| `Draining` | `Active` | A player reconnects or a new player enters |
| `Draining` | `Draining` | Re-auth fails for a `Reconnecting` session, returns to ghost — no zone-level change |
| `Draining` | `Closed` | All remaining TTLs expire |
| `Closed` | `Empty` | Zone re-allocated for new session |

- Zone capacity counts `Connected` + `Reconnecting` + `Disconnected_SessionActive` toward the cap — a zone is never "full" due to its own ghost sessions, but ghost sessions do occupy a slot against new outside joiners (EC-NET-3).
- Zone teardown enforcement: on transition toward `Closed`, emit `ZoneSessionEnded` (R-OD) to all transport-connected clients with `gracePeriodSeconds = ZONE_CLOSE_GRACE_PERIOD_SECONDS`; clients still connected at countdown expiry are forcibly disconnected into `Disconnected_SessionExpired` (no resume). Ghost sessions with no transport connection can't receive `ZoneSessionEnded` — at grace-period expiry they transition directly to `Disconnected_SessionExpired` (5-min TTL aborted), treated as normal TTL expiry (EC-NET-1 death rules apply).

---

## Out of Scope

*Handled by neighbouring stories:*

- Player connection state machine (ST-NET-1) — Stories 012–013
- TTL expiry sequence detail and zone crash — Story 015

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs`

- **AC-NC-14**: Given the last player disconnecting (becoming ghost), then zone → `Draining`.
- **AC-NC-22**: Given 50 players, a 51st join attempt is rejected with an overflow response.
- **AC-NC-24**: Given 49 connected + 1 ghost, a new join attempt is rejected (ghost counts toward cap).
- **AC-NC-41**: Given the last (only) player explicitly disconnecting, zone → `Closed` directly, no `Draining` observed.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs` — must exist and pass

**Status**: [x] Created — 36 test methods, all 4 blocking ACs COVERED with traceability

---

## Dependencies

- Depends on: Story 012 (player state machine this zone machine composes with)
- Unlocks: Story 015 (zone-level TTL/crash interactions), Story 021 (ghost cleanup's zone-crash handling)

---

## Completion Notes
**Completed**: 2026-07-18
**Criteria**: 4/4 passing (AC-NC-14, AC-NC-22, AC-NC-24, AC-NC-41)
**Deviations**: ADVISORY — TR-net-006 not found in `docs/architecture/tr-registry.yaml` (pre-existing systemic registry gap, same as every prior story; GDD text used directly as source of truth). ADVISORY — this story's own AC-NC-22 GDD-source attribution was wrong (corrected above; content matched verbatim, only the source doc was mislabeled) and AC-NC-41's text was a truncated subset of the GDD's current text (corrected above; implementation and tests were built against the fuller version).
**Test Evidence**: Logic: `tests/EditMode/Networking/Session_ZoneStateMachine_Capacity_tests.cs` (36 test methods)
**Code Review**: Complete — `/code-review` (lean mode, unity-specialist + qa-tester parallel): APPROVED WITH SUGGESTIONS. unity-specialist: CLEAN. qa-tester found the AC-NC-41 `IZoneTestConfigurator` cross-check test was tautological (hardcoded the expected value independent of the real state machine's result) — fixed to mirror the real `TryGetZoneState` result instead, with a doc comment scoping what it does/doesn't prove. Both specialists independently flagged the same `EvaluatePlayerCountChange` zero-occupied-slots guard asymmetry (only threw on the `Active` branch, not `Draining`) — fixed to be symmetric across both states. 3 more coverage gaps closed: a 2-cycle Active↔Draining regression test, 2 missing wrong-state guard tests for `CompleteExplicitDisconnectTeardown`, and a `CompleteTTLExpiryTeardown` empty-list success-path test. Final test count: 36 (31 + 5 new).
