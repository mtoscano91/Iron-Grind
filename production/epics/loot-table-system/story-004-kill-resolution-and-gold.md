# Story 004: Kill Resolution and Gold Distribution

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-14 (Gold Distribution), F-LT-1 (Gold per Member), CR-LT-4 (no attacker), Edge Cases ("If `floor(baseGold / N)` produces 0"). Entry point: `design/gdd/enemy-ai.md` — "Loot Table System" interaction (`ResolveMobDrop(EntityID mobEntityID, int tierShift)`).
**Requirement**: `TR-loot-006`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: None for loot logic (by design — see EPIC "Governing ADRs"). ADR-010: Event/Messaging Architecture (Accepted) governs injection.
**ADR Decision Summary**: Enemy AI calls the loot service directly through its injected interface (Tier 1); the loot service calls `ICurrencyService` and `IPartyService` the same way.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. No post-cutoff API.
**Performance**: Runs once per kill, far rarer than ticks. Allocations per kill: the drop list and whatever the party member lookup returns. No per-tick cost. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

**Control Manifest Rules (Core layer)**:
- Required: service interface naming `I[SystemName]Service` — ADR-010. enemy-ai.md writes `ILootTableSystem`; the code name is `ILootTableService`.
- Forbidden: calling game logic directly from NGO message handlers — ADR-010 (this entry point is called from the tick loop by Enemy AI)

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [x] **AC-LT-10** [BLOCKING]: mob with `GoldMin = 40` / `GoldMax = 60`, the server draws `baseGold = 52`, winning party of N = 3 → `goldPerMember = floor(52 / 3) = 17`; `AddGold` is called exactly 3 times at 17g each; the 1g remainder is discarded. With `baseGold = 2` and N = 4: `goldPerMember = 0`; `AddGold` is **not** called; the authoring violation is logged.
- [x] **AC-LT-20** [BLOCKING]: mob with `GoldMin = 10` / `GoldMax = 10` killed by a solo player (N = 1) → `AddGold(soloPlayerID, 10, GoldTransactionReason.MonsterDrop)` is called exactly once, in the same call as drop resolution. The same mob killed with an empty `damageRecord` → `AddGold` is not called for anyone.
- [x] **AC-LT-5** [BLOCKING]: a mob that dies with an empty `damageRecord` produces no drops and no `AddGold` calls; resolution completes silently — zero items and zero gold is the expected result, not an error.
- [x] **CR-LT-14**: every gold award uses `GoldTransactionReason.MonsterDrop`; N is the winning party's size at kill time; gold is applied in the same call that produces the pending drop list, whether or not any item is later picked up.
- [x] **Entry point** (enemy-ai.md): `ILootTableService.ResolveMobDrop(EntityID mobEntityID, int tierShift)` exists with that signature.
- [x] **Damage entry point** *(added at readiness, 2026-10-02)*: `ILootTableService.RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage)` forwards to the party tag tracker (Story 003), so combat code depends on one interface.
- [x] **Empty owner party** *(decided at readiness, 2026-10-02 — not covered by the GDD)*: if the tag owner's party has no members at kill time, nothing is distributed — no drops, no gold, no PRNG draw — a server warning is logged, and the mob's damage record is cleared. No other party inherits the kill.
- [x] **Record cleared**: after `ResolveMobDrop` returns, the mob has no damage record (a second call for the same mob distributes nothing).

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-14, F-LT-1 and the enemy-ai.md contract.*

**Module conventions:** see Story 001.

**Composition (settled at readiness, 2026-10-02):** `LootTableService : ILootTableService`, constructed with the `LootTableRegistry` (Story 001), a `PartyTagTracker` (Story 003), `IPartyService`, `IMobInfoProvider`, `ICurrencyService`, the injected `System.Random`, and an `ILootDropSink`. It does **not** need the `LootEquipmentCache` — classification starts in Story 006.

> **Changed at code review, 2026-10-02:** the damage record is now cleared at step 3, right after the member lookup and before anything is rolled or paid — not at step 9. A kill is therefore resolved at most once even if the currency service or the drop sink throws or calls back in. A gold share of zero **or less** is treated as non-payable. `ILootTableService` also gained `ClearMob(EntityID)` (for mobs removed without a kill), and `LootTableService` gained `Clear()` (zone teardown).

