# Story 008: Integration — Leveling System ↔ Character Stats (Spawn Path, Tier Transitions, MaxMP Ceiling)

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours (one integration test file; no production code changes expected — all behavior already exists in `LevelingService`/`CharacterStats`)

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-001`, `TR-stats-003`, `TR-stats-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None (design-only; LOW engine risk)
**ADR Decision Summary**: No ADR governs the Leveling System ↔ Character Stats collaboration. Integration tests verify that the Leveling System calls Character Stats APIs in the correct sequence: auto-alloc before tier recompute; `MaxMP` clamped before `SetBaseStat`; spawn path uses `LevelTierMultiplier ×1.0`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Integration tests require real `CharacterStats` and real `LevelingService` instances — no mocks for the two principal systems. Both are plain C# classes (EditMode-compatible); no Unity scene required.

**Control Manifest Rules (Foundation layer)**:
- Required: Leveling System must use `Mathf.FloorToInt()` (not `System.Math.Floor()`) for F-3 through F-9 — source: GDD Rule 7 / AC-25
- Required: Auto-allocation fires before tier recompute at milestone level-ups — ordering contract per AC-27b
- Guardrail: Leveling System must clamp F-4 output to 9,999 before calling `SetBaseStat(MaxMP)` — Character Stats does NOT enforce StatMax (verified in Story 001)

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

> **Build note (corrected 2026-09-25 readiness pass):** "Warrior Tank" gains 2 VIT/level = **1 VIT auto-alloc** (inside the level-up sequence) **+ 1 free point spent on VIT** via a separate `LevelingService.AllocateFreePoint(entity, StatID.Vitality)` call, which recomputes F-3–F-9 at the current tier. Each tier sub-case therefore asserts twice: after the level-up (VIT+1) and after the free-point spend (VIT+2). The GDD's AC-27 was corrected in the same pass (it previously attributed the full +2 to auto-alloc and listed an incorrect ordering-violation value of 2790; the correct figure is 2880).

- [x] **AC-27a** [BLOCKING]: Warrior Tank entity at L19 (VIT=46). L19→L20 level-up (auto-alloc VIT→47; tier ×1.0→×1.2; F-3 recomputed): `GetBaseStat(MaxHP)` = **1368** (`floor((200+47×20)×1.2)`). Then `AllocateFreePoint(VIT)` (VIT→48): `GetBaseStat(MaxHP)` = **1392** (`floor((200+48×20)×1.2)`). Failure values: 1140 / 1160 (tier ×1.0 still applied), 1344 (VIT=46 — recompute before auto-alloc).
- [x] **AC-27b** [BLOCKING]: Warrior Tank at L39 (VIT=86). L39→L40 level-up: auto-alloc VIT→87 **FIRST**, THEN F-3 recomputed at tier ×1.5: `GetBaseStat(MaxHP)` = **2910** (`floor((200+87×20)×1.5)`). A return of **2880** (VIT=86 used — ordering violation) is an ordering regression failure; 1940 (tier ×1.0) or 2328 (tier ×1.2) is also failure. Then `AllocateFreePoint(VIT)` (VIT→88): `GetBaseStat(MaxHP)` = **2940** (`floor((200+88×20)×1.5)`); 1960 (×1.0) or 2352 (×1.2) is failure.
- [x] **AC-27c** [BLOCKING]: Warrior Tank at L59 (VIT=126). L59→L60 level-up (VIT→127; tier→×2.0): `GetBaseStat(MaxHP)` = **5480** (`floor((200+127×20)×2.0)`); 5440 (VIT=126 — ordering violation) or 4110 (×1.5) is failure. Then `AllocateFreePoint(VIT)` (VIT→128): `GetBaseStat(MaxHP)` = **5520**; 2760 (×1.0) or 4140 (×1.5) is failure.
- [x] **AC-31** [BLOCKING]: New Warrior at spawn. Class System triggers Leveling System init (tier ×1.0, all primary attributes=10). `GetBaseStat(MaxHP)` immediately after spawn = **400** (`floor((200+10×20)×1.0)`). A return of 200 (formula base without tier) is failure.
- [x] **AC-34** [BLOCKING]: INT=409 at L60 (tier ×2.0). Leveling System evaluates F-4 raw=10,016; clamps to 9,999; calls `SetBaseStat(MaxMP, 9999)`. `GetBaseStat(MaxMP)` = **9999**. Then INT=500 → F-4 raw=12,200; clamp → 9999; `GetBaseStat(MaxMP)` = **9999**. Also verify: direct `SetBaseStat(MaxMP, 10016)` (bypassing Leveling System) → `GetBaseStat(MaxMP)` = **10016** (proves Character Stats does NOT clamp — Leveling System owns it).

---

## Implementation Notes

*Derived from GDD F-2a (LevelTierMultiplier), F-3–F-9, F-10 (level-up auto-alloc), F-4 (MaxMP ceiling):*

