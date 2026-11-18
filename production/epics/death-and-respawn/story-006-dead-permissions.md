# Story 006: Dead Player Permissions

> **Epic**: Death & Respawn
> **Status**: Ready
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

- [ ] **AC-1 Mob targeting (AC-DR-4)**: a dead player is not a valid mob target: `MobTargetValidity` (HP > 0 today) rejects them while Dead, and Enemy AI aggro scans skip them. Test with the real Character Stats; if HP alone is enough, no code change, only the test.
- [ ] **AC-2 Player targeting (AC-DR-4)**: a `SetTarget` at a dead player (or a dead player's auto-attack) does not resolve damage against them. PvE scope: confirm at readiness whether players can target players at all; if not, record that and test only the mob path.
- [ ] **AC-3 Skills (AC-DR-NEW-5)**: a skill cast by a dead player is rejected with no effect applied (existing `CharacterStatsSkillCasterAliveSource`, HP > 0); combat stays prohibited.
- [ ] **AC-4 Movement**: movement intents from a dead player move nothing (Movement `SetProhibited` from Story 002); position unchanged across 10 ticks of intents.
- [ ] **AC-5 No early respawn (AC-DR-17, CR-DR-17)**: there is no public way to respawn before `RespawnTick`: the service exposes no respawn-now method, and no inbound handler exists for a `RespawnNow` request (none is defined in the wire protocol). Test: a dead player receives N inbound messages of every registered type it may send; respawn still happens on exactly `RespawnTick`.
- [ ] **AC-6 Allowed while dead (CR-DR-15)**: chat and read-only panel requests are not blocked by this system (no code here touches them); recorded in the story notes only.

---

## Implementation Notes

- Prefer tests over new code: HP is 0 while Dead, and several systems already gate on HP > 0. Add a Dead check only where HP is not enough (e.g. the single Respawning tick, where HP is restored before combat is re-enabled — confirm order makes this moot).
- AC-DR-NEW-6 (voluntary zone change rejected) is out of this epic: no voluntary zone-change request exists in code. Note it in Completion Notes for the epic that builds zone travel.

---

## Out of Scope

- AC-DR-NEW-6 (moved, see notes)
- Chat (Party Chat epic)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1**: Mob in aggro range of dead P only → stays Idle / does not select P; after P respawns in range → may select P.
- **AC-3**: Dead P casts skill S → rejected; target HP unchanged; no status effect applied.
- **AC-4**: Dead P sends forward intents for 10 ticks → position equal to death position.
- **AC-5**: Respawn tick unchanged after a flood of inbound requests from P.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_DeadPermissions_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 009
