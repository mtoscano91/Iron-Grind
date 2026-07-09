# Story 010: Cross-Cutting RPC Guards — EntityID Validity, Session-Ready, Rate Limiting

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-core.md`
**Requirement**: `TR-net-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Decision 4 — `SenderEntityID` is a project `EntityID`, not NGO's `clientId`; the server validates the `clientId ↔ EntityID` mapping. These guards are the enforcement layer for that validation, applied to every inbound RPC before it reaches game logic.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: None beyond the `clientId ↔ EntityID` mapping already required by ADR-004 Decision 4.

**Control Manifest Rules (Foundation layer)**:
- Required: drop any inbound RPC referencing an `EntityID` not present in the current zone session — do not forward to game logic — source: Cross-Cutting Constraint 1
- Required: no inbound RPC forwarded to game logic until that client's `SessionReady` has been sent; RPCs arriving before session-ready are dropped, not queued — source: Cross-Cutting Constraint 2
- Required: `AllocateFreePoint` rate limit 200ms/entity; `NotifySkillUsed` rate limit 50ms/entity (one per tick at 20Hz); excess rejected with `RateLimitExceeded`, not queued — source: Cross-Cutting Constraint 3

---

## Acceptance Criteria

*From `design/gdd/networking-core.md`, scoped to this story:*

- [ ] **AC-NC-02** [BLOCKING]: Given a test client that sends an `AllocateFreePointRequest` for an `EntityID` it does not own, when the server processes the message, then the request is discarded, the target entity's stats are unchanged, and the anomaly is logged.
- [ ] **AC-NC-20** [BLOCKING]: Given a player who sends `AllocateFreePoint` requests at 100ms intervals, when the server processes the requests, then requests within 200ms of a prior accepted request are rejected with `RateLimitExceeded`; requests separated by ≥200ms are accepted.
- [ ] **AC-NC-46** [BLOCKING] (Logic): Given a test client sending `NotifySkillUsed` RPCs at 10ms intervals (5× the default rate limit), when the server processes 10 consecutive RPCs within 100ms, then at most 2 are accepted (one per 50ms `NOTIFY_SKILL_USED_RATE_LIMIT_MS`), the remaining 8 are rejected with `RateLimitExceeded`, and server game state is unaffected by rejected RPCs.
- [ ] **AC-NC-23** [BLOCKING] (session-ready gate, `networking-session.md`): Given a newly connected client that has not yet received `SessionReady`, when the client sends a valid `AllocateFreePointRequest`, then the server drops the RPC without forwarding it to game logic and logs the anomaly; `heldFreePoints` and stats are unchanged.

---

## Implementation Notes

*Derived from Cross-Cutting Constraints 1-3:*

- **EntityID validity gate**: before any RPC handler runs, check `SenderEntityID` against the current zone session's known-entity set (server-maintained `clientId ↔ EntityID` mapping per ADR-004 Decision 4). Unknown EntityID → drop silently to game logic (but log an anomaly), never forward.
- **Session-ready gate**: maintain a per-client "has SessionReady been sent" flag; any inbound RPC before that flag is set is dropped (not queued for later) and logged.
- **Rate limiting**: per-entity, per-RPC-type minimum inter-request interval, enforced via a tick-based (not wall-clock) "last accepted request tick" map. `AllocateFreePoint`: 200ms (`ALLOC_FREE_POINT_RATE_LIMIT_MS`, tuning knob [100,1000]). `NotifySkillUsed`: 50ms (`NOTIFY_SKILL_USED_RATE_LIMIT_MS`, tuning knob [25,200], = one per tick at 20Hz, the physical maximum for a legitimate client). Excess requests are rejected with `RateLimitExceeded`, never queued for later processing.
- These three guards compose as a pipeline every inbound RPC passes through, in order: EntityID validity → session-ready → rate limit → ownership check (AC-NC-02's specific "does this entity belong to this client" check) → forward to game logic. Implement as a reusable guard chain, not per-RPC-type duplicated logic.

---

## Out of Scope

*Handled by neighbouring stories:*

- `AC-NC-01` (position-boundary discard) — explicitly deferred; the GDD itself notes "Precondition: Zone Instancing GDD must define zone boundary before this AC is testable" — not implemented until that GDD exists
- The session-ready flag's own lifecycle (when it gets set) — Story 012/013 (session state machine) own the `SessionReady` emission itself; this story only consumes the flag
- Commit-before-broadcast — Story 011

---

## QA Test Cases

*Test file*: `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_tests.cs`

- **AC-NC-02**: Given an `AllocateFreePointRequest` for a non-owned EntityID, then it's discarded, stats unchanged, anomaly logged.
- **AC-NC-20**: Given requests at 100ms intervals, then alternating accept/reject at the 200ms boundary as described.
- **AC-NC-46**: Given 10 `NotifySkillUsed` RPCs at 10ms intervals, then exactly ≤2 accepted, 8 rejected, no game-state side effects from rejected RPCs.
- **AC-NC-23**: Given a client without `SessionReady` sent, then any RPC is dropped without reaching game logic.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/Networking/TickLoop_CrossCuttingGuards_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009 (tick-based timing), Story 012 (session-ready flag source)
- Unlocks: Every RPC-handling story in downstream system epics (all inbound RPCs pass through this guard chain)
