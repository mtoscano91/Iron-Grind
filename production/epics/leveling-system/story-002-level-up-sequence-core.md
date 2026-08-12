# Story 002: Level-Up Sequence Core — Guard, Increment, Auto-Alloc, Derived Stats, HP/MP Restore

> **Epic**: Leveling System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/leveling-system.md`
**Requirement**: `TR-lvl-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: None — design-only, LOW risk
**ADR Decision Summary**: N/A.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — the level-up sequence fires once per level-up (a rare, player-triggered event), not on the 20Hz tick path.

**Control Manifest Rules (Core layer)**:
- Required: N/A for this story (no event broadcast here — see Story 003 for `OnLevelUp`)

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [x] **AC-LS-03** [BLOCKING]: Level-up sequence step order — `SetBaseStat(Level, 20)` fires first, followed by at least one STR/DEX/VIT auto-alloc write, followed by derived-stat writes; `LevelTierMultiplier` used is ×1.2 (not ×1.0) for a L19→L20 transition. Requires exposing a call-order test seam (`ITestableCallOrderObserver` or equivalent) — confirm the exact interface shape during implementation; this is documented as a sprint-planning dependency in the GDD. *(Implemented via subscribing to the real `OnStatChanged` event rather than a new seam — simpler, uses the real production signal.)*
- [x] **AC-LS-04** [BLOCKING]: Warrior L1→L2 auto-alloc — `SetBaseStat(STR,12)`, `SetBaseStat(DEX,11)`, `SetBaseStat(VIT,11)` fire in that order (matching CR-2.4's `{STR,DEX,VIT,INT}` iteration order); INT untouched; each fires `OnStatChanged`.
- [x] **AC-LS-05** [BLOCKING]: Healer L1→L2 auto-alloc — `SetBaseStat(VIT,11)`, `SetBaseStat(INT,12)` fire; STR/DEX untouched; `heldFreePoints` increments by 2.
- [x] **AC-LS-06** [BLOCKING]: Full HP/MP restore uses the freshly-written MaxHP/MaxMP from this same sequence, not the pre-level-up value — see the CR-2.6→CR-2.7 worked example in Implementation Notes.

---

## Implementation Notes

**Real `StatID` names, corrected during this story's context-load (2026-07-22) — use these, not the GDD's abbreviations.** `leveling-system.md`'s prose uses `STR/DEX/VIT/INT` throughout; the actual enum (`src/Foundation/CharacterStats/StatID.cs`) uses `Strength`/`Dexterity`/`Vitality`/`Intelligence`. Map mentally as you read CR-2 in the GDD.

**`MagicDefense` gap found and fixed this session, before this story was implemented.** `character-stats.md`'s own GDD (F-7) always required `MagicDefense` as a real int-schema stat, range `[0, 9999]`, written by the Leveling System on every level-up — but the `StatID` enum (Character Stats epic, Complete) never had a `MagicDefense` member. Confirmed via full-repo grep that no code hardcodes a numeric `StatID` literal, so it was safe to add. Fixed directly (user-approved) by inserting `StatID.MagicDefense = 7` into the int-schema block (between `Defense` and the resource-pool group), shifting `CurrentHP`/`MaxMP`/`CurrentMP`/`Level`/`Experience`/all float-schema members by +1. `StatSchema.GetStatMin`/`GetStatMax` updated with `MagicDefense` → `[0f, 9999f]`. This was a pre-implementation fix to already-closed Character Stats files, not part of this story's own scope — it just needed to happen before this story could write `MagicDefense` correctly. `StatID.MagicDefense` now exists and is usable exactly like any other int-schema stat (`SetBaseStat`/`GetBaseStat`).

*Derived from CR-2.1–CR-2.7 (leveling-system.md):*

Fixed, non-reorderable sequence per level (this story covers steps 1–7; consecutive-level looping and the `OnLevelUp` broadcast are Story 003):

1. **At-Cap Guard (CR-2.1)**: if `GetBaseStat(Level) == 60`, abort — no level-up fires (see Story 008 for full cap behavior; this story only needs the guard itself).
2. **Increment Level (CR-2.2)**: `SetBaseStat(EntityID, Level, currentLevel + 1)`.
3. **Determine LevelTierMultiplier (CR-2.3)**: L1–19: ×1.0, L20–39: ×1.2, L40–59: ×1.5, L60: ×2.0. (Tier-transition-specific from-scratch recompute mechanics are Story 004 — this story only needs the lookup.)
4. **Auto-Allocate (CR-2.4)**: read `ClassDefinition` via `IClassRegistry.TryGetClass(classType, out def)` (classType cached per-entity during `InitializeAtL1`, Story 009). For each of `{Strength, Dexterity, Vitality, Intelligence}` in that fixed order, if `def.AutoAllocIncrement[stat] > 0`, call `SetBaseStat(stat, GetBaseStat(stat) + increment)`. Warrior: 3 calls (Strength+2, Dexterity+1, Vitality+1). Healer: 2 calls (Vitality+1, Intelligence+2). Values come from `ClassDefinition`, never hardcoded here.
5. **Read Current Totals (CR-2.5)**: `GetBaseStat()` for Strength/Dexterity/Vitality/Intelligence *after* step 4, so auto-alloc is included.
6. **Recompute Derived Stats (CR-2.6)**: evaluate F-3 through F-9 from the CR-2.5 totals with the new `LevelTierMultiplier`, in this exact order: MaxHP → MaxMP (clamp to ≤9,999 before write — see Story 004 for the ceiling-clamp AC) → AttackPower → Defense → MagicDefense → CritChance (write raw, unclamped, via `SetBaseStatFloat` — it's a float-schema stat) → AttackSpeedMultiplier (write raw, unclamped, via `SetBaseStatFloat`). Formulas:
   - `MaxHP = Mathf.FloorToInt((200 + Vitality×20) × tier)` → `SetBaseStat(MaxHP, ...)`
   - `MaxMP = min(Mathf.FloorToInt((100 + Intelligence×12) × tier), 9999)` → `SetBaseStat(MaxMP, ...)`
   - `AttackPower = Mathf.FloorToInt((10 + Strength×2) × tier)` → `SetBaseStat(AttackPower, ...)`
   - `Defense = Mathf.FloorToInt((5 + Vitality×1.5) × tier)` → `SetBaseStat(Defense, ...)`
   - `MagicDefense = Mathf.FloorToInt(Intelligence×0.4 × tier)` → `SetBaseStat(MagicDefense, ...)` (int-schema, see the fix note above)
   - `CritChance = 0.05f + (Dexterity×0.0015f × tier)` → `SetBaseStatFloat(CritChance, ...)`
   - `AttackSpeedMultiplier = 1.0f + (Dexterity×0.003f × tier)` → `SetBaseStatFloat(AttackSpeedMultiplier, ...)`
7. **Full HP/MP Restore (CR-2.7)**: after ALL derived stats are written, `SetBaseStat(CurrentHP, GetBaseStat(MaxHP))` then `SetBaseStat(CurrentMP, GetBaseStat(MaxMP))` — using the value **just written** in step 6, not the pre-level-up value. This is the only time the Leveling System writes CurrentHP/CurrentMP directly (not via `ApplyRegen`) — a level-up is a discrete full-state reset. `GetBaseStat(MaxHP)` must return the freshly-written value; Character Stats must not have a deferred propagation path.

Worked example (AC-LS-06): Warrior L9→L10, pre-level-up Vitality=18, MaxHP_old=560, CurrentHP=50 (mid-combat). Auto-alloc Vitality→19. New MaxHP = `FloorToInt((200+19×20)×1.0) = 580`. CR-2.7 writes `CurrentHP=580` — not 50, not 560.

---

## Out of Scope

*Handled by neighbouring stories:*

- Consecutive level-up looping (CR-2.9), `heldFreePoints` grant (CR-2.8), `OnLevelUp` broadcast (CR-2.10), re-entrancy guards — Story 003
- Tier-transition-specific from-scratch recompute proof and raw-write ceiling clamps — Story 004
- At-cap guard's full behavior (XP clamping, terminal state) — Story 008
- `InitializeAtL1` / classType caching — Story 009

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_LevelUpSequenceCore_tests.cs`

- **AC-LS-03**: Given a Warrior at L19 with sufficient XP and a call-order spy, When CR-2 executes, Then Level write precedes auto-alloc writes precedes derived-stat writes, and tier=×1.2 is used.
- **AC-LS-04**: Given Warrior L1 Strength=Dexterity=Vitality=10, When level-up to L2, Then `SetBaseStat` calls fire in Strength→Dexterity→Vitality order with correct values, Intelligence untouched.
- **AC-LS-05**: Given Healer L1 Vitality=Intelligence=10, When level-up to L2, Then Vitality→Intelligence fire in order, Strength/Dexterity untouched, `heldFreePoints` becomes 2.
- **AC-LS-06**: Given a Warrior mid-combat at L9 (Vitality=18, CurrentHP=50, MaxHP_old=560), When level-up to L10, Then CurrentHP is set to the freshly-computed MaxHP=580, not 50 or 560.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_LevelUpSequenceCore_tests.cs` — must exist and pass

**Status**: [x] Created — 4 tests, all 4 blocking ACs covered. Not confirmed in a live Unity Editor this session (none was open/available) — verified statically (grep-confirmed test counts, hand-verified formulas, 2 parallel self-performed code reviews, both 0 BLOCKING).

---

## Dependencies

- Depends on: Story 001 (threshold-crossed notification, Complete), Class System epic (Complete GDD; no concrete implementation exists — mocked via new `IClassRegistry`/`ClassDefinition`/`ClassRegistry` in `src/Foundation/LevelingSystem/`, confirmed via full-repo grep that neither existed before this story)
- Unlocks: Story 003 (consecutive level-up wraps this sequence in a loop), Story 004 (tier-transition proof builds on this sequence)

---

## Completion Notes
**Completed**: 2026-07-22
**Criteria**: 4/4 passing (AC-LS-03, AC-LS-04, AC-LS-05, AC-LS-06) — 4 tests in `tests/EditMode/LevelingSystem/LevelingSystem_LevelUpSequenceCore_tests.cs`.
**New production**: `LevelingService.ExecuteLevelUpSequence` (full rewrite of Story 001's `NotifyExperienceCrossedThreshold` stub — the real CR-2.1–CR-2.7 level-up sequence). `ClassDefinition`/`IClassRegistry`/`ClassRegistry` (new forward-dependency mocks for the not-yet-built Class System, `src/Foundation/LevelingSystem/`).
**Two further real gaps found and fixed in already-closed Character Stats files this session, both user-approved before any code was written** (bringing this epic's running total to three, after Story 001's `ILevelingService` contract mismatch):
1. **`MagicDefense` missing entirely**: `character-stats.md`'s own GDD (F-7) always required a `MagicDefense` int-schema stat — never added to the real `StatID` enum. Fixed by inserting `StatID.MagicDefense = 7`, renumbering `CurrentHP`/`MaxMP`/`CurrentMP`/`Level`/`Experience`/all float-schema members by +1 (safe — verified via full-repo grep that no code anywhere hardcodes a numeric `StatID` literal, and every array-size constant in `CharacterStats.cs`/`StatSchema.cs` is computed from enum member references, not literals). `StatSchema.GetStatMin`/`GetStatMax` extended with `MagicDefense` → `[0, 9999]`.
2. **`SetBaseStat(CurrentHP/CurrentMP, ...)` silently broken**: the GDD's CR-2.7 literally instructs `SetBaseStat(CurrentHP, GetBaseStat(MaxHP))` for the level-up HP restore, but `CurrentHP`/`CurrentMP` are actually stored in separate `Dictionary<EntityID,float>` fields written only by `ApplyDamage`/`ApplyRegen` — `SetBaseStat` would write into a dead array slot nobody reads, and `GetCurrentHP()` would keep returning the stale pre-level-up value. Found by the implementing agent tracing the real code rather than trusting the GDD's literal instruction. Fixed with new additive `CharacterStats.SetCurrentHP`/`SetCurrentMP` methods (clamp against `GetEffectiveStat(MaxHP/MaxMP)` matching `ApplyDamage`/`ApplyRegen`'s existing convention, fire `OnStatChanged`) — confirmed via `git diff` as a pure 48-line insertion, 0 deletions, nothing else in `CharacterStats.cs` touched.
**Design simplification during implementation**: Story 001's narrower `AttachLevelProvider` delegate (added specifically to avoid a full `CharacterStats` back-reference) was consolidated into a single `AttachCharacterStats(CharacterStats stats)` reference once Story 002 confirmed the fuller need — `GetExperienceThreshold` now reads `Level` through the same reference used for the CR-2 sequence's many other reads/writes. Story 001's test file was updated to match rather than leaving two parallel wiring mechanisms.
**Code Review**: Complete (lean self-performed review: unity-specialist CLEAN + qa-tester CLEAN, both parallel, 0 BLOCKING from either; both independently found the same 1 trivial Required Change — a stale doc-comment reference to the removed `AttachLevelProvider` name — fixed).
**Test Evidence**: Logic — `tests/EditMode/LevelingSystem/LevelingSystem_LevelUpSequenceCore_tests.cs`, 4 tests, all blocking ACs covered; Story 001's test file also updated (targeted edits, all 7 original tests still present and passing per static review). Not live-Editor-confirmed this session.
**Deviations**: None from Out of Scope — `ExecuteLevelUpSequence` resolves exactly one level per call, no `OnLevelUp` broadcast, no full `heldFreePoints` persistence beyond the minimal counter AC-LS-05 needs.
**Handoff note for Story 003**: `LevelingService._heldFreePoints` (a bare per-entity counter) and the single-level `ExecuteLevelUpSequence` are both intentionally minimal — Story 003 owns wrapping this in the CR-2.9 consecutive-level loop, adding the `OnLevelUp` broadcast (ADR-010 Tier-2 event), and the `_levelingUpInProgress` re-entrancy guard.
