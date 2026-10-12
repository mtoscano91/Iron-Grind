# Story 039 - NGO Send Verification Run (VS-1 to VS-5)

> **Story**: `production/epics/networking-core/story-039-client-message-outbox.md`
> **Run file**: `tests/PlayMode/Networking/ClientMessageOutbox_ngo_run_tests.cs` (assembly `IronGrind.PlayModeTests`); fixture `ClientMessageOutbox_ngo_run_tests`. The mapping table is tested in EditMode, in `tests/EditMode/Networking/NgoClientMessageSender_tests.cs`.
> **Sender under test**: `src/ServerLogic/Networking/Outbound/NgoClientMessageSender.cs`; outbox: `ClientMessageOutbox` (same folder)
> **Date**: 2026-10-12
> **Status**: RUN 2026-10-12. VS-1 to VS-5 PASS; none NOT RUN, none FAIL.

## How it is run

- Harness: Story 038's shape - one server `NetworkManager` and one client `NetworkManager` in one process, each with its own `UnityTransport`, on 127.0.0.1:7802 (038 uses 7801), no player prefab, `ConnectionApproval` off, `EnableSceneManagement` off. The test drives `outbox.Flush(tick)` itself, one flush per frame, with a tick counter it owns (no `ServerTickLoop`, no tick driver).
- Wiring as production will: `new ClientMessageOutbox(new NgoClientMessageSender(serverManager))`, `new NgoInboundTransportShell(serverManager, intake, dispatcher, outbox)`, `outbox.SetDisconnectRequester(shell.Adapter)`.
- Unity 6000.3.10f1, `com.unity.netcode.gameobjects` 2.13.3, `com.unity.transport` 2.6.0 (`Packages/packages-lock.json`).
- Editor Test Runner run by the user (PlayMode tab), **not a headless build**: `test-run` start 2026-10-12 19:00:45Z, end 19:01:04Z, **20 / 20 passed** (12 of Story 038, 8 of this story). Totals and observations read by the main session from `TestResults.xml`. EditMode the same day: 2487 / 2487 (18:56:54Z).
- Observations are the `[VS-n]` and `[SENDER]` lines the tests write with `Debug.Log`, copied from the results XML.
- Loopback: no loss, no reordering, no latency worth the name. Nothing below says anything about a real network.

## VS-1 Round trip

**What is run**: `test_vs1_round_trip_full_sync_then_slot_update_arrive_byte_for_byte_in_order`. An `InventoryFullSync` (body from `InventoryFullSyncCodec`, 211 bytes with its envelope) and an `InventorySlotUpdate` (two entries) are enqueued for one client and flushed once. The client's `OnUnnamedMessage` copies each message. Asserted: exactly two messages, each byte for byte equal to envelope (type id, sequence 1 then 2, the flush tick) plus body, in order.

**Observed**: `[VS-1] sent InventoryFullSync(211 bytes with envelope, seq 1) then InventorySlotUpdate(21 bytes, seq 2) at tick 100; received 2 messages of lengths 211, 21; byte for byte equal: first=True second=True; arrival order = send order: True`

**Result**: PASS

## VS-2 Cap on the wire

**What is run**: `test_vs2_eleven_messages_arrive_as_eight_at_tick_t_and_three_at_tick_t_plus_one`. 11 non-exempt `InventorySlotUpdate` messages are enqueued before one flush at tick T; the next flush, at T+1, is one frame later. Asserted: 8 arrive with tick T, 3 with tick T+1, sequence numbers 1..11 in arrival order, bodies in enqueue order.

**Observed**: `[VS-2] 11 non-exempt messages enqueued, flushes at tick 100 and 101 one frame apart: arrived with tick 100=8, tick 101=3, other=0; received 11 messages; sequence@tick/frame in arrival order: 1@100/frame146 ... 8@100/frame146 9@101/frame147 10@101/frame147 11@101/frame147; sequence 1..11 in arrival order=True; bodies and envelopes byte for byte=True` (the eight of tick 100 all arrived in frame 146, the three of tick 101 in frame 147)

**Result**: PASS

## VS-3 Removed client

