# Story 035: Guard Chain Changes for the Request Dispatcher

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-10-09
> **Estimate**: 2 hours

*Added 2026-10-09. First of two stories for ADR-014 Migration Plan step 1. ADR-014 words step 1 as one story; it was split in two by user decision 2026-10-09, so that the change to a Complete story's code (Story 010) is reviewed on its own. Story 036 builds the dispatcher on top of this one.*

## Context

**GDD**: `design/gdd/networking-core.md` — Cross-Cutting Constraints 1–3 (unknown entity, session-ready gate, rate limits; a rejected request is dropped, never queued). No rule or value of the GDD changes.
**Requirement**: none in the epic's TR table — this story changes a component as decided by ADR-014
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09) — Decision 4a.
**ADR Decision Summary**: `CrossCuttingRpcGuardChain` was written when three request types existed. The dispatcher will evaluate it for every client request, up to 64 per client per tick, and must re-check a held request without spending a rate-limit slot. Three changes: every `RpcTypeTag` member has a listed gap (0 = no limit) and a test proves it; a read-only `IsLiveOwner` query; rejection logs are throttled.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. The only Unity API is `Debug.LogWarning`, already used by this class.
**Performance**: `Evaluate` stays free of allocation on the accept path. On the reject path, the full log (an interpolated string) is built at most once per (client, result) per tick; every further rejection only increments a counter. The throttle's storage is created with the chain and reused every tick.

**Control Manifest Rules (Foundation layer — "Inbound request dispatch and tick order (ADR-014)")**:
- Required: `GetRequiredTickGap` lists every `RpcTypeTag` member with its gap (0 for no limit), and `Evaluate` skips the last-accepted-tick read and write when the gap is 0
- Required: `bool IsLiveOwner(uint clientId, EntityID entityId)` — the entity is registered, owned by that client, and the client is session-ready; it writes nothing
- Required: the first rejection per (client, result) per tick is logged in full, the rest are counted and reported in one line per tick
- Required (enforcement): a test enumerates `RpcTypeTag` and asserts that no member makes `Evaluate` throw
- Forbidden: holding a request that a guard rejected — it is dropped

---

## Acceptance Criteria

*From ADR-014 Decision 4a and Validation Criteria, scoped to this story:*

**Tags with no rate limit**

- [ ] **Every member is listed**: `GetRequiredTickGap` has one `case` per `RpcTypeTag` member. The `default` branch still throws `ArgumentOutOfRangeException` (a value cast from a number outside the enum).
- [ ] **Enumeration test**: a test iterates `Enum.GetValues(typeof(RpcTypeTag))`; for each member, `Evaluate` on an owned, session-ready entity returns `Accepted` and does not throw. The test needs no edit when a member is added, and fails if the new member has no `case`.
- [ ] **Gap 0 skips the rate-limit state**: for a tag whose gap is 0, `Evaluate` neither reads nor writes the last-accepted tick. Any number of requests with that tag, for one entity on one tick, are all `Accepted`.
- [ ] **`IsRateLimited`**: `public static bool IsRateLimited(RpcTypeTag tag)` is true exactly when the tag's gap is above 0 — true for `AllocateFreePoint` and `NotifySkillUsed`, false for `SetTarget`. *(Not named in ADR-014; added 2026-10-09 because Decision 3 makes the dispatcher refuse a held type that "carries a rate-limited tag", and the gap is private to the chain. Approved by the user at `/story-readiness` 2026-10-09.)*

**`IsLiveOwner`**

- [ ] **True** when the entity is registered, its owner is `clientId`, and `clientId` is session-ready.
- [ ] **False** in each of these cases: the entity is not registered; it is owned by another client; the client is not session-ready.
- [ ] **Writes nothing and logs nothing**: after any number of `IsLiveOwner` calls for an entity, a request with a rate-limited tag for that entity on the same tick is `Accepted` (no rate-limit slot was spent), and no log message was produced.

**Throttled rejection logs**

- [ ] **First in full**: the first rejection for a given (client id, `RpcGuardResult`) on a given tick is logged with the same text as today.
- [ ] **The rest are counted**: a further rejection with the same (client id, result) on the same tick produces no log message. The return value is the same as today for every rejection.
- [ ] **Different keys are independent**: on one tick, a second result for the same client, and the same result for a second client, are each logged in full.
- [ ] **One summary line per tick**: `FlushRejectionSummary()` logs one warning that gives the tick and the number of suppressed rejections per result, and clears the counts. It logs nothing when nothing was suppressed. *(ADR-014 says "reported in one line per tick" and names no trigger; decided 2026-10-09: an explicit method, which the dispatcher of Story 036 calls at the end of `DispatchTick`.)* It clears the counts only: the (client id, result) pairs already logged stay until the tick changes, so a further rejection with the same pair on the same tick, after the flush, is counted and not logged in full. *(User decision at `/story-readiness` 2026-10-09.)*
- [ ] **A new tick starts again**: when `Evaluate` is called with a `CurrentTick` different from the tick of the stored throttle state, the pending summary of the earlier tick is logged first if any rejection was suppressed, then the state is cleared. The first rejection per (client, result) on the new tick is logged in full.
- [ ] **The observer hook is not throttled**: `OnSkillUsedRateLimitRejected` fires for every rate-limited `NotifySkillUsed` rejection, including one whose log was suppressed.

