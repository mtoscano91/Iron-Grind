# Story 015: TTL Expiry, Zone Crash Recovery & In-Flight RPC Edge Cases

> **Epic**: Networking Core
> **Status**: Ready
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3-4 hours

## Context

**GDD**: `design/gdd/networking-session.md`
**Requirement**: `TR-net-006`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: `docs/architecture/ADR-004-networking-library-ngo.md`
**ADR Decision Summary**: Same governing ADR as the rest of the session cluster — this story is the crash/failure-mode closing chapter: what happens when the tick loop, persistence write, or transport fails mid-sequence.

**Engine**: Unity 6.3 LTS (6000.3) | **Risk**: MEDIUM
**Engine Notes**: None beyond the shared session-cluster concerns.

**Control Manifest Rules (Foundation layer)**:
- Required: CR-NET-6.5 TTL expiry sequence — complete current Beat boundary → process death if HP=0 → persist final state → broadcast `PlayerLeftZone` → release resources → any subsequent connection starts fresh — source: `networking-session.md`
- Required: no game-logic RPC processed in `Disconnected_SessionActive`, `Reconnecting`, or `Disconnected_SessionExpired` — source: EC-NET-6

---

## Acceptance Criteria

*From `design/gdd/networking-session.md`, scoped to this story:*

- [ ] **AC-NC-12** [BLOCKING] (Logic): Given a player in `Disconnected_SessionActive`, when the tick counter reaches `sessionExpiryTick`, then `OnPersistenceWriteCompleted(characterId, SessionExpiry)` fires before any resource release; the transition to `Disconnected_SessionExpired` fires; entity removed; resources released; a subsequent connection starts at `Connecting`.
- [ ] **AC-NC-16** [BLOCKING]: Given a server crash (via `IServerCrashInjector`) immediately after the persistence write completes and before emitting the outcome message, when the server restarts and the player reconnects, then the session handshake delivers the post-outcome item state without replaying the animation.
- [ ] **AC-NC-27** [BLOCKING]: Given a player who submits an enhancement attempt (`RequestID=X`) processed successfully, when the same client sends a second request with `RequestID=X`, then the server rejects it as a duplicate unconditionally — even when >30 seconds have elapsed (no time window on dedup, per `LastEnhancementRequestID`).
- [ ] **AC-NC-35** [BLOCKING] (Integration — fragment reassembly timeout): Given a client joining a zone where `ZoneStateSnapshot` is fragmented and `ITransportFaultInjector.DropSnapshotFragment(ushort.MaxValue)` drops the last fragment, when `FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` elapses, then the client emits `ZoneSnapshotRequest`; the zone-entry gate remains closed throughout; when reassembly completes, the gate opens within 100ms of the final fragment.
- [ ] **AC-NC-40** [BLOCKING] (Logic — snapshot retransmit limit): Given `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS=3` and repeated fragment drops, when the 3rd reassembly timeout elapses, then `OnSnapshotRetransmitAttempt` fires 3 times in order with correct `(attemptNumber, maxAttempts)`, after which the client drops the connection and begins a fresh reconnect; the gate never opened.
- [ ] **AC-NC-42** [BLOCKING] (Logic — in-flight RPC at disconnect boundary): Given a connected player with `heldFreePoints=1`, when heartbeat timeout is processed on tick T while an `AllocateFreePointRequest` is simultaneously in the input queue, then either the RPC was fully processed (persisted, `heldFreePoints=0`) OR fully dropped (`heldFreePoints=1`) — never a partial-application state.
- [ ] **AC-NC-34-CRASH** [BLOCKING] (Integration, session's own AC-NC-34 — distinct from wire-protocol's identically-numbered AC): Given an enhancement `RequestID=X` committed to persistence, and a crash after write but before broadcast, when the server restarts and the player reconnects, then the handshake delivers post-enhancement state, a re-submitted `RequestID=X` is rejected, and item state is unchanged by the rejected re-submit.

---

## Implementation Notes

*Derived from CR-NET-6.5, EC-NET-6, EC-NET-9, EC-NET-10:*

- CR-NET-6.5 expiry sequence, strict order: (1) complete current Beat boundary — no partial-Beat state ever persisted; (2) process death if `CurrentHP==0` at any point through step 1 — respawn position, HP restore, `wasKilledWhileDisconnected=true`, no death penalties; (3) atomic persistence write (commit-before-release, mirrors Story 011's pattern); (4) broadcast `PlayerLeftZone` (R-OD); (5) release all session resources (session slot, ghost memory record, token entry, zone entity slot); (6) any subsequent connection starts fresh at `Connecting`.
- EC-NET-6 in-flight RPC: the state-machine transition IS the authority boundary — if the RPC's processing completes before the transition advances, it's applied normally (commit-before-broadcast still applies); if the transition advances first, the RPC is dropped unprocessed. No partial-application is ever observable in persistence.
- EC-NET-9 duplicate enhancement dedup has NO time window — rejected unconditionally on `requestId == LastEnhancementRequestID` regardless of elapsed time; `LastEnhancementRequestID` persists atomically with the enhancement outcome (Story 011's pattern), never resets across sessions.
- EC-NET-10 zone crash: sessions with completed pre-crash persistence writes are intact and reconnect re-establishes with a new token (Story 016's `ActiveSessionTokens` is cleared by restart); ghost sessions whose final-state write hadn't completed lose ghost-period progress back to the last checkpoint; in-flight enhancements are protected from double-commit by the `LastEnhancementRequestID` atomicity, uncommitted attempts roll back.
- Snapshot retransmit bounds worst-case stall to `MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS × FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS` (3×10s=30s default) before the client gives up and reconnects fresh.

---

## Out of Scope

*Handled by neighbouring stories:*

- Core state transitions — Stories 012–014
- Ghost-specific TTL/crash handling (this story covers the *player connection* TTL/crash; ghost-specific reward/snapshot rules are the Ghost Session cluster) — Stories 017–021

---

## QA Test Cases

*Test file*: `tests/PlayMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs`

- **AC-NC-12**: Given tick advance to `sessionExpiryTick`, then persistence-write-before-release ordering holds, entity removed, fresh reconnect confirmed.
- **AC-NC-16**: Given a post-write, pre-broadcast crash, then reconnect handshake shows post-outcome state, no animation replay.
- **AC-NC-27**: Given a duplicate `RequestID` re-submitted after >30s, then rejected unconditionally.
- **AC-NC-35**: Given a dropped final fragment, then retransmit request fires at timeout, gate stays closed, opens within 100ms of successful reassembly.
- **AC-NC-40**: Given 3 consecutive failed reassembly cycles, then 3 `OnSnapshotRetransmitAttempt` calls fire in order, followed by a fresh reconnect.
- **AC-NC-42**: Given a race between heartbeat-timeout and an in-flight RPC, then persistence never shows a partial-application state.
- **AC-NC-34-CRASH**: Given a post-commit pre-broadcast crash on enhancement, then reconnect handshake is correct and re-submission is rejected.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/Networking/Session_TTLExpiry_ZoneCrash_tests.cs` OR documented playtest evidence in `production/qa/evidence/`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001 (`IServerCrashInjector`, `ITransportFaultInjector`), Story 011 (commit-before-broadcast pattern), Story 012–014 (state machines)
- Unlocks: Story 019/021 (ghost death/cleanup reuse this crash-recovery reasoning)
