using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// EditMode unit tests for Equipment System Story 005 — the <c>equipmentAppearanceFlags</c> byte and
    /// <c>OnAppearanceChanged</c> (CR-EQS-10, CR-EQS-11, EC-EQS-8, EC-EQS-9, EC-EQS-11; AC-EQS-11,
    /// AC-EQS-18, AC-EQS-30; TR-equip-007). Real <c>CharacterStats</c> and <c>InventoryService</c>;
    /// <see cref="InventoryDecorator"/> injects swap failures.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_AppearanceFlags_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const int BASE_STR = 20;
        private const float REQUIREMENT_ABOVE_BASE = 50f;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);

        private static readonly ItemID DarkSteelFireSword = new ItemID(101u);
        private static readonly ItemID IronSword = new ItemID(102u);
        private static readonly ItemID IronSwordB = new ItemID(103u);
        private static readonly ItemID BronzeSword = new ItemID(104u);
        private static readonly ItemID GatedSword = new ItemID(105u);
        private static readonly ItemID TwoModSword = new ItemID(106u);
        private static readonly ItemID SteelSword = new ItemID(107u);
        private static readonly ItemID BronzePoisonSword = new ItemID(108u);
        private static readonly ItemID SteelLegs = new ItemID(231u);
        private static readonly ItemID BronzeFireHelmet = new ItemID(214u);
        private static readonly ItemID DarkSteelRing = new ItemID(241u);
        private static readonly ItemID DarkSteelNecklace = new ItemID(251u);
        private static readonly ItemID DarkSteelChest = new ItemID(201u);
        private static readonly ItemID SteelChest = new ItemID(202u);
        private static readonly ItemID BronzeChest = new ItemID(203u);
        private static readonly ItemID IronHelmet = new ItemID(211u);
        private static readonly ItemID BronzeHelmet = new ItemID(212u);
        private static readonly ItemID BronzeHelmetB = new ItemID(213u);
        private static readonly ItemID SteelBoots = new ItemID(221u);
        private static readonly ItemID FillerId = new ItemID(900u);

        private FakeItemDatabase _db;
        private InventoryService _realInventory;
        private InventoryDecorator _inventory;
        private Stats _stats;
        private EquipmentService _equipment;
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private readonly List<EquipmentAppearanceChangedEventArgs> _appearanceEvents = new List<EquipmentAppearanceChangedEventArgs>();
        private int _statEvents;

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _appearanceEvents.Clear();
            _statEvents = 0;
            _db = new FakeItemDatabase();
            _realInventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory = new InventoryDecorator(_realInventory);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            _equipment = new EquipmentService(
                _inventory, _stats, _db, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default);
            _equipment.RegisterCharacter(Player, PlayerEntity);
            _equipment.OnAppearanceChanged += OnAppearanceChanged;
            _stats.Subscribe(OnStatChanged);

            AddItem(DarkSteelFireSword, GearSlot.Weapon, GearTier.DarkSteel, ElementType.Fire);
            AddItem(IronSword, GearSlot.Weapon, GearTier.Iron);
            AddItem(IronSwordB, GearSlot.Weapon, GearTier.Iron);
            AddItem(BronzeSword, GearSlot.Weapon, GearTier.Bronze);
            AddItem(GatedSword, GearSlot.Weapon, GearTier.Steel, requirementStat: StatID.Strength, requirementMin: REQUIREMENT_ABOVE_BASE);
            AddItem(TwoModSword, GearSlot.Weapon, GearTier.Iron, ElementType.None, null, 0f, Mod(StatID.Strength, 5f), Mod(StatID.Defense, 3f));
            AddItem(SteelSword, GearSlot.Weapon, GearTier.Steel);
            AddItem(BronzePoisonSword, GearSlot.Weapon, GearTier.Bronze, ElementType.Poison);
            AddItem(SteelLegs, GearSlot.Legs, GearTier.Steel);
            AddItem(BronzeFireHelmet, GearSlot.Helmet, GearTier.Bronze, ElementType.Fire);
            AddItem(DarkSteelRing, GearSlot.Ring, GearTier.DarkSteel, ElementType.Lightning);
            AddItem(DarkSteelNecklace, GearSlot.Necklace, GearTier.DarkSteel, ElementType.Cold);
            AddItem(DarkSteelChest, GearSlot.Chest, GearTier.DarkSteel);
            AddItem(SteelChest, GearSlot.Chest, GearTier.Steel);
            AddItem(BronzeChest, GearSlot.Chest, GearTier.Bronze);
            AddItem(IronHelmet, GearSlot.Helmet, GearTier.Iron);
            AddItem(BronzeHelmet, GearSlot.Helmet, GearTier.Bronze);
            AddItem(BronzeHelmetB, GearSlot.Helmet, GearTier.Bronze);
            AddItem(SteelBoots, GearSlot.Boots, GearTier.Steel);
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

        private void OnAppearanceChanged(EquipmentAppearanceChangedEventArgs args) => _appearanceEvents.Add(args);

        private void OnStatChanged(EntityID entity, StatID stat) => _statEvents++;

        private static StatModifierEntry Mod(StatID stat, float flat) => StatModifierEntry.CreateForTesting(stat, flat);

        private void AddItem(
            ItemID id, GearSlot slot, GearTier tier, ElementType element = ElementType.None,
            StatID? requirementStat = null, float requirementMin = 0f, params StatModifierEntry[] modifiers)
        {
            EquipmentData data = EquipmentData.CreateForTesting(
                slot, tier, modifiers, elementType: element, equipRequirementStat: requirementStat, equipRequirementMin: requirementMin);
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

        private EquipResult EquipFromBag(GearSlot slot, ItemID id, byte level = 0) => _equipment.Equip(Player, slot, id, PutInBag(id, level));

        // Equips for test setup and clears the recorded events, so the act step's events stand alone.
        private void EquipForSetup(GearSlot slot, ItemID id, byte level = 0)
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(slot, id, level), "test setup: equip");
            ResetEvents();
        }

        private void ResetEvents()
        {
            _appearanceEvents.Clear();
            _statEvents = 0;
        }

        private byte Flags() => _equipment.GetAppearanceFlags(Player);

        private void AssertOneEvent(byte expected)
        {
            Assert.AreEqual(1, _appearanceEvents.Count, "exactly one OnAppearanceChanged");
            Assert.AreEqual(expected, _appearanceEvents[0].Flags, "event byte");
            Assert.AreEqual(Player, _appearanceEvents[0].CharacterId, "event character");
            Assert.AreEqual(PlayerEntity, _appearanceEvents[0].EntityId, "event entity");
        }

        // ── Encoder (CR-EQS-11) ────────────────────────────────────────────────────────────

        [TestCase(0, 0x00)]
        [TestCase(1, 0x40)]
        [TestCase(2, 0x80)]
        [TestCase(3, 0xC0)]
        public void test_Encode_WeaponTierAlone_LandsInBits7To6(int tier, int expected)
        {
            Assert.AreEqual((byte)expected, EquipmentAppearanceEncoder.Encode((byte)tier, 0, 0, 0));
        }

        [TestCase(0, 0x00)]
        [TestCase(1, 0x08)]
        [TestCase(2, 0x10)]
        [TestCase(3, 0x18)]
        [TestCase(4, 0x20)]
        public void test_Encode_ElementAlone_LandsInBits5To3(int element, int expected)
        {
            Assert.AreEqual((byte)expected, EquipmentAppearanceEncoder.Encode(0, (byte)element, 0, 0));
        }

        [TestCase(0, 0x00)]
        [TestCase(1, 0x02)]
        [TestCase(2, 0x04)]
        [TestCase(3, 0x06)]
        public void test_Encode_BandAlone_LandsInBits2To1(int band, int expected)
        {
            Assert.AreEqual((byte)expected, EquipmentAppearanceEncoder.Encode(0, 0, (byte)band, 0));
        }

        [Test]
        public void test_Encode_ArmorBitAlone_LandsInBit0()
        {
            Assert.AreEqual((byte)0x01, EquipmentAppearanceEncoder.Encode(0, 0, 0, 1));
        }

        [Test]
        public void test_Encode_AllMax_Is11_100_11_1()
        {
            Assert.AreEqual((byte)0b11_100_11_1, EquipmentAppearanceEncoder.Encode(3, (byte)ElementType.Poison, 3, 1));
        }

        [Test]
        public void test_Encode_OutOfRangeFields_AreMaskedToTheirWidth()
        {
            Assert.AreEqual((byte)0x00, EquipmentAppearanceEncoder.Encode(4, 8, 4, 2), "each value is one past its field");
            Assert.AreEqual((byte)0xFF, EquipmentAppearanceEncoder.Encode(0xFF, 0xFF, 0xFF, 0xFF));
        }

        [TestCase(GearTier.None, 0)]
        [TestCase(GearTier.Bronze, 0)]
        [TestCase(GearTier.Iron, 1)]
        [TestCase(GearTier.Steel, 2)]
        [TestCase(GearTier.DarkSteel, 3)]
        [TestCase((GearTier)99, 0)]
        public void test_GetTierBits_MapsExplicitly_NotByCast(GearTier tier, int expected)
        {
            Assert.AreEqual((byte)expected, EquipmentAppearanceEncoder.GetTierBits(tier));
        }

        // ── Service byte ───────────────────────────────────────────────────────────────────

        [Test]
        public void test_FreshCharacter_AllSlotsEmpty_FlagsAreZero_EC_EQS_11()
        {
            Assert.AreEqual((byte)0x00, Flags());
            Assert.AreEqual(0, _appearanceEvents.Count, "registration raises nothing");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_DarkSteelFireSwordPlusDarkSteelChest_Is0xC9_AC_EQS_11()
        {
            EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);
            EquipFromBag(GearSlot.Chest, DarkSteelChest);

            Assert.AreEqual((byte)0xC9, Flags());
            CollectionAssert.AreEqual(new byte[] { 0xC8, 0xC9 }, _appearanceEvents.ConvertAll(e => e.Flags), "sword, then chest");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_DarkSteelFireSwordPlusSteelChest_Is0xC9_AC_EQS_11()
        {
            EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);
            EquipFromBag(GearSlot.Chest, SteelChest);

            Assert.AreEqual((byte)0xC9, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(4, 0)]
        [TestCase(5, 1)]
        [TestCase(6, 1)]
        [TestCase(7, 2)]
        [TestCase(8, 3)]
        [TestCase(10, 3)]
        public void test_WeaponLevel_MapsToPrestigeBand_InBits2To1(int level, int band)
        {
            EquipFromBag(GearSlot.Weapon, BronzeSword, (byte)level);

            Assert.AreEqual((byte)(band << 1), Flags(), "Bronze, no element, no armor: only the band bits");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ArmorLevel_NeverSetsBand_AC_EQS_30a()
        {
            EquipFromBag(GearSlot.Chest, BronzeChest, 9);

            Assert.AreEqual((byte)0x00, Flags(), "+9 Bronze Chest alone");
            Assert.AreEqual(9, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Chest));
            Assert.AreEqual(0, _appearanceEvents.Count, "byte unchanged → no event");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sequence_ChestThenSwordThenSwapThenUnequip_AC_EQS_30()
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Chest, BronzeChest, 9));
            Assert.AreEqual((byte)0x00, Flags(), "(a) +9 Bronze Chest");
            Assert.AreEqual(9, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Chest), "(a) chest level");

            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, IronSword, 0));
            Assert.AreEqual((byte)0x40, Flags(), "(b) +0 Iron Sword");

            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, IronSword, 7));
            Assert.AreEqual((byte)0x44, Flags(), "(c) swap to +7 Iron Sword");

            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Weapon));
            Assert.AreEqual((byte)0x00, Flags(), "(d) unequip weapon");

            CollectionAssert.AreEqual(new byte[] { 0x40, 0x44, 0x00 }, _appearanceEvents.ConvertAll(e => e.Flags), "one event per byte change");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ArmorBit_IronHelmetOnly_IsZero()
        {
            EquipFromBag(GearSlot.Helmet, IronHelmet);

            Assert.AreEqual((byte)0x00, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ArmorBit_IronHelmetPlusSteelBoots_IsOne()
        {
            EquipFromBag(GearSlot.Helmet, IronHelmet);
            EquipFromBag(GearSlot.Boots, SteelBoots);

            Assert.AreEqual((byte)0x01, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ArmorBit_SteelChestUnequippedLeavingBronze_IsZero()
        {
            EquipFromBag(GearSlot.Helmet, BronzeHelmet);
            EquipFromBag(GearSlot.Chest, SteelChest);
            Assert.AreEqual((byte)0x01, Flags(), "setup: Steel Chest sets the bit");
            ResetEvents();

            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Chest));

            Assert.AreEqual((byte)0x00, Flags());
            AssertOneEvent(0x00);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ArmorBit_SteelLegsAlone_IsOne()
        {
            EquipFromBag(GearSlot.Legs, SteelLegs);

            Assert.AreEqual((byte)0x01, Flags(), "Legs is inside the Helmet..Boots range");
            AssertOneEvent(0x01);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_SteelWeaponNoArmor_Is0x80_WeaponTierNeverSetsArmorBit()
        {
            EquipFromBag(GearSlot.Weapon, SteelSword);

            Assert.AreEqual((byte)0x80, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_DarkSteelWeaponWithBronzeArmor_ArmorBitStaysZero()
        {
            EquipFromBag(GearSlot.Weapon, DarkSteelFireSword, 10);
            EquipFromBag(GearSlot.Chest, BronzeChest);
            EquipFromBag(GearSlot.Helmet, BronzeHelmet);

            Assert.AreEqual((byte)0xCE, Flags(), "Dark Steel, Fire, band High, armor 0");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_PoisonWeapon_ElementBitsAre0x20()
        {
            EquipFromBag(GearSlot.Weapon, BronzePoisonSword);

            Assert.AreEqual((byte)0x20, Flags(), "Bronze tier 0, Poison 4 << 3");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ElementOnNonWeapon_ElementBitsStayZero()
        {
            EquipFromBag(GearSlot.Helmet, BronzeFireHelmet);

            Assert.AreEqual((byte)0x00, Flags(), "element comes from the Weapon slot only");
            Assert.AreEqual(0, _appearanceEvents.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RingAndNecklace_NeverAffectTheByte()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);

            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Ring, DarkSteelRing, 10));
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Necklace, DarkSteelNecklace, 10));

            Assert.AreEqual((byte)0x40, Flags(), "only the Iron Sword counts");
            Assert.AreEqual(0, _appearanceEvents.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_NoWeapon_WeaponFieldsZero_ArmorStillCounts_EC_EQS_9()
        {
            EquipFromBag(GearSlot.Chest, DarkSteelChest, 10);

            Assert.AreEqual((byte)0x01, Flags(), "tier, element and band all 0; armor bit only");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_GetAppearanceFlags_Unregistered_LogsAndReturnsZero()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"GetAppearanceFlags: .* is not a registered character"));

            Assert.AreEqual((byte)0x00, _equipment.GetAppearanceFlags(OtherPlayer));
        }

        [Test]
        public void test_Reregister_ResetsFlagsToZero_WithoutEvent()
        {
            EquipForSetup(GearSlot.Weapon, DarkSteelFireSword);

            _equipment.UnregisterCharacter(Player);
            _equipment.RegisterCharacter(Player, PlayerEntity);

            Assert.AreEqual((byte)0x00, Flags());
            Assert.AreEqual(0, _appearanceEvents.Count);

            EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);

            AssertOneEvent(0xC8);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Event (EC-EQS-8) ───────────────────────────────────────────────────────────────

        [Test]
        public void test_Equip_ByteChanges_RaisesOneEventWithByteAndIds()
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, DarkSteelFireSword));

            AssertOneEvent(0xC8);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_ReadsSlotAlreadyWritten_WhenEventFires()
        {
            EquipmentSlotState seen = default;
            _equipment.OnAppearanceChanged += _ => seen = _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon);

            EquipFromBag(GearSlot.Weapon, IronSword);

            Assert.AreEqual(IronSword, seen.ItemId);
            Assert.AreEqual(SlotState.Occupied, seen.State, "raised after the slot write, flag cleared");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_NoChange_RaisesNothing()
        {
            int bagSlot = PutInBag(IronSword);
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, IronSword, bagSlot));
            int again = PutInBag(IronSword);
            ResetEvents();

            Assert.AreEqual(EquipResult.NoChange, _equipment.Equip(Player, GearSlot.Weapon, IronSword, again));

            Assert.AreEqual(0, _appearanceEvents.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_StatRequirementNotMet_RaisesNothing()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);

            Assert.AreEqual(EquipResult.StatRequirementNotMet, EquipFromBag(GearSlot.Weapon, GatedSword));

            Assert.AreEqual(0, _appearanceEvents.Count);
            Assert.AreEqual((byte)0x40, Flags(), "the recompute keeps the Iron Sword's byte");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_InventoryFull_RaisesNothing()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);
            int bagSlot = PutInBag(DarkSteelFireSword);
            while (_realInventory.FilledSlots(Player) < InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                PutInBag(FillerId);
            }

            Assert.AreEqual(EquipResult.InventoryFull, _equipment.Equip(Player, GearSlot.Weapon, DarkSteelFireSword, bagSlot));

            Assert.AreEqual(0, _appearanceEvents.Count);
            Assert.AreEqual((byte)0x40, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Swap_InventoryError_RaisesNothing()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);
            int bagSlot = PutInBag(DarkSteelFireSword);
            _inventory.FailMoveItemIn = true;
            LogAssert.Expect(LogType.Error, new Regex("Auto-swap aborted"));

            Assert.AreEqual(EquipResult.InventoryError, _equipment.Equip(Player, GearSlot.Weapon, DarkSteelFireSword, bagSlot));

            Assert.AreEqual(0, _appearanceEvents.Count);
            Assert.AreEqual((byte)0x40, Flags());
        }

        [Test]
        public void test_Unequip_InventoryError_RaisesNothing()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);
            _inventory.FailMoveItemIn = true;

            Assert.AreEqual(EquipResult.InventoryError, _equipment.Unequip(Player, GearSlot.Weapon));

            Assert.AreEqual(0, _appearanceEvents.Count);
            Assert.AreEqual((byte)0x40, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_BronzeHelmet_ByteUnchanged_RaisesNothing()
        {
            EquipForSetup(GearSlot.Boots, SteelBoots);

            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Helmet, BronzeHelmet));

            Assert.AreEqual(0, _appearanceEvents.Count);
            Assert.AreEqual((byte)0x01, Flags(), "the Steel Boots still set the armor bit");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Unequip_EmptySlot_NoChange_RaisesNothing()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);

            Assert.AreEqual(EquipResult.NoChange, _equipment.Unequip(Player, GearSlot.Chest));

            Assert.AreEqual(0, _appearanceEvents.Count);
            Assert.AreEqual((byte)0x40, Flags());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_WeaponDefinitionVanished_NextEquipDropsWeaponBits()
        {
            EquipForSetup(GearSlot.Weapon, DarkSteelFireSword);
            _db.Remove(DarkSteelFireSword);

            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Helmet, BronzeHelmet));

            Assert.AreEqual((byte)0x00, Flags(), "a vanished definition counts as empty, silently");
            AssertOneEvent(0x00);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_TwoSubscribers_EachReceiveTheEventOnce()
        {
            var second = new List<byte>();
            _equipment.OnAppearanceChanged += e => second.Add(e.Flags);

            EquipFromBag(GearSlot.Weapon, IronSword);

            AssertOneEvent(0x40);
            CollectionAssert.AreEqual(new byte[] { 0x40 }, second);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_SubscriberUnequipsInsideEvent_NestedEventInOrder_FinalByteMatchesSlots()
        {
            bool unequipped = false;
            _equipment.OnAppearanceChanged += _ =>
            {
                if (!unequipped)
                {
                    unequipped = true;
                    Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Weapon), "nested unequip");
                }
            };

            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, IronSword));

            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon).State);
            Assert.AreEqual((byte)0x00, Flags());
            CollectionAssert.AreEqual(new byte[] { 0x40, 0x00 }, _appearanceEvents.ConvertAll(e => e.Flags), "outer, then nested");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Equip_OutOfRangeSlotAndUnregistered_RaiseNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex("gear slot 7 is out of range"));
            LogAssert.Expect(LogType.Error, new Regex("is not a registered character"));

            Assert.AreEqual(EquipResult.SlotMismatch, _equipment.Equip(Player, (GearSlot)7, IronSword, 0));
            Assert.AreEqual(EquipResult.NoChange, _equipment.Equip(OtherPlayer, GearSlot.Weapon, IronSword, 0));

            Assert.AreEqual(0, _appearanceEvents.Count);
        }

        [Test]
        public void test_Unequip_DefinitionMissing_ClearsSlot_RecomputesAndRaises()
        {
            EquipForSetup(GearSlot.Weapon, DarkSteelFireSword);
            _db.Remove(DarkSteelFireSword);
            LogAssert.Expect(LogType.Error, new Regex("Unequip GetItem null"));

            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Weapon));

            Assert.AreEqual((byte)0x00, Flags());
            AssertOneEvent(0x00);
        }

        [Test]
        public void test_Swap_ReclaimAlsoFails_WeaponSlotEmptied_RecomputesAndRaisesOnce()
        {
            EquipForSetup(GearSlot.Weapon, IronSword);
            int bagSlot = PutInBag(IronSwordB);
            _inventory.MoveItemOutOverride = MoveItemOutCode.SlotLocked; // fails the new item and the reclaim
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL: auto-swap rollback failed"));

            Assert.AreEqual(EquipResult.InventoryError, _equipment.Equip(Player, GearSlot.Weapon, IronSwordB, bagSlot));

            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon).State);
            Assert.AreEqual((byte)0x00, Flags(), "the weapon bits are gone");
            AssertOneEvent(0x00);
        }

        [Test]
        public void test_Swap_ReclaimAlsoFails_BronzeArmor_ByteUnchanged_RaisesNothing()
        {
            EquipForSetup(GearSlot.Helmet, BronzeHelmet);
            int bagSlot = PutInBag(BronzeHelmetB);
            _inventory.MoveItemOutOverride = MoveItemOutCode.SlotLocked;
            LogAssert.Expect(LogType.Error, new Regex("CRITICAL: auto-swap rollback failed"));

            Assert.AreEqual(EquipResult.InventoryError, _equipment.Equip(Player, GearSlot.Helmet, BronzeHelmetB, bagSlot));

            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, GearSlot.Helmet).State);
            Assert.AreEqual(0, _appearanceEvents.Count);
        }

        [Test]
        public void test_ThrowingSubscriber_DoesNotChangeResultOrSlot()
        {
            _equipment.OnAppearanceChanged -= OnAppearanceChanged;
            _equipment.OnAppearanceChanged += _ => throw new InvalidOperationException("appearance subscriber boom");
            LogAssert.Expect(LogType.Exception, new Regex("appearance subscriber boom"));

            EquipResult result = EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);

            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(DarkSteelFireSword, _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon).ItemId);
            Assert.AreEqual((byte)0xC8, Flags(), "the cached byte is written before the raise");
        }

        // ── CR-EQS-10 ──────────────────────────────────────────────────────────────────────

        [Test]
        public void test_EquipTwoModifierItem_ExactlyTwoStatEventsAndOneAppearanceEvent_AC_EQS_18()
        {
            int bagSlot = PutInBag(TwoModSword);
            ResetEvents();

            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, GearSlot.Weapon, TwoModSword, bagSlot));

            Assert.AreEqual(2, _statEvents, "one OnStatChanged per registered modifier");
            AssertOneEvent(0x40);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Service_DeclaresOnlyOnAppearanceChanged_AC_EQS_18()
        {
            EventInfo[] events = typeof(EquipmentService).GetEvents(BindingFlags.Public | BindingFlags.Instance);

            Assert.AreEqual(1, events.Length, "the Equipment System raises no other event (CR-EQS-10)");
            Assert.AreEqual(nameof(IEquipmentService.OnAppearanceChanged), events[0].Name);
        }
    }
}
