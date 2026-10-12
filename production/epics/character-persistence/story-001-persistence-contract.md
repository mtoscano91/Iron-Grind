# Story 001: The Persistence Contract — `ICharacterPersistence`, Result Types, `CharacterRecord`

> **Epic**: Character Persistence
> **Status**: Not Started — written 2026-10-12; `/story-readiness` pending (3 open decisions, listed below)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-1 (the interface and its result types), CR-CP-2 (the character record: 23 logical fields, the fields that are not persisted).
**Requirement**: `TR-cp-001`, `TR-cp-002`
*(The TR registry has no entry for this system; the IDs are the epic's placeholders.)*

**ADR Governing Implementation**: ADR-006 (Persistence Layer); ADR-011 (Asynchronous Persistence in the Server Tick Loop) Decision 3; ADR-012 Decision 1; ADR-010.
**ADR Decision Summary**: one async interface, `ICharacterPersistence`, unchanged by ADR-006 and ADR-011; its implementation and everything that names a server-only library live in `IronGrind.ServerLogic`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — plain C# types, no engine API.

**Control Manifest Rules (this layer)**:
- Required: service interface naming `I[SystemName]Service`, with `ICharacterPersistence` a grandfathered exception — ADR-010
- Required: a type that computes, validates or mutates authoritative game state is in `IronGrind.ServerLogic`; wire and shared value types are in `IronGrind.Foundation` — ADR-012
- Forbidden: Entity Framework Core for character persistence — ADR-006

---

## Acceptance Criteria

- [ ] **Interface**: `ICharacterPersistence` declares exactly the nine methods of CR-CP-1 — `LoadCharacter`, `SaveIrreversibleOutcome`, `SaveSession` (two overloads, the second with `float hpOverride`), `CreateStub`, `BeginPurchase`, `CompletePurchase`, `RefundPurchase`, `LoadOutstandingPurchases` — each returning a `Task` and taking a `CancellationToken` as its last parameter.
- [ ] **Enums**: `CharacterLoadCode`, `CharacterSaveResult`, `IrreversibleOutcomeTrigger`, `SessionEndReason`, `PendingPurchaseResult` and `PendingPurchaseState` are `byte` enums with the member values of CR-CP-1 (`DatabaseError = 255` in the three result enums); a test pins every value.
- [ ] **Load result**: `CharacterLoadResult` carries `Code` and `LastZoneID`.
- [ ] **Record**: `CharacterRecord` holds the 23 logical fields of CR-CP-2 with the types of that table — fixed-capacity arrays of 7 gear slots (`ItemID`, `EnhancementLevel: byte`) and 20 inventory slots (item, count `int`, enhancement level `byte`) — plus `SaveVersion`, `CreatedAtUtc`, `LastSavedAtUtc`. It holds none of the four not-persisted items (buff modifiers, equipment modifier values, `PrestigeBand` / appearance flags, `IsTransitioning`).
- [ ] **Copy**: a `CharacterRecord` can be copied so that changing the copy's arrays does not change the original (needed by ADR-011's snapshot rule).
- [ ] **Constants**: `CHARACTER_LOAD_TIMEOUT_SECONDS = 10`, `PERSISTENCE_WRITE_TIMEOUT_SECONDS = 5`, `PERSISTENCE_WRITE_WARNING_MS = 50`, `PERSISTENCE_WRITE_CRITICAL_MS = 100`, gear slot count 7 and inventory slot count 20 (the last from `InventoryConstants`, not redefined).
- [ ] **Assembly**: the interface and the record are in `IronGrind.ServerLogic`; the assembly-boundary tests pass unedited.
- [ ] **Doc comments** on every public type and member.

---

## Implementation Notes

- The interface text is CR-CP-1's. ADR-011 Decision 3 states that the interface "is unchanged"; tick code consumes it through `ITickCompletionQueue.Track`, never with `await`.
- `CharacterID` exists (`src/Foundation/Currency/CharacterID.cs`). **`AccountID`, `ZoneID` and `ClassType` are defined nowhere in `src/`** (checked 2026-10-12): see open decision 1.
- `PendingPurchaseRecord` and the purchase enums are declared here so the interface compiles; their behaviour is Story 011.

**Open decisions for `/story-readiness`**

1. **`AccountID`, `ZoneID`, `ClassType`.** Define them in this story (as `readonly struct` ids in `Foundation` beside `CharacterID`, and a `byte` enum), or take them from the Authentication / Zone Instancing / Class epics. `ZoneID.Invalid = 0` is the first-login sentinel of CR-CP-9.
2. **Where the types live.** Result enums that a client-side login flow reads (`CharacterLoadCode`) may belong in `Foundation`; ADR-012's placement questions decide.
3. **`CharacterRecord`: class or struct.** A 23-field value with three arrays; the copy criterion above is easier to state for a class with an explicit `Clone`.

---

## Out of Scope

- Story 002: the record store seam and `CreateStub`'s values
- Stories 003–010: every behaviour of the interface
- Story 011: purchase behaviour
- Stories 012–016: the database

---

## QA Test Cases

- **Enums** — Given the six enums, When each member is cast to `byte`, Then it equals the CR-CP-1 value. Edge: `DatabaseError` is 255 in all three result enums.
- **Interface shape** — Given `typeof(ICharacterPersistence)`, When its methods are read by reflection, Then there are nine, each returns `Task` or `Task<T>`, and each has a `CancellationToken` last.
- **Record fields** — Given a new `CharacterRecord`, Then gear has 7 entries and inventory 20, and no member is named for a not-persisted item.
- **Copy** — Given a record with gear slot 0 and inventory slot 3 set, When it is copied and the copy's slots are changed, Then the original is unchanged.
- **Constants** — Then the six constants have the values above.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterPersistence/CharacterPersistence_Contract_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002–011; Enhancement Story 011 (the type of `SaveIrreversibleOutcome`)