**Regression**

- [ ] **Story 010's tests are unchanged**: the 23 tests of `TickLoop_CrossCuttingGuards_tests.cs` pass with no edit to that file.

---

## Implementation Notes

- **File changed**: `src/ServerLogic/Networking/RpcGuards/CrossCuttingRpcGuardChain.cs` (namespace `IronGrind.Networking`, assembly `IronGrind.ServerLogic`). `RpcTypeTag.cs`, `InboundRpcDescriptor.cs` and `RpcGuardResult.cs` are not changed.
- **No new `RpcTypeTag` member.** The three members that exist already have a `case` each (`AllocateFreePoint` 4 ticks, `NotifySkillUsed` 1 tick, `SetTarget` 0). ADR-014 Decision 3 says the enum gains a member per request type; the story that adds a request type adds its member and its `case`. What this story adds is the test that makes a missing `case` fail the suite.
- **Gap 0**: read `GetRequiredTickGap` once at the start of guard 3. When it returns 0, skip the `_lastAcceptedTick` lookup and, after guard 4, skip the write. Today a `SetTarget` request writes one dictionary entry per entity it never reads back.
- **`IsLiveOwner`**: `_entityOwners.TryGetValue(entityId, out owner) && owner == clientId && _sessionReadyClients.Contains(clientId)`. No log, no write. It does not know about connections the dispatcher has marked removed; Story 036 checks that itself before calling.
- **Throttle state**: the tick it belongs to, a set of (client id, result) pairs already logged on that tick, and four counters (one per rejecting `RpcGuardResult`). Create the collection with the chain and `Clear()` it per tick; do not create one per tick. One private method replaces the four inline `Debug.LogWarning` calls: it decides whether to log the full message or to count.
- **Build the message only when it is logged.** The interpolated string must be inside the branch that logs. A helper that takes an already formatted `string` would keep the allocation the throttle is meant to remove.
- **Summary line** (one `Debug.LogWarning`): `[CrossCuttingRpcGuardChain] Tick {tick}: {total} further rejections not logged (UnknownEntity={a}, SessionNotReady={b}, RateLimited={c}, NotOwner={d}).`
- **Order inside `Evaluate`** when the tick changed: log the earlier tick's summary (if any), clear, then run the guards. The explicit `FlushRejectionSummary()` and this path share one private method.
- **`IsRateLimited`**: `GetRequiredTickGap(tag) > 0`. `GetRequiredTickGap` itself stays private.
- **Doc comments**: the class remarks say every rejection logs; correct them. Add doc comments with an example to `IsLiveOwner`, `IsRateLimited` and `FlushRejectionSummary`. `GetRequiredTickGap`'s summary still says "only the two rate-limit buckets"; correct it. `Evaluate`'s summary also says "Every rejection logs an anomaly"; correct it.
- **If an existing test fails**: a test of `TickLoop_CrossCuttingGuards_tests.cs` that expected two full logs for one (client, result, tick) would now fail. None was found when this story was written (the two tests with repeated rejections on one tick set `LogAssert.ignoreFailingMessages`). If one fails, stop and report it; do not edit that file.

---

## Out of Scope

- The dispatcher, the intake, the hold queues and every new interface of ADR-014 (Story 036).
- New `RpcTypeTag` members, and rate limits for request types that do not exist yet.
- The session layer that calls `MarkSessionReady` / `RegisterEntityOwnership` from a real connection.
- Changing the order of the four guards, or what each one rejects.
- Removing rate-limit entries of entities that have left the zone (`_lastAcceptedTick` is never pruned today; not part of Decision 4a).

---

## QA Test Cases

**File**: `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_Dispatch_tests.cs` (new). `TickLoop_CrossCuttingGuards_tests.cs` is not edited. Log messages are counted with a named handler on `Application.logMessageReceived`, removed in `TearDown` (`LogAssert` does not fail on an unexpected warning, so it cannot prove that a log was suppressed). No sleeps, no real threads.

**Tags**
- **Every member accepted** — for each value of `Enum.GetValues(typeof(RpcTypeTag))`: a fresh chain, an owned and session-ready entity → `Evaluate` returns `Accepted`, no exception.
- **Gap 0, same tick** — 100 `SetTarget` requests for one entity on one tick → all `Accepted`.
- **Rate-limited tag unchanged** — two `NotifySkillUsed` on one tick → `Accepted`, then `RejectedRateLimited`.
- **`IsRateLimited`** — `AllocateFreePoint` → true; `NotifySkillUsed` → true; `SetTarget` → false; no member throws.

