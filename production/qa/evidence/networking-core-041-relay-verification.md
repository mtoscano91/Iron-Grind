# Story 041 - Cross-Zone Relay Verification (ADR-015 VR-0, 1, 3, 5)

> **Story**: `production/epics/networking-core/story-041-cross-zone-relay-npgsql-transport.md`
> **Host under test**: `src/ServerLogic/Networking/CrossZoneRelay/Verification/RelayVerificationHost.cs` (a test call site, not the composition root)
> **Code under test**: `NpgsqlRelayPublisher`, `NpgsqlRelayListener` (`src/ServerLogic/Networking/CrossZoneRelay/`)
> **Date**: TBD
> **Platform**: Windows Dedicated Server, Mono, Development Build, `-batchmode -nographics`, two processes on one machine (user decision 2026-10-19; deviation from the IL2CPP Linux server of ADR-007, recorded here)
> **Database**: portable PostgreSQL 16.15 (`tools/postgres/README.md`, "Without Docker"), 127.0.0.1:5432
> **Npgsql**: 8.0.9 (`docs/server-libraries.md`)
> **Status**: NOT RUN (skeleton)

## Build settings

- Unity 6000.3.10f1; Unity Hub module "Windows Dedicated Server Build Support" installed (precondition).
- Build Profile: **Windows Server**, Scripting Backend **Mono**, **Development Build** on.
- Scenes in build: only `Assets/Scenes/Verification/RelayVerification.unity`.
- Output folder: `Builds/RelayVerification/` (git-ignored; executable name recorded here: TBD).
- No build script; built by hand from the Build Profiles window.

## Scene setup (manual, in the open Editor)

1. File > New Scene > Empty; no camera, no light.
2. Create one GameObject named `RelayVerificationHost`; add the `RelayVerificationHost` component (it appears only because `IronGrind.ServerLogic` compiles in the Editor).
3. Save as `Assets/Scenes/Verification/RelayVerification.unity`.

## Run procedure

