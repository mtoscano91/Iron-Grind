# Story 013: Player Connection State Machine — Reconnect, Session-Stealing & Re-Auth Limits

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-session.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-001-purchase-transaction-integrity.md` (Decision 4 — PendingPurchase reconciliation ordering on reconnect) and `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: ADR-001 Decision 4 (applied 2026-06-27) requires PendingPurchase reconciliation — calling `AddGold(charId, totalCost, CompensatingRefund)` for every `GoldDebited` record — to complete before the reconnect handshake is emitted, and before any `BuyRequest`/`SellRequest` is accepted.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: None beyond the shared ST-NET-1 concerns from Story 012.

**Control Manifest Rules (Foundation layer)**:
- Required: CR-NET-6.4 reconnect sequence order — re-authenticate → PendingPurchase reconciliation → emit handshake → check respec reservation → client seeds `cachedVersion` — source: `networking-session.md`, ADR-001 Decision 4
- Required: TTL does not reset on a failed re-auth attempt — continues from original disconnect tick — source: EC-NET-7

---

## Acceptance Criteria

*From `design/gdd/networking-session.md`, scoped to this story:*

- [ ] **AC-NC-11** [BLOCKING] (Logic): Given a player in `Disconnected_SessionActive` with gold=1,000g and Level=15, when the player reconnects while `IsTickExpired(currentTick, sessionExpiryTick)` is false, then `OnSessionHandshakeEmitted(characterId, wasKilledWhileDisconnected: false, goldBalance: 1000, level: 15)` fires.
- [ ] **AC-NC-13**: Given a player who submitted respec Phase 1 then disconnected: (a) reconnecting within 30s (respec TTL valid) re-presents Phase 2; (b) reconnecting after 30s but within 5 minutes returns the scroll to inventory with a "Respec scroll returned to inventory" notification.
- [ ] **AC-NC-CR64-RECONCILE** [BLOCKING] (ADR-001 Decision 4, PendingPurchase reconciliation): Given a character with one `PendingPurchase` record in `GoldDebited` state, when the character reconnects, then before the handshake is emitted: `AddGold(charId, record.totalCost, CompensatingRefund)` is called, the record transitions to `Refunded` and is deleted, and the reconciliation is logged (`charId, itemId, quantity, totalCost, requestId`). No `BuyRequest`/`SellRequest` is accepted until reconciliation completes.
- [ ] **AC-NC-37** [BLOCKING] (Logic — session-stealing detail beyond Story 012's scope): Given a player in `Connected` state (token T, character C), when a new authenticated connection arrives for the same account, then (a)-(d) all complete (invalidation, persistence write, transition, transport close) before (e) the new connection's `Connecting` transition fires. *Verifies strict ordering, not just eventual consistency.*
- [ ] **AC-NC-38-REAUTH** [BLOCKING] (Logic — `REAUTH_FAILURE_LIMIT` exhausted, `networking-session.md`'s AC-NC-38): Given `REAUTH_FAILURE_LIMIT=3`, when 3 reconnect attempts fail (corrupted tokens), then `OnReAuthAttemptFailed` fires 3 times with correct `(attemptNumber, remainingAttempts)` values in order, immediately followed by the transition to `Disconnected_SessionExpired` with reason `"ReauthLimitExceeded"`. Session TTL does not reset between failures.

---

## Implementation Notes

*Derived from CR-NET-6.4, EC-NET-7, ST-NET-1 transitions 7/9/10/11/12/13:*

| From | To | Trigger |
|---|---|---|
| `Disconnected_SessionActive` | `Reconnecting` | Transport re-established within TTL, token matches |
| `Reconnecting` | `Connected` | Re-auth succeeds — full CR-NET-6.4 sequence runs |
| `Reconnecting` | `Disconnected_SessionActive` | Re-auth fails, TTL not elapsed — TTL continues from original start, no reset (EC-NET-7) |
| `Reconnecting` | `Disconnected_SessionExpired` | Session TTL elapses during re-auth, OR `REAUTH_FAILURE_LIMIT` exhausted, OR session-stealing during re-auth |

- CR-NET-6.4 reconnect sequence, strict order: (1) re-authenticate; (2) PendingPurchase reconciliation — query `GoldDebited` records, `AddGold(CompensatingRefund)` each, mark `Refunded`+delete, log; (3) emit handshake (gold post-reconciliation, `GoldSyncEvent.Version`, zone, HP/MP/XP/level, `heldFreePoints`, `ClassType`, `wasKilledWhileDisconnected`, dedup-continuity fields `lastSeenUseItemRequestId`/`lastSeenBuyRequestId`); (4) check respec reservation — re-present Phase 2 if TTL valid, else release+notify; (5) client seeds `cachedVersion` from handshake.
- This story's `AddGold(CompensatingRefund)` call targets the already-implemented `CurrencySystem.AddGold` from the Currency System epic (Story 005/006) — no new Currency logic needed, just the orchestration call at the right point in this sequence.
- Session-stealing ordering (B-NP-7) is strict: steps (a) invalidate prior session's callbacks, (b) persist, (c) transition prior session, (d) close prior transport — ALL complete before (e) the new connection is allowed to proceed to `Connecting`.

---

## Out of Scope

*Handled by neighbouring stories:*

- Core (non-reconnect) transitions — Story 012
- Full `SessionHandshake` wire schema — pending `OQ-NC-SER-2` (Character Persistence GDD, not yet authored); this story tests via the `OnSessionHandshakeEmitted` observer callback's partial fields, not a complete wire struct
- Session token generation/validation itself — Story 016 (this story calls into it, doesn't implement it)

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/Session_ConnectionStateMachine_Reconnect_tests.cs`

- **AC-NC-11**: Given a reconnect inside the TTL window, then handshake fields match server state.
- **AC-NC-13**: Given respec-Phase-1-then-disconnect, then Phase 2 re-presents within 30s or the scroll returns after.
- **AC-NC-CR64-RECONCILE**: Given a `GoldDebited` PendingPurchase record, then reconciliation completes before handshake, record is refunded+deleted, logged.
- **AC-NC-37**: Given a concurrent second connection, then (a)-(d) provably complete before (e) via sequence-index assertion.
- **AC-NC-38-REAUTH**: Given 3 corrupted-token reconnects, then 3 `OnReAuthAttemptFailed` calls fire in order, followed by expiry; TTL unchanged throughout.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Networking/Session_ConnectionStateMachine_Reconnect_tests.cs` (crosses Currency System + Session state machine boundaries) OR documented playtest evidence

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 012 (base state machine), Story 016 (session token), Currency System Story 005/006 (`AddGold(CompensatingRefund)` — already Complete)
- Unlocks: Story 015 (TTL expiry builds on this), Story 017-021 (Ghost Session reconnect interactions)
