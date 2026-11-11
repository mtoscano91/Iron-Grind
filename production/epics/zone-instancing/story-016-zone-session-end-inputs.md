# Story 016: Zone-Side Session End Inputs (TD-101)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-11-11 from quick spec `design/quick-specs/zone-session-end-inputs-2026-11-11.md`; `/story-readiness` 2026-11-11: READY after the four readiness decisions below
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-11-06
> **Estimate**: 8 hours (quick spec)

## Context

**GDD**: `design/gdd/networking-session.md` — CR-NET-6.1, CR-NET-6.5, ST-NET-1, F-NET-5, OQ-NET-1 (resolved 2026-11-11); `design/gdd/zone-instancing.md` — CR-ZI-14
**Quick Spec**: `design/quick-specs/zone-session-end-inputs-2026-11-11.md` (rules 1–9)

> **ST-NET-1, Connected → Disconnected_SessionActive**: "Heartbeat timeout — no packet from client for >`HEARTBEAT_TIMEOUT_SECONDS` (10 s; OQ-NET-1 resolved 2026-11-11); a closed transport adds no transition — the client stops producing activity" — server: "Freeze client output; begin 5-minute TTL; continue processing entity."
> **ST-NET-1, Connecting → Disconnected_SessionExpired**: "Auth fails, zone full, client drops during handshake, or `CONNECTING_TIMEOUT_SECONDS` elapses" — release; no TTL.
> **CR-NET-6.1**: on disconnect the session is preserved for `SESSION_TTL_SECONDS` = 300 and the entity keeps being processed; `sessionExpiryTick = disconnectTick + SESSION_TTL_SECONDS × TICK_RATE_HZ`.
> **CR-ZI-14**: "All four exit causes [voluntary logout, SESSION_TTL expiry, teardown, entry-stall reclamation] must call [`NotifyZoneExit`]."

Today none of this runs in the zone: nothing calls `RecordInboundActivity` or `EvaluateTimeouts`, the activity sink is `StandInConnectionActivitySink`, the zone registers no heartbeat handler, `EvaluateTimeouts` sets no expiry tick when it ghosts a session, and the ghost TTL loop reads the empty `LoggingZoneGhostSessionSource` (TD-101).

**Requirement**: `TR-net-006` (session state machine ST-NET-1/2; ghost entity policy on disconnect) — registered, active.
**Tech Debt**: closes **TD-101** (P2, before Authentication Story 017)

**ADR Governing Implementation**: ADR-014 (inbound dispatch: `HeartbeatMessage` is connection-level; "the activity sink runs inside the receive callback, so it only records … the session layer implements it: it maps the connection to its account and calls `ConnectionStateMachine.RecordInboundActivity`"); ADR-009 Amendment 1 (only the composition constructs server systems); ADR-011 (persistence tracked on the tick queue, never awaited — the TTL save); ADR-012 (`IronGrind.ServerLogic`)
**ADR Decision Summary**: the zone's session machine gets its inputs from a recording activity sink drained on the tick and from a per-tick `EvaluateTimeouts`; ghost TTL expiry runs the existing coordinator path.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no post-cutoff engine API involved (plain C# over the dispatcher, session machine and wiring; the NGO transport path is unchanged).

