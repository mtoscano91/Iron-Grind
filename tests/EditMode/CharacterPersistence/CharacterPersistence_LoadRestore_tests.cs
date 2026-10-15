using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 004: the inventory, gold, at-cap and current HP / MP steps of
    /// <c>ApplyLoadedRecord</c> (CR-CP-3 steps 5 to 8, F-CP-4, EC-DR-2, AC-CP-10, AC-CP-25).
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_LoadRestore_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const string CALL_IMPORT = RecordingInventory.CALL_IMPORT;
        private const string CALL_CURRENCY = "currency";
        private const int LEVEL_CAP = 60;
        private const int CAP_THRESHOLD = 300;

        /// <summary>Wraps a real <see cref="CurrencySystem"/>, logging each restore and the current HP / MP seen at that moment.</summary>
        private sealed class RecordingCurrencyRestore : ICurrencyStateRestore
        {
            private readonly ICurrencyStateRestore _inner;
            private readonly List<string> _log;
            private readonly Func<(float Hp, float Mp)> _resources;

            public RecordingCurrencyRestore(ICurrencyStateRestore inner, List<string> log, Func<(float Hp, float Mp)> resources)
            {
                _inner = inner;
                _log = log;
                _resources = resources;
            }

            public int CallCount { get; private set; }

            public float HpAtCall { get; private set; } = -1f;

            public float MpAtCall { get; private set; } = -1f;

            public void RestoreCharacter(CharacterID charId, uint balance, uint version)
            {
                CallCount++;
                _log.Add(CALL_CURRENCY);
                (HpAtCall, MpAtCall) = _resources();
                _inner.RestoreCharacter(charId, balance, version);
            }
        }

        private List<string> _log;
        private InMemoryCharacterRecordStore _store;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private LevelingService _leveling;
        private CurrencySystem _currencySystem;
        private RecordingInventory _inventory;
        private RecordingCurrencyRestore _currency;
        private CharacterPersistenceService _service;
        private EntityID _entity;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _store = new InMemoryCharacterRecordStore(() => T1);
            _leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            _stats = CharacterStatsFixture.CreateWithLeveling(_leveling);
            _leveling.AttachCharacterStats(_stats);
            _entity = CharacterStatsFixture.PlayerEntityId;
            _currencySystem = new CurrencySystem();
            Func<(float Hp, float Mp)> resources = () => (_stats.GetCurrentHP(_entity), _stats.GetCurrentMP(_entity));
            _inventory = new RecordingInventory(_log, resources);
            _currency = new RecordingCurrencyRestore(_currencySystem, _log, resources);
            _service = new CharacterPersistenceService(_store, _stats, _leveling, _inventory, _currency);
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

        // 62 entries with the CR-5.3 sentinel at index 61, as in LevelingSystem_LevelCapBehavior_Tests.
        private static int[] BuildXpThresholdsWithCapSentinel()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD; // L59 -> L60
            t[61] = int.MaxValue;
            return t;
        }

        private static CharacterRecord MakeRecord()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            return r;
        }

        private CharacterPersistenceService BuildBareThreeArgService()
        {
            return new CharacterPersistenceService(_store, _stats, _leveling);
        }

        // ---------------------------------------------------------------- inventory (step 5)

        [Test]
        public void ApplyLoadedRecord_ItemsInSlots0And7And19_ImportsThreeEntriesWithIndicesCountsAndLevels()
        {
            CharacterRecord r = MakeRecord();
            r.InventorySlots[0] = new ItemID(5u);
            r.InventoryItemCounts[0] = 4;
            r.InventoryEnhancementLevels[0] = 0;
            r.InventorySlots[7] = new ItemID(12u);
            r.InventoryItemCounts[7] = 1;
            r.InventoryEnhancementLevels[7] = 3;
            r.InventorySlots[19] = new ItemID(31u);
            r.InventoryItemCounts[19] = 99;
            r.InventoryEnhancementLevels[19] = 5;

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(1, _inventory.ImportCalls.Count);
            Assert.AreEqual(CHAR_A, _inventory.ImportCalls[0].CharId);
            IReadOnlyList<InventorySnapshotEntry> slots = _inventory.ImportCalls[0].Snapshot.Slots;
            Assert.AreEqual(3, slots.Count);
            AssertEntry(slots[0], 0, 5u, 4, 0);
            AssertEntry(slots[1], 7, 12u, 1, 3);
            AssertEntry(slots[2], 19, 31u, 99, 5);
        }

        private static void AssertEntry(InventorySnapshotEntry entry, byte slot, uint item, int quantity, byte level)
        {
            Assert.AreEqual(slot, entry.SlotIndex);
            Assert.AreEqual(item, entry.ItemId);
            Assert.AreEqual(quantity, entry.Quantity);
            Assert.AreEqual(level, entry.EnhancementLevel);
        }

        [Test]
        public void ApplyLoadedRecord_EmptyBag_ImportsNonNullEmptySnapshot()
        {
            _service.ApplyLoadedRecord(_entity, MakeRecord());

            Assert.AreEqual(1, _inventory.ImportCalls.Count);
            Assert.IsNotNull(_inventory.ImportCalls[0].Snapshot);
            Assert.AreEqual(0, _inventory.ImportCalls[0].Snapshot.Slots.Count);
        }

        [Test]
        public void ApplyLoadedRecord_RefusedImport_ReturnsInventoryNotRestoredAndWritesNoGoldOrHpOrMp()
        {
            _stats.SetBaseStat(_entity, StatID.MaxHP, 1000);
            _stats.SetBaseStat(_entity, StatID.MaxMP, 1000);
            _stats.SetCurrentHP(_entity, 250f);
            _stats.SetCurrentMP(_entity, 100f);
            _inventory.ImportResult = false;
            CharacterRecord r = MakeRecord();
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.CurrentHP = 333f; // distinct from the pre-set 250 / 100, so a stray write would show
            r.CurrentMP = 77f;

            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, code);
            Assert.AreEqual(0, _currency.CallCount);
            Assert.AreEqual(new List<string> { CALL_IMPORT }, _log);
            Assert.AreEqual(0u, _currencySystem.GetBalance(CHAR_A));
            Assert.AreEqual(250f, _stats.GetCurrentHP(_entity));
            Assert.AreEqual(100f, _stats.GetCurrentMP(_entity));
        }

        // ---------------------------------------------------------------- gold (step 6)

        [Test]
        public void ApplyLoadedRecord_Gold_RestoresBalanceAndRaisesNoGoldSync()
        {
            int syncCount = 0;
            _currencySystem.OnGoldSync += _ => syncCount++;
            CharacterRecord r = MakeRecord();
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(1250u, _currencySystem.GetBalance(CHAR_A));
            Assert.AreEqual(0, syncCount);
        }

        [Test]
        public void ApplyLoadedRecord_Gold_FollowingSpendSucceedsAndItsSyncCarriesVersionTen()
        {
            CharacterRecord r = MakeRecord();
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            _service.ApplyLoadedRecord(_entity, r);
            var syncs = new List<GoldSyncEventArgs>();
            _currencySystem.OnGoldSync += syncs.Add;

            GoldMutationResult result = _currencySystem.TrySpendGold(CHAR_A, 250u, GoldTransactionReason.MonsterDrop);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1000u, _currencySystem.GetBalance(CHAR_A));
            Assert.AreEqual(1, syncs.Count);
            Assert.AreEqual(10u, syncs[0].Version);
        }

        // ---------------------------------------------------------------- at-cap (step 7, AC-CP-10)

        [Test]
        public void ApplyLoadedRecord_Level60_AddExperienceRaisesNoLevelUpAndLevelStays60()
        {
            CharacterRecord r = MakeRecord();
            r.Level = LEVEL_CAP;
            r.Experience = CAP_THRESHOLD;
            _service.ApplyLoadedRecord(_entity, r);
            _leveling.RegisterPlayerEntity(_entity);
            int levelUps = 0;
            _leveling.OnLevelUp += _ => levelUps++;

            _stats.AddExperience(_entity, 500);

            Assert.AreEqual(0, levelUps);
            Assert.AreEqual(LEVEL_CAP, _stats.GetBaseStat(_entity, StatID.Level));
        }

        [Test]
        public void ApplyLoadedRecord_Level59BelowThreshold_CrossingGrantLevelsUpToCap()
        {
            CharacterRecord r = MakeRecord();
            r.Level = LEVEL_CAP - 1;
            r.Experience = CAP_THRESHOLD - 10;
            _service.ApplyLoadedRecord(_entity, r);
            _leveling.RegisterPlayerEntity(_entity);
            int levelUps = 0;
            _leveling.OnLevelUp += _ => levelUps++;

            _stats.AddExperience(_entity, 20);

            Assert.AreEqual(1, levelUps);
            Assert.AreEqual(LEVEL_CAP, _stats.GetBaseStat(_entity, StatID.Level));
        }

        // ---------------------------------------------------------------- HP / MP (step 8)

        [TestCase(0f)]
        [TestCase(-5f)]
        public void ApplyLoadedRecord_DeadOnLoad_RestoresFullEffectiveMaxHp(float savedHp)
        {
            CharacterRecord r = MakeRecord();
            r.MaxHP = 400;
            r.CurrentHP = savedHp;

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(400f, _stats.GetCurrentHP(_entity));
        }

        [TestCase(300f, 400, 300f)]
        [TestCase(400f, 400, 400f)]
        [TestCase(650f, 500, 500f)]
        public void ApplyLoadedRecord_SavedHp_ClampedToEffectiveMaxHp(float savedHp, int maxHp, float expected)
        {
            CharacterRecord r = MakeRecord();
            r.MaxHP = maxHp;
            r.CurrentHP = savedHp;

            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.Success, code);
            Assert.AreEqual(expected, _stats.GetCurrentHP(_entity));
        }

        [TestCase(100f, 220, 100f)]
        [TestCase(300f, 220, 220f)]
        [TestCase(-5f, 220, 0f)]
        public void ApplyLoadedRecord_SavedMp_ClampedToEffectiveMaxMp(float savedMp, int maxMp, float expected)
        {
            CharacterRecord r = MakeRecord();
            r.MaxMP = maxMp;
            r.CurrentMP = savedMp;

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(expected, _stats.GetCurrentMP(_entity));
        }

        // ---------------------------------------------------------------- order and return value

        [Test]
        public void ApplyLoadedRecord_FullApply_CallsImportThenCurrencyRestoreThenWritesHpAndMp()
        {
            CharacterRecord r = MakeRecord();
            r.CurrentHP = 300f;
            r.CurrentMP = 100f;
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(new List<string> { CALL_IMPORT, CALL_CURRENCY }, _log);
            Assert.AreEqual(0f, _inventory.HpAtImport, "HP must not be written before the inventory import");
            Assert.AreEqual(0f, _inventory.MpAtImport, "MP must not be written before the inventory import");
            Assert.AreEqual(0f, _currency.HpAtCall, "HP must not be written before the gold restore");
            Assert.AreEqual(0f, _currency.MpAtCall, "MP must not be written before the gold restore");
            Assert.AreEqual(300f, _stats.GetCurrentHP(_entity));
            Assert.AreEqual(100f, _stats.GetCurrentMP(_entity));
        }

        [Test]
        public void ApplyLoadedRecord_FullApply_ReturnsSuccess()
        {
            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, MakeRecord());

            Assert.AreEqual(CharacterLoadCode.Success, code);
        }

        [Test]
        public void ApplyLoadedRecord_RetryAfterRefusedImport_ReturnsSuccessAndRestoresGoldAndHp()
        {
            CharacterRecord r = MakeRecord();
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.CurrentHP = 300f;
            _inventory.ImportResult = false;
            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, _service.ApplyLoadedRecord(_entity, r));

            _inventory.ImportResult = true;
            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.Success, code);
            Assert.AreEqual(2, _inventory.ImportCalls.Count);
            Assert.AreEqual(1, _currency.CallCount);
            Assert.AreEqual(1250u, _currencySystem.GetBalance(CHAR_A));
            Assert.AreEqual(300f, _stats.GetCurrentHP(_entity));
            Assert.AreEqual(0, _leveling.GetHeldFreePoints(_entity));
        }

        // ---------------------------------------------------------------- construction

        [Test]
        public void Constructor_FiveArgNullDependency_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(null, _stats, _leveling, _inventory, _currency));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, null, _leveling, _inventory, _currency));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, _stats, null, _inventory, _currency));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, _stats, _leveling, null, _currency));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, _stats, _leveling, _inventory, null));
        }

        [Test]
        public void ApplyLoadedRecord_ThreeArgService_ThrowsInvalidOperationExceptionAndWritesNothing()
        {
            CharacterPersistenceService bare = BuildBareThreeArgService();

            Assert.Throws<InvalidOperationException>(() => bare.ApplyLoadedRecord(_entity, MakeRecord()));
            Assert.AreEqual(0, _stats.GetBaseStat(_entity, StatID.MaxHP));
            Assert.AreEqual(0, _log.Count);
        }
    }
}
