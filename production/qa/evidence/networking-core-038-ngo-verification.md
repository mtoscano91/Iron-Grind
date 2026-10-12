# Story 038 - NGO Verification Run (ADR-014 Verification Required 1-6)

> **Story**: `production/epics/networking-core/story-038-transport-adapter-inbound.md`
> **Run file**: `tests/PlayMode/Networking/InboundTransportAdapter_ngo_run_tests.cs` (assembly `IronGrind.PlayModeTests`)
> **Tick driver**: `tests/PlayMode/Networking/TestTickDriver.cs` (test-only; VR-1 is a statement about this driver only)
> **Status**: run on 2026-10-12. No item is NOT RUN. No item is FAIL.

## How it was run

- Unity 6000.3.10f1 in **batch mode** (`-batchmode -runTests -testPlatform PlayMode`), Editor closed, Windows 11. Not a headless server build, and not the Editor's Test Runner window.
- Result: **12 / 12 passed** (`test-run` start 2026-10-12 10:31:56Z, 10.3 s), after the `/code-review` fixes of the same day. EditMode in the same state of the sources: **2420 / 2420** (2026-10-12 10:31:22Z), no `error CS`. The first run of the day (9 / 9, 08:14:19Z; EditMode 2413 / 2413) was before those fixes; every VR observation below was the same in both runs except the frame numbers of VR-6a.
- Packages (`Packages/packages-lock.json`): `com.unity.netcode.gameobjects` 2.13.3, `com.unity.transport` 2.6.0.
- Observations are the `[CONFIG]` and `[VR-n]` lines the tests write with `Debug.Log`, copied below from the results XML of that run.
- One process, one main thread: a server `NetworkManager` and client `NetworkManager`s, each with its own `UnityTransport` on 127.0.0.1:7801. Loopback has no loss, no reordering and no latency worth the name; nothing below says anything about a real network.

## Configuration

**What is run**: `test_config_server_starts_and_exchanges_message_with_no_prefab_no_approval_no_scene_management`. `NetworkConfig`: `PlayerPrefab = null`, `ConnectionApproval = false`, `EnableSceneManagement = false`, `NetworkTransport` assigned in code. No host, no `NetcodeIntegrationTest`, no reflection. GameObjects are created inactive, components added and configured, then activated, so `NetworkManager.Awake` and `OnEnable` run with the config in place. One 32-byte unnamed message is sent client to server and must reach the intake once.

**Observed**:
- `[CONFIG] PlayerPrefab=null ConnectionApproval=False EnableSceneManagement=False ForceSamePrefabs=True (left at its default) TickRate=30 NetworkTransport=UnityTransport endpoint=127.0.0.1:7801`
- `[CONFIG] server started and exchanged an unnamed message with these three settings; none had to change. Client NGO id=1, server-side id seen by dispatcher=1`
- Two `NetworkManager`s ran in one process in every test. `NetworkManager.Singleton` was never used to reach a manager; NGO did not refuse the second one.
- The PlayMode asmdef needed one reference the story's note did not list as certain: `Unity.Networking.Transport`. `UnityTransport.SetConnectionData` has an overload that takes a `NetworkEndpoint`, so the compiler needs that assembly even though the test names no type of it (`error CS0012` on the first compile). `UnityTransport` itself is in `Unity.Netcode.Runtime`. The asmdef uses a `UnityEngine.TestRunner` reference, not `optionalUnityReferences`.

**Result**: PASS

## VR-1 Frame position

**What is run**: `test_vr1_client_disconnect_never_enters_intake_inside_tick` (client `Shutdown()` at frame 45 of 90 frames of traffic) and `test_vr1_server_initiated_disconnect_never_enters_intake_inside_tick` (`shell.Disconnect(id)` at frame 45 of 90). The recording intake stores, per `TryAccept`, whether `TestTickDriver.IsInsideAdvanceTick` was true. Asserted: tick count > 0, messages flowed, zero `TryAccept` entries inside `AdvanceTick`.

**Observed**:
- `[VR-1] client-side disconnect: ticks=32 messagesSeen=92 accepted=92 tryAcceptInsideTick=0 addConnectionInsideTick=0 removeConnectionInsideTick=0 removeCalls=1`
- `[VR-1] server-initiated disconnect: ticks=42 messagesSeen=90 accepted=90 tryAcceptInsideTick=0 addConnectionInsideTick=0 removeConnectionInsideTick=0 removeCallsForId=1 clientStillConnected=False`

