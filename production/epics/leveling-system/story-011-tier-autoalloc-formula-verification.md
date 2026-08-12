# Story 011: Tier Multiplier & Auto-Alloc Formula Verification

> **Epic**: Leveling System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 1-2 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — this story is a pure formula/data verification test, exercising existing machinery from Stories 002/003/009, not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- N/A.

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [ ] **AC-LS-34** [BLOCKING]: `LevelTierMultiplier` boundary values correct at L1,19,20,39,40,59,60 — ×1.0/×1.0/×1.2/×1.2/×1.5/×1.5/×2.0. No stored field — derived on-demand from `GetBaseStat(Level)`.
- [ ] **AC-LS-35** [BLOCKING]: Warrior L60 auto-alloc snapshot with zero free-point spend — STR=128, DEX=69, VIT=69, INT=10, heldFreePoints=59.
- [ ] **AC-LS-36** [BLOCKING]: Healer L60 auto-alloc snapshot with zero free-point spend — STR=10, DEX=10, VIT=69, INT=128, heldFreePoints=118.

---

## Implementation Notes

*Derived from F-LS-3, F-LS-4 (leveling-system.md):*

- **F-LS-3 — LevelTierMultiplier**: L1–19: ×1.0, L20–39: ×1.2, L40–59: ×1.5, L60: ×2.0. This is a pure lookup table — this story tests only the lookup function itself, in isolation from any level-up sequence (contrast with Story 004, which proves the multiplier is correctly APPLIED during a real tier transition). Owned/defined by the Character Stats GDD; referenced here for completeness — do not duplicate the canonical definition, just the Leveling System's own lookup call.
- **F-LS-4 — Attribute auto-allocation, accumulated totals at L60** (59 level-ups from L1, zero free-point spend):
  - Warrior: STR = 10 + 59×2 = 128; DEX = 10 + 59×1 = 69; VIT = 10 + 59×1 = 69; INT = 10 (untouched); `heldFreePoints` max = 59.
  - Healer: VIT = 10 + 59×1 = 69; INT = 10 + 59×2 = 128; STR = DEX = 10 (untouched); `heldFreePoints` max = 118.
- This story is a pure data-table verification — the most efficient way to write it is to drive a real `InitializeAtL1` → 59× simulated level-up sequence (reusing Story 002/003/009's already-implemented machinery) and assert the final snapshot, rather than hand-computing 59 iterations in the test itself. This doubles as an end-to-end regression proof for the whole level-up pipeline at scale.
- `IClassRegistry`/`ClassDefinition` values are read from the class registry, not hardcoded — same forward-dependency mock-provider treatment as Stories 002/006 (no real Class System implementation exists yet).

---

## Out of Scope

*Handled by neighbouring stories:*

- The tier-transition-specific from-scratch recompute proof at each individual boundary — Story 004
- `XpThreshold` table (a separate formula, F-LS-1) — Story 010

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_TierAutoAllocFormulaVerification_tests.cs`

- **AC-LS-34**: Given characters constructed at L1,19,20,39,40,59,60, When `LevelTierMultiplier` is derived for each, Then values match ×1.0/1.0/1.2/1.2/1.5/1.5/2.0 exactly, no stored field referenced.
- **AC-LS-35**: Given a Warrior driven from L1 to L60 via 59 simulated level-ups with zero free-point spend, When queried, Then STR=128, DEX=69, VIT=69, INT=10, heldFreePoints=59.
- **AC-LS-36**: Given a Healer driven from L1 to L60 the same way, When queried, Then STR=10, DEX=10, VIT=69, INT=128, heldFreePoints=118.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_TierAutoAllocFormulaVerification_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (level-up sequence, driven 59× to reach L60), Story 003 (consecutive level-up loop), Story 009 (`InitializeAtL1`)
- Unlocks: None