Server: start PostgreSQL as `tools/postgres/README.md` step 3 describes (PowerShell `Start-Process`, not Git Bash). Create two PowerShell windows (processes A and B). `IRONGRIND_DEV_PG` is an Npgsql connection string; do not print it. For VR-5 add `Maximum Pool Size=2` to it (process A's window only).

```powershell
# Process A (publishes every 10 s)
$env:IRONGRIND_DEV_PG = [Environment]::GetEnvironmentVariable('IRONGRIND_DEV_PG','User')
& .\Builds\RelayVerification\<exe-name>.exe -batchmode -nographics -logFile .\Builds\RelayVerification\A.log -relayZone A -relayPublishIntervalSec 10

# Process B (listens only)
$env:IRONGRIND_DEV_PG = [Environment]::GetEnvironmentVariable('IRONGRIND_DEV_PG','User')
& .\Builds\RelayVerification\<exe-name>.exe -batchmode -nographics -logFile .\Builds\RelayVerification\B.log -relayZone B -relayPublishIntervalSec 0
```

Optional: `-relayTickHz <n>` (default 30), `-relayHoldPoolConnections <n>` (VR-5). Log lines to look for, all prefixed `[RelayVerificationHost]`: `Started`, `Publishing`, `LocalDelivery`, `InboxTryAdd` (listener thread id and name), `RelayedOnTick` (main thread id), `TickSummary` (once per second, max and avg ms), `SlowTick` (> 50 ms). Listener and publisher logs have their own prefixes.

Stop a process by closing it (Stop-Process); a clean exit runs `OnDestroy` (listener `Stop()`, held connections and data source disposed).

## VR-0 Npgsql version

**Documentation half (pre-filled)**: `NpgsqlConnection.Wait(int)`, `NpgsqlConnection.Notification`, `NpgsqlNotificationEventArgs.Payload` and `NpgsqlDataSource.CreateCommand(string)` are present in Npgsql 8.0.9's `Npgsql.xml`; see `docs/engine-reference/unity/plugins/npgsql-dapper.md`, section "API members present in Npgsql 8.0.9".

**Compile half**: the publisher, listener and host compile against the pinned DLL in the Editor and in the Windows Server build.

**PASS** if the Editor and the Windows Server build both compile with no `error CS` and no Npgsql reference or load error (grep `error CS` and `Npgsql` in the Editor log and the build log). **FAIL** otherwise.

**Result**: PENDING
**Observation**: _TBD_

## VR-1 Cross-process notification

**Procedure**: start B, then A (publish interval 10 s). Wait for at least three publishes.
**Expected**: B logs `InboxTryAdd` with a thread name `IronGrindRelayListener` and a managed thread id different from the main thread id, then `RelayedOnTick` with the main thread id, once per publish and with the counter in the player name (`A-1`, `A-2`, ...). A logs `LocalDelivery` for its own message and no `RelayedOnTick` for it (own origin dropped).

**PASS** if, for at least three publishes: B logs exactly one `InboxTryAdd` and one `RelayedOnTick` per counter value; every `InboxTryAdd` names thread `IronGrindRelayListener` with an id different from the `mainThread` id in B's `Started` line; every `RelayedOnTick` shows `thread` equal to `main`; A logs no `RelayedOnTick`. **FAIL** on a missing or duplicated counter value, a notification handled on the main thread, or a `RelayedOnTick` in A.

**Result**: PENDING
**Observation**: _TBD (thread ids, counters seen, any duplicate)_

## VR-2 Stripping

**Result**: NOT RUN. No IL2CPP on the dev machine (user decision 2026-10-19). Carried by the epic's headless-build gate. Expected cover: the `Npgsql` entry in `Assets/link.xml` (`<assembly fullname="Npgsql" preserve="all" />`).

## VR-3 Database restart

**Procedure**: both processes running, A publishing every 5 s (`-relayPublishIntervalSec 5`). Stop PostgreSQL (`pg_ctl.exe stop -D "$env:LOCALAPPDATA\IronGrind\pgdata" -m fast`), wait about 20 s (several backoff steps), start it again with the PowerShell `Start-Process` command of `tools/postgres/README.md` step 3.
**Expected**: both listeners log the loss, reconnect with backoff (500 ms doubling to 10 s), re-issue `LISTEN`; the `Publishing` lines continue; after the reconnect B receives each new counter value exactly once (no duplicate after the reconnect; messages published while the database was down are lost, by design); `TickSummary` keeps appearing every second with no `SlowTick` across the restart.

**PASS** if all of these hold:
- each process logs `[NpgsqlRelayListener] ConnectionLost` with `reconnecting in` values 500, 1000, 2000, ... (none above 10000) while the database is down, and no further `ConnectionLost` once it is back;
- B logs `RelayedOnTick` for every counter value published after its listener reconnected, each exactly once; no counter value appears twice anywhere in B's log;
- across the whole run, zero `SlowTick` lines and every `TickSummary` `maxMs` below 50.

**FAIL** on a missing reconnect, a duplicated counter value, a `SlowTick`, or a `TickSummary` `maxMs` of 50 or more. Values published while the database was down, or before B's `LISTEN` was re-issued, are lost by design and do not fail the item.

**Result**: PENDING
**Observation**: _TBD (time of loss, backoff steps, first message after reconnect, max tick ms across the restart)_

## VR-4 Teardown

**Result**: MOVED. Belongs to the composition-root / ADR-009 teardown story (open item C2 in `docs/architecture/architecture-traceability.md`). Not run here.

## VR-5 Publish never blocks

**Baseline (first).** Run A alone for 30 s with the idle-run connection string and `-relayPublishIntervalSec 5`; record the largest `TickSummary` `maxMs` as the baseline.

**Part 1: pool exhausted.** Start A with `Maximum Pool Size=2` in its connection string and `-relayHoldPoolConnections 2 -relayPublishIntervalSec 5`; B as above. The host holds both pooled connections for its lifetime, so each publish has no free connection.
**Expected**: `Publishing` lines continue; `TickSummary` max stays at its idle level (no tick lengthened by a publish); after `RELAY_PUBLISH_TIMEOUT_MS` (2 000 ms) each publish logs a timeout warning and frees its slot; `inFlightPublishes` in `TickSummary` stays at or below 8 and returns to 0 while publishing is stopped; B receives nothing from A.

**Part 2: hung server.** Start A against the real database with the idle-run connection string and wait for the first publish to succeed. Then suspend every PostgreSQL process with Sysinternals PsSuspend (`pssuspend64.exe postgres`, elevated PowerShell), so TCP stays open and nothing answers. (`pg_sleep` does not apply: `pg_notify` has no sleep to inject.) Watch three publishes, then resume (`pssuspend64.exe -r postgres`).
**Expected**: while suspended, each publish is cancelled after about 2 s (warning logged), its slot is freed, `inFlightPublishes` does not grow past the cap and no `SlowTick` appears; after the resume the relay recovers (the listener may reconnect, see VR-3). PsSuspend is the method for this run; if it cannot be used, record why here before changing the method.

**PASS** if, in both parts: every publish logs `[NpgsqlRelayPublisher] PublishTimedOut` (or `PublishFailed` naming a cancellation or timeout) between 2 000 and 5 000 ms (by log timestamps) after its `Publishing` line. The upper bound allows for Npgsql's own `Cancellation Timeout` (default 2 000 ms): on a hung server it first sends a cancel request and waits that long before breaking the connection. Record the measured times either way; `inFlightPublishes` never exceeds 8 and is 0 within 3 s of the last publish; zero `SlowTick` lines; every `TickSummary` `maxMs` is at most the baseline + 5 ms. **FAIL** otherwise.

**Result**: PENDING
**Observation**: _TBD (baseline maxMs; part 1: tick max vs baseline, timeout lines; part 2: method used, cancellation time, slot count)_

## VR-6 Leaving Play mode

**Result**: MOVED. Belongs to the composition-root / ADR-009 teardown story (open item C2). Not run here.

## Deviations

- Windows Dedicated Server, Mono, instead of the IL2CPP Linux server (user decision 2026-10-19). VR-2 is therefore not run.
- The host's inbox decorator logs from `TryAdd`; the production inbox does not. It is verification-only.
