using System;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.InventorySystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.EquipmentSystem
{
    /// <summary>
    /// EditMode unit tests for Equipment System Story 001 — slot registry, core types, the
    /// <c>RegisterCharacter</c> / <c>UnregisterCharacter</c> lifecycle and the read surface
    /// (TR-equip-001, TR-equip-008), including the <see cref="IEquippedWeaponQuery"/> reads.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_SlotRegistry_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const int CALL_COUNT = 100;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);
        private static readonly CharacterID UnknownPlayer = new CharacterID(9999u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);
        private static readonly EntityID OtherEntity = new EntityID(5002u);
        private static readonly EntityID UnknownEntity = new EntityID(9998u);

        private static readonly Regex UnregisteredLog = new Regex(@"\[EquipmentService] \w+: CharacterID\(9999\) is not a registered character\.");

        private static readonly GearSlot[] AllSlots =
        {
            GearSlot.Weapon, GearSlot.Helmet, GearSlot.Chest, GearSlot.Legs, GearSlot.Boots, GearSlot.Ring, GearSlot.Necklace,
        };

        private EquipmentService _equipment;

        [SetUp]
        public void SetUp()
        {
            _equipment = CreateService();
        }

        private static EquipmentService CreateService()
        {
            return new EquipmentService(
                new InventoryService(new StubItemDatabase(), () => 0u, MAX_ENHANCEMENT_LEVEL),
                CharacterStatsFixture.Create(),
                new StubItemDatabase(),
                new EnhancementBonusProvider(EnhancementConfig.Default),
                EnhancementConfig.Default);
        }

        private static Regex OutOfRangeLog(int slot) =>
            new Regex($@"\[EquipmentService] \w+: gear slot {slot} is out of range \[0, 7\)\.");

        private static Regex UnregisteredLogFor(string method) =>
            new Regex($@"\[EquipmentService] {method}: CharacterID\(9999\) is not a registered character\.");

        private static void AssertEmptyReads(EquipmentService equipment, CharacterID charId, GearSlot slot)
        {
            EquipmentSlotState state = equipment.GetEquipmentSlotState(charId, slot);
            Assert.AreEqual(ItemID.Invalid, state.ItemId, $"{slot}: item");
            Assert.AreEqual(SlotState.Empty, state.State, $"{slot}: state");
            Assert.AreEqual(0, equipment.GetSlotEnhancementLevel(charId, slot), $"{slot}: level");
            Assert.IsFalse(equipment.IsSlotTransitioning(charId, slot), $"{slot}: transitioning");
        }

        // ── Core types ─────────────────────────────────────────────────────────────────────

        [Test]
        public void test_EquipmentSlotEntry_Empty_IsInvalidLevelZeroNotTransitioning()
        {
            EquipmentSlotEntry empty = EquipmentSlotEntry.Empty;

            Assert.AreEqual(ItemID.Invalid, empty.ItemId);
            Assert.AreEqual(0, empty.EnhancementLevel);
            Assert.IsFalse(empty.IsTransitioning);
            Assert.IsTrue(empty.IsEmpty);
            Assert.IsTrue(default(EquipmentSlotEntry).IsEmpty, "a zeroed array element must read as an empty slot");
        }

        [Test]
        public void test_EquipmentSlotEntry_ReadonlyStructFields_MatchGdd()
        {
            Type type = typeof(EquipmentSlotEntry);

            Assert.IsTrue(type.IsValueType, "struct");
            Assert.IsTrue(type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false), "readonly struct");
            Assert.AreEqual(typeof(ItemID), type.GetField(nameof(EquipmentSlotEntry.ItemId)).FieldType);
            Assert.AreEqual(typeof(bool), type.GetField(nameof(EquipmentSlotEntry.IsTransitioning)).FieldType);
            Assert.AreEqual(typeof(byte), type.GetField(nameof(EquipmentSlotEntry.EnhancementLevel)).FieldType);
        }

        [Test]
        public void test_EquipResult_Members_MatchGddMappingTable()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "Success", "NoChange", "StatRequirementNotMet", "InventoryFull", "SlotMismatch",
                    "ItemLocked", "ItemNotInInventory", "InventoryError", "CriticalFailure",
                },
                Enum.GetNames(typeof(EquipResult)));
        }

        [Test]
        public void test_MergeResult_Members_MatchGddUiRequirements()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "Success", "RejectedEquipped", "RejectedMaxLevel", "RejectedInventoryFull",
                    "RejectedMergeError", "CriticalRollbackFailed",
                },
                Enum.GetNames(typeof(MergeResult)));
        }

        [Test]
        public void test_SlotState_Members_AreEmptyOccupiedTransitioning()
        {
            CollectionAssert.AreEqual(new[] { "Empty", "Occupied", "Transitioning" }, Enum.GetNames(typeof(SlotState)));
        }

        [Test]
        public void test_GearSlot_IndicesZeroToSix_MatchSlotCount()
        {
            Assert.AreEqual(EquipmentService.GEAR_SLOT_COUNT, Enum.GetValues(typeof(GearSlot)).Length);
            for (int i = 0; i < AllSlots.Length; i++)
            {
                Assert.AreEqual(i, (int)AllSlots[i], AllSlots[i].ToString());
            }
        }

        [Test]
        public void test_Constructor_NullDependency_ThrowsArgumentNullException()
        {
            var inventory = new InventoryService(new StubItemDatabase(), () => 0u, MAX_ENHANCEMENT_LEVEL);
            var stats = CharacterStatsFixture.Create();
            var items = new StubItemDatabase();
            var bonuses = new EnhancementBonusProvider(EnhancementConfig.Default);
            var config = EnhancementConfig.Default;

            Assert.Throws<ArgumentNullException>(() => new EquipmentService(null, stats, items, bonuses, config));
            Assert.Throws<ArgumentNullException>(() => new EquipmentService(inventory, null, items, bonuses, config));
            Assert.Throws<ArgumentNullException>(() => new EquipmentService(inventory, stats, null, bonuses, config));
            Assert.Throws<ArgumentNullException>(() => new EquipmentService(inventory, stats, items, null, config));
            Assert.Throws<ArgumentNullException>(() => new EquipmentService(inventory, stats, items, bonuses, null));
        }

        // ── Empty registry and weapon reads ────────────────────────────────────────────────

        [Test]
        public void test_RegisterCharacter_AllSevenSlots_ReadEmpty()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            foreach (GearSlot slot in AllSlots)
            {
                AssertEmptyReads(_equipment, Player, slot);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_GetEquippedWeapon_NothingEquipped_ReturnsInvalidAndLevelZero_AC_EQS_12()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
            Assert.AreEqual(0, _equipment.GetEquippedWeaponEnhancementLevel(Player));
            Assert.AreEqual(_equipment.GetSlotEnhancementLevel(Player, GearSlot.Weapon), _equipment.GetEquippedWeaponEnhancementLevel(Player));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_EquippedWeaponQuery_RegisteredEntity_ReturnsInvalidAndLevelZero()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);
            IEquippedWeaponQuery query = _equipment;

            Assert.AreEqual(ItemID.Invalid, query.GetEquippedWeaponID(PlayerEntity));
            Assert.AreEqual(0, query.GetEquippedWeaponEnhancementLevel(PlayerEntity));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_EquippedWeaponQuery_UnknownEntity_ReturnsInvalidAndLevelZeroWithoutLog_EC_EQS_9()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);
            IEquippedWeaponQuery query = _equipment;

            Assert.AreEqual(ItemID.Invalid, query.GetEquippedWeaponID(UnknownEntity));
            Assert.AreEqual(0, query.GetEquippedWeaponEnhancementLevel(UnknownEntity));
            LogAssert.NoUnexpectedReceived();
        }

        // ── Out-of-range slot ──────────────────────────────────────────────────────────────

        [TestCase(7)]
        [TestCase(128)]
        [TestCase(255)]
        public void test_SlotReads_OutOfRangeSlot_ReturnEmptyAndLogOneErrorEach(int rawSlot)
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);
            var slot = (GearSlot)rawSlot;

            LogAssert.Expect(LogType.Error, OutOfRangeLog(rawSlot));
            EquipmentSlotState state = _equipment.GetEquipmentSlotState(Player, slot);
            LogAssert.Expect(LogType.Error, OutOfRangeLog(rawSlot));
            byte level = _equipment.GetSlotEnhancementLevel(Player, slot);
            LogAssert.Expect(LogType.Error, OutOfRangeLog(rawSlot));
            bool transitioning = _equipment.IsSlotTransitioning(Player, slot);

            Assert.AreEqual(ItemID.Invalid, state.ItemId);
            Assert.AreEqual(SlotState.Empty, state.State);
            Assert.AreEqual(0, level);
            Assert.IsFalse(transitioning);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_SlotReads_LastValidSlotNecklace_ReadEmptyWithoutLog()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            AssertEmptyReads(_equipment, Player, (GearSlot)6);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_SlotRead_OutOfRangeSlotAndUnregisteredCharacter_LogsOnlyTheSlotError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService] GetEquipmentSlotState: gear slot 7 is out of range \[0, 7\)\."));

            EquipmentSlotState state = _equipment.GetEquipmentSlotState(UnknownPlayer, (GearSlot)7);

            Assert.AreEqual(SlotState.Empty, state.State);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Unregistered character ─────────────────────────────────────────────────────────

        [Test]
        public void test_AllReads_UnregisteredCharacter_ReturnEmptyAndLogOneErrorEach()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            LogAssert.Expect(LogType.Error, UnregisteredLogFor(nameof(IEquipmentService.GetEquipmentSlotState)));
            EquipmentSlotState state = _equipment.GetEquipmentSlotState(UnknownPlayer, GearSlot.Chest);
            LogAssert.Expect(LogType.Error, UnregisteredLogFor(nameof(IEquipmentService.GetSlotEnhancementLevel)));
            byte level = _equipment.GetSlotEnhancementLevel(UnknownPlayer, GearSlot.Chest);
            LogAssert.Expect(LogType.Error, UnregisteredLogFor(nameof(IEquipmentService.IsSlotTransitioning)));
            bool transitioning = _equipment.IsSlotTransitioning(UnknownPlayer, GearSlot.Chest);
            LogAssert.Expect(LogType.Error, UnregisteredLogFor(nameof(IEquipmentService.GetEquippedWeaponItemID)));
            ItemID weapon = _equipment.GetEquippedWeaponItemID(UnknownPlayer);
            LogAssert.Expect(LogType.Error, UnregisteredLogFor(nameof(IEquipmentService.GetEquippedWeaponEnhancementLevel)));
            byte weaponLevel = _equipment.GetEquippedWeaponEnhancementLevel(UnknownPlayer);

            Assert.AreEqual(ItemID.Invalid, state.ItemId);
            Assert.AreEqual(SlotState.Empty, state.State);
            Assert.AreEqual(0, level);
            Assert.IsFalse(transitioning);
            Assert.AreEqual(ItemID.Invalid, weapon);
            Assert.AreEqual(0, weaponLevel);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Invalid ids ────────────────────────────────────────────────────────────────────

        [Test]
        public void test_RegisterCharacter_InvalidCharacterId_ThrowsAndRegistersNothing()
        {
            Assert.Throws<ArgumentException>(() => _equipment.RegisterCharacter(CharacterID.Invalid, PlayerEntity));

            // PlayerEntity was not mapped by the rejected call.
            Assert.DoesNotThrow(() => _equipment.RegisterCharacter(Player, PlayerEntity));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RegisterCharacter_InvalidEntityId_ThrowsAndRegistersNothing()
        {
            Assert.Throws<ArgumentException>(() => _equipment.RegisterCharacter(Player, EntityID.Invalid));

            // Player was not registered by the rejected call.
            Assert.DoesNotThrow(() => _equipment.RegisterCharacter(Player, PlayerEntity));
            LogAssert.NoUnexpectedReceived();
        }

        // ── Double register ────────────────────────────────────────────────────────────────

        [Test]
        public void test_RegisterCharacter_SameCharacterTwice_ThrowsAndKeepsFirstRegistration()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            Assert.Throws<InvalidOperationException>(() => _equipment.RegisterCharacter(Player, OtherEntity));

            IEquippedWeaponQuery query = _equipment;
            AssertEmptyReads(_equipment, Player, GearSlot.Weapon);
            Assert.AreEqual(ItemID.Invalid, query.GetEquippedWeaponID(OtherEntity), "the rejected entity must not be mapped");

            // The first mapping survives: unregistering it frees PlayerEntity for a new character.
            _equipment.UnregisterCharacter(Player);
            Assert.DoesNotThrow(() => _equipment.RegisterCharacter(OtherPlayer, PlayerEntity));
            Assert.DoesNotThrow(() => _equipment.RegisterCharacter(Player, OtherEntity), "OtherEntity was never mapped");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_RegisterCharacter_EntityAlreadyMapped_ThrowsAndDoesNotRegisterSecondCharacter()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            Assert.Throws<InvalidOperationException>(() => _equipment.RegisterCharacter(OtherPlayer, PlayerEntity));

            LogAssert.Expect(LogType.Error, new Regex(@"\[EquipmentService] GetEquipmentSlotState: CharacterID\(1002\) is not a registered character\."));
            _equipment.GetEquipmentSlotState(OtherPlayer, GearSlot.Weapon);
            AssertEmptyReads(_equipment, Player, GearSlot.Weapon);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Unregister ─────────────────────────────────────────────────────────────────────

        [Test]
        public void test_UnregisterCharacter_ReadsBehaveAsUnregisteredAndEntityMapIsGone()
        {
            var unknownLog = new Regex(@"\[EquipmentService] \w+: CharacterID\(1001\) is not a registered character\.");
            _equipment.RegisterCharacter(Player, PlayerEntity);

            _equipment.UnregisterCharacter(Player);

            LogAssert.Expect(LogType.Error, unknownLog);
            EquipmentSlotState state = _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon);
            LogAssert.Expect(LogType.Error, unknownLog);
            ItemID weapon = _equipment.GetEquippedWeaponItemID(Player);
            Assert.AreEqual(SlotState.Empty, state.State);
            Assert.AreEqual(ItemID.Invalid, weapon);

            IEquippedWeaponQuery query = _equipment;
            Assert.AreEqual(ItemID.Invalid, query.GetEquippedWeaponID(PlayerEntity));
            Assert.AreEqual(0, query.GetEquippedWeaponEnhancementLevel(PlayerEntity));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_UnregisterCharacter_ThenRegisterAgain_Succeeds()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);
            _equipment.UnregisterCharacter(Player);

            Assert.DoesNotThrow(() => _equipment.RegisterCharacter(Player, PlayerEntity));
            AssertEmptyReads(_equipment, Player, GearSlot.Necklace);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_UnregisterCharacter_UnknownCharacter_IsSilentNoOp()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);

            Assert.DoesNotThrow(() => _equipment.UnregisterCharacter(UnknownPlayer));

            AssertEmptyReads(_equipment, Player, GearSlot.Weapon);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_UnregisterCharacter_LeavesOtherCharacterRegistered()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);
            _equipment.RegisterCharacter(OtherPlayer, OtherEntity);

            _equipment.UnregisterCharacter(Player);

            AssertEmptyReads(_equipment, OtherPlayer, GearSlot.Ring);
            Assert.Throws<InvalidOperationException>(() => _equipment.RegisterCharacter(Player, OtherEntity), "OtherEntity is still mapped");
            LogAssert.NoUnexpectedReceived();
        }

        // ── Allocation guardrail ───────────────────────────────────────────────────────────

        [Test]
        public void test_Reads_RegisteredCharacter_DoNotAllocate()
        {
            _equipment.RegisterCharacter(Player, PlayerEntity);
            IEquippedWeaponQuery query = _equipment;

            // Warm-up: first calls may JIT.
            ReadAll(query);

            // GC.GetAllocatedBytesForCurrentThread() always returns 0 in the Unity 6.3 Editor's Mono;
            // the project measure is Unity's GC.Alloc sample count (ZoneInstancing_TownRespawnPoint_tests.cs).
            Assert.That(() =>
            {
                for (int i = 0; i < CALL_COUNT; i++)
                {
                    ReadAll(query);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "a read allocated");
        }

        [Test]
        public void test_AllocationMeasure_UnregisteredRead_ReportsAllocation()
        {
            // Positive control: the error path builds a log string, so the GC.Alloc count must move.
            // If it did not, the zero-allocation assertion above would prove nothing.
            LogAssert.Expect(LogType.Error, UnregisteredLog);

            Assert.That(
                () => { _equipment.GetEquipmentSlotState(UnknownPlayer, GearSlot.Weapon); },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "the GC.Alloc measure did not register an allocation");
            LogAssert.NoUnexpectedReceived();
        }

        private void ReadAll(IEquippedWeaponQuery query)
        {
            _equipment.GetEquipmentSlotState(Player, GearSlot.Chest);
            _equipment.GetSlotEnhancementLevel(Player, GearSlot.Chest);
            _equipment.IsSlotTransitioning(Player, GearSlot.Chest);
            _equipment.GetEquippedWeaponItemID(Player);
            _equipment.GetEquippedWeaponEnhancementLevel(Player);
            query.GetEquippedWeaponID(PlayerEntity);
            query.GetEquippedWeaponEnhancementLevel(PlayerEntity);
            query.GetEquippedWeaponID(UnknownEntity);
        }
    }
}
