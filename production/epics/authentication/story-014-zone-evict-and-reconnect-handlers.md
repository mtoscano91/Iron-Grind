# Story 014: Zone `Evict` and `Reconnect` handlers

> **Epic**: Authentication
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 8 h

## Context

**GDD**: `design/gdd/authentication.md` — EC-AUTH-6 (zone side), EC-AUTH-7, CR-AUTH-9 step 2 (zone side); AC-AUTH-27 (b, c, e), 33; `networking-session.md` session-stealing protocol; `networking-session-token.md` CR-TOK-4/5/6
**Requirement**: `TR-auth-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3 messages `Evict`/`EvictResult`/`Reconnect`/`ReconnectResult`, Decision 4 token path), with ADR-011
**ADR Decision Summary**: On `Evict` the zone runs its session-steal path (save, close, token deleted) and sends `EvictResult` after the save has completed **or failed**. An admitted entry whose client has not arrived is simply dropped (nothing to save). On `Reconnect` the zone runs `TryValidateAndRotate` and the `REAUTH_FAILURE_LIMIT` count and answers `ReconnectResult { accepted, newToken }`.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: The save is async (persistence queue / tracked tasks); the tick never waits on it (ADR-011). `EvictResult` is queued when the save's completion is drained on the tick.

**Control Manifest Rules (this layer)**:
- Required: zone-side `Admit` / `Evict` / `Reconnect` handlers in `src/ServerLogic/Gateway/` — ADR-016 Decision 6
- Required: the tick never blocks on I/O — ADR-011
- Guardrail: the steal save's worst case (pool open 5 s + command 5 s, capped at 10 s by `PERSISTENCE_WATCHDOG_TICKS`) stays under `GATEWAY_EVICT_TIMEOUT_MS` (11 s) — ADR-016 Tuning Knobs. Time an eviction spends waiting on a held mutation gate (an irreversible write in flight) also counts against the 11 s; if it runs out the gateway answers `ServerError` and the client retries (EC-AUTH-6).

**Performance**: no per-tick cost — the handlers run once per link message; the save is async (ADR-011) and its completion is drained on the tick.

---

## Acceptance Criteria

- [x] `GatewayEvictHandler.Handle(Evict { requestId, accountId })` for a `Connected` session: the session goes to `Disconnected_SessionExpired`, the final character state is saved, the client's connection is closed with no reconnect window, the token is deleted (CR-TOK-6); `EvictResult { requestId }` is queued only after the save completed
- [x] AC-AUTH-27 (b, c): verified via the test observer — order is invalidate → save complete → `Disconnected_SessionExpired` → connection closed → `EvictResult`; no ghost / reconnect window is opened
- [x] AC-AUTH-27 (e) / EC-AUTH-6: with the save forced to fail, `EvictResult` is still sent, the session is still closed, and a critical persistence event is logged
- [x] Evict for an admitted-but-not-arrived account: entry and token deleted, `EvictResult` sent, no save
- [x] Evict while that account's `LoadCharacter` is in flight (`ZoneEntryAdmission.IsLoadPending`): the pending entry is marked dropped (existing `entry.Dropped` path), so the load's completion allocates no slot; admitted entry and token deleted; `EvictResult` sent at once; no save
- [x] Evict for an account whose zone slot is still entering (slot allocated, no `ZoneEntryAck`): the slot is reclaimed by the CR-ZI-17 leave path **without** a persistence write (`networking-session.md` B-NP-7 entering-slot exception), token deleted, `EvictResult` sent
- [x] Evict for an account in `Disconnected_SessionActive` (ghost) or `Reconnecting`: same end as a `Connected` session — save, token deleted, session released, `EvictResult` after the save completed or failed (`networking-session.md` B-NP-7 covers all three states; ST-NET-1 rows `Connected` / `Reconnecting` → `Disconnected_SessionExpired`)
- [x] Evict while a session end is already in flight for the account (clean logout, TTL expiry, or an earlier `Evict` — EC-CP-4, including one waiting on the mutation gate): no second save starts; every waiting `requestId` gets its `EvictResult` when that end completes. If the in-flight end was a clean logout whose save failed (session still `Connected`), the eviction save starts then
- [x] Evict for an unknown account: `EvictResult` at once (idempotent)
- [x] `GatewayReconnectHandler.Handle(Reconnect { requestId, accountId, token })` runs `TryValidateAndRotate` with `newExpiresAtTick` = the session's current expiry tick (TTL unchanged). **Success** → `ReconnectResult { accepted:true, newToken }`; the session state is not changed (stays `Disconnected_SessionActive` until the handshake — CR-TOK-5). **Failure** with the session in `Disconnected_SessionActive` → `EnterReconnecting` + `RecordFailedReAuthAttempt` (existing count, reused): below `REAUTH_FAILURE_LIMIT` the session is back in `Disconnected_SessionActive`, TTL unchanged; at the limit the token is deleted and the session end goes through `SessionEndCoordinator` (async save, release whatever the result — never the synchronous persist delegate). `ReconnectResult { accepted:false }` is sent at once in both cases. Failure in any other state (no session, `Connected`, `Reconnecting`) → `accepted:false`, nothing counted
- [x] AC-AUTH-33: setting `isBanned` on the account has no effect on a `Connected` session in the zone — the zone has no ban input; the session stays `Connected` for ≥ 5 s of simulated ticks with no disconnect event (EC-AUTH-7)
- [x] A `SessionReleased` is **not** sent for an evicted account (the gateway removes its map entry on `EvictResult`)

---

## Implementation Notes

*Derived from ADR-016 Decisions 3–4:*

- **Decision (`/story-readiness` 2026-11-06): route `Evict` through the existing session-end path; do not reuse `HandleSessionSteal`.** `HandleSessionSteal` saves synchronously, re-registers the account in `Connecting` and never removes the entity or broadcasts `PlayerLeftZone` — it is the same-zone, new-connection steal. Instead:
  - New `SessionEndCoordinator.BeginEviction(uint accountId, Action onEnded)`: starts the save exactly like logout / TTL (`SaveSession` + `ITickCompletionQueue.Track` in the same statement; waits on the mutation gate like the other ends; joins an end already in flight — EC-CP-4). The release runs **whatever the save result** (like TTL expiry; EC-AUTH-6). `onEnded` queues `EvictResult`.
  - New `ConnectionStateMachine.CompleteEviction(accountId, closeTransportIfOpen, removeEntityFromZone, broadcastPlayerLeftZone, observer)`: accepts `Connected`, `Disconnected_SessionActive` or `Reconnecting`; fires `OnSessionInvalidatedBySteal` → `OnPersistenceWriteCompleted(SessionSteal)` → `OnSessionStateTransitioned(…, Disconnected_SessionExpired, "GatewayEvict")`, closes the transport if still open, removes the entity, broadcasts `PlayerLeftZone`, removes the session. Called from the coordinator's completion callback with the state re-check widened to those three states (a transport drop mid-save turns `Connected` into a ghost; the eviction still ends it).
  - The token is invalidated when the `Evict` is handled (CR-TOK-6), before the save starts, so a `Reconnect` or handshake during the save fails.
  - Known window (accepted, same as clean logout): the client stays connected during the save, so input after the save's snapshot is lost.
- The handler never blocks; `EvictResult` is queued from the completion callback on the tick.
- `Reconnect` at the limit: the at-limit transition must not use `RecordFailedReAuthAttempt`'s synchronous persist delegate for the real save — start the end through `SessionEndCoordinator` (e.g. a `BeginReauthExhaustion` or a shared private start with a reason), then complete the state machine from its callback.
- Context: TD-095 — nothing in a live zone puts a client into `Connected` yet; tests drive the state machine directly.
- **Decisions at `/code-review` 2026-11-06:**
  - `Reconnect` for a `Connected` account is rejected even with the correct token (`accepted:false`, nothing rotated or counted): no rule defines a second client's handshake against a live `Connected` session, so the token path only serves ghosts. The client falls back to the credential path, which evicts.
  - `Reconnect` while a session end is in flight for the account (`SessionEndCoordinator.IsEndInFlight` — logout, TTL, eviction or exhaustion save) is rejected the same way: moving that session to `Reconnecting` would fail the end's completion re-check and leak the session.
  - The limiting re-auth failure is reported to the test observer (`OnReAuthAttemptFailed(attempt, 0)`) from `CompleteReauthExhaustion`, after the save.
  - Entering-slot eviction leaves the session-layer record to the TD-077 stand-in (noted in TD-077).
- Outgoing replies use the `IGatewayLinkOutbox` seam from Story 013.
- Tests drive the handlers directly with fakes for persistence (succeed / fail / delay) — no socket.

---

## Out of Scope

- Story 013: `Admit` and the token validator
- Gateway epic: link thread and message decoding; gateway-side map updates on `EvictResult`
- `AdminBanKick` (OQ-AUTH-4) — not in MVP

---

## QA Test Cases

- **AC-1 (AC-AUTH-27 b,c)**: Evict a connected player
  - Given: account 1 `Connected`, character 77, token T; persistence fake completes after 3 ticks
  - When: `Evict(req 9, account 1)`, then 3 ticks
  - Then: observer order = invalidated, persist completed, `Connected → Disconnected_SessionExpired`, connection closed; token store has no entry for 1; outbox = [`EvictResult(9)`] only after the 3rd tick; no ghost entry
- **AC-2 (AC-AUTH-27 e)**: Save fails
  - Given: persistence fake fails
  - Then: `EvictResult(9)` sent; session closed; one critical persistence log
- **AC-3**: Not arrived / unknown
  - Given: admitted-only account 2; unknown account 3
  - Then: `EvictResult` each; no save call; account 2's entry and token gone
- **AC-3b**: Load in flight / entering slot
  - Given: account 4 admitted with its `LoadCharacter` pending; account 5 with an allocated slot and no `ZoneEntryAck`
  - When: `Evict` each; then account 4's load completes
  - Then: no slot allocated for account 4; account 5's slot reclaimed; no save call for either; tokens gone; `EvictResult` each
- **AC-3c**: Ghost / Reconnecting
  - Given: account 6 `Disconnected_SessionActive`; account 7 `Reconnecting`
  - Then: one save each; `Disconnected_SessionExpired` with trigger `"GatewayEvict"`; entity removed; `EvictResult` after the save
- **AC-3d**: End already in flight
  - Given: account 1 clean logout in flight (save pending); a second `Evict` arrives while the first eviction is pending
  - Then: one save per end only; `EvictResult` for each `requestId` when the end completes; logout save forced to fail → the eviction save starts then
- **AC-4**: Reconnect
  - Given: account 1 ghost with token T, expiry tick E; `REAUTH_FAILURE_LIMIT` 3
  - When: `Reconnect(T)`; separately `Reconnect(T')` ×3
  - Then: accepted with a new token ≠ T, entry expiry = E, state still `Disconnected_SessionActive`; rejected ×2 back in `Disconnected_SessionActive`, the 3rd ends the session through the coordinator (one save, token gone); `ReconnectResult(false)` each at once
  - And: `Reconnect` for a `Connected` account with a wrong token → `accepted:false`, failure count unchanged
- **AC-5 (AC-AUTH-33)**: Ban does not kick
  - Given: account 1 `Connected`
  - When: 100 ticks (5 s at 20 Hz) after the account is marked banned in the database fake
  - Then: still `Connected`; no disconnect event

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Authentication/Authentication_ZoneEvictReconnect_tests.cs`

**Status**: [x] Created — 21 tests, passing

---

## Dependencies

- Depends on: Story 013
- Unlocks: Story 017

---

## Completion Notes
**Completed**: 2026-11-06
**Criteria**: 12/12 passing (no deferred items)
**Deviations** (advisory, accepted):
- AC-AUTH-27 (b, c) order: `EvictResult` after the save is asserted; the invalidate → save → expired → close order across the observer's separate lists is not
- AC-AUTH-27 (e) critical persistence log not asserted (the real `SaveSession` writes it; the fake persistence does not)
- `Reconnect` for a `Connected` account, or one whose session end is in flight, is rejected (decisions recorded in Implementation Notes)
- `REAUTH_FAILURE_LIMIT` is a new `ZoneInstancingWiringOptions.ReauthFailureLimit` (default 3, GDD default) — no existing config carried it
- The limiting re-auth failure ends through `SessionEndCoordinator.BeginReauthExhaustion` + `ConnectionStateMachine.CompleteReauthExhaustion` (trigger `"ReauthLimitExceeded"`), not `RecordFailedReAuthAttempt`; its `OnReAuthAttemptFailed` fires after the save
- New `SessionEndReason.Evicted = 3`; entering-slot eviction leaves the session-layer record to the TD-077 stand-in (noted in TD-077); TD-095 still applies
**Test Evidence**: Integration — `tests/EditMode/Authentication/Authentication_ZoneEvictReconnect_tests.cs` (21 tests). Full EditMode 6619: 6490 passed, 0 failed, 129 skipped (2026-11-06 23:30:27Z)
**Code Review**: Complete — `/code-review` 2026-11-06 CHANGES REQUIRED → in-flight Reconnect fix + 4 suggestions applied
