using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 008: <c>SaveIrreversibleOutcome</c> and the unknown-version re-read shared with
    /// <c>SaveSession</c> (CR-CP-4, CR-CP-6, CR-CP-7, CR-CP-10, EC-CP-5). Acceptance criteria covered: success for each
    /// trigger, AC-CP-32 (whole record), AC-CP-21 (version at its turn), snapshot before the first await, task completes
    /// only after the store confirmed, AC-CP-19 (conflict, one critical log), store failure, timeout, cancellation,
    /// AC-CP-33 and AC-CP-34 (unknown outcome that did / did not commit), the mark (set, fetch failure, cleared, not set
    /// by a conflict or a queued cancel), unknown character, queue ordering (CR-CP-7), latency log, and "disconnects and
    /// reverts nothing". The only wall-clock elements are the short injected write timeouts (100 ms); the latency
    /// measurement uses an injected clock. Every wait is bounded by 5 s.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_SaveIrreversible_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_UNKNOWN = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;
        private const int WAIT_MS = 5000;
        private const string REREAD_WARNING = @"re-read the stored SaveVersion";

        /// <summary>Wraps a store: the fetch can be made to fault or to return null; every fetch call is counted.</summary>
        private sealed class FetchFaultingStore : ICharacterRecordStore
        {
            private readonly ICharacterRecordStore _inner;

            public FetchFaultingStore(ICharacterRecordStore inner)
            {
                _inner = inner;
            }

            public bool FaultFetches { get; set; }

            public bool NullFetches { get; set; }

            public int FetchCalls { get; private set; }

            /// <summary>Invoked once after the inner fetch has returned (the fetched row is already read); the test clears it.</summary>
            public Action AfterFetch { get; set; }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                FetchCalls++;
                Task<CharacterRecord> inner = FetchInner(id, ct);
                AfterFetch?.Invoke();
                return inner;
            }

            private Task<CharacterRecord> FetchInner(CharacterID id, CancellationToken ct)
            {
                if (FaultFetches)
                {
                    return Task.FromException<CharacterRecord>(new InvalidOperationException("fetch down"));
                }

                if (NullFetches)
                {
                    return Task.FromResult<CharacterRecord>(null);
                }

                return _inner.FetchAsync(id, ct);
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
            {
                return _inner.InsertAsync(record, ct);
            }

            /// <summary>
            /// When true the update ignores its token, as a commit that wins against the service's write-timeout cancel
            /// (Story 014: the service cancels the store's token at the timeout).
            /// </summary>
            public bool IgnoreUpdateToken { get; set; }

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                return _inner.UpdateAsync(record, expectedSaveVersion, IgnoreUpdateToken ? CancellationToken.None : ct);
            }
        }

        /// <summary>How <see cref="TokenReactingStore"/> answers an update.</summary>
        private enum UpdateBehavior
        {
            /// <summary>Delegates to the inner store.</summary>
            Normal,

            /// <summary>Returns a task that completes canceled as soon as the update's token is cancelled.</summary>
            CancelOnToken,

            /// <summary>Returns a task that faults with <see cref="InvalidOperationException"/> as soon as the update's token is cancelled.</summary>
            FaultOnToken,

            /// <summary>Throws <see cref="InvalidOperationException"/> synchronously from <c>UpdateAsync</c>.</summary>
            ThrowSynchronously,

            /// <summary>Returns a null task.</summary>
            ReturnNull,
        }

        /// <summary>
        /// Wraps a store; fetch and insert go to the inner store, the update follows <see cref="Behavior"/>. Lets the
        /// reclassification of a failed store call (timeout vs caller cancel) be tested without a wall-clock race.
        /// </summary>
        private sealed class TokenReactingStore : ICharacterRecordStore
        {
            private readonly ICharacterRecordStore _inner;
            private int _updateCalls;

            public TokenReactingStore(ICharacterRecordStore inner)
            {
                _inner = inner;
            }

            public UpdateBehavior Behavior { get; set; }

            public int UpdateCalls
            {
                get { return Volatile.Read(ref _updateCalls); }
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                return _inner.FetchAsync(id, ct);
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
            {
                return _inner.InsertAsync(record, ct);
            }

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                Interlocked.Increment(ref _updateCalls);
                switch (Behavior)
                {
                    case UpdateBehavior.CancelOnToken:
                    {
                        var source = new TaskCompletionSource<int>();
                        ct.Register(() => source.TrySetCanceled(ct));
                        return source.Task;
                    }

                    case UpdateBehavior.FaultOnToken:
                    {
                        var source = new TaskCompletionSource<int>();
                        ct.Register(() => source.TrySetException(new InvalidOperationException("faulted after the token fired")));
                        return source.Task;
                    }

                    case UpdateBehavior.ThrowSynchronously:
                        throw new InvalidOperationException("sync boom");
                    case UpdateBehavior.ReturnNull:
                        return null;
                    default:
                        return _inner.UpdateAsync(record, expectedSaveVersion, ct);
                }
            }
        }

        private sealed class Rig
        {
            public LevelingService Leveling;
            public IronGrind.CharacterStats.CharacterStats Stats;
            public CurrencySystem CurrencySystem;
            public RecordingInventory Inventory;
            public FakeZoneLocator Zones;
            public CharacterWriteQueue Queue;
            public CharacterPersistenceService Service;
            public EntityID Entity;
        }

        private DateTime _storeTime;
        private DateTime _serviceTime;
        private InMemoryCharacterRecordStore _store;
        private CountingStore _counting;
        private FetchFaultingStore _faulty;
        private Rig _rig;

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _serviceTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);
            _counting = new CountingStore(_store);
            _faulty = new FetchFaultingStore(_counting);
            _rig = BuildRig(TimeSpan.FromSeconds(5));
        }

        private Rig BuildRig(TimeSpan writeTimeout)
        {
            return BuildRigOver(_faulty, writeTimeout);
        }

        private Rig BuildRigOver(ICharacterRecordStore store, TimeSpan writeTimeout)
        {
            var rig = new Rig();
            rig.Leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            rig.Stats = CharacterStatsFixture.CreateWithLeveling(rig.Leveling);
            rig.Leveling.AttachCharacterStats(rig.Stats);
            rig.Entity = CharacterStatsFixture.PlayerEntityId;
            rig.CurrencySystem = new CurrencySystem();
            rig.Inventory = new RecordingInventory { ExportResult = InventorySnapshot.Empty };
            rig.Zones = new FakeZoneLocator();
            rig.Queue = new CharacterWriteQueue();
            rig.Service = new CharacterPersistenceService(
                store,
                rig.Stats,
                rig.Leveling,
                rig.Inventory,
                rig.CurrencySystem,
                rig.Zones,
                rig.Queue,
                () => _serviceTime,
                writeTimeout);
            return rig;
        }

        private static IClassRegistry CreateClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WARRIOR_CLASS_TYPE, new ClassDefinition(
                strengthAutoAlloc: 2,
                dexterityAutoAlloc: 1,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 0,
                freePointsPerLevel: 1));
            return registry;
        }

        private static int[] BuildXpThresholdsWithCapSentinel()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD;
            t[61] = int.MaxValue;
            return t;
        }

        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within 5 s.");
            return task.Result;
        }

        private static void WaitUntil(Func<bool> condition, string what)
        {
            Assert.IsTrue(SpinWait.SpinUntil(condition, WAIT_MS), "Timed out after 5 s waiting for: " + what);
        }

        private static CharacterRecord MakeSeed()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = 10;
            r.Experience = 0;
            r.HeldFreePoints = 4;
            r.STR = 12;
            r.DEX = 11;
            r.VIT = 13;
            r.INT = 9;
            r.MaxHP = 1000;
            r.MaxMP = 220;
            r.AttackPower = 30;
            r.Defense = 20;
            r.MagicDefense = 10;
            r.CurrentHP = 650f;
            r.CurrentMP = 100f;
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.LastZoneID = LOADED_ZONE;
            r.GearSlots[0] = new GearSlotRecord(new ItemID(700u), 3);
            r.GearSlots[4] = new GearSlotRecord(new ItemID(701u), 0);
            return r;
        }

        // Inserts the seed (store clock T1), loads it into the rig, then moves the store clock to T2.
        private CharacterRecord SeedAndLoad(Rig rig)
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            CharacterRecord loaded = Load(rig);
            _storeTime = T2;
            return loaded;
        }

        private CharacterRecord Load(Rig rig)
        {
            CharacterLoadResult result = Complete(rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Service.ApplyLoadedRecord(rig.Entity, result.Record));
            return result.Record;
        }

        private CharacterRecord Stored()
        {
            return Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));
        }

        private Task<CharacterSaveResult> Irreversible(IrreversibleOutcomeTrigger trigger = IrreversibleOutcomeTrigger.LevelUp)
        {
            return _rig.Service.SaveIrreversibleOutcome(CHAR_A, trigger, CancellationToken.None);
        }

        private Task<CharacterSaveResult> Session(SessionEndReason reason = SessionEndReason.CleanLogout)
        {
            return _rig.Service.SaveSession(CHAR_A, reason, CancellationToken.None);
        }

        private static void ExpectError(string pattern)
        {
            LogAssert.Expect(LogType.Error, new Regex(pattern));
        }

        private static void ExpectWarning(string pattern)
        {
            LogAssert.Expect(LogType.Warning, new Regex(pattern));
        }

        // All persisted content fields (not version / saved time).
        private static void AssertSameContent(CharacterRecord expected, CharacterRecord actual)
        {
            Assert.AreEqual(expected.CharacterID, actual.CharacterID);
            Assert.AreEqual(expected.AccountID, actual.AccountID);
            Assert.AreEqual(expected.CharacterName, actual.CharacterName);
            Assert.AreEqual(expected.ClassType, actual.ClassType);
            Assert.AreEqual(expected.Level, actual.Level);
            Assert.AreEqual(expected.Experience, actual.Experience);
            Assert.AreEqual(expected.STR, actual.STR);
            Assert.AreEqual(expected.DEX, actual.DEX);
            Assert.AreEqual(expected.VIT, actual.VIT);
            Assert.AreEqual(expected.INT, actual.INT);
            Assert.AreEqual(expected.MaxHP, actual.MaxHP);
            Assert.AreEqual(expected.MaxMP, actual.MaxMP);
            Assert.AreEqual(expected.AttackPower, actual.AttackPower);
            Assert.AreEqual(expected.Defense, actual.Defense);
            Assert.AreEqual(expected.MagicDefense, actual.MagicDefense);
            Assert.AreEqual(expected.CritChance, actual.CritChance);
            Assert.AreEqual(expected.CritMultiplier, actual.CritMultiplier);
            Assert.AreEqual(expected.AttackRange, actual.AttackRange);
            Assert.AreEqual(expected.AttackSpeedMultiplier, actual.AttackSpeedMultiplier);
            Assert.AreEqual(expected.MovementSpeed, actual.MovementSpeed);
            Assert.AreEqual(expected.HeldFreePoints, actual.HeldFreePoints);
            Assert.AreEqual(expected.CurrentHP, actual.CurrentHP);
            Assert.AreEqual(expected.CurrentMP, actual.CurrentMP);
            Assert.AreEqual(expected.GoldBalance, actual.GoldBalance);
            Assert.AreEqual(expected.GoldVersion, actual.GoldVersion);
            Assert.AreEqual(expected.LastZoneID, actual.LastZoneID);
            for (int i = 0; i < expected.GearSlots.Length; i++)
            {
                Assert.AreEqual(expected.GearSlots[i].ItemId, actual.GearSlots[i].ItemId, "gear " + i);
                Assert.AreEqual(expected.GearSlots[i].EnhancementLevel, actual.GearSlots[i].EnhancementLevel, "gear level " + i);
            }

            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                Assert.AreEqual(expected.InventorySlots[i], actual.InventorySlots[i], "slot " + i);
                Assert.AreEqual(expected.InventoryItemCounts[i], actual.InventoryItemCounts[i], "count " + i);
                Assert.AreEqual(expected.InventoryEnhancementLevels[i], actual.InventoryEnhancementLevels[i], "level " + i);
            }
        }

        // Changes the live state of the rig away from the loaded record (no save).
        private void ChangeLiveState()
        {
            IronGrind.CharacterStats.CharacterStats s = _rig.Stats;
            EntityID e = _rig.Entity;
            s.SetBaseStat(e, StatID.Level, 11);
            s.SetBaseStat(e, StatID.Experience, 37);
            s.SetBaseStat(e, StatID.Strength, 55);
            s.SetBaseStat(e, StatID.MaxHP, 900);
            s.SetBaseStatFloat(e, StatID.CritChance, 0.33f);
            _rig.Leveling.RestoreLevelingState(e, new LevelingStateSnapshot(3));
            s.SetCurrentHP(e, 123f);
            s.SetCurrentMP(e, 45f);
            _rig.CurrencySystem.RestoreCharacter(CHAR_A, 4321u, 17u);
            _rig.Zones.Zone = new ZoneID(42u);
            _rig.Inventory.ExportResult = new InventorySnapshot(new List<InventorySnapshotEntry>
            {
                new InventorySnapshotEntry(2, 5u, 4, 0),
                new InventorySnapshotEntry(11, 12u, 1, 3),
            });
        }

        // ---------------------------------------------------------------- success and the whole record

        [TestCase(IrreversibleOutcomeTrigger.LevelUp)]
        [TestCase(IrreversibleOutcomeTrigger.EnhancementResult)]
        [TestCase(IrreversibleOutcomeTrigger.RespecCommit)]
        [TestCase(IrreversibleOutcomeTrigger.ItemConsumption)]
        public void Test_EachTrigger_SuccessVersionPlusOneStoredRecordIsTheStateAtTheCall(IrreversibleOutcomeTrigger trigger)
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            _rig.Stats.SetCurrentHP(_rig.Entity, 321f);

            CharacterSaveResult result = Complete(Irreversible(trigger));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord r = Stored();
            Assert.AreEqual(loaded.SaveVersion + 1u, r.SaveVersion);
            Assert.AreEqual(321f, r.CurrentHP);
            Assert.AreEqual(10, r.Level);
            Assert.AreEqual(1250u, r.GoldBalance);
            Assert.AreEqual(T2, r.LastSavedAtUtc);
            Assert.AreEqual(1, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_WholeRecord_GoldInventoryAndHpChangedSinceLoad_OneUpdateAllFieldsMatchMemory()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            ChangeLiveState();

            CharacterSaveResult result = Complete(Irreversible(IrreversibleOutcomeTrigger.LevelUp));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            Assert.AreEqual(1, _counting.UpdateCount, "One store update.");
            CharacterRecord r = Stored();
            Assert.AreEqual(loaded.SaveVersion + 1u, r.SaveVersion);
            Assert.AreEqual(11, r.Level);
            Assert.AreEqual(37, r.Experience);
            Assert.AreEqual(55, r.STR);
            Assert.AreEqual(11, r.DEX);
            Assert.AreEqual(13, r.VIT);
            Assert.AreEqual(9, r.INT);
            Assert.AreEqual(900, r.MaxHP);
            Assert.AreEqual(220, r.MaxMP);
            Assert.AreEqual(30, r.AttackPower);
            Assert.AreEqual(20, r.Defense);
            Assert.AreEqual(10, r.MagicDefense);
            Assert.AreEqual(0.33f, r.CritChance);
            Assert.AreEqual(3, r.HeldFreePoints);
            Assert.AreEqual(123f, r.CurrentHP);
            Assert.AreEqual(45f, r.CurrentMP);
            Assert.AreEqual(4321u, r.GoldBalance);
            Assert.AreEqual(17u, r.GoldVersion);
            Assert.AreEqual(new ZoneID(42u), r.LastZoneID);
            Assert.AreEqual(new ItemID(700u), r.GearSlots[0].ItemId);
            Assert.AreEqual((byte)3, r.GearSlots[0].EnhancementLevel);
            Assert.AreEqual(new ItemID(701u), r.GearSlots[4].ItemId);
            Assert.AreEqual(new ItemID(5u), r.InventorySlots[2]);
            Assert.AreEqual(4, r.InventoryItemCounts[2]);
            Assert.AreEqual(new ItemID(12u), r.InventorySlots[11]);
            Assert.AreEqual((byte)3, r.InventoryEnhancementLevels[11]);
            Assert.AreEqual(CHAR_A, r.CharacterID);
            Assert.AreEqual(ACCOUNT_A, r.AccountID);
            Assert.AreEqual("Brann", r.CharacterName);
            Assert.AreEqual(WARRIOR_CLASS_TYPE, r.ClassType);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_WholeRecord_SameStoredRecordForEveryTrigger()
        {
            SeedAndLoad(_rig);
            ChangeLiveState();
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible(IrreversibleOutcomeTrigger.LevelUp)));
            CharacterRecord first = Stored();

            foreach (IrreversibleOutcomeTrigger trigger in new[]
            {
                IrreversibleOutcomeTrigger.EnhancementResult,
                IrreversibleOutcomeTrigger.RespecCommit,
                IrreversibleOutcomeTrigger.ItemConsumption,
            })
            {
                Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible(trigger)));
                AssertSameContent(first, Stored());
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_WholeRecord_IrreversibleAndSessionSaveWriteTheSameContent()
        {
            SeedAndLoad(_rig);
            ChangeLiveState();
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            CharacterRecord viaIrreversible = Stored();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Session()));

            AssertSameContent(viaIrreversible, Stored());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_SnapshotBeforeFirstAwait_ChangesAfterTheCallWhileHeldAreNotWritten()
        {
            SeedAndLoad(_rig);
            _rig.Zones.Zone = new ZoneID(42u);
            Action release = _store.HoldNext();

            Task<CharacterSaveResult> task = Irreversible();
            Assert.IsFalse(task.IsCompleted);
            _rig.Stats.SetCurrentHP(_rig.Entity, 10f);
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Strength, 500);
            _rig.CurrencySystem.RestoreCharacter(CHAR_A, 1u, 99u);
            _rig.Inventory.ExportResult = new InventorySnapshot(new[] { new InventorySnapshotEntry(2, 5u, 4, 0) });
            _rig.Zones.Zone = new ZoneID(55u);
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(task));
            CharacterRecord r = Stored();
            Assert.AreEqual(650f, r.CurrentHP);
            Assert.AreEqual(12, r.STR);
            Assert.AreEqual(1250u, r.GoldBalance);
            Assert.AreEqual(ItemID.Invalid, r.InventorySlots[2]);
            Assert.AreEqual(new ZoneID(42u), r.LastZoneID);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_StoreHeld_TaskNotCompleteUntilReleased()
        {
            SeedAndLoad(_rig);
            Action release = _store.HoldNext();

            Task<CharacterSaveResult> task = Irreversible();

            Assert.IsFalse(task.IsCompleted, "The store has not confirmed.");
            Assert.AreEqual(1, _counting.UpdateCount);
            release();
            Assert.AreEqual(CharacterSaveResult.Success, Complete(task));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- version at its turn (AC-CP-21)

        [Test]
        public void Test_TwoIrreversibleSaves_FirstHeld_BothSucceedVersionsNPlus1ThenNPlus2()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> first = Irreversible(IrreversibleOutcomeTrigger.LevelUp);
            Task<CharacterSaveResult> second = Irreversible(IrreversibleOutcomeTrigger.EnhancementResult);
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            Assert.AreEqual(1, _counting.UpdateCount, "The second update does not start while the first is held.");

            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(second));
            Assert.AreEqual(2, _counting.UpdateCount);
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_TwoSequentialIrreversibleSaves_NPlus1ThenNPlus2()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(n + 1u, Stored().SaveVersion);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- queue (CR-CP-7)

        [Test]
        public void Test_SessionSaveIssuedWhileIrreversibleHeld_StartsItsUpdateOnlyAfterIt()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            _rig.Stats.SetCurrentHP(_rig.Entity, 100f);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> irreversible = Irreversible();
            _rig.Stats.SetCurrentHP(_rig.Entity, 200f);
            Task<CharacterSaveResult> session = Session(SessionEndReason.SessionTTLExpiry);
            Assert.AreEqual(1, _counting.UpdateCount, "The session save waits behind the held irreversible write.");
            Assert.IsFalse(session.IsCompleted);
            ExpectWarning(@"CR-CP-6.*EC-CP-4");

            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(irreversible));
            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, Complete(session), "SaveSession took its version at call time (n).");
            Assert.AreEqual(2, _counting.UpdateCount);
            CharacterRecord r = Stored();
            Assert.AreEqual(100f, r.CurrentHP, "The stored record is the irreversible write's.");
            Assert.AreEqual(n + 1u, r.SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_IrreversibleIssuedWhileSessionSaveHeld_StartsAfterItAndReadsTheVersionAtItsTurn()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> session = Session();
            Task<CharacterSaveResult> irreversible = Irreversible();
            Assert.AreEqual(1, _counting.UpdateCount);
            Assert.IsFalse(irreversible.IsCompleted);

            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(session));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(irreversible));
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- conflict (AC-CP-19)

        [Test]
        public void Test_StoreRowAdvancedBehind_ConcurrencyConflictOneCriticalNoWarningNoRetry()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, n + 5u));
            ExpectError(@"CRITICAL");

            CharacterSaveResult result = Complete(Irreversible());

            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, result);
            Assert.AreEqual(1, _counting.UpdateCount, "No retry.");
            Assert.AreEqual(n + 5u, Stored().SaveVersion, "Nothing was written.");
            LogAssert.NoUnexpectedReceived(); // a warning, or a second error, would fail here
        }

        // ---------------------------------------------------------------- failure, timeout, cancellation

        [Test]
        public void Test_StoreFaults_DatabaseErrorTaskNotFaultedOneErrorInMemoryStateUntouched()
        {
            SeedAndLoad(_rig);
            ChangeLiveState();
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed");

            Task<CharacterSaveResult> task = Irreversible();
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(task.IsCanceled);
            Assert.AreEqual(1, _counting.UpdateCount);
            Assert.AreEqual(11, _rig.Stats.GetBaseStat(_rig.Entity, StatID.Level), "Nothing is reverted.");
            Assert.AreEqual(123f, _rig.Stats.GetCurrentHP(_rig.Entity));
            Assert.IsTrue(_rig.CurrencySystem.TryGetState(CHAR_A, out uint balance, out uint version));
            Assert.AreEqual(4321u, balance);
            Assert.AreEqual(17u, version);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_StoreNeverCompletes_DatabaseErrorAfterTheInjectedTimeout()
        {
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100));
            SeedAndLoad(rig);
            _store.NeverCompleteNext();
            ExpectError(@"SaveIrreversibleOutcome \(RespecCommit\) for .* failed.*TimeoutException");

            Task<CharacterSaveResult> task = rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.RespecCommit, CancellationToken.None);
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_WriteTimeout_CancelsTheStoresTokenSoAHeldUpdateWritesNothing()
        {
            // Story 014 / ADR-006 Decision 4: the write timeout is a hard CancellationToken timeout for the store too.
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100));
            CharacterRecord loaded = SeedAndLoad(rig);
            Action release = _store.HoldNext(); // the in-memory hold honours the token
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException");

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)));
            release();

            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            Assert.AreEqual(loaded.SaveVersion, Stored().SaveVersion, "The held update was cancelled at the timeout and wrote nothing.");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- reclassification of a failed store call (Story 014)

        private Rig BuildReactingRig(TimeSpan writeTimeout, UpdateBehavior behavior, out TokenReactingStore store)
        {
            store = new TokenReactingStore(_faulty) { Behavior = behavior };
            Rig rig = BuildRigOver(store, writeTimeout);
            SeedAndLoad(rig);
            return rig;
        }

        [Test]
        public void Test_StoreEndsCanceledAtTheTimeoutWithTheCallersTokenUntouched_DatabaseErrorLoggedAsTimeout()
        {
            Rig rig = BuildReactingRig(TimeSpan.FromMilliseconds(50), UpdateBehavior.CancelOnToken, out TokenReactingStore _);
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException");

            Task<CharacterSaveResult> task = rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(task.IsCanceled);
            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_CallerCancelsBeforeTheTimeout_DatabaseErrorNotLoggedAsTimeout()
        {
            Rig rig = BuildReactingRig(TimeSpan.FromSeconds(5), UpdateBehavior.CancelOnToken, out TokenReactingStore store);
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed: (TaskCanceledException|OperationCanceledException)");
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSaveResult> task = rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, cts.Token);
                WaitUntil(() => store.UpdateCalls == 1, "the update to start");

                cts.Cancel();

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
                Assert.IsFalse(task.IsCanceled);
            }

            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            LogAssert.NoUnexpectedReceived(); // a TimeoutException log would not match the expected pattern
        }

        [Test]
        public void Test_StoreFaultsAfterTheTimeoutFired_ReportedAsTheTimeoutWithTheCauseAttached()
        {
            // Pins: a fault that follows the timeout's cancel is reported as the timeout (the cause is its inner exception).
            Rig rig = BuildReactingRig(TimeSpan.FromMilliseconds(50), UpdateBehavior.FaultOnToken, out TokenReactingStore _);
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException");

            Task<CharacterSaveResult> task = rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
            Assert.IsFalse(task.IsFaulted);
            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_StoreThrowsSynchronouslyFromUpdate_DatabaseErrorOneErrorAndAFollowingSaveWorks()
        {
            Rig rig = BuildReactingRig(TimeSpan.FromSeconds(5), UpdateBehavior.ThrowSynchronously, out TokenReactingStore store);
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*sync boom");

            Task<CharacterSaveResult> task = rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(task.IsCanceled);
            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            store.Behavior = UpdateBehavior.Normal;
            ExpectWarning(REREAD_WARNING); // the failed write left the version unknown
            Assert.AreEqual(CharacterSaveResult.Success, Complete(rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_StoreReturnsANullTask_DatabaseErrorLoggedAsInvalidOperationAndAFollowingSaveWorks()
        {
            Rig rig = BuildReactingRig(TimeSpan.FromSeconds(5), UpdateBehavior.ReturnNull, out TokenReactingStore store);
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*InvalidOperationException");

            Task<CharacterSaveResult> task = rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
            Assert.IsFalse(task.IsFaulted);
            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            store.Behavior = UpdateBehavior.Normal;
            ExpectWarning(REREAD_WARNING);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_TokenCancelledDuringUpdate_DatabaseErrorNotCancelledAndAFollowingSaveWorks()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            _store.HoldNext();
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed");
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSaveResult> task = _rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, cts.Token);
                Assert.IsFalse(task.IsCompleted);

                cts.Cancel();

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
                Assert.IsFalse(task.IsCanceled);
                Assert.IsFalse(task.IsFaulted);
            }

            ExpectWarning(REREAD_WARNING);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()), "The service's state for the character is usable.");
            Assert.AreEqual(loaded.SaveVersion + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- unknown outcome (AC-CP-33, AC-CP-34)

        [Test]
        public void Test_UnknownOutcomeThatCommitted_SessionSaveReReadsWritesRolledBackStateVersionNPlus2()
        {
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100));
            CharacterRecord loaded = SeedAndLoad(rig);
            uint n = loaded.SaveVersion;
            rig.Stats.SetCurrentHP(rig.Entity, 100f);
            _faulty.IgnoreUpdateToken = true; // the commit wins against the timeout's cancel
            Action release = _store.HoldNext();
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException");

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)));
            release();
            WaitUntil(() => Stored().SaveVersion == n + 1u, "the abandoned update to commit");
            _faulty.IgnoreUpdateToken = false;
            Assert.AreEqual(100f, Stored().CurrentHP);
            rig.Stats.SetCurrentHP(rig.Entity, 650f); // the caller rolls its in-memory state back
            ExpectWarning(REREAD_WARNING);

            CharacterSaveResult result = Complete(rig.Service.SaveSession(CHAR_A, SessionEndReason.SessionTTLExpiry, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord r = Stored();
            Assert.AreEqual(650f, r.CurrentHP, "The stored record is the rolled-back state.");
            Assert.AreEqual(n + 2u, r.SaveVersion);
            Assert.AreEqual(1, _faulty.FetchCalls - 1, "One re-read (the load fetched once).");
            LogAssert.NoUnexpectedReceived(); // no CRITICAL, no conflict warning
        }

        [Test]
        public void Test_UnknownOutcomeThatDidNotCommit_SessionSaveReReadsSuccessVersionNPlus1()
        {
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100));
            CharacterRecord loaded = SeedAndLoad(rig);
            uint n = loaded.SaveVersion;
            _store.NeverCompleteNext();
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException");

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)));
            Assert.AreEqual(n, Stored().SaveVersion, "It never committed.");
            ExpectWarning(REREAD_WARNING);

            CharacterSaveResult result = Complete(rig.Service.SaveSession(CHAR_A, SessionEndReason.SessionTTLExpiry, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            Assert.AreEqual(n + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- the mark (CR-CP-6)

        [Test]
        public void Test_SessionSaveDatabaseError_MarksTheVersionUnknownForTheNextIrreversibleWrite()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveSession \(CleanLogout\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Session()));
            int fetchesBefore = _faulty.FetchCalls;
            ExpectWarning(REREAD_WARNING);

            CharacterSaveResult result = Complete(Irreversible());

            Assert.AreEqual(CharacterSaveResult.Success, result);
            Assert.AreEqual(fetchesBefore + 1, _faulty.FetchCalls, "The write fetched the stored version once.");
            Assert.AreEqual(loaded.SaveVersion + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_IrreversibleDatabaseError_MarksTheVersionUnknownForTheNextIrreversibleWrite()
        {
            SeedAndLoad(_rig);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Irreversible()));
            int fetchesBefore = _faulty.FetchCalls;
            ExpectWarning(REREAD_WARNING);

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible(IrreversibleOutcomeTrigger.ItemConsumption)));

            Assert.AreEqual(fetchesBefore + 1, _faulty.FetchCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_FetchFails_DatabaseErrorNothingWrittenMarkKeptNextWriteFetchesAgain()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveSession \(CleanLogout\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Session()));
            int updatesBefore = _counting.UpdateCount;
            int fetchesBefore = _faulty.FetchCalls;
            _faulty.FaultFetches = true;
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* could not re-read the stored SaveVersion");

            Task<CharacterSaveResult> failed = Irreversible();
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(failed));

            Assert.IsFalse(failed.IsFaulted);
            Assert.AreEqual(updatesBefore, _counting.UpdateCount, "Nothing is written when the fetch fails.");
            Assert.AreEqual(fetchesBefore + 1, _faulty.FetchCalls);
            ExpectError(@"SaveSession \(CleanLogout\) for .* could not re-read the stored SaveVersion");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Session()));
            Assert.AreEqual(fetchesBefore + 2, _faulty.FetchCalls, "The mark was kept: the following write fetched again.");
            Assert.AreEqual(updatesBefore, _counting.UpdateCount);
            _faulty.FaultFetches = false;
            ExpectWarning(REREAD_WARNING);

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));

            Assert.AreEqual(fetchesBefore + 3, _faulty.FetchCalls);
            Assert.AreEqual(loaded.SaveVersion + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_FetchReturnsNoRecord_DatabaseErrorNothingWritten()
        {
            SeedAndLoad(_rig);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveSession \(CleanLogout\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Session()));
            int updatesBefore = _counting.UpdateCount;
            _faulty.NullFetches = true;
            ExpectError(@"could not re-read the stored SaveVersion");

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Irreversible()));

            Assert.AreEqual(updatesBefore, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_FetchTimesOut_DatabaseErrorNothingWritten()
        {
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100));
            SeedAndLoad(rig);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveSession \(CleanLogout\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));
            int updatesBefore = _counting.UpdateCount;
            _store.NeverCompleteNext(); // the next store call is the re-read
            ExpectError(@"could not re-read the stored SaveVersion.*TimeoutException");

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)));

            Assert.AreEqual(updatesBefore, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_Success_ClearsTheMarkSoTheNextWriteDoesNotFetch()
        {
            SeedAndLoad(_rig);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveSession \(CleanLogout\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Session()));
            ExpectWarning(REREAD_WARNING);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            int fetchesAfterReread = _faulty.FetchCalls;

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Session()));

            Assert.AreEqual(fetchesAfterReread, _faulty.FetchCalls, "No fetch after the mark was cleared.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_ConcurrencyConflict_DoesNotSetTheMark()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, n + 5u));
            ExpectError(@"CRITICAL");
            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, Complete(Irreversible()));
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, n));
            int fetchesBefore = _faulty.FetchCalls;

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));

            Assert.AreEqual(fetchesBefore, _faulty.FetchCalls, "No re-read after a conflict.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_SaveCancelledWhileWaitingInTheQueue_DoesNotSetTheMark()
        {
            SeedAndLoad(_rig);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> first = Session();
            ExpectWarning(@"SaveIrreversibleOutcome \(LevelUp\) for .* was cancelled before its write ran; nothing written");
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSaveResult> waiting = _rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, cts.Token);
                Assert.IsFalse(waiting.IsCompleted);

                cts.Cancel();

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(waiting));
                Assert.IsFalse(waiting.IsCanceled);
            }

            release();
            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            int fetchesBefore = _faulty.FetchCalls;
            Assert.AreEqual(1, _counting.UpdateCount, "The cancelled write never reached the store.");

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));

            Assert.AreEqual(fetchesBefore, _faulty.FetchCalls, "No re-read after a queued cancel.");
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- not found, wiring, snapshot failure

        [Test]
        public void Test_UnknownCharacter_CharacterNotFoundNoStoreCallNoQueueEntry()
        {
            SeedAndLoad(_rig);

            CharacterSaveResult result = Complete(_rig.Service.SaveIrreversibleOutcome(CHAR_UNKNOWN, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, result);
            Assert.AreEqual(0, _counting.UpdateCount);
            Assert.AreEqual(0, _rig.Queue.ActiveCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_EntryGoneAtItsTurn_CharacterNotFoundNoStoreCallForIt()
        {
            SeedAndLoad(_rig);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> first = Session();
            Task<CharacterSaveResult> second = Irreversible();
            Assert.IsFalse(second.IsCompleted);

            // A reload that fails removes the session entry while the second write is still queued.
            CharacterLoadResult reload = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            _rig.Inventory.ImportResult = false;
            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, _rig.Service.ApplyLoadedRecord(_rig.Entity, reload.Record));
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, Complete(second));
            Assert.AreEqual(1, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_ServiceBuiltWithoutZoneLocatorAndQueue_ThrowsInvalidOperationExceptionSynchronously()
        {
            Rig r = _rig;
            var service = new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem);

            Assert.Throws<InvalidOperationException>(() => service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None));
            Assert.AreEqual(0, _counting.UpdateCount);
        }

        [Test]
        public void Test_SnapshotThrows_DatabaseErrorOneErrorNoStoreCallNoQueueEntry()
        {
            SeedAndLoad(_rig);
            _rig.Inventory.ExportResult = null; // the fake then throws from ExportSnapshot
            ExpectError(@"SaveIrreversibleOutcome \(RespecCommit\) for .* could not take its snapshot");

            Task<CharacterSaveResult> task = _rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.RespecCommit, CancellationToken.None);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, _counting.UpdateCount);
            Assert.AreEqual(0, _rig.Queue.ActiveCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- latency (ADR-006 Decision 4)

        [TestCase(49, 0)]
        [TestCase(50, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 2)]
        public void Test_WriteLatencyMilliseconds_LogsWarningOrErrorByThreshold(int milliseconds, int expectedLog)
        {
            SeedAndLoad(_rig);
            _serviceTime = T1;
            Action release = _store.HoldNext();
            if (expectedLog == 1)
            {
                ExpectWarning(@"SaveIrreversibleOutcome for .* took .* ms");
            }
            else if (expectedLog == 2)
            {
                ExpectError(@"SaveIrreversibleOutcome for .* took .* ms");
            }

            Task<CharacterSaveResult> task = Irreversible();
            // The update is counted after the write read its start time, so the clock moves only inside the held window.
            WaitUntil(() => _counting.UpdateCount == 1, "the held update to start");
            _serviceTime = T1.AddMilliseconds(milliseconds);
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(task));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- code-review additions: re-read paths, reload in flight, cancelled token

        // Marks the entry with a failed SaveSession store call.
        private void MarkVersionUnknown()
        {
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(@"SaveSession \(CleanLogout\) for .* failed");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Session()));
        }

        [Test]
        public void Test_ReReadThenConflict_MarkKeptNextWriteFetchesAgain()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            MarkVersionUnknown();
            int fetchesBefore = _faulty.FetchCalls;
            _faulty.AfterFetch = () =>
            {
                _faulty.AfterFetch = null;
                _store.OverwriteSaveVersion(CHAR_A, n + 7u); // the stored version moves right after the re-read
            };
            ExpectWarning(REREAD_WARNING);
            ExpectError(@"CRITICAL");

            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, Complete(Irreversible()));

            Assert.AreEqual(fetchesBefore + 1, _faulty.FetchCalls);
            ExpectWarning(REREAD_WARNING);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(fetchesBefore + 2, _faulty.FetchCalls, "The conflict kept the mark: the next write fetched again.");
            Assert.AreEqual(n + 8u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_TokenCancelledDuringReReadFetch_DatabaseErrorNothingWrittenMarkKept()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            MarkVersionUnknown();
            int updatesBefore = _counting.UpdateCount;
            int fetchesBefore = _faulty.FetchCalls;
            Action release = _store.HoldNext(); // the next store call is the re-read fetch
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* could not re-read the stored SaveVersion");
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSaveResult> task = _rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, cts.Token);
                Assert.IsFalse(task.IsCompleted);

                cts.Cancel();

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
            }

            release(); // nothing dangles; the held call was cancelled and writes nothing
            Assert.AreEqual(updatesBefore, _counting.UpdateCount);
            Assert.AreEqual(fetchesBefore + 1, _faulty.FetchCalls);
            ExpectWarning(REREAD_WARNING);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(fetchesBefore + 2, _faulty.FetchCalls, "The mark was kept: the following write fetched again.");
            Assert.AreEqual(loaded.SaveVersion + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_ReReadThenUpdateFails_MarkKeptNextWriteFetchesAgain()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            MarkVersionUnknown();
            int fetchesBefore = _faulty.FetchCalls;
            // FailNext arms the next store call; armed after the fetch has returned, it hits the update.
            _faulty.AfterFetch = () =>
            {
                _faulty.AfterFetch = null;
                _store.FailNext(new InvalidOperationException("update down"));
            };
            ExpectWarning(REREAD_WARNING);
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed: .*update down");

            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(Irreversible()));

            Assert.AreEqual(fetchesBefore + 1, _faulty.FetchCalls);
            ExpectWarning(REREAD_WARNING);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(fetchesBefore + 2, _faulty.FetchCalls, "The mark was kept: the following write fetched again.");
            Assert.AreEqual(loaded.SaveVersion + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_ReloadWhileIrreversibleWriteInFlight_CommitAdvancesTheNewEntry()
        {
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> inFlight = Irreversible();
            Assert.IsFalse(inFlight.IsCompleted);

            Load(_rig); // installs a new session entry; the stored row is still n
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(inFlight));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived(); // no CRITICAL: the new entry was advanced to n + 1
        }

        [Test]
        public void Test_ReloadWhileIrreversibleWriteTimesOut_NewEntryMarkedNextWriteReReads()
        {
            _rig = BuildRig(TimeSpan.FromMilliseconds(300));
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            _faulty.IgnoreUpdateToken = true; // the commit wins against the timeout's cancel (Story 014)
            Action release = _store.HoldNext();
            ExpectError(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException");
            Task<CharacterSaveResult> inFlight = Irreversible();

            Load(_rig); // during the hold, well inside the timeout
            Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(inFlight));
            release(); // the abandoned update commits
            WaitUntil(() => Stored().SaveVersion == n + 1u, "the abandoned update to commit");
            _faulty.IgnoreUpdateToken = false;
            ExpectWarning(REREAD_WARNING);

            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));

            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_SessionSaveWithEntryRemovedAtItsTurn_WritesWithCallTimeVersion()
        {
            // Pins current behaviour: a SaveSession carries its call-time version, so it still issues its update after its
            // entry is gone. Whether it should be released with CharacterNotFound belongs to Story 010 (release semantics).
            CharacterRecord loaded = SeedAndLoad(_rig);
            uint n = loaded.SaveVersion;
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> first = Session();
            Task<CharacterSaveResult> second = Session(SessionEndReason.SessionTTLExpiry);
            Assert.IsFalse(second.IsCompleted);
            CharacterLoadResult reload = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            _rig.Inventory.ImportResult = false;
            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, _rig.Service.ApplyLoadedRecord(_rig.Entity, reload.Record));
            ExpectWarning(@"SaveSession \(SessionTTLExpiry\) for .* conflicted: .*SaveVersion " + n + @" ");

            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, Complete(second), "It expected n, but the first write moved the row to n + 1.");
            Assert.AreEqual(2, _counting.UpdateCount, "The second update was issued although the entry was gone.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_AlreadyCancelledToken_DatabaseErrorNoUpdateNoMark()
        {
            SeedAndLoad(_rig);
            ExpectWarning(@"SaveIrreversibleOutcome \(LevelUp\) for .* was cancelled before its write ran; nothing written");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Task<CharacterSaveResult> task = _rig.Service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, cts.Token);

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
                Assert.IsFalse(task.IsCanceled);
            }

            Assert.AreEqual(0, _counting.UpdateCount);
            int fetchesBefore = _faulty.FetchCalls;
            Assert.AreEqual(CharacterSaveResult.Success, Complete(Irreversible()));
            Assert.AreEqual(fetchesBefore, _faulty.FetchCalls, "No mark was set, so no re-read.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
