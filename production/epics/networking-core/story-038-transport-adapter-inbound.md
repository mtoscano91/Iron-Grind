# Story 038: Transport Adapter — Inbound Messages and Connections

> **Epic**: Networking Core
> **Status**: Ready — written 2026-10-09; `/story-readiness` 2026-10-09 (NEEDS WORK, closed the same day by the user decisions recorded below). `com.unity.netcode.gameobjects` 2.13.3 resolves with `com.unity.transport` 2.6.0 and the project compiles with them (EditMode 2383 / 2383, 2026-10-09).
> **Layer**: Foundation
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 12 hours

*Added 2026-10-09. ADR-014 Migration Plan step 4. Stories 035–037 built the guard chain changes, the dispatcher with its intake, and the tick pipeline; all of it is plain C# and has never received a byte from a network. This story writes the one piece of code that touches the transport's receive API, and carries ADR-014's six engine verification items. The outbound half is Story 039.*

> **User decisions 2026-10-09.** (1) ADR-004 OQ-ADR4-3 is resolved: project messages are NGO **unnamed custom messages** (ADR-004 Amendment 1). (2) The adapter work is two stories: this one (inbound and connections) and Story 039 (outbound). (3) The NGO-facing code lives **inside `IronGrind.ServerLogic`**, as ADR-012 Decision 1 already allows ("`ServerLogic` references it only if it declares NGO types"); no fourth assembly.

> **User decisions 2026-10-09 (`/story-readiness`).** (4) The verification run is a **PlayMode test on loopback**: a server `NetworkManager` and one client `NetworkManager` in one process, `UnityTransport` on 127.0.0.1. (5) The tick is driven for that run by a **test-only driver**; the production tick driver stays with the server composition root story, and VR-1 is a statement about the test driver only. (6) A verification item that comes back **FAIL is recorded and escalated** to the user with its consequence and does not by itself block `/story-done`; an item left **NOT RUN does block it**. (7) `AssemblyBoundary_tests.cs` is the one existing test this story edits. (8) When `AddConnection` returns false the adapter disconnects that client. (9) The test creates and starts the `NetworkManager`s and passes the server's to the shell; the run uses no player prefab, connection approval off and scene management off, and records what was actually needed. (10) Estimate 12 hours.

## Context

**GDD**: `design/gdd/networking-wire-protocol.md` — CR-NET-7.1 (client → server envelope, 10 bytes), CR-NET-7.10 (any packet resets the heartbeat timeout). `design/gdd/networking-core.md` — CR-NET-2 (game logic runs on the tick). No rule or value of a GDD changes.
**Requirement**: none in the epic's TR table — this story adds a component decided by ADR-014 and ADR-004
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty)*

**ADR Governing Implementation**:
- **ADR-014: Inbound Request Dispatch and Tick Order (Accepted 2026-10-09)** — Decision 2 (the adapter and the intake), Decision 4 (`AddConnection` / `RemoveConnection`; a disconnect raised inside the tick), Verification Required 1–6 and the dated note of 2026-10-09 on their status.
- **ADR-004: Networking Library (NGO)** — Decision 4 (project envelope as the message body) and Amendment 1 (unnamed messages).
- **ADR-012: Server/Client Assembly Boundary** — Decision 1 (`ServerLogic` may reference `Unity.Netcode.Runtime`), Decision 4 (server-side handlers are registered by the server composition root; a `MonoBehaviour` of `ServerLogic` appears in no asset a client build includes).
- **ADR-010** — a network callback enqueues, the tick executes.

**ADR Decision Summary**: the adapter is the only code that touches the transport's receive API. In its callback it checks that the message is 10 to `MAX_INBOUND_MESSAGE_BYTES` (400) bytes long and calls `IInboundMessageIntake.TryAccept(clientId, message)`. It decodes nothing and calls no game system, no guard and no handler.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH — Netcode for GameObjects 2.13 is past the LLM training cutoff.
**Engine Notes**: read `docs/engine-reference/unity/modules/networking.md`, section "NGO 2.13 — Custom Messaging, Transport and Frame Timing", before writing any NGO call. It was researched from source and manual pages on 2026-10-09 and **not run**; every signature in it is confirmed at first compile. Four facts shape this story: the `FastBufferReader` given to a handler is disposed when the handler returns and has no `Span` overload; handlers run in `EarlyUpdate` and can also run inside a transport disconnect event; `DisconnectClient(clientId)` is deferred to `PostLateUpdate`, and messages of that client already queued can still reach the handler that frame; NGO catches and logs an exception thrown by a handler.

