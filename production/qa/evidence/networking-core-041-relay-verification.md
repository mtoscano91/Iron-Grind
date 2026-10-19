# Story 041 - Cross-Zone Relay Verification (ADR-015 VR-0, 1, 3, 5)

> **Story**: `production/epics/networking-core/story-041-cross-zone-relay-npgsql-transport.md`
> **Host under test**: `src/ServerLogic/Networking/CrossZoneRelay/Verification/RelayVerificationHost.cs` (a test call site, not the composition root)
> **Code under test**: `NpgsqlRelayPublisher`, `NpgsqlRelayListener` (`src/ServerLogic/Networking/CrossZoneRelay/`)
> **Date**: 2026-10-19 (runs 15:30 to 16:10)
> **Platform**: Windows Dedicated Server, Mono, Development Build, `-batchmode -nographics`, two processes on one machine (user decision 2026-10-19; deviation from the IL2CPP Linux server of ADR-007, recorded here)
> **Database**: portable PostgreSQL 16.15 (`tools/postgres/README.md`, "Without Docker"), 127.0.0.1:5432
> **Npgsql**: 8.0.9 (`docs/server-libraries.md`)
> **Status**: RUN. VR-0 PASS, VR-1 PASS (run 2), VR-3 PASS (attempt 2), VR-5 part 1 PASS and part 2 FAIL (accepted as TD-074). VR-2 NOT RUN (carried by the headless-build gate). VR-4 and VR-6 moved.

## Build settings

- Unity 6000.3.10f1; Unity Hub module "Windows Dedicated Server Build Support" installed (precondition).
- Build Profile: **Windows Server**, Scripting Backend **Mono**, **Development Build** on.
- Scenes in build: only `Assets/Scenes/Verification/RelayVerification.unity`.
- Output folder: `Builds/RelayVerification/` (git-ignored; executable: `Builds/RelayVerification/IronGrind.exe`; 11 server DLLs present in `IronGrind_Data/Managed`).
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
& .\Builds\RelayVerification\IronGrind.exe -batchmode -nographics -logFile .\Builds\RelayVerification\A.log -relayZone A -relayPublishIntervalSec 10

