using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Stats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.EquipmentSystem
{
    /// <summary>
    /// EditMode integration tests for Equipment Story 009 — <see cref="AppearanceChangedSender"/> (CR-EQS-11; networking-wire-protocol.md
    /// <c>AppearanceChangedEvent</c>; ADR-015 Decision 1; ADR-010). A real <c>EquipmentService</c> drives the sender; a fake
    /// <see cref="IZoneClientBroadcastSink"/> records what would be enqueued.
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_AppearanceChangedSender_Tests
    {
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const int BASE_STR = 20;
        private const float REQUIREMENT_ABOVE_BASE = 50f;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly EntityID PlayerEntity = new EntityID(5001u);
        private static readonly CharacterID Rival = new CharacterID(1002u);
        private static readonly EntityID RivalEntity = new EntityID(5002u);

        private static readonly ItemID DarkSteelFireSword = new ItemID(101u);
        private static readonly ItemID IronSword = new ItemID(102u);
        private static readonly ItemID GatedSword = new ItemID(105u);
        private static readonly ItemID BronzeHelmet = new ItemID(212u);
        private static readonly ItemID SteelBoots = new ItemID(221u);

        private sealed class SinkCall
        {
            public ushort TypeId;
            public byte[] Body;
            public bool IsCapExempt;
        }

        private sealed class RecordingSink : IZoneClientBroadcastSink
        {
            public readonly List<SinkCall> Calls = new List<SinkCall>();
            public bool Throw;

            public void EnqueueToAllClients(ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                Calls.Add(new SinkCall { TypeId = messageTypeId, Body = body.ToArray(), IsCapExempt = isCapExempt });
                if (Throw)
                {
                    throw new InvalidOperationException("sink boom");
                }
            }
        }

        private FakeItemDatabase _db;
        private InventoryService _inventory;
        private Stats _stats;
        private EquipmentService _equipment;
        private RecordingSink _sink;
        private AppearanceChangedSender _sender;
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _db = new FakeItemDatabase();
            _inventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            _equipment = new EquipmentService(
                _inventory, _stats, _db, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default);
            _equipment.RegisterCharacter(Player, PlayerEntity);
            _sink = new RecordingSink();
            _sender = new AppearanceChangedSender(_equipment, _sink);

            AddItem(DarkSteelFireSword, GearSlot.Weapon, GearTier.DarkSteel, ElementType.Fire);
            AddItem(IronSword, GearSlot.Weapon, GearTier.Iron);
            AddItem(GatedSword, GearSlot.Weapon, GearTier.Steel, requirementStat: StatID.Strength, requirementMin: REQUIREMENT_ABOVE_BASE);
            AddItem(BronzeHelmet, GearSlot.Helmet, GearTier.Bronze);
            AddItem(SteelBoots, GearSlot.Boots, GearTier.Steel);
        }

        [TearDown]
        public void TearDown()
        {
            _sender.Dispose();
            foreach (ItemDefinition definition in _created)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }

            _created.Clear();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────

        private void AddItem(
            ItemID id, GearSlot slot, GearTier tier, ElementType element = ElementType.None,
            StatID? requirementStat = null, float requirementMin = 0f)
        {
            EquipmentData data = EquipmentData.CreateForTesting(
                slot, tier, new StatModifierEntry[0], elementType: element, equipRequirementStat: requirementStat, equipRequirementMin: requirementMin);
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, $"Item{id.RawValue}", ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
        }

        private int PutInBag(ItemID id, byte level = 0) => PutInBag(Player, id, level);

        private int PutInBag(CharacterID charId, ItemID id, byte level = 0)
        {
            MoveItemInResult placed = _inventory.MoveItemIn(charId, id, level);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            return placed.SlotIndex;
        }

        private void RegisterRival()
        {
            _inventory.RegisterCharacter(Rival);
            _stats.SetBaseStat(RivalEntity, StatID.Strength, BASE_STR);
            _equipment.RegisterCharacter(Rival, RivalEntity);
        }

        private static void AssertSend(SinkCall call, EntityID expectedEntity, byte expectedFlags)
        {
            Assert.AreEqual(AppearanceChangedEvent.MessageTypeId, call.TypeId);
            Assert.IsFalse(call.IsCapExempt, "isCapExempt");
            Assert.IsTrue(AppearanceChangedEventCodec.TryReadBody(call.Body, out uint entityId, out byte flags));
            Assert.AreEqual(expectedEntity.RawValue, entityId);
            Assert.AreEqual(expectedFlags, flags);
        }

        private EquipResult EquipFromBag(GearSlot slot, ItemID id, byte level = 0) => _equipment.Equip(Player, slot, id, PutInBag(id, level));

        private void EquipForSetup(GearSlot slot, ItemID id, byte level = 0)
        {
            Assert.AreEqual(EquipResult.Success, EquipFromBag(slot, id, level), "test setup: equip");
            _sink.Calls.Clear();
        }

        private void AssertOneSend(byte expectedFlags)
        {
            Assert.AreEqual(1, _sink.Calls.Count, "exactly one EnqueueToAllClients");
            Assert.AreEqual(0xE142, _sink.Calls[0].TypeId);
            AssertSend(_sink.Calls[0], PlayerEntity, expectedFlags);
        }

        // ── Sends ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_Sender_EquipDarkSteelFireSword_SendsOneEventWithPlayerAndFlags()
        {
            // Act
            EquipResult result = EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            AssertOneSend(0xC8);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_SwapIronSwordPlusZeroToPlusSeven_SendsFlags0x44()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSword, 0);

            // Act
            EquipResult result = EquipFromBag(GearSlot.Weapon, IronSword, 7);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            AssertOneSend(0x44);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_Unequip_SendsFlagsZero()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSword, 7);

            // Act
            EquipResult result = _equipment.Unequip(Player, GearSlot.Weapon);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            AssertOneSend(0x00);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_EquipThenUnequip_SendsBothBytesInOrder()
        {
            // Act: two sends through the one reused body buffer.
            EquipResult equip = EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);
            EquipResult unequip = _equipment.Unequip(Player, GearSlot.Weapon);

            // Assert
            Assert.AreEqual(EquipResult.Success, equip);
            Assert.AreEqual(EquipResult.Success, unequip);
            Assert.AreEqual(2, _sink.Calls.Count);
            AssertSend(_sink.Calls[0], PlayerEntity, 0xC8);
            AssertSend(_sink.Calls[1], PlayerEntity, 0x00);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_TwoCharacters_EachSendCarriesOwnEntityId()
        {
            // Arrange
            RegisterRival();

            // Act
            EquipResult first = EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);
            EquipResult second = _equipment.Equip(Rival, GearSlot.Weapon, IronSword, PutInBag(Rival, IronSword, 7));

            // Assert
            Assert.AreEqual(EquipResult.Success, first);
            Assert.AreEqual(EquipResult.Success, second);
            Assert.AreEqual(2, _sink.Calls.Count);
            AssertSend(_sink.Calls[0], PlayerEntity, 0xC8);
            AssertSend(_sink.Calls[1], RivalEntity, 0x44);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_EquipAfterRestore_SendsOnceWithNewByte()
        {
            // Arrange: the restore caches the Dark Steel byte as the baseline and sends nothing.
            var save = new SavedGearSlot[EquipmentService.GEAR_SLOT_COUNT];
            save[(int)GearSlot.Weapon] = new SavedGearSlot(DarkSteelFireSword, 8);
            _equipment.RestoreSlots(Player, save);

            // Act
            EquipResult result = EquipFromBag(GearSlot.Weapon, IronSword, 7);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            AssertOneSend(0x44);
            LogAssert.NoUnexpectedReceived();
        }

        // ── No send ────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_Sender_UnequipEmptySlot_SendsNothing()
        {
            // Act
            EquipResult result = _equipment.Unequip(Player, GearSlot.Weapon);

            // Assert
            Assert.AreEqual(EquipResult.NoChange, result);
            Assert.AreEqual(0, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_NoChange_SendsNothing()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSword);
            int again = PutInBag(IronSword);

            // Act
            EquipResult result = _equipment.Equip(Player, GearSlot.Weapon, IronSword, again);

            // Assert
            Assert.AreEqual(EquipResult.NoChange, result);
            Assert.AreEqual(0, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_StatRequirementNotMet_SendsNothing()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSword);

            // Act
            EquipResult result = EquipFromBag(GearSlot.Weapon, GatedSword);

            // Assert
            Assert.AreEqual(EquipResult.StatRequirementNotMet, result);
            Assert.AreEqual(0, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_BronzeHelmetLeavingByteUnchanged_SendsNothing()
        {
            // Arrange: Steel Boots already set the armor bit, so a Bronze Helmet changes nothing.
            EquipForSetup(GearSlot.Boots, SteelBoots);

            // Act
            EquipResult result = EquipFromBag(GearSlot.Helmet, BronzeHelmet);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual((byte)0x01, _equipment.GetAppearanceFlags(Player));
            Assert.AreEqual(0, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_RestoreSlots_SendsNothing()
        {
            // Arrange
            var save = new SavedGearSlot[EquipmentService.GEAR_SLOT_COUNT];
            save[(int)GearSlot.Weapon] = new SavedGearSlot(DarkSteelFireSword, 8);

            // Act
            _equipment.RestoreSlots(Player, save);

            // Assert
            Assert.AreNotEqual((byte)0x00, _equipment.GetAppearanceFlags(Player), "the byte is cached by the restore");
            Assert.AreEqual(0, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Dispose ────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_Sender_AfterDispose_ByteChangingEquipSendsNothing()
        {
            // Arrange
            _sender.Dispose();

            // Act
            EquipResult result = EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(0, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_DisposeTwice_IsSafe()
        {
            // Act / Assert
            Assert.DoesNotThrow(() =>
            {
                _sender.Dispose();
                _sender.Dispose();
            });
        }

        // ── Sink failure ───────────────────────────────────────────────────────────────────

        [Test]
        public void test_Sender_SinkThrows_EquipStillSucceedsAndOneErrorLogged()
        {
            // Arrange
            _sink.Throw = true;
            LogAssert.Expect(LogType.Exception, new Regex("sink boom"));

            // Act
            EquipResult result = EquipFromBag(GearSlot.Weapon, DarkSteelFireSword);

            // Assert
            Assert.AreEqual(EquipResult.Success, result);
            Assert.AreEqual(DarkSteelFireSword, _equipment.GetEquipmentSlotState(Player, GearSlot.Weapon).ItemId);
            Assert.AreEqual(1, _sink.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_Sender_AfterSinkThrew_NextChangeSendsNormally()
        {
            // Arrange
            _sink.Throw = true;
            LogAssert.Expect(LogType.Exception, new Regex("sink boom"));
            Assert.AreEqual(EquipResult.Success, EquipFromBag(GearSlot.Weapon, DarkSteelFireSword), "test setup: equip");
            _sink.Throw = false;
            _sink.Calls.Clear();

            // Act
            EquipResult result = _equipment.Unequip(Player, GearSlot.Weapon);

            // Assert: still subscribed, and the body is the new byte.
            Assert.AreEqual(EquipResult.Success, result);
            AssertOneSend(0x00);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Constructor ────────────────────────────────────────────────────────────────────

        [Test]
        public void test_Sender_NullService_ThrowsArgumentNullException()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => new AppearanceChangedSender(null, _sink));
        }

        [Test]
        public void test_Sender_NullSink_ThrowsArgumentNullException()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => new AppearanceChangedSender(_equipment, null));
        }
    }
}