**AC-27b ordering contract (critical regression detector):** At milestone level-ups (L20, L40, L60), the Leveling System MUST:
1. Apply all auto-allocations for the new level via `SetBaseStat()` (e.g., VIT 86→87 at L40)
2. THEN recompute F-3 through F-9 with the new `LevelTierMultiplier`

Failure mode: if F-3 fires before auto-alloc, Warrior Tank at L40 returns 2880 (VIT=86) instead of 2910 (VIT=87) immediately after the level-up — a detectable ordering violation. The free-point spend (VIT→88, MaxHP→2940) is a separate, later call and is not part of the ordering contract.

**AC-31 spawn path:** At entity spawn, the Class System calls the Leveling System's stat-initialization routine — `LevelingService.InitializeAtL1(entityId, classType)` (`src/Foundation/LevelingSystem/LevelingService.cs`). The Leveling System treats L1 spawn as a tier ×1.0 event and computes F-3 through F-9 with `LevelTierMultiplier = 1.0` and all starting primary attributes = 10. This path is triggered by Class System → Leveling System initialization, not by a level-up event.

**AC-34 clamp ownership split:** The Leveling System evaluates F-4 and calls `min(rawResult, 9999)` before calling `SetBaseStat(MaxMP, ...)`. Character Stats' `SetBaseStat()` does NOT enforce `StatMax` — it stores exactly what is written. The two-part AC-34 test verifies both halves: (a) Leveling System correctly writes 9999 even when raw formula = 10016; (b) direct `SetBaseStat(MaxMP, 10016)` writes 10016, proving Character Stats does not clamp.

**Integration test setup:** Instantiate real `CharacterStats` and real `LevelingService` (`new LevelingService(xpThresholds, classRegistry)`), then wire them with `leveling.AttachCharacterStats(stats)` — this is the actual injection seam; there is no constructor-injected `ICharacterStats`. Register the entity via `RegisterPlayerEntity` + `RegisterPlayerClassType`. Provide a stub Class System step that calls `leveling.InitializeAtL1(entityId, WarriorClassType)` at spawn. Mirror the wiring helper already used by `tests/EditMode/LevelingSystem/LevelingSystem_TierTransitionRecompute_tests.cs` (`CreateWiredStats`), including its XP-threshold sentinel sizing (the CR-2.9 re-check needs index `level+1` to exist). No mocks for the two principal systems — integration means real collaboration.

**Overlap note:** Leveling System AC-LS-27/38/53/54/43 already exercise real `CharacterStats` with a plain Warrior (no free-point spend). This story's distinct value: the Warrior Tank free-point path, the Character Stats does-not-clamp proof (direct 10016 write), and the exact GDD AC-27/31/34 values.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 001–007**: All Logic stories — must be Done before this story starts
- **Respec procedure**: Story 007 covers the transaction API unit test; Leveling System epic covers the Leveling System's respec logic
- **Status Effects integration**: Belongs in Status Effects epic

---

## QA Test Cases