**What is run**: `test_vs3_removed_client_nothing_on_the_wire_and_no_ngo_error`. One baseline message proves the path. Then `shell.Adapter.Disconnect(id)`; messages are enqueued for that id on three ticks and the outbox is flushed every frame for 60 frames. Asserted: the client receives nothing more, and no error, exception or assert is logged (the test's own log capture). The outbox's own warning for an unknown client is counted in the line, not asserted (a warning does not fail a test, and the story says "at most one per client per tick").

**Observed**: `[VS-3] after shell.Adapter.Disconnect(1): messages enqueued after it on 3 ticks, 60 flushes; client received 0 further messages; error/exception/assert log entries=0; log lines mentioning [ClientMessageOutbox]=3; client still connected after the wait=False` - the three lines are the outbox's own `UnknownClient` warning, one per tick, for type 0xE120. Whether NGO would also have dropped the sends silently is not separated by this test (the `[SENDER]` unknown-id line below shows it does).

**Result**: PASS

## VS-4 Allocation

**What is run**: `test_vs4_flush_of_500_messages_allocates_nothing_in_the_outbox_and_ngo_is_recorded`. Method of Story 038's VR-2: one `GC.Alloc` `UnityEngine.Profiling.Recorder` filtered to the current thread, enabled around `Flush` only and read right after it is disabled. A second outbox over a measuring seam that wraps the real `NgoClientMessageSender`. 20 warm-up flushes of 8 messages (forwarded). Then three phases of flushes of 8: (A) 63 flushes with the seam not forwarding, so the window holds the outbox's code alone - asserted 0 samples; (control) 3 flushes in which the seam allocates a 64 KB array per message in the same window - asserted samples >= messages; (B) 63 flushes with the seam forwarding, so the window holds the outbox, the sender and NGO's queueing - recorded, not asserted. No Profiler window was looked at. **A first version failed and was replaced**: it used two recorders and paused one inside the window around the inner send; its control read 0 for 24 allocations (run of 18:58Z), so its "0" for the outbox proved nothing.

**Observed**: `[VS-4] ... warmUpMessages=160 | outbox alone (seam not forwarding): flushes=63 messages=504 samples (asserted 0)=0 | control (one 65536-byte array per message in the same window): messages=24 samples=24 | outbox + sender + NGO queueing (seam forwarding; recorded, not asserted): messages=504 samples=0 (0.00 per message) | client received 664 of 664 forwarded.`

**Result**: PASS

## VS-5 Oversize

**What is run**: `test_vs5_oversize_1300_byte_message_through_the_sender_is_recorded_not_asserted`. `NgoClientMessageSender.Send` is called directly with a 1,300-byte message on `ReliableOrdered` (`ReliableSequenced`) in the Editor. Observation only: the exception type if any, every log entry, whether the client receives the message and how long it is, whether the client and the server stay up. The test passes whatever NGO does (`LogAssert.ignoreFailingMessages` is true inside this test only and restored in `finally`). Expected from source (read, **not run**): `ValidateMessageSize` throws `OverflowException` in builds with `DEBUG`.

**Observed**: `[VS-5] Editor run, 1300-byte message on ReliableOrdered (ReliableSequenced) through NgoClientMessageSender.Send: exception=OverflowException: Given message size (1300 bytes) is greater than the maximum allowed for the selected delivery method (1272 bytes). Try using ReliableFragmentedSequenced delivery method instead.; log entries during the call and 90 frames after: 0; client received 0 messages; client still connected=True; server still listening=True` - the Editor defines `DEBUG` for NGO, and the limit NGO states is 1272 bytes of payload.

**Result**: PASS

## Sender behaviour

**What is run** (not VS items):
- `test_sender_one_message_per_channel_arrives_intact` - one 40-byte message per `NetworkChannel` value through `Send`; each must arrive byte for byte (matched by content, since `Unreliable` carries no order guarantee). Line: `[SENDER] one 40-byte message per channel ...`.
- `test_sender_on_a_manager_that_was_never_started_throws_invalid_operation_and_sends_nothing` - `CustomMessagingManager` is null, `Send` throws `InvalidOperationException`.
- `test_sender_to_an_unknown_client_id_is_recorded_not_asserted` - record only: exception type or none, NGO log entries. Expected from source (read, **not run**): NGO skips the message silently at the default log level.
- The mapping table is not part of this run: five EditMode tests in `tests/EditMode/Networking/NgoClientMessageSender_tests.cs` (the three channels, an undefined value, a null manager). The delivery NGO actually used on the wire is not observed by any test; `ToNetworkDelivery` is the one table.

**Observed**: `[SENDER] one 40-byte message per channel: Unreliable->Unreliable intact=True, ReliableUnordered->Reliable intact=True, ReliableOrdered->ReliableSequenced intact=True; received 3 messages in all.` `[SENDER] NetworkManager never started: Send threw InvalidOperationException=True` `[SENDER] Send to unknown client id 999: exception=none; log entries=0; the real client received 0 messages`

**Result**: PASS

## Not verified / limits

- Loopback only: no packet loss, reordering or latency. The retransmission path of the reliable pipeline and `Unreliable` loss are not exercised.
- One client only in every test. The per-client cap with two clients is an EditMode test of the outbox, not of this run.
- VS-5 is the Editor run (`DEBUG` defined). A release or headless build is out of scope for this story; what NGO does with an oversize message there is not known.
- NGO's own allocations are recorded in VS-4, not asserted; the profiler marker is read by code on the main thread only, and no Profiler window was looked at.
- The tick is driven by the test, one flush per frame; `ZoneTickPipeline` and `ServerTickLoop` are not part of this run, so the claim "the Outbound step is the last step of the tick" is not exercised here.
- A reliable send-queue overflow (NGO disconnecting a client because the outbox burst too much) is not exercised.
- Only `ReliableSequenced` is used through the outbox in VS-1 to VS-4 (every inventory message is R-OD); `Reliable` and `Unreliable` are exercised only by the `[SENDER]` test, one message each.
- Behaviour of `SendUnnamedMessage` for an unknown client id and with the manager not listening is read in the installed source and checked only by the `[SENDER]` tests above.
- Client-side decoding is out of scope; the client test only copies bytes.
