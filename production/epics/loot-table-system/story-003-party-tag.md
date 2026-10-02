# Story 003: Party Tag — Damage Record, Threshold Lock, Fallback

> **Epic**: Loot Table System
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3–4 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-3 (Damage Accumulation), CR-LT-4 (Threshold Lock, fallback, no attacker), F-LT-3 (Tag Threshold), Edge Cases (one-shot mob; two parties crossing in one tick)
**Requirement**: `TR-loot-003`, `TR-loot-004`
*(Placeholder IDs — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: None for loot logic (by design — see EPIC "Governing ADRs"). ADR-010: Event/Messaging Architecture (Accepted) governs injection.
**ADR Decision Summary**: The Party System and mob data are reached through injected interfaces (Tier 1). Neither system has code yet, so this story declares the narrow consumer-side interfaces it needs and tests against stubs (EPIC Definition of Done — mock-provider precedent of `PartyDisbandCoordinator`).

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. Time is the server tick from an injected `Func<uint>` (precedent: `InventoryService`) — never `Time.time` or `DateTime`.
**Performance**: `RecordDamage` runs on every damage event. No heap allocation per call once the attacking party has an entry for that mob; the mob's tag threshold is computed once, on its first recorded hit, not per call. Lookups only. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

**Control Manifest Rules (Core layer)**:
- Required: service interface naming `I[SystemName]Service` — ADR-010. The GDDs write `IPartySystem`; the code name is `IPartyService` (as `IInventoryService` is to the GDDs' `IInventorySystem`).
- Forbidden: shared mutable state polling between systems — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [x] **AC-LT-3** [BLOCKING]: for `MaxHP = 120` and `TAG_THRESHOLD_FRACTION = 0.33`, `tagThreshold = 40`. For `MaxHP = 1`, `tagThreshold = 1` — the threshold is never zero.
- [x] **AC-LT-4** [BLOCKING]: for a mob with `MaxHP = 300` (`tagThreshold = 99`) attacked by two parties, when Party A's damage event brings its cumulative damage to ≥ 99 before Party B's does, the tag locks to Party A in that same call; Party B's later damage — even if larger — does not change ownership. **Fallback**: if the mob dies before any party crosses the threshold, the party with the highest cumulative damage wins; ties break by earliest `firstDamageTick`.
- [x] **CR-LT-3**: damage is accumulated per party, not per character — two members of one party add to the same `damageRecord` entry; a solo player is a party of one with its own `PartyID`.
- [x] **CR-LT-4 no attacker**: a mob with an empty `damageRecord` has no tag owner.
- [x] **Edge case — same-tick crossing**: when two parties both cross the threshold in the same server tick, the party whose damage event is processed first owns the tag.
- [x] **Zero damage** *(decided at readiness, 2026-10-02 — not covered by the GDD)*: a hit with `finalDamage = 0` creates no `damageRecord` entry, so a party that dealt no damage never locks or wins a tag.
- [x] **Fallback double tie** *(decided at readiness, 2026-10-02 — not covered by the GDD)*: when the fallback finds two parties equal on both cumulative damage and `firstDamageTick`, the party whose first damage event was recorded earlier wins — the same principle as the same-tick crossing rule.
- [x] **Unknown mob**: `RecordDamage` for a mob `EntityID` the mob provider does not know logs a server error and records nothing.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-3, CR-LT-4, F-LT-3.*

**Module conventions:** see Story 001.

**Consumer-side types declared here (no upstream code exists):**
- `PartyID` — `readonly struct` wrapping `uint`, `0` = uninitialized sentinel (entities.yaml; party-system.md CR-PS-1). Owned by the Party System; declared here as first consumer, like `ItemID` in `IronGrind.CharacterStats`.
- `IPartyService` — this story needs only `PartyID GetPartyID(CharacterID)` (party-system.md Interactions table). Later stories add members.
- `IMobInfoProvider` — `bool TryGetMob(EntityID mobEntityId, out MobInfo info)` where `MobInfo` carries `MobTypeID`, `int MaxHP` and the mob's world position. It stands in for Zone Instancing's entity registry plus Enemy AI's `IMobDefinitionRegistry` (mob-spawning.md Dependencies). Stories 004 and 005 use the other fields.

**API:** `RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage)`, called once per damage event with `DamageResult.FinalDamage`. It resolves the attacker's `PartyID`, adds to `damageRecord[partyId].cumulativeDamage`, sets `firstDamageTick` on the party's first hit, and locks the tag if this call takes the party to or past the threshold while no tag is locked. `TryGetTagOwner(EntityID mobEntityId, out PartyID)` returns the locked party, or applies the fallback (highest cumulative damage; ties by earliest `firstDamageTick`), or returns `false` for an empty record.

> **Superseded at code review, 2026-10-02:** the threshold is now computed in exact integer arithmetic from `LootTableConstants.TAG_THRESHOLD_PERMILLE = 330` — `(maxHp × 330 + 999) / 1000` — not in `double`. `double` is exact for 0.33 across MaxHP 1–9999 but not for other values in the knob's safe range (30 × 0.10 gives a ceiling of 4 instead of 3). A MaxHP below 1 logs a server error and uses a threshold of 1. The paragraph below is kept as the original reasoning.

**The tag threshold must be computed in `double`.** F-LT-3 is written `Mathf.CeilToInt(mob.MaxHP × TAG_THRESHOLD_FRACTION)`, but in single precision `300 × 0.33f = 99.0000076…`, which `Mathf.CeilToInt` rounds to **100** — AC-LT-4 requires **99**. Declare `TAG_THRESHOLD_FRACTION` as a `double` constant `0.33` and compute `(int)System.Math.Ceiling(maxHp * TAG_THRESHOLD_FRACTION)`: that yields 99 for 300, 40 for 120, 1 for 1 and 3300 for 9999, matching every value in the GDD. This departs from the literal `Mathf.CeilToInt` wording of F-LT-3; the GDD needs a wording fix (flagged at story creation, 2026-10-02).

**Lock timing:** the lock is evaluated inside `RecordDamage`, so it fires in the same tick as the threshold-crossing hit. Events within a tick are processed in call order — the first crossing call wins.

**Guards in `RecordDamage` (first match wins):** `finalDamage == 0` → return silently, nothing recorded (a normal outcome, no log). Mob unknown to `IMobInfoProvider` → log a server error, nothing recorded. Attacker's `PartyID` is `0` (the uninitialized sentinel, party-system.md CR-PS-1) → log a server error, nothing recorded. The last two are caller bugs, handled the way `IInventoryService` handles an unregistered character.

**Fallback order:** keep each mob's party records in first-recorded order and scan them in that order, replacing the current best only on strictly higher damage, or equal damage with a strictly earlier `firstDamageTick`. A full tie then keeps the earlier-recorded party without any extra bookkeeping.

**Threshold caching:** read the mob's `MaxHP` from the provider and compute the threshold once, when the mob's record is created on its first recorded hit.

**Lifetime:** records are per mob `EntityID`. Provide a way to drop a mob's record (`ClearMob`); Story 004 calls it after resolution.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 004**: using the tag owner at kill time (drops, gold); clearing the record after resolution.
- The damage pipeline that calls `RecordDamage` — Damage Calculation / combat epics. How an attacking `EntityID` maps to a `CharacterID` is theirs.
- Party membership changes mid-fight (a member leaving or joining while a record exists) — not defined by the loot GDD.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_PartyTag_integration_tests.cs` (stub `IPartyService`, stub `IMobInfoProvider`, fake tick counter)

- **AC-LT-3**: threshold values
  - Given: mobs with `MaxHP` 120 and 1
  - When: the threshold is computed
  - Then: 40 and 1
  - Edge cases: `MaxHP = 300` → 99; `MaxHP = 9999` → 3300

- **AC-LT-4**: threshold lock
  - Given: mob `MaxHP = 300`; Party A and Party B (stub maps characters to parties)
  - When: A deals 60, B deals 50, A deals 39 (A = 99), B deals 200
  - Then: `TryGetTagOwner` returns Party A
  - Edge cases: A at 98 does not lock (owner by fallback is still the higher damage); A reaching exactly 99 locks

- **AC-LT-4 fallback**
  - Given: mob `MaxHP = 300`; A deals 30 at tick 10, B deals 50 at tick 12; no party reaches 99
  - When: `TryGetTagOwner`
  - Then: Party B (highest cumulative)
  - Edge cases: A deals 40 at tick 10 and B deals 40 at tick 12 → Party A (earlier `firstDamageTick`)

- **CR-LT-3**: per-party accumulation
  - Given: characters C1 and C2 both in Party A; mob `MaxHP = 300`
  - When: C1 deals 50, C2 deals 49
  - Then: Party A is locked as owner (99 cumulative)
  - Edge cases: a solo character with its own `PartyID` accumulates separately from Party A

- **CR-LT-4 no attacker**
  - Given: a mob with no recorded damage
  - When: `TryGetTagOwner`
  - Then: returns `false`
  - Edge cases: an unknown mob `EntityID` → `false`

- **Same-tick crossing**
  - Given: mob `MaxHP = 300`; the tick counter fixed at one value
  - When: `RecordDamage(A, 99)` then `RecordDamage(B, 150)` in that tick
  - Then: Party A owns the tag
  - Edge cases: none

- **Zero damage**
  - Given: mob `MaxHP = 300`
  - When: `RecordDamage(A, 0)` and nothing else
  - Then: `TryGetTagOwner` returns `false`; no error is logged
  - Edge cases: `RecordDamage(A, 0)` at tick 5, then `RecordDamage(B, 10)` at tick 6 and `RecordDamage(A, 10)` at tick 7 → Party B wins the fallback (A's zero hit set no `firstDamageTick`)

- **Fallback double tie**
  - Given: mob `MaxHP = 300`; the tick counter fixed at one value
  - When: `RecordDamage(A, 40)` then `RecordDamage(B, 40)`
  - Then: `TryGetTagOwner` returns Party A
  - Edge cases: the reverse call order → Party B

- **Unknown mob**
  - Given: an `EntityID` the mob provider does not know
  - When: `RecordDamage(unknown, A, 50)`
  - Then: one server error is logged (`LogAssert.Expect`); `TryGetTagOwner(unknown)` returns `false`
  - Edge cases: an attacker whose `PartyID` is `0` → one server error logged, nothing recorded

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_PartyTag_integration_tests.cs` — must exist and pass.

**Status**: [x] Created — 27 test methods (32 NUnit cases), all 8 criteria covered

---

## Dependencies

- Depends on: None.
- Unlocks: Story 004.

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 8/8 passing (0 deferred)
**Deviations**: Advisory only — the tag threshold is computed in exact integer arithmetic from `TAG_THRESHOLD_PERMILLE = 330`, not the GDD's `Mathf.CeilToInt(MaxHP × TAG_THRESHOLD_FRACTION)` (float gives 100 for MaxHP 300 where AC-LT-4 requires 99; results match every value the GDD states); three rules not in the GDD were decided at readiness (zero damage records nothing; full fallback tie → first-recorded party; unknown mob / uninitialized `PartyID` → server error, nothing recorded); MaxHP < 1 logs a server error and uses threshold 1 (code review); `PartyTagTracker.Clear()` added for zone teardown (code review, beyond the story's API); TR-loot-003/004 are not in `tr-registry.yaml` (registry is empty)
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_PartyTag_integration_tests.cs` (27 test methods, 32 NUnit cases, stub `IPartyService` / `IMobInfoProvider`, fake tick). Full EditMode suite 1200/1200 passed in Unity 6000.3.10f1 batch mode, 0 compile errors
**Code Review**: Complete — `/code-review` on `PartyTagTracker.cs` and siblings returned CHANGES REQUIRED (invalid MaxHP unguarded); the required change and 6 of 7 suggestions applied (integer threshold, `Clear()`, first-hit-tick test, `ClearMob`/`Clear` tests, constructor null-guard tests, PascalCase test names, `CreateRecord` helper); the saturating-add test was not written — the running total is not observable once the tag locks; suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-048 in `docs/tech-debt-register.md` — loot GDD F-LT-3 wording, the registry's `TAG_THRESHOLD_FRACTION` entry, and the three readiness rules are not reflected in the design docs
**Note for Story 004**: call `PartyTagTracker.ClearMob` after resolving a kill; every other path that removes a mob must call it too
