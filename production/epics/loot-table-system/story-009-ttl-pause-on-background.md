# Story 009: TTL Pause on App Background

> **Epic**: Loot Table System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 2–3 hours

## Context

**GDD**: `design/gdd/loot-table-system.md` — CR-LT-13.1 (TTL Pause on App Background), CR-LT-13.3 (warning re-fires after an extension), Tuning Knobs (`GROUND_ITEM_TTL_PAUSE_CAP_TICKS`)
**Requirement**: `TR-loot-009`
*(Placeholder ID added at story creation — `docs/architecture/tr-registry.yaml` is empty; requirement text lives in the EPIC's GDD Requirements table.)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (Accepted)
**ADR Decision Summary**: NGO message handlers enqueue; the tick loop drains the queue and calls game logic. The `ClientBackgrounded` / `ClientForegrounded` wire messages therefore reach the loot service as plain method calls from the tick loop.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. `uint` tick arithmetic. No post-cutoff API. Detecting app background on the client is not part of this story.

**Control Manifest Rules (Core layer)**:
- Required: NGO message handlers enqueue to `Queue<T>` — never call game-logic methods directly — ADR-010
- Forbidden: calling game logic directly from a `ServerRpc` or NGO handler — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/loot-table-system.md`, scoped to this story:*

- [ ] **AC-LT-21** [ADVISORY]: a ground item assigned to Character 42 has 2,400 ticks remaining and `pauseBudgetRemaining = 1200`; 42's client is backgrounded at tick T and foregrounded at tick T+500 → `expiryTick` is extended by 500 and `pauseBudgetRemaining = 700`. 42 then backgrounds again for 800 ticks → only 700 more ticks are applied and `pauseBudgetRemaining = 0`.
- [ ] **AC-LT-24** [ADVISORY] *(second half)*: after the first expiry warning has fired, `expiryTick` is extended by CR-LT-13.1 → a second warning is raised at the new `expiryTick − EXPIRY_WARNING_TICKS`; exactly two warnings exist for that `groundItemID`, one per deadline.
- [ ] **CR-LT-13.1 budget**: each assignment starts with `pauseBudgetRemaining = GROUND_ITEM_TTL_PAUSE_CAP_TICKS` (1,200); once it reaches 0, further background periods extend nothing.
- [ ] **CR-LT-13.1 scope**: only items that are assigned to the backgrounded character and are bag-full blocked are extended; other characters' items and unblocked items are not.

---

## Implementation Notes

*Derived from ADR-010 and CR-LT-13.1.*

**Module conventions:** see Story 001.

**API:** `NotifyClientBackgrounded(CharacterID, uint tick)` records `backgroundedAtTick` for the character. `NotifyClientForegrounded(CharacterID, uint tick)` computes `pausedTicks = tick − backgroundedAtTick` and, for each of that character's assigned bag-full items, applies `extension = min(pausedTicks, pauseBudgetRemaining)`: `expiryTick += extension`, `pauseBudgetRemaining −= extension`. The wire schemas for the two messages are in `design/gdd/networking-wire-protocol.md`; decoding them is the network layer's job.

**Budget is per assignment** — the record field was added in Story 005.

**Warning re-fire** follows from Story 008's equality check: once `expiryTick` moves, `expiryTick − currentTick == EXPIRY_WARNING_TICKS` becomes true again at the new deadline. Verify, do not add a second mechanism.

**Guards:** a foreground with no recorded background for that character is ignored; a second background before a foreground keeps the first `backgroundedAtTick`.

**Open design point — raise at `/story-readiness` before implementing.** CR-LT-13.1 extends `expiryTick` only when the client comes back to the foreground. It does not say what happens if `expiryTick` is reached *while* the client is still backgrounded: read literally, CR-LT-12 despawns the item before any extension is applied, so an item with less time left than the background period gets no pause at all. AC-LT-21 does not exercise this case (2,400 ticks remaining, 500 backgrounded). Implement the literal rule only if the designer confirms it; the alternative is to hold the despawn while the character is backgrounded and budget remains.

---

## Out of Scope

*Handled by neighbouring stories — do not implement here:*

- **Story 008**: the expiry warning itself.
- Client-side background detection and the `ClientBackgrounded` / `ClientForegrounded` wire codecs.
- Pausing an auction window — CR-LT-13.1 covers assigned bag-full items only.

---

## QA Test Cases

*Lean review mode — QL-STORY-READY gate skipped; cases derived from the GDD acceptance criteria. Do not invent new test cases during implementation.*

*Test file*: `tests/EditMode/Integration/LootTableSystem/LootTable_TtlPause_integration_tests.cs`

- **AC-LT-21**: extension and budget
  - Given: an item assigned to 42, bag-full blocked, `expiryTick = E`, budget 1200
  - When: `NotifyClientBackgrounded(42, T)`, `NotifyClientForegrounded(42, T + 500)`
  - Then: `expiryTick = E + 500`; budget 700
  - Edge cases: a second background of 800 ticks → `expiryTick = E + 1200`, budget 0; a third background of 100 ticks → `expiryTick` unchanged

- **AC-LT-24 (second half)**: warning re-fires
  - Given: an item whose first warning has fired; then a background/foreground pair extends `expiryTick` by 300
  - When: `Tick` advances to the new `expiryTick − 600`
  - Then: a second `OnGroundItemExpiryWarning`; exactly two in total for that `groundItemId`
  - Edge cases: none

- **CR-LT-13.1 scope**
  - Given: item X assigned to 42 and blocked; item Y assigned to 42 and not blocked; item Z assigned to 43 and blocked
  - When: 42 backgrounds for 200 ticks and returns
  - Then: only X's `expiryTick` changed
  - Edge cases: a foreground for a character with no recorded background changes nothing

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/LootTableSystem/LootTable_TtlPause_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 008 (blocked state, expiry warning).
- Unlocks: None.
