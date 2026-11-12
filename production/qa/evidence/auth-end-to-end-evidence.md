# Authentication end-to-end evidence (Editor + local gateway)

> Story: `production/epics/authentication/story-017-end-to-end-login-to-zone-entry.md` (also closes Story 018 AC-9/10)
> Governs: AC-AUTH-01, 05a, 05b, 21, 27, 29 (d); ADR-016 Verification 2; round trip (AC-6)
> Run date: 2026-11-12 (UTC 00:17–01:13), Windows 11, 8 logical cores
> Story 019 (VPS / iOS / Linux zone build) appends its own section to this file.

## Setup

| Item | Value |
|------|-------|
| Operator | Manuel Toscano (Editor: Play mode, builds); Claude Code session (client launches, gateway stop, log reading) |
| PostgreSQL | portable 16.15, 127.0.0.1:5432 |
| Caddy | dev, `tools/caddy/Caddyfile.dev`, `https://localhost` (`tls internal`, root trusted with `caddy trust`) |
| Gateway | `dotnet run --project gateway\IronGrind.Gateway -c Release`, `ZONE_LAUNCHER=none`, role `gateway`, `Database=irongrind`, request logging on; started by the gitignored `Builds/start-e2e.ps1` (passwords read from `%LOCALAPPDATA%\IronGrind\secrets`, never printed); log `%LOCALAPPDATA%\IronGrind\gateway-e2e.log` |
| Zone | Unity 6000.3.10f1 Editor, scene `Assets/Scenes/Server/ZoneServer.unity`, active build profile `ZoneServer`, args `-zoneTemplate StartingZone -zoneId 1 -port 7777 -serverConfig …\Builds\ZoneServer\zone-config.json`, started by the gitignored `Builds/start-editor.ps1` (sets `IRONGRIND_PG_PASSWORD` for the Editor process only) |
| Client | Windows development player of `Assets/Scenes/DevHarness/ZoneEntryHarness.unity` (build profile `ZoneEntryHarness`) in `Builds/ZoneEntryHarness/`, launched windowed: `IronGrind.exe -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile %LOCALAPPDATA%\IronGrind\client-<x>.log` (`-zeUser zetest2 -zeCharacter Brannoc` for the second account, `-zeLoadDriver` for the load driver) |
| Harness config | `Assets/Scenes/DevHarness/ZoneEntryHarnessConfig.asset` — gitignored (holds a dev password); a fresh clone creates its own via *Create → IronGrind → Dev Harness → Zone Entry Config* |
| Tick log | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\IronGrind\tick-log-zone1.csv` (`tick,ms,durationMs`) |

Clocks: the client harness lines and the zone's `DevEvidenceLog` lines carry UTC timestamps from the same machine, so they can be ordered against each other. The gateway's console log has no timestamps.

### Defects found and fixed during the run

| # | Symptom | Cause | Fix |
|---|---------|-------|-----|
| 1 | Every idle tick warned `tick gap 50–60 ms` | At 20 Hz the nominal gap *is* 50 ms; ticks fire on frame boundaries | Tick log measures tick **duration** (budget 50 ms, AC-AUTH-05a wording) and flags gaps ≥ 100 ms (missed tick) — user decision |
| 2 | `The referenced script … is missing` in the player | `IronGrind.DevHarness` is Editor-only (ADR-012 Decision 7) | Zone-entry harness moved to its own assembly `IronGrind.DevHarness.Session` (`UNITY_EDITOR \|\| DEVELOPMENT_BUILD`, client references only); ADR-012 note; boundary test — user decision |
| 3 | `"127.0.0.1 " is not a valid hostname` | Trailing space typed into the config asset | Harness trims username, character, gateway URL, zone host |
| 4 | `[Netcode] NetworkConfig mismatch` on every connect | Harness set `ForceSamePrefabs = false` (Story 018 review), zone kept the default `true`; the flag is in NGO's config hash | Line removed |
| 5 | No zone log for `Admit` / `LoadCharacter` (AC-AUTH-01/21 need them) | Success paths did not log | `DevEvidenceLog` (dev builds only, enabled by `ZoneServerHost`) — UTC lines, never the token |
| 6 | Load run 1: clients froze; one client used 907 s CPU | Standalone players pause when unfocused; uncapped frame rate | Harness sets `Application.runInBackground = true`, vSync off, 30 fps |
| 7 | AC-6: token reconnect closed silently at the zone | Zone handshake check only knew admitted entries; the token-path branch (CR-TOK-9) was not implemented | Token-path reconnect implemented zone-side (see AC-6) — user decision |

Player build check (ADR-012 Verification 1, desktop half): `Builds/ZoneEntryHarness/IronGrind_Data/Managed/` holds `IronGrind.Foundation`, `IronGrind.Client`, `IronGrind.Client.Session.Unity`, `IronGrind.DevHarness.Session` — no `IronGrind.ServerLogic`, no Npgsql.

## Results

| QA case | Criterion | Result |
|---------|-----------|--------|
| AC-1 | AC-AUTH-01 (real link), AC-AUTH-21; Story 018 AC-9 | **PASS** |
| AC-2 | AC-AUTH-27 session steal | **PASS** |
| AC-3 | AC-AUTH-05a/05b tick budget under login load; Story 018 AC-10 | **PASS with note** (user decision 2026-11-12) |
| AC-4 | AC-AUTH-29 (d) gateway down | **PASS** (deviation: "still moving" → stays Entered/Connected) |
| AC-5 | ADR-016 Verification 2 | **PASS** |
| AC-6 | Round trip (token reconnect) | **PASS** after the defect 7 fix (first run FAIL) |

### AC-1 — login → zone entry (AC-AUTH-01, AC-AUTH-21)

Client A, account `zetest`, character `Juanperez` (AccountID 8, CharacterID 13):

```
[AdmittedEntries]   2026-11-12T00:28:03.475Z Admit AccountID(8) CharacterID(13) at tick 349        (zone)
[ZoneEntry]         2026-11-12T00:28:03.654Z login ok zone=1 port=7777                             (client)
[ZoneEntry]         2026-11-12T00:28:04.123Z handshake sent
[ZoneEntryAdmission]2026-11-12T00:28:04.201Z LoadCharacter start client 1 CharacterID(13)          (zone, once)
[ZoneEntryAdmission]2026-11-12T00:28:04.493Z LoadCharacter ok client 1 CharacterID(13) -> EntityID(1)
[ZoneEntry]         2026-11-12T00:28:04.560Z snapshot v=368
[ZoneEntry]         2026-11-12T00:28:04.567Z snapshot player name=Juanperez
[ZoneEntry]         2026-11-12T00:28:04.568Z snapshot entities=1 players=1
[ZoneEntry]         2026-11-12T00:28:04.569Z ack sent
[ZoneEntry]         2026-11-12T00:28:04.569Z entered
[ZoneEntryReclamation] ZoneEntryAck from client 1 for ZoneID(1) (snapshotVersion 368); slot EntityID(1) acked.
```

- `Admit` reached the zone 179 ms before the client had its `LoginResponse` (non-zero token, CharacterID 13, zoneId 1, port 7777) — PASS.
- `LoadCharacter` ran once; the session went `Connected` at slot creation (Gateway Story 012, `ReportAdmitted`); the name decoded from the snapshot (`EntityStateCodec`) equals the character name — PASS.
- Entry steps logged in order — Story 018 AC-9 PASS.
- `SessionReady not implemented` is logged by the zone (TD-104, expected).

### AC-2 — session steal (AC-AUTH-27)

Client A in the zone; client B logs in to the same account:

```
[CharacterPersistence] SaveSession for CharacterID(13) took 82.7313 ms (warning at 50 ms).        (zone: A saved)
[AdmittedEntries]   2026-11-12T00:28:42.967Z Admit AccountID(8) CharacterID(13) at tick 1138       (zone: B)
A: 00:28:42.995Z disconnected → 00:28:43.084Z reconnect failed → session ended
B: 00:28:43.043Z login ok → 00:28:43.653Z handshake sent → 00:28:43.797Z snapshot player name=Juanperez → 00:28:43.802Z entered
[ZoneEntryAdmission] … LoadCharacter ok client 2 CharacterID(13) -> EntityID(2)
```

A's character was saved before B's `Admit`; A was closed and its one token reconnect (Story 018 design) was refused; B entered — PASS. `SaveSession` ran on the write queue (82.7 ms, above its 50 ms warning; not on the tick).

### AC-3 — tick budget under login load (AC-AUTH-05a/05b)

Instrumentation (AC-AUTH-05b): `TickTimestampLog`, one CSV line per tick (`tick,ms,durationMs`), dev builds only; EditMode tests in `tests/EditMode/Networking/TickTimestampLog_tests.cs`.

Run 3 (valid): clients B (`zetest`) and C (`zetest2`/`Brannoc`) connected; 60 s baseline, then the load driver (10 logins in flight, 60 s); gateway at normal priority; Editor visible, Interaction Mode = No Throttling. Load window = ticks of the first to last load `Admit` (2186–3384); baseline = the 1200 ticks before.

| Window | Ticks | Mean duration | Max duration | Ticks > 50 ms | Max gap | Gaps ≥ 100 ms |
|--------|-------|---------------|--------------|---------------|---------|---------------|
| Baseline | 1200 | 0.07 ms | 2.6 ms | 0 | 63.0 ms | 0 |
| Load | 1199 | 1.30 ms | **9.5 ms** | **0** | 110.7 ms | 2 |

Load driver: `summary count=1608 ok=1608 min=262.8 median=363.8 max=822.2 ms` (Story 018 AC-10 — PASS).

Verdict: **PASS with note** (user decision). Every tick completed within the 50 ms budget (max 9.5 ms). Two frame stalls (110.7 ms at tick 2340, tick duration 3.3 ms; 104.5 ms at tick 2457, 0.1 ms) were Editor frame delays, not tick work; the load (~27 logins/s for 60 s, all processes on one 8-core machine) is far above real use. The stall check is repeated on the Linux server build on the VPS (Story 019).

Discarded runs: run 1 (482/482 logins; 7 ticks 56–148 ms, 269 gaps ≥ 100 ms) — clients were paused/uncapped (defect 6); run 2 — clients froze mid-run (defect 6). Separately, the first entry and first eviction of each Play session show one tick of 105–148 ms (Editor JIT warm-up); the second entry peaked at 36 ms.

### AC-4 — gateway down (AC-AUTH-29 d)

Clients B and C entered at 01:03:31 / 01:03:33; gateway process stopped at 01:03:40.865; checked at 01:04:27.442.

```
[GatewayLinkClient] LinkLost: SocketException: An existing connection was forcibly closed by the remote host..
[GatewayLinkClient] ConnectFailed: attempt 1 … retrying in 2000 ms.   … attempt 10 …
[ZoneTickProbe] second N: ticks=20 / 21   (every second of the window)
B, C: last line "entered" — no "session ended"
```

The zone kept ticking at 20–21 ticks/s and kept serving both clients (heartbeats kept their sessions `Connected`; no heartbeat timeout) — PASS. Deviation: the story says "a client still moving"; the harness has no movement input, so the criterion used is "client stays Entered and Connected for 30 s+". After the gateway was restarted the zone re-registered by itself (`ConnectFailed: attempt 80` → `LinkUp: registered with the gateway`; gateway `Zone 1 link registered from 127.0.0.1:61054`).

### AC-5 — Editor Play-mode exit (ADR-016 Verification 2)

```
[ZoneServer] host OnDestroy: NetworkManager alive=True
[GatewayLinkClient] LinkClosed: stopping.
[ZoneServer] teardown step 2a done in 74 ms
gateway: Zone link closed: zone 1 from 127.0.0.1:61054: peer closed
(≥ 10 s, Play mode entered again)
[ZoneInstancingWiring] ZoneID(1) Active at tick 0
[GatewayLinkClient] LinkUp: registered with the gateway.
gateway: Zone 1 link registered from 127.0.0.1:61076
```

Link thread stopped inside teardown step 2a; same ZoneID re-registered; 0 `StopTimedOut` in Editor.log — PASS.

### AC-6 — round trip (token reconnect)

Method: client D in the zone; the client process is **suspended** for 35 s (`NtSuspendProcess`, like an app suspended in the background) and resumed. Windows Firewall rules were not used: they do not filter loopback traffic.

First run (01:11:25.558 suspend, 01:12:00.579 resume):

```
D: 01:12:30.600Z disconnected → 01:12:30.648Z reconnect ok zone=1 port=7777 → 01:12:30.747Z handshake sent
   → 01:12:30.847Z disconnected → entry failed reason=ClosedBeforeAck → session ended
