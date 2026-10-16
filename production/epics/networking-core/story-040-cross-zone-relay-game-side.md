# Story 040: Cross-Zone Relay — the Game Side, on an In-Memory Bus

> **Epic**: Networking Core
> **Status**: Complete — 2026-10-16 (`/story-done`, COMPLETE WITH NOTES; see Completion Notes). Was Ready: written 2026-10-16 (`/create-stories`, ADR-015 Migration Plan step 1). `/story-readiness` 2026-10-16: NEEDS WORK (2 gaps), both fixed the same day at the user's request — `EnqueueToAllClients` under the outbox's deferred-removal guard; the in-memory bus placed in `tests/`. Implemented 2026-10-16 (`/dev-story`, EditMode 3043 / 3043); `/code-review` 2026-10-16 CHANGES REQUIRED (no blocking finding) — all fixes and suggestions applied at the user's request ("fix all"; minimum body length left to the message codec, see Implementation Notes).
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-16
> **Estimate**: 12 hours (provisional — confirm at `/story-readiness`)

*Added 2026-10-16 after ADR-015 was Accepted. `ServerBroadcast_Enhancement9` is coded `S→ALL (all connected clients, every zone)`, and one zone runs per process (ADR-002), so `ClientMessageOutbox` alone reaches only one zone. This story builds everything of the relay that needs no database: the broadcaster, the zone-wide send on the outbox, the envelope, the allowlist, the inbox, the receiving check, tick step 1a, and an in-memory bus that connects several test zones in one process. Story 041 adds the PostgreSQL transport behind the same interfaces. Enhancement Story 016 depends on this story only.*

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-14 / UI-ENH-3 (`ServerBroadcast_Enhancement9` to all online players), CR-ENH-15 step 8 (only after the commit), EC-ENH-8 (a delivery failure does not change the outcome). `design/gdd/networking-channel-contract.md` — the `ServerBroadcast_Enhancement9` row (`S→ALL`, R-OD, P1; note naming ADR-015). `design/gdd/networking-wire-protocol.md` — AC-NC-47 (the broadcast is not cap-exempt; body 4–52 bytes). No rule or value of a GDD changes.
**Requirement**: `TR-enh-010` (the delivery path the broadcast needs; the call from the Enhancement System is Enhancement Story 016)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: **ADR-015: Cross-Zone Message Relay** — Decisions 1, 4, 5, 6 and 7; the Key Interfaces; Migration Plan step 1; Validation Criteria 1–3.
Also: **ADR-014** Decision 6 (the tick order, amended by ADR-015 Decision 7); **ADR-011** (no `await` on the tick); **ADR-010** (no central bus; a callback off the tick only records); **ADR-012** (all relay types in `IronGrind.ServerLogic`); **ADR-013** (`originId` does not come from `IRandomProvider`).

**ADR Decision Summary**: game code calls `IServerWideBroadcaster.Broadcast` on the tick; it enqueues the body for this zone's clients through the outbox (not cap-exempt) and then publishes one relay envelope; every other zone checks the envelope, queues the body in a bounded inbox, and tick step 1a hands it to its own outbox. Best-effort, at most once, allowlisted types only.

**Engine**: Unity 6.3 LTS | **Risk**: LOW — plain C#, no engine API, no database; delivery reuses the verified outbox of Story 039.
**Engine Notes**: `System.Security.Cryptography.RandomNumberGenerator` is already used by ADR-013's seed (entropy in the IL2CPP Linux build is ADR-013 verification item 3, not this story's). Thread-safety of the inbox is plain .NET (`lock`); no Unity threading API.

