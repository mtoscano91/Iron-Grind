# Story 006: Leash, Target Invalidation and Returning

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — CR-AI-9, CR-AI-10, CR-AI-11 (Returning part), CR-AI-14, F-AI-4, EC-AI-2, EC-AI-5, AC-AI-9, AC-AI-10, AC-AI-11, AC-AI-12b, AC-AI-23, AC-AI-26
**Requirement**: `TR-ai-006`, `TR-ai-007`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-003: Nav Agent Lifecycle State Machine (Decision 1, Returning speed cap)
**ADR Decision Summary**: Returning entry: `SetSpeed(RETURN_SPEED_CAP)` then `SetDestination(spawn)`. Returning → Dormant: `Stop` then `SetSpeed(MoveSpeed)`. Returning → Pursuing (re-aggro): `SetSpeed(MoveSpeed)` then `SetDestination(target)`.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH (ADR-003 consumer; fake provider in tests)
**Engine Notes**: `RETURN_SPEED_CAP` = 6.0 m/s (ADR-003; Story 002 config).

**Control Manifest Rules (this layer)**:
- Required: the three Returning call sequences above — ADR-003
- Forbidden: `Resume` from Dormant re-aggro — ADR-003

---

## Acceptance Criteria

- [ ] **Leash (CR-AI-9, F-AI-4)**: checked every tick in Pursuing, WindingUp, AttackExecution, Recovering, Returning; XZ squared distance `> LeashRange²`. Pursuing / Recovering → Returning immediately; WindingUp / AttackExecution → `pendingReturn = true`, cycle continues. *(Returning listed by the GDD: a leash breach while Returning keeps Returning — no re-entry.)*
- [ ] **AC-AI-9**: WindingUp with 3 left, mob moved to `LeashRange + 0.1` → stays WindingUp, `pendingReturn` set, AttackExecution fires, then Recovering, then Returning (not Pursuing / WindingUp).
- [ ] **Target died (CR-AI-10)**: outside the cycle → immediate re-scan of players within `AggroRange` (HP > 0): found → new target, same state; none → standard invalidation. Inside the cycle → `pendingReturn`, finish the cycle, re-scan-or-return on Recovering exit.
- [ ] **AC-AI-10 / AC-AI-12b**: target HP 0 during WindingUp → stays, `pendingReturn` set, AttackExecution skips `CheckHit`, Recovering, then re-scan: new target in range → `TargetEntityID` updated (state per Recovering exit rules); none → Returning.
- [ ] **AC-AI-26**: Pursuing on A, B in range, A HP 0 → next tick still Pursuing, target B, `SetDestination` toward B.
- [ ] **Standard invalidation** (target gone from the zone, or target outside the mob's `LeashRange` from spawn): Dormant / Pursuing / Recovering / Returning → Returning, target cleared; WindingUp / AttackExecution → `pendingReturn`, `CheckHit` skipped on execution. **AC-AI-23**: target removed while Pursuing → Returning next tick, target cleared.
- [ ] **Returning entry**: `SetSpeed(RETURN_SPEED_CAP)` then `SetDestination(spawn)` once; target cleared.
- [ ] **AC-AI-11 / EC-AI-5**: within `RETURN_ARRIVAL_THRESHOLD` (XZ squared) → Dormant, `Stop` then `SetSpeed(MoveSpeed)`, target unset, `pendingReturn` false; a mob already at spawn arrives on its first Returning tick.
- [ ] Returning mobs do not aggro (GDD has no Returning → Pursuing transition); the ADR-003 Returning → Pursuing sequence is therefore unused — record as a note, not code.

---

## Implementation Notes

- "Disconnected" in the GDD = the target entity is no longer in the zone's player list (slot released); a ghost-session player still in the zone remains a valid target (Ghost Session GDD) — confirm at readiness.

---

## Out of Scope

- Story 011: mob death

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_LeashInvalidation_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Unlocks: Story 009
