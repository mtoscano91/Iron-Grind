using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Stats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.EquipmentSystem
{
    /// <summary>
    /// EditMode unit tests for Equipment System Story 002 — equip into an empty slot, unequip and
    /// enhanced modifier registration (CR-EQS-5, CR-EQS-7, "Enhanced modifier registration" rule;
    /// TR-equip-003, TR-equip-004). Real <c>CharacterStats</c> and <c>InventoryService</c>; an
    /// inventory decorator injects <c>MoveItemIn</c> / <c>MoveItemOut</c> failures.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_EquipUnequip_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const int BAG_SIZE = 20;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);

        private static readonly ItemID SwordId = new ItemID(101u);
        private static readonly ItemID OtherSwordId = new ItemID(102u);
        private static readonly ItemID HelmetId = new ItemID(201u);
        private static readonly ItemID FillerId = new ItemID(900u);

        private static readonly GearSlot[] AllSlots =
        {
            GearSlot.Weapon, GearSlot.Helmet, GearSlot.Chest, GearSlot.Legs, GearSlot.Boots, GearSlot.Ring, GearSlot.Necklace,
        };

        private FakeItemDatabase _db;
        private InventoryService _realInventory;
        private InventoryDecorator _inventory;
        private Stats _stats;
        private EquipmentService _equipment;
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private int _statEvents;

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _statEvents = 0;
            _db = new FakeItemDatabase();
            _realInventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory = new InventoryDecorator(_realInventory);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, 50);
            _stats.SetBaseStat(PlayerEntity, StatID.Defense, 30);
            _stats.SetBaseStat(PlayerEntity, StatID.MaxHP, 500);
            _stats.SetBaseStat(PlayerEntity, StatID.Dexterity, 20);
            _stats.SetBaseStat(PlayerEntity, StatID.Vitality, 20);
            BuildEquipment(new EnhancementBonusProvider(EnhancementConfig.Default));
            AddItem(FillerId, GearSlot.Weapon, GearTier.Bronze);
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

        // ── Helpers ────────────────────────────────────────────────────────────────────────

        private void BuildEquipment(IEnhancementBonusProvider bonuses)
        {
            _equipment = new EquipmentService(_inventory, _stats, _db, bonuses, EnhancementConfig.Default);
            _equipment.RegisterCharacter(Player, PlayerEntity);
        }

        private static StatModifierEntry Mod(StatID stat, float flat) => StatModifierEntry.CreateForTesting(stat, flat);

        private ItemDefinition AddItem(ItemID id, GearSlot slot, GearTier tier, params StatModifierEntry[] modifiers)
        {
            EquipmentData data = EquipmentData.CreateForTesting(slot, tier, modifiers);
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, $"Item{id.RawValue}", ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
            return definition;
        }

        // Places an item in the lowest free bag slot at the given level and returns that slot.
        private int PutInBag(ItemID id, byte level = 0)
        {
            MoveItemInResult placed = _realInventory.MoveItemIn(Player, id, level);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            return placed.SlotIndex;
        }

        private void FillBag()
        {
            for (int i = _realInventory.FilledSlots(Player); i < BAG_SIZE; i++)
            {
                PutInBag(FillerId);
            }

            Assert.IsTrue(_realInventory.IsFull(Player), "test setup: bag full");
        }

        private int Effective(StatID stat) => _stats.GetEffectiveStat(PlayerEntity, stat);

        private void CountStatEvents()
        {
            Stats.StatChangedHandler handler = (entity, stat) => _statEvents++;
            _stats.Subscribe(handler);
        }

        private void AssertSlot(GearSlot slot, ItemID item, SlotState state, int level)
        {
            EquipmentSlotState read = _equipment.GetEquipmentSlotState(Player, slot);
            Assert.AreEqual(item, read.ItemId, $"{slot}: item");
            Assert.AreEqual(state, read.State, $"{slot}: state");
            Assert.AreEqual(level, _equipment.GetSlotEnhancementLevel(Player, slot), $"{slot}: level");
            Assert.AreEqual(state == SlotState.Transitioning, _equipment.IsSlotTransitioning(Player, slot), $"{slot}: transitioning");
        }

        private static Regex ItemLog(string text, ItemID id) => new Regex($"^(?=.*{Regex.Escape(text)})(?=.*{Regex.Escape(id.ToString())})");

        // Matches "[EquipmentService] {item} stat {stat}: ... {text}".
        private static Regex ItemStatLog(string text, ItemID id, StatID stat) =>
            new Regex($@"\[EquipmentService\] {Regex.Escape(id.ToString())} stat {stat}: .*{Regex.Escape(text)}");

        // Records, on every OnStatChanged, the slot's state and transitioning flag as the HUD would read them.
        private List<(SlotState State, bool Transitioning)> RecordSlotOnStatEvents(GearSlot slot)
        {
            var seen = new List<(SlotState, bool)>();
            Stats.StatChangedHandler handler = (entity, stat) =>
                seen.Add((_equipment.GetEquipmentSlotState(Player, slot).State, _equipment.IsSlotTransitioning(Player, slot)));
            _stats.Subscribe(handler);
            return seen;
        }

        // ── Equip into an empty slot ───────────────────────────────────────────────────────

        [Test]
        public void test_Equip_EmptySlot_RegistersEveryModifier_AC_EQS_1()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f), Mod(StatID.Defense, 8f));
            int bagSlot = PutInBag(SwordId);
            Assert.AreEqual(50, Effective(StatID.Strength));
            Assert.AreEqual(30, Effective(StatID.Defense));

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(60, Effective(StatID.Strength));
            Assert.AreEqual(38, Effective(StatID.Defense));
            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 0);
            Assert.IsTrue(_realInventory.GetSlot(Player, bagSlot).IsEmpty, "the item left the bag");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_Weapon_ReadSurfaceReportsOccupiedSlot()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Iron, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId, 4);

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot));

            IEquippedWeaponQuery query = _equipment;
            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 4);
            Assert.AreEqual(SwordId, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(4, _equipment.GetEquippedWeaponEnhancementLevel(Player));
            Assert.AreEqual(SwordId, query.GetEquippedWeaponID(PlayerEntity));
            Assert.AreEqual(4, query.GetEquippedWeaponEnhancementLevel(PlayerEntity));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(GearSlot.Weapon)]
        [TestCase(GearSlot.Helmet)]
        [TestCase(GearSlot.Chest)]
        [TestCase(GearSlot.Legs)]
        [TestCase(GearSlot.Boots)]
        [TestCase(GearSlot.Ring)]
        [TestCase(GearSlot.Necklace)]
        public void test_Equip_EverySlot_ReadSurfaceReportsOccupiedSlotAtItsLevel(GearSlot slot)
        {
            AddItem(HelmetId, slot, GearTier.Iron, Mod(StatID.Defense, 5f));
            int bagSlot = PutInBag(HelmetId, 2);

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, slot, HelmetId, bagSlot));

            AssertSlot(slot, HelmetId, SlotState.Occupied, 2);
            foreach (GearSlot other in AllSlots)
            {
                if (other != slot)
                {
                    AssertSlot(other, ItemID.Invalid, SlotState.Empty, 0);
                }
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_Weapon_ReportsItemOnRepeatedCalls_AC_EQS_13()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId);
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));

            _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(SwordId, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(SwordId, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(SwordId, _equipment.GetEquippedWeaponItemID(Player));
        }

        [Test]
        public void test_Equip_AllSevenSlots_EquipIndependentlyAndStatsSum_AC_EQS_14()
        {
            var items = new[]
            {
                (GearSlot.Weapon, new ItemID(301u), StatID.Strength, 10f),
                (GearSlot.Helmet, new ItemID(302u), StatID.Defense, 4f),
                (GearSlot.Chest, new ItemID(303u), StatID.Defense, 6f),
                (GearSlot.Legs, new ItemID(304u), StatID.MaxHP, 10f),
                (GearSlot.Boots, new ItemID(305u), StatID.Dexterity, 3f),
                (GearSlot.Ring, new ItemID(306u), StatID.Strength, 2f),
                (GearSlot.Necklace, new ItemID(307u), StatID.Vitality, 1f),
            };
            var bagSlots = new int[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                AddItem(items[i].Item2, items[i].Item1, GearTier.Bronze, Mod(items[i].Item3, items[i].Item4));
                bagSlots[i] = PutInBag(items[i].Item2);
            }

            for (int i = 0; i < items.Length; i++)
            {
                Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, items[i].Item1, items[i].Item2, bagSlots[i]), $"{items[i].Item1}");
            }

            foreach (var item in items)
            {
                AssertSlot(item.Item1, item.Item2, SlotState.Occupied, 0);
            }

            Assert.AreEqual(50 + 10 + 2, Effective(StatID.Strength));
            Assert.AreEqual(30 + 4 + 6, Effective(StatID.Defense));
            Assert.AreEqual(500 + 10, Effective(StatID.MaxHP));
            Assert.AreEqual(20 + 3, Effective(StatID.Dexterity));
            Assert.AreEqual(20 + 1, Effective(StatID.Vitality));
            Assert.AreEqual(0, _realInventory.FilledSlots(Player), "all 7 items left the bag");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_EnhancedItem_KeepsLevelThroughEquipAndUnequip_AC_EQS_17()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Iron, Mod(StatID.Strength, 10f));
            PutInBag(FillerId); // slot 0
            int swordSlot = PutInBag(SwordId, 5); // slot 1
            _realInventory.RemoveItem(Player, 0); // slot 0 is now the lowest free slot, not swordSlot
            Assert.AreEqual(1, swordSlot);

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, SwordId, swordSlot));

            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 5);
            Assert.AreEqual(5, _equipment.GetEquippedWeaponEnhancementLevel(Player));
            Assert.IsTrue(_realInventory.GetSlot(Player, swordSlot).IsEmpty, "source slot empty");

            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Weapon));

            InventorySlot returned = _realInventory.GetSlot(Player, 0);
            Assert.AreEqual(SwordId, returned.ItemId, "lowest free slot holds the sword");
            Assert.AreEqual(5, returned.EnhancementLevel);
            Assert.IsTrue(_realInventory.GetSlot(Player, swordSlot).IsEmpty, "the original slot is not reused");
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            Assert.AreEqual(0, _equipment.GetEquippedWeaponEnhancementLevel(Player));
            Assert.AreEqual(50, Effective(StatID.Strength));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_EnhancedItem_PassesBonusProviderResultToCharacterStats_AC_EQS_29()
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId, 5);

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot));

            Assert.AreEqual(28, Effective(StatID.Strength) - 50, "the provider's value, not the raw 10");
            Assert.AreEqual(1, bonuses.CallCount);
            Assert.AreEqual(5, bonuses.LastLevel);
            Assert.AreEqual(10, bonuses.LastBaseFlat);
            Assert.AreEqual(GearTier.Bronze, bonuses.LastTier);
        }

        [Test]
        public void test_Equip_LevelZeroItem_PassesBonusProviderResultToCharacterStats_AC_EQS_29()
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId, 0);

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot));

            Assert.AreEqual(10, Effective(StatID.Strength) - 50);
            Assert.AreEqual(0, bonuses.LastLevel);
            Assert.AreEqual(10, bonuses.LastBaseFlat);
            Assert.AreEqual(GearTier.Bronze, bonuses.LastTier);
        }

        [Test]
        public void test_Equip_LockedBagSlot_ReturnsItemLockedAndChangesNothing_AC_EQS_32()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Iron, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId, 2);
            _realInventory.LockSlot(Player, bagSlot);
            CountStatEvents();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.ItemLocked, result);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            InventorySlot kept = _realInventory.GetSlot(Player, bagSlot);
            Assert.AreEqual(SwordId, kept.ItemId);
            Assert.AreEqual(2, kept.EnhancementLevel);
            Assert.IsTrue(_realInventory.IsSlotLocked(Player, bagSlot), "still locked");
            Assert.AreEqual(50, Effective(StatID.Strength));
            Assert.AreEqual(0, _statEvents, "no AddEquipmentModifier, so no OnStatChanged");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_MoveItemOutReportsSlotEmpty_ReturnsItemNotInInventoryWithNoStatCall_AC_EQS_32()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Iron, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId);
            _inventory.MoveItemOutOverride = MoveItemOutCode.SlotEmpty;
            CountStatEvents();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            Assert.AreEqual(50, Effective(StatID.Strength));
            Assert.AreEqual(0, _statEvents);
            Assert.AreEqual(SwordId, _realInventory.GetSlot(Player, bagSlot).ItemId, "the bag is untouched");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_FractionalFlatBonus_LogsOnceAndRegistersRoundedValue()
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10.6f));
            int bagSlot = PutInBag(SwordId, 0);

            LogAssert.Expect(LogType.Error, ItemStatLog("not a whole number", SwordId, StatID.Strength));
            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(11, bonuses.LastBaseFlat);
            Assert.AreEqual(61, Effective(StatID.Strength));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_WholeNumberFlatBonus_LogsNothing()
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10.0f));
            int bagSlot = PutInBag(SwordId, 0);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(10, bonuses.LastBaseFlat);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_OccupiedSlot_ThrowsNotSupportedAndLeavesBagUntouched()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f));
            AddItem(OtherSwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 20f));
            int first = PutInBag(SwordId);
            int second = PutInBag(OtherSwordId);
            _equipment.Equip(Player, GearSlot.Weapon, SwordId, first);

            Assert.Throws<NotSupportedException>(() => _equipment.Equip(Player, GearSlot.Weapon, OtherSwordId, second));

            Assert.AreEqual(OtherSwordId, _realInventory.GetSlot(Player, second).ItemId);
            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 0);
            Assert.AreEqual(60, Effective(StatID.Strength));
        }

        [Test]
        public void test_EquipAndUnequip_UnregisteredCharacter_LogAndReturnNoChange()
        {
            var unknown = new CharacterID(9999u);

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: CharacterID\(9999\) is not a registered character\."));
            Assert.AreEqual(EquipResult.NoChange, _equipment.Equip(unknown, GearSlot.Weapon, SwordId, 0));
            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Unequip: CharacterID\(9999\) is not a registered character\."));
            Assert.AreEqual(EquipResult.NoChange, _equipment.Unequip(unknown, GearSlot.Weapon));
            LogAssert.NoUnexpectedReceived();
        }

        // ── Unequip ────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_Unequip_OccupiedSlot_ReturnsItemToBagAndRemovesModifiers_AC_EQS_3()
        {
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Bronze, Mod(StatID.MaxHP, 12f));
            int bagSlot = PutInBag(HelmetId);
            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, bagSlot);
            Assert.AreEqual(512, Effective(StatID.MaxHP));

            EquipResult result = _equipment.Unequip(Player, GearSlot.Helmet);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(500, Effective(StatID.MaxHP));
            Assert.IsTrue(_realInventory.HasItem(Player, HelmetId));
            AssertSlot(GearSlot.Helmet, ItemID.Invalid, SlotState.Empty, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_FullBag_ReturnsInventoryFullAndChangesNothing_AC_EQS_7()
        {
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Iron, Mod(StatID.MaxHP, 12f));
            int bagSlot = PutInBag(HelmetId, 3);
            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, bagSlot);
            FillBag();
            int before = Effective(StatID.MaxHP);
            CountStatEvents();

            EquipResult result = _equipment.Unequip(Player, GearSlot.Helmet);

            Assert.AreEqual(EquipResult.InventoryFull, result);
            Assert.AreEqual(before, Effective(StatID.MaxHP));
            AssertSlot(GearSlot.Helmet, HelmetId, SlotState.Occupied, 3);
            Assert.AreEqual(0, _statEvents, "no RemoveEquipmentModifier");
            Assert.AreEqual(0, _inventory.MoveItemInCalls, "MoveItemIn was never reached");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_EmptySlot_ReturnsNoChange()
        {
            CountStatEvents();

            EquipResult result = _equipment.Unequip(Player, GearSlot.Chest);

            Assert.AreEqual(EquipResult.NoChange, result);
            AssertSlot(GearSlot.Chest, ItemID.Invalid, SlotState.Empty, 0);
            Assert.AreEqual(0, _statEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_MoveItemInFails_RestoresEnhancedModifiersAndReturnsInventoryError()
        {
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Iron, Mod(StatID.Defense, 8f));
            int bagSlot = PutInBag(HelmetId, 3);
            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, bagSlot);
            int before = Effective(StatID.Defense);
            int expected = 30 + new EnhancementBonusProvider(EnhancementConfig.Default).GetFlatBonus(3, 8, GearTier.Iron);
            Assert.AreEqual(expected, before, "the equipped value includes the level 3 enhancement");
            _inventory.FailMoveItemIn = true;
            CountStatEvents();

            EquipResult result = _equipment.Unequip(Player, GearSlot.Helmet);

            Assert.AreEqual(EquipResult.InventoryError, result);
            Assert.AreEqual(1, _inventory.MoveItemInCalls);
            Assert.GreaterOrEqual(_statEvents, 2, "removed, then re-added");
            Assert.AreEqual(expected, Effective(StatID.Defense), "modifiers re-added once, at the slot's level");
            AssertSlot(GearSlot.Helmet, HelmetId, SlotState.Occupied, 3);
            Assert.IsFalse(_equipment.IsSlotTransitioning(Player, GearSlot.Helmet));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_ItemDefinitionMissing_LogsClearsSlotAndReturnsSuccess()
        {
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Bronze, Mod(StatID.Defense, 8f));
            int bagSlot = PutInBag(HelmetId, 2);
            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, bagSlot);
            int equipped = Effective(StatID.Defense);
            _db.Remove(HelmetId);
            CountStatEvents();

            LogAssert.Expect(LogType.Error, ItemLog("Unequip GetItem null", HelmetId));
            EquipResult result = _equipment.Unequip(Player, GearSlot.Helmet);

            Assert.AreEqual(EquipResult.Success, result);
            AssertSlot(GearSlot.Helmet, ItemID.Invalid, SlotState.Empty, 0);
            Assert.AreEqual(0, _statEvents, "no RemoveEquipmentModifier");
            Assert.AreEqual(equipped, Effective(StatID.Defense), "modifiers of an undefined item are left alone");
            Assert.IsFalse(_realInventory.HasItem(Player, HelmetId), "the item is not returned to the bag");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_ItemDefinitionMissingAndBagFull_ReturnsInventoryFullWithoutLog()
        {
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Bronze, Mod(StatID.Defense, 8f));
            int bagSlot = PutInBag(HelmetId, 2);
            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, bagSlot);
            FillBag();
            _db.Remove(HelmetId);

            EquipResult result = _equipment.Unequip(Player, GearSlot.Helmet);

            Assert.AreEqual(EquipResult.InventoryFull, result, "CR-EQS-7 step 1 runs before the GetItem check");
            AssertSlot(GearSlot.Helmet, HelmetId, SlotState.Occupied, 2);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_SlotSeesTransitioning_AndEquipSeesNotTransitioning()
        {
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Bronze, Mod(StatID.Defense, 8f), Mod(StatID.MaxHP, 5f));
            int bagSlot = PutInBag(HelmetId);
            var duringEquip = RecordSlotOnStatEvents(GearSlot.Helmet);

            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, bagSlot);

            Assert.AreEqual(2, duringEquip.Count);
            Assert.IsTrue(duringEquip.TrueForAll(s => !s.Transitioning && s.State != SlotState.Transitioning), "equip events: slot is not transitioning");

            duringEquip.Clear();
            _equipment.Unequip(Player, GearSlot.Helmet);

            Assert.AreEqual(2, duringEquip.Count);
            Assert.IsTrue(duringEquip.TrueForAll(s => s.Transitioning && s.State == SlotState.Transitioning), "unequip events: slot is transitioning");
            AssertSlot(GearSlot.Helmet, ItemID.Invalid, SlotState.Empty, 0);
        }

        [Test]
        public void test_Unequip_OneOfSeveralItems_RemovesOnlyItsOwnModifiers()
        {
            ItemID ringId = new ItemID(202u);
            ItemID chestId = new ItemID(203u);
            AddItem(HelmetId, GearSlot.Helmet, GearTier.Bronze, Mod(StatID.Defense, 4f));
            AddItem(chestId, GearSlot.Chest, GearTier.Bronze, Mod(StatID.Defense, 6f));
            AddItem(ringId, GearSlot.Ring, GearTier.Bronze, Mod(StatID.Strength, 3f), Mod(StatID.Dexterity, 2f));
            _equipment.Equip(Player, GearSlot.Helmet, HelmetId, PutInBag(HelmetId));
            _equipment.Equip(Player, GearSlot.Chest, chestId, PutInBag(chestId));
            _equipment.Equip(Player, GearSlot.Ring, ringId, PutInBag(ringId));
            Assert.AreEqual(40, Effective(StatID.Defense));

            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Helmet));

            Assert.AreEqual(36, Effective(StatID.Defense), "exactly the helmet's 4 removed");
            AssertSlot(GearSlot.Chest, chestId, SlotState.Occupied, 0);

            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Ring));

            Assert.AreEqual(50, Effective(StatID.Strength));
            Assert.AreEqual(20, Effective(StatID.Dexterity));
            Assert.AreEqual(36, Effective(StatID.Defense));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_ItemWithoutEquipmentData_ReturnsSlotMismatchAndTouchesNothing()
        {
            ItemDefinition definition = ItemDefinitionBuilder.Build(SwordId.RawValue, "NoData", ItemCategory.Equipment, equipmentData: null);
            _created.Add(definition);
            _db.Add(definition);
            int bagSlot = PutInBag(SwordId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            Assert.AreEqual(SwordId, _realInventory.GetSlot(Player, bagSlot).ItemId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_UnknownItemId_ReturnsItemNotInInventoryAndTouchesNothing()
        {
            ItemID unknown = new ItemID(777u);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId);

            // The bag cannot hold an id the Item Database does not know, so the stale-render guard rejects it.
            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, unknown, bagSlot);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            Assert.AreEqual(SwordId, _realInventory.GetSlot(Player, bagSlot).ItemId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_EmptyBagSlot_RealInventory_ReturnsItemNotInInventoryWithNoStatEvent()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10f));
            CountStatEvents();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, 5);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            Assert.AreEqual(0, _statEvents);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            Assert.AreEqual(50, Effective(StatID.Strength));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_FractionalFlatBonusAndMoveItemInFails_DefectLogsOnEquipAndOnRollback()
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, 10.6f));
            int bagSlot = PutInBag(SwordId);

            LogAssert.Expect(LogType.Error, ItemStatLog("not a whole number", SwordId, StatID.Strength));
            _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);
            _inventory.FailMoveItemIn = true;
            LogAssert.Expect(LogType.Error, ItemStatLog("not a whole number", SwordId, StatID.Strength));
            EquipResult result = _equipment.Unequip(Player, GearSlot.Weapon);

            Assert.AreEqual(EquipResult.InventoryError, result);
            Assert.AreEqual(61, Effective(StatID.Strength));
            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_NaNFlatBonus_LogsSkipsThatModifierAndRegistersTheValidOne()
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Defense, float.NaN), Mod(StatID.Strength, 10f));
            int bagSlot = PutInBag(SwordId);

            LogAssert.Expect(LogType.Error, ItemStatLog("not a usable number", SwordId, StatID.Defense));
            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(60, Effective(StatID.Strength), "the valid modifier is registered");
            Assert.AreEqual(30, Effective(StatID.Defense), "the NaN modifier is not");
            Assert.AreEqual(1, bonuses.CallCount);
            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(10.5f, 11)]
        [TestCase(-10.5f, -11)]
        public void test_Equip_ExactHalfFlatBonus_RoundsAwayFromZero(float flat, int expectedBase)
        {
            var bonuses = new StubBonusProvider();
            BuildEquipment(bonuses);
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze, Mod(StatID.Strength, flat));
            int bagSlot = PutInBag(SwordId);

            LogAssert.Expect(LogType.Error, ItemStatLog("not a whole number", SwordId, StatID.Strength));
            _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot);

            Assert.AreEqual(expectedBase, bonuses.LastBaseFlat);
            Assert.AreEqual(50 + expectedBase, Effective(StatID.Strength));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_EquipAndUnequip_ItemWithNoModifiers_SucceedsWithoutStatEvents()
        {
            AddItem(SwordId, GearSlot.Weapon, GearTier.Bronze);
            int bagSlot = PutInBag(SwordId);
            CountStatEvents();

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, SwordId, bagSlot));
            AssertSlot(GearSlot.Weapon, SwordId, SlotState.Occupied, 0);
            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Weapon));

            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty, 0);
            Assert.IsTrue(_realInventory.HasItem(Player, SwordId));
            Assert.AreEqual(0, _statEvents);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Fakes ──────────────────────────────────────────────────────────────────────────

        // Returns 28 for (5, 10, Bronze) and the base value otherwise; records the last call.
        private sealed class StubBonusProvider : IEnhancementBonusProvider
        {
            public int CallCount;
            public int LastLevel = -1;
            public int LastBaseFlat = int.MinValue;
            public GearTier LastTier;

            public int GetFlatBonus(int level, int baseFlatBonus, GearTier gearTier)
            {
                CallCount++;
                LastLevel = level;
                LastBaseFlat = baseFlatBonus;
                LastTier = gearTier;
                return level == 5 && baseFlatBonus == 10 && gearTier == GearTier.Bronze ? 28 : baseFlatBonus;
            }

            public int GetElementalBonus(int level, int baseElementalDamage, GearTier gearTier, bool isWeapon) => 0;
        }
    }
}
