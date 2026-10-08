# Story 009: Client Stats View — the HUD Stops Referencing `CharacterStats`

> **Epic**: Character Stats
> **Status**: Complete (2026-10-08)
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/character-stats.md` — the server owns every stat: base values, modifiers and the transaction API are written only by server systems (write ownership). The HUD shows values the player is allowed to see: level and experience on the XP bar, the four primaries on the respec screen (`leveling-system.md` AC-LS-46, AC-LS-47, AC-LS-48).
**Requirement**: none registered — the requirement is ADR-012's. `docs/architecture/tr-registry.yaml` is empty.

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 7 ("Read-only views. `Foundation` declares read-only interfaces for the local player's state — stats (current and effective values, change notification) … working name `ILocalPlayerStatsView`"; "the stats view is completed in the Character Stats move story"), Decision 6 check 1 (shared allow-list), Migration Plan step 2 item 9.
**ADR Decision Summary**: `IronGrind.Client` cannot reference `IronGrind.ServerLogic`. `CharacterStats` is the last server class the HUD names. Before it can move, the three UI files that take it must read through an interface declared in `Foundation`.

**This story is the first half of ADR-012's Character Stats step**, split the same way as Leveling (Stories 014 and 015): this story changes code and moves nothing; Story 010 (not yet written) is the mechanical move of the six server types and ends Migration Plan step 2.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — same shape as Leveling Story 014, smaller, and ADR-012 Verification Required 8 (a `MonoBehaviour` in the `UNITY_EDITOR`-constrained harness assembly) is already confirmed.
**Engine Notes**:
- Unity generates `.meta` files for the new source files; they are committed.
- Manual check in Play mode: the harness's debug buttons sit in the bottom strip of the Game view. If they are not visible, drag the Game view's Scale fully left before suspecting the code; the harness logs `[LevelingHudManualTestHarness] Layout: row worldBound=…` with where the row is.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary; Presentation layer)**:
- Required: a type is added to `IronGrind.Foundation` only with its client consumer named on the shared allow-list.
- Required: a `Client` presenter depends on a view declared in `Foundation`, never on a service or a server state class.
- Required (ADR-010): event handlers with two value-type parameters use a named delegate type, not `Action<T1, T2>`.
- Forbidden: `InternalsVisibleTo` between production assemblies.

---

## What the Client Uses Today (checked 2026-10-08)

| File in `src/Client/UI/LevelingSystem/` | Use of `CharacterStats` in code |
|---|---|
| `PlayerResourceClusterPresenter.cs` | field and constructor parameter; `Subscribe(OnStatChanged)`, `Unsubscribe(OnStatChanged)`; `GetBaseStat(entityId, StatID.Level)`, `GetBaseStat(entityId, StatID.Experience)` |
| `RespecScreenPresenter.cs` | field and constructor parameter; `GetBaseStat(entityId, StatID.Level)` and `GetBaseStat(entityId, stat)` for the four primaries |
| `LevelingHudController.cs` | `Initialize` parameter, passed to the two presenters |
| `LevelUpOverlayPresenter.cs` | none (imports the namespace for `EntityID`) |

`CharacterStats.Subscribe` takes `CharacterStats.StatChangedHandler`, a delegate type nested in the server class. No other `Foundation` or `Client` code uses `CharacterStats`.

---

## Design

### New types in `IronGrind.Foundation` (folder `src/Foundation/CharacterStats/`, namespace `IronGrind.CharacterStats`)

1. **`LocalPlayerStatChangedHandler`** — `public delegate void LocalPlayerStatChangedHandler(EntityID entityId, StatID statId);` A top-level named delegate (ADR-010), because the view cannot use the delegate nested in the server class.
2. **`ILocalPlayerStatsView`** — `public interface` with exactly what the presenters call:
   - `int GetBaseStat(EntityID entityId, StatID statId);`
   - `void Subscribe(LocalPlayerStatChangedHandler handler);`
   - `void Unsubscribe(LocalPlayerStatChangedHandler handler);`

   ADR-012 describes the view as "current and effective values, change notification". No presenter reads an effective value or a float stat today, so those members are not added; the interface grows when a screen needs them (for example the inventory tooltip), with that consumer named.

### Changes in `IronGrind.Client`

- `PlayerResourceClusterPresenter` and `RespecScreenPresenter`: the `CharacterStats` field and constructor parameter become `ILocalPlayerStatsView`. `OnStatChanged(EntityID, StatID)` keeps its signature; the method group converts to the new delegate type.
- `LevelingHudController.Initialize`: the first parameter becomes `ILocalPlayerStatsView stats`.
- After this story no code line under `src/Client/` names the `CharacterStats` class. The namespace `IronGrind.CharacterStats` is still imported for `EntityID`, `StatID` and the new types.

### Changes in `IronGrind.DevHarness`

- New `HarnessStatsAdapter` (`src/DevHarness/UI/LevelingSystem/`, `internal sealed`, beside `HarnessLevelingAdapter`): implements `ILocalPlayerStatsView` over the harness's `CharacterStats`. `GetBaseStat` forwards. `Subscribe` wraps the handler in a `CharacterStats.StatChangedHandler`, remembers the pair, and subscribes the wrapper; `Unsubscribe` looks the wrapper up, unsubscribes it and forgets the pair, so the same handler can be subscribed and unsubscribed repeatedly without leaking a subscription. Subscribing the same handler twice does not double-subscribe.
- `LevelingHudManualTestHarness` builds the adapter and passes it to `Initialize`. It keeps using the real `CharacterStats` for its own writes (`SetBaseStat`, `AddExperience`).

### Not changed

- `CharacterStats` does **not** implement `ILocalPlayerStatsView`: a server class does not carry the client's view (same decision as `LevelingService` in Leveling Story 014). On a real client the implementation will be a mirror filled from state messages.
- No file under `src/Foundation/CharacterStats/` other than the two new ones.

---

## Acceptance Criteria

- [x] **View declared**: `ILocalPlayerStatsView` and `LocalPlayerStatChangedHandler` exist in `src/Foundation/CharacterStats/`, public, documented, with the three members above and no others.
- [x] **The client names no stats class**: a grep of `src/Client/` for `CharacterStats` as a whole word finds no code line other than `using IronGrind.CharacterStats;` (doc comments may mention it). `LevelingHudController.Initialize`, `PlayerResourceClusterPresenter` and `RespecScreenPresenter` take `ILocalPlayerStatsView`.
- [x] **Harness adapter**: `HarnessStatsAdapter` exists in `src/DevHarness/UI/LevelingSystem/` and the harness passes it to `Initialize`. Unsubscribing a handler stops its notifications (the wrapper is removed from `CharacterStats`).
- [x] **Boundary lists updated**: `SharedAllowList` gains `IronGrind.CharacterStats.ILocalPlayerStatsView` and `IronGrind.CharacterStats.LocalPlayerStatChangedHandler`, each with its consumer named (80 → 82). `NotYetMovedList` is unchanged (6).
- [x] **No server behaviour change**: no existing file under `src/Foundation/` or `src/ServerLogic/` is edited.
- [x] **Harness still works (manual)**: in Play mode with the harness, a level-up button moves the XP bar and updates the level badge (proves `Subscribe` reaches the presenter through the adapter), and the respec screen opens showing the current primaries and commits a respec. Confirmed by the user.
- [x] **Suite green, with the total recorded**: the full EditMode suite passes with no compile error and no new compiler warning; the total is read from a results file whose run started after the Editor compiled the change. Expected: 2009 (no test is added; see QA Test Cases).

---

## Implementation Notes

1. **Order**: add the two `Foundation` types and their `SharedAllowList` entries; change the three `Client` files; add the adapter and update the harness.
2. **`SharedAllowList` entries** (`tests/EditMode/Architecture/AssemblyBoundaryLists.cs`), in a new group "Character Stats: client read model (ADR-012 Decision 7)" placed after the Leveling client-read-model group and before the Networking section:
   - `ILocalPlayerStatsView` — consumer: `PlayerResourceClusterPresenter`, `RespecScreenPresenter`
   - `LocalPlayerStatChangedHandler` — consumer: `PlayerResourceClusterPresenter`
   A delegate is a type: `test_every_foundation_type_is_listed` will fail if it is not listed.
3. **Doc comments** in the three `Client` files that name `CharacterStats` as the thing they read: update the wording to the view. A `<see cref="CharacterStats"/>` in a `Client` file still resolves today (the class is in `Foundation` until Story 010) but will not after the move; write `<c>CharacterStats</c>` now so Story 010 has nothing to fix in `src/Client/`.
4. **Type name ambiguity**: inside namespace `IronGrind.CharacterStats` files and in files that import it, the bare name `CharacterStats` can mean the namespace or the class. The existing code writes `IronGrind.CharacterStats.CharacterStats` for the class; the adapter and the harness should do the same.
5. **Naming**: ADR-012 gives `ILocalPlayerStatsView` as a working name fixed by the story that introduces it. This story fixes it, and `LocalPlayerStatChangedHandler`.

Other rules:
- No performance impact expected — one interface call replaces one direct call per read; the presenters' read counts do not change. The adapter allocates one wrapper delegate per subscription, in the Editor-only harness.

---

## Out of Scope

- Moving any Character Stats type to `IronGrind.ServerLogic` (Story 010).
- Effective-value, float-stat and entity-died members on the view; a networked (mirror) implementation of the view.
- An automated test of `HarnessStatsAdapter`: the test assembly does not reference `IronGrind.DevHarness`, and adding that reference is a decision of its own.
- New HUD features or any visual change.

---

## QA Test Cases

**Automated (existing, unchanged)**: the full suite; in particular `test_every_foundation_type_is_listed` (the two new types are on the allow-list) and `test_no_type_is_on_both_lists_or_duplicated`.

**No new automated test.** The story adds an interface, a delegate and an Editor-only adapter and retargets three presenters that have no test files. The compiler proves the wiring; behaviour is proved by the manual walkthrough.

**Manual (user, Play mode with the harness)**:
- **XP bar follows the stats** — Setup: HUD scaffolding and harness added, Play mode. Verify: press "AC-LS-46: Normal Level-Up (L5->L6)". Pass: the level badge shows 6 and the XP bar updates, as before this story.
- **Respec screen reads the stats** — Verify: press "AC-LS-48: Open Respec Screen". Pass: the four primaries show their current values and floors; committing a valid respec closes the screen without an error in the Console.
- **Tier transition and L60** — Verify: the other three buttons. Pass: same behaviour as before.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- The full EditMode suite result with the total, from a results file.
- The user's confirmation of the manual walkthrough, recorded in the completion notes.

**Status**: [x] Complete — 2009 of 2009 passed (Test Runner results file, 2026-10-08); harness walkthrough confirmed by the user and by the Editor log

---

## Dependencies

- Depends on: ADR-012 (Accepted 2026-10-08); Leveling Story 014 (Complete — `IronGrind.DevHarness`, the harness, `HarnessLevelingAdapter`, and the `Initialize` signature this story changes again); Leveling Story 015 and Currency Story 007 (Complete — nothing else in `Foundation` uses `CharacterStats`).
- Unlocks: Character Stats Story 010 (move `CharacterStats`, `StatSchema`, `ILevelingService`, `BuffID`, `BuffModifierEntry`, `EquipmentModifierEntry` to `IronGrind.ServerLogic`), which ends ADR-012 Migration Plan step 2.

---

## Completion Notes
**Completed**: 2026-10-08
**Criteria**: 7/7 passing
- View declared: `ILocalPlayerStatsView` (`GetBaseStat`, `Subscribe`, `Unsubscribe`) and `LocalPlayerStatChangedHandler` in `src/Foundation/CharacterStats/`.
- The client names no stats class: the only code lines under `src/Client/` containing `CharacterStats` are `using IronGrind.CharacterStats;`.
- Harness adapter: `HarnessStatsAdapter` in `src/DevHarness/UI/LevelingSystem/`; the harness passes it to `Initialize`.
- Boundary lists: `SharedAllowList` 80 → 82; `NotYetMovedList` unchanged at 6.
- No existing file under `src/Foundation/` or `src/ServerLogic/` was edited.
- Suite: 2009 total, 2009 passed, 0 failed — run started 16:54:28, after the Editor compiled the change at 16:54:08; totals read from `TestResults.xml`. All 10 boundary tests pass. No compile error and no compiler warning, including after the later harness edits below, which no test covers.
- Harness walkthrough: the user confirmed in Play mode that the game works (level badge and XP bar follow the level-up buttons) and that a respec commits. The Editor log shows the Play-mode sessions after this story's compile (`Started`, `Layout` and `Respec` lines).

**Found during the walkthrough** (two problems, neither in the stats view):
1. *The HUD did not appear.* `HUD_Root` had never been saved into `SampleScene.unity` and was lost when the Editor reloaded the scene; the log showed "[HudBootstrap] No 'HUD_Root' GameObject found". Re-running both `Tools/HUD` commands restored it. The scene is still not saved with the HUD.
2. *A respec could not be committed.* The harness's own debug buttons covered the "Confirm Respec" button. Since the Leveling Story 014 fix the button row wrapped onto two lines and spanned the screen (y 668–760, full width), and it is drawn on top of the HUD; the commit button is at y 656–700, x 494–872 (measured by the new diagnostic). The regression was introduced by the Story 014 harness fix, not by this story.

**Deviations** (manual harness only; no production code):
- `LevelingHudManualTestHarness`: the debug buttons are now a vertical stack in the bottom-left corner (x 8–334, y 530–760 on 1366×768) with `pickingMode = Ignore` on the container, replacing the wrapped full-width row; "Open Respec Screen" logs the commit button's rectangle, whether it is enabled, the points-remaining label and whether the debug buttons overlap it.

**Test Evidence**: full EditMode suite 2009/2009; the user's walkthrough; Editor log lines of the Play-mode sessions.
**Code Review**: Line-by-line review of the agent's diff and of `HarnessStatsAdapter` by the orchestrator; no specialist panel was run.
