using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Zone Instancing Story 014: the composition's quit hook, close to teardown exit codes, loading tick, direct Fatal,
    /// the quit stall guard and the drain after the close steps. The fake backend of the startup tests plus a real
    /// <see cref="ZoneInstancingWiring"/> over fakes; ticks are applied by calling the wiring's step by hand and the clock
    /// is the rig's fake clock.
    /// </summary>
    [TestFixture]
    public sealed class ZoneServerCompositionDrainTests
    {
        private sealed class DrOutbox : IClientMessageOutbox
        {
            public int Sent;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                Sent++;
            }
        }

        private sealed class DrDispatcher : IInboundRequestDispatcher
        {
            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private sealed class DrDisconnector : IInboundTransportDisconnector
        {
            public void DisconnectClient(ulong transportClientId) { }
        }

        private sealed class DrApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        private sealed class DrPersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) =>
                Task.FromResult(CharacterSaveResult.Success);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) =>
                Task.FromResult(CharacterSaveResult.Success);

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                entityId = EntityID.Invalid;
                return false;
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly string QUIT_CLEAN = "Quit:" + (int)ZoneExitCode.Clean;
        private static readonly string QUIT_FATAL = "Quit:" + (int)ZoneExitCode.Fatal;
        private static readonly string QUIT_STARTUP_FAILED = "Quit:" + (int)ZoneExitCode.StartupFailed;
        private static readonly double STALL_SECONDS = ZoneServerComposition.QUIT_TICK_STALL_SECONDS;
        private static readonly Regex STALL_WARNING =
            new Regex(@"^\[ZoneServer\] no tick for \d+\.\d s with a quit pending: requesting a Clean teardown$");

        private TickCompletionQueue _queue;
        private uint _nextCharacter;

        [SetUp]
        public void SetUp()
        {
            _queue = new TickCompletionQueue();
            _nextCharacter = 1000u;
        }

        [TearDown]
        public void TearDown()
        {
            _queue.DrainOnShutdown(TimeSpan.FromMilliseconds(200));
        }

        private ZoneInstancingWiring Attach(SuRig rig, List<ZoneExitCode> requests = null)
        {
            var persistence = new DrPersistence();
            var wiring = new ZoneInstancingWiring(ZONE, rig.Backend.TickLoop, _queue, new DrDispatcher(), new DrOutbox(),
                persistence, persistence, new DrApplier(), new CharacterMutationGate(), new StandInZoneInventoryService(),
                new DrDisconnector(),
                code =>
                {
                    requests?.Add(code);
                    rig.Comp.RequestTeardown(code);
                });
            rig.Comp.AttachZoneInstancing(wiring);
            return wiring;
        }

        private ZoneInstancingWiring StartActive(SuRig rig, List<ZoneExitCode> requests = null)
        {
            ZoneInstancingWiring wiring = Attach(rig, requests);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            Assert.AreEqual(ZoneState.Active, wiring.Lifecycle.State);
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
            return wiring;
        }

        private void AddConnectedPlayer(ZoneInstancingWiring wiring, uint client)
        {
            uint n = _nextCharacter++;
            var character = new CharacterID(n);
            Assert.IsTrue(wiring.Pool.TryAllocatePlayer(ZONE, character, new AccountID(n + 5000u), client, out _, out EntityID id));
            wiring.Pool.SetSessionState(id, ZoneSessionState.Connected);
            wiring.Directory.Add(client, character);
            wiring.TickRoster.AddPending(id);
        }

        private static void RunTeardown(SuRig rig)
        {
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            SuRig.Drive(rig.Comp.RunTeardown());
        }

        // ---------------------------------------------------------------- quit hook

        [Test]
        public void test_quit_before_s5_requests_clean_teardown_and_stops_startup()
        {
            var rig = new SuRig();
            bool? quitResult = null;
            rig.Backend.OnOpenDatabase = () => quitResult = rig.Comp.OnWantsToQuit();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            RunTeardown(rig);

            Assert.IsTrue(quitResult.HasValue, "the quit hook ran at S4");
            Assert.IsFalse(quitResult.Value);
            CollectionAssert.DoesNotContain(rig.Log, "S5:Construct");
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_quit_while_loading_closes_the_zone_cleanly_and_abandons_the_scene_wait()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = Attach(rig);
            int polls = 0;
            bool? quitResult = null;
            rig.Scene.NotDonePolls = 10;
            rig.Scene.OnPoll = () =>
            {
                if (++polls == 2)
                {
                    quitResult = rig.Comp.OnWantsToQuit();
                }
            };

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsTrue(quitResult.HasValue, "the quit hook ran at the second poll");
            Assert.IsFalse(quitResult.Value);
            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            Assert.AreEqual(2, polls, "the wait ends at the quit");
            CollectionAssert.DoesNotContain(rig.Log, "S6:AddNavMeshData");
            CollectionAssert.DoesNotContain(rig.Log, "S7:Listener");
            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_quit_in_active_with_no_players_drains_and_closes_on_the_next_tick_with_clean_exit()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);

            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            Assert.AreEqual(1, wiring.PendingQuitCount);
            Assert.AreEqual(ZoneState.Active, wiring.Lifecycle.State, "applied by the next tick, not at once");
            Assert.IsFalse(rig.Comp.TryBeginTeardown());

            wiring.Run(5u, 0f);

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State, "T-4 and T-5 on the same tick");
            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_quit_in_active_with_a_connected_player_enters_draining_without_teardown()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);
            AddConnectedPlayer(wiring, 11u);

            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            wiring.Run(5u, 0f);

            Assert.AreEqual(ZoneState.Draining, wiring.Lifecycle.State);
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_second_quit_while_draining_closes_the_zone_with_clean_exit()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);
            AddConnectedPlayer(wiring, 11u);
            rig.Comp.OnWantsToQuit();
            wiring.Run(5u, 0f);

            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            wiring.Run(6u, 0f);

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_request_quit_takes_the_same_path_as_the_quit_hook()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);

            rig.Comp.RequestQuit();
            Assert.AreEqual(1, wiring.PendingQuitCount);
            Assert.IsFalse(rig.Comp.OnWantsToQuit());

            Assert.AreEqual(2, wiring.PendingQuitCount);
        }

        [Test]
        public void test_request_quit_without_a_wiring_requests_clean_teardown()
        {
            var rig = new SuRig();

            rig.Comp.RequestQuit();

            Assert.IsTrue(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_request_quit_when_the_zone_is_already_closed_requests_clean_teardown()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = Attach(rig);
            wiring.Lifecycle.MarkInitFailed(0u); // LoadFailed: closes the zone without a teardown request
            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            Assert.IsFalse(rig.Comp.TryBeginTeardown());

            rig.Comp.RequestQuit();

            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- quit stall

        [Test]
        public void test_quit_stall_with_a_pending_quit_requests_clean_teardown_only_after_the_threshold()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);
            rig.Now = 1d;
            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            Assert.AreEqual(1, wiring.PendingQuitCount);

            rig.Now = 1d + STALL_SECONDS - 0.01d;
            rig.Comp.PollQuitStall();
            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "not before the threshold");

            rig.Now = 1d + STALL_SECONDS;
            LogAssert.Expect(LogType.Warning, STALL_WARNING);
            rig.Comp.PollQuitStall();

            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_quit_stall_counts_from_the_quit_when_the_last_tick_was_long_ago()
        {
            var rig = new SuRig();
            StartActive(rig);
            rig.Now = 100d;
            Assert.IsFalse(rig.Comp.OnWantsToQuit());

            rig.Comp.PollQuitStall();
            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "a quit after a long gap is not forced at once");

            rig.Now = 100d + STALL_SECONDS;
            LogAssert.Expect(LogType.Warning, STALL_WARNING);
            rig.Comp.PollQuitStall();
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_quit_stall_does_nothing_while_ticks_keep_running()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);
            Assert.IsFalse(rig.Comp.OnWantsToQuit());

            for (int i = 0; i < 120; i++)
            {
                rig.Now += 0.05d;
                rig.Net.FireTick();
                rig.Comp.PollQuitStall();
            }

            Assert.Greater(rig.Now, STALL_SECONDS, "more than the threshold has passed since the quit");
            Assert.AreEqual(1, wiring.PendingQuitCount, "the pipeline is not wired here, so the quit stays pending");
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_quit_stall_in_draining_with_stopped_ticks_requests_clean_teardown()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);
            AddConnectedPlayer(wiring, 11u);
            Assert.IsFalse(rig.Comp.OnWantsToQuit());
            wiring.Run(5u, 0f);
            Assert.AreEqual(ZoneState.Draining, wiring.Lifecycle.State);
            Assert.AreEqual(0, wiring.PendingQuitCount);

            rig.Now = STALL_SECONDS - 0.01d;
            rig.Comp.PollQuitStall();
            Assert.IsFalse(rig.Comp.TryBeginTeardown());

            rig.Now = STALL_SECONDS;
            LogAssert.Expect(LogType.Warning, STALL_WARNING);
            rig.Comp.PollQuitStall();

            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_quit_stall_in_active_with_no_quit_pending_does_nothing()
        {
            var rig = new SuRig();
            StartActive(rig);

            rig.Now = 1000d;
            rig.Comp.PollQuitStall();

            Assert.IsFalse(rig.Comp.TryBeginTeardown());
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- loading tick

        [Test]
        public void test_loading_tick_past_the_load_timeout_ends_the_scene_wait_as_startup_failed()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = Attach(rig);
            rig.Scene.NotDonePolls = 100000;
            rig.Scene.OnPoll = () => rig.Now += 1d;
            LogAssert.Expect(UnityEngine.LogType.Error, "[ZoneServer] startup failed at S6: LoadTimeout");

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            CollectionAssert.DoesNotContain(rig.Log, "S6:AddNavMeshData");
            Assert.GreaterOrEqual(rig.Now, ZoneLifecycle.ZONE_LOAD_TIMEOUT_SECONDS);
            Assert.LessOrEqual(rig.Now, ZoneLifecycle.ZONE_LOAD_TIMEOUT_SECONDS + 1d, "closed on the first poll past the timeout");
            RunTeardown(rig);
            Assert.AreEqual(QUIT_STARTUP_FAILED, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_loading_tick_before_the_timeout_keeps_the_zone_loading()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = Attach(rig);
            var states = new List<ZoneState>();
            rig.Scene.NotDonePolls = 5;
            rig.Scene.OnPoll = () =>
            {
                states.Add(wiring.Lifecycle.State);
                rig.Now += 1d;
            };

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsNotEmpty(states);
            Assert.IsTrue(states.TrueForAll(state => state == ZoneState.Loading), "Loading at every poll of the wait");
            Assert.AreEqual(ZoneState.Active, wiring.Lifecycle.State);
            Assert.Less(rig.Now, ZoneLifecycle.ZONE_LOAD_TIMEOUT_SECONDS);
        }

        [Test]
        public void test_invalid_bounds_with_the_real_wiring_closes_the_zone_and_drains_without_a_flush()
        {
            var rig = new SuRig();
            rig.Plan.FailAt = "S6:Bounds";
            ZoneInstancingWiring wiring = Attach(rig);
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S6: StepReturnedFalse");

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State, "T-3");
            RunTeardown(rig);
            // The close steps are the wiring's own (the fake close steps are not used), so only the drain is logged.
            Assert.GreaterOrEqual(rig.Log.IndexOf("Drain"), 0, "the queue is drained although the tick never started");
            CollectionAssert.DoesNotContain(rig.Log, "OutboundFlush");
            Assert.AreEqual(QUIT_STARTUP_FAILED, rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- fatal

        [Test]
        public void test_direct_fatal_teardown_request_closes_the_lifecycle_once_with_fatal_exit()
        {
            var rig = new SuRig();
            var requests = new List<ZoneExitCode>();
            ZoneInstancingWiring wiring = StartActive(rig, requests);

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            CollectionAssert.AreEqual(new[] { ZoneExitCode.Fatal }, requests, "no recursion: one request from the wiring");
            RunTeardown(rig);
            Assert.AreEqual(QUIT_FATAL, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_direct_fatal_while_draining_closes_the_zone_with_fatal_exit()
        {
            var rig = new SuRig();
            var requests = new List<ZoneExitCode>();
            ZoneInstancingWiring wiring = StartActive(rig, requests);
            AddConnectedPlayer(wiring, 11u);
            rig.Comp.OnWantsToQuit();
            wiring.Run(5u, 0f);
            Assert.AreEqual(ZoneState.Draining, wiring.Lifecycle.State);

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            CollectionAssert.AreEqual(new[] { ZoneExitCode.Fatal }, requests);
            RunTeardown(rig);
            Assert.AreEqual(QUIT_FATAL, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_direct_fatal_when_the_zone_is_already_closed_does_not_ask_the_wiring_again()
        {
            var rig = new SuRig();
            var requests = new List<ZoneExitCode>();
            ZoneInstancingWiring wiring = Attach(rig, requests);
            wiring.Lifecycle.MarkInitFailed(0u); // LoadFailed: closed, no teardown request from the wiring
            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Assert.IsEmpty(requests, "no second close request through the wiring");
            RunTeardown(rig);
            Assert.AreEqual(QUIT_FATAL, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_first_teardown_code_wins_over_a_later_fatal()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            rig.Comp.RequestTeardown(ZoneExitCode.Fatal);

            Assert.AreEqual(ZoneState.Active, wiring.Lifecycle.State, "the later Fatal does nothing");
            RunTeardown(rig);
            Assert.AreEqual(QUIT_CLEAN, rig.Log[rig.Log.Count - 1]);
        }

        [Test]
        public void test_request_fatal_through_the_wiring_requests_teardown_with_fatal_exit()
        {
            var rig = new SuRig();
            ZoneInstancingWiring wiring = StartActive(rig);

            wiring.RequestFatal(9u);

            Assert.AreEqual(ZoneState.Closed, wiring.Lifecycle.State);
            RunTeardown(rig);
            Assert.AreEqual(QUIT_FATAL, rig.Log[rig.Log.Count - 1]);
        }

        // ---------------------------------------------------------------- drain after close steps

        [Test]
        public void test_teardown_drains_after_the_close_steps_when_the_tick_never_started()
        {
            var log = new List<string>();
            var comp = new ZoneServerComposition(new ZsApp(log), new ZsNetwork(log), new ZsNav(log), new ZsClose(log), 100);
            comp.AttachOwnedObjects(
                new ZsQueue(log), new ZsOutbound(log), new ZsListener(log), new ZsTransport(log), () => 1u, () => { });
            comp.MarkZoneStateEntered();

            SuRig.Drive(comp.RunTeardown());

            CollectionAssert.DoesNotContain(log, "OutboundFlush");
            Assert.Greater(log.IndexOf("Drain"), log.IndexOf("CloseSteps"), "drain runs after the close steps");
            Assert.GreaterOrEqual(log.IndexOf("CloseSteps"), 0);
        }
    }
}
