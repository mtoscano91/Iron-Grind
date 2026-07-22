# Story 017: Ghost Promotion & State Constraints

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-ghost-session.md` (+ `design/gdd/networking-ghost-character-state.md` for HP/IsGhost ownership)
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Ghost promotion messages (`GhostPromotionEvent`) are network-boundary R-OD broadcasts, governed by ADR-004's channel/envelope ownership, not ADR-010 (which explicitly excludes network-boundary messages).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None beyond the shared session-cluster concerns.
**Performance Notes**: No performance impact expected — the frozen-state check (AC-GH-2) is a simple flag guard evaluated per caller-driven tick call, not a scan; no new O(n) work added to the tick loop.

**Control Manifest Rules (Foundation layer)**:
- Required: `Connected` sessions in `Connected` state at heartbeat timeout produce a Ghost Entity; `Connecting`/`Disconnected_SessionExpired` sessions must NOT — source: CR-GH-1
- Required: `IsGhost` flag is server-owned, broadcast on zone sync, omitted from the wire when false — source: CGS-2
- Forbidden: client-side prediction/extrapolation/reconciliation of ghost HP — source: CGS-1

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-session.md` and `networking-ghost-character-state.md`, scoped to this story:*

- [x] **AC-GH-1** [BLOCKING]: Given a session in `Connected`, when the server receives no heartbeat for `HEARTBEAT_TIMEOUT_SECONDS`, then the session transitions to `Disconnected_SessionActive`, `IsGhost=true` broadcasts within one `ZONE_TICK_MS`, and `GhostPromotionEvent` (R-OD) emits to all zone clients. **Pass condition (restored from GDD, story-readiness fix):** `OnSessionStateTransitioned(accountId, Connected, Disconnected_SessionActive, "HeartbeatTimeout")` fires; `OnGhostPromotionEventEmitted(characterId)` fires within one `ZONE_TICK_MS` of the transition tick.
- [x] **AC-GH-2** [BLOCKING]: Given `IsGhost=true`, when the server processes 10 consecutive zone ticks, then the ghost's server-side position and attack queue are unchanged from the disconnect-moment values (frozen, not moving/attacking). **Pass condition (restored from GDD):** `IZoneTestConfigurator.GetEntityPosition(entityId)` returns identical values across all 10 ticks; attack-queue depth reported by the `OnTickCompleted` hook = 0 for each tick.
- [x] **AC-GH-3** [BLOCKING]: Given `IsGhost=true` and a mob attacking the ghost, when the mob executes an attack, then the ghost's HP reduces by the standard damage-pipeline-computed amount. **Pass condition:** `OnServerDamageEventSerialized(mobEntityId, ghostEntityId, computedDamage)` fires; ghost HP reported on next zone tick = prior HP minus `computedDamage` (within ±1 for integer rounding).
- [x] **AC-GH-13** [BLOCKING]: Given a client sending buffered action commands (movement, attack, skill) upon reconnecting after a ghost period, when the server receives those commands during re-authentication, then they are discarded without processing — no position change, attack, or skill activation occurs. **Pass condition (restored from GDD, story-readiness fix):** test technique is `ITransportFaultInjector` used to queue commands before transport reconnect completes; no `OnServerDamageEventSerialized`/`OnServerCycleTimerBroadcastSerialized` attributable to buffered commands fires during or before the `Reconnecting` phase.
- [x] **AC-GH-15** [BLOCKING]: Given a player whose auto-attack swing is in-flight when heartbeat timeout fires, when the server processes the disconnect, then the in-flight attack damage resolves normally, and no further attacks are queued after `IsGhost=true` is set. **Pass condition (restored from GDD, story-readiness fix):** `OnServerDamageEventSerialized` fires for the in-flight attack within the same tick or the next tick after `OnGhostPromotionEventEmitted`; no subsequent `OnServerDamageEventSerialized` fires for that ghost entity.
- [x] **AC-CGS-5** [BLOCKING] (`networking-ghost-character-state.md`): Given a ghost entity receiving damage, when the server updates ghost HP, then the update broadcasts via `EntityHealthUpdate` (R-U) on the next zone tick, and all zone clients apply the value directly without client-side prediction or blending. **Test-observability gap resolved before implementation (story-readiness fix):** the GDD's own pass condition wants to assert the exact `EntityHealthUpdate.HP` wire value, but `INetworkTestObserver.OnRUBatchEntityHealthUpdates(clientId, deliveredEntityIds)` only reports which entity IDs were included in a batch, never their HP payload — no callback exposes the HP value directly. Resolution: do NOT add a new observer callback for this. Derive expected HP indirectly, the same way AC-GH-3 already does: `OnServerDamageEventSerialized`'s `serverComputedDamage` plus a production-side HP query method this story builds anyway (whatever tracks ghost HP) — assert queried HP == prior HP − computedDamage, and separately assert no client-prediction hook/no predicted-HP path exists for a ghost entity (a negative assertion: this story's own HP-update code path has no branch that special-cases prediction/blending for `IsGhost=true` entities).
- [x] **AC-GH-19** [BLOCKING]: Given a character entity with `IsGhost=false`, when zone sync serializes, then the `IsGhost` field is absent from that entity's zone sync entry (omitted, not sent as `false`).

---

## Implementation Notes

*Derived from CR-GH-1 through CR-GH-5, CGS-1, CGS-2:*

- Promotion sequence (CR-GH-2), strict order: (1) transition `Connected→Disconnected_SessionActive`, take pre-disconnect snapshot (full detail is Story 018's job — this story only needs the transition trigger and freeze); (2) emit `GhostPromotionEvent` (R-OD, S→ALL); (3) freeze entity — clear queued movement/attack commands, entity MUST NOT move/attack for the rest of the ghost period; (4) broadcast `IsGhost=true` on the next zone sync tick (within one `ZONE_TICK_MS`); (5) start `GHOST_COMBAT_TTL` countdown (the countdown mechanism itself, using the F-GH-1 formula, is Story 019/021's concern — this story only starts a timer).
- CR-GH-4: while `IsGhost=true`, reject all client-originated action commands; buffered commands from the disconnected period sent on reconnect are discarded without processing (AC-GH-13).
- CR-GH-5/CGS-1/CGS-5: ghost HP authority is server-only — damage applies via the standard pipeline, broadcasts via `EntityHealthUpdate` (R-U), zero client prediction ever applied to a ghost entity (EC-GH-8 reinforces this for client-side prediction systems generally).
- CGS-2: `IsGhost` is omitted from the wire entirely when false (bandwidth optimization, not just a `false` value sent) — implement the serializer's conditional field inclusion accordingly.
- EC-GH-3/AC-GH-15: in-flight attacks (already committed, damage not yet resolved) complete normally — the server never rolls back a committed attack event on disconnect.

---

## Out of Scope

*Handled by neighbouring stories:*

- Pre-disconnect snapshot content and WAL write-ordering — Story 018
- Ghost death and mob de-targeting — Story 019
- XP/reward forfeit policy and party slot retention — Story 020
- Cleanup sequence, zone crash, voluntary dismissal — Story 021
- `GHOST_COMBAT_TTL` computation itself (F-GH-1 formula) — Story 019/021 (this story only starts/stops a generic timer)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs`

- **AC-GH-1**: Given heartbeat timeout at `Connected`, then promotion sequence fires as described.
- **AC-GH-2**: Given 10 ticks as ghost, then position/attack-queue unchanged.
- **AC-GH-3**: Given a mob attack on a ghost, then HP reduces via the standard pipeline.
- **AC-GH-13**: Given buffered commands during reconnect re-auth, then discarded, no state change.
- **AC-GH-15**: Given an in-flight attack at disconnect boundary, then it resolves normally, no further attacks queued.
- **AC-CGS-5**: Given ghost damage, then `EntityHealthUpdate` broadcasts next tick, no client prediction applied.
- **AC-GH-19**: Given `IsGhost=false`, then the field is absent from serialized output (byte-count assertion).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs` — must exist and pass

**Status**: [x] Created — 17 test cases, all 7 blocking ACs COVERED with traceability

---

## Dependencies

- Depends on: Story 012 (heartbeat-timeout trigger), Story 007 (EntityHealthUpdate batch delivery)
- Unlocks: Stories 018–021 (rest of the Ghost Session cluster build on this promotion mechanism)

---

## Completion Notes
**Completed**: 2026-07-18
**Criteria**: 7/7 passing (AC-GH-1, AC-GH-2, AC-GH-3, AC-GH-13, AC-GH-15, AC-CGS-5, AC-GH-19)
**Deviations**: ADVISORY — TR-net-006 not in `docs/architecture/tr-registry.yaml` (systemic, pre-existing gap). ADVISORY — TD-018 logged: AC-GH-2's freeze test is honestly tautological given no Movement/Combat system exists yet to actually attempt moving a ghost — needs a companion test once one does. ADVISORY — AC-GH-13's GDD pass-condition text names `ITransportFaultInjector` as the test technique, but that interface is confirmed outbound-fault-injection only; resolved via the new `GhostEntityTracker.ShouldRejectCommand` method instead, documented honestly in code rather than silently reinterpreted.
**Test Evidence**: Logic: `tests/EditMode/Networking/GhostSession_PromotionStateConstraints_tests.cs` (17 test cases)
**Code Review**: Complete — `/code-review` (lean mode, unity-specialist + qa-tester parallel): APPROVED WITH SUGGESTIONS. unity-specialist: CLEAN, independently confirmed `ApplyDamage` never reads `IsGhost` (the CGS-1 structural proof). qa-tester: GAPS — found a missing `ShouldRejectCommand` negative-path test and (independently, matching unity-specialist's own finding) dead `TransportFaultInjector` scaffolding in the AC-GH-13 test; also correctly pushed back on the AC-CGS-5 negative test's single-sample framing. All 4 suggestions fixed: added the missing test, removed dead code, strengthened the AC-CGS-5 test to 4 input vectors with reframed doc comment, logged TD-018. Final test count: 17 (16 + 1 new).

**First story in the Ghost Session cluster (017-021) — establishes the `GhostEntityTracker` foundation Stories 018-021 will extend.**

**Note**: `GHOST_COMBAT_TTL` has a cross-doc constant inconsistency (`GHOST_COMBAT_TTL_MINUTES` in `networking-session.md` vs. `GHOST_COMBAT_TTL_MIN_S`/F-GH-1 formula in `networking-ghost-session.md`) — this story only starts/stops a timer and does not compute its duration, so it is unaffected; Story 019/021 must resolve which constant is authoritative before implementing the actual duration.