*Written by qa-lead at story creation (values corrected 2026-09-25 readiness pass). The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs`
*(Placed under `tests/EditMode/` so it compiles into the existing `IronGrind.Foundation.EditModeTests` assembly — a top-level `tests/Integration/` folder has no asmdef and would be invisible to Unity.)*
*Setup: real `CharacterStats` + real `LevelingService` (wired via `AttachCharacterStats`) + stub Class System step calling `InitializeAtL1` — no mocks for principal systems*

- **AC-31**: L1 spawn path initializes MaxHP with LevelTierMultiplier ×1.0
  - Given: New Warrior entity; stub Class System triggers Leveling System init (tier ×1.0, all primary attributes=10)
  - When: Initialization sequence completes. `GetBaseStat(MaxHP)`.
  - Then: Returns 400 (`floor((200+10×20)×1.0) = 400`). A return of 200 (tier not applied) is failure.
  - Edge cases: Verify CharacterStats has no MaxHP value before spawn sequence fires.

- **AC-27a**: L19→L20 tier transition applies ×1.2 multiplier
  - Given: Warrior Tank entity at L19; VIT=46 (10 base + 18 level-ups × 2 VIT: 1 auto + 1 free)
  - When: Leveling System processes L19→L20 (auto-alloc: VIT→47; tier: ×1.0→×1.2; F-3 recomputed). `GetBaseStat(MaxHP)`.
  - Then: Returns 1368 (`floor((200+47×20)×1.2)`). 1140 (tier ×1.0) or 1344 (VIT=46 — ordering violation) is failure.
  - When: `AllocateFreePoint(entity, StatID.Vitality)`. `GetBaseStat(MaxHP)`.
  - Then: Returns 1392 (`floor((200+48×20)×1.2)`). 1160 (tier ×1.0) is failure.
  - Edge cases: Verify `GetBaseStat(VIT)` = 47 after level-up and 48 after the free-point spend; `AllocateFreePoint` returns `Success`.

- **AC-27b**: L39→L40 transition — auto-alloc fires BEFORE tier recompute (ordering regression detector)
  - Given: Warrior Tank entity at L39; VIT=86 (10 base + 38 level-ups × 2 VIT: 1 auto + 1 free)
  - When: Leveling System processes L39→L40 (ordering: VIT→87 FIRST via auto-alloc; THEN F-3 recomputed at tier ×1.5). `GetBaseStat(MaxHP)`.
  - Then: Returns 2910 (`floor((200+87×20)×1.5)`). A return of 2880 (VIT=86 used — auto-alloc fired after recompute) is an ordering regression failure. Returns of 1940 (tier ×1.0) or 2328 (tier ×1.2) also indicate failure.
  - When: `AllocateFreePoint(entity, StatID.Vitality)`. `GetBaseStat(MaxHP)`.
  - Then: Returns 2940 (`floor((200+88×20)×1.5)`). 1960 (×1.0) or 2352 (×1.2) is failure.
  - Edge cases: Compare MaxHP at L39 end vs L40 start to confirm the tier spike is visible.

- **AC-27c**: L59→L60 transition — final tier milestone ×2.0
  - Given: Warrior Tank at L59; VIT=126 (10 + 58 level-ups × 2 VIT: 1 auto + 1 free)
  - When: Leveling System processes L59→L60 (VIT→127; tier→×2.0; F-3 recomputed). `GetBaseStat(MaxHP)`.
  - Then: Returns 5480 (`floor((200+127×20)×2.0)`). 5440 (VIT=126 — ordering violation) or 4110 (×1.5) is failure.
  - When: `AllocateFreePoint(entity, StatID.Vitality)`. `GetBaseStat(MaxHP)`.
  - Then: Returns 5520 (`floor((200+128×20)×2.0)`). 2760 (×1.0) or 4140 (×1.5) is failure.
  - Edge cases: Verify Level=60 after this level-up (cap); subsequent `AddExperience` does not trigger further level-up.

- **AC-34**: Leveling System clamps F-4 before SetBaseStat; Character Stats does not clamp
  - Given: Player entity with INT=409 at L60 (tier ×2.0)
  - When: Leveling System evaluates F-4 = `floor((100+409×12)×2.0)` = 10,016; clamps to 9999; calls `SetBaseStat(MaxMP, 9999)`
  - Then: `GetBaseStat(MaxMP)` = 9999 (not 10016).
  - When: INT→500; Leveling System evaluates F-4 = 12,200; clamps to 9999; calls `SetBaseStat(MaxMP, 9999)`
  - Then: `GetBaseStat(MaxMP)` = 9999.
  - When (direct path, no Leveling System): `SetBaseStat(MaxMP, 10016)` called directly
  - Then: `GetBaseStat(MaxMP)` = 10016 (Character Stats stores unclamped — proves Leveling System owns the clamp).
  - Edge cases: INT=408 → F-4 = `floor((100+408×12)×2.0)` = 9992 → no clamp needed → `GetBaseStat(MaxMP)` = 9992.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs` OR documented playtest — must exist and pass

**Status**: [x] Created and passing — 5 tests, verified in the real Unity Test Runner 2026-09-25 (full EditMode suite 821/821)

---

## Dependencies

- Depends on: Character Stats Stories 001–007 (all Complete); Leveling System epic (13/13 Complete as of 2026-09-25) — **all dependencies satisfied**
- Unlocks: Nothing — this is the final story in the character-stats epic

---

## Completion Notes
**Completed**: 2026-09-25
**Criteria**: 5/5 passing (AC-31, AC-27a, AC-27b, AC-27c, AC-34) — all verified by real Unity Test Runner execution; full EditMode suite 821/821 green
**Deviations** (advisory):
- AC-34 drives the F-4 recompute via `AllocateFreePoint(Intelligence)` with held points seeded by `RestoreLevelingState` (Level pinned at the L60 cap, so no level-up can fire) — same `RecomputeDerivedStats` path as a level-up.
- AC-27b seeds the L39 MaxHP baseline (2304) directly via `SetBaseStat` to make the tier spike explicit; the ×1.2 formula itself is proven by AC-27a.
- Readiness pass corrected GDD AC-27 (Warrior Tank +2 VIT = 1 auto-alloc + 1 free point; ordering-violation value 2880, not 2790).
**Test Evidence**: Integration test at `tests/EditMode/Integration/CharacterStats/LevelingSystem_CharacterStats_integration_tests.cs` (5 [Test] methods)
**Code Review**: Complete — unity-specialist CLEAN, qa-tester TESTABLE; APPROVED WITH SUGGESTIONS, suggestions 1 & 3 applied, suggestion 2 logged as TD-040