**Control Manifest Rules (Foundation, v2026-10-16)**:
- Required: `Broadcast` enqueues through `IZoneClientBroadcastSink.EnqueueToAllClients(..., isCapExempt: false)` first, then publishes; a publish failure is logged and changes nothing; `Broadcast` never throws and refuses a type not on the allowlist — ADR-015
- Required: the recipient set is every client present in the zone's outbox; it is never derived from `MessageDirection` alone — ADR-015
- Required: `ZoneTickPipeline` runs Completion, Relay (1a), Requests, Simulation, Outbound, each step in its own `try`/`catch`; step 1a is `STEP_RELAY` — ADR-015, ADR-014
- Required: the receiving check drops a payload that is too long, malformed, of an unknown version, not on the allowlist, over the type's `MaxBodyBytes`, or from this process's own `originId`; otherwise it copies the body into the inbox and does nothing else — ADR-015
- Required: tests use `InMemoryCrossZoneRelayBus`; all relay types are `IronGrind.ServerLogic` — ADR-015, ADR-012
- Forbidden: relaying a request, a reply to one client, system state or a call between systems; an allowlist row without a story naming its GDD rule — ADR-015, ADR-010
- Forbidden: drawing `originId` from `IRandomProvider` — ADR-015, ADR-013
- Forbidden: calling game code from the receiving side — ADR-015, ADR-010
- Guardrail: step 1a costs one empty-queue check per tick; inbox memory 64 × ≤ 52 B — ADR-015

---

## Acceptance Criteria

*"Zone" in a test means one set of relay objects (outbox with a recording send seam, broadcaster, inbox, pipeline) connected to the others through one `InMemoryCrossZoneRelayBus`. "Delivered" means the message was enqueued on that zone's outbox and sent by its flush.*

**Two zones (ADR-015 Validation 1)**
- [ ] **Local first**: `Broadcast` in zone A enqueues the body once for each client of A's outbox, with `isCapExempt: false`, in the same tick; A's flush of that tick sends it to each A client exactly once.
- [ ] **Other zones**: the same broadcast reaches each client of zones B and C exactly once, sent by the flush of the first tick of B (and of C) after the bus delivered it — step 1a enqueues it, step 4 of the same tick sends it.
- [ ] **Own echo**: zone A receives its own envelope from the bus and drops it by `originId`; no A client receives the message twice.
- [ ] **Body intact**: the body each client receives is byte for byte the body passed to `Broadcast` (4-byte and 52-byte bodies).

**Refusals and drops (ADR-015 Validation 2)**
- [ ] **Allowlist on send**: `Broadcast` with a type not on `CrossZoneRelayAllowlist` returns false, logs one error, enqueues nothing locally and publishes nothing.
- [ ] **Allowlist and size on receive**: an envelope with a type not on the allowlist, or a body longer than the type's `MaxBodyBytes`, is dropped and logged; nothing reaches the inbox.
- [ ] **Malformed**: a payload with an unknown version prefix (`v2:`), a missing field, a non-decimal type, an `originId` that is not 16 hex digits, invalid base64, or a length above the longest valid envelope is dropped and logged, and never throws.
- [ ] **Body size on send** *(added 2026-10-16, code review)*: `Broadcast` with a body longer than the type's `MaxBodyBytes` returns false, logs one error, enqueues nothing locally and publishes nothing — the same refusal every receiver would apply.
- [ ] **Refusal logging is throttled** *(added 2026-10-16, code review)*: the receiver logs the first refusal of each reason in full, then one summary line per 100 refusals of that reason; every refusal is still counted. 250 refusals of one reason give exactly 3 lines.
- [ ] **Inbox full**: with `RELAY_INBOX_CAPACITY` entries waiting, `TryAdd` returns false and the entry is dropped and counted; the drops are logged once per tick (one line with the count), not once per entry. The entries already in the inbox are delivered by the next step 1a.

**Failure isolation (ADR-015 Validation 3)**
- [ ] **Failing publisher**: a publisher that throws leaves A's local delivery and `Broadcast`'s return value unchanged; one warning is logged; `Broadcast` does not throw.
- [ ] **Failing sink in step 1a**: a relay handler that throws for one entry does not stop the rest of step 1a (the remaining entries are still delivered) nor the later steps of the tick; the failure is logged.

**Tick order (ADR-015 Decision 7)**
- [ ] **Order**: the pipeline test shows Completion → Relay → Requests (Pass A, Pass B) → Simulation → Outbound.
- [ ] **Isolation**: a relay step that throws logs `StepFailed: step=Relay` and the Requests, Simulation and Outbound steps still run.
- [ ] **Drain bound**: step 1a delivers the entries present when it starts; an entry added during the drain (from another thread) waits for the next tick.
- [ ] **Existing tests**: the tests of `TickLoop_ZoneTickPipeline_tests.cs` pass unedited (they use the 5-argument constructor, which forwards an empty relay inbox).

