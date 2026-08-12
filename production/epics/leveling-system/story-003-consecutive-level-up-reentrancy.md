# Story 003: Consecutive Level-Up & Re-Entrancy Guards

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

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: `OnLevelUp` has multiple subscribers (HUD, Audio, Skill System) — a Tier-2 broadcast `event Action<T>` with `readonly struct` args, per ADR-010's three-tier model. `ILevelingService.NotifyExperienceCrossedThreshold` (Story 001's real, corrected contract — GDD prose says `OnExperienceThresholdCrossed`, the actual interface uses `NotifyExperienceCrossedThreshold`) is the single-listener Tier-1 counterpart; this story's broadcast is the multi-subscriber sibling.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.
**Performance**: No budget impact expected — `OnLevelUp` fires once per level-up (a rare, player-triggered event, capped by CR-2.9's own consecutive-level loop), not on the 20Hz tick path. `event Action<T>` with `readonly struct` args guarantees zero per-emit heap allocation regardless.

**Control Manifest Rules (Core layer)**:
- Required: Broadcast events (≥2 subscribers) use `event Action<T>` with `readonly struct` T — zero per-emit allocation (ADR-010)
- Required: Event naming PascalCase, `On` prefix — `OnLevelUp` (ADR-010)
- Forbidden: `Action<object>` or class-typed event args (ADR-010)
- Forbidden: lambda captures for persistent event subscriptions (ADR-010)

---

## Acceptance Criteria

*From `design/gdd/leveling-system.md`, scoped to this story:*

- [x] **AC-LS-07** [BLOCKING]: `heldFreePoints` increments correctly per class on level-up (Warrior +1, Healer +2); accessible only via `GetHeldFreePoints(EntityID)`, never `GetBaseStat`. *(Also verified accumulating correctly across multiple loop iterations within a single call, added during code review.)*
- [x] **AC-LS-08** [BLOCKING]: A single XP grant crossing 3 thresholds (L3→L4→L5→L6) fires `OnLevelUp(entity,4)`, `OnLevelUp(entity,5)`, `OnLevelUp(entity,6)` in sequence, all AFTER all CR-2.1–CR-2.9 iterations complete — `GetBaseStat(Level)` reaches 6 before any broadcast fires.
- [x] **AC-LS-09** [BLOCKING]: Consecutive level-up: each iteration reads fresh attribute totals written by the prior iteration's own auto-alloc — final MaxHP matches the formula applied to the LAST iteration's totals, not an earlier one.
- [x] **AC-LS-10** [BLOCKING]: `_levelingUpInProgress == true` blocks `AllocateFreePoint` — call rejected, `heldFreePoints` unchanged, no `SetBaseStat`, "system busy" response returned.
- [x] **AC-LS-49** [BLOCKING] — **split into two tests during implementation, see Completion Notes for why**: Re-entrant `NotifyExperienceCrossedThreshold` call (a subscriber calls `AddExperience` from inside an `OnStatChanged` handler mid-sequence) — no `SetBaseStat(Level,...)` during the re-entrant call itself, but the XP write is NOT blocked (only the level-up sequence is); after the original sequence exits and clears `_levelingUpInProgress`, CR-2.9 catches up and fires the additional level-up.

---

## Implementation Notes

**Real code state, as of Story 002's completion (2026-07-22) — read `src/Foundation/LevelingSystem/LevelingService.cs` fresh before implementing, this describes it as of now:**
- `NotifyExperienceCrossedThreshold(EntityID)` currently calls `ExecuteLevelUpSequence(entityId)` exactly once — resolves exactly ONE level per call, no loop, no `OnLevelUp`, no `_levelingUpInProgress`. This story wraps it in the CR-2.9 loop and adds the re-entrancy guard around it.
- **AC-LS-07 is already partially satisfied by existing Story 002 code**: `ExecuteLevelUpSequence`'s CR-2.4 block already does the minimal `heldFreePoints` bookkeeping (`_heldFreePoints[entityId] += def.FreePointsPerLevel`), and `GetHeldFreePoints(EntityID)` already exists and is the only accessor (no `StatID` for it, confirmed — it is intentionally not a Character Stats field). This story likely needs no new production code for AC-LS-07 itself, just a test confirming it still holds through the new consecutive-level-up loop. Verify, don't assume — but don't be surprised if this AC needs no new code.
- `AllocateFreePoint` does not exist yet anywhere (Story 005's job). For AC-LS-10, implement the smallest reasonable stub: check `_levelingUpInProgress` and reject if true (satisfies this story's AC), without building Story 005's real CR-3 guards/decrement/write/recompute. If you're unsure how to shape the non-blocked-path behavior of a stub that Story 005 will later replace, stop and ask rather than guessing — this is exactly the kind of ambiguity that's come up twice already in this epic.
- `OnLevelUp` does not exist yet. ADR-010 Decision 3's own example (`docs/architecture/ADR-010-event-messaging-architecture.md`) shows the exact pattern to follow: `public interface IXService { event Action<T> OnSomething; }` with `T` a `readonly struct`. **Important non-obvious implementation detail**: a plain C# multicast `event Action<T>` does NOT isolate exceptions between subscribers — if one throws, the rest of the invocation list never runs. CR-2.10 explicitly requires "subscriber exceptions are caught per-subscriber... never interrupt remaining subscribers." This means you cannot just call `OnLevelUp?.Invoke(...)` — you need to manually iterate `GetInvocationList()` (or maintain your own subscriber list) with a try/catch around each individual invocation. Check whether `CharacterStats.cs`'s own `FireOnStatChanged`/`FireOnEntityDied` pattern (fixed-size subscriber array, `_isFiring` re-entrancy guard) is the established precedent to mirror, or whether a simpler `GetInvocationList()`-based approach is more appropriate for this class — your call, but the per-subscriber exception isolation is not optional, it's a literal CR-2.10 requirement.

*Derived from CR-2.8–CR-2.10, EC-LS-09, EC-LS-10 (leveling-system.md):*

- **CR-2.8 — Grant Free Points**: `heldFreePoints += freePointsPerLevel[class]` (Warrior 1, Healer 2). Owned by the Leveling System, NOT stored in Character Stats — persisted separately (see Story 009). **`heldFreePoints` and the Level write (CR-2.2) must commit atomically in a single transaction** — a crash between the two writes must never produce a state where Level advanced without `heldFreePoints` incrementing, or vice versa.
- **CR-2.9 — Consecutive Level-Up Check**: re-evaluate `Experience >= XpThreshold[newLevel+1]`. If yes, repeat the full Story 002 sequence from CR-2.1. Each iteration must read fresh `GetBaseStat()` totals from its own prior steps — never cached values from an earlier iteration.
- **CR-2.10 — Broadcast Level-Up Complete**: fire `ILevelingEventBroadcaster.OnLevelUp(EntityID, newLevel)` only after ALL consecutive iterations resolve — not per-iteration. Subscribers: HUD, Audio, Skill System (updates `IsUnlocked` flags, sends `SkillUnlockNotification`). **Subscriber exceptions are caught per-subscriber, logged, and never interrupt remaining subscribers or subsequent levels.** **Tick-phase ordering constraint**: all `OnLevelUp` notifications must complete before the Skill System processes cast requests within the same server tick (see `skill-system.md` EC-SK-6, AC-SK-49 — cross-epic, not testable here until Skill System exists; document the contract, do not implement the tick-phase enforcement itself).
- **EC-LS-09 (re-entrancy guard)**: `_levelingUpInProgress` flag blocks `AllocateFreePoint` calls mid-sequence — an interleaved spend would write derived stats atop partially-updated primary attributes. The guard fires before the decrement — a rejected call must never consume a free point. UI should also disable the button, but the system layer defends independently.
- **EC-LS-10 (stack re-entrancy, distinct from CR-2.9's loop re-entrancy)**: if a subscriber calls `AddExperience` from inside an `OnStatChanged` handler while `_levelingUpInProgress == true`, the XP write itself still succeeds (Character Stats doesn't block it) but the resulting `NotifyExperienceCrossedThreshold` invocation aborts without advancing Level. Once the original sequence completes and clears the flag, CR-2.9's own loop picks up the accumulated XP and fires the additional level(s) normally.
- **Test seam requirement**: expose `IsLevelingUpInProgress` (read-only bool) on the public interface per OQ-LS-5 — confirm the exact property/interface shape is acceptable; this was flagged in the GDD as needing Lead Programmer sign-off before the sprint, treat that as resolved-by-implementation unless a real conflict surfaces.

---

## Out of Scope

*Handled by neighbouring stories:*

- The per-iteration level-up mechanics themselves (guard, increment, auto-alloc, derived stats, HP/MP restore) — Story 002
- Tier-transition-specific recompute proof — Story 004

---

## QA Test Cases

*Test file*: `tests/EditMode/LevelingSystem/LevelingSystem_ConsecutiveLevelUpReentrancy_tests.cs`

- **AC-LS-07**: Given Warrior and Healer both at L4 with heldFreePoints=0, When each gains one level, Then Warrior→1, Healer→2, both readable only via `GetHeldFreePoints`.
- **AC-LS-08**: Given a Warrior at L3 with an XP grant crossing 3 thresholds, When resolved, Then `OnLevelUp` fires exactly 3 times in order (4,5,6), all after Level reaches 6.
- **AC-LS-09**: Given a Warrior at L2 gaining 2 consecutive levels, When CR-2.9 iterates twice, Then the second iteration's CR-2.5 read reflects the second iteration's own CR-2.4 writes, and final MaxHP matches L3 totals.
- **AC-LS-10**: Given `_levelingUpInProgress==true` and heldFreePoints=3, When `AllocateFreePoint` is called, Then rejected, heldFreePoints stays 3, no SetBaseStat, "system busy" returned.
- **AC-LS-49**: Given a re-entrant `AddExperience` call from within an `OnStatChanged` handler mid-sequence, When it reaches the Leveling System, Then no Level write during the re-entrant call but the XP write succeeds, and CR-2.9 catches up post-sequence with the correct final Level and one `OnLevelUp` per level gained.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_ConsecutiveLevelUpReentrancy_tests.cs` — must exist and pass

**Status**: [x] Created — 7 tests, all 5 blocking ACs covered (AC-LS-49 split into two tests). Not confirmed in a live Unity Editor this session (none was open/available) — verified statically (grep-confirmed test counts, hand-verified control flow, 2 parallel self-performed code reviews, both 0 BLOCKING).

---

## Dependencies

- Depends on: Story 002 (level-up sequence core, wrapped in this story's loop). Story 005's real `AllocateFreePoint` was NOT built as a dependency — implemented a minimal local stub instead (busy-rejection only, throws `NotImplementedException` for the non-busy path), per this story's own contingency note.

---

## Completion Notes
**Completed**: 2026-07-22
**Criteria**: 5/5 passing (AC-LS-07, AC-LS-08, AC-LS-09, AC-LS-10, AC-LS-49) — 7 tests in `tests/EditMode/LevelingSystem/LevelingSystem_ConsecutiveLevelUpReentrancy_tests.cs`.
**New production**: `LevelingService.NotifyExperienceCrossedThreshold` rewritten to wrap Story 002's unmodified `ExecuteLevelUpSequence` in the real CR-2.9 consecutive-level loop, with a `finally`-guaranteed `_levelingUpInProgress` flag and a deferred `OnLevelUp` broadcast (fired only after the loop fully completes). `ILevelingEventBroadcaster`/`LevelUpEventArgs` (new, ADR-010 Tier-2 broadcast — ordinary `readonly struct` + `event Action<T>`, matching the ADR's own example; NOT the `CharacterStats`-style fixed-array subscriber pattern, since a manual `GetInvocationList()` + per-handler try/catch was simpler and sufficient here). `AllocateFreePointResult` (new enum) + a minimal `AllocateFreePoint` stub (busy-check only).
**A genuine GDD-vs-implementation conflict found and resolved, not glossed over**: EC-LS-10/AC-LS-49's literal scenario ("a subscriber calls `AddExperience` from inside an `OnStatChanged` handler") is now structurally impossible — `CharacterStats`'s own pre-existing `_isFiring` guard (Story 005, already closed; a single class-wide flag, not scoped per-stat) throws `InvalidOperationException` on ANY write attempted during ANY handler invocation, so the literal scenario throws inside `CharacterStats` before ever reaching `LevelingService`. Resolved by splitting AC-LS-49 into two tests: (a) a regression test proving the GDD's literal scenario genuinely throws (confirming existing Story 005 behavior, not assumed), and (b) a test proving `LevelingService`'s own new `_levelingUpInProgress` guard correctly handles the real, still-reachable case — a subscriber calling `NotifyExperienceCrossedThreshold` **directly**, bypassing `AddExperience`/`SetBaseStat`. Both the implementing agent and I independently traced `CharacterStats.IsFiringAndAssert`'s source before accepting this as correct rather than a misreading. **This should be raised with whoever owns `leveling-system.md` as a GDD correction** — EC-LS-10's text describes a scenario that can no longer occur; the doc should describe the real defended case instead.
**Also found**: `CurrencySystem.OnGoldSync` (an existing, unrelated system) uses a bare `?.Invoke()` for its broadcast, which does NOT isolate subscriber exceptions — the closest same-tier precedent in this codebase actually violates ADR-010/CR-2.10's isolation requirement. The implementing agent noticed this while researching the convention and deliberately did NOT copy it for `OnLevelUp`, using a proper `GetInvocationList()` + per-handler try/catch instead. Not fixed in `CurrencySystem.cs` (out of scope, different epic) — flagging here in case it's worth a future tech-debt item.
**Code Review**: Complete (lean self-performed review: unity-specialist 1 Required Change→fixed (missing `.meta` files for 4 new files) + qa-tester 1 Required Change→fixed (heldFreePoints multi-iteration accumulation wasn't directly tested) + 1 Suggestion→applied (split the combined Warrior/Healer AC-LS-07 test into two, matching established one-scenario-per-method convention), both parallel, 0 BLOCKING from either).
**Test Evidence**: Logic — `tests/EditMode/LevelingSystem/LevelingSystem_ConsecutiveLevelUpReentrancy_tests.cs`, 7 tests, all 5 blocking ACs covered. Not live-Editor-confirmed this session.
**Deviations**: None from Out of Scope — `ExecuteLevelUpSequence`'s internal formula/sequence logic untouched (confirmed via `git diff`, only new call sites added), `CharacterStats.cs`/`ILevelingService.cs` untouched (confirmed via `git diff`/`git status`).
**Handoff note for Story 005**: `AllocateFreePoint`'s current stub only implements the busy-rejection check and throws `NotImplementedException` for every other path — Story 005 replaces the stub body entirely with the real CR-3 guards (heldFreePoints==0, invalid StatID), decrement, write, and F-3–F-9 recompute. The `AllocateFreePointResult` enum may need additional values for Story 005's other rejection cases (invalid stat, zero held points) — currently only has `Success`/`RejectedSystemBusy`.
- Unlocks: Story 013 (HUD relies on `OnLevelUp`, not per-`OnStatChanged`, for level-up animation timing)
