# Quick Design Spec: Zone-side session end inputs (TD-101)

**Type**: Addition
**System**: Networking Session (zone process) / Zone Instancing wiring
**GDD Reference**: `design/gdd/networking-session.md` — CR-NET-6.1, CR-NET-6.5, ST-NET-1 (Connected → Disconnected_SessionActive; Connecting → Disconnected_SessionExpired), F-NET-5, OQ-NET-1; `design/gdd/zone-instancing.md` — CR-ZI-14
**Date**: 2026-11-11
**Estimated Implementation**: ~8 h

## Change Summary

Wires the zone's session machine to the zone server: client activity feeds it, `EvaluateTimeouts` runs every tick with a 10 s heartbeat timeout and the 30 s connecting timeout, entering the ghost state starts the 300 s session TTL, and a production ghost source lets the existing TTL loop save and release expired ghosts. Clean logout is deferred (no wire message exists).

## Motivation

Today a Connected player whose client drops stays Connected in the zone until the gateway's next `Evict`: nothing times out, and the ghost TTL loop iterates an empty stand-in, so the CR-NET-6.5 sequence (TTL save, `PlayerLeftZone`, slot release, `NotifyZoneExit` — CR-ZI-14) never runs. Blocks Authentication Story 017 (end-to-end).

Facts found 2026-11-11 (code research for this spec):
- `ConnectionStateMachine.RecordInboundActivity` and `EvaluateTimeouts` have no caller in `src/`; the zone dispatcher's activity sink is `StandInConnectionActivitySink`, and the zone registers no heartbeat handler (`RegisterCombatInput` path).
- `EvaluateTimeouts` moves Connected → Disconnected_SessionActive in place but sets no `SessionExpiryTick` and raises no production output; only `EnterReconnecting` sets the expiry.
- The wiring's ghost loop reads `IZoneGhostSessionSource`, defaulted to the empty `LoggingZoneGhostSessionSource`.
- No logout message exists in `MessageRoutingRegistry`.
- `CONNECTING_TIMEOUT_SECONDS` = 30 (`AdmittedEntries`); character loads are bounded by the tick-queue watchdog `PERSISTENCE_WATCHDOG_TICKS` = 200 (10 s at 20 Hz). `CHARACTER_LOAD_TIMEOUT_SECONDS` = 10 is unreferenced.

## Design Delta

`networking-session.md` ST-NET-1 says:

> Connected → Disconnected_SessionActive: "Heartbeat timeout — no packet from client for >N seconds (OQ-NET-1)" — freeze output, begin the 5-minute TTL, keep processing the entity.

This spec sets N = `HEARTBEAT_TIMEOUT_SECONDS` = **10** (user decision 2026-11-11; resolves OQ-NET-1). A closed transport adds **no new transition**: the client simply stops producing activity and the heartbeat timeout ghosts it (user decision 2026-11-11). No other GDD rule changes.

## New Rules / Values

**Activity input**
1. The zone composition replaces `StandInConnectionActivitySink` with a production sink. `OnInboundActivity(clientId, tick)` runs inside the receive callback, so it only **records** the latest tick per client.
2. On the tick, the wiring drains the records: for each client that holds a player slot, `ConnectionStateMachine.RecordInboundActivity(accountId, tick)` with the slot's account. Clients without a slot are ignored. No allocation per tick.
3. `HeartbeatMessage` 0x0210 (C→S, no body) is registered on the zone dispatcher as a connection-level message whose only effect is the rule 1 activity, so an idle client keeps its session with heartbeats alone. Any other accepted message also counts ("no packet from client").

**Timeouts**
4. `HEARTBEAT_TIMEOUT_SECONDS` = 10, a `public const` default passed through the wiring options (Story 040 / Zone Instancing Story 014 knob pattern). F-NET-5 holds: 10 < `SESSION_TTL_SECONDS` (300) and 10 < `GHOST_COMBAT_TTL_MINUTES` × 60 (60).
5. Each tick the wiring calls `EvaluateTimeouts(tick, HEARTBEAT_TIMEOUT_SECONDS × TICK_RATE_HZ, CONNECTING_TIMEOUT_SECONDS × TICK_RATE_HZ)` (`CONNECTING_TIMEOUT_SECONDS` = 30 from `AdmittedEntries`).
6. **Connecting-timeout constraint**: the connecting timeout must exceed the character load watchdog (`PERSISTENCE_WATCHDOG_TICKS` = 200 = 10 s at 20 Hz); a composition-level check fails startup otherwise. This guarantees a load in flight never outlives its Connecting session (which would admit a player with no session).