**Control Manifest Rules (Foundation layer — "Inbound request dispatch and tick order (ADR-014)")**:
- Required: the transport adapter is the only code that touches the transport's receive API; in its callback it checks the length (10 to `MAX_INBOUND_MESSAGE_BYTES`) and calls `IInboundMessageIntake.TryAccept(clientId, message)`; it decodes nothing and calls no game system, no guard and no handler — ADR-014
- Required: not reported as activity — a message the adapter dropped for its length — ADR-014
- Required: a network callback only enqueues; the tick executes — ADR-010
- Required: a type in `IronGrind.Foundation` carries no server-only type in its signature — ADR-012
- Forbidden: a `MonoBehaviour` of `IronGrind.ServerLogic` in a scene, prefab or asset a client build includes — ADR-012
- Guardrail: the transport adapter's engine behaviour is verification-pending (items 1–6) — ADR-014

---

## Acceptance Criteria

*"The core" is the plain C# class with no NGO type in its signature; "the NGO shell" is the class that subscribes to NGO. "Dropped" means `TryAccept` is not called and nothing is thrown.*

**Core (EditMode, no network session)**

- [ ] **Length check**: a message of 9 bytes and one of `MAX_INBOUND_MESSAGE_BYTES + 1` bytes are dropped; messages of 10 and of `MAX_INBOUND_MESSAGE_BYTES` bytes reach `TryAccept` once each, with the same bytes and length.
- [ ] **Connection id**: a transport id above `uint.MaxValue` is dropped with one server error log; ids 1 and `uint.MaxValue` are passed on unchanged as `uint`.
- [ ] **Nothing else**: for an accepted message the core makes exactly one call, `TryAccept`; it reads no field of the message and calls no guard, no handler and no `IConnectionActivitySink` itself. A source search of the core and the shell finds no reference to `CrossCuttingRpcGuardChain`, `InboundRequestHandler` or `MessageEnvelopeCodec`.
- [ ] **Never throws**: for any length from 0 to `MAX_INBOUND_MESSAGE_BYTES + 1` and for an intake that returns false, the core returns without throwing.
- [ ] **No allocation**: the core's accept path allocates nothing per message — asserted over 1,000 messages with `GC.GetAllocatedBytesForCurrentThread`, as `TickLoop_CompletionQueue_tests.cs` does.
- [ ] **Connections**: a connect event calls `IInboundRequestDispatcher.AddConnection` once with the `uint` id; a disconnect event calls `RemoveConnection` once; a connect event whose id is above `uint.MaxValue` adds nothing, logs one error and asks the transport to disconnect that id.
- [ ] **Dispatcher refuses the connection**: when `AddConnection` returns false (no free slot, or the id is already known), the adapter logs one server error and asks the transport to disconnect that id; a later message of that id is dropped by the intake. No connection exists that the dispatcher does not know.
- [ ] **Server-initiated disconnect**: `Disconnect(clientId)` on the adapter calls `RemoveConnection` before it asks the transport to disconnect, so a message of that client that the transport still delivers in the same frame is dropped by the intake (ADR-014 Decision 2 step 1). The later transport disconnect event for the same id does not call `RemoveConnection` a second time.

**NGO shell (compiles in `IronGrind.ServerLogic`)**

- [ ] **Subscription**: the shell subscribes to `CustomMessagingManager.OnUnnamedMessage`, `OnClientConnectedCallback` and `OnClientDisconnectCallback` when it is started and unsubscribes when it is stopped; a source search finds `OnUnnamedMessage` and `FastBufferReader` in the shell's file only, in all of `src/`.
- [ ] **Read without allocating**: the handler computes the remaining length from the reader, drops a message outside 10 to `MAX_INBOUND_MESSAGE_BYTES` **before reading it**, copies the bytes into one array allocated at construction, and passes a span of that array to the core. It keeps no reference to the reader.
- [ ] **Assembly**: `IronGrind.ServerLogic.asmdef` references `IronGrind.Foundation` and `Unity.Netcode.Runtime` and nothing else; `IronGrind.Foundation` and `IronGrind.Client` are unchanged. `test_reference_lists_match_adr012` in `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` asserts that list today with `Foundation` alone, so it is edited to the new list (ADR-012 Decision 1: "`ServerLogic` references it only if it declares NGO types") — the one existing test this story edits. The EditMode suite passes.

**Verification in a real run (evidence document)**

*The run is the PlayMode test of the Implementation Notes. Each item is recorded in `production/qa/evidence/networking-core-038-ngo-verification.md` with what was run, what was observed, and PASS / FAIL / NOT RUN. A FAIL is recorded and brought to the user with its consequence; it does not by itself block `/story-done` (user decision 2026-10-09 — ADR-014 says the dispatcher is written so that none of the six, if false, corrupts state). An item left NOT RUN blocks `/story-done`. Where an item can be asserted, the PlayMode test asserts it; where it can only be observed (the profiler in VR-2), the observation is written by hand.*

