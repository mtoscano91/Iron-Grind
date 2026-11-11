# Evidence: Zone ↔ gateway link in the Editor (Gateway Story 011)

> **Status: PASS (2026-11-11).**
> Gate: Integration (BLOCKING), `production/epics/gateway/story-011-zone-link-wiring.md` AC-8.
> Governs: ADR-016 Verification item 2 (Editor Play-mode exit stops the link thread); ADR-009 Amendment 1 (teardown 2a, `OnDestroy`).

## 1. Setup

| Item | Value |
|------|-------|
| Run date | 2026-11-11 |
| Operator | Manuel Toscano (Play mode toggles in the Editor); Claude Code session (gateway start/stop, log reading) |
| OS | Windows 11 Home 10.0.26200 |
| Unity | 6000.3.10f1 Editor, started from PowerShell with `IRONGRIND_PG_PASSWORD` set for that process only (read from `%LOCALAPPDATA%\IronGrind\secrets\irongrind.pw`, never printed); scene `Assets/Scenes/Server/ZoneServer.unity` |
| Zone code | `6d59e62` + Story 011 working tree (implementation + code-review fixes, uncommitted) |
| Gateway run | `ZONE_LAUNCHER=none`, `IRONGRIND_GATEWAY_DB` = role `gateway`, `Database=irongrind` (password from `secrets\gateway.pw`, never printed); `dotnet run --project gateway\IronGrind.Gateway -c Release --no-build`, started hidden with `Start-Process`; stdout to `%LOCALAPPDATA%\IronGrind\gateway-zonelink.log` |
| PostgreSQL | 16.15 portable, 127.0.0.1:5432 (already running) |
| Zone config | `Builds/ZoneServer/zone-config.json` (no gateway keys → defaults: `GatewayZoneLinkPort` 7900, `GatewayLinkRetrySeconds` 2) |
| Editor args | `-zoneTemplate StartingZone -zoneId 1 -port 7777 -serverConfig C:\Users\Manuel Toscano\Claude-Code-Game-Studios\Builds\ZoneServer\zone-config.json` |
| Editor setting | default Enter Play Mode settings (domain reload on) |

A first attempt from an Editor opened through Unity Hub stopped at S4 (`IRONGRIND_PG_PASSWORD is not set or empty`, startup failed at S4) — setup error, not counted; the Editor was restarted from PowerShell with the variable.

## 2. Procedure and results

Gateway log texts (from `gateway/IronGrind.Gateway/ZoneLink/ZoneLinkListener.cs`): registration = `Zone <id> link registered from <remote>`; link lost = `Zone link closed: zone <id> from <remote>: <reason>`. The gateway's console logger and the Editor log carry no timestamps; the time bounds below are shown by causality (the zone's step 2a closes the socket, and the gateway's `peer closed` is its read of that close).

| # | Step | Pass criterion | PASS / FAIL | Notes |
|---|------|----------------|-------------|-------|
| 1 | Start the gateway | `Zone link listening on 127.0.0.1:7900` logged | **PASS** | also `Now listening on: http://127.0.0.1:8080` |
| 2 | Enter Play mode | `Zone 1 link registered …` logged within 5 s of S10 | **PASS** | Editor: `ZoneID(1) Active at tick 0`, then `[GatewayLinkClient] LinkUp: registered with the gateway.`; gateway: `Zone 1 link registered from 127.0.0.1:58333` |
| 3 | Leave Play mode | `Zone link closed: zone 1 …` within 5 s; teardown step 2a done; no `StopTimedOut` | **PASS** | Editor: `host OnDestroy: NetworkManager alive=False`, `LinkClosed: stopping.`, `teardown step 2a done in 148 ms`, steps 1–7, no step 8; gateway: `Zone link closed: zone 1 from 127.0.0.1:58333: peer closed`. 0 `StopTimedOut` and 0 `NullReferenceException` in Editor.log |
| 4 | Wait ≥ 10 s after step 3 | no new connection / `link registered` until the next Play | **PASS** | the next zone-1 line in the gateway log is the step-5 registration from a new port (58339); no reconnect attempt in between |
| 5 | Enter Play mode again (same `-zoneId`) | `Zone 1 link registered …` | **PASS** | Editor: `Active at tick 0`, `LinkUp`; gateway: `Zone 1 link registered from 127.0.0.1:58339` |
| 6 | Leave Play mode | as step 3 | **PASS** | Editor: `teardown step 2a done in 98 ms`, steps 1–7; gateway: `Zone link closed: zone 1 from 127.0.0.1:58339: peer closed` |

## 3. Log excerpts

Gateway (`gateway-zonelink.log`, zone-link lines; the interleaved warm-spare lines are in section 5):

```
info: IronGrind.Gateway.ZoneLink.ZoneLinkListener[0]
      Zone link listening on 127.0.0.1:7900
info: IronGrind.Gateway.ZoneLink.ZoneLinkListener[0]
      Zone 1 link registered from 127.0.0.1:58333
info: IronGrind.Gateway.ZoneLink.ZoneLinkListener[0]
      Zone link closed: zone 1 from 127.0.0.1:58333: peer closed
info: IronGrind.Gateway.ZoneLink.ZoneLinkListener[0]
      Zone 1 link registered from 127.0.0.1:58339
info: IronGrind.Gateway.ZoneLink.ZoneLinkListener[0]
      Zone link closed: zone 1 from 127.0.0.1:58339: peer closed
```

Editor (`%LOCALAPPDATA%\Unity\Editor\Editor.log`, zone lines, cycle 1; cycle 2 identical with step 2a 98 ms):

```
[ZoneInstancingWiring] ZoneID(1) Active at tick 0
[GatewayLinkClient] LinkUp: registered with the gateway.
[ZoneServer] host OnDestroy: NetworkManager alive=False
[ZoneServer] teardown step 1 done in 1 ms
[GatewayLinkClient] LinkClosed: stopping.
[ZoneServer] teardown step 2a done in 148 ms
[ZoneServer] teardown step 3 done in 0 ms
[ZoneServer] teardown step 4 done in 2 ms
[ZoneServer] teardown step 4b done in 0 ms
[ZoneServer] teardown step 5 done in 0 ms
[ZoneServer] teardown step 6 done in 0 ms
[ZoneServer] teardown step 7 done in 1 ms
```

## 4. Verdict

**PASS** — steps 1–6 all PASS. Leaving Play mode stops the link thread inside teardown step 2a (148 ms / 98 ms, under the 2000 ms join timeout) through `OnDestroy`; the gateway sees the link close; re-entering Play mode re-registers the same ZoneID.

Signed off by: Manuel Toscano (Play-mode run), 2026-11-11

## 5. Observation outside this check (gateway, advisory)

With `ZONE_LAUNCHER=none` the gateway's warm spare allocated a new ZoneID every `ZONE_LOAD_TIMEOUT_SECONDS` (30 s) — ZoneIDs 4 to 19 during the run — each logged as `warn … NoOpZoneLauncher: zone N not started` then `crit … WarmSpareKeeper: ZoneDefinitionLoadFailed: zone ZoneID(N) … did not register within 30 s`. The retry rate matches ADR-016 ("failed starts retried at most once per `ZONE_LOAD_TIMEOUT_SECONDS`"), but in `none` mode it consumes a `zone_id_seq` value and a `crit` line per retry. While zone 1 was registered no new start was issued beyond the one already in flight. Candidate gateway follow-up (not Story 011 scope).
