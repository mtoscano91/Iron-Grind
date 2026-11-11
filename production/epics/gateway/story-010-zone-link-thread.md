# Story 010: Zone link thread

> **Epic**: Gateway
> **Status**: Complete
> **Layer**: Core
> **Type**: Integration
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/zone-instancing.md` — "Where routing runs (ADR-016)" (zone link at S10, re-registration on reconnect)
**Requirement**: `TR-zi-018`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-016: Gateway and Authentication Process (Decision 3), with ADR-011 (tick never blocks) and ADR-015 (`NpgsqlRelayListener` thread shape)
**ADR Decision Summary**: Zone side: one dedicated background `Thread` (`IsBackground = true`, named) does connect/read/write. Received messages go into a bounded concurrent inbox (`GATEWAY_LINK_INBOX_CAPACITY`, full closes the link) that the tick drains at its start; replies are queued from the tick and written by the link thread. The tick never touches the socket. `Stop()` sets a stop signal, closes the socket from the calling thread, then `Join`s with a timeout; the retry loop waits on the stop signal, never `Thread.Sleep`. Frames above `GATEWAY_LINK_MAX_FRAME_BYTES` close the link; it is re-established as on link loss (retry every `GATEWAY_LINK_RETRY_SECONDS`).

**Engine**: Unity 6.3 LTS (server build) | **Risk**: MEDIUM — link thread in the IL2CPP Linux build (ADR-016 Verification 1)
**Engine Notes**: `System.Net.Sockets.Socket` / `TcpClient` and `System.Threading.Thread` under IL2CPP: no `async` socket APIs on this thread (blocking calls only, as `NpgsqlRelayListener` does). Check `link.xml` needs for `System.Net.Sockets` under IL2CPP stripping when the Linux build is first made.

**Control Manifest Rules (this layer)**:
- Required: the "Zone side" and "Link `Stop()`" bullets of the manifest's ADR-016 block
- Forbidden: socket access from the tick thread; `Thread.Sleep` in the retry loop
- Guardrail: inbox drain at tick start is O(messages) with no allocation per message beyond the decoded struct

---

## Acceptance Criteria

- [ ] `GatewayLinkClient` (in `src/ServerLogic/Gateway/`): `Start(endpoint, Func<RegisterZone> registration)` starts one named background thread; it connects to `127.0.0.1:GATEWAY_ZONE_LINK_PORT`, sends the `RegisterZone` produced by the callback (fresh on every connect), then reads frames into the inbox
- [ ] `TryDequeue(out GatewayLinkMessage)` for the tick; `Enqueue(outbound message)` from the tick, written by the link thread in order
- [ ] Connect failure or link loss → wait `GATEWAY_LINK_RETRY_SECONDS` on the stop signal, reconnect, re-send `RegisterZone` (with the callback's current live accounts); outbound messages queued while down are dropped with a log (the gateway's timeouts cover them) — or kept, if the readiness check decides otherwise
- [ ] Inbox full (`GATEWAY_LINK_INBOX_CAPACITY`) or an oversized / malformed frame → close the link (then retry as on loss), logged once per event
- [ ] `Stop()` from any thread: returns within the join timeout even while the thread is blocked in connect or read; idempotent; no exception escapes the thread (caught and logged)
- [ ] `IsConnected` and a `LinkLost` / `LinkUp` counter for diagnostics
- [ ] Tests (EditMode, real loopback `TcpListener` as a fake gateway): register on connect; message in → inbox; outbound → frame at the fake gateway; fake gateway closes → reconnect + re-register with the callback's new account list; inbox overflow → close + reconnect; `Stop()` during a blocking read returns within the timeout; `Stop()` before `Start()` is safe
- [ ] Knobs (`GATEWAY_ZONE_LINK_PORT`, `GATEWAY_LINK_RETRY_SECONDS`, `GATEWAY_LINK_MAX_FRAME_BYTES`, `GATEWAY_LINK_INBOX_CAPACITY`) come from the zone server config, not constants

---

## Implementation Notes

*Derived from ADR-016 Decision 3 and ADR-015:*

- Copy the shape of `NpgsqlRelayListener` (find it under `src/ServerLogic/`): `ManualResetEventSlim`/`WaitHandle` stop signal, `volatile` socket field closed by `Stop()`, `Join(timeout)`.
- Inbox: `ConcurrentQueue<T>` + an `int` count via `Interlocked` for the capacity check, or a bounded structure — no `BlockingCollection` waits on the tick.
- Codec: Story 001.
- Zone server config: add the four knobs to the existing server config source (`IZoneConfigSource`) with defaults matching ADR-016's table.
- Real-time waits in tests: keep them short (configure retry to e.g. 50 ms in tests) and bounded.

---

## Out of Scope

- Story 011: who calls `Start` / `Stop`, draining the inbox into handlers, `PlayerCount` / `ZoneDraining` / `SessionReleased` sends
- The Linux IL2CPP run (ADR-016 Verification 1) — recorded with the Linux headless build gate

---

## QA Test Cases

- **AC-1**: Given fake gateway listening → `Start` → fake receives `RegisterZone` as first frame
- **AC-3**: Given connected → fake closes socket → within retry + margin a new connection sends `RegisterZone` with the updated accounts; Edge: gateway not listening at start → retries until it is
- **AC-4**: Given capacity 4 and a fake sending 5 frames with no tick draining → link closed, one log line, reconnect follows; Edge: frame declaring cap + 1 → closed
- **AC-5**: Given the thread blocked in `Read` → `Stop()` returns in < join timeout; thread no longer alive; second `Stop()` no-op
- **AC-2**: Given connected → tick enqueues `AdmitResult` then `EvictResult` → fake receives both in order

---

## Test Evidence

**Story Type**: Integration
**Required evidence**:
- `tests/EditMode/Gateway/GatewayLinkClient_tests.cs` — must pass

**Status**: [x] Created — 30 cases in `GatewayLinkClient_tests.cs` + 12 in `ZoneServer_Config_tests.cs`, all passing (Unity EditMode, 2026-11-11)

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 011

---

## Completion Notes
**Completed**: 2026-11-11
**Criteria**: 8/8 passing
**Deviations**: (advisory) TR-zi-018 not in `tr-registry.yaml` (zone-instancing TR-IDs live in its EPIC.md only); outbound frames queued while the link is down are DROPPED, counted in `DroppedOutboundCount` and summarised at the next LinkUp (readiness choice resolved: drop); `Enqueue` takes an encoded `byte[]` — Story 011 adapts `IGatewayLinkOutbox` onto it; `ZoneServerConfig` rejects `GatewayLinkMaxFrameBytes` below `1 + ADMIT_BODY_BYTES` (29) — added at code review. Untested: log-once-per-event (no `LogAssert`), `Stop()` during a blocked `Connect` (loopback connect cannot be made to block), the uncounted-drop race at re-registration.
**For Story 011**: the registration callback runs ON THE LINK THREAD — pass a thread-safe snapshot touching no Unity objects; call `Stop()` from teardown 2a and `OnDestroy` (Editor: also on Play-mode exit / domain reload); a refused loopback connect takes 1–2 s on Windows, inside the 2 s join timeout.
**Test Evidence**: Integration — `tests/EditMode/Gateway/GatewayLinkClient_tests.cs` (30 cases) + `tests/EditMode/Networking/ZoneServer_Config_tests.cs` (12 gateway cases); full EditMode 6779 / 0 failed / 129 skipped
**Code Review**: Complete — /code-review 2026-11-11 (unity-specialist + qa-tester), APPROVED WITH SUGGESTIONS, all 7 applied