**Pieces**
- [ ] **Zone-wide send**: `ClientMessageOutbox` implements `IZoneClientBroadcastSink`; `EnqueueToAllClients` calls `Enqueue` once per client added and not removed, with the given `isCapExempt`; with no client it does nothing; it never throws. The existing `ClientMessageOutbox_tests.cs` tests pass unedited.
- [ ] **Removal during the zone-wide send**: with three clients, where the second client's queue is full and its disconnect requester calls `RemoveClient` from inside `Enqueue`, `EnqueueToAllClients` still enqueues for the first and third clients, throws nothing, and the second client is removed when the call returns (absent from the next flush, `Enqueue` for it then logs the unknown-client warning).
- [ ] **Not cap-exempt (AC-NC-47 on both paths)**: client A's queue holds 8 non-exempt messages; a local broadcast enqueued before A's flush is sent at the next flush, not this one; the same holds for a relayed one delivered by step 1a.
- [ ] **Envelope codec**: `RelayEnvelopeCodec` encodes `v1:{messageTypeId}:{originId}:{base64(body)}` (type in decimal, `originId` 16 lowercase hex digits) and decodes it back for type ids 0, `0xE103` (`ServerBroadcast_Enhancement9`) and 65535 and bodies of 0, 4 and 52 bytes; a 52-byte body gives a payload under 100 characters.
- [ ] **Origin**: `originId` is drawn once per process from `RandomNumberGenerator`, not from `IRandomProvider`; two `CrossZoneRelayOrigin` instances (two test zones) get different ids; a source search finds no `IRandomProvider` in the relay folder.
- [ ] **Allowlist**: built by the caller, sealed after construction; holds one row in production, `ServerBroadcast_Enhancement9.MessageTypeId → 52`.
- [ ] **No allocation on the tick path**: `Broadcast`'s local half, `TryAdd`, an empty step 1a, and a step 1a draining 8 entries allocate nothing (`Is.Not.AllocatingGCMemory()`, as Story 039). The publish of the in-memory bus and the codec's `Encode` may allocate (a string per relayed message, a few per hour) — stated in their remarks.
- [ ] **Threading**: a test adds entries to one inbox from a second thread while the test thread drains it, 10,000 entries; every entry is delivered exactly once or counted as dropped, none twice.
- [ ] **Assembly**: every new type is in `src/ServerLogic/Networking/CrossZoneRelay/` (namespace of the folder), `IronGrind.ServerLogic`; the boundary test passes with no allow-list change.

---

## Implementation Notes

*Derived from ADR-015; read the ADR for the reasons.*

