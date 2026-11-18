using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.CharacterPersistence;
using IronGrind.Tests.EditMode.EquipmentSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// EditMode tests for Death and Respawn Story 010: ghost-death session end (AC-1 to AC-7; CR-GH-6, CR-GH-7,
    /// GD-CGS-2). Real <see cref="ConnectionStateMachine"/>, <see cref="TickCompletionQueue"/>,
    /// <see cref="CharacterMutationGate"/> and <see cref="ZoneSlotPool"/>; the persistence fake completes saves
    /// by hand.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_GhostSessionEnd_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint ACCOUNT = 7u;
        private const uint CHARACTER = 101u;
        private const uint CLIENT = 5u;
        private const uint CONNECT_TICK = 100u;
        private const uint TIMEOUT_TICK = 300u;
        private const uint HEARTBEAT_TICKS = 60u;
        private const uint CONNECTING_TICKS = 200u;
        private const uint UNKNOWN_ACCOUNT = 99u;
        private const uint UNKNOWN_ENTITY_RAW = 900000u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int SHORT_WATCHDOG_TICKS = 3;
        private const int REAUTH_LIMIT = 3;
        private const float SNAPSHOT_HP = 42f;
        private const float EFFECTIVE_MAX_HP = 777f;
        private const string TRIGGER_GHOST_DEATH = "GhostDeath";

        private static readonly CharacterID CHAR = new CharacterID(CHARACTER);
        private static readonly Regex WATCHDOG_ALERT = new Regex("CRITICAL infrastructure alert");
        private static readonly Regex SAVE_NOT_SUCCEEDED = new Regex("Ghost-death save for .* did not succeed");
        private static readonly Regex NOT_ACCEPTED_STATE = new Regex("Ghost death for account .* ignored");
        private static readonly Regex IN_FLIGHT = new Regex("already in flight");
        private static readonly Regex RECONNECTED = new Regex("player reconnected");
        private static readonly Regex NO_SLOT = new Regex("has no player slot");
        private static readonly Regex GATE_RECONNECTED =
            new Regex("waiting on the mutation gate but the player reconnected");
        private static readonly Regex GATE_SESSION_GONE =
            new Regex("waiting on the mutation gate but the session is no longer");
        private static readonly Regex RELEASE_THREW = new Regex("Release after ghost death for .* threw");
        private static readonly Regex SESSION_GONE_AT_COMPLETION =
            new Regex("finished but the session is not .* no release step was run");
        private static readonly Regex EXPIRED_HOOK_THREW = new Regex("ghost-expired broadcast for account .* threw");

        private sealed class FakeServerTicks : IServerTickSource
        {
            public uint ServerTickNumber => 0u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeHpSource : IPreDisconnectHpSource
        {
            public bool Has;
            public float Hp;
            public int Calls;
            public EntityID LastEntity;

            public bool TryGetHp(EntityID entity, out float hp)
            {
                Calls++;
                LastEntity = entity;
                hp = Hp;
                return Has;
            }
        }

        private sealed class RecordingLink : IGatewayLinkOutbox
        {
            public readonly List<(uint RequestId, bool Accepted)> Results = new List<(uint, bool)>();

            public void EnqueueAdmitResult(uint requestId, bool accepted, AdmitRejectReason reason) { }

            public void EnqueueSessionReleased(AccountID account) { }

            public void EnqueueEvictResult(uint requestId) { }

            public void EnqueueReconnectResult(uint requestId, bool accepted, byte[] newToken) =>
                Results.Add((requestId, accepted));

            public void EnqueuePlayerCount(ushort count) { }

            public void EnqueueZoneDraining() { }
        }

        // Saves are completed by hand; each call is recorded with the hp it was given (null: the no-override overload).
        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            private readonly List<string> _log;

            public FakePersistence(List<string> log)
            {
                _log = log;
            }

            public bool ThrowOnRelease;

            public readonly List<(SessionEndReason Reason, float? Hp, TaskCompletionSource<CharacterSaveResult> Source)>
                Saves = new List<(SessionEndReason, float?, TaskCompletionSource<CharacterSaveResult>)>();

            public Task<CharacterSaveResult> SaveSession(
                CharacterID id, SessionEndReason reason, CancellationToken ct) =>
                Record(reason, null);

            public Task<CharacterSaveResult> SaveSession(
                CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) =>
                Record(reason, hpOverride);

            private Task<CharacterSaveResult> Record(SessionEndReason reason, float? hp)
            {
                var source = new TaskCompletionSource<CharacterSaveResult>();
                Saves.Add((reason, hp, source));
                return source.Task;
            }

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                _log.Add("release");
                if (ThrowOnRelease)
                {
                    throw new InvalidOperationException("release failed");
                }

                entityId = EntityID.Invalid;
                return false;
            }

            public Task<CharacterLoadResult> LoadCharacter(
                CharacterID id, AccountID ownerAccountID, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(
                CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task CreateStub(
                AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(
                CharacterID charId,
                IronGrind.CharacterPersistence.PendingPurchaseRecord record,
                uint goldBalance,
                uint goldVersion,
                CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(
                long purchaseId, CharacterID charId, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(
                long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(
                CharacterID charId, CancellationToken ct) =>
                throw new NotSupportedException();
        }

        private List<string> _log;
        private FakePersistence _persist;
        private TickCompletionQueue _queue;
        private CharacterMutationGate _gate;
        private ConnectionStateMachine _sm;
        private ZoneSlotPool _pool;
        private FakeHpSource _hp;
        private SessionEndCoordinator _ends;
        private NetworkTestObserver _observer;
        private EntityID _entity;
        private uint _tick;
        private readonly List<(uint Account, DisconnectReason Reason)> _sent =
            new List<(uint, DisconnectReason)>();

        // Recorded by the chain delegates (null: the delegate has not run).
        private bool? _sessionGoneAtRemove;
        private bool? _markSetAtRemove;
        private bool? _markSetAtExpired;
        private bool _throwOnExpired;
        private int _maxHpCalls;
        private EntityID _maxHpEntity;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _persist = new FakePersistence(_log);
            _gate = new CharacterMutationGate();
            _sm = new ConnectionStateMachine();
            _hp = new FakeHpSource();
            _pool = new ZoneSlotPool(
                new ZoneID(ZONE_RAW), new FakeServerTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            Assert.IsTrue(_pool.TryAllocatePlayer(
                new ZoneID(ZONE_RAW), CHAR, new AccountID(ACCOUNT), CLIENT, out _, out _entity));
            _tick = 0u;
            _sent.Clear();
            _sessionGoneAtRemove = null;
            _markSetAtRemove = null;
            _markSetAtExpired = null;
            _throwOnExpired = false;
            _maxHpCalls = 0;
            _maxHpEntity = default;
            BuildEnds(LONG_WATCHDOG_TICKS, withOptionals: true);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        // withExpired / withDeTarget only matter when withOptionals is true.
        private void BuildEnds(
            int watchdogTicks, bool withOptionals, bool withExpired = true, bool withDeTarget = true)
        {
            _queue = new TickCompletionQueue(watchdogTicks);
            _ends = new SessionEndCoordinator(
                _persist,
                _persist,
                _queue,
                _gate,
                _sm,
                new RecordingInventory(_log),
                new RecordingEquipmentService(),
                _ => { },
                _ => { },
                rawChar =>
                {
                    _log.Add("remove");
                    _sessionGoneAtRemove = !_sm.TryGetSessionState(ACCOUNT, out _);
                    _markSetAtRemove = _sm.HasGhostDeathMark(ACCOUNT);
                },
                (rawChar, type) =>
                {
                    _log.Add($"left:{type}");
                    _pool.ReleasePlayer(_entity);
                },
                null,
                withOptionals ? _hp : null,
                withOptionals
                    ? (Func<EntityID, float>)(e =>
                    {
                        _maxHpCalls++;
                        _maxHpEntity = e;
                        return EFFECTIVE_MAX_HP;
                    })
                    : null,
                withOptionals && withExpired
                    ? (Action<uint, string>)((rawChar, what) =>
                    {
                        if (_throwOnExpired)
                        {
                            throw new InvalidOperationException("expired hook failed");
                        }

                        _log.Add($"expired:{what}");
                        _markSetAtExpired = _sm.HasGhostDeathMark(ACCOUNT);
                    })
                    : null,
                withOptionals && withDeTarget ? (Action<EntityID>)(e => _log.Add("detarget")) : null,
                withOptionals
                    ? (Action<uint, DisconnectReason>)((account, reason) => _sent.Add((account, reason)))
                    : null);
            _observer = new NetworkTestObserver();
            _ends.TestObserver = _observer;
        }

        private void DrainNextTick()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private void ConnectAccount()
        {
            _sm.EnterConnecting(ACCOUNT, CONNECT_TICK);
            _sm.CompleteAuthSuccess(ACCOUNT, CHARACTER, CONNECT_TICK);
        }

        private void DisconnectAccount()
        {
            ConnectAccount();
            _sm.EvaluateTimeouts(TIMEOUT_TICK, HEARTBEAT_TICKS, CONNECTING_TICKS);
            AssertState(SessionState.Disconnected_SessionActive);
        }

        private void MoveToReconnecting()
        {
            Assert.IsTrue(_sm.TryGetSessionExpiryTick(ACCOUNT, out uint expiry));
            _sm.EnterReconnecting(ACCOUNT, expiry);
            AssertState(SessionState.Reconnecting);
        }

        // Removes the Disconnected_SessionActive session through the state machine's own TTL-expiry completion.
        private void RemoveSessionBehindTheEnd()
        {
            Assert.IsTrue(_sm.TryGetSessionExpiryTick(ACCOUNT, out uint expiry));
            _sm.CompleteSessionActiveTTLExpiry(ACCOUNT, expiry, expiry, _ => { }, _ => { }, (_, __) => { });
            Assert.IsFalse(_sm.TryGetSessionState(ACCOUNT, out _));
        }

        private void AssertState(SessionState expected)
        {
            Assert.IsTrue(_sm.TryGetSessionState(ACCOUNT, out SessionState actual));
            Assert.AreEqual(expected, actual);
        }

        private void CompleteSave(CharacterSaveResult result = CharacterSaveResult.Success)
        {
            _persist.Saves[_persist.Saves.Count - 1].Source.SetResult(result);
            DrainNextTick();
        }

        private static readonly string[] FULL_CHAIN =
            { "release", "remove", "left:Timeout", "expired:GhostDeath", "detarget" };

        private static bool IsChainStep(string s) =>
            s == "release" || s == "remove" || s.StartsWith("left:") || s.StartsWith("expired:") || s == "detarget";

        // ----- AC-1 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_disconnected_session_active_starts_one_ghost_death_save()
        {
            DisconnectAccount();

            bool started = _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.IsTrue(started);
            Assert.AreEqual(1, _persist.Saves.Count);
            Assert.AreEqual(SessionEndReason.GhostDeath, _persist.Saves[0].Reason);
            Assert.IsTrue(_ends.IsEndInFlight(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_reconnecting_starts_one_ghost_death_save()
        {
            DisconnectAccount();
            MoveToReconnecting();

            bool started = _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.IsTrue(started);
            Assert.AreEqual(1, _persist.Saves.Count);
            Assert.AreEqual(SessionEndReason.GhostDeath, _persist.Saves[0].Reason);
        }

        [Test]
        public void test_ghost_session_end_connected_account_returns_false_with_one_warning()
        {
            ConnectAccount();
            LogAssert.Expect(LogType.Warning, NOT_ACCEPTED_STATE);

            bool started = _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.IsFalse(started);
            Assert.AreEqual(0, _persist.Saves.Count);
        }

        [Test]
        public void test_ghost_session_end_unknown_account_returns_false_with_one_warning()
        {
            LogAssert.Expect(LogType.Warning, NOT_ACCEPTED_STATE);

            bool started = _ends.BeginGhostDeath(UNKNOWN_ACCOUNT, _entity);

            Assert.IsFalse(started);
            Assert.AreEqual(0, _persist.Saves.Count);
        }

        [Test]
        public void test_ghost_session_end_end_already_in_flight_returns_false_with_one_warning()
        {
            DisconnectAccount();
            Assert.IsTrue(_ends.BeginGhostDeath(ACCOUNT, _entity));
            LogAssert.Expect(LogType.Warning, IN_FLIGHT);

            bool second = _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.IsFalse(second);
            Assert.AreEqual(1, _persist.Saves.Count);
        }

        [Test]
        public void test_ghost_session_end_gate_held_waits_until_opened_then_saves_once()
        {
            DisconnectAccount();
            _gate.Close(CHAR);

            bool started = _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.IsTrue(started);
            Assert.AreEqual(0, _persist.Saves.Count);
            Assert.IsTrue(_ends.IsEndInFlight(ACCOUNT));

            _gate.Open(CHAR);

            Assert.AreEqual(1, _persist.Saves.Count);
            Assert.AreEqual(SessionEndReason.GhostDeath, _persist.Saves[0].Reason);
            Assert.AreEqual(EFFECTIVE_MAX_HP, _persist.Saves[0].Hp, "no snapshot: the effective MaxHP is written");
        }

        [Test]
        public void test_ghost_session_end_gate_held_snapshot_set_before_open_writes_snapshot_hp()
        {
            DisconnectAccount();
            _gate.Close(CHAR);
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            _hp.Has = true;
            _hp.Hp = SNAPSHOT_HP;

            _gate.Open(CHAR);

            Assert.AreEqual(1, _persist.Saves.Count);
            Assert.AreEqual(SNAPSHOT_HP, _persist.Saves[0].Hp, "the HP is computed at save start, not at the request");
        }

        [Test]
        public void test_ghost_session_end_gate_held_account_reconnected_before_open_warns_and_saves_nothing()
        {
            DisconnectAccount();
            _gate.Close(CHAR);
            Assert.IsTrue(_ends.BeginGhostDeath(ACCOUNT, _entity));
            ConnectAccount();
            LogAssert.Expect(LogType.Warning, GATE_RECONNECTED);

            _gate.Open(CHAR);

            Assert.AreEqual(0, _persist.Saves.Count);
            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_gate_held_session_gone_before_open_logs_error_and_saves_nothing()
        {
            DisconnectAccount();
            _gate.Close(CHAR);
            Assert.IsTrue(_ends.BeginGhostDeath(ACCOUNT, _entity));
            RemoveSessionBehindTheEnd();
            LogAssert.Expect(LogType.Error, GATE_SESSION_GONE);

            _gate.Open(CHAR);

            Assert.AreEqual(0, _persist.Saves.Count);
            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
        }

        // ----- AC-2 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_snapshot_hp_is_written_as_override()
        {
            DisconnectAccount();
            _hp.Has = true;
            _hp.Hp = SNAPSHOT_HP;

            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.AreEqual(SNAPSHOT_HP, _persist.Saves[0].Hp);
            Assert.AreEqual(_entity, _hp.LastEntity, "the snapshot is looked up for the ghost entity");
        }

        [Test]
        public void test_ghost_session_end_snapshot_hp_zero_is_written_as_zero_without_max_hp_lookup()
        {
            DisconnectAccount();
            _hp.Has = true;
            _hp.Hp = 0f;

            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.AreEqual(0f, _persist.Saves[0].Hp);
            Assert.AreEqual(0, _maxHpCalls, "a present snapshot of 0 must not fall back to the effective MaxHP");
        }

        [Test]
        public void test_ghost_session_end_no_snapshot_writes_effective_max_hp()
        {
            DisconnectAccount();
            _hp.Has = false;

            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.AreEqual(EFFECTIVE_MAX_HP, _persist.Saves[0].Hp);
            Assert.AreEqual(1, _maxHpCalls);
            Assert.AreEqual(_entity, _maxHpEntity, "the effective MaxHP is looked up for the ghost entity");
        }

        [Test]
        public void test_ghost_session_end_no_snapshot_and_no_max_hp_func_uses_no_override_save()
        {
            BuildEnds(LONG_WATCHDOG_TICKS, withOptionals: false);
            DisconnectAccount();

            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.AreEqual(1, _persist.Saves.Count);
            Assert.IsNull(_persist.Saves[0].Hp);
        }

        // ----- AC-3 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_disconnected_completion_runs_chain_in_order()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);

            CompleteSave();

            CollectionAssert.AreEqual(FULL_CHAIN, _log.FindAll(IsChainStep));
            Assert.AreEqual((bool?)true, _sessionGoneAtRemove, "the session record is gone before the zone removal");
            Assert.AreEqual((bool?)false, _markSetAtRemove, "the mark is set after the release, not before");
            Assert.AreEqual((bool?)true, _markSetAtExpired, "the mark is set before the expired broadcast");
            Assert.IsFalse(_sm.TryGetSessionState(ACCOUNT, out _), "the session record is gone after the transition");
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount, "the zone slot is freed");
            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
            CollectionAssert.Contains(
                _observer.SessionStateTransitionedCalls,
                (ACCOUNT, SessionState.Disconnected_SessionActive, SessionState.Disconnected_SessionExpired,
                    TRIGGER_GHOST_DEATH));
        }

        [Test]
        public void test_ghost_session_end_optional_delegates_null_runs_rest_of_chain()
        {
            BuildEnds(LONG_WATCHDOG_TICKS, withOptionals: false);
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.DoesNotThrow(() => CompleteSave());

            CollectionAssert.AreEqual(new[] { "release", "remove", "left:Timeout" }, _log.FindAll(IsChainStep));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
        }

        [Test]
        public void test_ghost_session_end_expired_hook_only_runs_without_detarget()
        {
            BuildEnds(LONG_WATCHDOG_TICKS, withOptionals: true, withExpired: true, withDeTarget: false);
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.DoesNotThrow(() => CompleteSave());

            CollectionAssert.AreEqual(
                new[] { "release", "remove", "left:Timeout", "expired:GhostDeath" }, _log.FindAll(IsChainStep));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_detarget_hook_only_runs_without_expired()
        {
            BuildEnds(LONG_WATCHDOG_TICKS, withOptionals: true, withExpired: false, withDeTarget: true);
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);

            Assert.DoesNotThrow(() => CompleteSave());

            CollectionAssert.AreEqual(
                new[] { "release", "remove", "left:Timeout", "detarget" }, _log.FindAll(IsChainStep));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_throwing_expired_hook_logs_error_and_still_detargets()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            _throwOnExpired = true;
            LogAssert.Expect(LogType.Error, EXPIRED_HOOK_THREW);

            CompleteSave();

            CollectionAssert.AreEqual(
                new[] { "release", "remove", "left:Timeout", "detarget" }, _log.FindAll(IsChainStep));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
        }

        // ----- AC-4 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_failed_save_logs_error_and_still_runs_chain()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            LogAssert.Expect(LogType.Error, SAVE_NOT_SUCCEEDED);

            CompleteSave(CharacterSaveResult.DatabaseError);

            CollectionAssert.AreEqual(FULL_CHAIN, _log.FindAll(IsChainStep));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_timed_out_save_logs_error_and_still_runs_chain()
        {
            BuildEnds(SHORT_WATCHDOG_TICKS, withOptionals: true);
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            LogAssert.Expect(LogType.Error, WATCHDOG_ALERT);
            LogAssert.Expect(LogType.Error, SAVE_NOT_SUCCEEDED);

            for (int i = 0; i < SHORT_WATCHDOG_TICKS; i++)
            {
                DrainNextTick();
            }

            CollectionAssert.AreEqual(FULL_CHAIN, _log.FindAll(IsChainStep));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_release_throws_logs_error_and_still_finishes_end()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            _persist.ThrowOnRelease = true;
            LogAssert.Expect(LogType.Error, RELEASE_THREW);

            CompleteSave();

            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_session_gone_at_completion_logs_error_and_releases_nothing()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            RemoveSessionBehindTheEnd();
            LogAssert.Expect(LogType.Error, SESSION_GONE_AT_COMPLETION);

            CompleteSave();

            Assert.AreEqual(0, _log.FindAll(IsChainStep).Count);
            Assert.IsFalse(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
        }

        // ----- AC-5 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_reconnecting_at_completion_sends_session_ended_once_and_frees_slot()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            MoveToReconnecting();

            CompleteSave();

            Assert.AreEqual(1, _sent.Count);
            Assert.AreEqual((ACCOUNT, DisconnectReason.GhostDeath), _sent[0]);
            CollectionAssert.AreEqual(FULL_CHAIN, _log.FindAll(IsChainStep));
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.IsFalse(_sm.TryGetSessionState(ACCOUNT, out _));
            CollectionAssert.Contains(
                _observer.SessionStateTransitionedCalls,
                (ACCOUNT, SessionState.Reconnecting, SessionState.Disconnected_SessionExpired, TRIGGER_GHOST_DEATH));
        }

        [Test]
        public void test_ghost_session_end_reconnecting_at_completion_with_null_send_delegate_does_not_throw()
        {
            BuildEnds(LONG_WATCHDOG_TICKS, withOptionals: false);
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            MoveToReconnecting();

            Assert.DoesNotThrow(() => CompleteSave());

            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_connected_at_completion_warns_and_releases_nothing()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            ConnectAccount();
            LogAssert.Expect(LogType.Warning, RECONNECTED);

            CompleteSave();

            Assert.AreEqual(0, _log.FindAll(IsChainStep).Count);
            Assert.AreEqual(0, _sent.Count);
            AssertState(SessionState.Connected);
            Assert.IsFalse(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.AreEqual(1, _pool.OccupiedPlayerCount);
        }

        // ----- Eviction joining a ghost-death end (EC-CP-4) -------------------------------------------

        [Test]
        public void test_ghost_session_end_eviction_joins_in_flight_end_and_fires_once_after_chain()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            int fired = 0;
            _ends.BeginEviction(ACCOUNT, () =>
            {
                fired++;
                _log.Add("evicted");
            });
            Assert.AreEqual(0, fired, "the waiter waits for the end in flight");

            CompleteSave();

            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, _persist.Saves.Count, "the account has no session by then: no eviction save");
            Assert.AreEqual("evicted", _log[_log.Count - 1], "the waiter fires after the chain");
            Assert.AreEqual("detarget", _log[_log.Count - 2]);
        }

        [Test]
        public void test_ghost_session_end_eviction_joins_end_that_finds_player_connected_starts_eviction_save()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            int fired = 0;
            _ends.BeginEviction(ACCOUNT, () => fired++);
            ConnectAccount();
            LogAssert.Expect(LogType.Warning, RECONNECTED);

            CompleteSave();

            Assert.AreEqual(0, fired, "the evicted session still exists: the eviction save runs first");
            Assert.AreEqual(2, _persist.Saves.Count);
            Assert.AreEqual(SessionEndReason.Evicted, _persist.Saves[1].Reason);
            Assert.IsTrue(_ends.IsEndInFlight(ACCOUNT));
        }

        // ----- AC-6 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_gateway_reconnect_for_marked_account_rejects_and_consumes_mark()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            CompleteSave();
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));
            var link = new RecordingLink();
            var handler = new GatewayReconnectHandler(_sm, new SessionTokenStore(), _ends, link, REAUTH_LIMIT);

            handler.Handle(1u, new AccountID(ACCOUNT), new byte[SessionTokenStore.TOKEN_LENGTH_BYTES], _tick);

            CollectionAssert.AreEqual(new[] { (1u, false) }, link.Results);
            Assert.IsFalse(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_second_gateway_reconnect_after_mark_consumed_still_rejects()
        {
            DisconnectAccount();
            _ends.BeginGhostDeath(ACCOUNT, _entity);
            CompleteSave();
            var link = new RecordingLink();
            var handler = new GatewayReconnectHandler(_sm, new SessionTokenStore(), _ends, link, REAUTH_LIMIT);
            handler.Handle(1u, new AccountID(ACCOUNT), new byte[SessionTokenStore.TOKEN_LENGTH_BYTES], _tick);

            Assert.DoesNotThrow(() =>
                handler.Handle(2u, new AccountID(ACCOUNT), new byte[SessionTokenStore.TOKEN_LENGTH_BYTES], _tick));

            CollectionAssert.AreEqual(new[] { (1u, false), (2u, false) }, link.Results);
            Assert.IsFalse(_sm.HasGhostDeathMark(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_gateway_reconnect_for_unmarked_account_still_rejects()
        {
            var link = new RecordingLink();
            var handler = new GatewayReconnectHandler(_sm, new SessionTokenStore(), _ends, link, REAUTH_LIMIT);

            handler.Handle(2u, new AccountID(UNKNOWN_ACCOUNT), new byte[SessionTokenStore.TOKEN_LENGTH_BYTES], _tick);

            CollectionAssert.AreEqual(new[] { (2u, false) }, link.Results);
            Assert.IsFalse(_sm.HasGhostDeathMark(UNKNOWN_ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_enter_connecting_clears_ghost_death_mark()
        {
            _sm.MarkGhostDeath(ACCOUNT);
            Assert.IsTrue(_sm.HasGhostDeathMark(ACCOUNT));

            _sm.EnterConnecting(ACCOUNT, CONNECT_TICK);

            Assert.IsFalse(_sm.HasGhostDeathMark(ACCOUNT));
            Assert.IsFalse(_sm.TryConsumeGhostDeathMark(ACCOUNT));
        }

        // ----- AC-7 -----------------------------------------------------------------------------------

        [Test]
        public void test_ghost_session_end_sink_known_entity_begins_end_for_slot_account()
        {
            DisconnectAccount();
            var sink = new SessionEndGhostDeathSink(_pool, _ends);

            sink.OnGhostDied(_entity);

            Assert.AreEqual(1, _persist.Saves.Count);
            Assert.AreEqual(SessionEndReason.GhostDeath, _persist.Saves[0].Reason);
            Assert.IsTrue(_ends.IsEndInFlight(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_sink_unknown_entity_warns_and_starts_nothing()
        {
            DisconnectAccount();
            var sink = new SessionEndGhostDeathSink(_pool, _ends);
            LogAssert.Expect(LogType.Warning, NO_SLOT);

            sink.OnGhostDied(new EntityID(UNKNOWN_ENTITY_RAW));

            Assert.AreEqual(0, _persist.Saves.Count);
            Assert.IsFalse(_ends.IsEndInFlight(ACCOUNT));
        }

        [Test]
        public void test_ghost_session_end_sink_repeated_death_in_flight_starts_no_second_save()
        {
            DisconnectAccount();
            var sink = new SessionEndGhostDeathSink(_pool, _ends);
            sink.OnGhostDied(_entity);
            LogAssert.Expect(LogType.Warning, IN_FLIGHT);

            sink.OnGhostDied(_entity);

            Assert.AreEqual(1, _persist.Saves.Count);
        }

        [Test]
        public void test_ghost_session_end_sink_null_pool_throws_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionEndGhostDeathSink(null, _ends));
        }

        [Test]
        public void test_ghost_session_end_sink_null_ends_throws_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => new SessionEndGhostDeathSink(_pool, null));
        }
    }
}
