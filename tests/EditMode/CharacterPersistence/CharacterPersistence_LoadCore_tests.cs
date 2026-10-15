using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 003: <c>LoadCharacter</c> (CR-CP-3 steps 1-3 as validation, CR-CP-8 timeout) and
    /// the synchronous <c>ApplyLoadedRecord</c> that writes the 17 base fields.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_LoadCore_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly AccountID ACCOUNT_B = new AccountID(8u);
        private static readonly ZoneID ZONE_LAST = new ZoneID(42u);
        private const byte WARRIOR_CLASS_TYPE = 1;

        private sealed class CountingStore : ICharacterRecordStore
        {
            private readonly ICharacterRecordStore _inner;

            public CountingStore(ICharacterRecordStore inner)
            {
                _inner = inner;
            }

            public int FetchCount { get; private set; }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                FetchCount++;
                return _inner.FetchAsync(id, ct);
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
            {
                return _inner.InsertAsync(record, ct);
            }

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                return _inner.UpdateAsync(record, expectedSaveVersion, ct);
            }
        }

        // A store whose fetch is supplied by the test; insert and update are not used by a load.
        private sealed class ScriptedStore : ICharacterRecordStore
        {
            private readonly Func<CharacterID, CancellationToken, Task<CharacterRecord>> _fetch;

            public ScriptedStore(Func<CharacterID, CancellationToken, Task<CharacterRecord>> fetch)
            {
                _fetch = fetch;
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                return _fetch(id, ct);
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }

        private static readonly TimeSpan WAIT_LIMIT = TimeSpan.FromSeconds(5);
        private const int APPLY_STAT_EVENTS = 16;
        private const int GEAR_INDEX = 2;
        private const uint GEAR_ITEM = 77u;
        private const byte GEAR_ENHANCEMENT = 3;
        private const uint INVENTORY_ITEM = 5u;
        private const int INVENTORY_COUNT = 4;
        private const byte INVENTORY_ENHANCEMENT = 2;
        private const byte UNKNOWN_CLASS_TYPE = 99;

        private InMemoryCharacterRecordStore _store;
        private CountingStore _counting;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private LevelingService _leveling;
        private CharacterPersistenceService _service;
        private EntityID _entity;
        private int _statEvents;

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryCharacterRecordStore(() => T1);
            _counting = new CountingStore(_store);
            _leveling = new LevelingService(new int[] { 0, 0, 999999 }, CreateClassRegistry());
            _stats = CharacterStatsFixture.CreateWithLeveling(_leveling);
            _leveling.AttachCharacterStats(_stats);
            _service = new CharacterPersistenceService(_counting, _stats, _leveling);
            _entity = CharacterStatsFixture.PlayerEntityId;
            _statEvents = 0;
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

        private static CharacterRecord MakeRecord()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.LastZoneID = ZONE_LAST;
            r.SaveVersion = 3u;
            return r;
        }

        private static CharacterRecord MakeDistinctRecord()
        {
            CharacterRecord r = MakeRecord();
            r.Level = 10;
            r.Experience = 1234;
            r.HeldFreePoints = 5;
            r.STR = 21;
            r.DEX = 22;
            r.VIT = 23;
            r.INT = 24;
            r.MaxHP = 1501;
            r.MaxMP = 802;
            r.AttackPower = 77;
            r.Defense = 55;
            r.MagicDefense = 33;
            r.CritChance = 0.125f;
            r.CritMultiplier = 1.75f;
            r.AttackRange = 4.5f;
            r.AttackSpeedMultiplier = 1.25f;
            r.MovementSpeed = 6.5f;
            r.GearSlots[GEAR_INDEX] = new GearSlotRecord(new ItemID(GEAR_ITEM), GEAR_ENHANCEMENT);
            r.InventorySlots[0] = new ItemID(INVENTORY_ITEM);
            r.InventoryItemCounts[0] = INVENTORY_COUNT;
            r.InventoryEnhancementLevels[0] = INVENTORY_ENHANCEMENT;
            return r;
        }

        // Waits with a limit, so a task that never completes fails the test instead of hanging the suite.
        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_LIMIT), "the task did not complete within " + WAIT_LIMIT);
            return task.Result;
        }

        private static void Complete(Task task)
        {
            Assert.IsTrue(task.Wait(WAIT_LIMIT), "the task did not complete within " + WAIT_LIMIT);
        }

        private void Store(CharacterRecord r)
        {
            Assert.IsTrue(Complete(_store.InsertAsync(r, CancellationToken.None)));
        }

        private CharacterLoadResult Load(CharacterID id, AccountID account)
        {
            return Complete(_service.LoadCharacter(id, account, CancellationToken.None));
        }

        private void CountStatEvents()
        {
            _stats.Subscribe(new IronGrind.CharacterStats.CharacterStats.StatChangedHandler((e, s) => _statEvents++));
        }

        private static string Arrays(CharacterRecord r)
        {
            return string.Join(",", r.GearSlots.Select(g => g.ItemId.RawValue + ":" + g.EnhancementLevel))
                + "|" + string.Join(",", r.InventorySlots.Select(i => i.RawValue))
                + "|" + string.Join(",", r.InventoryItemCounts)
                + "|" + string.Join(",", r.InventoryEnhancementLevels);
        }

        private static void AssertSame(CharacterRecord expected, CharacterRecord actual)
        {
            Assert.IsNotNull(actual);
            foreach (PropertyInfo p in typeof(CharacterRecord).GetProperties().Where(property => !property.PropertyType.IsArray))
            {
                Assert.AreEqual(p.GetValue(expected), p.GetValue(actual), p.Name);
            }

            Assert.AreEqual(Arrays(expected), Arrays(actual));
        }

        private static void SetInt(CharacterRecord r, string field, int value)
        {
            switch (field)
            {
                case "Level": r.Level = value; break;
                case "Experience": r.Experience = value; break;
                case "HeldFreePoints": r.HeldFreePoints = value; break;
                case "STR": r.STR = value; break;
                case "DEX": r.DEX = value; break;
                case "VIT": r.VIT = value; break;
                case "INT": r.INT = value; break;
                case "MaxHP": r.MaxHP = value; break;
                case "MaxMP": r.MaxMP = value; break;
                case "AttackPower": r.AttackPower = value; break;
                case "Defense": r.Defense = value; break;
                case "MagicDefense": r.MagicDefense = value; break;
                default: throw new ArgumentException("Unknown int field " + field);
            }
        }

        private static void SetFloat(CharacterRecord r, string field, float value)
        {
            switch (field)
            {
                case "CritChance": r.CritChance = value; break;
                case "CritMultiplier": r.CritMultiplier = value; break;
                case "AttackRange": r.AttackRange = value; break;
                case "AttackSpeedMultiplier": r.AttackSpeedMultiplier = value; break;
                case "MovementSpeed": r.MovementSpeed = value; break;
                default: throw new ArgumentException("Unknown float field " + field);
            }
        }

        // ---------------------------------------------------------------- id / not found / mismatch

        [Test]
        public void LoadCharacter_InvalidId_ReturnsCorruptRecordWithNoStoreCall()
        {
            CharacterLoadResult result = Load(CharacterID.Invalid, ACCOUNT_A);

            Assert.AreEqual(CharacterLoadCode.CorruptRecord, result.Code);
            Assert.IsNull(result.Record);
            Assert.AreEqual(0, _counting.FetchCount);
        }

        [Test]
        public void LoadCharacter_UnknownId_ReturnsCharacterNotFound()
        {
            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            Assert.AreEqual(CharacterLoadCode.CharacterNotFound, result.Code);
            Assert.IsNull(result.Record);
            Assert.AreEqual(1, _counting.FetchCount);
        }

        [Test]
        public void LoadCharacter_OtherAccount_ReturnsAccountMismatchWithNoRecord()
        {
            Store(MakeRecord());

            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_B);

            Assert.AreEqual(CharacterLoadCode.AccountMismatch, result.Code);
            Assert.IsNull(result.Record);
        }

        // ---------------------------------------------------------------- range validation

        [TestCase("Level", 1, true)]
        [TestCase("Level", 60, true)]
        [TestCase("Level", 0, false)]
        [TestCase("Level", 61, false)]
        [TestCase("Experience", 0, true)]
        [TestCase("Experience", -1, false)]
        [TestCase("HeldFreePoints", 0, true)]
        [TestCase("HeldFreePoints", -1, false)]
        [TestCase("STR", 1, true)]
        [TestCase("STR", 999, true)]
        [TestCase("STR", 0, false)]
        [TestCase("STR", 1000, false)]
        [TestCase("DEX", 1, true)]
        [TestCase("DEX", 999, true)]
        [TestCase("DEX", 0, false)]
        [TestCase("DEX", 1000, false)]
        [TestCase("VIT", 1, true)]
        [TestCase("VIT", 999, true)]
        [TestCase("VIT", 0, false)]
        [TestCase("VIT", 1000, false)]
        [TestCase("INT", 1, true)]
        [TestCase("INT", 999, true)]
        [TestCase("INT", 0, false)]
        [TestCase("INT", 1000, false)]
        [TestCase("MaxHP", 1, true)]
        [TestCase("MaxHP", 99999, true)]
        [TestCase("MaxHP", 0, false)]
        [TestCase("MaxHP", 100000, false)]
        [TestCase("MaxMP", 1, true)]
        [TestCase("MaxMP", 99999, true)]
        [TestCase("MaxMP", 0, false)]
        [TestCase("MaxMP", 100000, false)]
        [TestCase("AttackPower", 0, true)]
        [TestCase("AttackPower", -1, false)]
        [TestCase("Defense", 0, true)]
        [TestCase("Defense", -1, false)]
        [TestCase("MagicDefense", 0, true)]
        [TestCase("MagicDefense", -1, false)]
        public void LoadCharacter_IntFieldAtAndBeyondBound_ValidatesAsExpected(string field, int value, bool expectedValid)
        {
            CharacterRecord r = MakeRecord();
            SetInt(r, field, value);
            Store(r);

            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            Assert.AreEqual(expectedValid ? CharacterLoadCode.Success : CharacterLoadCode.CorruptRecord, result.Code);
            if (expectedValid)
            {
                Assert.IsNotNull(result.Record);
            }
            else
            {
                Assert.IsNull(result.Record);
            }
        }

        [TestCase("CritChance", 0.0001f, true)]
        [TestCase("CritChance", 0f, false)]
        [TestCase("CritMultiplier", 0.0001f, true)]
        [TestCase("CritMultiplier", 0f, false)]
        [TestCase("AttackRange", 0.0001f, true)]
        [TestCase("AttackRange", 0f, false)]
        [TestCase("AttackSpeedMultiplier", 0.0001f, true)]
        [TestCase("AttackSpeedMultiplier", 0f, false)]
        [TestCase("MovementSpeed", 0.0001f, true)]
        [TestCase("MovementSpeed", 0f, false)]
        [TestCase("CritChance", float.PositiveInfinity, false)]
        [TestCase("CritMultiplier", float.PositiveInfinity, false)]
        [TestCase("AttackRange", float.PositiveInfinity, false)]
        [TestCase("AttackSpeedMultiplier", float.PositiveInfinity, false)]
        [TestCase("MovementSpeed", float.PositiveInfinity, false)]
        [TestCase("MovementSpeed", float.NaN, false)]
        [TestCase("MovementSpeed", -1f, false)]
        public void LoadCharacter_FloatFieldPositiveVersusZero_ValidatesAsExpected(string field, float value, bool expectedValid)
        {
            CharacterRecord r = MakeRecord();
            SetFloat(r, field, value);
            Store(r);

            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            Assert.AreEqual(expectedValid ? CharacterLoadCode.Success : CharacterLoadCode.CorruptRecord, result.Code);
            if (expectedValid)
            {
                Assert.IsNotNull(result.Record);
            }
            else
            {
                Assert.IsNull(result.Record);
            }
        }

        // ---------------------------------------------------------------- success

        [Test]
        public void LoadCharacter_ValidRecord_ReturnsSuccessLastZoneAndEqualRecord()
        {
            CharacterRecord stored = MakeDistinctRecord();
            Store(stored);

            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(ZONE_LAST, result.LastZoneID);
            // Expected is the stored row, not `stored`: the store stamps the timestamps on insert.
            AssertSame(Complete(_store.FetchAsync(CHAR_A, CancellationToken.None)), result.Record);
            Assert.AreEqual(GEAR_ITEM, result.Record.GearSlots[GEAR_INDEX].ItemId.RawValue);
            Assert.AreEqual(INVENTORY_COUNT, result.Record.InventoryItemCounts[0]);
        }

        // ---------------------------------------------------------------- ApplyLoadedRecord

        [Test]
        public void ApplyLoadedRecord_DistinctValues_RestoresAllSeventeenFields()
        {
            CharacterRecord stored = MakeDistinctRecord();
            Store(stored);
            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            _service.ApplyLoadedRecord(_entity, result.Record);

            Assert.AreEqual(stored.Level, _stats.GetBaseStat(_entity, StatID.Level));
            Assert.AreEqual(stored.Experience, _stats.GetBaseStat(_entity, StatID.Experience));
            Assert.AreEqual(stored.STR, _stats.GetBaseStat(_entity, StatID.Strength));
            Assert.AreEqual(stored.DEX, _stats.GetBaseStat(_entity, StatID.Dexterity));
            Assert.AreEqual(stored.VIT, _stats.GetBaseStat(_entity, StatID.Vitality));
            Assert.AreEqual(stored.INT, _stats.GetBaseStat(_entity, StatID.Intelligence));
            Assert.AreEqual(stored.MaxHP, _stats.GetBaseStat(_entity, StatID.MaxHP));
            Assert.AreEqual(stored.MaxMP, _stats.GetBaseStat(_entity, StatID.MaxMP));
            Assert.AreEqual(stored.AttackPower, _stats.GetBaseStat(_entity, StatID.AttackPower));
            Assert.AreEqual(stored.Defense, _stats.GetBaseStat(_entity, StatID.Defense));
            Assert.AreEqual(stored.MagicDefense, _stats.GetBaseStat(_entity, StatID.MagicDefense));
            Assert.AreEqual(stored.CritChance, _stats.GetBaseStatFloat(_entity, StatID.CritChance));
            Assert.AreEqual(stored.CritMultiplier, _stats.GetBaseStatFloat(_entity, StatID.CritMultiplier));
            Assert.AreEqual(stored.AttackRange, _stats.GetBaseStatFloat(_entity, StatID.AttackRange));
            Assert.AreEqual(stored.AttackSpeedMultiplier, _stats.GetBaseStatFloat(_entity, StatID.AttackSpeedMultiplier));
            Assert.AreEqual(stored.MovementSpeed, _stats.GetBaseStatFloat(_entity, StatID.MovementSpeed));
            Assert.AreEqual(stored.HeldFreePoints, _leveling.GetHeldFreePoints(_entity));
        }

        [Test]
        public void ApplyLoadedRecord_NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _service.ApplyLoadedRecord(_entity, null));
        }

        [Test]
        public void ApplyLoadedRecord_ServiceBuiltWithoutStatsAndLeveling_ThrowsInvalidOperationException()
        {
            var bare = new CharacterPersistenceService(_store);

            Assert.Throws<InvalidOperationException>(() => bare.ApplyLoadedRecord(_entity, MakeRecord()));
        }

        // ---------------------------------------------------------------- no write on failure

        [TestCase("NotFound")]
        [TestCase("Mismatch")]
        [TestCase("Corrupt")]
        [TestCase("InvalidId")]
        [TestCase("DatabaseError")]
        public void LoadCharacter_FailureCode_WritesNoStatsAndLeavesHeldFreePointsUnchanged(string scenario)
        {
            // Seed the entity so a stray write would be visible, then start counting.
            _leveling.RegisterPlayerClassType(_entity, WARRIOR_CLASS_TYPE);
            _stats.SetBaseStat(_entity, StatID.Level, 10);
            _leveling.RestoreLevelingState(_entity, new LevelingStateSnapshot(3));
            CountStatEvents();

            CharacterID id = CHAR_A;
            AccountID account = ACCOUNT_A;
            CharacterLoadCode expected;
            switch (scenario)
            {
                case "NotFound":
                    expected = CharacterLoadCode.CharacterNotFound;
                    break;
                case "Mismatch":
                    Store(MakeRecord());
                    account = ACCOUNT_B;
                    expected = CharacterLoadCode.AccountMismatch;
                    break;
                case "Corrupt":
                    CharacterRecord r = MakeRecord();
                    r.Level = 0;
                    Store(r);
                    expected = CharacterLoadCode.CorruptRecord;
                    break;
                case "InvalidId":
                    id = CharacterID.Invalid;
                    expected = CharacterLoadCode.CorruptRecord;
                    break;
                default:
                    Store(MakeRecord());
                    _store.FailNext(new InvalidOperationException("store down"));
                    expected = CharacterLoadCode.DatabaseError;
                    break;
            }

            CharacterLoadResult result = Load(id, account);

            Assert.AreEqual(expected, result.Code);
            Assert.IsNull(result.Record);
            Assert.AreEqual(0, _statEvents);
            Assert.AreEqual(3, _leveling.GetHeldFreePoints(_entity));
            Assert.AreEqual(10, _stats.GetBaseStat(_entity, StatID.Level));
        }

        [Test]
        public void LoadCharacter_Success_WritesNothingUntilApplyIsCalled()
        {
            Store(MakeDistinctRecord());
            CountStatEvents();

            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(0, _statEvents);
            Assert.AreEqual(0, _stats.GetBaseStat(_entity, StatID.MaxHP));
            Assert.AreEqual(0, _leveling.GetHeldFreePoints(_entity));
        }

        // ---------------------------------------------------------------- timeout / fault

        [Test]
        public void LoadCharacter_TokenCancelledWhileFetchPending_ReturnsDatabaseErrorAndRunsToCompletion()
        {
            Store(MakeRecord());
            _store.NeverCompleteNext();
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterLoadResult> task = _service.LoadCharacter(CHAR_A, ACCOUNT_A, cts.Token);
                Assert.IsFalse(task.IsCompleted);

                cts.Cancel();
                CharacterLoadResult result = Complete(task);

                Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
                Assert.AreEqual(CharacterLoadCode.DatabaseError, result.Code);
                Assert.IsNull(result.Record);
            }
        }

        [Test]
        public void LoadCharacter_StoreFaults_ReturnsDatabaseErrorAndTaskIsNotFaulted()
        {
            Store(MakeRecord());
            _store.FailNext(new InvalidOperationException("store down"));

            Task<CharacterLoadResult> task = _service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None);
            CharacterLoadResult result = Complete(task);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.AreEqual(CharacterLoadCode.DatabaseError, result.Code);
        }

        // ---------------------------------------------------------------- AC-CP-23 stub

        [Test]
        public void LoadCharacter_FreshStub_LoadsAndAppliesThroughSamePath()
        {
            Complete(_service.CreateStub(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE, CancellationToken.None));

            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(ZoneID.Invalid, result.LastZoneID);

            _service.ApplyLoadedRecord(_entity, result.Record);

            Assert.AreEqual(400, _stats.GetBaseStat(_entity, StatID.MaxHP));
            Assert.AreEqual(30, _stats.GetBaseStat(_entity, StatID.AttackPower));
        }

        // ---------------------------------------------------------------- code-review additions

        [Test]
        public void LoadCharacter_TokenCancelledWithStoreIgnoringToken_ReturnsDatabaseError()
        {
            // The store never completes and never looks at the token: only the service's own race can end the load.
            var pending = new TaskCompletionSource<CharacterRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new CharacterPersistenceService(new ScriptedStore((id, ct) => pending.Task), _stats, _leveling);
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterLoadResult> task = service.LoadCharacter(CHAR_A, ACCOUNT_A, cts.Token);
                Assert.IsFalse(task.IsCompleted);

                cts.Cancel();
                CharacterLoadResult result = Complete(task);

                Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
                Assert.AreEqual(CharacterLoadCode.DatabaseError, result.Code);
                Assert.IsFalse(pending.Task.IsCompleted, "the store's task must still be pending: the service ended the load");
            }
        }

        [Test]
        public void LoadCharacter_AbandonedFetchFaultsLater_ResultStaysDatabaseError()
        {
            var pending = new TaskCompletionSource<CharacterRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new CharacterPersistenceService(new ScriptedStore((id, ct) => pending.Task), _stats, _leveling);
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterLoadResult> task = service.LoadCharacter(CHAR_A, ACCOUNT_A, cts.Token);
                cts.Cancel();
                CharacterLoadResult result = Complete(task);

                Assert.DoesNotThrow(() => pending.SetException(new InvalidOperationException("late store fault")));

                Assert.AreEqual(CharacterLoadCode.DatabaseError, result.Code);
                Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            }
        }

        [Test]
        public void LoadCharacter_TokenAlreadyCancelled_ReturnsDatabaseErrorAfterOneFetch()
        {
            Store(MakeRecord());
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                CharacterLoadResult result = Complete(_service.LoadCharacter(CHAR_A, ACCOUNT_A, cts.Token));

                Assert.AreEqual(CharacterLoadCode.DatabaseError, result.Code);
                Assert.AreEqual(1, _counting.FetchCount);
            }
        }

        [Test]
        public void LoadCharacter_InvalidIdWithCancelledToken_ReturnsCorruptRecordWithNoStoreCall()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                CharacterLoadResult result = Complete(_service.LoadCharacter(CharacterID.Invalid, ACCOUNT_A, cts.Token));

                Assert.AreEqual(CharacterLoadCode.CorruptRecord, result.Code);
                Assert.AreEqual(0, _counting.FetchCount);
            }
        }

        [Test]
        public void LoadCharacter_TokenCancelledAfterFetchCompleted_StaysSuccess()
        {
            Store(MakeRecord());
            using (var cts = new CancellationTokenSource())
            {
                CharacterLoadResult result = Complete(_service.LoadCharacter(CHAR_A, ACCOUNT_A, cts.Token));
                cts.Cancel();

                Assert.AreEqual(CharacterLoadCode.Success, result.Code);
                Assert.IsNotNull(result.Record);
            }
        }

        [Test]
        public void LoadCharacter_StoreReturnsAnotherCharactersRow_ReturnsCorruptRecord()
        {
            CharacterRecord other = MakeRecord();
            other.CharacterID = new CharacterID(102u);
            var service = new CharacterPersistenceService(new ScriptedStore((id, ct) => Task.FromResult(other)), _stats, _leveling);

            CharacterLoadResult result = Complete(service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));

            Assert.AreEqual(CharacterLoadCode.CorruptRecord, result.Code);
            Assert.IsNull(result.Record);
        }

        [Test]
        public void ApplyLoadedRecord_DistinctValues_RaisesOneStatEventPerWrite()
        {
            // Positive control for the "no write" tests: the counter does count writes.
            Store(MakeDistinctRecord());
            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);
            CountStatEvents();

            _service.ApplyLoadedRecord(_entity, result.Record);

            Assert.AreEqual(APPLY_STAT_EVENTS, _statEvents);
        }

        [Test]
        public void ApplyLoadedRecord_LeavesCurrentHpAndMpUntouched()
        {
            _stats.SetBaseStat(_entity, StatID.MaxHP, 1000);
            _stats.SetBaseStat(_entity, StatID.MaxMP, 1000);
            _stats.SetCurrentHP(_entity, 300f);
            _stats.SetCurrentMP(_entity, 200f);
            Store(MakeDistinctRecord());
            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);

            _service.ApplyLoadedRecord(_entity, result.Record);

            Assert.AreEqual(300f, _stats.GetCurrentHP(_entity));
            Assert.AreEqual(200f, _stats.GetCurrentMP(_entity));
        }

        [Test]
        public void ApplyLoadedRecord_HeldFreePointsAboveClassMaximum_ClampedByLevelingWithError()
        {
            // Load accepts any HeldFreePoints >= 0; Leveling clamps to (Level - 1) * FreePointsPerLevel = 9 (AC-LS-30).
            CharacterRecord r = MakeDistinctRecord();
            r.HeldFreePoints = 50;
            Store(r);
            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*heldFreePoints=50.*clamped to 9"));

            _service.ApplyLoadedRecord(_entity, result.Record);

            Assert.AreEqual(9, _leveling.GetHeldFreePoints(_entity));
        }

        [Test]
        public void ApplyLoadedRecord_UnknownClassType_HeldFreePointsClampedToZeroWithError()
        {
            // Pins today's behaviour: an unknown class does not throw; Leveling's zero fallback clamps the points.
            CharacterRecord r = MakeDistinctRecord();
            r.ClassType = UNKNOWN_CLASS_TYPE;
            Store(r);
            CharacterLoadResult result = Load(CHAR_A, ACCOUNT_A);
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(@"\[LevelingService\] RestoreLevelingState:.*heldFreePoints=5.*clamped to 0"));

            _service.ApplyLoadedRecord(_entity, result.Record);

            Assert.AreEqual(0, _leveling.GetHeldFreePoints(_entity));
            Assert.AreEqual(r.Level, _stats.GetBaseStat(_entity, StatID.Level));
        }

        [Test]
        public void Constructor_NullDependency_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(null, _stats, _leveling));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, null, _leveling));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, _stats, null));
        }
    }
}
