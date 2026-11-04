using System;
using System.Collections;
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
    /// Character Persistence Story 020: the zone's save path. S5 builds the zone locator and the write queue and uses the
    /// save constructor; a missing item database fails S5; zone close (after the entry ack) and clean logout write the
    /// player's bag, gear, gold and zone; logout then unregisters Inventory and Equipment; a saved record comes back on
    /// re-admission; unknown items are dropped from the record by the next save (GDD EC-CP-1). Built over
    /// <see cref="WiRig"/> with an in-memory record store that keeps what it is given, an accepting token validator and
    /// the MVP items.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_ZoneSavePath_Tests
    {
        private const uint CLIENT = 41u;
        private const uint CLIENT_SECOND = 42u;
        private const uint OTHER_CHARACTER = 43u;   // a second player, on its own connection
        private const uint OTHER_CLIENT = 44u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const byte CLASS_WARRIOR = 0;
        private const int FREE_POINTS_PER_LEVEL = 2;
        private const int GEAR_SLOT_COUNT = 7;
        private const uint TEST_ZONE = 9u;

        private const uint WEAPON = MvpItemFixture.BRONZE_SWORD;
        private const uint HELMET = MvpItemFixture.BRONZE_HELMET;
        private const byte WEAPON_LEVEL = 0;
        private const byte HELMET_LEVEL = 3;
        private const uint POTION = MvpItemFixture.HP_POTION_SMALL;
        private const int POTION_COUNT = 4;
        private const int POTIONS_PICKED_UP = 2;
        private const uint BAG_GEAR = MvpItemFixture.IRON_HELMET;
        private const byte BAG_GEAR_LEVEL = 2;
        private const uint UNKNOWN_ITEM = 999u;
        private const int UNKNOWN_BAG_SLOT = 2;
        private const uint GOLD = 1234u;
        private const uint LOADED_SAVE_VERSION = 7u;

        // Bounded wait for the save task (a completed store task normally finishes it inline; the bound is a safety net).
        private const int MAX_WAIT_STEPS = 200;
        private const int WAIT_STEP_MS = 5;

        private MvpItemFixture _items;
        private KeepingStore _store;

        /// <summary>Keeps every record it is given; an update succeeds only on the stored version (as the SQL predicate does).</summary>
        private sealed class KeepingStore : ICharacterRecordStore
        {
            private readonly Dictionary<CharacterID, CharacterRecord> _records = new Dictionary<CharacterID, CharacterRecord>();
            private readonly object _lock = new object();
            private int _updates;

            public int Updates => Volatile.Read(ref _updates);

            public void Put(CharacterRecord record)
            {
                lock (_lock)
                {
                    _records[record.CharacterID] = record.Clone();
                }
            }

            public CharacterRecord Get(CharacterID id)
            {
                lock (_lock)
                {
                    return _records.TryGetValue(id, out CharacterRecord r) ? r.Clone() : null;
                }
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                return Task.FromResult(Get(id));
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
            {
                return Task.FromResult(false);
            }

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                lock (_lock)
                {
                    if (!_records.TryGetValue(record.CharacterID, out CharacterRecord stored) || stored.SaveVersion != expectedSaveVersion)
                    {
                        return Task.FromResult(0);
                    }

                    CharacterRecord kept = record.Clone();
                    kept.SaveVersion = unchecked(expectedSaveVersion + 1u);
                    _records[record.CharacterID] = kept;
                }

                Interlocked.Increment(ref _updates);
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

        // ---------------------------------------------------------------- fixtures

        private static CharacterRecord MakeRecord(uint character = CLIENT)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(new AccountID(character + ACCOUNT_OFFSET), new CharacterID(character), CLASS_WARRIOR);
            r.GearSlots[(int)GearSlot.Weapon] = new GearSlotRecord(new ItemID(WEAPON), WEAPON_LEVEL);
            r.GearSlots[(int)GearSlot.Helmet] = new GearSlotRecord(new ItemID(HELMET), HELMET_LEVEL);
            r.InventorySlots[0] = new ItemID(POTION);
            r.InventoryItemCounts[0] = POTION_COUNT;
            r.InventorySlots[5] = new ItemID(BAG_GEAR);
            r.InventoryItemCounts[5] = 1;
            r.InventoryEnhancementLevels[5] = BAG_GEAR_LEVEL;
            r.GoldBalance = GOLD;
            r.SaveVersion = LOADED_SAVE_VERSION;
            return r;
        }

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

        private static List<(ItemID, byte)> Gear(CharacterRecord record)
        {
            var gear = new List<(ItemID, byte)>();
            for (int i = 0; i < GEAR_SLOT_COUNT; i++)
            {
                gear.Add((record.GearSlots[i].ItemId, record.GearSlots[i].EnhancementLevel));
            }

            return gear;
        }

        private static ClassRegistry Registry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, FREE_POINTS_PER_LEVEL, Array.Empty<uint>()));
            return registry;
        }

        private WiRig StartRig(CharacterRecord record)
        {
            _store = new KeepingStore();
            if (record != null)
            {
                _store.Put(record);
            }

            var rig = new WiRig();
            rig.Comp.ClassRegistryOverride = Registry();
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.RecordStoreOverride = _store;
            rig.Comp.EntryTokenValidatorOverride = new AcceptAllValidator();
            rig.Comp.ItemDatabaseOverride = _items.Database;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        private static bool EnterThroughAdmission(WiRig rig, uint client, bool firstEntry, out EntityID entity)
        {
            return EnterThroughAdmission(rig, client, CLIENT, firstEntry, out entity);
        }

        // The handshake for `character` on connection `client`, then one tick (load drained, admission runs).
        // The NavMesh error: the EditMode scene has no baked NavMesh (first entry of a rig only).
        private static bool EnterThroughAdmission(WiRig rig, uint client, uint character, bool firstEntry, out EntityID entity)
        {
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(client));
            rig.Net.ClientListener.AddClient(client);
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            rig.Comp.ZoneInstancing.Admission.BeginEntry(new ZoneEntryRequest(client, new AccountID(character + ACCOUNT_OFFSET),
                new CharacterID(character), pool.Zone, new byte[SessionTokenStore.TOKEN_LENGTH_BYTES]));
            if (firstEntry)
            {
                LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
            }

            rig.Net.FireTick();

            IReadOnlyList<EntityID> players = pool.GetPlayerEntityIDsInZone(pool.Zone);
            entity = default;
            foreach (EntityID p in players)
            {
                if (pool.TryGetPlayer(p, out ZonePlayerSlot slot) && slot.ClientId == client)
                {
                    entity = p;
                    return true;
                }
            }

            return false;
        }

        // The client's ZoneEntryAck for this zone, applied on the next tick: the player is no longer entering.
        private static void Ack(WiRig rig, uint client)
        {
            var body = new byte[ZoneEntryAck.BodySize];
            ZoneEntryAckCodec.WriteBody(body, new ZoneEntryAck(rig.Comp.ZoneInstancing.Pool.Zone, 0u));
            rig.Comp.ZoneInstancing.Reclamation.OnConnectionMessage(client, ZoneEntryAck.MessageTypeId, default, body);
            rig.Net.FireTick();
        }

        // The zone's session state machine is not fed by admission yet (session epic): the logout tests register the
        // account as Connected themselves.
        private static uint Connect(WiRig rig)
        {
            uint account = CLIENT + ACCOUNT_OFFSET;
            uint tick = rig.Comp.TickLoop.ServerTickNumber;
            rig.Comp.ZoneInstancing.Sessions.EnterConnecting(account, tick);
            rig.Comp.ZoneInstancing.Sessions.CompleteAuthSuccess(account, CLIENT, tick);
            return account;
        }

        private static void TickUntil(WiRig rig, Func<bool> done, string what)
        {
            for (int i = 0; i < MAX_WAIT_STEPS && !done(); i++)
            {
                rig.Net.FireTick();
                if (!done())
                {
                    Thread.Sleep(WAIT_STEP_MS);
                }
            }

            Assert.IsTrue(done(), what);
        }

        private CharacterSaveResult? Logout(WiRig rig)
        {
            uint account = Connect(rig);
            CharacterSaveResult? result = null;
            Assert.IsTrue(rig.Comp.ZoneInstancing.SessionEnds.BeginCleanLogout(account, r => result = r), "logout save started");
            TickUntil(rig, () => result.HasValue, "the logout callback ran");
            return result;
        }

        private static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, target.GetType().Name + "." + name);
            return field.GetValue(target);
        }

        private static bool Holds(object service, string dictionaryField, CharacterID id)
        {
            return ((IDictionary)Field(service, dictionaryField)).Contains(id);
        }

        // ---------------------------------------------------------------- AC-1: the locator

        [Test]
        public void test_zone_save_path_locator_returns_its_zone_for_any_character()
        {
            var locator = new ZoneServerCharacterZoneLocator(new ZoneID(TEST_ZONE));

            Assert.AreEqual(new ZoneID(TEST_ZONE), locator.GetCurrentZone(new CharacterID(CLIENT)));
            Assert.AreEqual(new ZoneID(TEST_ZONE), locator.GetCurrentZone(new CharacterID(CLIENT_SECOND)));
            Assert.AreEqual(new ZoneID(TEST_ZONE), locator.GetCurrentZone(default));
        }

        // ---------------------------------------------------------------- AC-2: the save constructor

        [Test]
        public void test_zone_save_path_s5_uses_the_save_constructor_with_the_zone_locator_queue_and_timeout()
        {
            WiRig rig = StartRig(null);
            CharacterPersistenceService persistence = rig.Comp.PersistenceService;

            var locator = Field(persistence, "_zones") as ZoneServerCharacterZoneLocator;
            Assert.IsNotNull(locator, "the zone server's locator");
            Assert.AreEqual(rig.Comp.ZoneInstancing.Pool.Zone, locator.Zone, "the zone's own id");
            Assert.IsInstanceOf<CharacterWriteQueue>(Field(persistence, "_queue"));
            Assert.IsNotNull(Field(persistence, "_utcNow"), "a clock is injected");
            Assert.AreEqual(TimeSpan.FromSeconds(CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS),
                Field(persistence, "_writeTimeout"));
        }

        [Test]
        public void test_zone_save_path_s5_builds_the_write_queue_and_locator_after_the_store_and_before_persistence()
        {
            WiRig rig = StartRig(null);

            int store = rig.Observed.IndexOf("NpgsqlCharacterRecordStore");
            int queue = rig.Observed.IndexOf(nameof(CharacterWriteQueue));
            int locator = rig.Observed.IndexOf(nameof(ZoneServerCharacterZoneLocator));
            int persistence = rig.Observed.IndexOf(nameof(CharacterPersistenceService));
            Assert.That(store, Is.GreaterThanOrEqualTo(0));
            Assert.Less(store, queue);
            Assert.Less(queue, locator);
            Assert.Less(locator, persistence);
        }

        // ---------------------------------------------------------------- AC-3: no item database fails S5

        [Test]
        public void test_zone_save_path_missing_item_database_fails_startup_at_s5()
        {
            var rig = new WiRig();
            rig.Comp.ItemDatabaseOverride = null; // the recording S2 loader gives none
            LogAssert.Expect(LogType.Error, ZoneServerComposition.NO_ITEM_DATABASE_ERROR);
            LogAssert.Expect(LogType.Error, "[ZoneServer] startup failed at S5: StepReturnedFalse");

            rig.RunAll();

            Assert.IsNull(rig.Comp.ItemDatabaseInUse);
            Assert.IsNull(rig.Comp.ZoneInventory, "no Inventory");
            Assert.IsNull(rig.Comp.ZoneEquipment, "no Equipment");
            Assert.IsNull(rig.Comp.PersistenceService, "no Persistence");
            CollectionAssert.DoesNotContain(rig.Observed, nameof(InventoryService));
            CollectionAssert.DoesNotContain(rig.Observed, nameof(CharacterPersistenceService));
            CollectionAssert.DoesNotContain(rig.Observed, "ZoneTickPipeline");
        }

        // ---------------------------------------------------------------- AC-4: zone close saves

        [Test]
        public void test_zone_save_path_zone_close_writes_the_acked_players_bag_gear_gold_and_zone()
        {
            CharacterRecord record = MakeRecord();
            WiRig rig = StartRig(record);
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out EntityID e));
            Ack(rig, CLIENT);
            Assert.IsFalse(rig.Comp.ZoneInstancing.Reclamation.IsEntering(e), "acked");

            using (var log = new WiLogCapture())
            {
                rig.Comp.ZoneInstancing.RunCloseSteps();
                TickUntil(rig, () => _store.Updates >= 1, "the zone-close save reached the store");

                // ZoneCloseSteps logs a save that could not start as "zone close step 2: SaveSession for ... threw ...".
                Assert.IsFalse(log.Lines.Exists(l => l.Contains("zone close step 2")), "no failed save start");
            }

            Assert.AreEqual(1, _store.Updates, "exactly one write");
            CharacterRecord written = _store.Get(new CharacterID(CLIENT));
            CollectionAssert.AreEqual(ExpectedBag(record), ExpectedBag(written), "bag: slot, item, quantity, level");
            CollectionAssert.AreEqual(Gear(record), Gear(written), "gear");
            Assert.AreEqual(GOLD, written.GoldBalance);
            Assert.AreEqual(rig.Comp.ZoneInstancing.Pool.Zone, written.LastZoneID, "this zone");
            Assert.AreEqual(LOADED_SAVE_VERSION + 1u, written.SaveVersion);
        }

        [Test]
        public void test_zone_save_path_zone_close_skips_a_still_entering_player()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out EntityID e));
            Assert.IsTrue(rig.Comp.ZoneInstancing.Reclamation.IsEntering(e));

            rig.Comp.ZoneInstancing.RunCloseSteps();
            rig.Net.FireTick();

            Assert.AreEqual(0, _store.Updates, "no write for an entering player");
        }

        [Test]
        public void test_zone_save_path_zone_close_writes_each_acked_player_once()
        {
            WiRig rig = StartRig(MakeRecord());
            _store.Put(MakeRecord(OTHER_CHARACTER));
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, CLIENT, true, out _));
            Assert.IsTrue(EnterThroughAdmission(rig, OTHER_CLIENT, OTHER_CHARACTER, false, out _));
            Ack(rig, CLIENT);
            Ack(rig, OTHER_CLIENT);

            rig.Comp.ZoneInstancing.RunCloseSteps();
            TickUntil(rig, () => _store.Updates >= 2, "both zone-close saves reached the store");

            Assert.AreEqual(2, _store.Updates, "one write per player");
            Assert.AreEqual(LOADED_SAVE_VERSION + 1u, _store.Get(new CharacterID(CLIENT)).SaveVersion);
            Assert.AreEqual(LOADED_SAVE_VERSION + 1u, _store.Get(new CharacterID(OTHER_CHARACTER)).SaveVersion);
        }

        [Test]
        public void test_zone_save_path_zone_close_after_a_clean_logout_writes_nothing_more()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _));
            Ack(rig, CLIENT);
            Assert.AreEqual(CharacterSaveResult.Success, Logout(rig));
            Assert.AreEqual(1, _store.Updates);

            rig.Comp.ZoneInstancing.RunCloseSteps();
            rig.Net.FireTick();

            Assert.AreEqual(1, _store.Updates, "the logged-out player is no longer in the zone; close saves nothing");
            Assert.AreEqual(LOADED_SAVE_VERSION + 1u, _store.Get(new CharacterID(CLIENT)).SaveVersion);
        }

        // ---------------------------------------------------------------- AC-5: clean logout

        [Test]
        public void test_zone_save_path_clean_logout_writes_once_reports_success_and_unregisters_both_services()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _));
            var id = new CharacterID(CLIENT);
            Assert.IsTrue(Holds(rig.Comp.ZoneInventory, "_inventories", id), "registered after admission");
            Assert.IsTrue(Holds(rig.Comp.ZoneEquipment, "_characters", id), "registered after admission");

            CharacterSaveResult? result = Logout(rig);

            Assert.AreEqual(CharacterSaveResult.Success, result);
            Assert.AreEqual(1, _store.Updates);
            Assert.AreEqual(LOADED_SAVE_VERSION + 1u, _store.Get(id).SaveVersion);
            Assert.IsFalse(Holds(rig.Comp.ZoneInventory, "_inventories", id), "Inventory unregistered");
            Assert.IsFalse(Holds(rig.Comp.ZoneEquipment, "_characters", id), "Equipment unregistered");
        }

        // ---------------------------------------------------------------- AC-6: round trip

        [Test]
        public void test_zone_save_path_saved_bag_and_gear_come_back_on_readmission()
        {
            CharacterRecord record = MakeRecord();
            WiRig rig = StartRig(record);
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out EntityID first));
            var id = new CharacterID(CLIENT);
            rig.Comp.ZoneInventory.Pickup(id, new ItemID(POTION), POTIONS_PICKED_UP);
            List<(byte, uint, int, byte)> changedBag = Bag(rig.Comp.ZoneInventory.ExportSnapshot(id));
            Assert.AreNotEqual(ExpectedBag(record), changedBag, "the bag changed in the zone");

            Assert.AreEqual(CharacterSaveResult.Success, Logout(rig));
            Assert.IsFalse(rig.Comp.ZoneInstancing.Pool.TryGetPlayer(first, out ZonePlayerSlot _), "logout released the zone slot");
            Assert.IsTrue(EnterThroughAdmission(rig, CLIENT_SECOND, false, out EntityID second), "readmitted from the saved record");

            Assert.AreNotEqual(first, second);
            CollectionAssert.AreEqual(changedBag, Bag(rig.Comp.ZoneInventory.ExportSnapshot(id)), "the saved bag");
            SavedGearSlot[] gear = rig.Comp.ZoneEquipment.ExportSlots(id);
            Assert.AreEqual(new SavedGearSlot(new ItemID(WEAPON), WEAPON_LEVEL), gear[(int)GearSlot.Weapon]);
            Assert.AreEqual(new SavedGearSlot(new ItemID(HELMET), HELMET_LEVEL), gear[(int)GearSlot.Helmet]);
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            float bonus = _items.FlatBonus(WEAPON, StatID.AttackPower);
            Assert.AreEqual(stats.GetBaseStat(second, StatID.AttackPower) + (int)bonus, stats.GetEffectiveStat(second, StatID.AttackPower),
                "the weapon's modifier is back in Character Stats");
        }

        // ---------------------------------------------------------------- AC-7: unknown items cleaned by the next save

        [Test]
        public void test_zone_save_path_unknown_bag_and_gear_items_are_dropped_from_the_record_by_the_next_save()
        {
            CharacterRecord record = MakeRecord();
            record.InventorySlots[UNKNOWN_BAG_SLOT] = new ItemID(UNKNOWN_ITEM);
            record.InventoryItemCounts[UNKNOWN_BAG_SLOT] = 1;
            record.GearSlots[(int)GearSlot.Helmet] = new GearSlotRecord(new ItemID(UNKNOWN_ITEM), 0);
            WiRig rig = StartRig(record);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: .* slot Helmet item .* is not equipment in the Item Database"));
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, CLIENT, true, out _), "admitted");

                Assert.AreEqual(1, log.Lines.FindAll(l => l.Contains("ImportSnapshot: entry for slot " + UNKNOWN_BAG_SLOT)
                    && l.Contains("unknown item")).Count, "one warning for the unknown bag item");
            }

            Assert.AreEqual(CharacterSaveResult.Success, Logout(rig));

            CharacterRecord written = _store.Get(new CharacterID(CLIENT));
            CharacterRecord expected = record.Clone();
            expected.InventorySlots[UNKNOWN_BAG_SLOT] = ItemID.Invalid;
            expected.InventoryItemCounts[UNKNOWN_BAG_SLOT] = 0;
            expected.GearSlots[(int)GearSlot.Helmet] = GearSlotRecord.Empty;
            CollectionAssert.AreEqual(ExpectedBag(expected), ExpectedBag(written), "unknown bag item gone, rest unchanged");
            CollectionAssert.AreEqual(Gear(expected), Gear(written), "unknown gear item gone, weapon kept");
            Assert.AreEqual(GOLD, written.GoldBalance);
        }
    }
}
