using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.InventorySystem
{
    /// <summary>
    /// EditMode unit tests for Inventory System Story 003 — the <c>OnInventoryFull</c> bag-full
    /// notification and its 30-second (600-tick) per-character dedup window (GDD Rule 4.10/4.11).
    /// Time is a fake server-tick counter injected into the service.
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_BagFullNotification_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID HPPotionItemId = new ItemID(3001u);    // Consumable, StackLimit 99
        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u); // Equipment, StackLimit 1
        private static readonly ItemID UnregisteredItemId = new ItemID(3999u);

        // Occupies slots in seeded full-bag states; seeding bypasses the Item Database.
        private static readonly ItemID FillerItemId = new ItemID(3500u);

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private List<ItemDefinition> _definitions;
        private List<CharacterID> _fullEvents;
        private int _changedEventCount;
        private uint _tick;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: 99),
                ItemDefinitionBuilder.Build(BronzeSwordItemId.RawValue, "Bronze Sword", ItemCategory.Equipment, stackLimit: 1),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _tick = 0u;
            _inventory = new InventoryService(_itemDatabase, CurrentTick);
            _inventory.RegisterCharacter(Player);

            _fullEvents = new List<CharacterID>();
            _changedEventCount = 0;
            _inventory.OnInventoryFull += RecordFullEvent;
            _inventory.OnInventoryChanged += RecordChangedEvent;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        private uint CurrentTick() => _tick;

        private void RecordFullEvent(InventoryFullEventArgs args) => _fullEvents.Add(args.CharacterID);

        private void RecordChangedEvent(InventoryChangedEventArgs args) => _changedEventCount++;

        private void ReentrantPickupHandler(InventoryFullEventArgs args)
        {
            _inventory.Pickup(args.CharacterID, HPPotionItemId, 1);
        }

        private void FillBag(CharacterID characterId)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(characterId, i, FillerItemId, 1);
        }

        private PickupResult BlockedPickupAt(uint tick, CharacterID characterId)
        {
            _tick = tick;
            var result = _inventory.Pickup(characterId, BronzeSwordItemId, 1);
            Assert.AreEqual(PickupFailReason.InventoryFull, result.Reason, $"Pickup at tick {tick} must be blocked by a full bag.");
            return result;
        }

        private InventorySlot[] Snapshot()
        {
            var slots = new InventorySlot[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = _inventory.GetSlot(Player, i);
            return slots;
        }

        private void AssertSlotsUnchanged(InventorySlot[] before)
        {
            for (int i = 0; i < before.Length; i++)
            {
                var after = _inventory.GetSlot(Player, i);
                Assert.AreEqual(before[i].ItemId, after.ItemId, $"Slot {i} ItemId must be unchanged.");
                Assert.AreEqual(before[i].Quantity, after.Quantity, $"Slot {i} Quantity must be unchanged.");
            }
        }

        // -----------------------------------------------------------------------
        // AC-INV-1 / AC-INV-15 — a blocked drop fails, mutates nothing, notifies.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_ConsumableIntoFullBag_FailsAndFiresOnInventoryFullOnce_AC_INV_1()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(PickupFailReason.InventoryFull, result.Reason);
            AssertSlotsUnchanged(before);
            Assert.AreEqual(1, _fullEvents.Count, "OnInventoryFull must fire exactly once.");
            Assert.AreEqual(Player, _fullEvents[0], "OnInventoryFull must carry the blocked character's ID.");
            Assert.AreEqual(0, _changedEventCount, "A blocked drop must not fire OnInventoryChanged.");
        }

        [Test]
        public void Pickup_EquipmentIntoFullBag_FailsAndFiresOnInventoryFullOnce_AC_INV_15()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            Assert.AreEqual(PickupFailReason.InventoryFull, result.Reason);
            AssertSlotsUnchanged(before);
            Assert.AreEqual(1, _fullEvents.Count);
            Assert.AreEqual(0, _changedEventCount);
        }

        [Test]
        public void Pickup_FullBagWithMergeablePartialStack_Succeeds_NoOnInventoryFull_F_INV_3()
        {
            // Arrange
            FillBag(Player);
            _inventory.SeedSlotForTesting(Player, 4, HPPotionItemId, 50);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(51, _inventory.GetSlot(Player, 4).Quantity);
            Assert.AreEqual(0, _fullEvents.Count, "A full bag with merge room is not a blocked drop.");
        }

        [Test]
        public void Pickup_MultiUnitPartiallyPlannedThenNoRoom_FiresOnInventoryFull_AC_INV_1()
        {
            // Arrange — 3 potions: 1 fits the 98/99 stack, 2 have nowhere to go.
            FillBag(Player);
            _inventory.SeedSlotForTesting(Player, 4, HPPotionItemId, 98);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 3);

            // Assert
            Assert.AreEqual(PickupFailReason.InventoryFull, result.Reason);
            AssertSlotsUnchanged(before);
            Assert.AreEqual(1, _fullEvents.Count, "A pickup that fails after partial Step 1 planning is still a blocked drop.");
        }

        [Test]
        public void Pickup_BlockedWithNoOnInventoryFullSubscribers_DoesNotThrow()
        {
            // Arrange
            FillBag(Player);
            _inventory.OnInventoryFull -= RecordFullEvent;

            // Act
            PickupResult result = default;
            Assert.DoesNotThrow(() => result = _inventory.Pickup(Player, BronzeSwordItemId, 1));

            // Assert
            Assert.AreEqual(PickupFailReason.InventoryFull, result.Reason);
        }

        // -----------------------------------------------------------------------
        // Rule 4.10 — at most one notification per 600-tick window.
        // -----------------------------------------------------------------------

        [Test]
        public void DedupWindowTicks_Is30SecondsOfServerTicks_Tuning()
        {
            Assert.AreEqual(30 * ServerTickLoop.TICK_RATE_HZ, (int)InventoryConstants.BAG_FULL_DEDUP_WINDOW_TICKS);
            Assert.AreEqual(600u, InventoryConstants.BAG_FULL_DEDUP_WINDOW_TICKS);
        }

        [Test]
        public void Pickup_BlockedRepeatedly_FiresOnlyOncePerWindow_BoundaryInclusive_Rule4_10()
        {
            // Arrange
            FillBag(Player);

            // Act / Assert — every blocked call fails; only ticks 0 and 600 notify.
            BlockedPickupAt(0u, Player);
            Assert.AreEqual(1, _fullEvents.Count, "Tick 0: first blocked drop notifies.");

            BlockedPickupAt(200u, Player);
            Assert.AreEqual(1, _fullEvents.Count, "Tick 200: inside the window, suppressed.");

            BlockedPickupAt(599u, Player);
            Assert.AreEqual(1, _fullEvents.Count, "Tick 599: last tick inside the window, suppressed.");

            BlockedPickupAt(600u, Player);
            Assert.AreEqual(2, _fullEvents.Count, "Tick 600: window [0, 600) has expired, notifies.");
        }

        [Test]
        public void Pickup_SuccessfulPickupInsideWindow_ResetsWindow_NextBlockedFiresImmediately_Rule4_10()
        {
            // Arrange
            FillBag(Player);
            BlockedPickupAt(0u, Player);
            _inventory.SeedSlotForTesting(Player, 5, ItemID.Invalid, 0);

            // Act — tick 100: the sword fills slot 5, bag is full again; tick 120: blocked.
            _tick = 100u;
            var success = _inventory.Pickup(Player, BronzeSwordItemId, 1);
            BlockedPickupAt(120u, Player);

            // Assert
            Assert.IsTrue(success.Success);
            Assert.AreEqual(2, _fullEvents.Count, "The successful pickup must reset the window.");
        }

        [Test]
        public void Pickup_BlockedDifferentItemInsideWindow_StillSuppressed_Rule4_10()
        {
            // Arrange
            FillBag(Player);
            BlockedPickupAt(0u, Player); // Bronze Sword

            // Act — a different item, also blocked, inside the same window.
            _tick = 200u;
            var result = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert — the window is per character, not per item.
            Assert.AreEqual(PickupFailReason.InventoryFull, result.Reason);
            Assert.AreEqual(1, _fullEvents.Count);
        }

        [Test]
        public void Pickup_SuccessfulPickupOfDifferentItem_ResetsWindow_Rule4_10()
        {
            // Arrange — blocked on a sword at tick 0, then slot 5 is freed.
            FillBag(Player);
            BlockedPickupAt(0u, Player);
            _inventory.SeedSlotForTesting(Player, 5, ItemID.Invalid, 0);

            // Act — tick 100: a potion (not the blocked item) fills slot 5; tick 120: sword blocked.
            _tick = 100u;
            var success = _inventory.Pickup(Player, HPPotionItemId, 1);
            BlockedPickupAt(120u, Player);

            // Assert
            Assert.IsTrue(success.Success);
            Assert.AreEqual(2, _fullEvents.Count, "Any successful pickup resets the character's window.");
        }

        [Test]
        public void Pickup_WindowSpanningTickWraparound_ExpiresAfter600Ticks()
        {
            // Arrange
            FillBag(Player);
            uint start = uint.MaxValue - 100u;

            // Act / Assert — the window's expiry tick wraps to 499.
            BlockedPickupAt(start, Player);
            Assert.AreEqual(1, _fullEvents.Count);

            BlockedPickupAt(200u, Player); // 301 ticks after start
            Assert.AreEqual(1, _fullEvents.Count, "301 ticks after start (wrapped): still inside the window.");

            BlockedPickupAt(499u, Player); // exactly 600 ticks after start
            Assert.AreEqual(2, _fullEvents.Count, "600 ticks after start (wrapped): window expired.");
        }

        // -----------------------------------------------------------------------
        // Rule 4.11 — per-character isolation.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_TwoCharactersBlocked_EachHasOwnWindow_Rule4_11()
        {
            // Arrange
            _inventory.RegisterCharacter(OtherPlayer);
            FillBag(Player);
            FillBag(OtherPlayer);

            // Act
            BlockedPickupAt(0u, Player);
            BlockedPickupAt(20u, OtherPlayer);
            BlockedPickupAt(40u, Player);

            // Assert
            Assert.AreEqual(2, _fullEvents.Count, "Player at tick 40 is still inside its own window.");
            Assert.AreEqual(Player, _fullEvents[0]);
            Assert.AreEqual(OtherPlayer, _fullEvents[1], "OtherPlayer's first blocked drop notifies regardless of Player's window.");
        }

        [Test]
        public void Pickup_SuccessForOneCharacter_DoesNotResetAnotherCharactersWindow_Rule4_11()
        {
            // Arrange
            _inventory.RegisterCharacter(OtherPlayer);
            FillBag(Player);
            BlockedPickupAt(0u, Player);

            // Act — OtherPlayer (empty bag) picks up successfully, then Player is blocked again.
            _tick = 10u;
            Assert.IsTrue(_inventory.Pickup(OtherPlayer, HPPotionItemId, 1).Success);
            BlockedPickupAt(20u, Player);

            // Assert
            Assert.AreEqual(1, _fullEvents.Count, "OtherPlayer's success must not reset Player's window.");
        }

        // -----------------------------------------------------------------------
        // Invalid requests are not blocked drops.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_InvalidRequestsOnFullBag_NeverFireOrTouchDedupState()
        {
            // Arrange
            FillBag(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*not a known item"));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*not a registered character"));

            // Act
            var zeroQuantity = _inventory.Pickup(Player, HPPotionItemId, 0);
            var unknownItem = _inventory.Pickup(Player, UnregisteredItemId, 1);
            var unknownCharacter = _inventory.Pickup(UnregisteredPlayer, HPPotionItemId, 1);

            // Assert
            Assert.AreEqual(PickupFailReason.InvalidQuantity, zeroQuantity.Reason);
            Assert.AreEqual(PickupFailReason.UnknownItem, unknownItem.Reason);
            Assert.AreEqual(PickupFailReason.CharacterNotRegistered, unknownCharacter.Reason);
            Assert.AreEqual(0, _fullEvents.Count, "Invalid requests must never fire OnInventoryFull.");

            BlockedPickupAt(1u, Player);
            Assert.AreEqual(1, _fullEvents.Count, "No window was opened by the invalid requests; the real blocked drop notifies.");
        }

        // -----------------------------------------------------------------------
        // Re-registration, re-entrancy, constructor.
        // -----------------------------------------------------------------------

        [Test]
        public void RegisterCharacter_ClearsDedupWindow_NextBlockedFiresImmediately()
        {
            // Arrange
            FillBag(Player);
            BlockedPickupAt(0u, Player);

            // Act
            _inventory.RegisterCharacter(Player);
            FillBag(Player);
            BlockedPickupAt(10u, Player);

            // Assert
            Assert.AreEqual(2, _fullEvents.Count);
        }

        [Test]
        public void OnInventoryFull_SubscriberMutatesInventory_ThrowsAndStillConsumesWindow()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot();
            _inventory.OnInventoryFull += ReentrantPickupHandler;

            // Act / Assert
            _tick = 0u;
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, BronzeSwordItemId, 1));
            AssertSlotsUnchanged(before);
            Assert.AreEqual(1, _fullEvents.Count, "The recorder (subscribed first) saw the one dispatch.");

            _inventory.OnInventoryFull -= ReentrantPickupHandler;
            BlockedPickupAt(10u, Player);
            Assert.AreEqual(1, _fullEvents.Count, "The throwing dispatch still consumed the window.");
        }

        [Test]
        public void Constructor_NullTickProvider_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new InventoryService(_itemDatabase, null));
        }
    }
}