# Process B (listens only)
$env:IRONGRIND_DEV_PG = [Environment]::GetEnvironmentVariable('IRONGRIND_DEV_PG','User')
& .\Builds\RelayVerification\IronGrind.exe -batchmode -nographics -logFile .\Builds\RelayVerification\B.log -relayZone B -relayPublishIntervalSec 0
```

Optional: `-relayTickHz <n>` (default 30), `-relayHoldPoolConnections <n>` (VR-5). Log lines to look for, all prefixed `[RelayVerificationHost]`: `Started`, `Publishing`, `LocalDelivery`, `InboxTryAdd` (listener thread id and name), `RelayedOnTick` (main thread id), `TickSummary` (once per second, max and avg ms), `SlowTick` (> 50 ms). Listener and publisher logs have their own prefixes.

Stop a process by closing it (Stop-Process); a clean exit runs `OnDestroy` (listener `Stop()`, held connections and data source disposed).

## VR-0 Npgsql version

**Documentation half (pre-filled)**: `NpgsqlConnection.Wait(int)`, `NpgsqlConnection.Notification`, `NpgsqlNotificationEventArgs.Payload` and `NpgsqlDataSource.CreateCommand(string)` are present in Npgsql 8.0.9's `Npgsql.xml`; see `docs/engine-reference/unity/plugins/npgsql-dapper.md`, section "API members present in Npgsql 8.0.9".

**Compile half**: the publisher, listener and host compile against the pinned DLL in the Editor and in the Windows Server build.

**PASS** if the Editor and the Windows Server build both compile with no `error CS` and no Npgsql reference or load error (grep `error CS` and `Npgsql` in the Editor log and the build log). **FAIL** otherwise.

**Result**: PASS
**Observation**: 2026-10-19 15:23, Editor compile 0 `error CS`, EditMode 3145 / 3146 (1 explicit skip); Windows Server build (Mono, Development, Build Profile `RelayVerification`, scene `RelayVerification` only) "Build completed with a result of 'Succeeded'" in 114 s, 0 `error CS` in the Editor log; `IronGrind_Data/Managed` holds `IronGrind.ServerLogic.dll`, `Npgsql.dll` and the 10 other server DLLs of `docs/server-libraries.md`. Runtime load of Npgsql is shown by VR-1.

## VR-1 Cross-process notification

**Procedure**: start B, then A (publish interval 10 s). Wait for at least three publishes.
**Expected**: B logs `InboxTryAdd` with a thread name `IronGrindRelayListener` and a managed thread id different from the main thread id, then `RelayedOnTick` with the main thread id, once per publish and with the counter in the player name (`A-1`, `A-2`, ...). A logs `LocalDelivery` for its own message and no `RelayedOnTick` for it (own origin dropped).

**PASS** if, for at least three publishes: B logs exactly one `InboxTryAdd` and one `RelayedOnTick` per counter value; every `InboxTryAdd` names thread `IronGrindRelayListener` with an id different from the `mainThread` id in B's `Started` line; every `RelayedOnTick` shows `thread` equal to `main`; A logs no `RelayedOnTick`. **FAIL** on a missing or duplicated counter value, a notification handled on the main thread, or a `RelayedOnTick` in A.

**Result**: PASS (run 2)
**Run 1 (2026-10-19 ~15:30, FAIL, fixed)**: B then A, A publishing every 5 s, about 19 s. On both processes the listener logged `[NpgsqlRelayListener] ConnectionLost: consecutiveFailures=1 - NpgsqlException: Exception while reading from stream` 17 times, about once per second. LISTEN succeeded each time and every empty `Wait(500)` broke the connection (Npgsql's synchronous Wait under Unity Mono, see `docs/engine-reference/unity/plugins/npgsql-dapper.md`). One of three messages arrived: `InboxTryAdd` for A-2 on thread 7 "IronGrindRelayListener", then `RelayedOnTick` on main thread 1. A-1 and A-3 were lost in the reconnect gaps. maxMs stayed at or below 1.6. Fix: the listener connection uses `WaitAsync` (ADR-015 note 2026-10-19).

**Observation (run 2, 2026-10-19 15:34, after the WaitAsync fix; logs in the git-ignored `Builds/RelayVerification/run2/`)**: B started, then A publishing every 5 s. A published A-1 to A-5, each with its own `LocalDelivery`. B logged exactly one `InboxTryAdd` per value, each on thread 6 `IronGrindRelayListener` (B's `Started` line has `mainThread=1`), and then one `RelayedOnTick` with `thread=1 (main=1)`. No value was missing or duplicated. A logged no `RelayedOnTick`, so its own origin was dropped. There was no listener failure in the 30 s before VR-3. Finding (not a VR-1 criterion): A's first publish cost 58 ms on the tick (`SlowTick` at tick 159, the A-1 publish). That is the first use of the data source, which opens the first physical connection with SCRAM authentication and runs the Npgsql path for the first time under Mono JIT. Publishes 2 to 5 cost 0.7 to 2.3 ms; idle ticks are 0.005 to 0.01 ms.

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

**Attempt 1 (2026-10-19 15:36, same processes as VR-1 run 2; incomplete evidence)**: PostgreSQL stopped at 15:36:16 (`pg_ctl stop -m fast`) and restarted at 15:36:43. Both listeners logged `ConnectionLost` with `PostgresException 57P01` (terminating connection due to administrator command), then `Failed to connect` (refused) for failures 2 to 6, and `ReconnectAlert` on the 5th. Then no further failure, so both reconnected. Gaps between failures, from the one-per-second `TickSummary` lines: 2, 3, 4, 6, 10 s, which is backoff 0.5, 1, 2, 4, 8 s plus about 1.5 to 2 s per refused connect on Windows. B received A-1 to A-11, then A-19 to A-22, each exactly once (no duplicates). A-12 failed on a pooled connection broken by the restart (`PublishFailed`). A-13 to A-16 timed out at 2 000 ms while the server was down (`PublishTimedOut status=Canceled`). A-17 and A-18 went out while B's listener was still in its 10 s backoff, so they were lost by design. Ticks from the stop to the end of the run: max 3.75 ms, no `SlowTick`. The `reconnecting in N ms` text and `socketError` were cut off the log lines: Unity's native log stops at the NUL that ends Mono's Winsock message. Fixed (`LogSafe` in `NpgsqlRelayListener`, with a test). VR-3 to be repeated on the rebuilt player for the logged backoff values.

**Observation (attempt 2, 2026-10-19 15:47, rebuilt player with `LogSafe` and the startup warm-up; logs in `Builds/RelayVerification/run3/`)**: B started, then A publishing every 5 s; B received A-1 to A-3. PostgreSQL stopped at 15:48:12 and was ready again at 15:48:39. Both listeners logged, in order: `consecutiveFailures=1 PostgresException 57P01` reconnecting in 500 ms; 2 `socketError=ConnectionRefused` 1000 ms; 3 2000 ms; 4 4000 ms; `ReconnectAlert: 5` 8000 ms; 6 10000 ms. Then no further failure, so the reconnect and the re-issued LISTEN succeeded. A's publisher: A-4 `PublishFailed` (pooled connection broken by the stop); A-5 to A-8 `PublishTimedOut status=Canceled timeoutMs=2000`; A-9 and A-10 sent while B was still in its 10 s backoff, lost by design. B then received A-11 to A-16, each exactly once; no value appears twice in B's log. From the first `ConnectionLost` to the end of the run, every `TickSummary` `maxMs` was at most 2.84 and there was no `SlowTick`. The run's only `SlowTick` (A, tick 159, 74.7 ms) is A's first publish, before the restart; it is tracked under VR-5.

**Result**: PASS (attempt 2), with the restart window as the measured window; the first-publish spike is a VR-5 item.

## VR-4 Teardown

**Result**: MOVED. Belongs to the composition-root / ADR-009 teardown story (open item C2 in `docs/architecture/architecture-traceability.md`). Not run here.

## VR-5 Publish never blocks

**Baseline (first).** Run A alone for 30 s with the idle-run connection string and `-relayPublishIntervalSec 5`; record the largest `TickSummary` `maxMs` as the baseline.

**Part 1: pool exhausted.** Start A with `Maximum Pool Size=2` in its connection string and `-relayHoldPoolConnections 2 -relayPublishIntervalSec 5`; B as above. The host holds both pooled connections for its lifetime, so each publish has no free connection.
**Expected**: `Publishing` lines continue; `TickSummary` max stays at its idle level (no tick lengthened by a publish); after `RELAY_PUBLISH_TIMEOUT_MS` (2 000 ms) each publish logs a timeout warning and frees its slot; `inFlightPublishes` in `TickSummary` stays at or below 8 and returns to 0 while publishing is stopped; B receives nothing from A.

**Part 2: hung server.** Start A against the real database with the idle-run connection string and wait for the first publish to succeed. Then suspend every PostgreSQL process with Sysinternals PsSuspend (`pssuspend64.exe postgres`, elevated PowerShell), so TCP stays open and nothing answers. (`pg_sleep` does not apply: `pg_notify` has no sleep to inject.) Watch three publishes, then resume (`pssuspend64.exe -r postgres`).
**Expected**: while suspended, each publish is cancelled after about 2 s (warning logged), its slot is freed, `inFlightPublishes` does not grow past the cap and no `SlowTick` appears; after the resume the relay recovers (the listener may reconnect, see VR-3). PsSuspend is the method for this run; if it cannot be used, record why here before changing the method.

**PASS** if, in both parts: every publish logs `[NpgsqlRelayPublisher] PublishTimedOut` (or `PublishFailed` naming a cancellation or timeout) between 2 000 and 5 000 ms (by log timestamps) after its `Publishing` line. The upper bound allows for Npgsql's own `Cancellation Timeout` (default 2 000 ms): on a hung server it first sends a cancel request and waits that long before breaking the connection. Record the measured times either way; `inFlightPublishes` never exceeds 8 and is 0 within 3 s of the last publish; zero `SlowTick` lines; every `TickSummary` `maxMs` is at most the baseline + 5 ms. **FAIL** otherwise.

**Baseline (2026-10-19 15:53, A alone, 30 s, publishing every 5 s; rebuilt host with the parameterized warm-up and `PublishCallMs`; logs in `run4-baseline/`)**: `WarmUp` took 355.9 ms at startup. The largest `TickSummary` `maxMs` was **23.936**, on the tick of the first publish: `PublishCallMs n=1` 8.6 ms, and the rest is the host's first `Debug.Log` calls in a development build. Later publish calls cost 0.33 to 1.19 ms, with one outlier at 11.1 ms (n=6; a Mono GC pause is suspected but not shown). There was no `SlowTick`. Without the warm-up, the first publish tick cost 58 to 75 ms (VR-1 run 2, VR-3 attempt 2), which is why the composition-root story must warm the data source before its tick loop.

**Part 1 observation (2026-10-19 15:55; logs in `run5-vr5p1/`)**: B listening. A ran with `Maximum Pool Size=2` (inside the process environment only) and `-relayHoldPoolConnections 2`, so `PoolHeld: held=2 maxPoolSize=2`. Six publishes were sent (ticks 158 to 907), and each logged `PublishTimedOut status=Canceled timeoutMs=2000` two `TickSummary` lines after its `Publishing` line, i.e. between 2 and 3 s, consistent with the 2 000 ms cancellation. `PublishCallMs` was 6.3 ms for the first call and 0.08 to 0.17 ms for the rest. `inFlightPublishes` was 1 in 12 summaries and 0 in 22: never above 1, and back to 0 after each timeout. B received nothing. A and B had no `SlowTick`. A's largest `maxMs` was 11.396, below baseline + 5 = 28.936. **Part 1: PASS.**

**Part 2 method change (recorded with this run)**: the user has no PsSuspend. The PostgreSQL processes run under the user's account, so they were suspended with the same Windows call PsSuspend makes, `NtSuspendProcess` (and resumed with `NtResumeProcess`), through a small PowerShell helper with `Add-Type`, not elevated. All 10 postgres processes were suspended (rc 0) and resumed (rc 0).

**Part 2 observation (2026-10-19 16:07; logs in `run6-vr5p2/`)**: B listening, A publishing every 5 s with the normal pool. After B received A-1, PostgreSQL was suspended from 16:07:52 to 16:08:15. A-2, A-3 and A-4 each logged `PublishTimedOut status=TimedOut` about 6.7 s after their publish, which is the ADR-011 completion-queue watchdog (200 ticks at 30 Hz), **not** the 2 000 ms cancellation. A-5 completed `status=Canceled` after the resume. `inFlightPublishes` reached 2 (cap 8). There was no `SlowTick`; A's largest `maxMs` was 26.764, below baseline + 5 = 28.936. `PublishCallMs` stayed between 0.4 and 1.6 ms during the suspension. After the resume the relay recovered: B received A-6 to A-9, each once. B also received A-2, whose publish had been reported timed out with an unknown outcome. That is allowed by Decision 6 (at most once, outcome unknown), but it means a "timed out" publish can still arrive. **Repeat with `Cancellation Timeout=-1`** (A only, in the process environment; logs in `run7-vr5p2-cto/`): the same result, with A-2 to A-4 freed by the watchdog at about 7 s. So Npgsql 8.0.9 under Mono does not complete a cancelled command against a frozen server.

**Result**: Part 1 PASS. **Part 2 FAIL**: the slot is freed at the watchdog, not at `RELAY_PUBLISH_TIMEOUT_MS`. The tick-related conditions passed (no `SlowTick`, max at most baseline + 5, in-flight at most 8). Escalated and accepted by the user on 2026-10-19 as TD-074, with an ADR-015 Decision 2 note. A FAIL does not by itself block `/story-done` (story rule).

## VR-6 Leaving Play mode

**Result**: MOVED. Belongs to the composition-root / ADR-009 teardown story (open item C2). Not run here.

## Deviations

- Windows Dedicated Server, Mono, instead of the IL2CPP Linux server (user decision 2026-10-19). VR-2 is therefore not run.
- The host's inbox decorator logs from `TryAdd`; the production inbox does not. It is verification-only.
- Found and fixed during the run (2026-10-19, user decisions):
  - The listener uses `WaitAsync` instead of the synchronous `Wait`, which treated every empty wait as a broken connection under Unity Mono (ADR-015 Decision 3 note).
  - Listener log text is cleaned of control characters, because Unity's log stopped at the NUL that ends Mono's Winsock messages.
  - The host warms the data source before the first tick, because the first publish cost 58 to 75 ms; the composition-root story must do the same (Networking Core EPIC).
- VR-5 part 2 suspended PostgreSQL with `NtSuspendProcess` directly instead of PsSuspend, which makes the same call.
- Run logs are kept outside git in `Builds/RelayVerification/run1` to `run7-vr5p2-cto`.
