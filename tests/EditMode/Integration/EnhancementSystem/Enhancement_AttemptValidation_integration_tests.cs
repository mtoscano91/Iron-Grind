using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// EditMode integration tests for Enhancement System Story 003: attempt validation and result
    /// codes (design/gdd/enhancement-system.md CR-ENH-2, CR-ENH-3, CR-ENH-4, CR-ENH-5, CR-ENH-15
    /// step 2, CR-ENH-17, UI-ENH-2; AC-ENH-3, AC-ENH-4, AC-ENH-5, AC-ENH-14, AC-ENH-22, AC-ENH-27,
    /// AC-ENH-37), using a real <see cref="InventoryService"/> over a stub item database.
    /// Every rejection must leave the bag, both lock flags and the event stream untouched.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_AttemptValidation_Integration_Tests
    {
        private const uint RAW_PLAYER = 1001u;
        private const uint RAW_UNREGISTERED_PLAYER = 1999u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint IRON_SWORD_ID = 4002u;
        private const uint BRONZE_RING_ID = 4003u;
        private const uint BRONZE_PLAIN_ID = 4004u;          // Bronze weapon, not upgradeable.
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint IRON_SCROLL_ID = 4006u;
        private const uint HP_POTION_ID = 4007u;
        private const uint BAD_UPGRADEABLE_ID = 4008u;       // Consumable marked upgradeable, no EquipmentData.
        private const uint BRONZE_NECKLACE_ID = 4009u;
        private const uint UNKNOWN_ID = 4999u;               // Never registered in the stub database.

        private const int ITEM_SLOT = 3;
        private const int SCROLL_SLOT = 5;
        private const int OTHER_SLOT = 8;
        private const int SAME_SLOT = 0;
        private const int OUT_OF_RANGE_LOW = -1;
        private const int OUT_OF_RANGE_HIGH = InventoryConstants.INVENTORY_SLOT_COUNT;   // One past the end.

        private const byte SEEDED_LEVEL = 2;
        private const int SCROLL_QUANTITY = 5;
        private const int POTION_QUANTITY = 7;
        private const int STACK_LIMIT_CONSUMABLE = 99;

        private static readonly CharacterID Player = new CharacterID(RAW_PLAYER);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(RAW_UNREGISTERED_PLAYER);

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID IronSwordId = new ItemID(IRON_SWORD_ID);
        private static readonly ItemID BronzeRingId = new ItemID(BRONZE_RING_ID);
        private static readonly ItemID BronzePlainId = new ItemID(BRONZE_PLAIN_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);
        private static readonly ItemID IronScrollId = new ItemID(IRON_SCROLL_ID);
        private static readonly ItemID HpPotionId = new ItemID(HP_POTION_ID);
        private static readonly ItemID BadUpgradeableId = new ItemID(BAD_UPGRADEABLE_ID);
        private static readonly ItemID BronzeNecklaceId = new ItemID(BRONZE_NECKLACE_ID);
        private static readonly ItemID UnknownId = new ItemID(UNKNOWN_ID);

        private byte _maxLevel;
        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private StubNpcSessions _sessions;
        private EnhancementService _service;
        private List<ItemDefinition> _definitions;
        private int _inventoryEvents;

        /// <summary>Full copy of a character's bag and lock flags plus the event count at capture time.</summary>
        private sealed class BagSnapshot
        {
            public InventorySlot[] Slots;
            public bool[] Locks;
            public int EventCount;
        }

        [SetUp]
        public void SetUp()
        {
            _maxLevel = EnhancementConfig.Default.MaxEnhancementLevel;

            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BRONZE_SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SWORD_ID, "Iron Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Iron)),
                ItemDefinitionBuilder.Build(BRONZE_RING_ID, "Bronze Ring", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Ring, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(BRONZE_NECKLACE_ID, "Bronze Necklace", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Necklace, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(BRONZE_PLAIN_ID, "Bronze Plain Blade", ItemCategory.Equipment,
                    isUpgradeable: false, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(BRONZE_SCROLL_ID, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SCROLL_ID, "Iron Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Iron)),
                ItemDefinitionBuilder.Build(HP_POTION_ID, "HP Potion", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE),
                ItemDefinitionBuilder.Build(BAD_UPGRADEABLE_ID, "Mislabelled Consumable", ItemCategory.Consumable,
                    isUpgradeable: true, stackLimit: STACK_LIMIT_CONSUMABLE),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _inventory.RegisterCharacter(Player);

            _inventoryEvents = 0;
            _inventory.OnInventoryChanged += _ => _inventoryEvents++;

            _sessions = new StubNpcSessions();
            _service = new EnhancementService(_inventory, _itemDatabase, EnhancementConfig.Default, _sessions, new System.Random(0));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void SeedSword(int slot, ItemID swordId, byte level)
        {
            _inventory.SeedSlotForTesting(Player, slot, swordId, 1, level);
        }

        private void SeedItem(int slot, ItemID itemId)
        {
            _inventory.SeedSlotForTesting(Player, slot, itemId, 1);
        }

        private void SeedScroll(int slot, ItemID scrollId)
        {
            _inventory.SeedSlotForTesting(Player, slot, scrollId, SCROLL_QUANTITY);
        }

        // Valid baseline: Bronze sword at SEEDED_LEVEL in ITEM_SLOT, Bronze scroll in SCROLL_SLOT.
        private void SeedValidRequest()
        {
            SeedSword(ITEM_SLOT, BronzeSwordId, SEEDED_LEVEL);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);
        }

        private BagSnapshot TakeSnapshot()
        {
            var snapshot = new BagSnapshot
            {
                Slots = new InventorySlot[InventoryConstants.INVENTORY_SLOT_COUNT],
                Locks = new bool[InventoryConstants.INVENTORY_SLOT_COUNT],
                EventCount = _inventoryEvents,
            };
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                snapshot.Slots[i] = _inventory.GetSlot(Player, i);
                snapshot.Locks[i] = _inventory.IsSlotLocked(Player, i);
            }
            return snapshot;
        }

        private void AssertBagUnchanged(BagSnapshot before)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                var slot = _inventory.GetSlot(Player, i);
                Assert.AreEqual(before.Slots[i].ItemId, slot.ItemId, $"Slot {i} ItemId.");
                Assert.AreEqual(before.Slots[i].Quantity, slot.Quantity, $"Slot {i} Quantity.");
                Assert.AreEqual(before.Slots[i].EnhancementLevel, slot.EnhancementLevel, $"Slot {i} EnhancementLevel.");
                Assert.AreEqual(before.Locks[i], _inventory.IsSlotLocked(Player, i), $"Slot {i} lock flag.");
            }
            Assert.AreEqual(before.EventCount, _inventoryEvents, "OnInventoryChanged must not fire during validation.");
        }

        // Runs the call, asserts the rejection code and empty payload, and that nothing in the bag changed.
        private void AssertRejected(int itemSlot, int scrollSlot, EnhancementResultCode expected)
        {
            var before = TakeSnapshot();

            var result = _service.ValidateAttempt(Player, itemSlot, scrollSlot);

            Assert.IsFalse(result.IsValid, "IsValid.");
            Assert.AreEqual(expected, result.RejectionCode, "RejectionCode.");
            Assert.AreEqual(ItemID.Invalid, result.ItemId, "ItemId on rejection.");
            Assert.AreEqual(ItemID.Invalid, result.ScrollItemId, "ScrollItemId on rejection.");
            Assert.AreEqual(0, result.CurrentLevel, "CurrentLevel on rejection.");
            AssertBagUnchanged(before);
        }

        // As above, for rejections that must be completely silent in the log.
        private void AssertRejectedSilently(int itemSlot, int scrollSlot, EnhancementResultCode expected)
        {
            AssertRejected(itemSlot, scrollSlot, expected);
            LogAssert.NoUnexpectedReceived();
        }

        private static void ExpectServiceError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[EnhancementService\] ValidateAttempt: "));
        }

        // =====================================================================
        // Constructor and surface
        // =====================================================================

        [Test]
        public void Constructor_NullInventory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new EnhancementService(null, _itemDatabase, EnhancementConfig.Default, _sessions, new System.Random(0)));
        }

        [Test]
        public void Constructor_NullItemDatabase_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new EnhancementService(_inventory, null, EnhancementConfig.Default, _sessions, new System.Random(0)));
        }

        [Test]
        public void Constructor_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new EnhancementService(_inventory, _itemDatabase, null, _sessions, new System.Random(0)));
        }

        [Test]
        public void Constructor_NullNpcSessions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new EnhancementService(_inventory, _itemDatabase, EnhancementConfig.Default, null, new System.Random(0)));
        }

        [Test]
        public void IsAttemptInProgress_WithNoAttemptPending_IsFalse()
        {
            // Act / Assert
            Assert.IsFalse(_service.IsAttemptInProgress(Player));
            Assert.IsFalse(_service.IsAttemptInProgress(UnregisteredPlayer));
        }

        [Test]
        public void EnhancementResultCode_HasTenMembersInOrder_BackedByByte()
        {
            // Arrange
            var expected = new[]
            {
                (EnhancementResultCode.Success, (byte)0),
                (EnhancementResultCode.Destruction, (byte)1),
                (EnhancementResultCode.RejectedAtMaxLevel, (byte)2),
                (EnhancementResultCode.RejectedTierMismatch, (byte)3),
                (EnhancementResultCode.RejectedAccessoryType, (byte)4),
                (EnhancementResultCode.RejectedItemNotFound, (byte)5),
                (EnhancementResultCode.RejectedConcurrentAttempt, (byte)6),
                (EnhancementResultCode.RejectedNoNPCSession, (byte)7),
                (EnhancementResultCode.RejectedScrollNotFound, (byte)8),
                (EnhancementResultCode.RejectedNotUpgradeable, (byte)9),
            };

            // Act
            var actualValues = (EnhancementResultCode[])Enum.GetValues(typeof(EnhancementResultCode));

            // Assert
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(typeof(EnhancementResultCode)));
            Assert.AreEqual(expected.Length, actualValues.Length, "Member count.");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].Item1, actualValues[i], $"Member {i} name/order.");
                Assert.AreEqual(expected[i].Item2, (byte)actualValues[i], $"Member {i} value.");
            }
        }

        // =====================================================================
        // Valid request
        // =====================================================================

        [Test]
        public void ValidateAttempt_ValidRequest_ReturnsItemLevelAndScroll_NothingChanged()
        {
            // Arrange
            SeedValidRequest();
            var before = TakeSnapshot();

            // Act
            var result = _service.ValidateAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(result.IsValid, "IsValid.");
            Assert.AreEqual(BronzeSwordId, result.ItemId, "ItemId.");
            Assert.AreEqual(SEEDED_LEVEL, result.CurrentLevel, "CurrentLevel.");
            Assert.AreEqual(BronzeScrollId, result.ScrollItemId, "ScrollItemId.");
            AssertBagUnchanged(before);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must not be locked.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, SCROLL_SLOT), "Scroll slot must not be locked.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ValidateAttempt_ValidIronRequest_ReturnsIronSwordAndIronScroll()
        {
            // Arrange
            SeedSword(ITEM_SLOT, IronSwordId, 0);
            SeedScroll(SCROLL_SLOT, IronScrollId);

            // Act
            var result = _service.ValidateAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(result.IsValid, "IsValid.");
            Assert.AreEqual(IronSwordId, result.ItemId);
            Assert.AreEqual(0, result.CurrentLevel);
            Assert.AreEqual(IronScrollId, result.ScrollItemId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ValidateAttempt_ValidRequestOneBelowMax_IsValid()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, (byte)(_maxLevel - 1));
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act
            var result = _service.ValidateAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(result.IsValid, "IsValid.");
            Assert.AreEqual((byte)(_maxLevel - 1), result.CurrentLevel);
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Rejections in check order
        // =====================================================================

        [Test]
        public void ValidateAttempt_NoActiveNpcSession_RejectedNoNPCSession_NothingChanged_AC_ENH_27()
        {
            // Arrange
            SeedValidRequest();
            _sessions.Active = false;

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedNoNPCSession);
            Assert.AreEqual(SCROLL_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll quantity.");
        }

        [Test]
        public void ValidateAttempt_ItemSlotEmptied_RejectedItemNotFound_ScrollQuantityUnchanged_AC_ENH_22()
        {
            // Arrange
            SeedValidRequest();
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, ItemID.Invalid, 0);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedItemNotFound);
            Assert.AreEqual(SCROLL_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll quantity.");
        }

        [Test]
        public void ValidateAttempt_ItemSlotEmptyBecauseSwordEquipped_RejectedItemNotFound_NoSlotLocked_AC_ENH_4()
        {
            // Arrange: slot 0 is empty (its sword is "equipped"), scroll in slot 1.
            const int emptiedSlot = 0;
            const int scrollSlot = 1;
            SeedScroll(scrollSlot, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(emptiedSlot, scrollSlot, EnhancementResultCode.RejectedItemNotFound);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, emptiedSlot), "Item slot must not be locked.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, scrollSlot), "Scroll slot must not be locked.");
        }

        [Test]
        public void ValidateAttempt_ItemSlotLocked_RejectedConcurrentAttempt_LockStaysSet()
        {
            // Arrange
            SeedValidRequest();
            _inventory.LockSlot(Player, ITEM_SLOT);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedConcurrentAttempt);
            Assert.IsTrue(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Lock must still be set.");
        }

        [Test]
        public void ValidateAttempt_NonUpgradeableItem_RejectedNotUpgradeable_NeverLocked_AC_ENH_37()
        {
            // Arrange
            SeedItem(ITEM_SLOT, BronzePlainId);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedNotUpgradeable);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must never be locked.");
        }

        [TestCase(BRONZE_RING_ID)]
        [TestCase(BRONZE_NECKLACE_ID)]
        public void ValidateAttempt_Accessory_RejectedAccessoryType_ScrollRemains_AC_ENH_5(uint rawAccessoryId)
        {
            // Arrange
            SeedItem(ITEM_SLOT, new ItemID(rawAccessoryId));
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedAccessoryType);
            Assert.AreEqual(BronzeScrollId, _inventory.GetSlot(Player, SCROLL_SLOT).ItemId, "Scroll item.");
            Assert.AreEqual(SCROLL_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll quantity.");
        }

        [Test]
        public void ValidateAttempt_SwordAtMaxLevel_RejectedAtMaxLevel_ScrollRemains_LevelUnchanged_AC_ENH_14()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, _maxLevel);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedAtMaxLevel);
            Assert.AreEqual(_maxLevel, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level.");
            Assert.AreEqual(SCROLL_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll quantity.");
        }

        [Test]
        public void ValidateAttempt_SwordAboveMaxLevel_RejectedAtMaxLevel()
        {
            // Arrange — a level above the cap cannot arise in play (the inventory bounds it), but the
            // check is "at or above the cap", not "equal to it"; the test seam can seed such a slot.
            byte aboveMax = (byte)(_maxLevel + 1);
            SeedSword(ITEM_SLOT, BronzeSwordId, aboveMax);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedAtMaxLevel);
        }

        [Test]
        public void ValidateAttempt_ScrollSlotEmpty_RejectedScrollNotFound()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, SEEDED_LEVEL);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedScrollNotFound);
        }

        [Test]
        public void ValidateAttempt_ScrollSlotHoldsPotion_RejectedScrollNotFound()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, SEEDED_LEVEL);
            _inventory.SeedSlotForTesting(Player, SCROLL_SLOT, HpPotionId, POTION_QUANTITY);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedScrollNotFound);
            Assert.AreEqual(POTION_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Potion quantity.");
        }

        [Test]
        public void ValidateAttempt_BronzeSwordWithIronScroll_RejectedTierMismatch_ScrollAndLevelUnchanged_AC_ENH_3()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, SEEDED_LEVEL);
            SeedScroll(SCROLL_SLOT, IronScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedTierMismatch);
            Assert.AreEqual(SCROLL_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Iron scroll quantity.");
            Assert.AreEqual(SEEDED_LEVEL, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level.");
        }

        // =====================================================================
        // Check order (first failure wins)
        // =====================================================================

        [Test]
        public void ValidateAttempt_InactiveSessionAndEmptyItemSlot_ReportsNoNPCSession()
        {
            // Arrange
            _sessions.Active = false;
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedNoNPCSession);
        }

        [Test]
        public void ValidateAttempt_RingAtMaxLevel_ReportsAccessoryTypeBeforeMaxLevel()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, BronzeRingId, 1, _maxLevel);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedAccessoryType);
        }

        [Test]
        public void ValidateAttempt_MaxLevelSwordWithMismatchedScroll_ReportsMaxLevelBeforeTierMismatch()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, _maxLevel);
            SeedScroll(SCROLL_SLOT, IronScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedAtMaxLevel);
        }

        [Test]
        public void ValidateAttempt_LockedNonUpgradeableItem_ReportsConcurrentAttemptBeforeNotUpgradeable()
        {
            // Arrange
            SeedItem(ITEM_SLOT, BronzePlainId);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);
            _inventory.LockSlot(Player, ITEM_SLOT);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedConcurrentAttempt);
        }

        [Test]
        public void ValidateAttempt_NonUpgradeableItemWithMissingScroll_ReportsNotUpgradeableBeforeScrollNotFound()
        {
            // Arrange
            SeedItem(ITEM_SLOT, BronzePlainId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedNotUpgradeable);
        }

        // =====================================================================
        // Out-of-range indices
        // =====================================================================

        [TestCase(OUT_OF_RANGE_LOW)]
        [TestCase(OUT_OF_RANGE_HIGH)]
        public void ValidateAttempt_ItemSlotOutOfRange_RejectedItemNotFound_NoLog(int itemSlot)
        {
            // Arrange
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(itemSlot, SCROLL_SLOT, EnhancementResultCode.RejectedItemNotFound);
        }

        [TestCase(OUT_OF_RANGE_LOW)]
        [TestCase(OUT_OF_RANGE_HIGH)]
        public void ValidateAttempt_ScrollSlotOutOfRange_RejectedScrollNotFound_NoLog(int scrollSlot)
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, SEEDED_LEVEL);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, scrollSlot, EnhancementResultCode.RejectedScrollNotFound);
        }

        // =====================================================================
        // Same slot twice
        // =====================================================================

        [Test]
        public void ValidateAttempt_SwordInSameSlotForBoth_RejectedScrollNotFound()
        {
            // Arrange
            SeedSword(SAME_SLOT, BronzeSwordId, SEEDED_LEVEL);

            // Act / Assert
            AssertRejectedSilently(SAME_SLOT, SAME_SLOT, EnhancementResultCode.RejectedScrollNotFound);
        }

        [Test]
        public void ValidateAttempt_ScrollInSameSlotForBoth_RejectedNotUpgradeable()
        {
            // Arrange
            SeedScroll(SAME_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(SAME_SLOT, SAME_SLOT, EnhancementResultCode.RejectedNotUpgradeable);
        }

        // =====================================================================
        // Non-equipment items in the item slot
        // =====================================================================

        [Test]
        public void ValidateAttempt_PotionInItemSlot_RejectedNotUpgradeable()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, HpPotionId, POTION_QUANTITY);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedNotUpgradeable);
        }

        [Test]
        public void ValidateAttempt_UpgradeableFlaggedConsumableWithoutEquipmentData_RejectedNotUpgradeable_NoException()
        {
            // Arrange
            SeedItem(ITEM_SLOT, BadUpgradeableId);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);

            // Act / Assert
            Assert.DoesNotThrow(() => _service.ValidateAttempt(Player, ITEM_SLOT, SCROLL_SLOT));
            AssertRejectedSilently(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedNotUpgradeable);
        }

        // =====================================================================
        // Unresolvable items (one error logged each)
        // =====================================================================

        [Test]
        public void ValidateAttempt_ItemSlotHoldsUnknownItem_RejectedItemNotFound_OneErrorLogged()
        {
            // Arrange: SeedSlotForTesting does not consult the item database, so an unknown id can be seeded.
            SeedItem(ITEM_SLOT, UnknownId);
            SeedScroll(SCROLL_SLOT, BronzeScrollId);
            ExpectServiceError();

            // Act / Assert
            AssertRejected(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedItemNotFound);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ValidateAttempt_ScrollSlotHoldsUnknownItem_RejectedScrollNotFound_OneErrorLogged()
        {
            // Arrange
            SeedSword(ITEM_SLOT, BronzeSwordId, SEEDED_LEVEL);
            _inventory.SeedSlotForTesting(Player, SCROLL_SLOT, UnknownId, SCROLL_QUANTITY);
            ExpectServiceError();

            // Act / Assert
            AssertRejected(ITEM_SLOT, SCROLL_SLOT, EnhancementResultCode.RejectedScrollNotFound);
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Unregistered character
        // =====================================================================

        [Test]
        public void ValidateAttempt_UnregisteredCharacter_RejectedItemNotFound_OnlyInventoryErrorLogged()
        {
            // Arrange: the session stub is active for every character.
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] GetSlot: .*is not a registered character"));

            // Act
            var result = _service.ValidateAttempt(UnregisteredPlayer, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsFalse(result.IsValid, "IsValid.");
            Assert.AreEqual(EnhancementResultCode.RejectedItemNotFound, result.RejectionCode, "RejectionCode.");
            Assert.AreEqual(ItemID.Invalid, result.ItemId, "ItemId on rejection.");
            Assert.AreEqual(ItemID.Invalid, result.ScrollItemId, "ScrollItemId on rejection.");
            Assert.AreEqual(0, result.CurrentLevel, "CurrentLevel on rejection.");
            Assert.AreEqual(0, _inventoryEvents, "OnInventoryChanged count.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
