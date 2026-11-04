using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 018 (TD-090): the production zone composition builds Character Stats and Leveling
    /// before <see cref="CharacterPersistenceService"/>, gives it the six services, and a player admitted through the
    /// real <see cref="ZoneEntryAdmission"/> (handshake → <c>LoadCharacter</c> → <c>ApplyLoadedRecord</c>) is restored
    /// and kept. Built over <see cref="WiRig"/> with an in-memory record store and an accepting token validator; no I/O.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_ZoneApplier_Tests
    {
        private const uint CLIENT = 21u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const byte CLASS_WARRIOR = 0;
        private const int FREE_POINTS_PER_LEVEL = 2;
        private const uint SKILL_ID_BASE = 100u;
        private const int SKILL_COUNT = 3;

        // A record distinct from the Level 1 stub in every restored field.
        private const int LEVEL = 5;
        private const int EXPERIENCE = 1234;
        private const int HELD_FREE_POINTS = 3; // <= (LEVEL - 1) * FREE_POINTS_PER_LEVEL, so no clamp error
        private const int STR = 21;
        private const int DEX = 17;
        private const int VIT = 19;
        private const int INT = 13;
        private const int MAX_HP = 260;
        private const int MAX_MP = 90;
        private const int ATTACK_POWER = 44;
        private const int DEFENSE = 31;
        private const int MAGIC_DEFENSE = 27;
        private const float CRIT_CHANCE = 0.12f;
        private const float CRIT_MULTIPLIER = 1.75f;
        private const float ATTACK_RANGE = 2.5f;
        private const float ATTACK_SPEED = 1.1f;
        private const float MOVEMENT_SPEED = 5.5f;
        private const float CURRENT_HP = 120f;
        private const float CURRENT_MP = 33f;
        private const uint GOLD = 4321u;
        private const uint GOLD_VERSION = 7u;
        private const uint GEAR_ITEM = MvpItemFixture.BRONZE_SWORD; // a Weapon: restored into slot 0 (GearSlot.Weapon)
        private const byte GEAR_LEVEL = 2;
        private const uint BAG_ITEM = MvpItemFixture.HP_POTION_SMALL; // stackable
        private const int BAG_COUNT = 4;

        private const uint CLIENT_SECOND = 22u;
        private const byte CLASS_UNKNOWN = 99; // not in the registry StartRig builds
        private const uint GOLD_RELOADED = 999u;
        private const uint GOLD_VERSION_RELOADED = 8u;

        private SkillDatabase _database;
        private MvpItemFixture _items;
        private MemoryStore _store;

        private sealed class MemoryStore : ICharacterRecordStore
        {
            private readonly Dictionary<CharacterID, CharacterRecord> _records = new Dictionary<CharacterID, CharacterRecord>();

            public void Put(CharacterRecord record) { _records[record.CharacterID] = record; }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
            {
                _records.TryGetValue(id, out CharacterRecord record);
                return Task.FromResult(record);
            }

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => throw new NotSupportedException("load only");

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct) =>
                throw new NotSupportedException("load only");
        }

        private sealed class AcceptAllValidator : IZoneEntryTokenValidator
        {
            public bool IsValid(in ZoneEntryRequest request) => true;
        }

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return Vector3.zero; }
        }

        [TearDown]
        public void TearDown()
        {
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }

            _items?.Dispose();
            _items = null;
        }

        private static CharacterRecord MakeRecord()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(new AccountID(CLIENT + ACCOUNT_OFFSET), new CharacterID(CLIENT), CLASS_WARRIOR);
            r.CharacterName = "Brann";
            r.Level = LEVEL;
            r.Experience = EXPERIENCE;
            r.HeldFreePoints = HELD_FREE_POINTS;
            r.STR = STR;
            r.DEX = DEX;
            r.VIT = VIT;
            r.INT = INT;
            r.MaxHP = MAX_HP;
            r.MaxMP = MAX_MP;
            r.AttackPower = ATTACK_POWER;
            r.Defense = DEFENSE;
            r.MagicDefense = MAGIC_DEFENSE;
            r.CritChance = CRIT_CHANCE;
            r.CritMultiplier = CRIT_MULTIPLIER;
            r.AttackRange = ATTACK_RANGE;
            r.AttackSpeedMultiplier = ATTACK_SPEED;
            r.MovementSpeed = MOVEMENT_SPEED;
            r.CurrentHP = CURRENT_HP;
            r.CurrentMP = CURRENT_MP;
            r.GoldBalance = GOLD;
            r.GoldVersion = GOLD_VERSION;
            // Non-empty gear and bag, both in the MVP item database (Story 019): no ItemDatabaseMiss, no InventoryNotRestored.
            r.GearSlots[0] = new GearSlotRecord(new ItemID(GEAR_ITEM), GEAR_LEVEL);
            r.InventorySlots[0] = new ItemID(BAG_ITEM);
            r.InventoryItemCounts[0] = BAG_COUNT;
            return r;
        }

        // Starts the composition with the in-memory store holding `record` (null: none) and an accepting validator.
        private WiRig StartRig(CharacterRecord record)
        {
            var ids = new uint[SKILL_COUNT];
            var defs = new SkillDefinition[SKILL_COUNT];
            for (int i = 0; i < SKILL_COUNT; i++)
            {
                ids[i] = SKILL_ID_BASE + (uint)i;
                defs[i] = new SkillDefinition { ID = ids[i], UnlockLevel = 1 };
            }

            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, FREE_POINTS_PER_LEVEL, ids));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);

            var store = new MemoryStore();
            _store = store;
            if (record != null)
            {
                store.Put(record);
            }

            var rig = new WiRig();
            rig.Comp.ClassRegistryOverride = registry;
            rig.Comp.SkillDatabaseOverride = _database;
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.RecordStoreOverride = store;
            rig.Comp.EntryTokenValidatorOverride = new AcceptAllValidator();
            _items = new MvpItemFixture();
            rig.Comp.ItemDatabaseOverride = _items.Database;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        private static bool EnterThroughAdmission(WiRig rig, out EntityID entity)
        {
            return EnterThroughAdmission(rig, CLIENT, true, out entity);
        }

        // The handshake for the character CLIENT on connection `client`, then one tick: the completed load is drained and
        // admission runs. Returns true when exactly one player holds a slot and it belongs to `client`.
        // The NavMesh error: the EditMode test scene has no baked NavMesh, so Movement logs it once per MovementService,
        // on the first tick with a player. Only the first entry of a rig expects it; a scene with a baked NavMesh would
        // break this expectation.
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

        private static bool TryDecodeSnapshot(WiRig rig, out SkillCooldownSnapshot snapshot)
        {
            snapshot = default;
            foreach ((uint to, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (to != CLIENT || !MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope envelope)
                    || envelope.MessageTypeId != SkillCooldownSnapshot.MessageTypeId)
                {
                    continue;
                }

                return SkillCooldownSnapshotCodec.TryReadBody(data.AsSpan(ServerMessageEnvelope.WireSize), out snapshot);
            }

            return false;
        }

        // ---------------------------------------------------------------- AC-1: construction order

        [Test]
        public void test_zone_applier_s5_builds_stats_before_persistence_and_status_effects_after_zone_instancing()
        {
            WiRig rig = StartRig(null);

            int stats = rig.Observed.IndexOf("CharacterStats");
            int persistence = rig.Observed.IndexOf(nameof(CharacterPersistenceService));
            int wiring = rig.Observed.IndexOf(nameof(ZoneInstancingWiring));
            int statusEffects = rig.Observed.IndexOf("StatusEffectsService");
            Assert.That(stats, Is.GreaterThanOrEqualTo(0), "CharacterStats observed");
            Assert.Less(stats, persistence, "Character Stats before Persistence");
            Assert.Less(persistence, wiring, "Persistence before Zone Instancing");
            Assert.Less(wiring, statusEffects, "Zone Instancing before Status Effects");
        }

        [Test]
        public void test_zone_applier_persistence_holds_all_six_services()
        {
            WiRig rig = StartRig(null);
            CharacterPersistenceService service = rig.Comp.PersistenceService;

            Assert.AreSame(rig.Comp.ZoneCharacterStats, Field(service, "_stats"));
            Assert.AreSame(rig.Comp.ZoneLeveling, Field(service, "_leveling"));
            Assert.AreSame(rig.Comp.ZoneInventory, Field(service, "_inventory"));
            Assert.AreSame(rig.Comp.ZoneCurrency, Field(service, "_currency"));
            Assert.AreSame(rig.Comp.ZoneEquipment, Field(service, "_equipment"));
            Assert.IsInstanceOf<CurrencySystem>(Field(service, "_currency"), "the real Currency, not a stand-in");
        }

        // ---------------------------------------------------------------- AC-2: shared instances

        [Test]
        public void test_zone_applier_zone_instancing_and_status_effects_share_the_persistence_instances()
        {
            WiRig rig = StartRig(null);
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;

            Assert.AreSame(rig.Comp.PersistenceService, Field(wiring.Admission, "_applier"), "admission applies through Persistence");
            object sessionEnds = wiring.SessionEnds;
            Assert.AreSame(rig.Comp.ZoneInventory, Field(sessionEnds, "_inventory"), "session end unregisters the same inventory");
            Assert.AreSame(rig.Comp.ZoneEquipment, Field(sessionEnds, "_equipment"), "session end unregisters the same equipment");

            object persistenceStats = Field(rig.Comp.PersistenceService, "_stats");
            object layer = Field(rig.Comp.StatusEffects, "_modifierLayer");
            Assert.AreSame(persistenceStats, Field(layer, "_stats"), "Status Effects over the Character Stats Persistence restores into");
            Assert.AreSame(persistenceStats, Field(rig.Comp.ZoneLeveling, "_stats"), "Leveling attached to the same Character Stats");
            Assert.AreSame(Field(rig.Comp.PersistenceService, "_leveling"), rig.Comp.ZoneLeveling, "one Leveling service");
        }

        // ---------------------------------------------------------------- AC-3 / AC-4 / AC-5: admission restores

        [Test]
        public void test_zone_applier_admitted_player_is_kept_and_no_admission_error_is_logged()
        {
            WiRig rig = StartRig(MakeRecord());
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, out EntityID entity), "one admitted player holds a slot");
                Assert.IsTrue(rig.Comp.ZoneInstancing.Pool.TryGetPlayer(entity, out ZonePlayerSlot slot));
                Assert.AreEqual(CLIENT, slot.ClientId);

                foreach (string line in log.Lines)
                {
                    StringAssert.DoesNotContain("ApplyLoadedRecord", line, "no throw, no ItemDatabaseMiss, no other code");
                    StringAssert.DoesNotContain("LoadCharacter failed", line);
                    StringAssert.DoesNotContain("slot released", line);
                }
            }
        }

        [Test]
        public void test_zone_applier_admission_restores_class_level_xp_and_free_points()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, out EntityID e));
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            LevelingService leveling = rig.Comp.ZoneLeveling;

            Assert.IsTrue(leveling.TryGetClassType(e, out byte classType));
            Assert.AreEqual(CLASS_WARRIOR, classType);
            Assert.AreEqual(LEVEL, stats.GetBaseStat(e, StatID.Level));
            Assert.AreEqual(EXPERIENCE, stats.GetBaseStat(e, StatID.Experience));
            Assert.AreEqual(HELD_FREE_POINTS, leveling.GetHeldFreePoints(e));
        }

        [Test]
        public void test_zone_applier_admission_restores_the_base_stats_and_current_resources()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, out EntityID e));
            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;

            Assert.AreEqual(STR, stats.GetBaseStat(e, StatID.Strength));
            Assert.AreEqual(DEX, stats.GetBaseStat(e, StatID.Dexterity));
            Assert.AreEqual(VIT, stats.GetBaseStat(e, StatID.Vitality));
            Assert.AreEqual(INT, stats.GetBaseStat(e, StatID.Intelligence));
            Assert.AreEqual(MAX_HP, stats.GetBaseStat(e, StatID.MaxHP));
            Assert.AreEqual(MAX_MP, stats.GetBaseStat(e, StatID.MaxMP));
            Assert.AreEqual(ATTACK_POWER, stats.GetBaseStat(e, StatID.AttackPower));
            Assert.AreEqual(DEFENSE, stats.GetBaseStat(e, StatID.Defense));
            Assert.AreEqual(MAGIC_DEFENSE, stats.GetBaseStat(e, StatID.MagicDefense));
            Assert.AreEqual(CRIT_CHANCE, stats.GetBaseStatFloat(e, StatID.CritChance));
            Assert.AreEqual(CRIT_MULTIPLIER, stats.GetBaseStatFloat(e, StatID.CritMultiplier));
            Assert.AreEqual(ATTACK_RANGE, stats.GetBaseStatFloat(e, StatID.AttackRange));
            Assert.AreEqual(ATTACK_SPEED, stats.GetBaseStatFloat(e, StatID.AttackSpeedMultiplier));
            Assert.AreEqual(MOVEMENT_SPEED, stats.GetBaseStatFloat(e, StatID.MovementSpeed));
            Assert.AreEqual(CURRENT_HP, stats.GetCurrentHP(e));
            Assert.AreEqual(CURRENT_MP, stats.GetCurrentMP(e));
        }

        [Test]
        public void test_zone_applier_dead_on_load_record_is_admitted_at_full_hp()
        {
            CharacterRecord record = MakeRecord();
            record.CurrentHP = 0f;
            WiRig rig = StartRig(record);

            Assert.IsTrue(EnterThroughAdmission(rig, out EntityID e));

            IronGrind.CharacterStats.CharacterStats stats = rig.Comp.ZoneCharacterStats;
            Assert.AreEqual((float)stats.GetEffectiveStat(e, StatID.MaxHP), stats.GetCurrentHP(e));
        }

        [Test]
        public void test_zone_applier_admission_restores_gold_balance_and_version_into_the_zone_currency()
        {
            WiRig rig = StartRig(MakeRecord());
            Assert.IsTrue(EnterThroughAdmission(rig, out _));

            Assert.IsTrue(rig.Comp.ZoneCurrency.TryGetState(new CharacterID(CLIENT), out uint balance, out uint version));
            Assert.AreEqual(GOLD, balance);
            Assert.AreEqual(GOLD_VERSION, version);
        }

        [Test]
        public void test_zone_applier_readmitted_character_gets_the_new_record()
        {
            WiRig rig = StartRig(MakeRecord());
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, out EntityID first));
                Assert.IsTrue(rig.Comp.ZoneInstancing.Pool.ReleasePlayer(first));
                CharacterRecord reloaded = MakeRecord();
                reloaded.GoldBalance = GOLD_RELOADED;
                reloaded.GoldVersion = GOLD_VERSION_RELOADED;
                _store.Put(reloaded);

                Assert.IsTrue(EnterThroughAdmission(rig, CLIENT_SECOND, false, out EntityID second), "readmitted on a new connection");

                Assert.AreNotEqual(first, second, "a fresh entity per admission");
                Assert.IsTrue(rig.Comp.ZoneCurrency.TryGetState(new CharacterID(CLIENT), out uint balance, out uint version));
                Assert.AreEqual(GOLD_RELOADED, balance, "the reload overwrites the earlier balance");
                Assert.AreEqual(GOLD_VERSION_RELOADED, version);
            }
        }

        [Test]
        public void test_zone_applier_record_with_an_unknown_class_is_admitted_with_an_empty_skill_store()
        {
            CharacterRecord record = MakeRecord();
            record.ClassType = CLASS_UNKNOWN;
            record.HeldFreePoints = 0; // the unknown class has no free points per level: avoids the AC-LS-30 clamp error
            WiRig rig = StartRig(record);
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, out EntityID e), "admitted, slot kept");

                Assert.IsTrue(rig.Comp.ZoneLeveling.TryGetClassType(e, out byte classType));
                Assert.AreEqual(CLASS_UNKNOWN, classType);
                Assert.IsTrue(rig.Comp.SkillService.HasRecord(e), "a store is created, with no skills");
                Assert.IsTrue(log.Lines.Exists(l => l.Contains("class 99 has no skills")), "EC-SK-12 warning");
                Assert.IsTrue(TryDecodeSnapshot(rig, out SkillCooldownSnapshot snapshot), "snapshot was sent");
                for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
                {
                    Assert.AreEqual(default(SkillCooldownEntry), snapshot.GetEntry(i), "entry " + i);
                }
            }
        }

        // ---------------------------------------------------------------- AC-7: skill store spawned by real admission

        [Test]
        public void test_zone_applier_admitted_player_gets_a_skill_store_and_the_class_snapshot()
        {
            WiRig rig = StartRig(MakeRecord());
            using (var log = new WiLogCapture())
            {
                Assert.IsTrue(EnterThroughAdmission(rig, out EntityID e));

                Assert.IsTrue(rig.Comp.SkillService.HasRecord(e), "skill store created on entry");
                Assert.IsTrue(TryDecodeSnapshot(rig, out SkillCooldownSnapshot snapshot), "snapshot was sent");
                for (int i = 0; i < SKILL_COUNT; i++)
                {
                    Assert.AreEqual(new SkillCooldownEntry(SKILL_ID_BASE + (uint)i, 0u), snapshot.GetEntry(i), "entry " + i);
                }

                foreach (string line in log.Lines)
                {
                    StringAssert.DoesNotContain("no skill store", line);
                    StringAssert.DoesNotContain("no class type", line);
                }
            }
        }
    }
}