**Ghost entry and TTL**
7. When `EvaluateTimeouts` moves Connected → Disconnected_SessionActive it also sets `SessionExpiryTick = currentTick + SESSION_TTL_SECONDS × TICK_RATE_HZ` (`networking-session.md` sessionExpiryTick rule). The entity stays in the zone and keeps being processed (CR-NET-6.1).
8. A production `IZoneGhostSessionSource` replaces `LoggingZoneGhostSessionSource`: it lists every Disconnected_SessionActive account in the session machine with its expiry tick, without allocation per tick. The existing tick step then calls `SessionEndCoordinator.EvaluateSessionTTL`, whose TTL-expiry path already saves, releases, sends `PlayerLeftZone` and calls `NotifyZoneExit`.
9. **Tick order** in the wiring step: … → handshake drain → **activity drain (2)** → **`EvaluateTimeouts` (5)** → ghost TTL loop (8) → `_admitted.OnTick` → `_lifecycle.OnTick` → …

**Out of scope**
- Clean logout — deferred (user decision 2026-11-11), new tech-debt item; until then a quitting client is a ghost for 5 minutes.
- "Freeze output" for a ghost whose transport is still open.
- `GHOST_COMBAT_TTL`.
- TTL expiry while Reconnecting.
- TD-077 (the reclaim session sink for entering slots, CR-ZI-17 (c)).

No new tuning knobs beyond `HEARTBEAT_TIMEOUT_SECONDS` (GDD range [3, 30]).

## Affected Systems

| System | Impact | Action Required |
|--------|--------|-----------------|
| Zone wiring / composition | New activity sink, heartbeat registration, tick-step calls, ghost source | Code |
| `ConnectionStateMachine` | Sets `SessionExpiryTick` on heartbeat-timeout ghost entry; ghost enumeration | Code (small) |
| `networking-session.md` | OQ-NET-1 resolved; heartbeat-timeout knob default 10 s | GDD edit (asked separately) |
| `docs/tech-debt-register.md` | TD-101 resolved; new item: no logout message | Register |

## Acceptance Criteria

- [ ] A Connected client that sends any accepted message or a heartbeat at least every 10 s stays Connected over 60 s of ticks.
- [ ] A Connected client silent for 10 s (200 ticks) moves to Disconnected_SessionActive at tick 200 (not 199) with `SessionExpiryTick` = that tick + 6000; its slot and entity stay; no save, `PlayerLeftZone` or `NotifyZoneExit`.
- [ ] A transport close of a Connected client changes nothing at once and ghosts it after 10 s.
- [ ] At the expiry tick the ghost goes through `EvaluateSessionTTL`: exactly one `SaveSession(SessionTTLExpiry)`, then one `PlayerLeftZone`, one `NotifyZoneExit`, slot freed, session removed.
- [ ] A Connecting session with no admission for 30 s is removed (`ConnectingTimeout`); a character load taking up to the 10 s watchdog is admitted with its session Connected (`SessionSinkAdapter` never skips silently).
- [ ] Startup fails if the connecting timeout is not greater than the load watchdog.
- [ ] Activity from a client without a player slot is ignored; the activity drain and ghost enumeration allocate nothing per tick.
- [ ] No regression: entry, admission, reclamation (CR-ZI-17), gateway Evict / Reconnect and the existing EditMode suite pass; the composition no longer constructs `StandInConnectionActivitySink` or `LoggingZoneGhostSessionSource`.

## GDD Update Required?

Yes — `design/gdd/networking-session.md`: OQ-NET-1 marked resolved (`HEARTBEAT_TIMEOUT_SECONDS` = 10, 2026-11-11, this spec); the heartbeat-timeout tuning-knob row gets default 10 s; the ST-NET-1 row's "N seconds" names `HEARTBEAT_TIMEOUT_SECONDS` (10 s). Asked separately.
