# Story 012: Player Connection State Machine — Core Transitions

> **Epic**: Networking Core
> **Status**: Complete
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-session.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: NGO's connection lifecycle callbacks (`OnClientConnectedCallback`, `OnClientDisconnectCallback`) map directly onto the connection-driven execution category (Story 009) and this state machine's transitions (ADR-004 Consequences).

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: Binding to NGO's actual connection callbacks is deferred to implementation time; this story's state machine logic is testable against injected transition triggers independent of that binding.

**Control Manifest Rules (Foundation layer)**:
- Required: `ST-NET-1` five states (`Connecting`, `Connected`, `Disconnected_SessionActive`, `Reconnecting`, `Disconnected_SessionExpired`) — source: `networking-session.md`
- Required: heartbeat timeout is the sole trigger for `Connected → Disconnected_SessionActive`; explicit disconnect and session-stealing skip straight to `Disconnected_SessionExpired` — source: CR-NET-6.1, B-NP-7

---

## Acceptance Criteria

*From `design/gdd/networking-session.md`, scoped to this story — the core (non-reconnect) transitions; Reconnecting-state transitions are Story 013:*

- [x] **AC-NC-10** [BLOCKING] (Logic): Given a connected player with `HEARTBEAT_TIMEOUT_SECONDS=3` (test config), when the server tick loop advances 61 ticks without any inbound packet, then `OnSessionStateTransitioned(accountId, Connected, Disconnected_SessionActive, "HeartbeatTimeout")` fires, the entity remains in the zone, no session resources are released. *No wall-clock wait — advance tick counter programmatically.*
- [x] **AC-NC-26** [BLOCKING]: Given a connected player who sends an explicit disconnect, when the server processes it, then: the 5-minute session TTL is skipped entirely; final character state is written to persistence immediately; the entity is removed from the zone; other clients receive `PlayerLeftZone` with `disconnectType=graceful`. No `Disconnected_SessionActive` state is entered.
- [x] **AC-NC-39-SESSION** [BLOCKING] (session-stealing, `networking-session.md`'s AC-NC-37 in the original doc numbering — renamed here to avoid collision with wire-protocol's AC-NC-37): Given a player in `Connected` state, when a new authenticated connection arrives for the same account, then: `OnSessionInvalidatedBySteal` fires for the prior session; `OnPersistenceWriteCompleted(characterId, SessionSteal)` fires; the prior session transitions to `Disconnected_SessionExpired`; the prior transport connection closes; the new connection proceeds to `Connecting`. Ordering: prior-session cleanup completes before the new connection's `Connecting` transition fires.
- [x] **AC-NC-39-CONNECTING** [BLOCKING] (`Connecting` timeout, `networking-session.md`'s AC-NC-39): Given `CONNECTING_TIMEOUT_SECONDS=10` (test config), when the tick counter advances 200 ticks (`CONNECTING_TIMEOUT_TICKS`) without auth completion, then `OnSessionStateTransitioned(accountId, Connecting, Disconnected_SessionExpired, "ConnectingTimeout")` fires, the pending session slot is released, and `OnPersistenceWriteCompleted` does NOT fire (no session was ever established).

---

## Implementation Notes

*Derived from ST-NET-1 (this story's subset: transitions 1-6, 8):*

| From | To | Trigger |
|---|---|---|
| — | `Connecting` | Client initiates transport connection |
| `Connecting` | `Connected` | Auth succeeds, zone assignment completes |
| `Connecting` | `Disconnected_SessionExpired` | Auth fails / zone full / client drops during handshake / `CONNECTING_TIMEOUT_SECONDS` elapses |
| `Connected` | `Disconnected_SessionActive` | Heartbeat timeout, no packet for `HEARTBEAT_TIMEOUT_SECONDS` |
| `Connected` | `Disconnected_SessionExpired` | Explicit disconnect (skip TTL, write immediately, release) |
| `Connected` | `Disconnected_SessionExpired` | Session-stealing (new auth connection for same account) |

- All timing uses tick-based comparison (`IsTickExpired` from Story 005), never wall-clock (`Thread.Sleep` forbidden in tests, per every relevant AC's own note).
- `SessionState` enum is the production enum declared in Story 001 (`Connecting=0, Connected=1, Disconnected_SessionActive=2, Reconnecting=3, Disconnected_SessionExpired=4`) — reuse it, do not redeclare.
- `OnSessionStateTransitioned` callback signature (from `INetworkTestObserver`, Story 002): `(accountId, fromState, toState, trigger: string)` — trigger strings used across the epic: `"HeartbeatTimeout"`, `"ConnectingTimeout"`, `"SessionSteal"`, `"NewConnection"`, plus the Story 013/014/015 reconnect-specific triggers.

---

## Out of Scope

*Handled by neighbouring stories:*

- `Reconnecting` state transitions, PendingPurchase reconciliation on reconnect — Story 013
- TTL expiry sequence detail (CR-NET-6.5) and zone crash recovery — Story 015
- Zone-level state machine (ST-NET-2) — Story 014

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/Session_ConnectionStateMachine_Core_tests.cs`

- **AC-NC-10**: Given 61 silent ticks at `HEARTBEAT_TIMEOUT_SECONDS=3`, then the transition fires with `trigger="HeartbeatTimeout"`.
- **AC-NC-26**: Given an explicit disconnect, then TTL is skipped, persistence writes immediately, `PlayerLeftZone(graceful)` broadcasts.
- **AC-NC-39-SESSION**: Given a concurrent second connection for the same account, then the prior session is invalidated and cleaned up before the new connection proceeds.
- **AC-NC-39-CONNECTING**: Given 200 silent ticks in `Connecting`, then the transition fires with `trigger="ConnectingTimeout"` and no persistence write occurs.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/Session_ConnectionStateMachine_Core_tests.cs` — must exist and pass

**Status**: [x] Created — 25 test methods, all 4 blocking ACs covered (see Completion Notes)

---

## Dependencies

- Depends on: Story 001/002 (test harness), Story 009 (tick-based timing), Story 008 (heartbeat)
- Unlocks: Story 010 (session-ready gate consumes this state), Story 013 (reconnect builds on this), Story 016 (session token issued on `Connecting→Connected`)

**Note**: `HEARTBEAT_TIMEOUT_SECONDS`'s production default is OQ-NET-1, still BLOCKING/undetermined (recommended 8-12s) — this story's tests inject their own override value and are unaffected, but the production default must be set before launch.

---

## Completion Notes

**Completed**: 2026-07-17
**Criteria**: 4/4 passing (AC-NC-10, AC-NC-26, AC-NC-39-SESSION, AC-NC-39-CONNECTING) — no deferred items
**Deviations**:
- ADVISORY: TR-net-006 not present in `docs/architecture/tr-registry.yaml` (still empty) — same systemic gap already logged as TD-014 for this epic. Implementation proceeded against this story's own embedded AC text, independently verified word-for-word against `networking-session.md`'s actual AC-NC-10/26/37/39 during `/story-readiness`.
- ADVISORY: `/story-readiness` flagged a missing performance-budget note on the story; not added (user chose to proceed straight to implementation). No performance concern surfaced during implementation or review — `EvaluateTimeouts` is O(n) over registered accounts with a reused scratch buffer, no steady-state allocation.
- ADVISORY: two design judgment calls, both approved before implementation and documented inline in `ConnectionStateMachine`'s class remarks — (1) `SessionState.Disconnected_SessionExpired` reused as the synthetic `fromState` for the `— → Connecting` transition (no real prior state exists; adding a 6th enum member would violate the control manifest's "ST-NET-1 five states" rule); (2) `"ExplicitDisconnect"` trigger string, mirroring the existing `PersistenceWriteReason.ExplicitDisconnect` member (neither the story nor the GDD prescribes one).
**Test Evidence**: Logic — `tests/EditMode/Networking/Session_ConnectionStateMachine_Core_tests.cs`, 25 test methods. Not yet run in a real Unity Editor (no compiler available in this sandboxed session — same limitation as every prior story).
**Code Review**: Complete — `/code-review` (lean mode, unity-specialist + qa-tester in parallel). Verdict: APPROVED WITH SUGGESTIONS. Zero BLOCKING findings, including a clean release-stripping-guard pass across all 6 public methods + 1 private helper, and a confirmed-safe hand-trace of the dictionary-mutation-during-enumeration pattern in `EvaluateTimeouts`. One real test-quality gap found and fixed: the AC-NC-39-SESSION ordering test missed a swap between `OnPersistenceWriteCompleted` and `OnSessionStateTransitioned(SessionSteal)` — closed with one added assertion. Also added per user direction: 4 missing null-guard tests and 2 parity/robustness tests (`RecordInboundActivity` no-op during `Connecting`; `FailConnecting` throw-guard parity). Final test count: 25 (19 → 25).
