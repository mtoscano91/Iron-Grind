# Story 001: The Persistence Contract — `ICharacterPersistence`, Result Types, `CharacterRecord`

> **Epic**: Character Persistence
> **Status**: Complete — 2026-10-12 (COMPLETE WITH NOTES; see Completion Notes)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 5 hours (4 as written, plus the two id structs)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-1 (the interface and its result types), CR-CP-2 (the character record: 23 logical fields, the fields that are not persisted).
**Requirement**: `TR-cp-001`, `TR-cp-002`
*(Epic placeholders: `docs/architecture/tr-registry.yaml` has no entry for this system. Accepted at `/story-readiness` 2026-10-12; the ids become real at the next `/architecture-review`.)*

**ADR Governing Implementation**: ADR-006 (Persistence Layer); ADR-011 (Asynchronous Persistence in the Server Tick Loop) Decision 3; ADR-012 Decision 1; ADR-010.
**ADR Decision Summary**: one async interface, `ICharacterPersistence`, unchanged by ADR-006 and ADR-011; its implementation and everything that names a server-only library live in `IronGrind.ServerLogic`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — plain C# types, no engine API.
**Performance**: no impact expected — type declarations and one `Clone`, nothing on the tick path.

**Control Manifest Rules (this layer)**:
- Required: service interface naming `I[SystemName]Service`, with `ICharacterPersistence` a grandfathered exception — ADR-010
- Required: a type that computes, validates or mutates authoritative game state is in `IronGrind.ServerLogic`; wire and shared value types are in `IronGrind.Foundation` — ADR-012
- Forbidden: Entity Framework Core for character persistence — ADR-006

---

## Acceptance Criteria

- [ ] **Interface**: `ICharacterPersistence` declares exactly the nine methods of CR-CP-1 — `LoadCharacter`, `SaveIrreversibleOutcome`, `SaveSession` (two overloads, the second with `float hpOverride`), `CreateStub`, `BeginPurchase`, `CompletePurchase`, `RefundPurchase`, `LoadOutstandingPurchases` — each returning a `Task` or `Task<T>` and taking a `CancellationToken` as its last parameter. `CreateStub` takes `byte classType` (decision 1).
- [ ] **Id types**: `AccountID` and `ZoneID` are `readonly struct`s over `uint` in `IronGrind.Foundation`, beside `CharacterID` and with its shape (`IEquatable<T>`, a static `Invalid` equal to 0). `ZoneID.Invalid` is the first-login sentinel of CR-CP-9.
- [ ] **Enums**: six `byte` enums with exactly these members; a test pins every value.
  - `CharacterLoadCode`: `Success = 0`, `CharacterNotFound = 1`, `AccountMismatch = 2`, `CorruptRecord = 3`, `ItemDatabaseMiss = 4`, `DatabaseError = 255`
  - `CharacterSaveResult`: `Success = 0`, `CharacterNotFound = 1`, `ConcurrencyConflict = 2`, `DatabaseError = 255`
  - `PendingPurchaseResult`: `Success = 0`, `InsufficientGold = 1`, `ConcurrencyConflict = 2`, `DatabaseError = 255`
  - `IrreversibleOutcomeTrigger`: `LevelUp = 0`, `EnhancementResult = 1`, `RespecCommit = 2`, `ItemConsumption = 3`
  - `SessionEndReason`: `CleanLogout = 0`, `SessionTTLExpiry = 1`, `ZoneClosed = 2`
  - `PendingPurchaseState`: `GoldDebited = 0`, `Refunded = 1`, `Completed = 2`
