# Story 004: Kill Resolution and Gold Distribution

> **Epic**: Loot Table System
> **Status**: Ready
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

**Control Manifest Rules (Core layer)**:
- Required: service interface naming `I[SystemName]Service` — ADR-010. enemy-ai.md writes `ILootTableSystem`; the code name is `ILootTableService`.
- Forbidden: calling game logic directly from NGO message handlers — ADR-010 (this entry point is called from the tick loop by Enemy AI)

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-10** [BLOCKING]: mob with `GoldMin = 40` / `GoldMax = 60`, the server draws `baseGold = 52`, winning party of N = 3 → `goldPerMember = floor(52 / 3) = 17`; `AddGold` is called exactly 3 times at 17g each; the 1g remainder is discarded. With `baseGold = 2` and N = 4: `goldPerMember = 0`; `AddGold` is **not** called; the authoring violation is logged.
- [ ] **AC-LT-20** [BLOCKING]: mob with `GoldMin = 10` / `GoldMax = 10` killed by a solo player (N = 1) → `AddGold(soloPlayerID, 10, GoldTransactionReason.MonsterDrop)` is called exactly once, in the same call as drop resolution. The same mob killed with an empty `damageRecord` → `AddGold` is not called for anyone.
- [ ] **AC-LT-5** [BLOCKING]: a mob that dies with an empty `damageRecord` produces no drops and no `AddGold` calls; resolution completes silently — zero items and zero gold is the expected result, not an error.
- [ ] **CR-LT-14**: every gold award uses `GoldTransactionReason.MonsterDrop`; N is the winning party's size at kill time; gold is applied in the same call that produces the pending drop list, whether or not any item is later picked up.
- [ ] **Entry point** (enemy-ai.md): `ILootTableService.ResolveMobDrop(EntityID mobEntityID, int tierShift)` exists with that signature.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-14, F-LT-1 and the enemy-ai.md contract.*

**Module conventions:** see Story 001.

**Flow of `ResolveMobDrop(mobEntityID, tierShift)`:**
1. Look the mob up through `IMobInfoProvider` (Story 003) → `MobTypeID`; get its table from the `LootTableRegistry` (Story 001).
2. Ask the party tag (Story 003) for the owner. No owner → return: no drops, no gold, nothing logged as an error.
3. Roll the table (Story 002) → pending drop list.
4. Draw `baseGold` uniformly from `[GoldMin, GoldMax]` inclusive — `rng.Next(GoldMin, GoldMax + 1)` on the injected PRNG.
5. `N` = number of members of the winning party (`IPartyService.GetPartyMembers(PartyID)` — add it to the interface declared in Story 003). `goldPerMember = baseGold / N` (integer division of non-negative ints equals the GDD's `floor`).
6. If `goldPerMember > 0`: `AddGold(member, goldPerMember, GoldTransactionReason.MonsterDrop)` for each member. If it is 0: do not call `AddGold` (it rejects 0 with `InvalidAmount`) and log the authoring violation.
7. Hand the pending drop list, the winning `PartyID` and the mob position to the drop hand-off (below).
8. Clear the mob's damage record.

**Drop hand-off.** Ground items do not exist until Story 005. In this story, expose the outcome of step 7 through one internal seam (e.g. an internal `IDropSink` with a no-op default) that Stories 005–006 implement. Tests observe the pending list through a test implementation of that seam.

**`tierShift` (decided 2026-10-02):** accept the parameter, do not apply it. The loot GDD has no rule for what a tier shift does (enemy-ai.md OQ-AI-1 is open). When `tierShift != 0`, log a single server warning that the shift was ignored; behaviour is otherwise identical to `tierShift = 0`.

**Guards (not GDD rules — caller-bug handling in the style of `IInventoryService`):** an unknown mob `EntityID`, or a `MobTypeID` with no table, logs a server error and returns with no drops and no gold. A non-success `GoldMutationResult` from `AddGold` is logged as a server error and the remaining members are still paid; the GDD defines no retry.

**Testing the 0-gold guard:** `baseGold = 2` needs a table with `GoldMin < 4`, which the Story 001 validator rejects. Build that table through a test seam that bypasses validation; do not weaken the validator.

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
  - Given: table `GoldMin = GoldMax = 10`; solo party
  - When: `ResolveMobDrop(mob, 0)`
  - Then: exactly one `AddGold(solo, 10, MonsterDrop)`; the drop sink received its call during the same `ResolveMobDrop`
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

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_KillResolution_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (roll), Story 003 (party tag, `IPartyService`, `IMobInfoProvider`).
- Unlocks: Story 005.
