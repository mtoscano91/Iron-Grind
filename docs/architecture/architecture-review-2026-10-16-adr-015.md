# Architecture Review — ADR-015 only

Date: 2026-10-16
Engine: Unity 6.3 LTS (6000.3)
Scope: ADR-015 Cross-Zone Message Relay (Proposed, commit `f122de5`), reviewed alone for acceptance. No traceability matrix rebuilt; `tr-registry.yaml` unchanged.
ADRs read: ADR-015 in full; the cited parts of ADR-006, ADR-007, ADR-009, ADR-012, ADR-014.
Code read: `ClientMessageOutbox.cs`, `ZoneTickPipeline.cs`, `ITickCompletionQueue.cs`, `TickCompletionQueue.cs`, `InboundTransportAdapter.cs` (`AddClient` call), `MessageRoutingRegistry.cs` / `ServerBroadcast_Enhancement9*.cs` (grep).

## Verdict: CONCERNS — stays Proposed

The decision is sound and does not conflict with another ADR. Its amendment of ADR-014 Decision 6 is deliberate, and Migration Plan step 4 already gives ADR-014 the note it needs. All dependencies are Accepted. Two items to fix before acceptance (A1, A2) and four small items (R1–R4).

## Claims confirmed

| Claim in ADR-015 | Checked against | Result |
|---|---|---|
| `ClientMessageOutbox` holds the zone's client set (`AddClient` / `RemoveClient`); `Enqueue(clientId, typeId, body, isCapExempt)` never throws | `ClientMessageOutbox.cs` lines 141, 158, 192 | ✅ |
| `ZoneTickPipeline` has four isolated steps, `LogStepFailed`, `STEP_*` names; step 1a fits the pattern | `ZoneTickPipeline.cs` | ✅ |
| `ITickCompletionQueue.Track` takes a started task and a caller-owned `CancellationTokenSource`; 10 s watchdog | `ITickCompletionQueue.cs`; `PERSISTENCE_WATCHDOG_TICKS = 200` | ✅ |
| Npgsql is referenced by `IronGrind.ServerLogic` only; Character Persistence Story 012 brings it in | ADR-012 Decision 1; `story-012-npgsql-and-dapper-in-the-project.md` | ✅ |
| IL2CPP server backend; PostgreSQL on loopback; `zone_server` role; gateway over a Unix socket, "Redis or file-based" on scale-out; example pool 10 | ADR-007 lines 15–43, 84–115 | ✅ |
| ADR-006 pool `Maximum Pool Size=20`; `link.xml` for IL2CPP | ADR-006 lines 133, 214 | ✅ |
| `ServerBroadcast_Enhancement9` — S→ALL every zone, not cap-exempt; body 4–52 bytes | `networking-channel-contract.md` line 110; codec | ✅ |
| Dependencies Accepted: ADR-002, 004, 006, 007, 010, 011, 014 | Status lines | ✅ |
| TR-enh-010 registered | `tr-registry.yaml` line 350 | ✅ |

## To fix before acceptance

**A1 — Nothing stops the listener thread when the Editor leaves Play mode.** Decision 3 ("Editor") says "the same teardown stops the thread on leaving Play mode". The teardown it relies on, ADR-009 Decision 3, runs when a zone enters `Closed` and ends in a process exit. Leaving Play mode in the Editor does not run it. A background thread blocked in a socket read inside `NpgsqlConnection.Wait` can hold up a domain reload.
Fix: name the hook that stops the listener on leaving Play mode (the server host's `OnDestroy` / `Application.quitting`, or equivalent) and add a test or evidence item for it. Also add ADR-009 to Depends On, and extend Migration Plan step 4 with a dated note on ADR-009 Decision 3 adding a "stop relay listener" step after the tick loop stops. This adds to the open teardown-order item C2 in `architecture-traceability.md`.

**A2 — Publishing can block the tick (R4).** Decision 2 runs `ExecuteNonQueryAsync` "on a pooled connection from the zone's existing ADR-006 pool" but does not say how the connection is opened. A synchronous `Open()` on an exhausted pool waits up to the connection `Timeout` (5 s in ADR-007's string) on the tick thread.
Fix: state that opening the connection is part of the one tracked task (for example `NpgsqlDataSource.CreateCommand(...).ExecuteNonQueryAsync(token)`, or `OpenAsync` chained before the command), and that the connection goes back to the pool when that task ends.

## Small items

- **R1 — The ordering claim is wrong.** Decision 6 says notifications from one sender arrive in send order "(PostgreSQL delivers a session's notifications in commit order)". With up to 8 publishes in flight on different pooled connections, those are different sessions, so no order is guaranteed even from one sender. Harmless for +9 announcements. Reword to "no order is guaranteed".
- **R2 — The CTS is never disposed.** `ITickCompletionQueue` never disposes the `CancellationTokenSource` it is given (caller-owned). State that the publisher's callback disposes it.
- **R3 — The "allocates nothing" wording is inaccurate.** Decision 3 step 3 says the handler "allocates nothing beyond the bounded inbox entry". Npgsql allocates the payload string and the event arguments for every notification. This happens off the tick thread at a few per hour, so it is harmless. Reword.
- **R4 — Clients that are not yet session-ready receive the broadcast.** "Connected client means a client present in the zone's outbox". `InboundTransportAdapter` (line 218) adds a client on transport connect, before `SessionReady`, so a client still loading receives the +9. State whether that is accepted (the client discards it before its gate opens) or filtered. The same applies to any zone-wide message.

## Not done

- `unity-specialist` second opinion not spawned: ADR-015 was validated by it during authoring (4 blocking findings, all applied), and the ADR-014 reviews set the same precedent. A1 is the only engine-specific finding here.
- Traceability matrix not rebuilt; no new TR-IDs. TR-enh-010 is the requirement this ADR serves.

## Next

Fresh authoring session: amend ADR-015 for A1, A2 and R1–R4. If the amendment stays within those items, the user can mark it Accepted without another review. After acceptance, follow Migration Plan step 4 (ADR-014 and ADR-009 notes, the channel-contract note), then run `/create-stories` in Networking Core for Migration Plan steps 1 and 2.
