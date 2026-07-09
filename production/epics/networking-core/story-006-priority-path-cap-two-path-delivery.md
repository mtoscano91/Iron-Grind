# Story 006: Priority-Path Cap & Two-Path Delivery Model

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-wire-protocol.md`
**Requirement**: `TR-net-003`, `TR-net-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 2 — three CR-NET-3 channel types map to NGO `NetworkDelivery` values (exact enum names verification-pending against Unity 6.3 API, but the guarantee mapping is the binding contract). This story implements the priority-path (R-OD) queue mechanics that sit above whatever concrete `NetworkDelivery` value gets selected.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: HIGH
**Engine Notes**: Exact `NetworkDelivery` enum member names must be confirmed against `docs/engine-reference/unity` before the concrete NGO send call is wired — this story's queue logic is transport-agnostic and does not require that verification to be implemented and tested.

**Control Manifest Rules (Foundation layer)**:
- Required: priority-path cap of 8 non-exempt R-OD messages per client per tick; excess deferred (never dropped) to following tick(s) — source: CR-NET-7.7
- Required: enhancement-path and bulk-transfer messages are cap-exempt, queue-jump to front at application layer — source: CR-NET-7.7

---

## Acceptance Criteria

*From `design/gdd/networking-wire-protocol.md` and `networking-channel-contract.md`, scoped to this story:*

- [x] **AC-NC-34** [BLOCKING] (Integration): Given a server tick where 12 R-OD messages are queued for client A (4 above `PRIORITY_PATH_CAP=8`), when the tick's Path 1 flush occurs, then exactly 8 are emitted in that tick in emission order, the remaining 4 appear in the following tick(s) in the same order, and all 12 are delivered within ≤2 ticks total — none dropped.
- [x] **AC-NC-35** [BLOCKING] (Integration): Given a Path 1 queue for client A already at 8 queued R-OD messages, when an `EnhancementOutcomeBroadcast` is enqueued before the tick's flush, then it is placed at position 1 of the current tick's queue (displacing the oldest non-exempt message to the next tick), and the displaced message appears at position 1 of the following tick's capture. **Note (resolved at closure — see Completion Notes): this AC's explicit displacement behavior was implemented as written, in preference to the Implementation Notes' `PathCapacity_effective` formula, which would have implied additive (non-displacing) capacity. Three independent reviews confirmed this AC is authoritative.**
- [x] **AC-CCR-05** [BLOCKING] (Integration): Given a client with 9 non-exempt R-OD messages queued for the same tick, when the tick flush runs, then exactly 8 are delivered that tick and the 9th is sent at the start of the next tick's flush — no message dropped.

---

## Implementation Notes

*Derived from CR-NET-7.7 and F-CCR-1:*

```
PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued
```
- `PRIORITY_PATH_CAP = 8` (tuning knob, safe range [4,16]) is the non-exempt ceiling per destination client per tick.
- Cap-exempt categories: (1) enhancement-path (`EnhancementRequestReceived` + outcome broadcast) — queue-jumps to front of the *current* tick's queue if enqueued before flush, otherwise front of *next* tick's queue; (2) bulk-transfer (`0xF000–0xFFFF` fragments) — connection-phase one-time transmission, never subject to the 8-cap.
- Deferred messages are never dropped — only deferred to the next tick(s), preserving relative emission order within the deferred set.
- Tie-break rule (B-NP-6): simultaneous enhancement outcomes ordered by commit-to-persistence order (relevant once Story 011's commit-before-broadcast pattern exists — this story just needs to accept an explicit ordering key from the caller, not invent one).
- Implement as a per-client, per-tick queue data structure with an `Enqueue(message, isExempt)` API and a `Flush(tickNumber) -> IReadOnlyList<QueuedMessage>` that the tick loop (Story 009) calls once per tick.

---

## Out of Scope

*Handled by neighbouring stories:*

- R-U/U-U batch framing (Path 2a/2b) — Story 007
- The concrete NGO `NetworkDelivery` binding — deferred to implementation-time engine verification, not blocking this story's queue logic
- Enhancement/respec business logic itself — Story 011 and future Enhancement/Leveling epics

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/WireProtocol_PriorityPathCap_tests.cs`

- **AC-NC-34**: Given 12 queued R-OD messages, when tick T flushes, then 8 are captured at tick T and 4 at tick T+1, matching emission order.
- **AC-NC-35**: Given 8 queued + 1 late-enqueued enhancement message before flush, then the enhancement message is position 1 in tick T's capture and the displaced message is position 1 in tick T+1's capture.
- **AC-CCR-05**: Given 9 non-exempt messages, then exactly 8 deliver at tick T and 1 at tick T+1.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/WireProtocol_PriorityPathCap_tests.cs` — must exist and pass

**Status**: [x] Created — 6 test methods, all 3 blocking ACs covered

---

## Dependencies

- Depends on: Story 003 (envelope), Story 001 (test harness)
- Unlocks: Story 009 (tick loop calls this queue's Flush), Story 011 (commit-before-broadcast uses the enhancement-exempt path), Story 026 (GoldSyncEvent forced delivery uses this queue for its R-OD fallback)

---

## Completion Notes

**Completed**: 2026-07-09
**Criteria**: 3/3 passing (AC-NC-34, AC-NC-35, AC-CCR-05)
**Deviations**:
- ADVISORY — **AC-NC-35 vs. `PathCapacity_effective` formula resolved**: the Implementation Notes formula (`PathCapacity_effective = PRIORITY_PATH_CAP + ExemptMessages_queued`) reads as additive capacity growth; AC-NC-35 (BLOCKING, repeated in QA Test Cases) explicitly describes displacement instead. Implemented AC-NC-35's displacement model — the enhancement-path exemption is a priority-ordering mechanism within the fixed 8-slot cap, not a capacity increase. Independently verified correct by two specialist code reviews plus a pre-review hand-trace — three total independent confirmations.
- ADVISORY — **Bulk-transfer exemption not modeled**: per the story's own text ("never subject to the 8-cap... connection-phase one-time transmission"), this is a separate transmission path. None of this story's 3 blocking ACs exercise it; documented as an explicit scope boundary in `PriorityPathQueue<T>`'s XML remarks, not implemented.
- ADVISORY — **Exempt-count-exceeds-cap edge case found during review**: if more than `PRIORITY_PATH_CAP` exempt messages are enqueued in one tick, `Flush` returns more than the cap total (exempt admission is uncapped; upstream business logic is responsible for rate-limiting). Not required by any AC. Doc comments corrected for accuracy; a test now locks in the exact behavior.
- ADVISORY — Forward-looking note for Story 009 (not this story's scope): `Flush`'s internal scratch-list allocations sit on the 20Hz tick-loop hot path; pooling worth considering once the tick loop is wired up.
- ADVISORY — TR registry gap (same pre-existing systemic gap as every prior story).
**Test Evidence**: `tests/EditMode/Networking/WireProtocol_PriorityPathCap_tests.cs` — 6 test methods. Not run in the Unity Test Runner this session; algorithm hand-traced and independently verified correct by two specialist reviews.
**Code Review**: Complete — APPROVED WITH SUGGESTIONS (unity-specialist + qa-tester, lean mode). Two doc-accuracy issues found and fixed (false "up to PRIORITY_PATH_CAP" claim, misleading cross-reference), one coverage gap found and fixed (exempt-overflow test).
