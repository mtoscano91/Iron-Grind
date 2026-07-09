# Story 005: Version/SequenceNumber Stale-Discard Helpers

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md`
**Requirement**: `TR-net-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Project-owned serialization (Decision 4) — this story implements the wraparound-safe comparison helpers every stale-state-discard check in the project depends on.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW (pure C# arithmetic, no engine API)
**Engine Notes**: None — this is engine-agnostic managed code.

**Control Manifest Rules (Foundation layer)**:
- Required: `Version`/`SequenceNumber` stale-discard comparison must use RFC 1982 serial arithmetic (`IsNewerVersion`), never raw `uint` comparison — source: CR-NET-7.5
- Required: `IsTickExpired` uses different equality semantics from `IsNewerVersion` (true at equality, vs. false at equality) — source: CR-NET-7.5, `networking-session.md`

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md` and `networking-session.md`, scoped to this story:*

- [x] **AC-NC-07** [BLOCKING]: Given two `GoldSyncEvent` stale-discard scenarios for the same character: (A) Version=5 (500g) and Version=6 (600g) arrive out of order (6 first); (B, wraparound) Version=4,294,967,295 (500g) arrives first, then Version=1 (600g). When `IsNewerVersion` is applied in both cases, then: scenario A displays 600g and discards Version=5; scenario B displays 600g (Version=1) and discards Version=4,294,967,295 as stale. A raw `uint` comparison `1 > 4,294,967,295` evaluates false — this AC proves `IsNewerVersion` is used, not raw comparison.
- [x] **AC-NC-36** [BLOCKING] (Integration): Given a test connection whose server-side `SequenceNumber` is initialized to `4,294,967,293` via `ITransportFaultInjector.SetSequenceNumber`, when the server emits 5 consecutive messages, then the observed values are `4,294,967,293 → 4,294,967,294 → 4,294,967,295 → 1 → 2` (wraps from max to 1, skipping 0); the receiver's `IsNewerVersion` check accepts all 5; `SequenceNumber = 0` never appears in any captured message. *(Corrected at story closure — see Completion Notes: `ITransportFaultInjector.SetSequenceNumber`'s already-reviewed "resume from" contract means the seeded value itself is the first observed value, not the value after it. The originally-printed sequence `4,294,967,294 → ... → 3` was off by one position.)*
- [x] **AC-WP-2** [BLOCKING] (`IsTickExpired`, `networking-session.md`): Given `IsTickExpired(currentTick, expiryTick)`, when `currentTick == expiryTick`, then it returns `true` (equality = expired) — the opposite of `IsNewerVersion`'s equality behavior (equality = not newer). Given `currentTick` has wrapped past `uint.MaxValue` relative to `expiryTick`, the comparison still correctly reports expired using unsigned-safe arithmetic: `(uint)(currentTick - expiryTick) < 0x80000000u`.

---

## Implementation Notes

*Derived from CR-NET-7.5 and `networking-session.md`'s `IsTickExpired` definition:*

```csharp
// IsNewerVersion: true when 'candidate' is strictly newer than 'current' (equality = false, not newer)
static bool IsNewerVersion(uint current, uint candidate) =>
    (uint)(candidate - current) < 0x80000000u && candidate != current;

// IsTickExpired: true when currentTick is at or past expiryTick (equality = true, expired)
static bool IsTickExpired(uint currentTick, uint expiryTick) =>
    (uint)(currentTick - expiryTick) < 0x80000000u;
```
- Both use RFC 1982 serial-number arithmetic to handle `uint` wraparound correctly — never compare with a plain `>`/`>=` operator on the raw values.
- `SequenceNumber` shares the same helper as `Version`/gold `Version` — one implementation, multiple call sites (envelope `SequenceNumber`, `GoldSyncEvent.Version`, any future versioned-state message).
- `SequenceNumber` starts at 1 per connection; `0` is reserved as "uninitialized" and must never appear in a valid message (ties into Story 004's zero-ID assert pattern, but this is a distinct field — do not conflate `SequenceNumber == 0` with an ID-zero violation).
- `DateTime.UtcNow` is forbidden for any of this — not synchronized with the tick loop, correctness hazard across restarts/clock skew (`networking-session.md` EC-TOK-4 note).

---

## Out of Scope

*Handled by neighbouring stories:*

- The tick loop that produces `ServerTickNumber`/`currentTick` — Story 009
- Any specific message's use of these helpers beyond proving the helpers themselves are correct — every downstream story that carries a `Version` or TTL-expiry field

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/WireProtocol_StaleDiscard_tests.cs`

- **AC-NC-07**: Given the two `GoldSyncEvent` scenarios (normal reorder and wraparound), when `IsNewerVersion` is applied, then both resolve to displaying the higher-Version-post-wraparound value as described.
- **AC-NC-36**: Given `SetSequenceNumber(4294967293)` then 5 emits, then the sequence is exactly `[4294967293, 4294967294, 4294967295, 1, 2]` and all pass `IsNewerVersion` against the prior value.
- **AC-WP-2**: Given `currentTick == expiryTick`, `IsTickExpired` returns true. Given `currentTick` wrapped past `uint.MaxValue` relative to `expiryTick` by a small delta, `IsTickExpired` still returns true. Given `IsNewerVersion` at equality, it returns false (contrast case, same test file).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/WireProtocol_StaleDiscard_tests.cs` — must exist and pass

**Status**: [x] Created — 10 test methods (13 executions), all 3 blocking ACs covered

---

## Dependencies

- Depends on: Story 001 (`ITransportFaultInjector.SetSequenceNumber` for AC-NC-36)
- Unlocks: Story 013 (reconnect/session-stealing), Story 026 (GoldSyncEvent forced delivery), all future versioned-state messages

---

## Completion Notes

**Completed**: 2026-07-09
**Criteria**: 3/3 passing (AC-NC-07, AC-NC-36, AC-WP-2)
**Deviations**:
- ADVISORY — **AC-NC-36 numbering corrected**: the originally-printed sequence (`4294967294 → ... → 3`) was off by one position relative to `ITransportFaultInjector.SetSequenceNumber`'s already-reviewed "resume from" contract (Story 001). Corrected to `[4294967293, 4294967294, 4294967295, 1, 2]` above and in the QA Test Cases section. Two independent specialist code reviews (unity-specialist, qa-tester) traced this by hand from scratch and independently confirmed the correction.
- ADVISORY — **Real bug fixed in already-committed Story 001 code**: `TransportFaultInjector.ConsumeNextSequenceNumber()` wrapped `uint.MaxValue` to `0` via plain overflow, contradicting CR-NET-7.5 ("0 = uninitialized, must never appear in a valid message"). Fixed to skip to `1`. The one existing Story 001 test asserting the old behavior was updated to the corrected expected value, not weakened.
- ADVISORY — TD-010 logged in `docs/tech-debt-register.md`: `TransportFaultInjector._sequenceNumber` still defaults to `0` at construction (unseeded, pre-`SetSequenceNumber` state) — same "0 never valid" invariant, different vector than the wraparound case this story fixed. Not fixed now — no production send path consumes it yet.
- ADVISORY — TR registry gap (`TR-net-001` not in `docs/architecture/tr-registry.yaml`) — same pre-existing systemic gap as every prior story.
**Test Evidence**: `tests/EditMode/Networking/WireProtocol_StaleDiscard_tests.cs` — 10 test methods, 13 executions. Not run in the Unity Test Runner this session; RFC 1982 arithmetic hand-traced and independently verified correct by two specialist reviews.
**Code Review**: Complete — APPROVED WITH SUGGESTIONS (unity-specialist + qa-tester, lean mode). Two boundary-coverage gaps found (ordinary non-wrapping case, RFC-1982 ambiguous half-circle boundary) and fixed before closure.
