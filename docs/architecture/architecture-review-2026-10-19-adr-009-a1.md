# Architecture Review — ADR-009 Amendment 1 and ADR-006 Amendment 3

Date: 2026-10-19
Engine: Unity 6.3 LTS (6000.3.10f1); NGO 2.13.3, Unity Transport 2.6.0, Npgsql 8.0.9
Mode: focused review of two Proposed amendments (committed in `c9777dc`); not a full GDD traceability pass
ADRs reviewed: ADR-009 Amendment 1, ADR-006 Amendment 3; cross-checked against ADR-002, 004, 007, 011, 013, 014, 015
GDDs checked: `zone-instancing.md` (CR-ZI-12), `navigation-pathfinding.md` (CR-NAV-3)
Engine specialist: unity-specialist (read-only), no blocking finding

## Verdict: CONCERNS — nothing blocking; both amendments Accepted 2026-10-19 by the user after R1–R3 were folded in

C2 (teardown order) and P4 (startup) in `architecture-traceability.md` are closed. C1 (zone process supervision) stays open.

## Confirmed

- **Code names.** Every type and member S5 and the teardown name exist with the shape the amendment uses: `RandomProviderFactory.CreateSeededFromEntropy(out int)`, `ServerTickLoop.TICK_RATE_HZ` (20), `RegisterTickDriven`, `AdvanceTick`, 7-argument `ZoneTickPipeline`, `NpgsqlRelayPublisher(NpgsqlDataSource, ITickCompletionQueue, CrossZoneRelayOrigin, …)`, `NpgsqlRelayListener(string, CrossZoneRelayReceiver, …)`, `ServerWideBroadcaster(sink, publisher, allowlist)`, `ITickCompletionQueue.DrainOnShutdown(TimeSpan)`, `RELAY_LISTENER_STOP_TIMEOUT_MS` = 2000, `NgoInboundTransportShell`, `NpgsqlCharacterRecordStore(NpgsqlDataSource)`.
- **GDD order.** Teardown step 3 (nav `Teardown`) precedes CR-ZI-12, as CR-NAV-3 requires; step 4b sends CR-ZI-12 step 4's `ZoneSessionEnded` after the tick stops.
- **Cross-ADR.** ADR-002 Decision 3's wait for the current tick holds by construction (the tick is synchronous on the main thread). ADR-011 Decision 2's drain is at step 5, after the writes CR-ZI-12 starts; the dated note on ADR-011 (two blocking waits) agrees. ADR-013 Decision 4: one generator, created in S3. ADR-014: one `RegisterTickDriven` call. ADR-015: listener connection string and the two stop paths (step 2a, `OnDestroy`).
- **Pool.** Amendment 3 equals ADR-007's string (ADR-007 uses Npgsql's keyword aliases `CommandTimeout`, `MinPoolSize`, `MaxPoolSize`) plus `Connection Idle Lifetime=60`. 4 zones × 10 + 4 listener connections = 44 < 100.
- **Engine (unity-specialist).** `NetworkTickSystem` exists only after `StartServer`, so subscribing in S9 after S8 is right; teardown step 1 must null-guard it (the text says every step null-guards). `targetFrameRate` is honoured in `-batchmode -nographics`. `Task.Run(...).Wait()` at S4 cannot deadlock on the player's context. `Application.Quit(int)` sets the exit status once `wantsToQuit` lets it through, which `quitAllowed` does.

## Findings

| # | Severity | Where | Finding | Resolution |
|---|---|---|---|---|
| R1 | Fix before acceptance | Teardown step 6 | The wait on `ShutdownInProgress` had no cap; a destroyed or inactive `NetworkManager` never reaches its completion frame, so teardown would hang until systemd kills it. | **Folded in:** wait while `NetworkManager != null && ShutdownInProgress`, at most `NGO_SHUTDOWN_WAIT_FRAMES` (server config, 100); warning at the cap. |
| R2 | Fix before acceptance | A1-1 | Whether SIGTERM on the Linux player reaches `wantsToQuit` is unknown (specialist: medium-low confidence that it does not). The fallback named only "a native signal shim". | **Folded in:** `PosixSignalRegistration` if Unity's profile has it, otherwise a native plugin; the handler only sets a volatile flag read by `Update`. |
| R3 | Fix before acceptance | A1 checks | All checks named the Linux IL2CPP build; this machine has only the Windows Dedicated Server (Mono) module, and Windows has no SIGTERM. | **Folded in:** A1-3, A1-5, A1-6 (and a first A1-4) run locally; A1-1, A1-2, A1-7 and the final A1-4 go to the epic's Linux IL2CPP headless-build gate. |
| R4 | Note | CR-ZI-12 step 5 | `NotifyZoneExit` fires before the step-2 saves complete (their completions arrive in the drain). The GDD allows it only with "guaranteed ordering" in the write queue. | Migration Plan note for the Zone Instancing story that replaces the stand-in. |
| R5 | Propagation | Stale text | ADR-006 diagram (pool 20), ADR-015 Consequences (pool 20), control manifest lines 71 and 79 (`Process.Exit(0)`). | ADR-006 and ADR-015 updated; manifest left to `/create-control-manifest update`. |
| R6 | Note | ADR-007 C1 | `Restart=always` also restarts a zone that exited `Clean`. | Added to Migration Plan step 4 (ADR-007 C1 amendment). |
| R7 | Note | S8 | `NetworkConfig.TickRate` is `uint`, `TICK_RATE_HZ` is `int`. | Story note: explicit cast. Also noted: NGO's `DontDestroy` already applies `DontDestroyOnLoad` to a root `NetworkManager`. |

## Cross-ADR conflicts

None new. C2 closed. C1 open (ADR-007 supervision; now also covers `Restart=always` after a clean exit).

## Registry

Added (`docs/registry/architecture.yaml`): state `zone_process_object_graph`; API decisions `zone_process_lifecycle`, `postgres_connection_pool`; forbidden patterns `server_construction_outside_composition_root`, `teardown_inside_tick_or_quit_handler`.

## Next

1. `/create-control-manifest update` (pool, exit call, teardown order, new forbidden patterns).
2. `/create-stories networking-core` for the composition-root stories (042+), carrying ADR-015 Verification 4 and 6, A1-1 to A1-7 with the R3 split, and the R4/R7 notes.
3. ADR-007 C1 amendment (supervision, exit codes, `TimeoutStopSec`, `Restart=`).