- [ ] **VR-1 Frame position**: with the tick driven by the test-only driver, the handler is never entered while `ServerTickLoop.AdvanceTick` is running, over a run that includes a client disconnecting and a server-initiated disconnect.
- [ ] **VR-2 Buffer and allocation**: the profiler shows no managed allocation in the shell's handler and the core per message; NGO's own allocations are recorded as observed, not counted against the criterion.
- [ ] **VR-3 Ids**: the ids of three successive connections are recorded; whether an id is reused after a disconnect in one server run, and after a `NetworkManager` shutdown and restart in one process, is recorded.
- [ ] **VR-4 Version and API**: the resolved NGO and Unity Transport versions are recorded from `Packages/packages-lock.json`; the signatures the shell uses compile as written in the engine reference, or the reference is corrected.
- [ ] **VR-5 Reliable ordered**: 200 numbered `ReliableSequenced` messages from one client arrive in order, each once.
- [ ] **VR-6 After a disconnect**: whether a message of a client reaches the handler after `DisconnectClient(id)` in the same frame is recorded, and with the adapter's `Disconnect` no such message reaches `TryAccept`'s accept path.
- [ ] **Reference updated**: every statement of the engine reference section that the run confirms or contradicts is marked so, with the date.

---

## Implementation Notes

- **Two classes.** A plain C# core, `InboundTransportAdapter` (proposed name), takes an `IInboundMessageIntake`, an `IInboundRequestDispatcher` and a small transport seam, and holds the logic: the length check, the `ulong` → `uint` check, the connection calls, `Disconnect`. A thin NGO shell holds only what cannot be tested without NGO: the three subscriptions, the read from the `FastBufferReader`, and the transport seam's implementation (`DisconnectClient`). All behaviour criteria are tested on the core with fakes; the shell is checked by compilation, by source search and by the run.
- **Reading the message.** `FastBufferReader` has no `Span` overload and `IronGrind.ServerLogic` has `allowUnsafeCode: false`. Use `ReadBytesSafe(ref byte[] value, int size, int offset = 0)` into a `byte[MAX_INBOUND_MESSAGE_BYTES]` allocated once, after checking `reader.Length - reader.Position`. Do not use `ToArray()` (it allocates).
- **The length check is on the project message**, the bytes NGO hands to the handler — not on NGO's own framing.
- **Server-initiated disconnect.** NGO defers `DisconnectClient(clientId)` to the end of the frame and can still deliver that client's queued messages; `DisconnectClient(clientId, null)` raises the disconnect callback inside the call. The adapter's `Disconnect` removes the connection from the dispatcher first, so neither behaviour lets a message through. The dispatcher already tolerates `RemoveConnection` inside the tick (ADR-014 Decision 4).
- **A handler inside a disconnect event.** NGO flushes its incoming queue before it raises a transport disconnect event, so the handler can run at that point too. The core does not assume `EarlyUpdate`; it only requires the main thread, outside `AdvanceTick` (VR-1).
- **Where the shell lives.** Server-only, created in code by the server composition root (ADR-012 Decision 4). If it is a `MonoBehaviour` at all, it is on no prefab and in no scene a client build includes; a plain class given the `NetworkManager` is preferred.
- **`Foundation` is not touched.** No NGO type goes into a shared signature.
- **Activity.** The adapter never calls `IConnectionActivitySink`; the intake does, for a message of a known connection. A message dropped for its length is therefore not activity (ADR-014 Decision 2).

**The verification run (user decisions 2026-10-09)**

