# Story 006: Priority-Path Cap & Two-Path Delivery Model

> **Epic**: Networking Core
> **Status**: Ready
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

- [ ] **AC-NC-34** [BLOCKING] (Integration): Given a server tick where 12 R-OD messages are queued for client A (4 above `PRIORITY_PATH_CAP=8`), when the tick's Path 1 flush occurs, then exactly 8 are emitted in that tick in emission order, the remaining 4 appear in the following tick(s) in the same order, and all 12 are delivered within ≤2 ticks total — none dropped.
- [ ] **AC-NC-35** [BLOCKING] (Integration): Given a Path 1 queue for client A already at 8 queued R-OD messages, when an `EnhancementOutcomeBroadcast` is enqueued before the tick's flush, then it is placed at position 1 of the current tick's queue (displacing the oldest non-exempt message to the next tick), and the displaced message appears at position 1 of the following tick's capture.
- [ ] **AC-CCR-05** [BLOCKING] (Integration): Given a client with 9 non-exempt R-OD messages queued for the same tick, when the tick flush runs, then exactly 8 are delivered that tick and the 9th is sent at the start of the next tick's flush — no message dropped.

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

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (envelope), Story 001 (test harness)
- Unlocks: Story 009 (tick loop calls this queue's Flush), Story 011 (commit-before-broadcast uses the enhancement-exempt path), Story 026 (GoldSyncEvent forced delivery uses this queue for its R-OD fallback)
