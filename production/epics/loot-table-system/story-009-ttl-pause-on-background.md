# Story 009: TTL Pause on App Background

> **Epic**: Loot Table System
> **Status**: Complete
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

**Performance**: No per-tick cost on the normal path: the pause is only consulted for an item that has reached its expiry tick. A foreground notice is one pass over the live records. Server tick target is under 30 ms (ADR-004). *(Added at readiness, 2026-10-02.)*

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
- [ ] **Bag-full items** *(readiness decision, 2026-10-02)*: an item is "bag-full" from the moment one of its pickups fails with a full bag until it is delivered or despawns — including after its assignee has walked out of the pickup radius.
- [ ] **Expiry while backgrounded** *(readiness decision, 2026-10-02)*: a bag-full item whose assignee is backgrounded does not despawn while its pause budget is not used up — it expires when `currentTick` reaches `expiryTick + min(currentTick − backgroundedAtTick, pauseBudgetRemaining)`, so at the latest at `expiryTick + pauseBudgetRemaining` if the client never returns. The foreground booking is unchanged (AC-LT-21's numbers hold).

---

## Implementation Notes

*Derived from ADR-010 and CR-LT-13.1.*

**Module conventions:** see Story 001.

**API:** `NotifyClientBackgrounded(CharacterID, uint tick)` records `backgroundedAtTick` for the character. `NotifyClientForegrounded(CharacterID, uint tick)` computes `pausedTicks = tick − backgroundedAtTick` and, for each of that character's assigned bag-full items, applies `extension = min(pausedTicks, pauseBudgetRemaining)`: `expiryTick += extension`, `pauseBudgetRemaining −= extension`. The wire schemas for the two messages are in `design/gdd/networking-wire-protocol.md`; decoding them is the network layer's job.

**Budget is per assignment** — the record field was added in Story 005.

**Warning re-fire** follows from Story 008's equality check: once `expiryTick` moves, `expiryTick − currentTick == EXPIRY_WARNING_TICKS` becomes true again at the new deadline. Verify, do not add a second mechanism.

**Guards:** a foreground with no recorded background for that character is ignored; a second background before a foreground keeps the first `backgroundedAtTick`.

**Settled at readiness (2026-10-02) — these resolve the "Open design point" below:**
- **Where it lives:** `NotifyClientBackgrounded` / `NotifyClientForegrounded` on `GroundItemService` and `IGroundItemService`; the tick is passed in by the caller (the envelope's server tick).
- **Which items (user decision):** a new sticky record flag, set when a pickup of the item fails with `PickupFailReason.InventoryFull` (entry or retry) and never cleared while the record lives. It is separate from Story 008's "blocked, in radius" flag, which is cleared when the assignee leaves the radius. The QA case's "blocked" means this sticky flag.
- **Expiry while backgrounded (user decision): hold the despawn while budget remains.** In the expiry step, for a bag-full item whose assignee has a recorded `backgroundedAtTick` and whose budget is above 0: `accrued = min(currentTick − backgroundedAtTick, pauseBudgetRemaining)` and the item expires only when `currentTick` has reached `expiryTick + accrued`. Nothing is written to the record while backgrounded; the foreground call books the extension exactly as CR-LT-13.1 says, so there is no double counting.
- **Bad ticks:** a foreground tick that is not at or after the recorded background tick extends nothing and clears the record (unsigned subtraction would otherwise read as a huge pause).
- **Warning:** unchanged from Story 008 — equality against the stored `expiryTick`. A warning can fire while the client is backgrounded; after an extension the second one fires only if the new threshold tick is still ahead.
- **Cleanup:** `Dispose` clears the per-character background records; a client that never returns leaves one entry until then.
- **Pickup before expiry** (Story 007) is unaffected: the pickup step still runs before the expiry step.

**Added at code review (2026-10-02, user: "Fix all"):**
- **Disconnect ends the pause (user decision).** New `NotifyClientDisconnected(CharacterID, uint tick)` on the service and the interface: it books the pause earned up to that tick exactly like a foreground and clears the record; the timer then runs normally, as the GDD says for a disconnected assignee. The network layer must call it on every disconnect and zone leave — otherwise a background record outlives the session, and a later background of the same character would be measured from the old tick (pause the player did not earn, up to the whole budget).
- **Item states:** only `Assigned` items are held, and only `Assigned` / `Claiming` items take a booking — an allow-list, so an auction can never be paused by accident.
- **For Story 010:** when an item is reassigned (CR-LT-10 fallback), reset its per-assignment data: the "assignee was inside" flag, the bag-full flag and the pause budget.
- **Accepted as built, recorded for TD-048:** (1) a held item past its stored expiry gets no `BagFullPickupBlocked` on a new entry (its assignee's client is backgrounded, and the remaining time could not be stated truthfully before the booking); (2) the whole background period counts, even the part before the item's pickup failed — CR-LT-13.1 as written; a test pins it.

**GDD gap (not fixed here):** after an extension, clients still hold the `expiryTick` from `GroundItemSpawned`; nothing tells them the new deadline except the next `GroundItemExpiryWarning`.

**Open design point — RESOLVED at readiness 2026-10-02 (hold the despawn while budget remains; see "Settled at readiness" above). Original note:** CR-LT-13.1 extends `expiryTick` only when the client comes back to the foreground. It does not say what happens if `expiryTick` is reached *while* the client is still backgrounded: read literally, CR-LT-12 despawns the item before any extension is applied, so an item with less time left than the background period gets no pause at all. AC-LT-21 does not exercise this case (2,400 ticks remaining, 500 backgrounded). Implement the literal rule only if the designer confirms it; the alternative is to hold the despawn while the character is backgrounded and budget remains.

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

**Status**: [x] Created — 28 test methods (28 NUnit cases), all 6 criteria covered

---

## Dependencies

- Depends on: Story 008 (blocked state, expiry warning).
- Unlocks: None.

---

## Completion Notes
**Completed**: 2026-10-02
**Criteria**: 6/6 passing (0 deferred). AC-LT-21 says "verified via server ground item state log": the tests read the item's state through `TryGetGroundItem`; no such log exists
**Deviations**: Advisory only — `TR-loot-009` is not in the TR registry (same accepted gap as Stories 001–008); user decisions not in CR-LT-13.1: the despawn is held while the assignee is backgrounded and pause budget remains, "bag-full" is a sticky flag that survives leaving the radius, a disconnect ends the pause; `NotifyClientDisconnected` is a public method beyond the story's API (code review); left as built: no blocked notice for a held item past its stored expiry, and the whole background period counts even before the item's pickup failed (CR-LT-13.1 as written); GDD gap: after an extension clients still hold the spawn message's `expiryTick`
**Test Evidence**: Integration — `tests/EditMode/Integration/LootTableSystem/LootTable_TtlPause_integration_tests.cs` (28 test methods, 28 NUnit cases — 14 from implementation, 14 from the code review; real `GroundItemService` over the shared recording fake). Full EditMode suite 1348/1348 passed in Unity 6000.3.10f1 batch mode, 0 compile errors — on the second run; the first run after the fixes had one failure in an unrelated, flaky Networking timing test (TD-050)
**Code Review**: Complete — `/code-review` returned CHANGES REQUIRED (a background record outliving a disconnected session; the hold only tested with the assignee inside the radius; a deny-list of item states; doc drift); all applied; suite re-run green; fixes not re-reviewed
**Tech debt logged**: TD-048 extended (pause rules not in the GDD; behaviours left as built; stale client expiry tick); TD-050 (new — flaky wall-clock test in the Networking suite, found during this story's test runs)
**Files outside the story's own list**: one doc comment in `GroundItem.cs`
**Note for the network layer**: call `NotifyClientDisconnected` on every disconnect and zone leave; all three notifications come from the tick loop, never from a message handler (ADR-010 Decision 5)
**Note for Story 010**: on reassignment reset the "assignee was inside" flag, the bag-full flag and the pause budget; an auction is never paused (the pause applies to `Assigned` / `Claiming` items only)
