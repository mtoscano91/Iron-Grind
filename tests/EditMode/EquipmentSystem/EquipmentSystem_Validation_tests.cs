using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
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
    /// EditMode unit tests for Equipment System Story 003 — the validation guards that run before
    /// any equip mutation: slot range, <c>ItemID.Invalid</c>, stale-render guard, slot type, same-item
    /// guard and stat gate (CR-EQS-2, CR-EQS-3, CR-EQS-4, CR-EQS-13, F-EQS-3; TR-equip-002).
    /// Real <c>CharacterStats</c> and <c>InventoryService</c>; <see cref="InventoryDecorator"/>
    /// counts <c>MoveItemOut</c> / <c>MoveItemIn</c>.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_Validation_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;

        // Story boundary values (AC-EQS-4/5/6/15).
        private const int BASE_STR = 62;
        private const float SWORD_MIN_STR = 85f;
        private const int SWORD_STR_BONUS = 15;
        private const int STR_BUFF = 30;
        private const int BASE_MAX_HP = 280;
        private const float HELMET_MIN_MAX_HP = 350f;
        private const float BOOTS_MIN_MOVE_SPEED = 1.5f;
        private const int LAST_BAG_INDEX = 19;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);

        private static readonly ItemID DarkSteelSwordId = new ItemID(101u);
        private static readonly ItemID IronSwordId = new ItemID(102u);
        private static readonly ItemID IronHelmetId = new ItemID(201u);
        private static readonly ItemID RingId = new ItemID(301u);
        private static readonly ItemID BootsId = new ItemID(401u);
        private static readonly ItemID PotionId = new ItemID(501u);

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
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            _stats.SetBaseStat(PlayerEntity, StatID.MaxHP, BASE_MAX_HP);
            _equipment = Build(throwOnInvalidItem: false);

            AddItem(DarkSteelSwordId, GearSlot.Weapon, StatID.Strength, SWORD_MIN_STR, Mod(StatID.Strength, SWORD_STR_BONUS));
            AddItem(IronSwordId, GearSlot.Weapon, null, 0f, Mod(StatID.Strength, 10f));
            AddItem(IronHelmetId, GearSlot.Helmet, StatID.MaxHP, HELMET_MIN_MAX_HP, Mod(StatID.Defense, 5f));
            AddItem(RingId, GearSlot.Ring, null, 0f, Mod(StatID.Dexterity, 3f));

            Stats.StatChangedHandler handler = (entity, stat) => _statEvents++;
            _stats.Subscribe(handler);
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

        private EquipmentService Build(bool throwOnInvalidItem)
        {
            var service = new EquipmentService(
                _inventory, _stats, _db, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default, throwOnInvalidItem);
            service.RegisterCharacter(Player, PlayerEntity);
            return service;
        }

        private static StatModifierEntry Mod(StatID stat, float flat) => StatModifierEntry.CreateForTesting(stat, flat);

        private void AddItem(ItemID id, GearSlot slot, StatID? requirementStat, float requirementMin, params StatModifierEntry[] modifiers)
        {
            EquipmentData data = EquipmentData.CreateForTesting(
                slot, GearTier.Iron, modifiers, equipRequirementStat: requirementStat, equipRequirementMin: requirementMin);
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, $"Item{id.RawValue}", ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
        }

        private int PutInBag(ItemID id, byte level = 0)
        {
            MoveItemInResult placed = _realInventory.MoveItemIn(Player, id, level);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            return placed.SlotIndex;
        }

        private int FindInBag(ItemID id)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_realInventory.GetSlot(Player, i).ItemId == id)
                {
                    return i;
                }
            }

            Assert.Fail($"test setup: {id} is not in the bag");
            return -1;
        }

        // Puts the item in the bag and equips it.
        private void EquipForSetup(GearSlot slot, ItemID id, byte level = 0)
        {
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, slot, id, PutInBag(id, level)), "test setup: equip");
        }

        // Zeroes the Inventory call counters and the OnStatChanged count (subscribed in SetUp) right
        // before the act step, so setup writes are not counted.
        private void ResetCounters()
        {
            _inventory.MoveItemOutCalls = 0;
            _inventory.MoveItemInCalls = 0;
            _statEvents = 0;
        }

        private EquipmentService BuildDevMode()
        {
            _equipment.UnregisterCharacter(Player);
            return Build(throwOnInvalidItem: true);
        }

        private int Effective(StatID stat) => _stats.GetEffectiveStat(PlayerEntity, stat);

        private void AssertSlot(GearSlot slot, ItemID item, SlotState state)
        {
            EquipmentSlotState read = _equipment.GetEquipmentSlotState(Player, slot);
            Assert.AreEqual(item, read.ItemId, $"{slot}: item");
            Assert.AreEqual(state, read.State, $"{slot}: state");
        }

        private void AssertBag(int bagSlot, ItemID item)
        {
            Assert.AreEqual(item, _realInventory.GetSlot(Player, bagSlot).ItemId, $"bag slot {bagSlot}");
        }

        private void AssertNoMutation()
        {
            Assert.AreEqual(0, _inventory.MoveItemOutCalls, "MoveItemOut calls");
            Assert.AreEqual(0, _inventory.MoveItemInCalls, "MoveItemIn calls");
            Assert.AreEqual(0, _statEvents, "OnStatChanged events");
        }

        // ── Stat gate (CR-EQS-3, F-EQS-3) ──────────────────────────────────────────────────

        [Test]
        public void test_Equip_BaseStrBelowRequirement_ReturnsStatRequirementNotMet_AC_EQS_4()
        {
            int bagSlot = PutInBag(DarkSteelSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            AssertBag(bagSlot, DarkSteelSwordId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            Assert.AreEqual(BASE_STR, Effective(StatID.Strength));
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_BaseStrExactlyAtRequirement_Succeeds_AC_EQS_5()
        {
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, (int)SWORD_MIN_STR);
            int bagSlot = PutInBag(DarkSteelSwordId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertSlot(GearSlot.Weapon, DarkSteelSwordId, SlotState.Occupied);
            AssertBag(bagSlot, ItemID.Invalid);
            Assert.AreEqual((int)SWORD_MIN_STR + SWORD_STR_BONUS, Effective(StatID.Strength));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_BaseStrOneBelowRequirement_ReturnsStatRequirementNotMet_AC_EQS_5()
        {
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, (int)SWORD_MIN_STR - 1);
            int bagSlot = PutInBag(DarkSteelSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            AssertBag(bagSlot, DarkSteelSwordId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(1.5f, EquipResult.Success)]
        [TestCase(1.49f, EquipResult.StatRequirementNotMet)]
        public void test_Equip_FloatSchemaRequirement_ReadsGetBaseStatFloat(float baseValue, EquipResult expected)
        {
            AddItem(BootsId, GearSlot.Boots, StatID.MovementSpeed, BOOTS_MIN_MOVE_SPEED, Mod(StatID.Defense, 2f));
            _stats.SetBaseStatFloat(PlayerEntity, StatID.MovementSpeed, baseValue);
            int bagSlot = PutInBag(BootsId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Boots, BootsId, bagSlot);

            Assert.AreEqual(expected, result);
            AssertSlot(GearSlot.Boots, expected == EquipResult.Success ? BootsId : ItemID.Invalid,
                expected == EquipResult.Success ? SlotState.Occupied : SlotState.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_RequirementStatOutsideStatId_LogsAndReturnsStatRequirementNotMet()
        {
            AddItem(BootsId, GearSlot.Boots, (StatID)99, 1f, Mod(StatID.Defense, 2f));
            int bagSlot = PutInBag(BootsId);
            ResetCounters();

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: .* requires stat 99, which is not a StatID"));
            EquipResult result = _equipment.Equip(Player, GearSlot.Boots, BootsId, bagSlot);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            AssertBag(bagSlot, BootsId);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_BuffLiftsEffectiveStrAboveRequirement_StillRejected_AC_EQS_6()
        {
            _stats.AddBuffModifier(PlayerEntity, StatID.Strength, new BuffModifierEntry(STR_BUFF, 0f, 10, (BuffID)601u));
            Assert.AreEqual(BASE_STR + STR_BUFF, Effective(StatID.Strength), "test setup: effective STR above the requirement");
            int bagSlot = PutInBag(DarkSteelSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            AssertBag(bagSlot, DarkSteelSwordId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
        }

        [Test]
        public void test_Equip_BaseMaxHpBelowHelmetRequirement_ReturnsStatRequirementNotMet_AC_EQS_15()
        {
            int bagSlot = PutInBag(IronHelmetId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Helmet, IronHelmetId, bagSlot);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            AssertBag(bagSlot, IronHelmetId);
            AssertSlot(GearSlot.Helmet, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
        }

        [Test]
        public void test_Equip_BaseMaxHpAtHelmetRequirement_Succeeds_AC_EQS_15()
        {
            _stats.SetBaseStat(PlayerEntity, StatID.MaxHP, (int)HELMET_MIN_MAX_HP);
            int bagSlot = PutInBag(IronHelmetId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Helmet, IronHelmetId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertSlot(GearSlot.Helmet, IronHelmetId, SlotState.Occupied);
            AssertBag(bagSlot, ItemID.Invalid);
        }

        [Test]
        public void test_Equip_AccessoryWithNoRequirement_IsNeverGated()
        {
            foreach (StatID stat in new[] { StatID.Strength, StatID.MaxHP, StatID.Dexterity, StatID.Intelligence, StatID.Vitality })
            {
                _stats.SetBaseStat(PlayerEntity, stat, 0);
            }

            int bagSlot = PutInBag(RingId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Ring, RingId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertSlot(GearSlot.Ring, RingId, SlotState.Occupied);
        }

        // ── Same-item guard (CR-EQS-4, EC-EQS-8) ───────────────────────────────────────────

        [Test]
        public void test_Equip_SameItemAndLevelAlreadyEquipped_ReturnsNoChange_AC_EQS_8()
        {
            EquipForSetup(GearSlot.Weapon, IronSwordId);
            int effectiveStr = Effective(StatID.Strength);
            int bagSlot = PutInBag(IronSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.NoChange, result);
            AssertSlot(GearSlot.Weapon, IronSwordId, SlotState.Occupied);
            AssertBag(bagSlot, IronSwordId);
            Assert.AreEqual(effectiveStr, Effective(StatID.Strength));
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_SameItemDifferentLevel_PassesTheSameItemGuard()
        {
            // A different level is a different item (CR-EQS-4): it passes the guard (not NoChange)
            // and auto-swaps (AC-EQS-28).
            EquipForSetup(GearSlot.Weapon, IronSwordId);
            int bagSlot = PutInBag(IronSwordId, 3);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(3, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Weapon));
            Assert.AreEqual(1, _inventory.MoveItemInCalls, "the level-0 sword returned to the bag");
            Assert.AreEqual(1, _inventory.MoveItemOutCalls, "the level-3 sword left the bag");
        }

        [Test]
        public void test_Equip_LeveledItemIntoEmptySlot_KeepsItsLevel()
        {
            int bagSlot = PutInBag(IronSwordId, 3);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(3, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Weapon));
        }

        // ── Slot type (CR-EQS-2) ───────────────────────────────────────────────────────────

        [Test]
        public void test_Equip_HelmetIntoWeaponSlot_ReturnsSlotMismatch_AC_EQS_9()
        {
            _stats.SetBaseStat(PlayerEntity, StatID.MaxHP, (int)HELMET_MIN_MAX_HP);
            int bagSlot = PutInBag(IronHelmetId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronHelmetId, bagSlot);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
            AssertBag(bagSlot, IronHelmetId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertSlot(GearSlot.Helmet, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_ConsumableItem_ReturnsSlotMismatchWithoutLog()
        {
            ItemDefinition potion = ItemDefinitionBuilder.Build(PotionId.RawValue, "Potion", ItemCategory.Consumable, stackLimit: 5);
            _created.Add(potion);
            _db.Add(potion);
            _realInventory.Pickup(Player, PotionId, 1);
            int bagSlot = FindInBag(PotionId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, PotionId, bagSlot);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
            AssertBag(bagSlot, PotionId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_BagItemMissingFromItemDatabase_LogsAndReturnsSlotMismatch()
        {
            int bagSlot = PutInBag(IronSwordId);
            _db.Remove(IronSwordId);
            ResetCounters();

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: .* is in the bag but not in the Item Database"));
            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
            AssertBag(bagSlot, IronSwordId);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        // ── Input validation (CR-EQS-13) ───────────────────────────────────────────────────

        [Test]
        public void test_Equip_InvalidItemIdReleaseMode_ReturnsItemNotInInventory_AC_EQS_19()
        {
            int bagSlot = PutInBag(IronSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemID.Invalid, bagSlot);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            AssertBag(bagSlot, IronSwordId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_InvalidItemIdDevMode_ThrowsInvalidOperation_AC_EQS_19()
        {
            _equipment = BuildDevMode();
            int bagSlot = PutInBag(IronSwordId);
            ResetCounters();

            Assert.Throws<InvalidOperationException>(() => _equipment.Equip(Player, GearSlot.Weapon, ItemID.Invalid, bagSlot));

            AssertBag(bagSlot, IronSwordId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_DevModeValidItem_Succeeds()
        {
            _equipment = BuildDevMode();
            int bagSlot = PutInBag(IronSwordId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertSlot(GearSlot.Weapon, IronSwordId, SlotState.Occupied);
        }

        [Test]
        public void test_EquipAndUnequip_GearSlotOutOfRange_ReturnSlotMismatchWithoutCalls_AC_EQS_20()
        {
            int bagSlot = PutInBag(IronSwordId);
            ResetCounters();

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: gear slot 7 is out of range"));
            EquipResult equipResult = _equipment.Equip(Player, (GearSlot)7, IronSwordId, bagSlot);
            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Unequip: gear slot 7 is out of range"));
            EquipResult unequipResult = _equipment.Unequip(Player, (GearSlot)7);

            Assert.AreEqual(EquipResult.SlotMismatch, equipResult);
            Assert.AreEqual(EquipResult.SlotMismatch, unequipResult);
            AssertBag(bagSlot, IronSwordId);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_GearSlot255_ReturnsSlotMismatchWithoutException_AC_EQS_20()
        {
            int bagSlot = PutInBag(IronSwordId);
            ResetCounters();

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: gear slot 255 is out of range"));
            EquipResult result = _equipment.Equip(Player, (GearSlot)255, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
            AssertBag(bagSlot, IronSwordId);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_EquipAndUnequip_UnregisteredCharacterAndBadSlot_ReturnSlotMismatchLoggingOnlyTheRange()
        {
            var unknown = new CharacterID(9999u);

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: gear slot 7 is out of range"));
            EquipResult equipResult = _equipment.Equip(unknown, (GearSlot)7, IronSwordId, 0);
            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Unequip: gear slot 7 is out of range"));
            EquipResult unequipResult = _equipment.Unequip(unknown, (GearSlot)7);

            Assert.AreEqual(EquipResult.SlotMismatch, equipResult);
            Assert.AreEqual(EquipResult.SlotMismatch, unequipResult);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Stale-render guard (CR-EQS-13, AC-EQS-33) ──────────────────────────────────────

        [Test]
        public void test_Equip_EmptyBagSlot_ReturnsItemNotInInventory_AC_EQS_33a()
        {
            int heldSlot = PutInBag(IronSwordId);
            int emptySlot = heldSlot + 1;
            Assert.IsTrue(_realInventory.GetSlot(Player, emptySlot).IsEmpty, "test setup: empty slot");
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, emptySlot);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertBag(heldSlot, IronSwordId);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_BagSlotHoldsDifferentItem_ReturnsItemNotInInventory_AC_EQS_33b()
        {
            // Before this guard, Equip took the ring out of the slot but registered the sword (Story 002 review gap).
            int bagSlot = PutInBag(RingId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, bagSlot);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            AssertBag(bagSlot, RingId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            Assert.AreEqual(BASE_STR, Effective(StatID.Strength));
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(-1)]
        [TestCase(20)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void test_Equip_BagIndexOutOfRange_ReturnsItemNotInInventoryWithoutLog_AC_EQS_33c(int index)
        {
            int bagSlot = PutInBag(IronSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, index);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            AssertBag(bagSlot, IronSwordId);
            AssertSlot(GearSlot.Weapon, ItemID.Invalid, SlotState.Empty);
            AssertNoMutation();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_LastBagIndex_IsAccepted()
        {
            int placed = PutInBag(IronSwordId);
            _realInventory.Move(Player, placed, LAST_BAG_INDEX);
            AssertBag(LAST_BAG_INDEX, IronSwordId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSwordId, LAST_BAG_INDEX);

            Assert.AreEqual(EquipResult.Success, result);
            AssertBag(LAST_BAG_INDEX, ItemID.Invalid);
        }

        // ── Check order ────────────────────────────────────────────────────────────────────

        [Test]
        public void test_Equip_OccupiedSlotAndStatGateFails_ReturnsStatRequirementNotMetBeforeSwap()
        {
            EquipForSetup(GearSlot.Weapon, IronSwordId);
            int bagSlot = PutInBag(DarkSteelSwordId);
            ResetCounters();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            AssertSlot(GearSlot.Weapon, IronSwordId, SlotState.Occupied);
            AssertBag(bagSlot, DarkSteelSwordId);
            AssertNoMutation();
        }

        [Test]
        public void test_Equip_InvalidItemAndStaleIndexReleaseMode_ReturnsItemNotInInventory()
        {
            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemID.Invalid, 20);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_InvalidItemAndStaleIndexDevMode_ThrowsBeforeTheStaleGuard()
        {
            _equipment = BuildDevMode();

            Assert.Throws<InvalidOperationException>(() => _equipment.Equip(Player, GearSlot.Weapon, ItemID.Invalid, 20));
        }

        [Test]
        public void test_Equip_WrongSlotTypeAndStatGateFail_ReturnsSlotMismatchFirst()
        {
            int bagSlot = PutInBag(DarkSteelSwordId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Helmet, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
        }

        [Test]
        public void test_Equip_StaleSlotAndWrongSlotType_ReturnsItemNotInInventoryFirst()
        {
            int bagSlot = PutInBag(RingId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronHelmetId, bagSlot);

            Assert.AreEqual(EquipResult.ItemNotInInventory, result);
        }

        [Test]
        public void test_Equip_SameItemEquippedAndStatGateFails_ReturnsNoChangeFirst()
        {
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, (int)SWORD_MIN_STR);
            EquipForSetup(GearSlot.Weapon, DarkSteelSwordId);
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            int bagSlot = PutInBag(DarkSteelSwordId);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            Assert.AreEqual(EquipResult.NoChange, result);
        }

        [Test]
        public void test_Equip_OutOfRangeSlotAndInvalidItemDevMode_ReturnsSlotMismatchBeforeThrowing()
        {
            _equipment = BuildDevMode();

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] Equip: gear slot 7 is out of range"));
            EquipResult result = _equipment.Equip(Player, (GearSlot)7, ItemID.Invalid, 0);

            Assert.AreEqual(EquipResult.SlotMismatch, result);
        }
    }
}