- **PlayMode assembly.** `tests/PlayMode/` holds only a README, which plans `tests/PlayMode/PlayModeTests.asmdef`; this story creates it (references: `IronGrind.Foundation`, `IronGrind.ServerLogic`, `Unity.Netcode.Runtime`, and the Unity Transport assembly if the test names a transport type; `UNITY_INCLUDE_TESTS`; the `TestAssemblies` reference as in the EditMode asmdef). `IronGrind.ServerLogic` compiles under `UNITY_SERVER || UNITY_EDITOR`, so the PlayMode assembly is constrained the same way. If the asmdef needs a setting this note does not foresee, record it in the evidence document.
- **Two `NetworkManager`s in one process.** The test creates a server `NetworkManager` and a client `NetworkManager` on two `GameObject`s, each with its own `UnityTransport` on 127.0.0.1 and a fixed test port, starts the server with `StartServer()` and the client with `StartClient()`, and destroys both in tear-down. A host is not used: on a host a local send runs the handler inside the send call. NGO's `NetcodeIntegrationTest` helpers are not used (undocumented, and they need a `testables` entry). If NGO refuses two managers in one process — `NetworkManager.Singleton` is the known risk — stop and report it; do not work around it with reflection.
- **`NetworkConfig` for the run.** No player prefab, connection approval off, `EnableSceneManagement` off. Whether the server starts and exchanges unnamed messages with that configuration, and whether any of the three had to change, is recorded.
- **The client side of the test** sends unnamed messages with `SendUnnamedMessage(NetworkManager.ServerClientId, writer, delivery)`. It is test code; no client production code is written.
- **The test-only tick driver** is a `MonoBehaviour` in the PlayMode assembly that accumulates `Time.unscaledDeltaTime` in `Update` and calls `ServerTickLoop.AdvanceTick` once per 50 ms. It sets a flag for the duration of the call; the test's recording intake records whether `TryAccept` was entered while the flag was set (VR-1).
- **Who owns the `NetworkManager`.** The test creates and starts it and passes it to the shell; the shell starts and stops nothing. In production that is the server composition root. Story 039's sender takes the same instance.

**Performance**: one handler call, one copy of at most 400 bytes and one `TryAccept` per message; no allocation in project code. NGO's cost per received message (a native allocation and a copy) is outside this story.

---

## Out of Scope

- Story 039: sending, `IClientMessageOutbox`, `NetworkDelivery` selection, the Path 1 queue
- The server composition root, the **production tick driver** (this story's driver is test code; VR-1 holds for it only), starting and configuring the `NetworkManager` in a server build
- The session layer: `IConnectionActivitySink`, `IConnectionCharacterDirectory`, the heartbeat sink, `SessionHandshake`, connection approval and `SessionReady`
- The client side: no client code, no client composition root
- The epic's engine-risk gate in a **headless server build** (ADR-004 Validation Criteria): this story's run is in the Editor; the headless build check stays open on the epic
- Any change to the dispatcher, the intake, the guard chain or the tick pipeline
- A fourth assembly (rejected, user decision 2026-10-09)

---

## QA Test Cases

**Files** (to be created): `tests/EditMode/Networking/InboundTransportAdapter_tests.cs` (core, with a recording intake, a recording dispatcher and a recording transport seam); `tests/PlayMode/PlayModeTests.asmdef` and `tests/PlayMode/Networking/InboundTransportAdapter_ngo_run_tests.cs` (the run); `production/qa/evidence/networking-core-038-ngo-verification.md`. Edited: `tests/EditMode/Architecture/AssemblyBoundary_tests.cs` (one assertion).

- **Length boundaries** — 0, 9, 10, 400, 401 bytes → dropped, dropped, accepted, accepted, dropped; accepted bytes equal the input.
- **Id boundaries** — ids 1, `uint.MaxValue`, `uint.MaxValue + 1` → passed, passed, dropped with one error.
- **Intake refuses** — the intake returns false → no exception, no second call.
- **Connect / disconnect** — one `AddConnection`, one `RemoveConnection`, in that order, with the id.
- **Oversize id on connect** — nothing added, one error, the transport seam asked to disconnect that id.
- **Dispatcher refuses** — `AddConnection` returns false → one error, the transport seam asked to disconnect that id.
- **Server-initiated disconnect** — `Disconnect(7)` → `RemoveConnection(7)` recorded before the transport seam's disconnect; the transport's later disconnect event for 7 adds no second `RemoveConnection`.
- **Message after removal** — with the real dispatcher: connect, `Disconnect`, then a valid message from that id → `TryAccept` returns false and nothing is dispatched on the next tick.
- **No allocation** — 1,000 accepted messages, zero bytes allocated.
- **Source searches** — NGO receive types only in the shell's file; no guard, handler or envelope codec in either class.
- **Run** — the six VR items, each with its observation.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Networking/InboundTransportAdapter_tests.cs` — must exist and pass; `tests/PlayMode/Networking/InboundTransportAdapter_ngo_run_tests.cs` — must exist and have been run; `production/qa/evidence/networking-core-038-ngo-verification.md` — every VR item PASS or FAIL with its observation, none NOT RUN.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 036 (`IInboundMessageIntake`, `IInboundRequestDispatcher`, `InboundDispatchConstants`) and Story 037 (`ZoneTickPipeline`) — Complete. ADR-004 Amendment 1 (2026-10-09). The NGO package resolves (2.13.3, 2026-10-09).
- Unlocks: Story 039 (the same `NetworkManager` and id mapping); the session layer stories; the server composition root; the epic's headless-build engine-risk gate
