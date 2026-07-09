# Story 017: Ghost Promotion & State Constraints

> **Epic**: Networking Core
> **Status**: Ready
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

**Control Manifest Rules (Foundation layer)**:
- Required: `Connected` sessions in `Connected` state at heartbeat timeout produce a Ghost Entity; `Connecting`/`Disconnected_SessionExpired` sessions must NOT — source: CR-GH-1
- Required: `IsGhost` flag is server-owned, broadcast on zone sync, omitted from the wire when false — source: CGS-2
- Forbidden: client-side prediction/extrapolation/reconciliation of ghost HP — source: CGS-1

---

## Acceptance Criteria

*From `design/gdd/networking-ghost-session.md` and `networking-ghost-character-state.md`, scoped to this story:*

- [ ] **AC-GH-1** [BLOCKING]: Given a session in `Connected`, when the server receives no heartbeat for `HEARTBEAT_TIMEOUT_SECONDS`, then the session transitions to `Disconnected_SessionActive`, `IsGhost=true` broadcasts within one `ZONE_TICK_MS`, and `GhostPromotionEvent` (R-OD) emits to all zone clients.
- [ ] **AC-GH-2** [BLOCKING]: Given `IsGhost=true`, when the server processes 10 consecutive zone ticks, then the ghost's server-side position and attack queue are unchanged from the disconnect-moment values (frozen, not moving/attacking).
- [ ] **AC-GH-3** [BLOCKING]: Given `IsGhost=true` and a mob attacking the ghost, when the mob executes an attack, then the ghost's HP reduces by the standard damage-pipeline-computed amount.
- [ ] **AC-GH-13** [BLOCKING]: Given a client sending buffered action commands (movement, attack, skill) upon reconnecting after a ghost period, when the server receives those commands during re-authentication, then they are discarded without processing — no position change, attack, or skill activation occurs.
- [ ] **AC-GH-15** [BLOCKING]: Given a player whose auto-attack swing is in-flight when heartbeat timeout fires, when the server processes the disconnect, then the in-flight attack damage resolves normally, and no further attacks are queued after `IsGhost=true` is set.
- [ ] **AC-CGS-5** [BLOCKING] (`networking-ghost-character-state.md`): Given a ghost entity receiving damage, when the server updates ghost HP, then the update broadcasts via `EntityHealthUpdate` (R-U) on the next zone tick, and all zone clients apply the value directly without client-side prediction or blending.
- [ ] **AC-GH-19** [BLOCKING]: Given a character entity with `IsGhost=false`, when zone sync serializes, then the `IsGhost` field is absent from that entity's zone sync entry (omitted, not sent as `false`).

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 012 (heartbeat-timeout trigger), Story 007 (EntityHealthUpdate batch delivery)
- Unlocks: Stories 018–021 (rest of the Ghost Session cluster build on this promotion mechanism)

**Note**: `GHOST_COMBAT_TTL` has a cross-doc constant inconsistency (`GHOST_COMBAT_TTL_MINUTES` in `networking-session.md` vs. `GHOST_COMBAT_TTL_MIN_S`/F-GH-1 formula in `networking-ghost-session.md`) — this story only starts/stops a timer and does not compute its duration, so it is unaffected; Story 019/021 must resolve which constant is authoritative before implementing the actual duration.