**Control Manifest Rules (this layer)**:
- Required: the composition root is the only place that constructs server systems — ADR-009 A1
- Required: receive-callback code only records; state changes happen on the tick — ADR-014
- Forbidden: registering a core system's own delegate with `ServerTickLoop.RegisterTickDriven` — the tick order is `ZoneTickPipeline` — ADR-014
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **AC-1 (activity)**: a new production `IConnectionActivitySink` replaces `StandInConnectionActivitySink` in the composition. `OnInboundActivity(clientId, tick)` only records the latest tick per client. Each tick the wiring drains the records and, for each client holding a player slot, calls `ConnectionStateMachine.RecordInboundActivity(accountId, tick)` with the slot's account; a client without a slot is ignored. Test: a Connected client that sends an accepted message or a `HeartbeatMessage` (0x0210) at least every 10 s stays Connected over 60 s of ticks.
- [ ] **AC-2 (heartbeat)**: `HeartbeatMessage` 0x0210 reaches the zone dispatcher as a connection-level message (ADR-014 table) and counts as activity; it has no other effect. Test: a client sending only heartbeats every 3 s stays Connected over 60 s, and no `ANOMALY_UNKNOWN_TYPE` (or any other dispatcher anomaly) is logged for 0x0210.
- [ ] **AC-3 (heartbeat timeout)**: `HEARTBEAT_TIMEOUT_SECONDS` = 10 (`public const` default passed through the wiring options). A Connected client silent for 10 s (200 ticks at 20 Hz) moves to `Disconnected_SessionActive` at tick 200 (not 199) with `SessionExpiryTick` = that tick + 6000 (`SESSION_TTL_SECONDS` 300 × 20); its slot and entity stay; no save, `PlayerLeftZone` or `NotifyZoneExit` happens then.
- [ ] **AC-4 (transport close)**: a transport close of a Connected client changes nothing at once (no new transition); with no further activity the client is ghosted 10 s later (AC-3).
- [ ] **AC-5 (ghost TTL)**: a production `IZoneGhostSessionSource` replaces `LoggingZoneGhostSessionSource` and lists every `Disconnected_SessionActive` account with its `SessionExpiryTick`. At the expiry tick the existing loop's `SessionEndCoordinator.EvaluateSessionTTL` runs: exactly one `SaveSession(SessionTTLExpiry)`, then one `PlayerLeftZone`, one `NotifyZoneExit` (CR-ZI-14), the slot freed and the session removed. The source snapshots the ghosts once per tick (`Refresh`, see Implementation Notes) so removals inside the loop cannot shift indices. Test: two ghosts expiring in the same tick are both ended (two saves, two `PlayerLeftZone`, two `NotifyZoneExit`); a ghost whose expiry is later is untouched.
- [ ] **AC-6 (connecting timeout)**: each tick the wiring calls `EvaluateTimeouts(tick, HEARTBEAT_TIMEOUT_SECONDS × TICK_RATE_HZ, CONNECTING_TIMEOUT_SECONDS × TICK_RATE_HZ)` with `CONNECTING_TIMEOUT_SECONDS` = 30 (`AdmittedEntries`). A Connecting session with no admission for 30 s is removed (`ConnectingTimeout`). A character load that takes up to the load watchdog (`PERSISTENCE_WATCHDOG_TICKS` = 200, 10 s) is admitted with its session Connected (`SessionSinkAdapter` does not skip).
- [ ] **AC-7 (startup check)**: composition startup fails (S-fail, logged) if `CONNECTING_TIMEOUT_SECONDS × TICK_RATE_HZ` ≤ `PERSISTENCE_WATCHDOG_TICKS`.
- [ ] **AC-8 (tick order)**: in the wiring step: … → handshake drain → activity drain (AC-1) → `EvaluateTimeouts` (AC-6) → ghost `Refresh` → ghost TTL loop (AC-5) → `_admitted.OnTick` → `_lifecycle.OnTick` → … A test shows activity recorded in a tick's receive callback prevents a timeout evaluated in that same tick.
- [ ] **AC-9 (allocation)**: the activity drain, `EvaluateTimeouts` call and ghost enumeration allocate nothing per tick in steady state (`Is.Not.AllocatingGCMemory()`).
- [ ] **AC-10 (no regression / stand-ins)**: entry, admission, reclamation (CR-ZI-17), gateway Evict / Reconnect and the EditMode suite pass; the composition no longer constructs `StandInConnectionActivitySink` or `LoggingZoneGhostSessionSource`. TD-101 marked RESOLVED; a new tech-debt row logs the deferred clean-logout input (no logout wire message; until it exists a quitting client is a 5-minute ghost).

---

## Implementation Notes