- **Interfaces, as ADR-015 gives them**: `IServerWideBroadcaster.Broadcast(ushort, ReadOnlySpan<byte>) : bool`; `IZoneClientBroadcastSink.EnqueueToAllClients(ushort, ReadOnlySpan<byte>, bool)`; `ICrossZoneRelayPublisher.Publish(ushort, ReadOnlySpan<byte>)`; `ICrossZoneRelayInbox.TryAdd(ushort, ReadOnlySpan<byte>) : bool` and `Drain(uint currentTick, ICrossZoneRelayHandler) : int`; `ICrossZoneRelayHandler.OnRelayed(ushort, ReadOnlySpan<byte>)`. Do not change their signatures — Story 041 implements the publisher and calls the inbox through them.
- **The receiving check is one class**, shared by the in-memory bus (this story) and the Npgsql listener's `Notification` handler (Story 041): given a payload string, it decodes with `RelayEnvelopeCodec`, applies the length, version, allowlist, `MaxBodyBytes` and own-`originId` checks, and calls `inbox.TryAdd`. Working name `CrossZoneRelayReceiver.OnPayload(string)`. It runs off the tick thread in production: it reads only the immutable allowlist and its own origin id and writes only the inbox. Decode into a buffer the receiver owns; it is called from one thread at a time (the listener thread; in tests, the bus), and its remarks say so.
- **The bus goes through the codec.** `InMemoryCrossZoneRelayBus` hands out one `ICrossZoneRelayPublisher` per registered zone; `Publish` encodes the envelope with that zone's origin and passes the payload string to every registered zone's receiver, the publisher's own included (so the echo drop is exercised). It is a test double and lives in `tests/EditMode/Networking/CrossZoneRelayTestDoubles.cs`: the one test assembly `IronGrind.Foundation.EditModeTests` holds both this story's tests and Enhancement Story 016's integration tests, so nothing in `src/` needs it.
- **Zone-wide send and re-entry.** `EnqueueToAllClients` walks `_clients` under the same deferred-removal guard `Flush` uses (`_flushing` / `_listDirty`): `Enqueue` on a full queue requests a disconnect, which re-enters `RemoveClient`; inside the walk that call only marks the client removed, and the list is compacted when the walk ends. A plain `for` loop with `RemoveAt` inside it would skip the next client or read past the end. If the guard is renamed to cover both walks, keep the Story 039 tests unedited.
- **Inbox.** A ring of `RELAY_INBOX_CAPACITY` slots, each a byte array of the largest `MaxBodyBytes` on the allowlist (52), plus the type id and the length, allocated at construction; one `lock` around `TryAdd` and around taking the count at the start of `Drain`. `Drain` copies each entry out under the lock, or holds the lock only to read the start and end indices and relies on the single reader — either is fine; the threading test decides. The drop counter is read and reset by `Drain`, which logs one line when it is non-zero.
- **Broadcaster.** `ServerWideBroadcaster(IZoneClientBroadcastSink, ICrossZoneRelayPublisher, CrossZoneRelayAllowlist)`. Check the allowlist first (return false, log, nothing sent); then `EnqueueToAllClients(type, body, isCapExempt: false)`; then `Publish` inside a `try`/`catch` that logs a warning. The local half must not depend on the publisher in any way.
- **Pipeline.** Add a 7-argument constructor `(completionQueue, relayInbox, relayHandler, dispatcher, simulation, outbound, tickSource)` and keep the existing 5-argument one, forwarding an empty inbox and a no-op handler (the precedent of Enhancement Story 015 decision D2). Step 1a runs right after Completion in its own `try`/`catch` with `STEP_RELAY = "Relay"`. Update the class and `Tick` doc comments from four steps to five. The relay handler in production is a small adapter that calls `EnqueueToAllClients(type, body, isCapExempt: false)`.
- **Origin.** `CrossZoneRelayOrigin` draws 8 bytes from `RandomNumberGenerator` once at construction and exposes the `ulong` and its 16-hex-digit form; the composition root creates one per process.
- **Tuning knobs** (server config, not hardcoded): `RELAY_INBOX_CAPACITY` (64) is the only one this story uses; take it as a constructor argument with that default. `RELAY_CHANNEL_NAME` and the others are Story 041's.
- **Clients before `SessionReady`.** `EnqueueToAllClients` reaches every client in the outbox, which includes clients still loading (ADR-015 Decision 1, accepted 2026-10-16). Nothing to do here; say so in the remarks of `EnqueueToAllClients`.
- **Minimum body length is the message codec's job** *(user decision 2026-10-16, code review)*. The allowlist holds a maximum only. `ServerBroadcast_Enhancement9`'s wire minimum (4 bytes, AC-NC-47) is enforced by its own codec, which is the only producer of the body; the relay does not check it, and a 0- to 3-byte body would be relayed as is.
- **Not done here:** no `Npgsql`, no thread of its own, no composition root. The story provides the objects and tests them by constructing them.

**Performance**: per broadcast, one copy per local client (the outbox), one envelope encode (a string, allocates) per publish; per relayed message, one decode into the receiver's buffer and one copy into the inbox off the tick, one copy per client at step 1a. Empty step 1a: one locked count read.

---

## Out of Scope

- Story 041: `NpgsqlRelayPublisher`, `NpgsqlRelayListener`, `Stop()`, the headless-build and Editor verification
- Enhancement Story 016: calling `Broadcast` from the commit-success delivery, the names, AC-ENH-18 end to end, the observer signatures
- The server composition root and its wiring of these objects (no composition root exists yet)
- A second allowlist row (cross-zone world chat), by a later story naming its GDD rule
- The client handler for `ServerBroadcast_Enhancement9` (it must tolerate arrival before `SessionReady` — Presentation rule, its own story)
- Any change to `IClientMessageOutbox`'s signature or to `PriorityPathQueue`

