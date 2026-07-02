# Story 005: OnStatChanged / OnEntityDied Events — Named Delegates, Fixed Subscriber Array, Re-entrance Guard

> **Epic**: Character Stats
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28

## Context

**GDD**: `design/gdd/character-stats.md`
**Requirement**: `TR-stats-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: All cross-system broadcasts use C# `event` with `readonly struct` arg types; no central EventBus; no lambda captures for persistent subscriptions; server-side subscribers must implement `IDisposable` and unsubscribe in `Dispose()`. Named delegates (not generic `Action<T,U>`) required where two value-type parameters would cause IL2CPP boxing.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Generic `Action<EntityID, StatID>` with two value-type parameters can cause IL2CPP boxing on iOS — use named delegate `public delegate void StatChangedHandler(EntityID entityId, StatID statId)` instead. Subscribe in constructor/`Initialize()`, unsubscribe in `Dispose()`. Thread safety: `OnStatChanged` fires on Unity main thread only. Subscriber list = fixed-capacity array (16 slots) iterated by index — no `List<T>/foreach` to avoid heap enumerator allocation per event invocation.

**Control Manifest Rules (Foundation layer)**:
- Required: All `event` arg types must be `readonly struct` — source: ADR-010
- Required: Event naming PascalCase prefixed with `On` — `OnStatChanged`, `OnEntityDied` — source: ADR-010
- Required: Subscribe in constructor/`Initialize()`; unsubscribe in `Dispose()` — source: ADR-010
- Required: All server-side event subscribers must implement `IDisposable`; `Dispose()` unsubscribes from all events — source: ADR-010
- Forbidden: Never use a central EventBus/MessageBus singleton — source: ADR-010
- Forbidden: Never use lambda captures for persistent event subscriptions — not unsubscribeable by reference — source: ADR-010
- Forbidden: Never use `Action<object>` or class-typed event args — boxes struct args — source: ADR-010
- Forbidden: Never use generic `Action<EntityID, StatID>` (two value-type params on one delegate) — IL2CPP boxing risk; use named delegate instead

---

## Acceptance Criteria

*From GDD `design/gdd/character-stats.md`, scoped to this story:*

- [ ] **AC-29** [BLOCKING]: Subscriber registered on `OnStatChanged` for AttackPower. `AddBuffModifier(AP, +30 flat)` → `handlerFired == true` immediately after `AddBuffModifier` returns (synchronous, no yield). `notifiedValue` (read via `GetEffectiveStat` inside handler) == 130. Then `RemoveBuffModifier(WarriorCry)` → fires again with `notifiedValue == 100`.
- [ ] **AC-29b** [BLOCKING]: Subscriber on `OnStatChanged(AP)` attempts `AddBuffModifier` inside handler body; catches `InvalidOperationException` → `exceptionThrown == true`. `GetEffectiveStat(AP)` = 130 (outer add applied; inner write rejected by re-entrance guard; no modifier corruption).
- [ ] **[NEW] Unsubscribe correctness** [BLOCKING]: Subscribe `OnStatChanged(AP)`, fire event (callCount=1 confirmed), unsubscribe, fire event again → callCount remains 1 (handler not called after unsubscribe).
- [ ] **[NEW] OnEntityDied re-entrance guard** [BLOCKING]: Subscriber on `OnEntityDied` attempts `ApplyDamage` inside handler → catches `InvalidOperationException`. CurrentHP=0.0f (death was processed correctly). Read-only `GetEffectiveStat` inside `OnEntityDied` handler does NOT throw.

---

## Implementation Notes

*Derived from GDD Rule 8 and ADR-010:*

**Delegate types** (named, not generic `Action`):
```csharp
public delegate void StatChangedHandler(EntityID entityId, StatID statId);
public delegate void EntityDiedHandler(EntityID entityId);
```

**Subscriber list**: Fixed-capacity `StatChangedHandler[]` array, 16 slots, iterated by index (not `foreach`). On overflow: log error in dev builds, return without registering. This is a programming error — raise capacity in code.

**Re-entrance guard** (`bool _isFiring` field): When `_isFiring == true` and a write operation is attempted inside a handler, dev builds throw `InvalidOperationException`; IL2CPP release builds drop the write silently. Read-only queries (`GetEffectiveStat`, `GetBaseStat`) are always permitted inside handlers.

**`OnStatChanged` fires synchronously** within the same call stack as any modifier change (add, remove, expire, `SetBaseStat`). No coroutines, no deferred dispatch.

**`OnEntityDied` fires synchronously** when `CurrentHP` reaches 0.0 (Story 004 stubs the call — this story wires it). Same re-entrance rules as `OnStatChanged`. Subscribers receive only `EntityID`; CurrentHP is 0.0 when the event fires.

**Unsubscribe in `Dispose()`** — all HUD subscribers and server-side game logic subscribers must implement `IDisposable` and unsubscribe to prevent dangling references after HUD destruction or zone teardown. The `Unsubscribe` API must correctly remove the handler from the fixed-capacity array.

**`StatChangedArgs.cs`** and related event arg types live in the `IronGrind.Events` namespace (one file per type) per the control manifest.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 001–004**: Container, modifier stack, modifier lifecycle, resource pools — all must be Done
- **AC-17 duration-refresh confirmation**: Deferred to Status Effects integration
- **Subscriber capacity overflow test**: Deferred to advisory — test that 17th subscriber is rejected (mirrors equipment cap test in Story 003)

---

## QA Test Cases

*Written by qa-lead at story creation. The developer implements against these — do not invent new test cases during implementation.*

**File**: `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs`

- **AC-29**: OnStatChanged fires synchronously on AddBuffModifier and RemoveBuffModifier
  - Given: BaseStat(AP)=100; `bool handlerFired=false; float notifiedValue=0f`. Subscribe `OnStatChanged(AP)`: `(id, stat) => { handlerFired=true; notifiedValue=GetEffectiveStat(id, stat); }`
  - When: (step 1) `AddBuffModifier(AP, +30 flat, 0 pct, 10 ticks, WarriorCry)`. Immediately after returns (no yield):
  - Then: `handlerFired==true`; `notifiedValue==130f`. A deferred or coroutine-delayed fire is failure.
  - When: (step 2) `handlerFired=false`; `RemoveBuffModifier(EntityID, AP, WarriorCry)`
  - Then: `handlerFired==true`; `notifiedValue==100f`.
  - Edge cases: Two different stats modified — handler fires once per stat change (not once total).

- **AC-29b**: Re-entrance guard throws InvalidOperationException on write-during-handler (dev build)
  - Given: `bool exceptionThrown=false`. Subscribe `OnStatChanged(AP)`: attempt `AddBuffModifier(AP, +10 flat, 0 pct, 5, ReentryTest)` inside handler; catch `InvalidOperationException` → `exceptionThrown=true`. BaseStat(AP)=100.
  - When: `AddBuffModifier(AP, +30 flat, 0 pct, 10 ticks, WarriorCry)`
  - Then: `exceptionThrown==true`; `GetEffectiveStat(AP)` = 130 (outer add applied; inner write rejected; no corruption).
  - Edge cases: Verify `GetEffectiveStat` inside handler does NOT throw (read-only calls permitted).

- **[NEW] Unsubscribe correctness**: Unsubscribed handler does not receive notifications
  - Given: `int callCount=0`. Subscribe `OnStatChanged(AP)`: `_ => callCount++`. `AddBuffModifier(AP, +10, ..., TestA)` → `callCount==1` (subscriber confirmed active).
  - When: Unsubscribe the handler. `AddBuffModifier(AP, +10, ..., TestB)`.
  - Then: `callCount==1` (unchanged — handler not called after unsubscribe).
  - Edge cases: Unsubscribe when not subscribed (no-op); subscribe → unsubscribe → re-subscribe (must fire again).

- **[NEW] OnEntityDied re-entrance guard**: Write during OnEntityDied handler throws
  - Given: `bool exceptionThrown=false`. Entity with CurrentHP=50.0f. Subscribe `OnEntityDied`: attempt `ApplyDamage(EntityID, 1.0f)` inside handler; catch `InvalidOperationException` → `exceptionThrown=true`.
  - When: `ApplyDamage(EntityID, 50.0f)` (kills entity, fires `OnEntityDied`)
  - Then: `exceptionThrown==true` (write-during-handler rejected). CurrentHP=0.0f (death was processed).
  - Edge cases: Verify `GetEffectiveStat` inside `OnEntityDied` handler does NOT throw (read-only permitted).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001, Story 002, Story 003, Story 004 must be Done
- Unlocks: Story 006, Story 007

## Completion Notes
**Completed**: 2026-07-02
**Criteria**: 4/4 passing (all blocking ACs covered)
**Deviations**:
- ADVISORY: Subscribe/Unsubscribe method pairs used instead of C# `event` keyword (ADR-010 D3 drift) — intentional, documented in implementation notes
- ADVISORY: TR-stats-005 not in registry (registry empty — pre-existing across all character-stats stories)
- ADVISORY: SetBaseStat firing OnStatChanged has no dedicated event test (deferred per story spec)
- ADVISORY: StatEventRecorder unused in events test file — available for Story 006/007
**Test Evidence**: Logic — `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` (9 tests, all blocking ACs covered)
**Code Review**: Complete (CHANGES REQUIRED → fixed before closure: count snapshot in fire loops, IsFiringAndAssert guards on Subscribe/Unsubscribe)