**`IsLiveOwner`**
- **Owner, ready** → true.
- **Unregistered entity** → false. **Other owner** → false. **Not session-ready** → false. **After `ClearSessionReady`** → false. **After `UnregisterEntityOwnership`** → false.
- **Spends nothing** — 10 `IsLiveOwner` calls, then one `AllocateFreePoint` on the same tick → `Accepted`; the handler counted 0 log messages.

**Throttle**
- **Three identical rejections** — one client, not session-ready, three `Evaluate` on tick 10 → three `RejectedSessionNotReady`; 1 warning counted, and it matches the existing "arrived before SessionReady" text.
- **Flush** — after the case above, `FlushRejectionSummary()` → 1 more warning; it contains `Tick 10`, `2 further rejections` and `SessionNotReady=2`. A second `FlushRejectionSummary()` → no further warning.
- **Rejection after a flush, same tick** — three identical rejections on tick 10, flush, one more identical rejection on tick 10 → that rejection produces no warning; a second flush → 1 warning that contains `1 further rejections` and `SessionNotReady=1`.
- **Flush with nothing suppressed** — one rejection, then flush → 1 warning in total.
- **Two results, one client** — an unknown-entity rejection and a not-owner rejection for the same client on one tick → 2 full warnings.
- **Two clients, one result** — the same result for clients 7 and 8 on one tick → 2 full warnings.
- **New tick without a flush** — three identical rejections on tick 10, then one on tick 11 → warnings in this order: full (tick 10), summary for tick 10 with count 2, full (tick 11).
- **Observer hook** — five rate-limited `NotifySkillUsed` rejections on one tick with an observer → the hook fired 5 times; 1 full warning counted.

**Regression**
- **Story 010** — `TickLoop_CrossCuttingGuards_tests.cs` has no diff and its 23 tests pass.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_Dispatch_tests.cs` — must exist and pass; `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_tests.cs` — unchanged, must still pass. The suite total before this story is 2059; record the total after it at `/story-done`.

**Status**: [x] Created — `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_Dispatch_tests.cs` (24 tests); suite 2083 / 2083 on 2026-10-09

---

## Dependencies

- Depends on: Story 010 (Complete — `CrossCuttingRpcGuardChain`), Story 029 (Complete — the `SetTarget` tag). ADR-014 Accepted (2026-10-09).
- Unlocks: Story 036 (Inbound Request Dispatcher).

---

## Completion Notes
**Completed**: 2026-10-09
**Criteria**: 14/14 passing
- Suite: 2083 total, 2083 passed, 0 failed (totals read from `TestResults.xml`, run of 2026-10-09 10:32 local, after the review fixes). `TickLoop_CrossCuttingGuards_Dispatch_Tests` 24/24; `TickLoop_CrossCuttingGuards_Tests` 23/23 with no diff on its file. No compile error.
- "Gap 0 skips the rate-limit state": the behaviour (any number of requests accepted) is tested; that `_lastAcceptedTick` is neither read nor written is verified by reading the code only — it is not observable through the public API, and no reflection test was added.
- Decisions taken at `/story-readiness` 2026-10-09: `IsRateLimited` kept; `FlushRejectionSummary()` clears the counts only.
**Deviations** (advisory):
- The story says one private method replaces the four inline `Debug.LogWarning` calls. The code has one private decision method (`ShouldLogInFull`) and the log call stays at the four sites, so each message is built only when it is logged.
- The throttle key is one `ulong` (client id shifted left 8 bits, the result in the low byte) in a set pre-sized to `ZoneBufferPool.MAX_PLAYERS_PER_ZONE × 4`, not a set of (client id, result) pairs: no enum is hashed. From the code review.
- 24 tests against 17 QA cases. Added beyond the list: the `default` branch throws (`Evaluate` and `IsRateLimited`), mixed results in one summary, an accepted request on a new tick logs the pending summary, false `IsLiveOwner` results log nothing.
- `Evaluate` is about 90 lines (review limit 40); not split.
**Test Evidence**: `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_Dispatch_tests.cs` (24 tests, passing); `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_tests.cs` unchanged, passing.
**Code Review**: Complete — `/code-review` 2026-10-09 (`unity-specialist`, `qa-tester`; ADR-014 check by the main session): APPROVED WITH SUGGESTIONS, nothing blocking; the nine suggestions were applied and the suite re-run.
**Tech debt**: TD-064 (`_lastAcceptedTick` key).
**For Story 036**: every descriptor evaluated in one tick must carry the same `CurrentTick`, or the throttle resets at each change; `Evaluate` throws for a tag value outside the enum, so the dispatcher must not cast raw wire bytes to `RpcTypeTag`.
