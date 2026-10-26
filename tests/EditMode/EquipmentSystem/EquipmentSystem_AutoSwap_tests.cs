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
    /// EditMode unit tests for Equipment System Story 004 — auto-swap, rollback and the Transitioning
    /// guard (CR-EQS-6, CR-EQS-8, CR-EQS-9, CR-EQS-15; AC-EQS-2/10/16/28/31; TR-equip-005, TR-equip-006).
    /// Real <c>CharacterStats</c> and <c>InventoryService</c>; <see cref="InventoryDecorator"/> injects
    /// <c>MoveItemIn</c> / <c>MoveItemOut</c> failures, counts <c>ForceInsert</c> and exposes mid-swap hooks.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_AutoSwap_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const int BAG_SIZE = 20;
        private const int BASE_STR = 60;
        private const int BASE_DEF = 30;
        private const float ITEM_A_STR = 25f;
        private const float ITEM_B_STR = 35f;
        private const float ITEM_B_MIN_STR = 55f;
        private const float ITEM_C_DEF = 10f;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);

        private static readonly ItemID ItemA = new ItemID(101u);
        private static readonly ItemID ItemB = new ItemID(102u);
        // Two modifiers on two stats, so restore and removal are checked per stat.
        private static readonly ItemID ItemC = new ItemID(103u);
        private static readonly ItemID FillerId = new ItemID(900u);

        private FakeItemDatabase _db;
        private InventoryService _realInventory;
        private InventoryDecorator _inventory;
        private Stats _stats;
        private EquipmentService _equipment;
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private readonly List<(StatID Stat, bool Transitioning, EquipmentSlotState Read)> _events = new List<(StatID, bool, EquipmentSlotState)>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _events.Clear();
            _db = new FakeItemDatabase();
            _realInventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory = new InventoryDecorator(_realInventory);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            _stats.SetBaseStat(PlayerEntity, StatID.Defense, BASE_DEF);
            _equipment = new EquipmentService(
                _inventory, _stats, _db, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default);
            _equipment.RegisterCharacter(Player, PlayerEntity);

            AddItem(ItemA, GearTier.Iron, null, 0f, Mod(StatID.Strength, ITEM_A_STR));
            AddItem(ItemB, GearTier.Iron, StatID.Strength, ITEM_B_MIN_STR, Mod(StatID.Strength, ITEM_B_STR));
            AddItem(ItemC, GearTier.Iron, null, 0f, Mod(StatID.Strength, ITEM_A_STR), Mod(StatID.Defense, ITEM_C_DEF));
            AddItem(FillerId, GearTier.Bronze, null, 0f);
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

        private static StatModifierEntry Mod(StatID stat, float flat) => StatModifierEntry.CreateForTesting(stat, flat);

        private void AddItem(ItemID id, GearTier tier, StatID? requirementStat, float requirementMin, params StatModifierEntry[] modifiers)
        {
            EquipmentData data = EquipmentData.CreateForTesting(
                GearSlot.Weapon, tier, modifiers, equipRequirementStat: requirementStat, equipRequirementMin: requirementMin);
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

        private void FillBagTo(int filled)
        {
            while (_realInventory.FilledSlots(Player) < filled)
            {
                PutInBag(FillerId);
            }
        }

        // Puts the item in the bag and equips it into the Weapon slot. The setup equip goes through
        // the decorator, so its call counters are zeroed for the test's own swap.
        private void EquipForSetup(ItemID id, byte level = 0)
        {
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, id, PutInBag(id, level)), "test setup: equip");
            _inventory.MoveItemInCalls = 0;
            _inventory.MoveItemOutCalls = 0;
        }

        private int FindInBag(ItemID id)
        {
            for (int i = 0; i < BAG_SIZE; i++)
            {
                if (_realInventory.GetSlot(Player, i).ItemId == id)
                {
                    return i;
                }
            }

            Assert.Fail($"{id} is not in the bag");
            return -1;
        }

        private int Strength() => _stats.GetEffectiveStat(PlayerEntity, StatID.Strength);

        private int Defense() => _stats.GetEffectiveStat(PlayerEntity, StatID.Defense);

        // Records every OnStatChanged with the Weapon slot's flag and state as the HUD would read them.
        private void SpyOnStatEvents()
        {
            Stats.StatChangedHandler handler = (entity, stat) => _events.Add((
                stat,
                _equipment.IsSlotTransitioning(Player, GearSlot.Weapon),
                _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon)));
            _stats.Subscribe(handler);
        }

        private void AssertWeapon(ItemID item, SlotState state, int level)
        {
            EquipmentSlotState read = _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon);
            Assert.AreEqual(item, read.ItemId, "Weapon: item");
            Assert.AreEqual(state, read.State, "Weapon: state");
            Assert.AreEqual(level, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Weapon), "Weapon: level");
            Assert.AreEqual(state == SlotState.Transitioning, _equipment.IsSlotTransitioning(Player, GearSlot.Weapon), "Weapon: transitioning");
        }

        // Matches a log line containing every given text.
        private static Regex LogWith(params string[] texts)
        {
            string pattern = "^";
            foreach (string text in texts)
            {
                pattern += $"(?=.*{Regex.Escape(text)})";
            }

            return new Regex(pattern);
        }

        // ── Transitioning read surface (CR-EQS-9) ──────────────────────────────────────────

        [Test]
        public void test_Swap_MidSwap_ReadSurfaceReportsOldItemTransitioning()
        {
            EquipForSetup(ItemA);
            int bagSlot = PutInBag(ItemB);
            var seen = new List<(ItemID Item, SlotState State, bool Transitioning)>();
            void Observe()
            {
                EquipmentSlotState read = _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon);
                seen.Add((read.ItemId, read.State, _equipment.IsSlotTransitioning(Player, GearSlot.Weapon)));
            }

            _inventory.BeforeMoveItemIn = Observe;
            _inventory.BeforeMoveItemOut = Observe;

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(2, seen.Count, "MoveItemIn and MoveItemOut were observed");
            foreach (var sample in seen)
            {
                Assert.AreEqual(ItemA, sample.Item);
                Assert.AreEqual(SlotState.Transitioning, sample.State);
                Assert.IsTrue(sample.Transitioning, "both reads agree");
            }

            AssertWeapon(ItemB, SlotState.Occupied, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Success paths ──────────────────────────────────────────────────────────────────

        [Test]
        public void test_Swap_SteelOverIron_NetStatDeltaAndItemsMove_AC_EQS_2()
        {
            EquipForSetup(ItemA);
            int bagSlot = PutInBag(ItemB);
            Assert.AreEqual(BASE_STR + 25, Strength());

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(BASE_STR + 35, Strength(), "net +10");
            AssertWeapon(ItemB, SlotState.Occupied, 0);
            Assert.AreEqual(0, _realInventory.GetSlot(Player, FindInBag(ItemA)).EnhancementLevel, "ItemA is in the bag at level 0");
            Assert.IsFalse(_realInventory.HasItem(Player, ItemB), "ItemB left the bag");
            Assert.AreEqual(0, _inventory.ForceInsertCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_StatEventsFireWhileTransitioning_FlagFalseAfterReturn_AC_EQS_10()
        {
            EquipForSetup(ItemA);
            int bagSlot = PutInBag(ItemB);
            SpyOnStatEvents();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(2, _events.Count);
            Assert.IsTrue(_events[0].Transitioning, "the remove-phase callback sees the flag set");
            Assert.IsTrue(_events[1].Transitioning, "the add-phase callback sees the flag set");
            foreach (var sample in _events)
            {
                Assert.AreEqual(ItemA, sample.Read.ItemId, "the slot still reports the old item");
                Assert.AreEqual(SlotState.Transitioning, sample.Read.State, "GetEquipmentSlotState agrees with the flag");
            }

            Assert.IsFalse(_equipment.IsSlotTransitioning(Player, GearSlot.Weapon));
            AssertWeapon(ItemB, SlotState.Occupied, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_OneModifierItems_FiresExactlyTwoStatEvents_NoTransactionCollapse()
        {
            EquipForSetup(ItemA);
            int bagSlot = PutInBag(ItemB);
            SpyOnStatEvents();

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot));

            Assert.AreEqual(2, _events.FindAll(e => e.Stat == StatID.Strength).Count, "remove, then add");
            Assert.AreEqual(2, _events.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_SameItemDifferentLevel_SucceedsAndOnlyNewLevelCounts_AC_EQS_28()
        {
            EquipForSetup(ItemA, 0);
            int bagSlot = PutInBag(ItemA, 5);
            int expected = BASE_STR + new EnhancementBonusProvider(EnhancementConfig.Default).GetFlatBonus(5, 25, GearTier.Iron);

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemA, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertWeapon(ItemA, SlotState.Occupied, 5);
            Assert.AreEqual(0, _realInventory.GetSlot(Player, FindInBag(ItemA)).EnhancementLevel, "the +0 sword is in the bag");
            Assert.AreEqual(expected, Strength(), "no residue from the +0 sword");
            Assert.AreEqual(1, _realInventory.FilledSlots(Player), "exactly one sword in the bag");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_TwoStatItemForOneStatItem_RemovesEveryOldModifier()
        {
            EquipForSetup(ItemC);
            int bagSlot = PutInBag(ItemB);
            Assert.AreEqual(BASE_DEF + (int)ITEM_C_DEF, Defense(), "test setup: ItemC's Defense applied");

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertWeapon(ItemB, SlotState.Occupied, 0);
            Assert.AreEqual(BASE_STR + (int)ITEM_B_STR, Strength());
            Assert.AreEqual(BASE_DEF, Defense(), "no leftover Defense modifier from ItemC");
            LogAssert.NoUnexpectedReceived();
        }

        // ── Full bag (step 3) ──────────────────────────────────────────────────────────────

        [Test]
        public void test_Swap_FullBag_ReturnsInventoryFullBeforeAnyMutation()
        {
            EquipForSetup(ItemA, 3);
            int bagSlot = PutInBag(ItemB);
            FillBagTo(BAG_SIZE);
            int before = Strength();
            SpyOnStatEvents();

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.InventoryFull, result);
            Assert.AreEqual(0, _events.Count, "no modifier removed, flag never set while an event could observe it");
            Assert.AreEqual(0, _inventory.MoveItemInCalls);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            Assert.AreEqual(before, Strength());
            AssertWeapon(ItemA, SlotState.Occupied, 3);
            Assert.AreEqual(ItemB, _realInventory.GetSlot(Player, bagSlot).ItemId);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Rollback paths (CR-EQS-8) ──────────────────────────────────────────────────────

        [Test]
        public void test_Swap_MoveItemInFails_AbortsInPlaceAndNeverForceInserts_AC_EQS_16()
        {
            EquipForSetup(ItemC, 3);
            int bagSlot = PutInBag(ItemB);
            int strengthBefore = Strength();
            int defenseBefore = Defense();
            _inventory.FailMoveItemIn = true;
            LogAssert.Expect(LogType.Error, LogWith("Auto-swap aborted", ItemC.ToString(), PlayerEntity.ToString()));

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.InventoryError, result);
            AssertWeapon(ItemC, SlotState.Occupied, 3);
            Assert.AreEqual(strengthBefore, Strength(), "Strength modifier re-added exactly once");
            Assert.AreEqual(defenseBefore, Defense(), "Defense modifier re-added exactly once");
            Assert.AreEqual(ItemB, _realInventory.GetSlot(Player, bagSlot).ItemId, "ItemB still in S");
            Assert.AreEqual(0, _inventory.ForceInsertCalls);
            Assert.AreEqual(1, _inventory.MoveItemInCalls);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls, "step 7 is never reached");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_RetryAfterAbortedSwap_SucceedsWithCorrectStats()
        {
            EquipForSetup(ItemC, 3);
            int bagSlot = PutInBag(ItemB);
            _inventory.FailMoveItemIn = true;
            LogAssert.Expect(LogType.Error, LogWith("Auto-swap aborted", ItemC.ToString()));
            Assert.AreEqual(EquipResult.InventoryError, _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot), "test setup: first swap aborts");
            _inventory.FailMoveItemIn = false;

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.Success, result);
            AssertWeapon(ItemB, SlotState.Occupied, 0);
            Assert.AreEqual(BASE_STR + (int)ITEM_B_STR, Strength(), "no doubled modifier left by the rollback");
            Assert.AreEqual(BASE_DEF, Defense());
            Assert.AreEqual(3, _realInventory.GetSlot(Player, FindInBag(ItemC)).EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_OldDefinitionMissing_AbortsAndLeavesSlotUntouched()
        {
            EquipForSetup(ItemA);
            int bagSlot = PutInBag(ItemB);
            int before = Strength();
            _db.Remove(ItemA);
            SpyOnStatEvents();
            LogAssert.Expect(LogType.Error, LogWith("Equip swap GetItem null", ItemA.ToString(), PlayerEntity.ToString()));

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(EquipResult.InventoryError, result);
            AssertWeapon(ItemA, SlotState.Occupied, 0);
            Assert.AreEqual(before, Strength());
            Assert.AreEqual(0, _events.Count, "refused before any modifier is removed");
            Assert.AreEqual(ItemB, _realInventory.GetSlot(Player, bagSlot).ItemId, "ItemB still in S");
            Assert.AreEqual(0, _inventory.MoveItemInCalls);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            Assert.AreEqual(0, _inventory.ForceInsertCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(MoveItemOutCode.SlotLocked, EquipResult.ItemLocked)]
        [TestCase(MoveItemOutCode.SlotEmpty, EquipResult.ItemNotInInventory)]
        public void test_Swap_MoveItemOutFails_ReclaimsOldItemAndRestoresState_AC_EQS_31(MoveItemOutCode failure, EquipResult expected)
        {
            EquipForSetup(ItemA, 3);
            int bagSlot = PutInBag(ItemB);
            FillBagTo(BAG_SIZE - 1);
            int freeSlot = BAG_SIZE - 1;
            Assert.IsTrue(_realInventory.GetSlot(Player, freeSlot).IsEmpty, "test setup: exactly one free slot F");
            int before = Strength();
            _inventory.FailMoveItemOutSlot = bagSlot;
            _inventory.FailMoveItemOutCode = failure;

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            Assert.AreEqual(expected, result);
            AssertWeapon(ItemA, SlotState.Occupied, 3);
            Assert.AreEqual(before, Strength(), "stats equal the pre-call value");
            Assert.IsTrue(_realInventory.GetSlot(Player, freeSlot).IsEmpty, "F is empty again, ItemA not duplicated");
            Assert.IsFalse(_realInventory.HasItem(Player, ItemA));
            Assert.AreEqual(ItemB, _realInventory.GetSlot(Player, bagSlot).ItemId, "ItemB still in S");
            Assert.AreEqual(1, _inventory.MoveItemInCalls, "step 6 placed ItemA in F");
            Assert.AreEqual(2, _inventory.MoveItemOutCalls, "the new item, then the reclaim");
            Assert.AreEqual(0, _inventory.ForceInsertCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_ReclaimAlsoFails_LeavesSlotEmptyAndOldItemInBag()
        {
            EquipForSetup(ItemC, 3);
            int bagSlot = PutInBag(ItemB);
            _inventory.MoveItemOutOverride = MoveItemOutCode.SlotLocked; // fails S and the reclaim
            LogAssert.Expect(LogType.Error, LogWith("CRITICAL", ItemC.ToString(), ItemB.ToString(), "SlotLocked", PlayerEntity.ToString()));

            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, ItemB, bagSlot);

            // Option A (Story 004 code review): consistent state, no item lost or duplicated.
            Assert.AreEqual(EquipResult.InventoryError, result);
            AssertWeapon(ItemID.Invalid, SlotState.Empty, 0);
            Assert.AreEqual(BASE_STR, Strength(), "ItemC's Strength modifier stays removed");
            Assert.AreEqual(BASE_DEF, Defense(), "ItemC's Defense modifier stays removed");
            Assert.AreEqual(3, _realInventory.GetSlot(Player, FindInBag(ItemC)).EnhancementLevel, "ItemC is in the bag at its level");
            Assert.AreEqual(ItemB, _realInventory.GetSlot(Player, bagSlot).ItemId, "ItemB still in S");
            Assert.AreEqual(2, _realInventory.FilledSlots(Player), "exactly ItemB and ItemC in the bag");
            Assert.AreEqual(1, _inventory.MoveItemInCalls);
            Assert.AreEqual(2, _inventory.MoveItemOutCalls, "the new item, then the reclaim");
            Assert.AreEqual(0, _inventory.ForceInsertCalls);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