---

## QA Test Cases

**Files** — to be created: `src/ServerLogic/Networking/CrossZoneRelay/` (`IServerWideBroadcaster.cs`, `IZoneClientBroadcastSink.cs`, `ICrossZoneRelayPublisher.cs`, `ICrossZoneRelayInbox.cs`, `ICrossZoneRelayHandler.cs`, `ServerWideBroadcaster.cs`, `CrossZoneRelayInbox.cs`, `CrossZoneRelayAllowlist.cs`, `RelayEnvelopeCodec.cs`, `CrossZoneRelayReceiver.cs`, `CrossZoneRelayOrigin.cs`, the production relay handler); `tests/EditMode/Networking/CrossZoneRelayTestDoubles.cs` (`InMemoryCrossZoneRelayBus`); `tests/EditMode/Networking/CrossZoneRelay_tests.cs`. To be edited: `src/ServerLogic/Networking/Outbound/ClientMessageOutbox.cs` (implements the sink), `src/ServerLogic/Networking/TickLoop/ZoneTickPipeline.cs` (step 1a, the 7-argument constructor, doc comments); new tests only in `TickLoop_ZoneTickPipeline_tests.cs` and `ClientMessageOutbox_tests.cs`.

- **Two zones, three clients** — A has 2 clients, B has 1: after `Broadcast` in A, A's flush that tick sends 2 messages; B's next tick sends 1; nothing else is sent anywhere over 3 more ticks.
- **Three zones** — A, B, C with one client each → each client receives exactly one.
- **Echo** — A's receiver gets A's payload → dropped, counted as own-origin; A's client receives one message.
- **Body** — 4-byte and 52-byte bodies arrive byte for byte.
- **Allowlist send** — type `0xE104` (not listed) → false, one error, nothing enqueued, `Publish` not called.
- **Allowlist receive** — a hand-built `v1` payload with an unlisted type → dropped; with a 53-byte body for the listed type → dropped.
- **Malformed** — `v2:…`, `v1:57362:abc`, `v1:x:…`, a 15-digit origin, `@@@` as base64, an empty string, a 200-character payload → each dropped and logged, no exception.
- **Inbox full** — capacity 4 in the test: 6 `TryAdd` → 4 true, 2 false; next drain delivers 4 and logs one line with count 2; a further drain logs nothing.
- **Failing publisher** — publisher throws → local clients still get the message, `Broadcast` returns true, one warning, no exception.
- **Failing handler** — handler throws on the second of three entries → the first and third are delivered, one error, Requests and Outbound steps still run.
- **Order** — recording steps: Completion, Relay, Requests, Simulation, Outbound.
- **Relay step throws** — `StepFailed: step=Relay`; later steps run.
- **Drain bound** — an entry added from inside the handler (simulating the listener) is delivered on the next tick, not this one.
- **5-argument constructor** — existing tests unchanged; a pipeline built with it runs an empty relay step.
- **Sink** — 0 clients → nothing; 3 clients, one removed → 2 enqueues with the flag passed through.
- **Removal during the zone-wide send** — 3 clients, the second with 128 messages queued and a disconnect requester that calls `RemoveClient` → clients 1 and 3 each have the broadcast queued, no exception, client 2 absent from the next flush.
- **AC-NC-47** — 8 non-exempt queued for A's client; a local broadcast → that flush sends the 8, the broadcast goes next tick; the same with the broadcast arriving through step 1a.
- **Codec round trip** — types 0, `0xE103`, 65535 × bodies 0, 4, 52 bytes; 52-byte payload length < 100.
- **Origin** — two instances differ; 16 lowercase hex digits; source search for `IRandomProvider` in the folder finds nothing.
- **No allocation** — `Broadcast` with a no-op publisher, `TryAdd`, empty drain, drain of 8 → zero bytes, 1,000 iterations.
- **Threading** — 10,000 entries from a second thread, capacity 64, the test thread draining → delivered + dropped = 10,000, no duplicate (each body carries its index).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/CrossZoneRelay_tests.cs` — must exist and pass, with the full EditMode suite (Unity batch mode or the Editor's Test Runner).

**Status**: [x] `tests/EditMode/Networking/CrossZoneRelay_tests.cs` (60 test methods, 79 cases), EditMode 3053 / 3053 (2026-10-16 18:37Z, Unity batch mode)

---

## Dependencies

- Depends on: ADR-015 (Accepted 2026-10-16); Story 039 (`ClientMessageOutbox`), Story 037 (`ZoneTickPipeline`) — Complete. No database.
- Unlocks: Enhancement Story 016 (`ServerBroadcast_Enhancement9` delivery); Story 041 (the Npgsql transport implements `ICrossZoneRelayPublisher` and feeds `CrossZoneRelayReceiver`)

---

## Completion Notes
**Completed**: 2026-10-16
**Criteria**: 25/25 passing (23 written at readiness plus the two added by the code review: body size on send, throttled refusal logging). None deferred.
**Files**: `src/ServerLogic/Networking/CrossZoneRelay/` — `IServerWideBroadcaster.cs`, `IZoneClientBroadcastSink.cs`, `ICrossZoneRelayPublisher.cs`, `ICrossZoneRelayInbox.cs`, `ICrossZoneRelayHandler.cs`, `ServerWideBroadcaster.cs`, `CrossZoneRelayInbox.cs`, `CrossZoneRelayAllowlist.cs`, `RelayEnvelopeCodec.cs`, `CrossZoneRelayReceiver.cs`, `CrossZoneRelayOrigin.cs`, `ZoneClientRelayHandler.cs`; `src/ServerLogic/Networking/Outbound/ClientMessageOutbox.cs` (`IZoneClientBroadcastSink`, `EnqueueToAllClients` under the `_flushing` / `_listDirty` guard); `src/ServerLogic/Networking/TickLoop/ZoneTickPipeline.cs` (7-argument constructor, step 1a `STEP_RELAY`); `tests/EditMode/Networking/CrossZoneRelay_tests.cs`, `CrossZoneRelayTestDoubles.cs`.
**Deviations** (advisory, accepted by the user 2026-10-16):
- *No allocation, step 1a draining 8*: asserted on `CrossZoneRelayInbox.Drain` directly, not through `ZoneTickPipeline` with a real outbox; the send half is covered by the local-broadcast allocation test.
- *Threading test*: sets `LogAssert.ignoreFailingMessages` for its duration — a 64-slot inbox under 10,000 writes logs a nondeterministic number of `EntriesDropped` warnings.
- *API beyond the story*: `CrossZoneRelayOrigin(ulong)` (tests), `CrossZoneRelayAllowlist.CreateProduction()`, `CrossZoneRelayAllowlistRow`, `RelayEnvelopeDecodeResult` (with `BodyTooLong`), public `OwnOriginDropCount` / `RefusedPayloadCount`, `CrossZoneRelayReceiver.REFUSAL_LOG_EVERY = 100`; `CrossZoneRelayInbox` requires `maxBodyBytes` (the composition root passes `allowlist.LargestMaxBodyBytes`).
- *Receiver decode buffer*: 54 bytes (base64 padding of the 52-byte maximum); a 53- or 54-byte body is refused after decoding as `BodyTooLong`, a larger one by the codec before decoding.
- *Tests reuse doubles* of the same test assembly (`OutboxRecordingSendSeam`, `OutboxRecordingDisconnectRequester`, `OutboxCountingSendSeam`, `InboundSettableTickSource`).
- *Not wired*: no composition root exists; these objects are constructed only by tests until the composition-root story.
**Test Evidence**: Integration — `tests/EditMode/Networking/CrossZoneRelay_tests.cs`.
**Code Review**: Complete — CHANGES REQUIRED (no blocking; unity-specialist 1 should-fix + 4 suggestions, qa-tester GAPS), all applied ("fix all"); not re-run after the fixes. LP-CODE-REVIEW and QL-TEST-COVERAGE skipped — lean mode.
**Tech debt logged**: none (user's choice).
