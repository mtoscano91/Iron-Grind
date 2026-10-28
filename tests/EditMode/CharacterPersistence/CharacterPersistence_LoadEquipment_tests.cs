using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.EquipmentSystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 005: load step 4 (equipment re-registration) and save step 3 (gear from
    /// <c>ExportSlots</c>). Covers CR-CP-3 step 4, CR-CP-10 step 3, EC-CP-1, EC-CP-2, AC-CP-7, AC-CP-11, AC-CP-12, AC-CP-24.
    /// Real Character Stats, Leveling, Currency and Equipment; a recording fake where only call order matters.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_LoadEquipment_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int WAIT_MS = 5000;
        private const int CAP_THRESHOLD = 300;
        private const byte MAX_LEVEL = 10;

        private static readonly ItemID IronSword = new ItemID(401u);
        private static readonly ItemID BronzeHelmet = new ItemID(402u);
        private static readonly ItemID BronzeChest = new ItemID(403u);
        private static readonly ItemID IronLegs = new ItemID(404u);
        private static readonly ItemID UnknownItem = new ItemID(999u);
        private static readonly ItemID UnknownItem2 = new ItemID(998u);

        private const int SWORD_ATTACK = 12;
        private const int CHEST_MAX_HP = 150;
        private const int BASE_MAX_HP = 500;

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private List<string> _log;
        private InMemoryCharacterRecordStore _store;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private LevelingService _leveling;
        private CurrencySystem _currencySystem;
        private RecordingInventory _inventory;
        private FakeItemDatabase _db;
        private InventoryService _realInventory;
        private InventoryDecorator _equipInventory;
        private EnhancementBonusProvider _bonuses;
        private EquipmentService _equipment;
        private CharacterPersistenceService _service;
        private EntityID _entity;

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _log = new List<string>();
            _store = new InMemoryCharacterRecordStore(() => T1);
            _leveling = new LevelingService(BuildXpThresholds(), CreateClassRegistry());
            _stats = CharacterStatsFixture.CreateWithLeveling(_leveling);
            _leveling.AttachCharacterStats(_stats);
            _entity = CharacterStatsFixture.PlayerEntityId;
            _currencySystem = new CurrencySystem();
            _inventory = new RecordingInventory(_log);
            _inventory.ExportResult = InventorySnapshot.Empty;
            _db = new FakeItemDatabase();
            _realInventory = new InventoryService(_db, () => 0u, MAX_LEVEL);
            _equipInventory = new InventoryDecorator(_realInventory);
            _equipInventory.RegisterCharacter(CHAR_A);
            _bonuses = new EnhancementBonusProvider(EnhancementConfig.Default);
            _equipment = new EquipmentService(_equipInventory, _stats, _db, _bonuses, EnhancementConfig.Default);
            _service = new CharacterPersistenceService(_store, _stats, _leveling, _inventory, _currencySystem, _equipment, new FakeZoneLocator(), () => T1);

            AddItem(IronSword, GearSlot.Weapon, GearTier.Iron, StatID.AttackPower, SWORD_ATTACK);
            AddItem(BronzeHelmet, GearSlot.Helmet, GearTier.Bronze, StatID.Defense, 10);
            AddItem(BronzeChest, GearSlot.Chest, GearTier.Bronze, StatID.MaxHP, CHEST_MAX_HP);
            AddItem(IronLegs, GearSlot.Legs, GearTier.Iron, StatID.Defense, 7);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _created)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }

            _created.Clear();
        }

        // ---------------------------------------------------------------- helpers

        private void AddItem(ItemID id, GearSlot slot, GearTier tier, StatID stat, float flat)
        {
            EquipmentData data = EquipmentData.CreateForTesting(slot, tier, new[] { StatModifierEntry.CreateForTesting(stat, flat) });
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, "Item" + id.RawValue, ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
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

        private static int[] BuildXpThresholds()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD;
            t[61] = int.MaxValue;
            return t;
        }

        private static CharacterRecord MakeRecord()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.MaxHP = BASE_MAX_HP;
            return r;
        }

        private static void SetGear(CharacterRecord r, GearSlot slot, ItemID item, byte level)
        {
            r.GearSlots[(int)slot] = new GearSlotRecord(item, level);
        }

        private static Regex DropRx(GearSlot slot, ItemID item) => new Regex(@"RestoreSlots: .*slot " + slot + " item " + Regex.Escape(item.ToString()));

        private CharacterPersistenceService BuildFakeEquipmentService(out FakeEquipmentService fake)
        {
            fake = new FakeEquipmentService(_log);
            var inventory = new RecordingInventory(_log);
            _inventory = inventory;
            _inventory.ExportResult = InventorySnapshot.Empty;
            return new CharacterPersistenceService(_store, _stats, _leveling, _inventory, _currencySystem, fake);
        }

        private CharacterPersistenceService BuildSavingFakeEquipmentService(out FakeEquipmentService fake, out CountingStore counting)
        {
            fake = new FakeEquipmentService(_log);
            counting = new CountingStore(_store);
            _inventory = new RecordingInventory(_log);
            _inventory.ExportResult = InventorySnapshot.Empty;
            return new CharacterPersistenceService(counting, _stats, _leveling, _inventory, _currencySystem, fake, new FakeZoneLocator(), () => T1);
        }

        private static T Wait<T>(System.Threading.Tasks.Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "task did not complete");
            return task.Result;
        }

        private CharacterLoadResult InsertAndLoad(CharacterRecord record)
        {
            Assert.IsTrue(Wait(_store.InsertAsync(record, CancellationToken.None)));
            return Wait(_service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
        }

        // ---------------------------------------------------------------- validation (LoadCharacter)

        [Test]
        public void test_load_gear_level_10_loads()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, MAX_LEVEL);

            CharacterLoadResult result = InsertAndLoad(r);

            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.IsNotNull(result.Record);
        }

        [Test]
        public void test_load_gear_level_11_is_corrupt_record_and_nothing_applied()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, MAX_LEVEL + 1);
            int maxHpBefore = _stats.GetBaseStat(_entity, StatID.MaxHP);
            int strengthBefore = _stats.GetBaseStat(_entity, StatID.Strength);

            CharacterLoadResult result = InsertAndLoad(r);

            Assert.AreEqual(CharacterLoadCode.CorruptRecord, result.Code);
            Assert.IsNull(result.Record);
            Assert.AreEqual(0, _log.Count);
            Assert.AreEqual(0, _inventory.ImportCalls.Count);
            Assert.AreEqual(maxHpBefore, _stats.GetBaseStat(_entity, StatID.MaxHP));
            Assert.AreEqual(strengthBefore, _stats.GetBaseStat(_entity, StatID.Strength));
            Assert.Throws<InvalidOperationException>(() => _equipment.ExportSlots(CHAR_A), "the character must not be registered with Equipment");
        }

        [Test]
        public void test_load_empty_gear_slot_with_nonzero_level_is_corrupt_record()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Boots, ItemID.Invalid, 3);

            CharacterLoadResult result = InsertAndLoad(r);

            Assert.AreEqual(CharacterLoadCode.CorruptRecord, result.Code);
            Assert.IsNull(result.Record);
        }

        // ---------------------------------------------------------------- call order (step 4)

        [Test]
        public void test_apply_calls_unregister_register_restore_then_import_in_order()
        {
            CharacterPersistenceService service = BuildFakeEquipmentService(out FakeEquipmentService fake);
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, 4);

            CharacterLoadCode code = service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.Success, code);
            Assert.AreEqual(
                new List<string> { FakeEquipmentService.CALL_UNREGISTER, FakeEquipmentService.CALL_REGISTER, FakeEquipmentService.CALL_RESTORE, RecordingInventory.CALL_IMPORT },
                _log);
            Assert.AreEqual(CHAR_A, fake.UnregisterCalls[0]);
            Assert.AreEqual((CHAR_A, _entity), fake.RegisterCalls[0]);
            Assert.AreEqual(EquipmentService.GEAR_SLOT_COUNT, fake.RestoreCalls[0].Length);
            Assert.AreEqual(new SavedGearSlot(IronSword, 4), fake.RestoreCalls[0][(int)GearSlot.Weapon]);
        }

        [Test]
        public void test_apply_all_step3_writes_are_made_before_restore_slots_AC_CP_11()
        {
            CharacterPersistenceService service = BuildFakeEquipmentService(out FakeEquipmentService fake);
            CharacterRecord r = MakeRecord();
            r.Level = 12;
            r.Experience = 40;
            r.STR = 15;
            r.DEX = 11;
            r.VIT = 13;
            r.INT = 6;
            r.MaxMP = 77;
            r.AttackPower = 33;
            r.Defense = 21;
            r.MagicDefense = 9;
            r.CritChance = 0.25f;
            r.CritMultiplier = 1.75f;
            r.AttackRange = 2.5f;
            r.AttackSpeedMultiplier = 1.2f;
            r.MovementSpeed = 4.5f;
            r.HeldFreePoints = 5;
            SetGear(r, GearSlot.Weapon, IronSword, 0);
            StatID[] intStats =
            {
                StatID.Level, StatID.Experience, StatID.Strength, StatID.Dexterity, StatID.Vitality, StatID.Intelligence,
                StatID.MaxHP, StatID.MaxMP, StatID.AttackPower, StatID.Defense, StatID.MagicDefense,
            };
            int[] expectedInts = { r.Level, r.Experience, r.STR, r.DEX, r.VIT, r.INT, r.MaxHP, r.MaxMP, r.AttackPower, r.Defense, r.MagicDefense };
            StatID[] floatStats = { StatID.CritChance, StatID.CritMultiplier, StatID.AttackRange, StatID.AttackSpeedMultiplier, StatID.MovementSpeed };
            float[] expectedFloats = { r.CritChance, r.CritMultiplier, r.AttackRange, r.AttackSpeedMultiplier, r.MovementSpeed };
            var seenInts = new int[intStats.Length];
            var seenFloats = new float[floatStats.Length];
            int held = -1;
            fake.OnRestore = () =>
            {
                for (int i = 0; i < intStats.Length; i++)
                {
                    seenInts[i] = _stats.GetBaseStat(_entity, intStats[i]);
                }

                for (int i = 0; i < floatStats.Length; i++)
                {
                    seenFloats[i] = _stats.GetBaseStatFloat(_entity, floatStats[i]);
                }

                held = _leveling.GetLevelingState(_entity).HeldFreePoints;
            };

            service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(11, intStats.Length);
            Assert.AreEqual(5, floatStats.Length);
            Assert.AreEqual(expectedInts, seenInts);
            Assert.AreEqual(expectedFloats, seenFloats);
            Assert.AreEqual(r.HeldFreePoints, held);
        }

        [Test]
        public void test_apply_all_empty_gear_first_load_registers_and_restores_seven_empty_slots()
        {
            CharacterPersistenceService service = BuildFakeEquipmentService(out FakeEquipmentService fake);

            CharacterLoadCode code = service.ApplyLoadedRecord(_entity, MakeRecord());

            Assert.AreEqual(CharacterLoadCode.Success, code);
            Assert.AreEqual(1, fake.RegisterCalls.Count);
            Assert.AreEqual(1, fake.RestoreCalls.Count);
            Assert.AreEqual(EquipmentService.GEAR_SLOT_COUNT, fake.RestoreCalls[0].Length);
            foreach (SavedGearSlot slot in fake.RestoreCalls[0])
            {
                Assert.AreEqual(SavedGearSlot.Empty, slot);
            }
        }

        [Test]
        public void test_apply_reload_of_same_character_into_second_entity_does_not_throw_and_modifiers_are_on_new_entity()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, 0);
            EntityID second = CharacterStatsFixture.PlayerBEntityId;

            Assert.AreEqual(CharacterLoadCode.Success, _service.ApplyLoadedRecord(_entity, r));
            Assert.DoesNotThrow(() => _service.ApplyLoadedRecord(second, r));

            Assert.AreEqual(_stats.GetBaseStat(second, StatID.AttackPower) + SWORD_ATTACK, _stats.GetEffectiveStat(second, StatID.AttackPower));
        }

        // ---------------------------------------------------------------- effective stat

        [Test]
        public void test_apply_plus7_weapon_effective_attack_is_base_plus_flat_bonus_AC_CP_7()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, 7);

            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.Success, code);
            int expected = _stats.GetBaseStat(_entity, StatID.AttackPower) + _bonuses.GetFlatBonus(7, SWORD_ATTACK, GearTier.Iron);
            Assert.AreEqual(expected, _stats.GetEffectiveStat(_entity, StatID.AttackPower));
            Assert.AreEqual(r.AttackPower, _stats.GetBaseStat(_entity, StatID.AttackPower));
        }

        // ---------------------------------------------------------------- item miss (AC-CP-12 / EC-CP-1)

        [Test]
        public void test_apply_one_unknown_item_returns_item_database_miss_and_applies_everything_else()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, 2);
            SetGear(r, GearSlot.Chest, UnknownItem, 4);
            SetGear(r, GearSlot.Legs, IronLegs, 1);
            r.GoldBalance = 1250u;
            r.GoldVersion = 3u;
            r.InventorySlots[0] = new ItemID(5u);
            r.InventoryItemCounts[0] = 4;
            r.CurrentHP = 321f;
            LogAssert.Expect(LogType.Error, DropRx(GearSlot.Chest, UnknownItem));

            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.ItemDatabaseMiss, code);
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Chest).State);
            Assert.AreEqual(IronSword, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Weapon).ItemId);
            Assert.AreEqual(IronLegs, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Legs).ItemId);
            Assert.AreEqual(1, _inventory.ImportCalls.Count);
            Assert.AreEqual(1250u, _currencySystem.GetBalance(CHAR_A));
            Assert.AreEqual(321f, _stats.GetCurrentHP(_entity));
            Assert.IsTrue(_service.ReleaseSession(CHAR_A, out EntityID released), "session entry created");
            Assert.AreEqual(_entity, released);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_apply_two_unknown_items_returns_item_database_miss_with_two_errors()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Chest, UnknownItem, 1);
            SetGear(r, GearSlot.Legs, UnknownItem2, 0);
            LogAssert.Expect(LogType.Error, DropRx(GearSlot.Chest, UnknownItem));
            LogAssert.Expect(LogType.Error, DropRx(GearSlot.Legs, UnknownItem2));

            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.ItemDatabaseMiss, code);
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Chest).State);
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Legs).State);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_apply_item_for_another_gear_slot_returns_item_database_miss()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, BronzeHelmet, 0);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: .*slot Weapon"));

            CharacterLoadCode code = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.ItemDatabaseMiss, code);
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Weapon).State);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_apply_inventory_not_restored_still_wins_over_item_miss_and_creates_no_session()
        {
            CharacterPersistenceService service = BuildFakeEquipmentService(out FakeEquipmentService fake);
            int droppedBit = 1 << (int)GearSlot.Chest;
            fake.DroppedMask = (byte)droppedBit;
            _inventory.ImportResult = false;

            CharacterLoadCode code = service.ApplyLoadedRecord(_entity, MakeRecord());

            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, code);
            Assert.AreEqual(1, fake.RestoreCalls.Count, "RestoreSlots must have been called before the inventory import failed");
            Assert.IsFalse(service.ReleaseSession(CHAR_A, out _));
        }

        // ---------------------------------------------------------------- HP clamp (AC-CP-24 / EC-CP-2)

        [Test]
        public void test_apply_hp_650_clamps_to_500_after_item_miss_lowers_max_hp_AC_CP_24()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Chest, UnknownItem, 0);
            r.CurrentHP = 650f;
            LogAssert.Expect(LogType.Error, DropRx(GearSlot.Chest, UnknownItem));

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(BASE_MAX_HP, _stats.GetEffectiveStat(_entity, StatID.MaxHP));
            Assert.AreEqual(500f, _stats.GetCurrentHP(_entity));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_apply_dead_on_load_with_max_hp_gear_loads_as_effective_max_hp()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Chest, BronzeChest, 0);
            r.CurrentHP = 0f;

            _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(BASE_MAX_HP + CHEST_MAX_HP, _stats.GetEffectiveStat(_entity, StatID.MaxHP));
            Assert.AreEqual((float)(BASE_MAX_HP + CHEST_MAX_HP), _stats.GetCurrentHP(_entity));
        }

        // ---------------------------------------------------------------- constructors

        [Test]
        public void test_constructor_null_equipment_throws_argument_null_exception()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_store, _stats, _leveling, _inventory, _currencySystem, null));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(
                _store, _stats, _leveling, _inventory, _currencySystem, null, new FakeZoneLocator(), new CharacterWriteQueue(), () => T1, TimeSpan.FromSeconds(5)));
        }

        // ---------------------------------------------------------------- save step 3

        [Test]
        public void test_save_session_after_item_miss_writes_that_slot_empty()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, 2);
            SetGear(r, GearSlot.Chest, UnknownItem, 4);
            CharacterLoadResult loaded = InsertAndLoad(r);
            LogAssert.Expect(LogType.Error, DropRx(GearSlot.Chest, UnknownItem));
            Assert.AreEqual(CharacterLoadCode.ItemDatabaseMiss, _service.ApplyLoadedRecord(_entity, loaded.Record));

            CharacterSaveResult saved = Wait(_service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, saved);
            CharacterRecord stored = Wait(_store.FetchAsync(CHAR_A, CancellationToken.None));
            Assert.AreEqual(ItemID.Invalid, stored.GearSlots[(int)GearSlot.Chest].ItemId);
            Assert.AreEqual((byte)0, stored.GearSlots[(int)GearSlot.Chest].EnhancementLevel);
            Assert.AreEqual(IronSword, stored.GearSlots[(int)GearSlot.Weapon].ItemId);
            Assert.AreEqual((byte)2, stored.GearSlots[(int)GearSlot.Weapon].EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_save_irreversible_outcome_after_item_miss_writes_that_slot_empty()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Legs, UnknownItem, 3);
            CharacterLoadResult loaded = InsertAndLoad(r);
            LogAssert.Expect(LogType.Error, DropRx(GearSlot.Legs, UnknownItem));
            _service.ApplyLoadedRecord(_entity, loaded.Record);

            CharacterSaveResult saved = Wait(_service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, saved);
            CharacterRecord stored = Wait(_store.FetchAsync(CHAR_A, CancellationToken.None));
            Assert.AreEqual(ItemID.Invalid, stored.GearSlots[(int)GearSlot.Legs].ItemId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_save_session_after_equip_in_session_writes_new_item_and_level()
        {
            CharacterLoadResult loaded = InsertAndLoad(MakeRecord());
            Assert.AreEqual(CharacterLoadCode.Success, _service.ApplyLoadedRecord(_entity, loaded.Record));
            MoveItemInResult placed = _realInventory.MoveItemIn(CHAR_A, IronSword, 3);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(CHAR_A, GearSlot.Weapon, IronSword, placed.SlotIndex));

            CharacterSaveResult saved = Wait(_service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, saved);
            CharacterRecord stored = Wait(_store.FetchAsync(CHAR_A, CancellationToken.None));
            Assert.AreEqual(IronSword, stored.GearSlots[(int)GearSlot.Weapon].ItemId);
            Assert.AreEqual((byte)3, stored.GearSlots[(int)GearSlot.Weapon].EnhancementLevel);
        }

        [Test]
        public void test_save_irreversible_outcome_after_equip_in_session_writes_new_item_and_level()
        {
            CharacterLoadResult loaded = InsertAndLoad(MakeRecord());
            Assert.AreEqual(CharacterLoadCode.Success, _service.ApplyLoadedRecord(_entity, loaded.Record));
            MoveItemInResult placed = _realInventory.MoveItemIn(CHAR_A, IronSword, 3);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(CHAR_A, GearSlot.Weapon, IronSword, placed.SlotIndex));

            CharacterSaveResult saved = Wait(_service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, saved);
            CharacterRecord stored = Wait(_store.FetchAsync(CHAR_A, CancellationToken.None));
            Assert.AreEqual(IronSword, stored.GearSlots[(int)GearSlot.Weapon].ItemId);
            Assert.AreEqual((byte)3, stored.GearSlots[(int)GearSlot.Weapon].EnhancementLevel);
        }

        // ---------------------------------------------------------------- failure paths

        [Test]
        public void test_apply_register_character_throws_propagates_and_leaves_no_session()
        {
            CharacterPersistenceService service = BuildFakeEquipmentService(out FakeEquipmentService fake);
            fake.ThrowOnRegister = new InvalidOperationException("register failed");

            Assert.Throws<InvalidOperationException>(() => service.ApplyLoadedRecord(_entity, MakeRecord()));

            Assert.AreEqual(1, fake.RegisterCalls.Count);
            Assert.IsFalse(service.ReleaseSession(CHAR_A, out _));
        }

        [Test]
        public void test_apply_restore_slots_throws_propagates_and_leaves_no_session()
        {
            CharacterPersistenceService service = BuildFakeEquipmentService(out FakeEquipmentService fake);
            fake.ThrowOnRestore = new ArgumentException("restore failed");

            Assert.Throws<ArgumentException>(() => service.ApplyLoadedRecord(_entity, MakeRecord()));

            Assert.AreEqual(1, fake.RestoreCalls.Count);
            Assert.IsFalse(service.ReleaseSession(CHAR_A, out _));
        }

        [Test]
        public void test_save_session_export_slots_throws_gives_database_error_and_no_write()
        {
            CharacterPersistenceService service = BuildSavingFakeEquipmentService(out FakeEquipmentService fake, out CountingStore counting);
            Assert.AreEqual(CharacterLoadCode.Success, service.ApplyLoadedRecord(_entity, MakeRecord()));
            fake.ThrowOnExport = new InvalidOperationException("export failed");
            LogAssert.Expect(LogType.Error, new Regex("could not take its snapshot"));

            CharacterSaveResult saved = Wait(service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, saved);
            Assert.AreEqual(1, fake.ExportCallCount);
            Assert.AreEqual(0, counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_save_session_export_slots_returns_six_slots_gives_database_error_and_no_write()
        {
            CharacterPersistenceService service = BuildSavingFakeEquipmentService(out FakeEquipmentService fake, out CountingStore counting);
            Assert.AreEqual(CharacterLoadCode.Success, service.ApplyLoadedRecord(_entity, MakeRecord()));
            var shortExport = new SavedGearSlot[EquipmentService.GEAR_SLOT_COUNT - 1];
            for (int i = 0; i < shortExport.Length; i++)
            {
                shortExport[i] = SavedGearSlot.Empty;
            }

            fake.ExportOverride = shortExport;
            LogAssert.Expect(LogType.Error, new Regex("could not take its snapshot"));

            CharacterSaveResult saved = Wait(service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, saved);
            Assert.AreEqual(0, counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_apply_reload_after_inventory_not_restored_into_fresh_entity_succeeds_and_restores_gear()
        {
            CharacterRecord r = MakeRecord();
            SetGear(r, GearSlot.Weapon, IronSword, 2);
            EntityID second = CharacterStatsFixture.PlayerBEntityId;
            _inventory.ImportResult = false;

            CharacterLoadCode first = _service.ApplyLoadedRecord(_entity, r);

            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, first);
            _inventory.ImportResult = true;
            CharacterLoadCode retry = CharacterLoadCode.CorruptRecord;
            Assert.DoesNotThrow(() => retry = _service.ApplyLoadedRecord(second, r));

            Assert.AreEqual(CharacterLoadCode.Success, retry);
            Assert.AreEqual(IronSword, _equipment.GetEquipmentSlotState(CHAR_A, GearSlot.Weapon).ItemId);
            Assert.AreEqual((byte)2, _equipment.GetSlotEnhancementLevel(CHAR_A, GearSlot.Weapon));
            Assert.IsTrue(_service.ReleaseSession(CHAR_A, out EntityID released));
            Assert.AreEqual(second, released);
        }
    }
}