```

**FAIL.** The gateway token path and the zone's `Reconnect` (ghost found, token rotated) worked; the zone then closed the handshake silently because `GatewayZoneEntryTokenValidator` only resolves accounts through admitted entries — the CR-TOK-9 token-path branch (ghost session → `Reconnecting` → CR-NET-6.4 → `Connected`, slot rebind) was not implemented (defect 7).

Fix (user decision: fix in Story 017): the validator also resolves a ghost session by `CharacterID` (`ConnectionStateMachine.TryGetAccountByCharacter`, `FixedTimeEquals` through `SessionTokenStore`); `ZoneEntryAdmission.BeginReconnect` — zone not Active → `ZoneDoesNotExist`; `Disconnected_SessionActive → Reconnecting`; old connection closed; slot and entering tracking rebound to the new client (`ZoneSlotPool.RebindClient`, `ZoneEntryReclamation.RebindClient`); `CompleteReAuthSuccess` (CR-NET-6.4; pending purchases and respec reservation are stand-ins) → `Connected`; snapshot, `SessionReady` stub and cooldown snapshot to the new client; no `LoadCharacter`, no new EntityID, no `PlayerJoinedZone`. A rejected handshake now logs `EntryTokenRejected` once per connection (CR-TOK-9). Tests: `tests/EditMode/Gateway/ZoneTokenPathReconnect_tests.cs` (9); 4 existing entry-admission tests updated for the new warning. EditMode 7008 tests, 0 failures after the update (fixture re-run 51/51).

Re-run (client E; 01:33:18.509 suspend, 01:33:53.531 resume):

```
client  01:34:23.548Z disconnected
client  01:34:23.879Z reconnect ok zone=1 port=7777
client  01:34:23.980Z handshake sent
zone    01:34:24.008Z [ZoneEntryAdmission] Reconnect client 2 CharacterID(13) -> EntityID(1) (was client 1)
client  01:34:24.013Z snapshot v=8094 / snapshot player name=Juanperez
client  01:34:24.014Z ack sent / entered
zone    [ZoneEntryReclamation] ZoneEntryAck from client 2 for ZoneID(1) (snapshotVersion 8094); slot EntityID(1) acked.
client  still "entered" 20 s later
```

**PASS.** Login → UDP zone entry → transport drop → token reconnect → same character, same EntityID, rebound to the new connection, with no second `LoadCharacter`.

Open points from the fix (not blocking AC-6): nothing marks a pool slot `Disconnected_SessionActive`, so the snapshot's ghost flag and the zone-close steps never see ghosts (pre-existing); a ghost also accepts its unrotated token on a direct handshake (CR-TOK-9 wording allows it; the client always goes through the gateway); CR-NET-6.4 pending-purchase and respec stand-ins.

## Sign-off

AC-1, AC-2, AC-4, AC-5, AC-6 PASS; AC-3 PASS with note. Signed off by: Manuel Toscano, 2026-11-12