- **Session machine change (small, in scope):** `ConnectionStateMachine.EvaluateTimeouts` sets `SessionExpiryTick = currentTick + SESSION_TTL_SECONDS × TICK_RATE_HZ` when it moves Connected → Disconnected_SessionActive (today: state only, ~line 494). Keep the dev-build `INetworkTestObserver` transition call.
- **Ghost enumeration (decided at readiness 2026-11-11 — snapshot with `Refresh`):** the wiring loop reads `Count` once and calls `Get(i)`, and `EvaluateSessionTTL` removes an expired session — a live view over the machine would shift indices mid-loop (skip or out of range). So:
  - New `ConnectionStateMachine.CollectSessionActiveGhosts(List<uint> accountIds, List<uint> expiryTicks)`: clears both caller-owned lists and appends every `Disconnected_SessionActive` record's account and `SessionExpiryTick` (dictionary `foreach` — struct enumerator, no allocation; lists pre-sized by the caller, e.g. to the zone's player capacity).
  - `IZoneGhostSessionSource` gains `void Refresh();`. The production source (`ZoneServer/`, over the wiring's `_sessions`) calls `CollectSessionActiveGhosts` in `Refresh`; `Count` / `Get` read the snapshot and have no side effects. Update `LoggingZoneGhostSessionSource` / test fakes for the new member (the stand-in may remain for tests but the composition no longer constructs it — AC-10).
  - The wiring calls `_ghosts.Refresh()` immediately after `EvaluateTimeouts` and before the loop (AC-8), so a session ghosted this tick is listed this tick (its expiry is 6000 ticks out, so it is not ended).
  - Rejected: snapshotting inside the `Count` getter (hidden side effect).
- **Where the TTL constant comes from (decided):** reuse `CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS` = 300 directly — it is in the same assembly (`IronGrind.ServerLogic`) as `ConnectionStateMachine`; no move.
- **Account lookup:** the slot pool's player slot carries the account (`ZonePlayerSlot.Account`, `.ClientId`); the pool has no client → slot lookup, so the drain either scans slots with `ZoneSlotPool.TryGetPlayerAtSlot(i, out _, out clientId)` (≤ 50 slots) or adds a small allocation-free lookup — implementer's choice, AC-9 applies. Clients in the entering phase have a slot too — they also count as activity (harmless: CR-ZI-17 reclamation handles entering slots and `RecordInboundActivity` only updates Connected sessions).
- **Heartbeat registration (decided):** an unregistered 0x0210 already reaches `OnInboundActivity` (dispatcher step 1, `InboundRequestDispatcher.cs` ~597, runs before decoding) but is then dropped with `ANOMALY_UNKNOWN_TYPE`, because the zone calls only `NetworkingCoreInboundRegistration.RegisterCombatInput`. Register 0x0210 connection-level on the zone with a no-op `IConnectionMessageSink`, using the descriptor `NetworkingCoreInboundRegistration.Register` uses (`HeartbeatMessage.MessageTypeId`, `maxBodyBytes: 0`, `carriesSenderEntityId: false`) — e.g. a new `RegisterHeartbeat(dispatcher, sink)` helper there, called by the composition next to `RegisterCombatInput`.
- **Activity sink construction order (decided):** the dispatcher is built at `ZoneServerComposition.cs` ~1693, the wiring at ~1797. The recording sink has no dependencies, so the composition constructs it first, passes it to the dispatcher (replacing `new StandInConnectionActivitySink()`), and hands the same instance to the wiring through a new `ZoneInstancingWiringOptions` field; the wiring drains it in `Run`. No forwarding or two-phase build.
- **Existing seams:** `_sessions` / `_sessionEnds` are built in `ZoneInstancingWiring` (~349, ~398); the ghost loop is in `Run` (~564–569).
- `GatewayEvictHandler` / `GatewayReconnectHandler` already share `_sessions` / `_sessionEnds`; a ghost that the gateway evicts or reconnects leaves the ghost list through the machine's own transitions.
- **Performance**: per tick O(clients with activity) for the drain + O(sessions) inside `EvaluateTimeouts` (existing scratch-list design) + O(ghosts); no allocation (AC-9). At 50 players per zone this is negligible against the 50 ms tick.

---

## Out of Scope

- Clean logout input (no wire message — deferred by user decision 2026-11-11; new tech-debt row).
- "Freeze client output" for a ghost whose transport is still open.
- `GHOST_COMBAT_TTL_MINUTES` disengage.
- TTL expiry while `Reconnecting`.
- TD-077 (reclaim session sink for entering slots, CR-ZI-17 (c)).

---

## QA Test Cases

- **AC-1..AC-6, AC-8, AC-9** — on `ZoneInstancingWiring` with fakes (persistence, outbox, disconnector, tick source), ticks advanced programmatically; activity injected through the production sink as the dispatcher would; no database.
- **AC-7** — composition startup with a fake backend and an options override that makes the connecting timeout ≤ the watchdog.
- **AC-10** — composition test: no stand-in log for the activity sink or ghost enumeration; full EditMode suite.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_SessionEndInputs_tests.cs` (new) — AC-1..AC-9
- `tests/EditMode/Networking/ZoneServer_CompositionWiring_tests.cs` or a new composition test — AC-7, AC-10

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 009–014 (Complete), 015 (Complete); Gateway Story 012 (Complete)
- Unlocks: Authentication Story 017 (end-to-end)
