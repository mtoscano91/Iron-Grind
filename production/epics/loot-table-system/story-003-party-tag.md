# Story 003: Party Tag — Damage Record, Threshold Lock, Fallback

> **Epic**: Loot Table System
> **Status**: Ready
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

**Control Manifest Rules (Core layer)**:
- Required: service interface naming `I[SystemName]Service` — ADR-010. The GDDs write `IPartySystem`; the code name is `IPartyService` (as `IInventoryService` is to the GDDs' `IInventorySystem`).
- Forbidden: shared mutable state polling between systems — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-3** [BLOCKING]: for `MaxHP = 120` and `TAG_THRESHOLD_FRACTION = 0.33`, `tagThreshold = 40`. For `MaxHP = 1`, `tagThreshold = 1` — the threshold is never zero.
- [ ] **AC-LT-4** [BLOCKING]: for a mob with `MaxHP = 300` (`tagThreshold = 99`) attacked by two parties, when Party A's damage event brings its cumulative damage to ≥ 99 before Party B's does, the tag locks to Party A in that same call; Party B's later damage — even if larger — does not change ownership. **Fallback**: if the mob dies before any party crosses the threshold, the party with the highest cumulative damage wins; ties break by earliest `firstDamageTick`.
- [ ] **CR-LT-3**: damage is accumulated per party, not per character — two members of one party add to the same `damageRecord` entry; a solo player is a party of one with its own `PartyID`.
- [ ] **CR-LT-4 no attacker**: a mob with an empty `damageRecord` has no tag owner.
- [ ] **Edge case — same-tick crossing**: when two parties both cross the threshold in the same server tick, the party whose damage event is processed first owns the tag.

---

## Implementation Notes

*No ADR Implementation Guidelines apply; derived from CR-LT-3, CR-LT-4, F-LT-3.*

**Module conventions:** see Story 001.

**Consumer-side types declared here (no upstream code exists):**
- `PartyID` — `readonly struct` wrapping `uint`, `0` = uninitialized sentinel (entities.yaml; party-system.md CR-PS-1). Owned by the Party System; declared here as first consumer, like `ItemID` in `IronGrind.CharacterStats`.
- `IPartyService` — this story needs only `PartyID GetPartyID(CharacterID)` (party-system.md Interactions table). Later stories add members.
- `IMobInfoProvider` — `bool TryGetMob(EntityID mobEntityId, out MobInfo info)` where `MobInfo` carries `MobTypeID`, `int MaxHP` and the mob's world position. It stands in for Zone Instancing's entity registry plus Enemy AI's `IMobDefinitionRegistry` (mob-spawning.md Dependencies). Stories 004 and 005 use the other fields.

**API:** `RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage)`, called once per damage event with `DamageResult.FinalDamage`. It resolves the attacker's `PartyID`, adds to `damageRecord[partyId].cumulativeDamage`, sets `firstDamageTick` on the party's first hit, and locks the tag if this call takes the party to or past the threshold while no tag is locked. `TryGetTagOwner(EntityID mobEntityId, out PartyID)` returns the locked party, or applies the fallback (highest cumulative damage; ties by earliest `firstDamageTick`), or returns `false` for an empty record.

**The tag threshold must be computed in `double`.** F-LT-3 is written `Mathf.CeilToInt(mob.MaxHP × TAG_THRESHOLD_FRACTION)`, but in single precision `300 × 0.33f = 99.0000076…`, which `Mathf.CeilToInt` rounds to **100** — AC-LT-4 requires **99**. Declare `TAG_THRESHOLD_FRACTION` as a `double` constant `0.33` and compute `(int)System.Math.Ceiling(maxHp * TAG_THRESHOLD_FRACTION)`: that yields 99 for 300, 40 for 120, 1 for 1 and 3300 for 9999, matching every value in the GDD. This departs from the literal `Mathf.CeilToInt` wording of F-LT-3; the GDD needs a wording fix (flagged at story creation, 2026-10-02).

**Lock timing:** the lock is evaluated inside `RecordDamage`, so it fires in the same tick as the threshold-crossing hit. Events within a tick are processed in call order — the first crossing call wins.

**Lifetime:** records are per mob `EntityID`. Provide a way to drop a mob's record (`ClearMob`); Story 004 calls it after resolution.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 004**: using the tag owner at kill time (drops, gold); clearing the record after resolution.
- The damage pipeline that calls `RecordDamage` — Damage Calculation / combat epics. How an attacking `EntityID` maps to a `CharacterID` is theirs.
- A tie on **both** cumulative damage and `firstDamageTick` in the fallback — the GDD defines no further tie-break; do not add or test one.
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

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_PartyTag_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None.
- Unlocks: Story 004.