**Limits**: the statement holds for the test driver (`AdvanceTick` from `Update`) only. The tick's work in this run is an empty `ServerTickLoop`: nothing in it sends, disconnects or otherwise touches the transport, so the case "the tick itself triggers a transport event" was not exercised. A disconnect caused by a reliable send-queue overflow was not exercised either.

**Result**: PASS

## VR-2 Buffer and allocation

**What is run**: `test_vr2_shell_handler_and_core_allocate_nothing_per_message`. NGO calls the unnamed-message handlers in subscription order with nothing but a `Seek` between two of them (`CustomMessagingManager.InvokeUnnamedMessage`, read in the installed source). The test subscribes four handlers in the order *begin, shell, control, end*: *begin* enables the profiler's `GC.Alloc` recorder (`UnityEngine.Profiling.Recorder`, filtered to the current thread), *end* disables it and adds its `sampleBlockCount`. The window therefore holds `NgoInboundTransportShell.HandleUnnamedMessage`, `InboundTransportAdapter.OnMessageReceived` and the intake double (a counter and a `HashSet<uint>.Contains`), and nothing of NGO. This is the measurement behind `Is.Not.AllocatingGCMemory()`, taken once per message. 100 warm-up messages, then 500 measured messages (5 per frame, 32 bytes), then a control: 20 messages for which the *control* handler allocates one 65,536-byte array inside the window. Asserted: 0 samples over the 500 messages; the control sees at least one sample per message.

**Observed**:
- `[VR-2] GC.Alloc samples inside the window [shell handler + core + intake double], per message: warmUpMessages=100 warmUpSamples=0 | measuredMessages=500 accepted=500 gcAllocSamples=0 | control (one 65536-byte array per message inside the window): messages=20 gcAllocSamples=20.`
- The core alone, EditMode: `test_accept_path_allocates_nothing_over_1000_messages` passes with `Is.Not.AllocatingGCMemory()`.
- NGO's own allocations per received message: **not measured**. They are outside the window by construction. Two whole-frame measurements were tried first and dropped: `GC.GetAllocatedBytesForCurrentThread` read 0 for every window in this Editor, an idle one included, so it measures nothing here; the profiler counter "GC Allocated In Frame" read about 550 KB per idle frame in batch mode, which hides any per-message figure.

**How this differs from the criterion's wording**: the criterion says "the profiler shows". What was read is the profiler's `GC.Alloc` marker, by code, scoped to the handler; **no one looked at a Profiler window**. If a look at the window is wanted as well, it is still to do; the 32-byte message size was the only size measured.

**Result**: PASS (by the `GC.Alloc` marker; no Profiler window look)

## VR-3 Ids

**What is run**: `test_vr3_ids_across_connections_and_after_server_restart_are_recorded`. Three successive client connections (connect, record the id, client `Shutdown`, wait for `RemoveConnection`, destroy, repeat). Then `Shutdown()` of the server `NetworkManager`, `StartServer()` again on the same instance in the same process, a new shell, one client connects. Record-only: asserted only that the connections happened and the ids fit in `uint`.

**Observed**:
- `[VR-3] successive connection ids (dispatcher AddConnection): 1, 2, 3 | client-side LocalClientId: 1, 2, 3 | any id repeated within one server run after a disconnect: False`
- `[VR-3] after NetworkManager shutdown and restart in one process: first id=4 (client LocalClientId=4); equals an earlier id: False`
- So: no reuse after a disconnect within one server run, and the counter was **not reset** by a `Shutdown()` / `StartServer()` on the same `NetworkManager` instance. A new `NetworkManager` instance in the same process, and a new process, were not tried.

**Result**: PASS (recorded)

## VR-4 Version and API

**What is run**: `test_vr4_versions_are_logged` reads `Packages/packages-lock.json`. API: `IronGrind.ServerLogic` and the PlayMode assembly compile against the installed packages.

**Observed**:
- `[VR-4] Unity=6000.3.10f1 packages-lock.json: com.unity.netcode.gameobjects=2.13.3, com.unity.transport=2.6.0 | assembly versions (not package versions): Unity.Netcode.Runtime=0.0.0.0, UnityTransport type loaded from Unity.Netcode.Runtime.`
- The signatures the shell uses compile as written in the engine reference: `NetworkManager.CustomMessagingManager`, `event UnnamedMessageDelegate OnUnnamedMessage` with `(ulong, FastBufferReader)`, `FastBufferReader.Length` / `Position` / `ReadBytesSafe(ref byte[], int, int)`, `NetworkManager.OnClientConnectedCallback` / `OnClientDisconnectCallback` as `Action<ulong>`, `NetworkManager.DisconnectClient(ulong)`. No signature had to be corrected.

