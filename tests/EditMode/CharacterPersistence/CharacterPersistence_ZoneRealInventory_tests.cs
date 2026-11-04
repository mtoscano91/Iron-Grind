using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 019: the zone composition builds the real <see cref="InventoryService"/> and
    /// <see cref="EquipmentService"/> over the S2 item database, a player admitted through the real
    /// <see cref="ZoneEntryAdmission"/> gets the record's bag and gear back (gear modifiers in Character Stats), an unknown
    /// gear item is dropped with the one <c>ItemDatabaseMiss</c> warning, and the save path stays disabled (TD-091, Story
    /// 020). Built over <see cref="WiRig"/> with an in-memory record store, an accepting token validator and the MVP items.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_ZoneRealInventory_Tests
    {
        private const uint CLIENT = 31u;
        private const uint CLIENT_SECOND = 32u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const byte CLASS_WARRIOR = 0;
        private const int FREE_POINTS_PER_LEVEL = 2;
        private const int GEAR_SLOT_COUNT = 7;

        private const uint WEAPON = MvpItemFixture.BRONZE_SWORD;   // GearSlot.Weapon, no STR requirement
        private const uint HELMET = MvpItemFixture.BRONZE_HELMET;  // GearSlot.Helmet, no STR requirement
        private const byte WEAPON_LEVEL = 0;                       // level 0: the stat check sees the record's flat bonus only
        private const byte HELMET_LEVEL = 3;
        private const uint POTION = MvpItemFixture.HP_POTION_SMALL;
        private const int POTION_COUNT = 4;
        private const uint BAG_GEAR = MvpItemFixture.IRON_HELMET;
        private const byte BAG_GEAR_LEVEL = 2;
        private const uint UNKNOWN_ITEM = 999u;                    // not an MVP item id
        private const int RELOADED_POTION_COUNT = 9;

        private const string NO_DATABASE_WARNING = "[ZoneServer] no item database from S2; using an empty one (tests only)";

        private MvpItemFixture _items;
        private MemoryStore _store;

        private sealed class MemoryStore : ICharacterRecordStore
        {
            private readonly Dictionary<CharacterID, CharacterRecord> _records = new Dictionary<CharacterID, CharacterRecord>();

            public int Writes;

            public void Put(CharacterRecord record) { _records[record.CharacterID] = record; }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                _records.TryGetValue(id, out CharacterRecord record);
                return Task.FromResult(record);
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
            {
                Interlocked.Increment(ref Writes);
                return Task.FromResult(true);
            }

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                Interlocked.Increment(ref Writes);
                return Task.FromResult(1);
            }
        }

        private sealed class AcceptAllValidator : IZoneEntryTokenValidator
        {
            public bool IsValid(in ZoneEntryRequest request) => true;
        }

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return Vector3.zero; }
        }

        // An S2 loader that provides an item database, as ZoneClassSkillDataLoader does in production.
        private sealed class ItemDataLoader : IZoneStaticDataLoader, IZoneItemData
        {
            public ItemDataLoader(IItemDatabase database) { LoadedItemDatabase = database; }

            public IItemDatabase LoadedItemDatabase { get; }

            public bool LoadAndValidate() => true;
        }

        [SetUp]
        public void SetUp()
        {
            _items = new MvpItemFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        private static CharacterRecord MakeRecord()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(new AccountID(CLIENT + ACCOUNT_OFFSET), new CharacterID(CLIENT), CLASS_WARRIOR);
            r.GearSlots[(int)GearSlot.Weapon] = new GearSlotRecord(new ItemID(WEAPON), WEAPON_LEVEL);
            r.GearSlots[(int)GearSlot.Helmet] = new GearSlotRecord(new ItemID(HELMET), HELMET_LEVEL);
            r.InventorySlots[0] = new ItemID(POTION);
            r.InventoryItemCounts[0] = POTION_COUNT;
            r.InventorySlots[5] = new ItemID(BAG_GEAR);
            r.InventoryItemCounts[5] = 1;
            r.InventoryEnhancementLevels[5] = BAG_GEAR_LEVEL;
            return r;
        }

        private static SavedGearSlot[] ExpectedGear(CharacterRecord record)
        {
            var gear = new SavedGearSlot[GEAR_SLOT_COUNT];
            for (int i = 0; i < GEAR_SLOT_COUNT; i++)
            {
                gear[i] = new SavedGearSlot(record.GearSlots[i].ItemId, record.GearSlots[i].EnhancementLevel);
            }

            return gear;
        }

        // The record's bag as (slot, item, quantity, level), empty slots skipped, in slot order.
        private static List<(byte, uint, int, byte)> ExpectedBag(CharacterRecord record)
        {
            var bag = new List<(byte, uint, int, byte)>();
            for (int i = 0; i < record.InventorySlots.Length; i++)
            {
                if (record.InventorySlots[i] != ItemID.Invalid)
                {
                    bag.Add(((byte)i, record.InventorySlots[i].RawValue, record.InventoryItemCounts[i], record.InventoryEnhancementLevels[i]));
                }
            }

            return bag;
        }

        private static List<(byte, uint, int, byte)> Bag(InventorySnapshot snapshot)
        {
            var bag = new List<(byte, uint, int, byte)>();
            foreach (InventorySnapshotEntry e in snapshot.Slots)
            {
                bag.Add((e.SlotIndex, e.ItemId, e.Quantity, e.EnhancementLevel));
            }

            bag.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return bag;
        }

        private static ClassRegistry Registry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, FREE_POINTS_PER_LEVEL, Array.Empty<uint>()));
            return registry;
        }

        // Starts the composition with the in-memory store holding `record` (null: none). `useOverride` sets the item
        // database through ItemDatabaseOverride; `loader` replaces the S2 loader.
        private WiRig StartRig(CharacterRecord record, bool useOverride = true, IZoneStaticDataLoader loader = null)
        {
            _store = new MemoryStore();
            if (record != null)
            {
                _store.Put(record);
            }

            var rig = new WiRig(staticDataLoader: loader);
            rig.Comp.ClassRegistryOverride = Registry();
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.RecordStoreOverride = _store;
            rig.Comp.EntryTokenValidatorOverride = new AcceptAllValidator();
            if (useOverride)
            {
                rig.Comp.ItemDatabaseOverride = _items.Database;
            }

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        // The handshake for the character CLIENT on connection `client`, then one tick: the completed load is drained and
        // admission runs. The NavMesh error: the EditMode scene has no baked NavMesh, so Movement logs it once per
        // MovementService, on the first tick with a player (first entry of a rig only).
        private static bool EnterThroughAdmission(WiRig rig, uint client, bool firstEntry, out EntityID entity)
        {
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(client));
            rig.Net.ClientListener.AddClient(client);
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            rig.Comp.ZoneInstancing.Admission.BeginEntry(new ZoneEntryRequest(client, new AccountID(CLIENT + ACCOUNT_OFFSET),
                new CharacterID(CLIENT), pool.Zone, new byte[SessionTokenStore.TOKEN_LENGTH_BYTES]));
            if (firstEntry)
            {
                LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
            }

            rig.Net.FireTick();

            IReadOnlyList<EntityID> players = pool.GetPlayerEntityIDsInZone(pool.Zone);
            entity = players.Count == 1 ? players[0] : default;
            return players.Count == 1 && pool.TryGetPlayer(entity, out ZonePlayerSlot slot) && slot.ClientId == client;
        }

        private static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, target.GetType().Name + "." + name);
            return field.GetValue(target);
        }

        // ---------------------------------------------------------------- AC-2: the item database at S5

        [Test]
        public void test_zone_real_inventory_override_is_the_database_in_use()
        {
            WiRig rig = StartRig(null);

            Assert.AreSame(_items.Database, rig.Comp.ItemDatabaseInUse);
        }

        [Test]
        public void test_zone_real_inventory_s2_loader_database_is_used_without_an_override()
        {
            using (var log = new WiLogCapture())
            {
                WiRig rig = StartRig(null, useOverride: false, loader: new ItemDataLoader(_items.Database));

                Assert.AreSame(_items.Database, rig.Comp.ItemDatabaseInUse);
                Assert.IsFalse(log.Lines.Contains(NO_DATABASE_WARNING), "no fallback warning");
            }
        }

        [Test]
        public void test_zone_real_inventory_no_database_falls_back_to_an_empty_ready_one_with_one_warning()
        {
            using (var log = new WiLogCapture())
            {
                WiRig rig = StartRig(null, useOverride: false);

                Assert.IsNotNull(rig.Comp.ItemDatabaseInUse);
                Assert.IsTrue(rig.Comp.ItemDatabaseInUse.IsReady);
                Assert.IsFalse(rig.Comp.ItemDatabaseInUse.TryGetItem(new ItemID(WEAPON), out ItemDefinition _), "empty");
                Assert.AreEqual(1, log.Lines.FindAll(l => l == NO_DATABASE_WARNING).Count);
            }
        }

        // ---------------------------------------------------------------- AC-3: the real services at S5

        [Test]
        public void test_zone_real_inventory_s5_builds_inventory_and_equipment_after_stats_and_before_persistence()
        {
            WiRig rig = StartRig(null);

            int stats = rig.Observed.IndexOf("CharacterStats");
            int inventory = rig.Observed.IndexOf(nameof(InventoryService));
            int equipment = rig.Observed.IndexOf(nameof(EquipmentService));
            int persistence = rig.Observed.IndexOf(nameof(CharacterPersistenceService));
            Assert.That(stats, Is.GreaterThanOrEqualTo(0));
            Assert.Less(stats, inventory, "Character Stats before Inventory");
            Assert.Less(inventory, equipment, "Inventory before Equipment");
            Assert.Less(equipment, persistence, "Equipment before Persistence");
        }

        [Test]
        public void test_zone_real_inventory_persistence_session_end_and_equipment_share_the_instances()
        {
            WiRig rig = StartRig(null);
            InventoryService inventory = rig.Comp.ZoneInventory;
            EquipmentService equipment = rig.Comp.ZoneEquipment;
            Assert.IsNotNull(inventory);
            Assert.IsNotNull(equipment);

            Assert.AreSame(inventory, Field(rig.Comp.PersistenceService, "_inventory"), "Persistence restores into the zone Inventory");
            Assert.AreSame(equipment, Field(rig.Comp.PersistenceService, "_equipment"), "Persistence restores into the zone Equipment");
            object sessionEnds = rig.Comp.ZoneInstancing.SessionEnds;
            Assert.AreSame(inventory, Field(sessionEnds, "_inventory"), "session end unregisters the same Inventory");
            Assert.AreSame(equipment, Field(sessionEnds, "_equipment"), "session end unregisters the same Equipment");
            Assert.AreSame(inventory, Field(equipment, "_inventory"), "Equipment over the zone Inventory");
            Assert.AreSame(rig.Comp.ZoneCharacterStats, Field(equipment, "_stats"), "Equipment over the zone Character Stats");
            Assert.AreSame(_items.Database, Field(equipment, "_itemDatabase"));
            Assert.AreSame(_items.Database, Field(inventory, "_itemDatabase"));
        }

        // ---------------------------------------------------------------- AC-4: bag and gear restored

        [Test]
        public void test_zone_real_inventory_admission_restores_the_bag_and_the_gear()
        {
            CharacterRecord record = MakeRecord();
            WiRig rig = StartRig(record);
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _), "admitted and kept");

                CollectionAssert.AreEqual(ExpectedBag(record), Bag(rig.Comp.ZoneInventory.ExportSnapshot(new CharacterID(CLIENT))));
                CollectionAssert.AreEqual(ExpectedGear(record), rig.Comp.ZoneEquipment.ExportSlots(new CharacterID(CLIENT)));
                foreach (string line in log.Lines)
                {
                    StringAssert.DoesNotContain("ApplyLoadedRecord", line, "no throw, no ItemDatabaseMiss, no other code");
                    StringAssert.DoesNotContain("slot released", line);
                }
            }
        }

        [Test]
        public void test_zone_real_inventory_restored_weapon_adds_its_attack_power_to_character_stats()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out EntityID e));
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;

            float bonus = _items.FlatBonus(WEAPON, StatID.AttackPower);
            Assert.Greater(bonus, 0f, "the Bronze Sword carries Attack Power");
            Assert.AreEqual(stats.GetBaseStat(e, StatID.AttackPower) + (int)bonus, stats.GetEffectiveStat(e, StatID.AttackPower));
        }

        // ---------------------------------------------------------------- AC-5: unknown gear item

        [Test]
        public void test_zone_real_inventory_unknown_gear_item_is_dropped_with_one_item_database_miss_warning()
        {
            CharacterRecord record = MakeRecord();
            record.GearSlots[(int)GearSlot.Helmet] = new GearSlotRecord(new ItemID(UNKNOWN_ITEM), 0);
            WiRig rig = StartRig(record);
            using (var log = new WiLogCapture())
            {
                LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: .* slot Helmet item .* is not equipment in the Item Database"));

                Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _), "admitted with the rest of the gear");

                Assert.AreEqual(1, log.Lines.FindAll(l => l.Contains("ApplyLoadedRecord returned ItemDatabaseMiss")).Count);
                SavedGearSlot[] gear = rig.Comp.ZoneEquipment.ExportSlots(new CharacterID(CLIENT));
                Assert.AreEqual(new SavedGearSlot(new ItemID(WEAPON), WEAPON_LEVEL), gear[(int)GearSlot.Weapon], "weapon restored");
                Assert.AreEqual(SavedGearSlot.Empty, gear[(int)GearSlot.Helmet], "unknown item dropped");
                CollectionAssert.AreEqual(ExpectedBag(record), Bag(rig.Comp.ZoneInventory.ExportSnapshot(new CharacterID(CLIENT))), "bag restored");
            }
        }

        // ---------------------------------------------------------------- AC-6: re-admission

        [Test]
        public void test_zone_real_inventory_readmission_restores_bag_and_gear_from_the_new_record_only()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out EntityID first));
            Assert.IsTrue(rig.Comp.ZoneInstancing.Pool.ReleasePlayer(first));

            CharacterRecord reloaded = MakeRecord();
            reloaded.InventoryItemCounts[0] = RELOADED_POTION_COUNT;
            reloaded.InventorySlots[5] = ItemID.Invalid; // the bag gear is gone
            reloaded.InventoryItemCounts[5] = 0;
            reloaded.InventoryEnhancementLevels[5] = 0;
            reloaded.GearSlots[(int)GearSlot.Helmet] = GearSlotRecord.Empty;
            _store.Put(reloaded);

            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT_SECOND, false, out EntityID second), "readmitted on a new connection");

            Assert.AreNotEqual(first, second);
            CollectionAssert.AreEqual(ExpectedBag(reloaded), Bag(rig.Comp.ZoneInventory.ExportSnapshot(new CharacterID(CLIENT))));
            CollectionAssert.AreEqual(ExpectedGear(reloaded), rig.Comp.ZoneEquipment.ExportSlots(new CharacterID(CLIENT)));
        }

        // ---------------------------------------------------------------- AC-7: save path still disabled

        [Test]
        public void test_zone_real_inventory_save_session_still_throws_before_any_write()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _));

            var ex = Assert.Throws<InvalidOperationException>(() =>
                rig.Comp.PersistenceService.SaveSession(new CharacterID(CLIENT), SessionEndReason.CleanLogout, CancellationToken.None));

            StringAssert.Contains("zone locator", ex.Message);
            Assert.AreEqual(0, _store.Writes, "nothing written");
        }

        // In the composed zone an admitted player stays "entering" (SessionReady is not wired yet), so close step 2 skips
        // it and starts no save; either way nothing reaches the store.
        [Test]
        public void test_zone_real_inventory_zone_close_writes_nothing()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out EntityID e));
            Assert.IsTrue(rig.Comp.ZoneInstancing.Reclamation.IsEntering(e), "the admitted player is still entering, so step 2 skips it");

            using (var log = new WiLogCapture())
            {
                rig.Comp.ZoneInstancing.RunCloseSteps();

                Assert.IsFalse(log.Lines.Exists(l => l.Contains("zone close step 2")), "no save was started");
            }

            Assert.AreEqual(0, _store.Writes, "nothing written");
        }

        // ---------------------------------------------------------------- unknown bag item (current behaviour)

        // InventoryService.ImportSnapshot skips a bag entry whose item is not in the database (one warning, slot left
        // empty) and the player is admitted. Pinned here because once saves are enabled (Story 020) the next save would
        // drop that item from the stored record for good.
        [Test]
        public void test_zone_real_inventory_unknown_bag_item_is_skipped_with_a_warning_and_the_player_admitted()
        {
            CharacterRecord record = MakeRecord();
            record.InventorySlots[2] = new ItemID(UNKNOWN_ITEM);
            record.InventoryItemCounts[2] = 1;
            WiRig rig = StartRig(record);
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _), "admitted");

                Assert.AreEqual(1, log.Lines.FindAll(l => l.Contains("ImportSnapshot: entry for slot 2")
                    && l.Contains("unknown item")).Count, "one warning for the unknown bag item");
                record.InventorySlots[2] = ItemID.Invalid; // expected: the slot is left empty, the rest restored
                record.InventoryItemCounts[2] = 0;
                CollectionAssert.AreEqual(ExpectedBag(record), Bag(rig.Comp.ZoneInventory.ExportSnapshot(new CharacterID(CLIENT))));
            }
        }
    }
}
