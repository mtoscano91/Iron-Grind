using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Integration.ZoneInstancing
{
    /// <summary>
    /// Integration tests for Zone Instancing Story 012 — <see cref="ZoneCloseSteps"/> (CR-ZI-12 steps 1-8) and its
    /// three seams with their stand-ins. Real pool, directory, roster, <see cref="ZoneEntryReclamation"/> and
    /// <see cref="TickCompletionQueue"/> (saves are <see cref="TaskCompletionSource{TResult}"/> tasks); fake persistence,
    /// party, mob timers, session sink, drain notice log and outbox. The reverse-completion-order test uses a manual
    /// queue because the real queue delivers finished tasks in track order.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_Teardown_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint PARTY_RAW = 77u;
        private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromSeconds(5);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private struct P
        {
            public EntityID Entity;
            public CharacterID Character;
            public uint Client;
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 100u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            public int Calls;

            public void DisconnectClient(ulong transportClientId) => Calls++;
        }

        private sealed class NoopReclaimSink : IZoneEntryReclaimSessionSink
        {
            public void ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned) { }
        }

        private struct Sent
        {
            public uint Client;
            public ZoneSessionEnded Message;
            public bool CapExempt;
        }

        private sealed class FakeOutbox : IClientMessageOutbox
        {
            public readonly List<Sent> Messages = new List<Sent>();

            /// <summary>Every message of any type: (client, type id, cap-exempt).</summary>
            public readonly List<KeyValuePair<uint, ushort>> All = new List<KeyValuePair<uint, ushort>>();

            public int AllFor(uint client)
            {
                int n = 0;
                foreach (KeyValuePair<uint, ushort> m in All)
                {
                    if (m.Key == client)
                    {
                        n++;
                    }
                }

                return n;
            }

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                All.Add(new KeyValuePair<uint, ushort>(clientId, messageTypeId));
                if (messageTypeId != ZoneSessionEnded.MessageTypeId)
                {
                    return; // e.g. the ZoneFullResponse of a stall reclamation; typed decoding only for ZoneSessionEnded
                }

                Assert.IsTrue(ZoneSessionEndedCodec.TryReadBody(body, out ZoneSessionEnded message));
                Messages.Add(new Sent { Client = clientId, Message = message, CapExempt = isCapExempt });
            }

            public int CountFor(uint client)
            {
                int n = 0;
                foreach (Sent s in Messages)
                {
                    if (s.Client == client)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private struct SaveCall
        {
            public CharacterID Character;
            public SessionEndReason Reason;
            public bool HasOverride;
            public float HpOverride;
        }

        private sealed class FakePersistence : ICharacterPersistence
        {
            public readonly List<SaveCall> Calls = new List<SaveCall>();
            public readonly Dictionary<CharacterID, TaskCompletionSource<CharacterSaveResult>> Sources =
                new Dictionary<CharacterID, TaskCompletionSource<CharacterSaveResult>>();
            public readonly HashSet<CharacterID> ThrowFor = new HashSet<CharacterID>();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) =>
                Record(id, reason, false, 0f);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) =>
                Record(id, reason, true, hpOverride);

            private Task<CharacterSaveResult> Record(CharacterID id, SessionEndReason reason, bool hasOverride, float hp)
            {
                Calls.Add(new SaveCall { Character = id, Reason = reason, HasOverride = hasOverride, HpOverride = hp });
                if (ThrowFor.Contains(id))
                {
                    throw new InvalidOperationException("save start failed");
                }

                var source = new TaskCompletionSource<CharacterSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                Sources[id] = source;
                return source.Task;
            }

            public int CallsFor(CharacterID id)
            {
                int n = 0;
                foreach (SaveCall c in Calls)
                {
                    if (c.Character == id)
                    {
                        n++;
                    }
                }

                return n;
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private struct Exit
        {
            public PartyID Party;
            public EntityID Entity;
        }

        private sealed class FakeParty : IPartyZonePresence
        {
            public readonly HashSet<CharacterID> InParty = new HashSet<CharacterID>();
            public readonly List<Exit> Exits = new List<Exit>();
            public readonly HashSet<EntityID> ThrowOnExit = new HashSet<EntityID>();
            public readonly HashSet<CharacterID> ThrowOnLookup = new HashSet<CharacterID>();
            public readonly List<int> ExitThreadIds = new List<int>();

            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                if (ThrowOnLookup.Contains(character))
                {
                    throw new InvalidOperationException("party lookup failed");
                }

                bool inParty = InParty.Contains(character);
                party = inParty ? new PartyID(PARTY_RAW) : PartyID.Uninitialized;
                return inParty;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId)
            {
                if (ThrowOnExit.Contains(entityId))
                {
                    throw new InvalidOperationException("party exit failed");
                }

                ExitThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                Exits.Add(new Exit { Party = party, Entity = entityId });
            }

            public int ExitsFor(EntityID entity)
            {
                int n = 0;
                foreach (Exit e in Exits)
                {
                    if (e.Entity == entity)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private sealed class FakeTimers : IZoneMobSpawnTimers
        {
            public readonly List<ZoneID> Cancelled = new List<ZoneID>();
            public bool Throw;

            public void CancelAllSpawnTimers(ZoneID zone)
            {
                Cancelled.Add(zone);
                if (Throw)
                {
                    throw new InvalidOperationException("timers failed");
                }
            }
        }

        private sealed class FakeSink : IZoneTeardownSessionSink
        {
            public readonly List<KeyValuePair<CharacterID, uint>> GhostSaves = new List<KeyValuePair<CharacterID, uint>>();
            public readonly Dictionary<CharacterID, TaskCompletionSource<CharacterSaveResult>> Sources =
                new Dictionary<CharacterID, TaskCompletionSource<CharacterSaveResult>>();
            public readonly List<uint> Expired = new List<uint>();
            public bool ThrowOnGhostSave;
            public readonly HashSet<uint> ThrowOnExpire = new HashSet<uint>();

            public Task<CharacterSaveResult> SaveGhostSnapshotAndExpire(CharacterID characterId, uint clientId)
            {
                GhostSaves.Add(new KeyValuePair<CharacterID, uint>(characterId, clientId));
                if (ThrowOnGhostSave)
                {
                    throw new InvalidOperationException("ghost save failed");
                }

                var source = new TaskCompletionSource<CharacterSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                Sources[characterId] = source;
                return source.Task;
            }

            public void ExpireConnectedSession(uint clientId)
            {
                if (ThrowOnExpire.Contains(clientId))
                {
                    throw new InvalidOperationException("expire failed");
                }

                Expired.Add(clientId);
            }
        }

        private sealed class FakeDrainLog : IZoneDrainNoticeLog
        {
            public readonly HashSet<uint> Notified = new HashSet<uint>();

            public bool WasNotifiedAtDrain(uint clientId) => Notified.Contains(clientId);
        }

        /// <summary>Holds callbacks until the test delivers them, in any order.</summary>
        private sealed class ManualQueue : ITickCompletionQueue
        {
            public readonly List<Action<TickTaskResult<CharacterSaveResult>>> Callbacks = new List<Action<TickTaskResult<CharacterSaveResult>>>();

            public int InFlightCount => Callbacks.Count;

            public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick) =>
                Callbacks.Add((Action<TickTaskResult<CharacterSaveResult>>)(object)onCompletedOnTick);

            public void Drain(uint currentTick) { }

            public void DrainOnShutdown(TimeSpan timeout) { }

            public void Deliver(int index, CharacterSaveResult value) =>
                Callbacks[index](new TickTaskResult<CharacterSaveResult>(TickTaskStatus.Completed, value, null));
        }

        /// <summary>A <see cref="ManualQueue"/> whose Track throws on one call (1-based).</summary>
        private sealed class ThrowingTrackQueue : ITickCompletionQueue
        {
            private int _calls;
            public int ThrowOnCall;
            public readonly List<Action<TickTaskResult<CharacterSaveResult>>> Callbacks = new List<Action<TickTaskResult<CharacterSaveResult>>>();

            public int InFlightCount => Callbacks.Count;

            public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick)
            {
                _calls++;
                if (_calls == ThrowOnCall)
                {
                    throw new InvalidOperationException("track failed");
                }

                Callbacks.Add((Action<TickTaskResult<CharacterSaveResult>>)(object)onCompletedOnTick);
            }

            public void Drain(uint currentTick) { }

            public void DrainOnShutdown(TimeSpan timeout) { }

            public void Deliver(int index, CharacterSaveResult value) =>
                Callbacks[index](new TickTaskResult<CharacterSaveResult>(TickTaskStatus.Completed, value, null));
        }

        private FakeOutbox _outbox;
        private FakePersistence _persistence;
        private FakeParty _party;
        private FakeTimers _timers;
        private FakeSink _sink;
        private FakeDrainLog _drainLog;
        private FakeDisconnector _disconnector;
        private ZoneSlotPool _pool;
        private ZoneConnectionCharacterDirectory _directory;
        private ZoneTickRoster _roster;
        private ZoneEntryReclamation _reclamation;
        private TickCompletionQueue _queue;
        private uint _nextCharacter;

        [SetUp]
        public void SetUp()
        {
            var ticks = new FakeTicks();
            _outbox = new FakeOutbox();
            _persistence = new FakePersistence();
            _party = new FakeParty();
            _timers = new FakeTimers();
            _sink = new FakeSink();
            _drainLog = new FakeDrainLog();
            _disconnector = new FakeDisconnector();
            _pool = new ZoneSlotPool(Zone, ticks, new FakePoolListener());
            _directory = new ZoneConnectionCharacterDirectory();
            _roster = new ZoneTickRoster(_pool, ticks);
            _reclamation = new ZoneEntryReclamation(Zone, _pool, _directory, _roster, _party, _outbox, _disconnector, new NoopReclaimSink(), ticks);
            _queue = new TickCompletionQueue();
            _nextCharacter = 1000u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private ZoneCloseSteps Steps(ITickCompletionQueue queue = null) =>
            new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, _persistence, queue ?? _queue, _party, _timers, _sink,
                _drainLog, _outbox, Zone);

        private P AddPlayer(uint client, ZoneSessionState state = ZoneSessionState.Connected, bool inParty = true)
        {
            uint n = _nextCharacter++;
            var character = new CharacterID(n);
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, character, new AccountID(n + 5000u), client, out _, out EntityID id));
            _pool.SetSessionState(id, state);
            _directory.Add(client, character);
            _roster.AddPending(id);
            if (inParty)
            {
                _party.InParty.Add(character);
            }

            return new P { Entity = id, Character = character, Client = client };
        }

        private void AddMobs(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out _));
            }
        }

        private void CompleteSave(P p, CharacterSaveResult result = CharacterSaveResult.Success) =>
            _persistence.Sources[p.Character].SetResult(result);

        private void Drain() => _queue.DrainOnShutdown(DRAIN_TIMEOUT);

        private static Regex Rx(string pattern) => new Regex(pattern);

        // ----- AC-ZI-12 -------------------------------------------------------------------------

        [Test]
        public void test_active_zone_with_dead_player_saves_with_hp_override_and_notifies_after_drain()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u, ZoneSessionState.Dead);
            P c = AddPlayer(3u);
            AddMobs(5);

            Steps().RunCloseSteps();

            Assert.AreEqual(3, _persistence.Calls.Count);
            foreach (SaveCall call in _persistence.Calls)
            {
                Assert.AreEqual(SessionEndReason.ZoneClosed, call.Reason);
                Assert.AreEqual(call.Character == b.Character, call.HasOverride, "only the Dead player is saved with an override");
                if (call.HasOverride)
                {
                    Assert.AreEqual(0f, call.HpOverride);
                }
            }

            Assert.AreEqual(0, _party.Exits.Count, "no NotifyZoneExit before the drain");

            CompleteSave(a);
            CompleteSave(b);
            CompleteSave(c);
            Drain();

            Assert.AreEqual(3, _party.Exits.Count);
            foreach (P p in new[] { a, b, c })
            {
                Assert.AreEqual(1, _party.ExitsFor(p.Entity));
            }

            Assert.AreEqual(new PartyID(PARTY_RAW), _party.Exits[0].Party);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _pool.OccupiedMobCount);
            Assert.AreEqual(3, _outbox.All.Count, "nothing but the three ZoneSessionEnded messages was sent");
            Assert.AreEqual(3, _outbox.Messages.Count);
            Assert.AreEqual(0, _pool.GetMobEntityIDsInZone(Zone).Count);
            foreach (P p in new[] { a, b, c })
            {
                Assert.AreEqual(1, _outbox.CountFor(p.Client));
            }

            foreach (Sent s in _outbox.Messages)
            {
                Assert.AreEqual(new ZoneSessionEnded(Zone, DisconnectReason.ZoneClosed, 0), s.Message);
                Assert.IsTrue(s.CapExempt);
            }

            Assert.AreEqual(0, _disconnector.Calls, "guards the reclamation path: teardown did not route a disconnect through it");
        }

        [Test]
        public void test_close_steps_constructor_takes_no_transport_disconnector()
        {
            foreach (System.Reflection.ConstructorInfo ctor in typeof(ZoneCloseSteps).GetConstructors())
            {
                foreach (System.Reflection.ParameterInfo parameter in ctor.GetParameters())
                {
                    Assert.IsFalse(typeof(IInboundTransportDisconnector).IsAssignableFrom(parameter.ParameterType),
                        "ZoneCloseSteps must not be able to close a connection: " + parameter.Name);
                }
            }
        }

        [Test]
        public void test_respawning_player_is_saved_with_hp_override_zero()
        {
            P a = AddPlayer(1u, ZoneSessionState.Respawning);

            Steps().RunCloseSteps();

            Assert.AreEqual(1, _persistence.Calls.Count);
            Assert.IsTrue(_persistence.Calls[0].HasOverride);
            Assert.AreEqual(0f, _persistence.Calls[0].HpOverride);
            CompleteSave(a);
            Drain();
            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
        }

        [Test]
        public void test_saves_complete_on_another_thread_during_drain_and_notify_is_delivered_inside_it()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);
            Steps().RunCloseSteps();
            Assert.AreEqual(0, _party.Exits.Count);

            var completer = new Thread(() =>
            {
                CompleteSave(b);
                CompleteSave(a);
            });
            completer.Start();
            Drain();
            completer.Join();

            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
            Assert.AreEqual(1, _party.ExitsFor(b.Entity));
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.AreEqual(2, _party.ExitThreadIds.Count);
            foreach (int id in _party.ExitThreadIds)
            {
                Assert.AreEqual(Thread.CurrentThread.ManagedThreadId, id, "callbacks run on the draining (tick) thread");
            }
        }

        // ----- AC-ZI-31 / EC-ZI-15 --------------------------------------------------------------

        [Test]
        public void test_entering_player_is_not_saved_and_is_notified_before_the_drain_while_acked_waits_for_its_save()
        {
            P acked = AddPlayer(1u);
            P entering = AddPlayer(2u);
            _reclamation.RegisterEntering(entering.Client, entering.Entity);

            Steps().RunCloseSteps();

            Assert.AreEqual(1, _persistence.Calls.Count);
            Assert.AreEqual(acked.Character, _persistence.Calls[0].Character);
            Assert.AreEqual(0, _persistence.CallsFor(entering.Character));
            Assert.AreEqual(1, _party.ExitsFor(entering.Entity), "entering slot is notified at once");
            Assert.AreEqual(0, _party.ExitsFor(acked.Entity), "acked slot waits for its save");

            CompleteSave(acked);
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(acked.Entity));
            Assert.AreEqual(1, _party.ExitsFor(entering.Entity));
            Assert.AreEqual(1, _outbox.CountFor(acked.Client));
            Assert.AreEqual(1, _outbox.CountFor(entering.Client));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_slot_already_reclaimed_by_stall_handling_is_skipped()
        {
            P stalled = AddPlayer(1u);
            P live = AddPlayer(2u);
            _reclamation.RegisterEntering(stalled.Client, stalled.Entity);
            _reclamation.OnTick(100u + _reclamation.StallTicks);
            Assert.IsFalse(_pool.TryGetPlayer(stalled.Entity, out _), "stall reclamation freed the slot");
            int stalledExitsBefore = _party.ExitsFor(stalled.Entity);

            Steps().RunCloseSteps();

            Assert.AreEqual(1, _persistence.Calls.Count);
            Assert.AreEqual(live.Character, _persistence.Calls[0].Character);
            Assert.AreEqual(0, _outbox.CountFor(stalled.Client));
            Assert.AreEqual(stalledExitsBefore, _party.ExitsFor(stalled.Entity), "no second notification for the reclaimed slot");
            CollectionAssert.DoesNotContain(_sink.Expired, stalled.Client);
        }

        // ----- AC-ZI-23 (teardown side) ---------------------------------------------------------

        [Test]
        public void test_ghost_gets_one_snapshot_save_no_session_save_nothing_sent_and_notify_only_after_snapshot_completes()
        {
            P ghost = AddPlayer(1u, ZoneSessionState.Disconnected_SessionActive);
            P noticed = AddPlayer(2u);
            _drainLog.Notified.Add(noticed.Client);

            Steps().RunCloseSteps();

            Assert.AreEqual(1, _sink.GhostSaves.Count);
            Assert.AreEqual(ghost.Character, _sink.GhostSaves[0].Key);
            Assert.AreEqual(ghost.Client, _sink.GhostSaves[0].Value);
            Assert.AreEqual(0, _persistence.CallsFor(ghost.Character), "no SaveSession for a ghost");
            Assert.AreEqual(0, _outbox.AllFor(ghost.Client), "nothing of any type sent to a ghost");
            Assert.AreEqual(0, _outbox.AllFor(noticed.Client), "no second ZoneSessionEnded after the T-4 notice");
            Assert.AreEqual(0, _outbox.All.Count, "no message at all: the ghost is skipped and the other client was already told");
            CollectionAssert.AreEqual(new[] { noticed.Client }, _sink.Expired, "expired through the sink, the ghost is not");
            Assert.AreEqual(0, _party.ExitsFor(ghost.Entity), "not before the snapshot save completes");

            _sink.Sources[ghost.Character].SetResult(CharacterSaveResult.Success);
            CompleteSave(noticed);
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(ghost.Entity));
            Assert.AreEqual(1, _party.ExitsFor(noticed.Entity));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_every_non_ghost_slot_is_expired_through_the_sink_even_when_already_noticed()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u, ZoneSessionState.Dead);
            _drainLog.Notified.Add(a.Client);

            Steps().RunCloseSteps();

            CollectionAssert.AreEquivalent(new[] { a.Client, b.Client }, _sink.Expired);
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(b.Client, _outbox.Messages[0].Client);
        }

        // ----- ordering -------------------------------------------------------------------------

        [Test]
        public void test_saves_completing_in_reverse_order_notify_in_that_order_and_none_before_its_own_save()
        {
            var queue = new ManualQueue();
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);
            P c = AddPlayer(3u);

            Steps(queue).RunCloseSteps();

            Assert.AreEqual(3, queue.Callbacks.Count);
            Assert.AreEqual(0, _party.Exits.Count);
            // Callbacks were tracked in slot order (a, b, c); deliver them c, b, a.
            P[] reverse = { c, b, a };
            for (int i = 0; i < reverse.Length; i++)
            {
                Assert.AreEqual(0, _party.ExitsFor(reverse[i].Entity), "no notification before its own save completes");
                queue.Deliver(2 - i, CharacterSaveResult.Success);
                Assert.AreEqual(i + 1, _party.Exits.Count);
                Assert.AreEqual(reverse[i].Entity, _party.Exits[i].Entity);
            }
        }

        [Test]
        public void test_save_completing_with_failure_still_notifies_and_logs_one_error()
        {
            P a = AddPlayer(1u);
            Steps().RunCloseSteps();

            CompleteSave(a, CharacterSaveResult.DatabaseError);
            LogAssert.Expect(LogType.Error, Rx(@"SaveSession for .* did not succeed \(DatabaseError\)"));
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
        }

        [Test]
        public void test_save_that_faults_still_notifies_and_logs_one_error()
        {
            P a = AddPlayer(1u);
            Steps().RunCloseSteps();

            _persistence.Sources[a.Character].SetException(new InvalidOperationException("db down"));
            LogAssert.Expect(LogType.Error, Rx(@"did not succeed \(Faulted\)"));
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
        }

        [Test]
        public void test_save_that_is_cancelled_still_notifies_and_logs_one_error()
        {
            P a = AddPlayer(1u);
            Steps().RunCloseSteps();

            _persistence.Sources[a.Character].SetCanceled();
            LogAssert.Expect(LogType.Error, Rx(@"did not succeed \(Canceled\)"));
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
        }

        [Test]
        public void test_save_that_never_completes_gets_no_notify_while_the_other_does()
        {
            P stuck = AddPlayer(1u);
            P done = AddPlayer(2u);
            Steps().RunCloseSteps();

            CompleteSave(done);
            LogAssert.Expect(LogType.Error, Rx(@"still incomplete at shutdown"));
            _queue.DrainOnShutdown(TimeSpan.FromMilliseconds(50));

            Assert.AreEqual(0, _party.ExitsFor(stuck.Entity));
            Assert.AreEqual(1, _party.ExitsFor(done.Entity));
        }

        [Test]
        public void test_player_not_in_a_formal_party_is_saved_but_never_notified()
        {
            P solo = AddPlayer(1u, ZoneSessionState.Connected, inParty: false);
            Steps().RunCloseSteps();

            CompleteSave(solo);
            Drain();

            Assert.AreEqual(1, _persistence.CallsFor(solo.Character));
            Assert.AreEqual(0, _party.Exits.Count);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        // ----- EC-ZI-11 -------------------------------------------------------------------------

        [Test]
        public void test_slot_released_before_teardown_gets_no_save_message_or_notify()
        {
            P gone = AddPlayer(1u);
            P stays = AddPlayer(2u);
            Assert.IsTrue(_pool.ReleasePlayer(gone.Entity));
            _directory.Remove(gone.Client);

            Steps().RunCloseSteps();
            CompleteSave(stays);
            Drain();

            Assert.AreEqual(0, _persistence.CallsFor(gone.Character));
            Assert.AreEqual(0, _outbox.CountFor(gone.Client));
            Assert.AreEqual(0, _party.ExitsFor(gone.Entity));
            Assert.AreEqual(1, _party.ExitsFor(stays.Entity));
        }

        // ----- run once / throw policy ----------------------------------------------------------

        [Test]
        public void test_second_run_makes_no_calls_on_any_fake()
        {
            AddPlayer(1u);
            AddPlayer(2u, ZoneSessionState.Disconnected_SessionActive);
            ZoneCloseSteps steps = Steps();
            steps.RunCloseSteps();
            int saves = _persistence.Calls.Count;
            int ghosts = _sink.GhostSaves.Count;
            int expired = _sink.Expired.Count;
            int messages = _outbox.Messages.Count;
            int timers = _timers.Cancelled.Count;
            int inFlight = _queue.InFlightCount;

            steps.RunCloseSteps();

            Assert.AreEqual(saves, _persistence.Calls.Count);
            Assert.AreEqual(ghosts, _sink.GhostSaves.Count);
            Assert.AreEqual(expired, _sink.Expired.Count);
            Assert.AreEqual(messages, _outbox.Messages.Count);
            Assert.AreEqual(timers, _timers.Cancelled.Count);
            Assert.AreEqual(inFlight, _queue.InFlightCount);
            Assert.AreEqual(0, _party.Exits.Count);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _directory.Count);
        }

        [Test]
        public void test_track_that_throws_for_one_character_logs_once_notifies_that_one_at_once_and_tracks_the_rest()
        {
            var queue = new ThrowingTrackQueue { ThrowOnCall = 2 };
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);
            P c = AddPlayer(3u);

            LogAssert.Expect(LogType.Error, Rx(@"step 2: SaveSession for .* threw InvalidOperationException"));
            Steps(queue).RunCloseSteps();

            Assert.AreEqual(3, _persistence.Calls.Count, "every character was saved");
            Assert.AreEqual(2, queue.Callbacks.Count, "the other two saves are tracked");
            Assert.AreEqual(1, _party.Exits.Count, "only the character whose Track failed, before any drain");
            Assert.AreEqual(1, _party.ExitsFor(b.Entity));
            Assert.AreEqual(0, _party.ExitsFor(a.Entity));
            Assert.AreEqual(0, _party.ExitsFor(c.Entity));

            queue.Deliver(0, CharacterSaveResult.Success);
            queue.Deliver(1, CharacterSaveResult.Success);
            Assert.AreEqual(3, _party.Exits.Count);
            Assert.AreEqual(1, _party.ExitsFor(a.Entity));
            Assert.AreEqual(1, _party.ExitsFor(b.Entity));
            Assert.AreEqual(1, _party.ExitsFor(c.Entity));
        }

        [Test]
        public void test_slot_that_is_both_ghost_and_entering_is_treated_as_a_ghost()
        {
            P both = AddPlayer(1u, ZoneSessionState.Disconnected_SessionActive);
            _reclamation.RegisterEntering(both.Client, both.Entity);

            Steps().RunCloseSteps();

            Assert.AreEqual(1, _sink.GhostSaves.Count);
            Assert.AreEqual(0, _persistence.CallsFor(both.Character));
            Assert.AreEqual(0, _outbox.AllFor(both.Client));
            Assert.AreEqual(0, _party.ExitsFor(both.Entity), "not before the snapshot completes");

            _sink.Sources[both.Character].SetResult(CharacterSaveResult.Success);
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(both.Entity));
        }

        [Test]
        public void test_expire_that_throws_for_one_client_logs_once_and_the_next_slot_is_still_messaged_and_expired()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);
            _sink.ThrowOnExpire.Add(a.Client);

            LogAssert.Expect(LogType.Error, Rx(@"step 4 failed for client 1 \(InvalidOperationException\)"));
            Steps().RunCloseSteps();

            Assert.AreEqual(1, _outbox.CountFor(b.Client));
            CollectionAssert.AreEqual(new[] { b.Client }, _sink.Expired);
        }

        [Test]
        public void test_party_lookup_that_throws_for_one_character_logs_once_and_loses_only_its_notify()
        {
            P bad = AddPlayer(1u);
            P good = AddPlayer(2u);
            _party.ThrowOnLookup.Add(bad.Character);

            LogAssert.Expect(LogType.Error, Rx(@"step 5 failed for .* \(InvalidOperationException\)"));
            Steps().RunCloseSteps();
            CompleteSave(bad);
            CompleteSave(good);
            Drain();

            Assert.AreEqual(0, _party.ExitsFor(bad.Entity));
            Assert.AreEqual(1, _party.ExitsFor(good.Entity));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_throwing_mob_timers_log_one_error_and_later_steps_still_run()
        {
            P a = AddPlayer(1u);
            AddMobs(2);
            _timers.Throw = true;

            LogAssert.Expect(LogType.Error, Rx(@"step 1 failed \(InvalidOperationException\)"));
            Steps().RunCloseSteps();

            Assert.AreEqual(1, _persistence.CallsFor(a.Character));
            Assert.AreEqual(1, _outbox.CountFor(a.Client));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _pool.OccupiedMobCount);
        }

        [Test]
        public void test_save_that_throws_synchronously_logs_once_notifies_at_once_and_others_are_still_saved()
        {
            P bad = AddPlayer(1u);
            P good = AddPlayer(2u);
            _persistence.ThrowFor.Add(bad.Character);

            LogAssert.Expect(LogType.Error, Rx(@"step 2: SaveSession for .* threw InvalidOperationException"));
            Steps().RunCloseSteps();

            Assert.AreEqual(1, _persistence.CallsFor(good.Character));
            Assert.AreEqual(1, _party.ExitsFor(bad.Entity), "notified at once in step 5");
            Assert.AreEqual(0, _party.ExitsFor(good.Entity));

            CompleteSave(good);
            Drain();

            Assert.AreEqual(1, _party.ExitsFor(bad.Entity), "still exactly one");
            Assert.AreEqual(1, _party.ExitsFor(good.Entity));
        }

        [Test]
        public void test_ghost_snapshot_that_throws_synchronously_logs_once_and_notifies_at_once()
        {
            P ghost = AddPlayer(1u, ZoneSessionState.Disconnected_SessionActive);
            _sink.ThrowOnGhostSave = true;

            LogAssert.Expect(LogType.Error, Rx(@"step 3: SaveGhostSnapshotAndExpire for .* threw InvalidOperationException"));
            Steps().RunCloseSteps();

            Assert.AreEqual(1, _party.ExitsFor(ghost.Entity));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_notify_zone_exit_throwing_for_one_character_does_not_stop_the_others()
        {
            P a = AddPlayer(1u);
            P b = AddPlayer(2u);
            _party.ThrowOnExit.Add(a.Entity);
            Steps().RunCloseSteps();

            CompleteSave(a);
            CompleteSave(b);
            LogAssert.Expect(LogType.Error, Rx(@"NotifyZoneExit for .* threw InvalidOperationException"));
            Drain();

            Assert.AreEqual(0, _party.ExitsFor(a.Entity));
            Assert.AreEqual(1, _party.ExitsFor(b.Entity));
        }

        [Test]
        public void test_notify_zone_exit_throwing_in_step_5_for_an_entering_slot_does_not_stop_the_others()
        {
            P entering = AddPlayer(1u);
            P other = AddPlayer(2u);
            _reclamation.RegisterEntering(entering.Client, entering.Entity);
            _reclamation.RegisterEntering(other.Client, other.Entity);
            _party.ThrowOnExit.Add(entering.Entity);

            LogAssert.Expect(LogType.Error, Rx(@"step 5 failed for .* \(InvalidOperationException\)"));
            Steps().RunCloseSteps();

            Assert.AreEqual(1, _party.ExitsFor(other.Entity));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount, "step 6 still ran");
        }

        // ----- slots, directory, roster, retirement ---------------------------------------------

        [Test]
        public void test_all_slots_directory_and_roster_are_empty_afterwards_and_retirement_is_logged_once()
        {
            AddPlayer(1u);
            AddPlayer(2u, ZoneSessionState.Dead);
            AddPlayer(3u, ZoneSessionState.Disconnected_SessionActive);
            AddMobs(5);
            Assert.AreEqual(3, _directory.Count);

            LogAssert.Expect(LogType.Log, "[ZoneInstancing] zone " + ZONE_RAW + " retired");
            ZoneCloseSteps steps = Steps();
            steps.RunCloseSteps();
            steps.RunCloseSteps();

            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _pool.OccupiedMobCount);
            Assert.AreEqual(0, _directory.Count);
            Assert.AreEqual(0, _roster.PendingCount);
            Assert.AreEqual(0, _roster.ActiveCount);
            Assert.AreEqual(1, _timers.Cancelled.Count);
            Assert.AreEqual(Zone, _timers.Cancelled[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_constructor_rejects_null_arguments_and_invalid_zone()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneCloseSteps(null, _directory, _roster, _reclamation, _persistence, _queue,
                _party, _timers, _sink, _drainLog, _outbox, Zone));
            Assert.Throws<ArgumentNullException>(() => new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, _persistence, null,
                _party, _timers, _sink, _drainLog, _outbox, Zone));
            Assert.Throws<ArgumentNullException>(() => new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, _persistence, _queue,
                _party, _timers, _sink, null, _outbox, Zone));
            Assert.Throws<ArgumentException>(() => new ZoneCloseSteps(_pool, _directory, _roster, _reclamation, _persistence, _queue,
                _party, _timers, _sink, _drainLog, _outbox, ZoneID.Invalid));
        }

        // ----- stand-ins ------------------------------------------------------------------------

        [Test]
        public void test_logging_stand_ins_log_once_and_return_safe_defaults()
        {
            var timers = new LoggingZoneMobSpawnTimers();
            var sink = new LoggingZoneTeardownSessionSink();

            LogAssert.Expect(LogType.Log, "[ZoneServer] mob spawn timers not implemented");
            timers.CancelAllSpawnTimers(Zone);
            timers.CancelAllSpawnTimers(Zone);

            LogAssert.Expect(LogType.Log, "[ZoneServer] teardown session sink (TD-079) not implemented");
            Task<CharacterSaveResult> save = sink.SaveGhostSnapshotAndExpire(new CharacterID(5u), 1u);
            sink.ExpireConnectedSession(1u);

            Assert.IsTrue(save.IsCompleted);
            Assert.AreEqual(CharacterSaveResult.Success, save.Result);
            Assert.IsFalse(new NoZoneDrainNoticeLog().WasNotifiedAtDrain(1u));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