**Flow of `ResolveMobDrop(mobEntityID, tierShift)`:**
1. Look the mob up through `IMobInfoProvider` → `MobTypeID`; get its table from the `LootTableRegistry`. Unknown mob or no table → server error, clear the record, return.
2. Ask the tag tracker for the owner. No owner → return silently: no drops, no gold, nothing logged.
3. `members = IPartyService.GetPartyMembers(owner)`. Empty → server warning, clear the record, return (no PRNG draw).
4. If `tierShift != 0`, log one server warning that the shift is ignored.
5. Roll the table (`LootDropRoller.Roll`) → pending drop list.
6. Draw `baseGold` uniformly from `[GoldMin, GoldMax]` inclusive — `rng.Next(GoldMin, GoldMax + 1)` on the injected PRNG, after the roll.
7. `N = members.Count`; `goldPerMember = baseGold / N` (integer division of non-negative ints equals the GDD's `floor`). If `goldPerMember > 0`: `AddGold(member, goldPerMember, GoldTransactionReason.MonsterDrop)` for each member. If it is 0: do not call `AddGold` (it rejects 0 with `InvalidAmount`) and log the authoring violation as a server error.
8. If the drop list is not empty, hand it, the winning `PartyID` and the mob position to the drop sink. An empty list is not handed on.
9. Clear the mob's damage record (`PartyTagTracker.ClearMob`).

**`IPartyService.GetPartyMembers(PartyID)`** — add it to the interface from Story 003, returning `IReadOnlyList<CharacterID>`: the party's actual members in join order, with no empty slots, and an empty list for an unknown party. party-system.md stores membership as four slots with empty ones set to `CharacterID(0)` and does not say whether its `GetPartyMembers` returns them; note in the doc comment that the Party System GDD must confirm this contract.

**Drop hand-off.** Ground items do not exist until Story 005. Expose the outcome of step 8 through a `public interface ILootDropSink` with one method (mob `EntityID`, winning `PartyID`, the drop list, the mob position). Stories 005–006 implement it; tests use a recording implementation. It is public because it appears in the public constructor of `LootTableService`.

**Log levels:** unknown mob, missing table, and a zero gold share are server errors (`Debug.LogError`); an ignored `tierShift` and an empty owner party are warnings (`Debug.LogWarning`).

**`tierShift` (decided 2026-10-02):** accept the parameter, do not apply it. The loot GDD has no rule for what a tier shift does (enemy-ai.md OQ-AI-1 is open). When `tierShift != 0`, log a single server warning that the shift was ignored; behaviour is otherwise identical to `tierShift = 0`.

**Guards (not GDD rules — caller-bug handling in the style of `IInventoryService`):** an unknown mob `EntityID`, or a `MobTypeID` with no table, logs a server error and returns with no drops and no gold. A non-success `GoldMutationResult` from `AddGold` is logged as a server error and the remaining members are still paid; the GDD defines no retry.

**Testing the 0-gold guard:** `baseGold = 2` cannot come from a valid table (`GoldMin ≥ 4`, Story 001), and the registry has no way to skip validation — do not add one. Keep a valid table and have the test's fixed-`Next` PRNG return 2; the service uses whatever `rng.Next` returns. *(Revised at readiness, 2026-10-02.)*

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 005**: spawning ground items for the pending drop list.
- **Story 006**: assigning Common drops; **Story 010**: Rare drops.
- **Story 011**: the auction gold pool (F-LT-2).
- Applying a non-zero `tierShift` — needs a loot GDD amendment (enemy-ai.md OQ-AI-1).
- Raising `MobEventBus.MobDied` — Enemy AI does that after this call returns.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` (real `CurrencySystem` with registered characters, or a recording `ICurrencyService` fake; stub `IPartyService` and `IMobInfoProvider`; a `System.Random` subclass that returns a fixed `Next` value)

- **AC-LT-10**: gold split
  - Given: table `GoldMin = 40`, `GoldMax = 60`; PRNG fixed so the draw is 52; winning party of 3
  - When: `ResolveMobDrop(mob, 0)`
  - Then: exactly 3 `AddGold` calls, each `(member, 17, MonsterDrop)`
  - Edge cases: `baseGold = 2` (validation-bypassing table), N = 4 → 0 `AddGold` calls and a logged violation (`LogAssert.Expect`)

- **AC-LT-20**: solo full amount
  - Given: table `GoldMin = GoldMax = 10` with one `DropChance = 1.0` entry; solo party
  - When: `ResolveMobDrop(mob, 0)`
  - Then: exactly one `AddGold(solo, 10, MonsterDrop)`; the drop sink received its call (that one item, the solo `PartyID`, the mob position) during the same `ResolveMobDrop`
  - Edge cases: same mob with no recorded damage → 0 `AddGold` calls

- **AC-LT-5**: no attacker
  - Given: a mob with an empty damage record; a table with one `DropChance = 1.0` entry
  - When: `ResolveMobDrop(mob, 0)`
  - Then: the drop sink receives nothing; 0 `AddGold` calls; no error or warning is logged
  - Edge cases: none

- **CR-LT-14**: party size at kill time
  - Given: winning party of 2; draw 51
  - When: resolved
  - Then: 2 `AddGold` calls of 25 each
  - Edge cases: a table with all entries missing → gold is still paid

- **Entry point / `tierShift`**
  - Given: a valid kill
  - When: `ResolveMobDrop(mob, 2)`
  - Then: drops and gold are identical to `tierShift = 0` with the same PRNG state; one warning is logged
  - Edge cases: unknown mob `EntityID` → server error logged, no drops, no gold

- **Damage entry point**
  - Given: a mob with `MaxHP = 300` and a valid table; a solo party
  - When: `ILootTableService.RecordDamage(mob, solo, 50)`, then `ResolveMobDrop(mob, 0)`
  - Then: the solo party is paid (the damage reached the tracker)
  - Edge cases: none

- **Empty owner party**
  - Given: Party A recorded damage on the mob; the party stub then returns an empty member list for A; a table with one `DropChance = 1.0` entry; a counting PRNG
  - When: `ResolveMobDrop(mob, 0)`
  - Then: 0 `AddGold` calls; the drop sink receives nothing; the PRNG made 0 draws; one warning is logged
  - Edge cases: none

- **Record cleared**
  - Given: a resolved kill that paid gold
  - When: `ResolveMobDrop(mob, 0)` is called a second time
  - Then: no further `AddGold` calls and nothing more reaches the drop sink
  - Edge cases: none

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 20 test methods (20 NUnit cases), all 8 criteria covered

---

## Dependencies

- Depends on: Story 002 (roll), Story 003 (party tag, `IPartyService`, `IMobInfoProvider`).
- Unlocks: Story 005.

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 8/8 passing (0 deferred)
**Deviations**: Advisory only — `tierShift` is accepted and not applied (user decision; enemy-ai.md OQ-AI-1 open); an empty owner party distributes nothing (user decision at readiness, not in the GDD); the damage record is cleared before rolling and paying, not last (code review — at-most-once resolution); a gold share of zero or less is non-payable (code review); `ILootTableService.ClearMob` and `LootTableService.Clear()` added beyond the story's API (code review); `IPartyService.GetPartyMembers` contract (real members only, snapshot) is this module's assumption until party-system.md confirms it; `TR-loot-006` is not in `tr-registry.yaml` (registry is empty)
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` (20 test methods, 20 NUnit cases; recording currency service, stub party and mob providers, recording drop sink, fixed-`Next` PRNG). Full EditMode suite 1220/1220 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `LootTableService.cs` and siblings returned CHANGES REQUIRED (record cleared last; share guard `== 0`; empty-party clearing untested); all 3 required changes and the suggestions applied; suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-048 extended (empty-owner-party rule and the `GetPartyMembers` contract are not in the design docs)
**Note for callers (Enemy AI)**: a kill clears the mob's record first, so damage recorded on a mob after it has died starts a fresh record — do not record damage on dead mobs, and call `ILootTableService.ClearMob` when a mob despawns without a kill
**Note for Stories 005–006**: implement `ILootDropSink`; it is never called with an empty list
