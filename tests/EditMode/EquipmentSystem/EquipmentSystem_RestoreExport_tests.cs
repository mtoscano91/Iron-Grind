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
    /// EditMode integration tests for Equipment System Story 007 — <c>RestoreSlots</c> /
    /// <c>ExportSlots</c>, the load and save seam (CR-EQS-15; AC-EQS-25; TR-equip-010). Real
    /// <c>CharacterStats</c>, <c>InventoryService</c> (behind <see cref="InventoryDecorator"/>) and
    /// <c>EnhancementBonusProvider</c>.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_RestoreExport_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const int BASE_STR = 20;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);

        private static readonly ItemID DarkSteelFireSword = new ItemID(301u);
        private static readonly ItemID BronzeHelmet = new ItemID(302u);
        private static readonly ItemID SteelChest = new ItemID(303u);
        private static readonly ItemID IronLegs = new ItemID(304u);
        private static readonly ItemID IronBoots = new ItemID(305u);
        private static readonly ItemID IronRing = new ItemID(306u);
        private static readonly ItemID IronNecklace = new ItemID(307u);
        private static readonly ItemID IronSword = new ItemID(308u);
        private static readonly ItemID UnknownItem = new ItemID(999u);

        private FakeItemDatabase _db;
        private InventoryService _realInventory;
        private InventoryDecorator _inventory;
        private Stats _stats;
        private EnhancementBonusProvider _bonuses;
        private EquipmentService _equipment;
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private readonly List<EquipmentAppearanceChangedEventArgs> _appearanceEvents = new List<EquipmentAppearanceChangedEventArgs>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _appearanceEvents.Clear();
            _db = new FakeItemDatabase();
            _realInventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory = new InventoryDecorator(_realInventory);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            _bonuses = new EnhancementBonusProvider(EnhancementConfig.Default);
            _equipment = new EquipmentService(_inventory, _stats, _db, _bonuses, EnhancementConfig.Default);
            _equipment.RegisterCharacter(Player, PlayerEntity);
            _equipment.OnAppearanceChanged += args => _appearanceEvents.Add(args);

            AddItem(DarkSteelFireSword, GearSlot.Weapon, GearTier.DarkSteel, 30f, ElementType.Fire);
            AddItem(BronzeHelmet, GearSlot.Helmet, GearTier.Bronze, 10f);
            AddItem(SteelChest, GearSlot.Chest, GearTier.Steel, 20f);
            AddItem(IronLegs, GearSlot.Legs, GearTier.Iron, 15f);
            AddItem(IronBoots, GearSlot.Boots, GearTier.Iron, 8f);
            AddItem(IronRing, GearSlot.Ring, GearTier.Iron, 5f);
            AddItem(IronNecklace, GearSlot.Necklace, GearTier.Iron, 6f);
            AddItem(IronSword, GearSlot.Weapon, GearTier.Iron, 12f);
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

        private void AddItem(ItemID id, GearSlot slot, GearTier tier, float strength, ElementType element = ElementType.None)
        {
            EquipmentData data = EquipmentData.CreateForTesting(
                slot, tier, new[] { StatModifierEntry.CreateForTesting(StatID.Strength, strength) }, elementType: element);
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, $"Item{id.RawValue}", ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
        }

        private static SavedGearSlot Saved(ItemID id, byte level) => new SavedGearSlot(id, level);

        // The AC-EQS-25 save: levels 0, 3, 5, 7, 0, 0, 0 (accessories at 0).
        private static SavedGearSlot[] FullSave()
        {
            return new[]
            {
                Saved(DarkSteelFireSword, 0), Saved(BronzeHelmet, 3), Saved(SteelChest, 5), Saved(IronLegs, 7),
                Saved(IronBoots, 0), Saved(IronRing, 0), Saved(IronNecklace, 0),
            };
        }

        private static SavedGearSlot[] EmptySave()
        {
            var save = new SavedGearSlot[EquipmentService.GEAR_SLOT_COUNT];
            for (int i = 0; i < save.Length; i++)
            {
                save[i] = SavedGearSlot.Empty;
            }

            return save;
        }

        private int Strength() => _stats.GetEffectiveStat(PlayerEntity, StatID.Strength);

        // Base + sum of the real provider's enhanced bonus for each saved slot.
        private int ExpectedStrength(SavedGearSlot[] save)
        {
            int total = BASE_STR;
            foreach (SavedGearSlot slot in save)
            {
                if (slot.ItemId == ItemID.Invalid)
                {
                    continue;
                }

                EquipmentData equipment = _db.GetItem(slot.ItemId).EquipmentData;
                foreach (StatModifierEntry modifier in equipment.StatModifiers)
                {
                    total += _bonuses.GetFlatBonus(slot.EnhancementLevel, (int)modifier.FlatBonus, equipment.GearTier);
                }
            }

            return total;
        }

        private void AssertAllSlotsEmpty()
        {
            for (int i = 0; i < EquipmentService.GEAR_SLOT_COUNT; i++)
            {
                Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, (GearSlot)i).State, $"slot {i}");
            }
        }

        private EquipResult EquipFromBag(GearSlot slot, ItemID id, byte level)
        {
            MoveItemInResult placed = _realInventory.MoveItemIn(Player, id, level);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            return _equipment.Equip(Player, slot, id, placed.SlotIndex);
        }

        private SavedGearSlot[] ReadSurface()
        {
            var surface = new SavedGearSlot[EquipmentService.GEAR_SLOT_COUNT];
            for (int i = 0; i < surface.Length; i++)
            {
                surface[i] = Saved(_equipment.GetEquipmentSlotState(Player, (GearSlot)i).ItemId, _equipment.GetSlotEnhancementLevel(Player, (GearSlot)i));
            }

            return surface;
        }

        // ── AC-EQS-25 ──────────────────────────────────────────────────────────────────────

        [Test]
        public void test_RestoreSlots_SevenSavedPairs_RestoresStateLevelsAndModifiers_AC_EQS_25()
        {
            SavedGearSlot[] save = FullSave();

            GearRestoreResult result = _equipment.RestoreSlots(Player, save);

            for (int i = 0; i < save.Length; i++)
            {
                var slot = (GearSlot)i;
                Assert.IsFalse(_equipment.IsSlotTransitioning(Player, slot), $"slot {i} transitioning");
                EquipmentSlotState state = _equipment.GetEquipmentSlotState(Player, slot);
                Assert.AreEqual(save[i].ItemId, state.ItemId, $"slot {i} item");
                Assert.AreEqual(SlotState.Occupied, state.State, $"slot {i} state");
                Assert.AreEqual(save[i].EnhancementLevel, _equipment.GetSlotEnhancementLevel(Player, slot), $"slot {i} level");
            }

            Assert.AreEqual(DarkSteelFireSword, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(ExpectedStrength(save), Strength(), "base + sum of enhanced bonuses");
            Assert.Greater(Strength(), BASE_STR);
            Assert.AreEqual(0, result.DroppedCount);
            Assert.AreEqual((byte)0, result.DroppedSlotMask);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RestoreSlots_DarkSteelFireSword8AndSteelChest_CachesAppearanceByteWithoutEvent()
        {
            SavedGearSlot[] save = EmptySave();
            save[(int)GearSlot.Weapon] = Saved(DarkSteelFireSword, 8);
            save[(int)GearSlot.Chest] = Saved(SteelChest, 0);

            _equipment.RestoreSlots(Player, save);

            Assert.AreEqual((byte)0b11_001_11_1, _equipment.GetAppearanceFlags(Player));
            Assert.AreEqual(0, _appearanceEvents.Count, "restore raises no OnAppearanceChanged");
            LogAssert.NoUnexpectedReceived();
        }

        // ── Item Database miss and slot mismatch ───────────────────────────────────────────

        [Test]
        public void test_RestoreSlots_UnknownItemInSlot2_LeavesItEmptyLogsAndReportsIt()
        {
            SavedGearSlot[] save = FullSave();
            save[2] = Saved(UnknownItem, 4);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: CharacterID\(1001\) slot " + ((GearSlot)2) + @" item ItemID\(999\)"));

            GearRestoreResult result = _equipment.RestoreSlots(Player, save);

            Assert.AreEqual(1, result.DroppedCount);
            Assert.AreEqual((byte)(1 << 2), result.DroppedSlotMask);
            Assert.IsTrue(result.IsDropped((GearSlot)2));
            Assert.IsFalse(result.IsDropped(GearSlot.Weapon));
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, (GearSlot)2).State);
            Assert.AreEqual(0, _equipment.GetSlotEnhancementLevel(Player, (GearSlot)2));
            for (int i = 0; i < save.Length; i++)
            {
                if (i == 2)
                {
                    continue;
                }

                Assert.AreEqual(save[i].ItemId, _equipment.GetEquipmentSlotState(Player, (GearSlot)i).ItemId, $"slot {i} restored");
                Assert.AreEqual(save[i].EnhancementLevel, _equipment.GetSlotEnhancementLevel(Player, (GearSlot)i), $"slot {i} level");
            }

            SavedGearSlot[] withoutDropped = (SavedGearSlot[])save.Clone();
            withoutDropped[2] = SavedGearSlot.Empty;
            Assert.AreEqual(ExpectedStrength(withoutDropped), Strength(), "the dropped slot registers nothing");
        }

        [Test]
        public void test_RestoreSlots_HelmetSavedUnderWeapon_IsTreatedAsAMiss()
        {
            SavedGearSlot[] save = FullSave();
            save[(int)GearSlot.Weapon] = Saved(BronzeHelmet, 0);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: CharacterID\(1001\) slot Weapon item ItemID\(302\)"));

            GearRestoreResult result = _equipment.RestoreSlots(Player, save);

            Assert.AreEqual(1, result.DroppedCount);
            Assert.AreEqual((byte)(1 << (int)GearSlot.Weapon), result.DroppedSlotMask);
            Assert.IsTrue(result.IsDropped(GearSlot.Weapon));
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon).State);
            Assert.AreEqual(0, _equipment.GetSlotEnhancementLevel(Player, GearSlot.Weapon));
            Assert.AreEqual(BronzeHelmet, _equipment.GetEquipmentSlotState(Player, GearSlot.Helmet).ItemId, "the genuine helmet slot still restores");

            SavedGearSlot[] withoutDropped = (SavedGearSlot[])save.Clone();
            withoutDropped[(int)GearSlot.Weapon] = SavedGearSlot.Empty;
            Assert.AreEqual(ExpectedStrength(withoutDropped), Strength(), "the mismatched helmet registers nothing");
        }

        [Test]
        public void test_RestoreSlots_MissAndMismatchInOneRestore_ReportsBothSlots()
        {
            SavedGearSlot[] save = FullSave();
            save[(int)GearSlot.Weapon] = Saved(BronzeHelmet, 0);
            save[2] = Saved(UnknownItem, 0);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: CharacterID\(1001\) slot Weapon item ItemID\(302\) belongs to Helmet"));
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: CharacterID\(1001\) slot " + ((GearSlot)2) + @" item ItemID\(999\) is not equipment"));

            GearRestoreResult result = _equipment.RestoreSlots(Player, save);

            Assert.AreEqual(2, result.DroppedCount);
            Assert.AreEqual((byte)0b101, result.DroppedSlotMask);
            Assert.IsTrue(result.IsDropped(GearSlot.Weapon));
            Assert.IsTrue(result.IsDropped((GearSlot)2));
            Assert.IsFalse(result.IsDropped(GearSlot.Helmet));

            SavedGearSlot[] withoutDropped = (SavedGearSlot[])save.Clone();
            withoutDropped[(int)GearSlot.Weapon] = SavedGearSlot.Empty;
            withoutDropped[2] = SavedGearSlot.Empty;
            Assert.AreEqual(ExpectedStrength(withoutDropped), Strength());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RestoreSlots_KnownNonEquipmentItem_IsTreatedAsAMiss()
        {
            var potionId = new ItemID(501u);
            ItemDefinition potion = ItemDefinitionBuilder.Build(potionId.RawValue, "Potion", ItemCategory.Consumable, stackLimit: 5);
            _created.Add(potion);
            _db.Add(potion);
            SavedGearSlot[] save = FullSave();
            save[(int)GearSlot.Boots] = Saved(potionId, 0);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: CharacterID\(1001\) slot Boots item ItemID\(501\) is not equipment"));

            GearRestoreResult result = _equipment.RestoreSlots(Player, save);

            Assert.AreEqual((byte)(1 << (int)GearSlot.Boots), result.DroppedSlotMask);
            Assert.AreEqual(SlotState.Empty, _equipment.GetEquipmentSlotState(Player, GearSlot.Boots).State);
            LogAssert.NoUnexpectedReceived();
        }

        // ── No cleanup ─────────────────────────────────────────────────────────────────────

        [Test]
        public void test_RestoreSlots_WeaponAlsoInBag_BothRemain_NoInventoryCall()
        {
            MoveItemInResult placed = _realInventory.MoveItemIn(Player, DarkSteelFireSword, 0);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            _inventory.TotalForwardedCalls = 0;

            _equipment.RestoreSlots(Player, FullSave());

            Assert.AreEqual(0, _inventory.TotalForwardedCalls, "restore makes no Inventory call");
            Assert.AreEqual(DarkSteelFireSword, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(DarkSteelFireSword, _realInventory.GetSlot(Player, placed.SlotIndex).ItemId, "the bag copy stays");
            Assert.AreEqual(1, _realInventory.FilledSlots(Player));
            LogAssert.NoUnexpectedReceived();
        }

        // ── Preconditions ──────────────────────────────────────────────────────────────────

        [Test]
        public void test_RestoreSlots_Twice_SecondThrowsAndModifiersRegisteredOnce()
        {
            _equipment.RestoreSlots(Player, FullSave());
            int afterFirst = Strength();

            Assert.Throws<InvalidOperationException>(() => _equipment.RestoreSlots(Player, FullSave()));

            Assert.AreEqual(afterFirst, Strength(), "the second restore registered nothing");
            Assert.AreEqual(ExpectedStrength(FullSave()), Strength());
            CollectionAssert.AreEqual(FullSave(), ReadSurface(), "the first restore's slots are untouched");
        }

        [Test]
        public void test_RestoreSlots_UnregisteredCharacter_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _equipment.RestoreSlots(OtherPlayer, FullSave()));
        }

        [Test]
        public void test_RestoreSlots_LengthSix_ThrowsArgumentException()
        {
            var six = new SavedGearSlot[6];

            Assert.Throws<ArgumentException>(() => _equipment.RestoreSlots(Player, six));

            Assert.AreEqual(BASE_STR, Strength());
            AssertAllSlotsEmpty();
        }

        [Test]
        public void test_RestoreSlots_LevelAboveMaximum_ThrowsOutOfRangeBeforeAnyRegistration()
        {
            SavedGearSlot[] save = FullSave();
            save[3] = Saved(IronLegs, MAX_ENHANCEMENT_LEVEL + 1);

            Assert.Throws<ArgumentOutOfRangeException>(() => _equipment.RestoreSlots(Player, save));

            Assert.AreEqual(BASE_STR, Strength(), "nothing registered, though slots 0-2 were valid");
            AssertAllSlotsEmpty();
        }

        [Test]
        public void test_RestoreSlots_EmptySlotWithLevel3_ThrowsArgumentExceptionBeforeAnyRegistration()
        {
            SavedGearSlot[] save = FullSave();
            save[4] = Saved(ItemID.Invalid, 3);

            Assert.Throws<ArgumentException>(() => _equipment.RestoreSlots(Player, save));

            Assert.AreEqual(BASE_STR, Strength(), "nothing registered");
            AssertAllSlotsEmpty();
        }

        [Test]
        public void test_ExportSlots_UnregisteredCharacter_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _equipment.ExportSlots(OtherPlayer));
        }

        // ── Export ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_ExportSlots_AfterEquipsAndAutoSwap_MatchesReadSurface()
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, DarkSteelFireSword, 2));
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Chest, SteelChest, 5));
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, IronSword, 4), "auto-swap");

            SavedGearSlot[] exported = _equipment.ExportSlots(Player);

            Assert.AreEqual(EquipmentService.GEAR_SLOT_COUNT, exported.Length);
            CollectionAssert.AreEqual(ReadSurface(), exported);
            Assert.AreEqual(Saved(IronSword, 4), exported[(int)GearSlot.Weapon]);
            Assert.AreEqual(Saved(SteelChest, 5), exported[(int)GearSlot.Chest]);
            Assert.AreEqual(SavedGearSlot.Empty, exported[(int)GearSlot.Helmet]);
            Assert.AreEqual(ItemID.Invalid, exported[(int)GearSlot.Ring].ItemId);
            Assert.AreEqual(0, exported[(int)GearSlot.Ring].EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ExportSlots_AfterEquipThenUnequip_ExportsInvalidAndLevelZero()
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Chest, SteelChest, 5));
            Assert.AreEqual(EquipResult.Success, _equipment.Unequip(Player, GearSlot.Chest));

            SavedGearSlot[] exported = _equipment.ExportSlots(Player);

            Assert.AreEqual(SavedGearSlot.Empty, exported[(int)GearSlot.Chest], "a cleared slot exports Invalid / 0");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ExportSlots_RightAfterRestore_EqualsTheSave()
        {
            SavedGearSlot[] save = FullSave();
            _equipment.RestoreSlots(Player, save);

            CollectionAssert.AreEqual(save, _equipment.ExportSlots(Player));
            LogAssert.NoUnexpectedReceived();
        }

        // ── Round trip ─────────────────────────────────────────────────────────────────────

        [Test]
        public void test_RoundTrip_ExportUnregisterRegisterRestore_ReproducesSurfaceStatsAndAppearance()
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, DarkSteelFireSword, 8));
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Chest, SteelChest, 5));
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Ring, IronRing, 3));
            SavedGearSlot[] originalSurface = ReadSurface();
            int originalStrength = Strength();
            byte originalFlags = _equipment.GetAppearanceFlags(Player);
            SavedGearSlot[] exported = _equipment.ExportSlots(Player);

            _equipment.UnregisterCharacter(Player);
            // UnregisterCharacter leaves modifiers in Character Stats (the entity is normally discarded
            // with the session). This test reuses the entity, so remove them to make the comparison meaningful.
            foreach (SavedGearSlot slot in exported)
            {
                if (slot.ItemId == ItemID.Invalid)
                {
                    continue;
                }

                foreach (StatModifierEntry modifier in _db.GetItem(slot.ItemId).EquipmentData.StatModifiers)
                {
                    _stats.RemoveEquipmentModifier(PlayerEntity, modifier.StatId, slot.ItemId);
                }
            }

            Assert.AreEqual(BASE_STR, Strength(), "test setup: stale modifiers cleared");
            _equipment.RegisterCharacter(Player, PlayerEntity);
            _equipment.RestoreSlots(Player, exported);

            CollectionAssert.AreEqual(originalSurface, ReadSurface());
            Assert.AreEqual(originalStrength, Strength());
            Assert.AreEqual(originalFlags, _equipment.GetAppearanceFlags(Player));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
