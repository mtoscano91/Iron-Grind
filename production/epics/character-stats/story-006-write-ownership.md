# Story 006: Write Ownership — ILevelingService Injection, Mob Guard, Write-Locked Stat Rejection

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Estimate**: 3–4 hours
> **Manifest Version**: 2026-06-28

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: Tier 1 pattern — synchronous cross-system calls where ordering matters use direct method calls on injected interfaces named `I[SystemName]Service`. `AddExperience` notifying the Leveling System when an XP threshold is crossed is a Tier 1 call on an injected `ILevelingService`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Dependency injection via constructor for plain C# classes. `ILevelingService` is injected at construction time. Tests inject a mock `ILevelingService` that records notification calls.

**Control Manifest Rules (Foundation layer)**:
- Required: Service interface naming `I[SystemName]Service` — source: ADR-010
- Required: Tier 1 pattern for synchronous cross-system calls (direct method call on injected interface) — source: ADR-010
- Forbidden: Never use `UnityEvent` for server-side game logic — source: ADR-010
- Guardrail: OQ-1 (caller identity enforcement for `SetBaseStat(Level)`) is unresolved — AC-13 and AC-15 are ADVISORY until OQ-1 is answered

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-13** [ADVISORY — OQ-1 pending]: Unauthorized caller calls `SetBaseStat(Level, 5)` → returns error; `GetBaseStat(Level)` unchanged. *Defer implementation until OQ-1 (caller identity mechanism: enum tag, interface cast, or compile-time restriction) is resolved.*
- [ ] **AC-14** [BLOCKING]: `AddExperience(MobEntityID, 500)` → completes without error; `GetBaseStat(MobEntityID, Experience)` = **0**; injected `ILevelingService` mock receives **zero** notification calls.
- [ ] **AC-15** [ADVISORY — OQ-1 pending]: `AddBuffModifier(EntityID, StatID.Level, ...)` and `StatID.Experience` → error returned; no modifier created; `GetEffectiveStat` for each returns pre-call value. *(CurrentHP and CurrentMP write-lock tested in Story 004 AC-06.)*
- [ ] **[NEW] AddExperience positive path** [BLOCKING]: `AddExperience(PlayerEntityID, 1000)` below configured threshold → `GetBaseStat(Experience)` = **1000**; `ILevelingService` mock receives zero calls. Then `AddExperience(PlayerEntityID, 600)` (total crosses threshold) → `GetBaseStat(Experience)` = **1600**; mock receives **one** notification call.

---

## Implementation Notes

*Derived from GDD Rules 9, Edge Cases EC-27–EC-28, and ADR-010 Tier 1 pattern:*

**`ILevelingService` injection (ADR-010 Tier 1):** `CharacterStats` constructor accepts an `ILevelingService` parameter. When `AddExperience(EntityID, amount)` causes a player's Experience to cross the Leveling System's configured threshold, call `ILevelingService.NotifyExperienceCrossedThreshold(EntityID)`. The threshold is provided by the `ILevelingService` implementation — `CharacterStats` does not hardcode XP thresholds.

**`AddExperience` mob guard (EC-28):** Validate that the target is a player entity. If a mob entity ID is passed, return immediately (no-op). Mob Experience is inert — cannot be written via `AddExperience` and never read by any system.

**`AddExperience` accumulates** (not overwrites): `GetBaseStat(Experience)` increases by `amount` each call.

**Write-locked stats for buff modifiers (EC-15):** `AddBuffModifier` must also reject `StatID.Level` and `StatID.Experience` in addition to `CurrentHP` and `CurrentMP` (covered in Story 004). Return an error; no modifier entry created.

**OQ-1 (caller identity enforcement for `SetBaseStat(Level)`):** Leave a `// TODO: OQ-1 — enforce caller identity for Level write` comment in `SetBaseStat`. Do not implement any enforcement until OQ-1 is answered via `/architecture-decision`.

**Performance**: `AddExperience` is a synchronous O(1) call — integer accumulation + one threshold comparison via `ILevelingService.GetThreshold()`. No GC allocations on the hot path. Mob guard is an early-return before any write. No performance impact expected.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 004**: `CurrentHP`/`CurrentMP` write-lock (AC-06)
- **Story 001–005**: All must be Done before this story starts
- **OQ-1 implementation**: Deferred pending `/architecture-decision` for caller identity mechanism

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs`

- **AC-14**: AddExperience on mob entity is a no-op
  - Given: Mob EntityID; Experience=0. `CharacterStats` constructed with mock `ILevelingService` (records calls).
  - When: `AddExperience(MobEntityID, 500)`
  - Then: Returns without error. `GetBaseStat(MobEntityID, Experience)` = 0. Mock received 0 notification calls.
  - Edge cases: `AddExperience(MobEntityID, 0)` → same no-op. Large amount on mob → still no-op.

- **[NEW] AddExperience positive path on player entity**
  - Given: Player EntityID; Experience=0. `CharacterStats` constructed with mock `ILevelingService` (threshold=1500).
  - When: `AddExperience(PlayerEntityID, 1000)` (below threshold)
  - Then: `GetBaseStat(PlayerEntityID, Experience)` = 1000. Mock received 0 calls.
  - When: `AddExperience(PlayerEntityID, 600)` (total=1600, crosses threshold)
  - Then: `GetBaseStat(PlayerEntityID, Experience)` = 1600. Mock received 1 notification call.
  - Edge cases: Two consecutive `AddExperience` calls — Experience accumulates (not overwrites).

- **AC-13 (ADVISORY — OQ-1 pending)**:
  - Status: Deferred. Placeholder: When OQ-1 is answered, verify `SetBaseStat(Level, 5)` from an unauthorized caller returns error and `GetBaseStat(Level)` = previous value unchanged.

- **AC-15 (ADVISORY — OQ-1 pending)**:
  - Status: Deferred (write-lock for Level/Experience via `AddBuffModifier`). Placeholder: `AddBuffModifier(EntityID, StatID.Level, ...)` returns error; no modifier created; `GetEffectiveStat(Level)` returns pre-call value.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003, Story 004, Story 005 must be Done
- Unlocks: Story 007

## Completion Notes
**Completed**: 2026-07-02
**Criteria**: 2/4 passing (AC-13 and AC-15 deferred — ADVISORY, OQ-1 pending; both blocking ACs fully covered)
**Deviations**: TR-stats-006 not registered in tr-registry.yaml (run /architecture-review to register); AC-13/AC-15 deferred per story spec
**Test Evidence**: Logic — `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (7 tests: 3 mob-guard, 4 player-path)
**Code Review**: Complete — 4 required changes applied (null guard, remarks removed, amount≤0 guard, redundant GetBaseStat eliminated)
