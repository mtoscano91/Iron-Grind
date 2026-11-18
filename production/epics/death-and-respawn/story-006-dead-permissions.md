# Story 006: Dead Player Permissions

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 2–3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-15, CR-DR-16, CR-DR-17, States table (Targetable / Can Move)
**Requirement**: `TR-dr-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Client requests reach a system through one handler per request type; the system owns the reply. Rejections happen in the owning system, not in the dispatcher.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Forbidden: a request queue inside a game system (ADR-014)

---

## Acceptance Criteria

- [x] **AC-1 Mob targeting (AC-DR-4)**: a dead player is never selected or kept as a mob target. Covered by existing checks, verified with the real Character Stats, `ZoneSlotPool` and `DeathAndRespawnService` (player killed through T-1, so HP 0 and slot `Dead`):
  - aggro scan: `ZoneAggroCandidateSource` offers only `Connected` slots, and `MobController`'s scan skips HP ≤ 0 (`MobController.cs:318`), so a mob with only dead P in aggro range stays Idle;
  - chase: a mob already targeting P gets `TargetCheck.Dead` once P dies (`MobController.cs:482`) and drops P;
  - after P respawns (T-3) inside aggro range, the mob may select P again.
  Test-only unless a check fails; `MobTargetValidity` is not involved (it is the player auto-attack validity, mobs only).
- [x] **AC-2 Player targeting (AC-DR-4)**: PvE only, confirmed at readiness 2026-11-18: players cannot target players (`MobTargetValidity` rejects any non-mob ID), so "dead player as a player's target" cannot occur; recorded, not tested. Tested instead:
  - a dead P's `SetTarget` at a mob plus `SetAutoAttack` on, over 10 ticks → mob HP unchanged, no damage event (combat prohibited, Story 002);
  - a mob attack resolved against P after P died (`MobAttackResolver`, HP 0) → no `ApplyDamage`, no damage event (`MobAttackResolver.cs:83`).
- [x] **AC-3 Skills (AC-DR-NEW-5)**: a skill cast by a dead player is rejected with no effect applied (existing `CharacterStatsSkillCasterAliveSource`, HP > 0); target HP unchanged, no status effect applied; `AutoAttackState` for P stays `Prohibited`.
- [x] **AC-4 Movement**: movement intents from a dead player move nothing (Movement `SetProhibited` from Story 002); position unchanged across 10 ticks of intents.
- [x] **AC-5 No early respawn (AC-DR-17, CR-DR-17)**: no way to respawn before `RespawnTick`. Three tests:
  - (a) API: reflection over `DeathAndRespawnService`'s public instance methods equals exactly {`Tick`, `TryGetDeathState`, `IsDead`, `Dispose`} (plus `object` members); a new public method fails the test and forces a review against CR-DR-17;
  - (b) wire: `MessageRoutingRegistry` has no `ClientToServer` entry in the Death & Respawn range 0xE160–0xE16F, so a `RespawnNow` request has no type and no handler (AC-DR-17's "silently dropped" holds by absence);
  - (c) flood: with P dead, every tick until `RespawnTick` call the real `SetTargetRequestHandler`, `SetAutoAttackRequestHandler` and `SkillCastRequestHandler` paths for P (unit level; the dispatcher is wired in Story 008) → `DeathState` unchanged and respawn (T-3) happens on exactly `RespawnTick`, not earlier.

---

## Implementation Notes

- Prefer tests over new code: HP is 0 while Dead, and several systems already gate on HP > 0. Add a Dead check only where a test shows HP is not enough. No Respawning-tick gap exists: Respawning is set and cleared inside one `Tick` call (`DeathAndRespawnService.cs:251` → `280`/`283`), so nothing outside the service observes it.
- AC-DR-NEW-6 (voluntary zone change rejected) is out of this epic: no voluntary zone-change request exists in code. Note it in Completion Notes for the epic that builds zone travel.
- **Allowed while dead (CR-DR-15)**, note only, not tested: chat, party HP bars, read-only inventory/stat panels and party invite accept/decline are not blocked by this system; no code here touches them.
- **Performance**: no performance impact expected — tests plus at most a guard check; no new per-tick work.

---

## Out of Scope

- AC-DR-NEW-6 (moved, see notes)
- Chat (Party Chat epic)
- CR-DR-16 "cannot initiate a party invite" → Party System epic (party ACs live there, seam in this epic)
- CR-DR-15 allowed actions (recorded in notes, not tested)
- PvP targeting (players cannot target players at MVP)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refined at `/story-readiness` 2026-11-18.*

- **AC-1**: Mob in aggro range of dead P only → stays Idle / does not select P; mob chasing P when P dies → drops P; after P respawns in range → may select P.
- **AC-2**: Dead P targets mob M with auto-attack on for 10 ticks → M HP unchanged, no damage event; mob attack resolved on dead P → no damage, no event.
- **AC-3**: Dead P casts skill S → rejected; target HP unchanged; no status effect applied; P auto-attack state Prohibited.
- **AC-4**: Dead P sends forward intents for 10 ticks → position equal to death position.
- **AC-5**: (a) public method set exact; (b) no ClientToServer type in 0xE160–0xE16F; (c) respawn tick unchanged after per-tick SetTarget / SetAutoAttack / SkillCast from P.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeadPermissions_tests.cs` — must exist and pass

**Status**: [x] Created 2026-11-18 — 14 tests. AC-2 and AC-3 proof is the fake counters (`FakeDamageApplier.Count`, the validated cast sink count): those fakes never write Character Stats, so target HP is not asserted. "No status effect" (AC-3) is proven by the validated cast sink seeing zero casts, since execution starts there; the rejection code is `CasterNotAlive`.

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 009

---

## Completion Notes
**Completed**: 2026-11-18
**Criteria**: 5/5 passing (all automated; none deferred)
**Deviations** (advisory, none logged as tech debt):
- No production code: the existing HP > 0, Connected-slot and combat/movement prohibition checks already enforce every AC.
- The story says the mob stays "Idle"; the code's state is `MobState.Dormant`, which the tests assert.
- Request handlers are called directly with an `InboundRequestContext`; dispatcher wiring is Story 008.
- AC-2/AC-3 proof is fake counters (see Test Evidence); target HP is not asserted.
- AC-DR-NEW-6 (voluntary zone change rejected) not implemented here: no zone-change request exists. Carry it to the epic that builds zone travel.
- CR-DR-15 allowed actions recorded only (notes); CR-DR-16 party invite → Party System epic.
**Test Evidence**: Logic — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeadPermissions_tests.cs` (14 tests); filter DeathAndRespawn 167/167; full EditMode `-testCategory "!Database"` 8006/8006 (2026-11-18 14:18:29Z)
**Code Review**: Complete (`/code-review` 2026-11-18, all required fixes + suggestions applied)
