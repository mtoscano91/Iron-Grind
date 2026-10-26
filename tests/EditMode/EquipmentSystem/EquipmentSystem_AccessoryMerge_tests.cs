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
    /// EditMode unit tests for Equipment System Story 006 — accessory merge (CR-EQS-14, TR-equip-009,
    /// AC-EQS-21/22/23/24/26/27/34). Real <c>CharacterStats</c> and <c>InventoryService</c>;
    /// <see cref="InventoryDecorator"/> counts <c>MoveItemOut</c> and fails chosen <c>ForceInsert</c> calls.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_AccessoryMerge_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);

        private static readonly ItemID SwordId = new ItemID(102u);
        private static readonly ItemID RingPlus1Id = new ItemID(301u);
        private static readonly ItemID RingPlus2Id = new ItemID(302u);
        private static readonly ItemID RingPlus3Id = new ItemID(303u);
        private static readonly ItemID RingPlus10Id = new ItemID(310u);
        private static readonly ItemID NecklacePlus1Id = new ItemID(351u);
        private static readonly ItemID NecklacePlus2Id = new ItemID(352u);

        private sealed class StubEquippedCheck : IMergeEquippedCheck
        {
            private readonly bool _equipped;

            public StubEquippedCheck(bool equipped)
            {
                _equipped = equipped;
            }

            public bool IsSourceEquipped(CharacterID charId, int bagSlotIndex, ItemID itemId) => _equipped;
        }

        private FakeItemDatabase _db;
        private InventoryService _realInventory;
        private InventoryDecorator _inventory;
        private Stats _stats;
        private EquipmentService _equipment;
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _db = new FakeItemDatabase();
            _realInventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory = new InventoryDecorator(_realInventory);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _equipment = new EquipmentService(
                _inventory, _stats, _db, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default, false);
            _equipment.RegisterCharacter(Player, PlayerEntity);

            AddItem(SwordId, GearSlot.Weapon, null);
            AddItem(RingPlus1Id, GearSlot.Ring, RingPlus2Id);
            AddItem(RingPlus2Id, GearSlot.Ring, RingPlus3Id);
            AddItem(RingPlus3Id, GearSlot.Ring, null);
            AddItem(RingPlus10Id, GearSlot.Ring, null);
            AddItem(NecklacePlus1Id, GearSlot.Necklace, NecklacePlus2Id);
            AddItem(NecklacePlus2Id, GearSlot.Necklace, null);
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

        private void AddItem(ItemID id, GearSlot slot, ItemID? mergeResult)
        {
            EquipmentData data = EquipmentData.CreateForTesting(
                slot, GearTier.Iron, new StatModifierEntry[0], mergeResultItemID: mergeResult);
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, $"Item{id.RawValue}", ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
        }

        private int PutInBag(ItemID id)
        {
            MoveItemInResult placed = _realInventory.MoveItemIn(Player, id, 0);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            return placed.SlotIndex;
        }

        // Places the item in the (empty) target slot.
        private void PlaceAt(ItemID id, int target)
        {
            int placed = PutInBag(id);
            if (placed != target)
            {
                Assert.IsTrue(_realInventory.Move(Player, placed, target).Success, "test setup: move");
            }
        }

        private void PlaceAt(ItemID id, params int[] targets)
        {
            foreach (int target in targets)
            {
                PlaceAt(id, target);
            }
        }

        private void FillRestOfBagWithSwords()
        {
            while (_realInventory.HasFreeSlot(Player))
            {
                PutInBag(SwordId);
            }
        }

        private ItemID[] Snapshot()
        {
            var result = new ItemID[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = _realInventory.GetSlot(Player, i).ItemId;
            }

            return result;
        }

        private int CountInBag(ItemID id)
        {
            int count = 0;
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_realInventory.GetSlot(Player, i).ItemId == id)
                {
                    count++;
                }
            }

            return count;
        }

        private void EquipForSetup(GearSlot slot, ItemID id)
        {
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, slot, id, PutInBag(id)), "test setup: equip");
        }

        private void ResetCounters()
        {
            _inventory.MoveItemOutCalls = 0;
            _inventory.MoveItemInCalls = 0;
            _inventory.ForceInsertCalls = 0;
        }

        private void AssertNoGearTransition()
        {
            Assert.IsFalse(_equipment.IsSlotTransitioning(Player, GearSlot.Ring), "Ring transitioning");
            Assert.IsFalse(_equipment.IsSlotTransitioning(Player, GearSlot.Necklace), "Necklace transitioning");
        }

        // ── AC-EQS-21: success ─────────────────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_ThreeRingsWithFreeSlot_Succeeds_AC_EQS_21()
        {
            // Fillers in 0 and 1: the lowest free slot after the removal is source slot 2.
            PlaceAt(SwordId, 0, 1);
            PlaceAt(RingPlus1Id, 2, 5, 9);
            Assert.AreEqual(5, _realInventory.FilledSlots(Player), "test setup");
            ResetCounters();

            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.Success, result);
            Assert.AreEqual(RingPlus2Id, _realInventory.GetSlot(Player, 2).ItemId, "result in the lowest free slot");
            Assert.AreEqual(0, _realInventory.GetSlot(Player, 2).EnhancementLevel);
            Assert.AreEqual(ItemID.Invalid, _realInventory.GetSlot(Player, 5).ItemId);
            Assert.AreEqual(ItemID.Invalid, _realInventory.GetSlot(Player, 9).ItemId);
            Assert.AreEqual(SwordId, _realInventory.GetSlot(Player, 0).ItemId, "filler untouched");
            Assert.AreEqual(SwordId, _realInventory.GetSlot(Player, 1).ItemId, "filler untouched");
            Assert.AreEqual(1, CountInBag(RingPlus2Id));
            Assert.AreEqual(0, CountInBag(RingPlus1Id));
            Assert.AreEqual(3, _realInventory.FilledSlots(Player), "5 -> 3 (−2)");
            AssertNoGearTransition();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RequestMerge_ThreeNecklaces_Succeeds_AC_EQS_21()
        {
            PlaceAt(NecklacePlus1Id, 3, 4, 8);

            MergeResult result = _equipment.RequestMerge(Player, 3, 4, 8);

            Assert.AreEqual(MergeResult.Success, result);
            Assert.AreEqual(1, CountInBag(NecklacePlus2Id));
            Assert.AreEqual(0, CountInBag(NecklacePlus1Id));
            Assert.AreEqual(1, _realInventory.FilledSlots(Player));
            AssertNoGearTransition();
            LogAssert.NoUnexpectedReceived();
        }

        // ── AC-EQS-22: max level ───────────────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_ThreePlus10Rings_ReturnsRejectedMaxLevel_AC_EQS_22()
        {
            PlaceAt(RingPlus10Id, 2, 5, 9);
            ItemID[] before = Snapshot();
            ResetCounters();

            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.RejectedMaxLevel, result);
            CollectionAssert.AreEqual(before, Snapshot());
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            Assert.AreEqual(0, _inventory.ForceInsertCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ── AC-EQS-23: full bag ────────────────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_FullBag_ReturnsRejectedInventoryFull_AC_EQS_23()
        {
            PlaceAt(RingPlus1Id, 0, 1, 2);
            FillRestOfBagWithSwords();
            Assert.IsTrue(_realInventory.IsFull(Player), "test setup: full bag");
            ItemID[] before = Snapshot();
            ResetCounters();

            MergeResult result = _equipment.RequestMerge(Player, 0, 1, 2);

            Assert.AreEqual(MergeResult.RejectedInventoryFull, result);
            CollectionAssert.AreEqual(before, Snapshot());
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ── AC-EQS-24: result insert fails, rollback succeeds ──────────────────────────────

        [Test]
        public void test_RequestMerge_ResultInsertFails_RestoresSourcesAndReturnsMergeError_AC_EQS_24()
        {
            EquipForSetup(GearSlot.Ring, RingPlus2Id);
            EquipForSetup(GearSlot.Necklace, NecklacePlus1Id);
            PlaceAt(RingPlus1Id, 2, 5, 9);
            ResetCounters();
            _inventory.FailForceInsertCallNumbers.Add(1);

            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.RejectedMergeError, result);
            Assert.AreEqual(3, CountInBag(RingPlus1Id), "three sources back");
            Assert.AreEqual(0, CountInBag(RingPlus2Id), "no result item");
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_realInventory.GetSlot(Player, i).ItemId == RingPlus1Id)
                {
                    Assert.AreEqual(0, _realInventory.GetSlot(Player, i).EnhancementLevel);
                }
            }

            Assert.AreEqual(4, _inventory.ForceInsertCalls);
            Assert.AreEqual(RingPlus2Id, _equipment.GetEquipmentSlotState(Player, GearSlot.Ring).ItemId);
            Assert.AreEqual(0, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Ring));
            Assert.AreEqual(NecklacePlus1Id, _equipment.GetEquipmentSlotState(Player, GearSlot.Necklace).ItemId);
            Assert.AreEqual(0, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Necklace));
            AssertNoGearTransition();
            LogAssert.NoUnexpectedReceived();
        }

        // ── AC-EQS-26: rollback also fails ─────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_ResultAndOneRollbackInsertFail_ReturnsCriticalRollbackFailed_AC_EQS_26()
        {
            PlaceAt(RingPlus1Id, 2, 5, 9);
            ResetCounters();
            _inventory.FailForceInsertCallNumbers.Add(1);
            _inventory.FailForceInsertCallNumbers.Add(3);

            LogAssert.Expect(LogType.Error, new Regex(
                @"\[EquipmentService\] CRITICAL: " + Regex.Escape($"Merge rollback incomplete — 1 source item(s) not restored for entity {PlayerEntity}")));
            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.CriticalRollbackFailed, result);
            Assert.AreEqual(4, _inventory.ForceInsertCalls, "all three rollback inserts attempted");
            Assert.AreEqual(2, CountInBag(RingPlus1Id), "two sources restored");
            Assert.AreEqual(0, CountInBag(RingPlus2Id));
            AssertNoGearTransition();
            LogAssert.NoUnexpectedReceived();
        }

        // ── AC-EQS-27: equipped check seam ─────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_EquippedCheckReportsEquipped_ReturnsRejectedEquipped_AC_EQS_27()
        {
            PlaceAt(RingPlus1Id, 2, 5, 9);
            ItemID[] before = Snapshot();
            ResetCounters();
            _equipment.MergeEquippedCheck = new StubEquippedCheck(true);

            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.RejectedEquipped, result);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            CollectionAssert.AreEqual(before, Snapshot());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RequestMerge_EquippedCheckNull_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _equipment.MergeEquippedCheck = null);
        }

        // ── AC-EQS-34: preconditions 0-2 ───────────────────────────────────────────────────

        private void AssertInvalidSources(MergeResult result, ItemID[] before)
        {
            Assert.AreEqual(MergeResult.RejectedInvalidSources, result);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls, "MoveItemOut calls");
            Assert.AreEqual(0, _inventory.ForceInsertCalls, "ForceInsert calls");
            CollectionAssert.AreEqual(before, Snapshot());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RequestMerge_DifferingItemId_ReturnsRejectedInvalidSources_AC_EQS_34()
        {
            PlaceAt(RingPlus1Id, 2, 5);
            PlaceAt(RingPlus2Id, 9);
            ItemID[] before = Snapshot();
            ResetCounters();

            AssertInvalidSources(_equipment.RequestMerge(Player, 2, 5, 9), before);
        }

        [Test]
        public void test_RequestMerge_OneSlotLocked_ReturnsRejectedInvalidSources_AC_EQS_34()
        {
            PlaceAt(RingPlus1Id, 2, 5, 9);
            _realInventory.LockSlot(Player, 5);
            ItemID[] before = Snapshot();
            ResetCounters();

            AssertInvalidSources(_equipment.RequestMerge(Player, 2, 5, 9), before);
        }

        [Test]
        public void test_RequestMerge_ThreeSwords_ReturnsRejectedInvalidSources_AC_EQS_34()
        {
            PlaceAt(SwordId, 2, 5, 9);
            ItemID[] before = Snapshot();
            ResetCounters();

            AssertInvalidSources(_equipment.RequestMerge(Player, 2, 5, 9), before);
        }

        [Test]
        public void test_RequestMerge_EmptySlot_ReturnsRejectedInvalidSources_AC_EQS_34()
        {
            PlaceAt(RingPlus1Id, 2, 5);
            ItemID[] before = Snapshot();
            ResetCounters();

            AssertInvalidSources(_equipment.RequestMerge(Player, 2, 5, 9), before);
        }

        [TestCase(2, 2, 5)]
        [TestCase(2, 5, 5)]
        [TestCase(5, 2, 5)]
        [TestCase(2, 2, 2)]
        [TestCase(2, 5, 20)]
        [TestCase(20, 2, 5)]
        [TestCase(2, 5, -1)]
        [TestCase(-1, 2, 5)]
        [TestCase(2, int.MaxValue, 5)]
        public void test_RequestMerge_RepeatedOrOutOfRangeIndex_ReturnsRejectedInvalidSources_AC_EQS_34(int a, int b, int c)
        {
            PlaceAt(RingPlus1Id, 2, 5, 9);
            ItemID[] before = Snapshot();
            ResetCounters();

            AssertInvalidSources(_equipment.RequestMerge(Player, a, b, c), before);
        }

        // ── Precondition order ─────────────────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_LockedSlotWithPlus10Rings_ReturnsInvalidSourcesBeforeMaxLevel()
        {
            PlaceAt(RingPlus10Id, 2, 5, 9);
            _realInventory.LockSlot(Player, 9);

            Assert.AreEqual(MergeResult.RejectedInvalidSources, _equipment.RequestMerge(Player, 2, 5, 9));
        }

        [Test]
        public void test_RequestMerge_Plus10RingsInFullBag_ReturnsMaxLevelBeforeInventoryFull()
        {
            PlaceAt(RingPlus10Id, 0, 1, 2);
            FillRestOfBagWithSwords();

            Assert.AreEqual(MergeResult.RejectedMaxLevel, _equipment.RequestMerge(Player, 0, 1, 2));
        }

        [Test]
        public void test_RequestMerge_EquippedCheckAndMaxLevel_ReturnsEquippedBeforeMaxLevel()
        {
            PlaceAt(RingPlus10Id, 2, 5, 9);
            _equipment.MergeEquippedCheck = new StubEquippedCheck(true);

            Assert.AreEqual(MergeResult.RejectedEquipped, _equipment.RequestMerge(Player, 2, 5, 9));
        }

        // ── Four identical, one equipped ───────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_OneEquippedThreeInBag_MergesTheBagCopiesOnly()
        {
            EquipForSetup(GearSlot.Ring, RingPlus1Id);
            PlaceAt(RingPlus1Id, 2, 5, 9);

            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.Success, result);
            Assert.AreEqual(RingPlus1Id, _equipment.GetEquipmentSlotState(Player, GearSlot.Ring).ItemId);
            Assert.AreEqual(SlotState.Occupied, _equipment.GetEquipmentSlotState(Player, GearSlot.Ring).State);
            Assert.IsFalse(_equipment.IsSlotTransitioning(Player, GearSlot.Ring));
            Assert.AreEqual(1, CountInBag(RingPlus2Id));
            Assert.AreEqual(0, CountInBag(RingPlus1Id));
            LogAssert.NoUnexpectedReceived();
        }

        // ── MoveItemOut defence ────────────────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_ThirdMoveItemOutFails_RestoresRemovedSourcesAndReturnsMergeError()
        {
            PlaceAt(RingPlus1Id, 2, 5, 9);
            ResetCounters();
            _inventory.FailMoveItemOutSlot = 9;

            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.RejectedMergeError, result);
            Assert.AreEqual(3, _inventory.MoveItemOutCalls);
            Assert.AreEqual(2, _inventory.ForceInsertCalls, "only the two removed sources restored; no result insert");
            Assert.AreEqual(3, CountInBag(RingPlus1Id));
            Assert.AreEqual(RingPlus1Id, _realInventory.GetSlot(Player, 9).ItemId, "third source never moved");
            Assert.AreEqual(0, CountInBag(RingPlus2Id));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RequestMerge_ThirdMoveItemOutFailsAndARestoreFails_ReturnsCriticalRollbackFailed()
        {
            PlaceAt(RingPlus1Id, 2, 5, 9);
            ResetCounters();
            _inventory.FailMoveItemOutSlot = 9;
            _inventory.FailForceInsertCallNumbers.Add(1);

            LogAssert.Expect(LogType.Error, new Regex(
                @"\[EquipmentService\] CRITICAL: " + Regex.Escape($"Merge rollback incomplete — 1 source item(s) not restored for entity {PlayerEntity}")));
            MergeResult result = _equipment.RequestMerge(Player, 2, 5, 9);

            Assert.AreEqual(MergeResult.CriticalRollbackFailed, result);
            Assert.AreEqual(2, _inventory.ForceInsertCalls, "both removed sources attempted; no result insert");
            Assert.AreEqual(2, CountInBag(RingPlus1Id), "one restored, the never-moved third still in place");
            Assert.AreEqual(RingPlus1Id, _realInventory.GetSlot(Player, 9).ItemId, "third source never moved");
            Assert.AreEqual(0, CountInBag(RingPlus2Id));
            AssertNoGearTransition();
            LogAssert.NoUnexpectedReceived();
        }

        // ── Unregistered character ─────────────────────────────────────────────────────────

        [Test]
        public void test_RequestMerge_UnregisteredCharacter_LogsAndReturnsRejectedInvalidSources()
        {
            var unknown = new CharacterID(9999u);
            ResetCounters();

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService\] RequestMerge: .* is not a registered character"));
            MergeResult result = _equipment.RequestMerge(unknown, 2, 5, 9);

            Assert.AreEqual(MergeResult.RejectedInvalidSources, result);
            Assert.AreEqual(0, _inventory.MoveItemOutCalls);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