**Result**: PASS

## VR-5 Reliable ordered

**What is run**: `test_vr5_reliable_sequenced_200_messages_arrive_in_order_once`. 200 numbered `ReliableSequenced` messages, 5 per frame, from one client; waits for 200 accepted, then 30 more frames to catch late duplicates. Asserted: exactly 200 accepted, no duplicate, each sequence number one more than the previous, first is 0.

**Observed**: `[VR-5] sent=200 accepted=200 duplicates=0 outOfOrderSteps=0 (ReliableSequenced, 5 per frame)`

**Limits**: loopback. No packet was lost or reordered underneath, so the retransmission path of the reliable pipeline was not exercised.

**Result**: PASS

## VR-6 After a disconnect

**What is run**: the client sends a burst of 40 messages in one frame, so NGO dispatches them from one receive loop.
- (a) `test_vr6a_raw_handler_after_direct_disconnect_client_is_recorded_not_asserted` - record-only. A bare handler subscribed by the test to the server's `OnUnnamedMessage` calls `serverManager.DisconnectClient(id)` (the deferred overload, directly, not through the adapter) **from inside the handler of the first message**, with the other 39 still queued behind it.
- (b) `test_vr6b_adapter_disconnect_accepts_nothing_after_the_call` - asserted. Same burst; the intake double calls `shell.Disconnect(id)` from inside `TryAccept` of the first accepted message. Asserted: no message of that id is accepted afterwards.

A first version of both tests called the disconnect from the test body after the first delivery; on loopback the whole burst had already been dispatched by then, so it showed nothing. The calls were moved inside the handler for that reason.

**Observed**:
- `[VR-6a] burst=40 rawHandlerBeforeCall=1 rawHandlerAfterCallSameFrame=39 rawHandlerAfterCallLaterFrames=0 intakeAcceptedAfterCall(adapter not told)=39 frameOfCall=540 frameOfRemoveConnection=540.` - **yes: after `DisconnectClient(id)`, the 39 queued messages of that client still reached the handlers in the same frame**, and an adapter that is not told accepts them. The disconnect callback came later in the same frame.
- `[VR-6b] burst=40 acceptedBeforeCall=1 acceptedAfterCall=0 reachedIntakeButRejectedAfterCall=39 (NGO still delivered queued messages after the call; the intake dropped them)` - with the adapter's `Disconnect`, the same 39 reached `TryAccept` and none was accepted; `RemoveConnection` ran once for the id.

**Limits**: the disconnect was called from inside a receive handler, which is how the "still queued" case is forced. A disconnect decided on the tick (from `Update`) was run in VR-1; there nothing of that client arrived afterwards on loopback.

**Result**: PASS

## Shell behaviour (added after `/code-review`, 2026-10-12)

Not a VR item. Three tests of the NGO shell through a real session, added by the review.

**Observed**:
- `[SHELL] sent lengths 9, 10, 400, 401 then a 32-byte sentinel; lengths that reached TryAccept: 10, 400, 32` - the shell's own length check, through NGO's reader.
- `[SHELL] NGO delivered 3 messages; accepted while started=1, while stopped=0, TryAccept calls in all after a double Start=2` - `Stop()` ends delivery, `Stop()` and `Start()` twice are harmless, a second `Start()` does not subscribe twice.
- `[SHELL] NetworkManager never started: CustomMessagingManager is null=True; Start() threw InvalidOperationException.`

**Not run**: `NetworkManager.DisconnectClient` throwing `NotServerException` on a manager that is not a server was read in the installed source by the reviewer, not exercised; the core's catch of a throwing transport seam is tested in EditMode with a fake.

**Result**: PASS

## Reference updated

**What was done**: `docs/engine-reference/unity/modules/networking.md`, section "NGO 2.13 - Custom Messaging, Transport and Frame Timing": a subsection "Confirmed in a run, 2026-10-12" lists each statement the run confirmed, the one it settled (the id counter across a restart) and what it added (`NetworkConfig` is a field, the `Unity.Networking.Transport` reference, two managers in one process); the header line, the "Connections" id line and the "Not verified" list are updated. The run contradicted no statement of the section.

**Result**: PASS
