# Story 014: Zone `Evict` and `Reconnect` handlers

> **Epic**: Authentication
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06

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
- Guardrail: the steal save's worst case (pool open 5 s + command 5 s, capped at 10 s by `PERSISTENCE_WATCHDOG_TICKS`) stays under `GATEWAY_EVICT_TIMEOUT_MS` (11 s) — ADR-016 Tuning Knobs

---

## Acceptance Criteria

- [ ] `GatewayEvictHandler.Handle(Evict { requestId, accountId })` for a `Connected` session: the session goes to `Disconnected_SessionExpired`, the final character state is saved, the client's connection is closed with no reconnect window, the token is deleted (CR-TOK-6); `EvictResult { requestId }` is queued only after the save completed
- [ ] AC-AUTH-27 (b, c): verified via the test observer — order is invalidate → save complete → `Disconnected_SessionExpired` → connection closed → `EvictResult`; no ghost / reconnect window is opened
- [ ] AC-AUTH-27 (e) / EC-AUTH-6: with the save forced to fail, `EvictResult` is still sent, the session is still closed, and a critical persistence event is logged
- [ ] Evict for an admitted-but-not-arrived account: entry and token deleted, `EvictResult` sent, no save
- [ ] Evict for an account in the ghost (disconnected, reconnect window) state: same session end as networking-session.md defines for a steal of a ghost — save, token deleted, `EvictResult` (confirm the rule at `/story-readiness`; if the GDD is silent, flag it rather than invent)
- [ ] Evict for an unknown account: `EvictResult` at once (idempotent)
- [ ] `GatewayReconnectHandler.Handle(Reconnect { requestId, accountId, token })` → `TryValidateAndRotate`; success → `ReconnectResult { accepted:true, newToken }`; failure → `accepted:false` and the `REAUTH_FAILURE_LIMIT` count advances (existing networking-session logic — reuse, do not reimplement)
- [ ] AC-AUTH-33: setting `isBanned` on the account has no effect on a `Connected` session in the zone — the zone has no ban input; the session stays `Connected` for ≥ 5 s of simulated ticks with no disconnect event (EC-AUTH-7)
- [ ] A `SessionReleased` is **not** sent for an evicted account (the gateway removes its map entry on `EvictResult`)

---

## Implementation Notes

*Derived from ADR-016 Decisions 3–4:*

- Existing code: `ConnectionStateMachine.HandleSessionSteal` runs the steal for a **new connection in the same zone** (it calls `persistFinalCharacterState` synchronously and then re-registers the account in `Connecting`). A gateway `Evict` has no new connection here — the new session lands in another zone (or this one later, through `Admit`). Read `HandleSessionSteal`, `SessionEndCoordinator` and `GhostCleanupSequencer` at `/story-readiness` and decide: extend with a "no successor" variant, or route through the existing session-end path with an `Evicted` reason. Record the decision in the story before implementing.
- The save goes through the existing save path (Character Persistence Story 020 / `SessionEndCoordinator`); the handler waits for its completion on the tick (completion queue), never blocks.
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
- **AC-4**: Reconnect
  - Given: account 1 ghost with token T
  - When: `Reconnect(T)`; separately `Reconnect(T')` ×3
  - Then: accepted with a new token ≠ T; rejected ×3 then the session expires per `REAUTH_FAILURE_LIMIT`
- **AC-5 (AC-AUTH-33)**: Ban does not kick
  - Given: account 1 `Connected`
  - When: 100 ticks (5 s at 20 Hz) after the account is marked banned in the database fake
  - Then: still `Connected`; no disconnect event

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Authentication/Authentication_ZoneEvictReconnect_tests.cs`

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 013
- Unlocks: Story 017
