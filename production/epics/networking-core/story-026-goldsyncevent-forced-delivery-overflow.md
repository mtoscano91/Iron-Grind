# Story 026: GoldSyncEvent Forced-Delivery Overflow Policy

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2-3 hours

## Context

**GDD**: `design/gdd/networking-message-criticality.md`
**Requirement**: `TR-net-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR — this policy is the escalation path from Story 007's R-U overflow-drop into Story 006's R-OD priority-path when gold sync has been dropped too many consecutive ticks.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: LOW
**Engine Notes**: None.

**Control Manifest Rules (Foundation layer)**:
- Required: after `GOLD_MAX_CONSECUTIVE_DROP` consecutive overflow-drop ticks, emit standalone `GoldSyncEvent` on R-OD priority path next tick — source: MCR-4
- Required: `GoldSyncEvent.newBalance` is always absolute, never re-encoded as a delta — source: MCR-2, this echoes Story 007's AC-NC-19

---

## Acceptance Criteria

*From `design/gdd/networking-message-criticality.md`, scoped to this story:*

- [ ] **AC-MCR-01** [BLOCKING] (Integration): Given a simulated zone with 50 players causing R-U batch overflow for 3 consecutive ticks for at least one client, when `GoldSyncEvent` is overflow-dropped on ticks T, T+1, T+2, then on tick T+3 the server emits a standalone `GoldSyncEvent` on the R-OD priority path, and the client's gold balance display matches the server's authoritative balance within 1 tick of receiving the forced delivery. *Requires `IZoneTestConfigurator.SetBatchSizeLimit(int)` to force deterministic overflow — if absent, this AC is blocked pending that test-harness addition.*
- [ ] **AC-MCR-07** [BLOCKING] (Integration): Given a client where forced delivery has fired continuously for `FORCED_DELIVERY_CONSECUTIVE_TICKS` consecutive ticks without a successful normal R-U delivery, when the threshold is reached, then the server logs a `GoldSyncForcedDelivery` critical anomaly with the affected EntityID and the consecutive-tick count.

---

## Implementation Notes

*Derived from MCR-4, F-MCR-1:*

```
ForcedDeliveryRate_per_client = TICK_RATE_HZ ÷ (GOLD_MAX_CONSECUTIVE_DROP + 1)
```
- Maintain a per-client, per-character overflow-drop counter, incremented each tick the R-U batch overflow policy (Story 007) drops that character's `GoldSyncEvent`; reset only on confirmed delivery (never on zone transition/disconnect — a session that reconnects resumes its drop count, it is not forgiven).
- After the counter reaches `GOLD_MAX_CONSECUTIVE_DROP` (default 3, safe range [1,5]), the NEXT tick must emit a standalone `GoldSyncEvent` on the R-OD priority path (Story 006's queue), using a `MessageTypeID` in the `0xE000–0xEFFF` priority/control range. This is subject to `PRIORITY_PATH_CAP` — if the queue is already at cap, forced delivery goes to the front of the next tick's queue, behind enhancement-exempt messages but ahead of ordinary non-exempt ones.
- If forced delivery itself fires for more than `FORCED_DELIVERY_CONSECUTIVE_TICKS` (default 100, ~5s at 20Hz) consecutive ticks without a normal R-U delivery succeeding in between, log a `GoldSyncForcedDelivery` critical anomaly — this signals sustained overflow pressure worth investigating, not a per-occurrence log spam.
- `newBalance` must never be re-encoded as a delta during this forced-delivery path — reuse the exact same absolute-value encoding as the normal R-U path (Story 007's AC-NC-19 invariant applies here unchanged).

---

## Out of Scope

*Handled by neighbouring stories:*

- The R-U batch overflow-drop policy this escalates from — Story 007
- The R-OD priority-path queue this escalates into — Story 006
- The message routing table this policy's channel choice derives from — Story 025

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/MessageRouting_GoldSyncForcedDelivery_tests.cs`

- **AC-MCR-01**: Given 3 consecutive drop ticks, then tick T+3 emits a standalone R-OD `GoldSyncEvent` matching authoritative balance.
- **AC-MCR-07**: Given `FORCED_DELIVERY_CONSECUTIVE_TICKS` consecutive forced-delivery ticks, then a `GoldSyncForcedDelivery` critical anomaly logs with correct EntityID and tick count.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/MessageRouting_GoldSyncForcedDelivery_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006 (priority-path queue), Story 007 (R-U overflow-drop counter), Story 025 (routing table)
- Unlocks: None — completes the gold-sync delivery guarantee chain