- [ ] **Load result**: `CharacterLoadResult` is a struct carrying `Code: CharacterLoadCode` and `LastZoneID: ZoneID`.
- [ ] **Purchase record**: `PendingPurchaseRecord` is a struct with `Id: long`, `CharId: CharacterID`, `RequestId: uint`, `ItemId: uint`, `Quantity: int`, `TotalCost: uint`, `State: PendingPurchaseState`.
- [ ] **Record**: `CharacterRecord` is a sealed class holding these members and no others that carry character state (CR-CP-2; the GDD calls them "23 logical fields" — the list below, not the count, is what the test checks):
  - identity: `CharacterID: CharacterID`, `AccountID: AccountID`, `CharacterName: string`, `ClassType: byte`
  - progression: `Level`, `Experience`, `HeldFreePoints` — `int`
  - attributes: `STR`, `DEX`, `VIT`, `INT` — `int`
  - base stats: `MaxHP`, `MaxMP`, `AttackPower`, `Defense`, `MagicDefense` — `int`; `CritChance`, `CritMultiplier`, `AttackRange`, `AttackSpeedMultiplier`, `MovementSpeed` — `float`
  - vitals: `CurrentHP`, `CurrentMP` — `float`
  - currency and zone: `GoldBalance: uint`, `GoldVersion: uint`, `LastZoneID: ZoneID`
  - gear: 7 slots, each `ItemID` + `EnhancementLevel: byte`
  - inventory: 20 slots, each item `ItemID`, count `int`, enhancement level `byte`
  - bookkeeping: `SaveVersion: uint`, `CreatedAtUtc: DateTime`, `LastSavedAtUtc: DateTime`

  It holds none of the four not-persisted items (buff modifiers, equipment modifier values, `PrestigeBand` / appearance flags, `IsTransitioning`).
- [ ] **Copy**: `CharacterRecord.Clone()` returns a record whose arrays are new: changing the clone's gear or inventory slots does not change the original (needed by ADR-011's snapshot rule).
- [ ] **Constants**: `CHARACTER_LOAD_TIMEOUT_SECONDS = 10` (CR-CP-8); `PERSISTENCE_WRITE_TIMEOUT_SECONDS = 5`, `PERSISTENCE_WRITE_WARNING_MS = 50`, `PERSISTENCE_WRITE_CRITICAL_MS = 100` (ADR-006 Decision 4); a new `GEAR_SLOT_COUNT = 7`, with a test that it equals the number of `GearSlot` members; the inventory slot count is `InventoryConstants.INVENTORY_SLOT_COUNT`, not redefined.
- [ ] **Assembly**: the interface, the record, the result types, the enums and the constants are in `IronGrind.ServerLogic`; only `AccountID` and `ZoneID` are in `IronGrind.Foundation`. The assembly-boundary tests pass unedited.
- [ ] **Doc comments** on every public type and member.

---

## Implementation Notes

- The interface text is CR-CP-1's, except `CreateStub`'s `classType` (decision 1). ADR-011 Decision 3 states that the interface "is unchanged"; tick code consumes it through `ITickCompletionQueue.Track`, never with `await`.
- `CharacterID` exists (`src/Foundation/Currency/CharacterID.cs`); `ItemID` exists (`src/Foundation/CharacterStats/ItemID.cs`); `GearSlot` has seven members, 0–6 (`src/Foundation/ItemDatabase/GearSlot.cs`).
- `PendingPurchaseRecord` and the purchase enums are declared here so the interface compiles; their behaviour is Story 011.

**Decisions — `/story-readiness` 2026-10-12 (user)**

