# Story 003: Position Integration and Effective Speed

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (4 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — CR-MOV-4, F-MOV-1, F-MOV-2, EC-MOV-10, EC-MOV-19; AC-MOV-7, 8, 9, 10
**Requirement**: `TR-mov-001`

**ADR Governing Implementation**: ADR-002 Decision 1 (position integration is the last simulation phase); ADR-012
**ADR Decision Summary**: positions are integrated once per tick with the fixed tick delta, after systems have read nav/combat state.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `Vector3` math only.

**Control Manifest Rules (this layer)**:
- Required: fixed `dt = ServerTickLoop.FIXED_DELTA_TIME` (0.050), never wall-clock (CR-NET-2)
- Required: speed read through an injected seam (DI), not a `CharacterStats` singleton
- Guardrail: no allocation per call (20 Hz × 50 players)

---

## Acceptance Criteria

- [ ] A pure integrator `MovementIntegrator.Integrate(Vector3 oldPos, Vector3 inputDir, float vEff, float dt)` returns `oldPos + normalize(inputDir) × vEff × dt`, with Y kept at `oldPos.y` (ground-plane movement; Y comes from the NavMesh in Story 005).
- [ ] **Zero-input guard**: `inputDir == Vector3.zero` (both XZ components exactly 0) → returns `oldPos` without normalizing.
- [ ] A speed seam `IMovementSpeedSource.TryGetMovementSpeed(EntityID, out float)` with a `CharacterStats` adapter over **`GetEffectiveStatFloat(entity, StatID.MovementSpeed)`** (`CharacterStats.cs:373`) — **not** `GetEffectiveStat`, which returns an int via `FloorToInt` (0.5 → 0, 9.6 → 9); read every tick, never cached across ticks; no second clamp (F-MOV-2 — Character Stats clamps to [0.5, 20.0]).
- [ ] **Unknown entity**: `GetEffectiveStatFloat` does not throw for an entity `CharacterStats` does not know — `GetBaseStatFloat` returns `0f` and, with no modifiers, the method returns `0f`. The adapter passes `0f` through; the range check below rejects it as a failed read.
- [ ] **EC-MOV-19 — `MovementSpeedResolver`**: a static `MovementSpeedResolver.TryResolve(IMovementSpeedSource source, EntityID id, ref float lastValidSpeed, out float vEff)` sits between the seam and the integrator (`MovementIntegrator.Integrate` stays pure, no logging, no state):
  - read succeeds, value finite and in [0.5, 20.0] → `vEff` = value, `lastValidSpeed` = value, returns `true`;
  - read fails (`TryGetMovementSpeed` false, exception thrown by the source — caught here, NaN/±Inf, or outside [0.5, 20.0] including the unknown-entity `0f`) → logs **one `Debug.LogError` per failing call** (prefix style as `StatusEffectsService`); then if `lastValidSpeed > 0` → `vEff = lastValidSpeed`, returns `true`; else (no valid speed yet) → returns `false` and the caller **does not move the entity that tick** (GDD EC-MOV-19: "integration does not proceed with an undefined speed"; `BASE_MOVE_SPEED` is **not** used as a fallback — it is a reference value only, Story 001 decision, and a 5.0 fallback could exceed a slowed/rooted player's real speed);
  - `lastValidSpeed` is caller-owned (`0f` = none yet); Story 005's per-slot "last valid speed" array holds it.
- [ ] **AC-MOV-8**: base speed 5.0, full deflection along +Z → delta exactly 0.25 per tick (5.0 × 1/20).
- [ ] **AC-MOV-9**: a debuff stack that computes below 0.5 → Character Stats returns 0.5 → delta 0.025.
- [ ] **AC-MOV-10**: a buff stack above 20.0 → 20.0 → delta 1.0.
- [ ] **AC-MOV-7**: a tick with no input after a moving tick → delta 0.0 on X and Z.
- [ ] F-MOV-2 examples hold through the real `CharacterStats`: +3 flat +20% → 9.6 (0.48/tick); −50% → 2.5 (0.125/tick); two +20% buffs multiply (×1.44).

---

## Implementation Notes

- Files: `src/ServerLogic/MovementSystem/MovementIntegrator.cs`, `IMovementSpeedSource.cs`, `CharacterStatsMovementSpeedSource.cs`, `MovementSpeedResolver.cs`.
- The adapter calls `CharacterStats.GetEffectiveStatFloat(EntityID, StatID)` (float-schema stats; it throws `ArgumentException` only for int-schema stat IDs, which never happens here). The adapter itself does not catch; `MovementSpeedResolver` catches.
- The per-entity "last valid speed" lives in Story 005's per-slot state; this story only takes it as a `ref float` parameter (`0f` = none yet), so tests drive it directly.
- The error log string allocates only on the failure path; the no-allocation guardrail covers `Integrate` and the success path of `TryResolve`.

---

## Out of Scope

- Story 005: when integration is called, NavMesh validation, commit
- Story 006: Prohibited and auto-face changing `inputDir`

---

## QA Test Cases

- **AC-MOV-8**: Given (10, 0, 5), dir (0, 0, 1), vEff 5.0, dt 0.05 — Then (10, 0, 5.25); with a non-unit dir (0, 0, 3) — same result (normalized).
- **Zero guard**: dir (0, 0, 0) → oldPos exactly; dir (1e-9, 0, 0) passes the exact-zero guard but `Vector3.normalized` returns zero below its 1e-5 magnitude epsilon → no movement; assert that and document it (the wire never carries such a vector: shorts ×32,767 make the smallest non-zero component ≈ 3e-5).
- **AC-MOV-9/10**: real `CharacterStats` entity, base 5.0, ΣPct −1.20 → `GetEffectiveStat` 0.5 → delta 0.025; ΣFlat +30 → 20.0 → delta 1.0.
- **AC-MOV-7**: tick N moving, tick N+1 zero input → position N+1 == position N.
- **EC-MOV-19**: fake source fails on tick 3 after valid 5.0 on ticks 1–2 → `TryResolve` returns true, `vEff` 5.0, one `LogType.Error` logged (`LogAssert.Expect`); NaN, +Inf, 25.0, 0.4, and a source that throws behave the same; `lastValidSpeed` stays 5.0 after the failure.
- **First-tick failure**: `lastValidSpeed = 0f`, source fails → returns false, one error logged; the caller's position is unchanged (no `BASE_MOVE_SPEED` fallback).
- **Unknown entity (real `CharacterStats`)**: adapter over a `CharacterStats` with no registered entity → reads `0f` → `TryResolve` treats it as a failure (error logged; false with no last valid speed).
- **Adapter uses the float read**: real `CharacterStats` entity with effective speed 9.6 → adapter returns exactly 9.6f (an int read would give 9).
- **No caching**: speed changes between ticks 1 and 2 → tick 2 uses the new value.
- **No allocation**: 1,000 `Integrate` calls and 1,000 successful `TryResolve` calls inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_Integration_tests.cs` *(F-MOV-1 integration, not an Integration-type story)* — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (tuning read model)
- Unlocks: Story 005