1. **Id types.** `AccountID` and `ZoneID` are defined in this story. `ClassType` stays a raw `byte`, as `LevelingService.RegisterPlayerClassType(EntityID, byte)` already has it; no class enum is defined here. **Deviation from CR-CP-1**, whose signature names a `ClassType` type — to be noted in the GDD when a class enum exists.
2. **Placement.** Everything in `IronGrind.ServerLogic` except the two id structs. `CharacterLoadCode` moves to `Foundation` only if a client-side flow comes to need it. *(ADR-012's placement questions were not re-read for this decision; it rests on the manifest rule quoted above.)*
3. **`CharacterRecord`** is a sealed class with an explicit deep `Clone()`.

---

## Out of Scope

- Story 002: the record store seam and `CreateStub`'s values
- Stories 003–010: every behaviour of the interface
- Story 011: purchase behaviour
- Stories 012–016: the database
- A class-type enum, and the GDD wording for "23 logical fields" and `ClassType classType` (design fixes)

---

## QA Test Cases

- **Enums** — Given the six enums, When each member is cast to `byte`, Then it equals the value listed in the criterion, and no enum has a member outside the list. Edge: `DatabaseError` is 255 in all three result enums.
- **Interface shape** — Given `typeof(ICharacterPersistence)`, When its methods are read by reflection, Then there are nine, each returns `Task` or `Task<T>`, each has a `CancellationToken` last, and `CreateStub`'s third parameter is `byte`.
- **Id types** — Given `default(AccountID)` and `default(ZoneID)`, Then each equals its `Invalid`; two ids built from the same `uint` are equal, from different ones not.
- **Record members** — Given `typeof(CharacterRecord)`, When its public members are read by reflection, Then every member of the criterion's list is present with its type, and no member is named for a not-persisted item. Given a new record, Then gear has 7 entries and inventory 20.
- **Purchase record and load result** — Then the members and types are those of the criteria.
- **Copy** — Given a record with gear slot 0 and inventory slot 3 set, When it is cloned and the clone's slots are changed, Then the original is unchanged; scalar members of the clone equal the original's.
- **Constants** — Then the five constants have the values above, and `GEAR_SLOT_COUNT` equals `Enum.GetValues(typeof(GearSlot)).Length`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_Contract_tests.cs` — must exist and pass.

**Status**: [x] Created — 36 tests; EditMode 2523 / 2523 (2026-10-12 19:35:04Z, `TestResults.xml`)

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002–011; Enhancement Story 011 (the type of `SaveIrreversibleOutcome`)

---

## Completion Notes

**Completed**: 2026-10-12
**Criteria**: 10/10 passing (nine by automated test; "Doc comments" by reading — no doc-comment scan exists in the suite)
**Test Evidence**: Logic — `tests/EditMode/CharacterPersistence/CharacterPersistence_Contract_tests.cs`, 36 tests, in the EditMode run of 2523 / 2523
**Code Review**: Complete — `/code-review` 2026-10-12 (`unity-specialist`, `qa-tester`): CHANGES REQUIRED, tests only; fixes applied (tests 25 → 36, two remarks in `CharacterRecord.cs`); not re-run after the fixes
**Gates**: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — lean mode

**Files**: `src/Foundation/CharacterPersistence/` (`AccountID`, `ZoneID`); `src/ServerLogic/CharacterPersistence/` (`ICharacterPersistence`, `CharacterRecord`, `GearSlotRecord`, `CharacterLoadResult`, `PendingPurchaseRecord`, `CharacterPersistenceConstants`, six enums); the test file; two entries in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`.

**Deviations (advisory, accepted by the user 2026-10-12)**
1. `AssemblyBoundaryLists.cs` gained `AccountID` and `ZoneID` on `SharedAllowList` (`test_every_foundation_type_is_listed` needs them). The Assembly criterion's "unedited" holds for `AssemblyBoundary_tests.cs`, not for its data file.
2. `CreateStub` takes `byte classType`, not CR-CP-1's `ClassType` (readiness decision 1). The GDD is not yet changed.
3. Not listed in the story: the `GearSlotRecord` struct; gear slots indexed by `(int)GearSlot` (this type's convention — CR-CP-2 gives the count, not the order); the namespace `IronGrind.CharacterPersistence` on both assemblies, not on `ServerOnlyNamespaces`.
4. Test methods are named `Type_Scenario` (the suite's majority style), not `test_…`.

**Decision 2026-10-12 (user)**: ADR-011's "the snapshot type is immutable" is a separate concern, for the save stories; `CharacterRecord` stays a mutable class with `Clone()`.

**For later stories**
- `default(CharacterLoadResult).Code` is `Success` and `default(PendingPurchaseRecord).State` is `GoldDebited` — both are 0 (Stories 003, 011).
- Dapper cannot fill get-only arrays, readonly structs or `uint` columns directly: a private row type, explicit column aliases, checked casts (Story 012). Whether ADR-006's `INTEGER` columns hold the full `uint` range of gold and zone ids was not confirmed.
- The GDD still says "23 logical fields" and `ClassType classType`.

**Tech debt logged**: none (user's choice)
