using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 007: <c>SaveSession</c> (CR-CP-10, CR-CP-6, CR-CP-11, EC-CP-4, AC-CP-15, AC-CP-18,
    /// AC-CP-20, AC-CP-30, AC-CP-31). The only wall-clock elements are the short injected write timeouts (100 ms) in
    /// the timeout tests; the latency measurement uses an injected clock. Every wait is bounded by 5 s.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_SaveSession_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_B = new CharacterID(202u);
        private static readonly EntityID ENTITY_B = new EntityID(1002u);
        private static readonly CharacterID CHAR_UNKNOWN = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;
        private const int WAIT_MS = 5000;

        /// <summary>Currency fake that knows no character: <c>TryGetState</c> is false.</summary>
        private sealed class UnregisteredCurrency : ICurrencyStateRestore
        {
            public void RestoreCharacter(CharacterID charId, uint balance, uint version)
            {
            }

            public bool TryGetState(CharacterID charId, out uint balance, out uint version)
            {
                balance = 0u;
                version = 0u;
                return false;
            }
        }

        /// <summary>One complete set of live systems plus the service over them.</summary>
        private sealed class Rig
        {
            public LevelingService Leveling;
            public IronGrind.CharacterStats.CharacterStats Stats;
            public CurrencySystem CurrencySystem;
            public RecordingInventory Inventory;
            public FakeZoneLocator Zones;
            public CharacterWriteQueue Queue;
            public CharacterPersistenceService Service;
            public EntityID Entity;
        }

        private DateTime _storeTime;
        private DateTime _serviceTime;
        private InMemoryCharacterRecordStore _store;
        private CountingStore _counting;
        private Rig _rig;

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _serviceTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);
            _counting = new CountingStore(_store);
            _rig = BuildRig(TimeSpan.FromSeconds(5), null);
        }

        private Rig BuildRig(TimeSpan writeTimeout, ICurrencyStateRestore currencyOverride)
        {
            var rig = new Rig();
            rig.Leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            rig.Stats = CharacterStatsFixture.CreateWithLeveling(rig.Leveling);
            rig.Leveling.AttachCharacterStats(rig.Stats);
            rig.Entity = CharacterStatsFixture.PlayerEntityId;
            rig.CurrencySystem = new CurrencySystem();
            rig.Inventory = new RecordingInventory { ExportResult = InventorySnapshot.Empty };
            rig.Zones = new FakeZoneLocator();
            rig.Queue = new CharacterWriteQueue();
            rig.Service = new CharacterPersistenceService(
                _counting,
                rig.Stats,
                rig.Leveling,
                rig.Inventory,
                currencyOverride ?? rig.CurrencySystem,
                rig.Zones,
                rig.Queue,
                () => _serviceTime,
                writeTimeout);
            return rig;
        }

        private static IClassRegistry CreateClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WARRIOR_CLASS_TYPE, new ClassDefinition(
                strengthAutoAlloc: 2,
                dexterityAutoAlloc: 1,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 0,
                freePointsPerLevel: 1));
            return registry;
        }

        private static int[] BuildXpThresholdsWithCapSentinel()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD;
            t[61] = int.MaxValue;
            return t;
        }

        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within 5 s.");
            return task.Result;
        }

        private static void WaitUntil(Func<bool> condition, string what)
        {
            Assert.IsTrue(SpinWait.SpinUntil(condition, WAIT_MS), "Timed out after 5 s waiting for: " + what);
        }

        private static CharacterRecord MakeSeed()
        {
            return MakeSeed(CHAR_A);
        }

        private static CharacterRecord MakeSeed(CharacterID id)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, id, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = 10;
            r.Experience = 0;
            r.HeldFreePoints = 4;
            r.STR = 12;
            r.DEX = 11;
            r.VIT = 13;
            r.INT = 9;
            r.MaxHP = 1000;
            r.MaxMP = 220;
            r.AttackPower = 30;
            r.Defense = 20;
            r.MagicDefense = 10;
            r.CurrentHP = 650f;
            r.CurrentMP = 100f;
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.LastZoneID = LOADED_ZONE;
            r.GearSlots[0] = new GearSlotRecord(new ItemID(700u), 3);
            r.GearSlots[4] = new GearSlotRecord(new ItemID(701u), 0);
            return r;
        }

        // Inserts the seed (store clock T1), loads it through LoadCharacter + ApplyLoadedRecord into the rig, then
        // moves the store clock to T2 so a later save is distinguishable. Returns the loaded record.
        private CharacterRecord SeedAndLoad(Rig rig, CharacterRecord seed)
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(seed, CancellationToken.None)));
            CharacterRecord loaded = LoadInto(rig);
            _storeTime = T2;
            return loaded;
        }

        private CharacterRecord LoadInto(Rig rig)
        {
            return LoadCharacterInto(rig, CHAR_A, rig.Entity);
        }

        private CharacterRecord LoadCharacterInto(Rig rig, CharacterID id, EntityID entity)
        {
            CharacterLoadResult result = Complete(rig.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Service.ApplyLoadedRecord(entity, result.Record));
            return result.Record;
        }

        // Inserts a second character (store clock T1) and loads it with its own entity; the store clock is left at T2.
        private CharacterRecord SeedAndLoadCharacterB(Rig rig)
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(CHAR_B), CancellationToken.None)));
            CharacterRecord loaded = LoadCharacterInto(rig, CHAR_B, ENTITY_B);
            _storeTime = T2;
            return loaded;
        }

        private CharacterRecord Stored()
        {
            return Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));
        }

        private static void AssertSlot(CharacterRecord r, int slot, uint item, int quantity, byte level)
        {
            Assert.AreEqual(new ItemID(item), r.InventorySlots[slot], "item in slot " + slot);
            Assert.AreEqual(quantity, r.InventoryItemCounts[slot], "count in slot " + slot);
            Assert.AreEqual(level, r.InventoryEnhancementLevels[slot], "level in slot " + slot);
        }

        // All 20 slots: 2 (item 5 x4 +0) and 11 (item 12 x1 +3) filled, the other 18 empty.
        private static void AssertSlots2And11FilledRestEmpty(CharacterRecord r)
        {
            int empty = 0;
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (i == 2)
                {
                    AssertSlot(r, i, 5u, 4, 0);
                }
                else if (i == 11)
                {
                    AssertSlot(r, i, 12u, 1, 3);
                }
                else
                {
                    AssertSlot(r, i, 0u, 0, 0);
                    empty++;
                }
            }

            Assert.AreEqual(InventoryConstants.INVENTORY_SLOT_COUNT - 2, empty);
        }

        // ---------------------------------------------------------------- session entry

        [Test]
        public void SessionEntry_AfterSuccessfulApply_SaveSessionFindsTheCharacter()
        {
            SeedAndLoad(_rig, MakeSeed());

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            Assert.AreEqual(1, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SessionEntry_AfterInventoryNotRestored_HoldsNoneAndSaveSessionReturnsCharacterNotFound()
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            _rig.Inventory.ImportResult = false;
            CharacterLoadResult loadResult = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, _rig.Service.ApplyLoadedRecord(_rig.Entity, loadResult.Record));

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, result);
            Assert.AreEqual(0, _counting.UpdateCount);
            Assert.AreEqual(0, _rig.Inventory.ExportCallCount);
        }

        [Test]
        public void SessionEntry_FailedReloadAfterASuccessfulLoad_RemovesTheEntryAndSaveSessionReturnsCharacterNotFound()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Inventory.ImportResult = false;
            CharacterLoadResult reload = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.InventoryNotRestored, _rig.Service.ApplyLoadedRecord(_rig.Entity, reload.Record));

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, result);
            Assert.AreEqual(0, _counting.UpdateCount, "No store call.");
            Assert.AreEqual(0, _rig.Queue.ActiveCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_UnknownCharacter_ReturnsCharacterNotFoundWithNoStoreCallAndNoQueueEntry()
        {
            SeedAndLoad(_rig, MakeSeed());

            CharacterSaveResult plain = Complete(_rig.Service.SaveSession(CHAR_UNKNOWN, SessionEndReason.ZoneClosed, CancellationToken.None));
            CharacterSaveResult withOverride = Complete(_rig.Service.SaveSession(CHAR_UNKNOWN, SessionEndReason.ZoneClosed, 0f, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, plain);
            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, withOverride);
            Assert.AreEqual(0, _counting.UpdateCount);
            Assert.AreEqual(0, _rig.Queue.ActiveCharacterCount);
        }

        // ---------------------------------------------------------------- snapshot (steps 2 to 6)

        [Test]
        public void SaveSession_ChangesAfterTheCallWhileStoreHeld_StoredRecordHasTheEarlierValues()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Inventory.ExportResult = new InventorySnapshot(new[] { new InventorySnapshotEntry(2, 5u, 4, 0) });
            _rig.Zones.Zone = new ZoneID(42u);
            Action release = _store.HoldNext();

            Task<CharacterSaveResult> task = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            Assert.IsFalse(task.IsCompleted, "The store is held, so the save is pending.");
            _rig.CurrencySystem.RestoreCharacter(CHAR_A, 1u, 99u);
            _rig.Stats.SetCurrentHP(_rig.Entity, 10f);
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Strength, 500);
            _rig.Inventory.ExportResult = InventorySnapshot.Empty;
            _rig.Zones.Zone = new ZoneID(55u);
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(task));
            CharacterRecord stored = Stored();
            Assert.AreEqual(1250u, stored.GoldBalance);
            Assert.AreEqual(9u, stored.GoldVersion);
            Assert.AreEqual(650f, stored.CurrentHP);
            Assert.AreEqual(12, stored.STR);
            AssertSlot(stored, 2, 5u, 4, 0);
            Assert.AreEqual(new ZoneID(42u), stored.LastZoneID, "The zone is read at call time, before the later change to 55.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_EquipmentModifiersRaiseEffectiveStats_StoredRecordHoldsTheBaseValues()
        {
            SeedAndLoad(_rig, MakeSeed());
            IronGrind.CharacterStats.CharacterStats s = _rig.Stats;
            EntityID e = _rig.Entity;
            s.AddEquipmentModifier(e, StatID.Strength, new EquipmentModifierEntry(100f, 0f, new ItemID(900u)));
            s.AddEquipmentModifier(e, StatID.MaxHP, new EquipmentModifierEntry(200f, 0f, new ItemID(900u)));
            Assert.Greater(s.GetEffectiveStat(e, StatID.Strength), s.GetBaseStat(e, StatID.Strength), "effective STR differs from base");
            Assert.Greater(s.GetEffectiveStat(e, StatID.MaxHP), s.GetBaseStat(e, StatID.MaxHP), "effective MaxHP differs from base");
            s.SetCurrentHP(e, 1100f);
            Assert.AreEqual(1100f, s.GetCurrentHP(e), "HP above the base MaxHP is allowed by the raised effective MaxHP.");

            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));

            CharacterRecord r = Stored();
            Assert.AreEqual(12, r.STR, "base STR, not effective");
            Assert.AreEqual(1000, r.MaxHP, "base MaxHP, not effective");
            Assert.AreEqual(1100f, r.CurrentHP, "HP is written as read");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_LiveBaseStatsFloatsHpMpAndFreePoints_StoredRecordMatchesAfterReload()
        {
            SeedAndLoad(_rig, MakeSeed());
            IronGrind.CharacterStats.CharacterStats s = _rig.Stats;
            EntityID e = _rig.Entity;
            s.SetBaseStat(e, StatID.Strength, 55);
            s.SetBaseStat(e, StatID.Dexterity, 44);
            s.SetBaseStat(e, StatID.Vitality, 33);
            s.SetBaseStat(e, StatID.Intelligence, 22);
            s.SetBaseStat(e, StatID.MaxHP, 900);
            s.SetBaseStat(e, StatID.MaxMP, 300);
            s.SetBaseStat(e, StatID.AttackPower, 77);
            s.SetBaseStat(e, StatID.Defense, 66);
            s.SetBaseStat(e, StatID.MagicDefense, 58);
            s.SetBaseStatFloat(e, StatID.CritChance, 0.33f);
            s.SetBaseStatFloat(e, StatID.CritMultiplier, 2.5f);
            s.SetBaseStatFloat(e, StatID.AttackRange, 3.5f);
            s.SetBaseStatFloat(e, StatID.AttackSpeedMultiplier, 1.2f);
            s.SetBaseStatFloat(e, StatID.MovementSpeed, 6.5f);
            _rig.Leveling.RestoreLevelingState(e, new LevelingStateSnapshot(3));
            s.SetCurrentHP(e, 123f);
            s.SetCurrentMP(e, 45f);

            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));

            CharacterRecord r = Stored();
            Assert.AreEqual(10, r.Level);
            Assert.AreEqual(0, r.Experience);
            Assert.AreEqual(55, r.STR);
            Assert.AreEqual(44, r.DEX);
            Assert.AreEqual(33, r.VIT);
            Assert.AreEqual(22, r.INT);
            Assert.AreEqual(900, r.MaxHP);
            Assert.AreEqual(300, r.MaxMP);
            Assert.AreEqual(77, r.AttackPower);
            Assert.AreEqual(66, r.Defense);
            Assert.AreEqual(58, r.MagicDefense);
            Assert.AreEqual(0.33f, r.CritChance);
            Assert.AreEqual(2.5f, r.CritMultiplier);
            Assert.AreEqual(3.5f, r.AttackRange);
            Assert.AreEqual(1.2f, r.AttackSpeedMultiplier);
            Assert.AreEqual(6.5f, r.MovementSpeed);
            Assert.AreEqual(3, r.HeldFreePoints);
            Assert.AreEqual(123f, r.CurrentHP);
            Assert.AreEqual(45f, r.CurrentMP);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_Gear_WrittenAsLoadedAndIdentityFromTheEntry()
        {
            SeedAndLoad(_rig, MakeSeed());

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            CharacterRecord r = Stored();
            Assert.AreEqual(new ItemID(700u), r.GearSlots[0].ItemId);
            Assert.AreEqual((byte)3, r.GearSlots[0].EnhancementLevel);
            Assert.AreEqual(new ItemID(701u), r.GearSlots[4].ItemId);
            Assert.AreEqual(ItemID.Invalid, r.GearSlots[1].ItemId);
            Assert.AreEqual(CHAR_A, r.CharacterID);
            Assert.AreEqual(ACCOUNT_A, r.AccountID);
            Assert.AreEqual("Brann", r.CharacterName);
            Assert.AreEqual(WARRIOR_CLASS_TYPE, r.ClassType);
        }

        [Test]
        public void SaveSession_LoadedRecordGearMutatedAfterApply_StoredGearIsTheOriginal()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            loaded.GearSlots[0] = new GearSlotRecord(new ItemID(999u), 9);
            loaded.GearSlots[1] = new GearSlotRecord(new ItemID(998u), 8);

            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));

            CharacterRecord r = Stored();
            Assert.AreEqual(new ItemID(700u), r.GearSlots[0].ItemId);
            Assert.AreEqual((byte)3, r.GearSlots[0].EnhancementLevel);
            Assert.AreEqual(new ItemID(701u), r.GearSlots[4].ItemId);
            Assert.AreEqual(ItemID.Invalid, r.GearSlots[1].ItemId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_InventorySlots2And11_ExpandedToDenseLayoutWithEighteenEmpty()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Inventory.ExportResult = new InventorySnapshot(new List<InventorySnapshotEntry>
            {
                new InventorySnapshotEntry(2, 5u, 4, 0),
                new InventorySnapshotEntry(11, 12u, 1, 3),
            });

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            AssertSlots2And11FilledRestEmpty(Stored());
        }

        [Test]
        public void SaveSession_InventoryEntryOutOfRange_IsIgnored()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Inventory.ExportResult = new InventorySnapshot(new List<InventorySnapshotEntry>
            {
                new InventorySnapshotEntry(3, 8u, 2, 0),
                new InventorySnapshotEntry((byte)InventoryConstants.INVENTORY_SLOT_COUNT, 99u, 9, 9),
            });

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            AssertSlot(Stored(), 3, 8u, 2, 0);
        }

        [Test]
        public void SaveSession_Gold_BalanceAndVersionComeFromTryGetState()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.CurrencySystem.RestoreCharacter(CHAR_A, 4321u, 17u);

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            CharacterRecord r = Stored();
            Assert.AreEqual(4321u, r.GoldBalance);
            Assert.AreEqual(17u, r.GoldVersion);
        }

        [Test]
        public void SaveSession_CurrencyKnowsNoCharacter_DatabaseErrorOneErrorNothingWritten()
        {
            Rig rig = BuildRig(TimeSpan.FromSeconds(5), new UnregisteredCurrency());
            CharacterRecord loaded = SeedAndLoad(rig, MakeSeed());
            LogAssert.Expect(LogType.Error, new Regex(@"Currency has no state for .*nothing written"));

            Task<CharacterSaveResult> task = rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, _counting.UpdateCount, "No store call.");
            Assert.AreEqual(0, rig.Queue.ActiveCharacterCount, "No queue entry.");
            CharacterRecord r = Stored();
            Assert.AreEqual(1250u, r.GoldBalance);
            Assert.AreEqual(9u, r.GoldVersion);
            Assert.AreEqual(loaded.SaveVersion, r.SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_LocatorReturnsZone42_Writes42()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Zones.Zone = new ZoneID(42u);

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(new ZoneID(42u), Stored().LastZoneID);
        }

        [Test]
        public void SaveSession_LocatorReturnsInvalid_WritesTheLoadedZone()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Zones.Zone = ZoneID.Invalid;

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(LOADED_ZONE, Stored().LastZoneID);
        }

        [Test]
        public void SaveSession_SnapshotThrows_DatabaseErrorOneErrorNoStoreCallNoQueueEntry()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Inventory.ExportResult = null; // the fake then throws NotSupportedException from ExportSnapshot
            LogAssert.Expect(LogType.Error, new Regex(@"SaveSession \(CleanLogout\) for .* could not take its snapshot"));

            Task<CharacterSaveResult> task = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, _counting.UpdateCount);
            Assert.AreEqual(0, _rig.Queue.ActiveCharacterCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- step 7 (AC-CP-18, AC-CP-15)

        [Test]
        public void SaveSession_Success_StoredVersionPlusOneNewSavedTimeCreatedTimeKept()
        {
            CharacterRecord seed = MakeSeed();
            seed.CreatedAtUtc = T1.AddDays(-10);
            CharacterRecord loaded = SeedAndLoad(_rig, seed);
            uint n = loaded.SaveVersion;
            Assert.AreNotEqual(T2, loaded.CreatedAtUtc, "The created time differs from the store clock at save time.");

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord r = Stored();
            Assert.AreEqual(n + 1u, r.SaveVersion);
            Assert.AreEqual(T2, r.LastSavedAtUtc);
            Assert.AreEqual(T1, r.CreatedAtUtc);
            Assert.AreEqual(loaded.CreatedAtUtc, r.CreatedAtUtc);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_TwoSequentialSaves_BothSucceedBecauseTheEntryVersionAdvanced()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            uint n = loaded.SaveVersion;

            CharacterSaveResult first = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));
            CharacterSaveResult second = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, first);
            Assert.AreEqual(CharacterSaveResult.Success, second);
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            Assert.AreEqual(2, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_ThenLoadIntoFreshSystems_EveryPersistedFieldMatches()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            uint n = loaded.SaveVersion;
            IronGrind.CharacterStats.CharacterStats s = _rig.Stats;
            EntityID e = _rig.Entity;
            s.SetBaseStat(e, StatID.Level, 11);
            s.SetBaseStat(e, StatID.Experience, 37);
            s.SetBaseStat(e, StatID.Strength, 55);
            s.SetBaseStat(e, StatID.Dexterity, 44);
            s.SetBaseStat(e, StatID.Vitality, 33);
            s.SetBaseStat(e, StatID.Intelligence, 22);
            s.SetBaseStat(e, StatID.MaxHP, 900);
            s.SetBaseStat(e, StatID.MaxMP, 300);
            s.SetBaseStat(e, StatID.AttackPower, 77);
            s.SetBaseStat(e, StatID.Defense, 66);
            s.SetBaseStat(e, StatID.MagicDefense, 58);
            s.SetBaseStatFloat(e, StatID.CritChance, 0.33f);
            s.SetBaseStatFloat(e, StatID.CritMultiplier, 2.5f);
            s.SetBaseStatFloat(e, StatID.AttackRange, 3.5f);
            s.SetBaseStatFloat(e, StatID.AttackSpeedMultiplier, 1.2f);
            s.SetBaseStatFloat(e, StatID.MovementSpeed, 6.5f);
            _rig.Leveling.RestoreLevelingState(e, new LevelingStateSnapshot(3));
            s.SetCurrentHP(e, 123f);
            s.SetCurrentMP(e, 45f);
            _rig.CurrencySystem.RestoreCharacter(CHAR_A, 4321u, 17u);
            _rig.Zones.Zone = new ZoneID(42u);
            _rig.Inventory.ExportResult = new InventorySnapshot(new List<InventorySnapshotEntry>
            {
                new InventorySnapshotEntry(2, 5u, 4, 0),
                new InventorySnapshotEntry(11, 12u, 1, 3),
            });
            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));

            Rig fresh = BuildRig(TimeSpan.FromSeconds(5), null);
            CharacterLoadResult result = Complete(fresh.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, fresh.Service.ApplyLoadedRecord(fresh.Entity, result.Record));

            IronGrind.CharacterStats.CharacterStats f = fresh.Stats;
            EntityID fe = fresh.Entity;
            Assert.AreEqual(new ZoneID(42u), result.LastZoneID);
            Assert.AreEqual(n + 1u, result.Record.SaveVersion);
            Assert.AreEqual("Brann", result.Record.CharacterName);
            Assert.AreEqual(WARRIOR_CLASS_TYPE, result.Record.ClassType);
            Assert.AreEqual(T1, result.Record.CreatedAtUtc);
            Assert.AreEqual(11, f.GetBaseStat(fe, StatID.Level));
            Assert.AreEqual(37, f.GetBaseStat(fe, StatID.Experience));
            Assert.AreEqual(55, f.GetBaseStat(fe, StatID.Strength));
            Assert.AreEqual(44, f.GetBaseStat(fe, StatID.Dexterity));
            Assert.AreEqual(33, f.GetBaseStat(fe, StatID.Vitality));
            Assert.AreEqual(22, f.GetBaseStat(fe, StatID.Intelligence));
            Assert.AreEqual(900, f.GetBaseStat(fe, StatID.MaxHP));
            Assert.AreEqual(300, f.GetBaseStat(fe, StatID.MaxMP));
            Assert.AreEqual(77, f.GetBaseStat(fe, StatID.AttackPower));
            Assert.AreEqual(66, f.GetBaseStat(fe, StatID.Defense));
            Assert.AreEqual(58, f.GetBaseStat(fe, StatID.MagicDefense));
            Assert.AreEqual(0.33f, f.GetBaseStatFloat(fe, StatID.CritChance));
            Assert.AreEqual(2.5f, f.GetBaseStatFloat(fe, StatID.CritMultiplier));
            Assert.AreEqual(3.5f, f.GetBaseStatFloat(fe, StatID.AttackRange));
            Assert.AreEqual(1.2f, f.GetBaseStatFloat(fe, StatID.AttackSpeedMultiplier));
            Assert.AreEqual(6.5f, f.GetBaseStatFloat(fe, StatID.MovementSpeed));
            Assert.AreEqual(3, fresh.Leveling.GetHeldFreePoints(fe));
            Assert.AreEqual(123f, f.GetCurrentHP(fe));
            Assert.AreEqual(45f, f.GetCurrentMP(fe));
            Assert.IsTrue(fresh.CurrencySystem.TryGetState(CHAR_A, out uint balance, out uint version));
            Assert.AreEqual(4321u, balance);
            Assert.AreEqual(17u, version);
            AssertSlots2And11FilledRestEmpty(result.Record);
            Assert.AreEqual(new ItemID(700u), result.Record.GearSlots[0].ItemId);
            Assert.AreEqual((byte)3, result.Record.GearSlots[0].EnhancementLevel);
            Assert.AreEqual(new ItemID(701u), result.Record.GearSlots[4].ItemId);
            Assert.AreEqual((byte)0, result.Record.GearSlots[4].EnhancementLevel);
            Assert.AreEqual(2, fresh.Inventory.ImportCalls[0].Snapshot.Slots.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- HP override (AC-CP-30, AC-CP-31)

        [Test]
        public void SaveSession_HpOverrideZeroWithHp650_WritesZeroHpAndUntouchedMp()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Stats.SetCurrentMP(_rig.Entity, 77f);

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.ZoneClosed, 0f, CancellationToken.None));

            CharacterRecord r = Stored();
            Assert.AreEqual(0f, r.CurrentHP);
            Assert.AreEqual(77f, r.CurrentMP);
            Assert.AreEqual(650f, _rig.Stats.GetCurrentHP(_rig.Entity), "The live HP is not changed by the save.");
        }

        [Test]
        public void SaveSession_NoOverrideWithHp350_WritesHp350()
        {
            SeedAndLoad(_rig, MakeSeed());
            _rig.Stats.SetCurrentHP(_rig.Entity, 350f);

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(350f, Stored().CurrentHP);
        }

        [Test]
        public void SaveSession_HpOverrideAboveMaxHp_WrittenAsGiven()
        {
            SeedAndLoad(_rig, MakeSeed());

            Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.ZoneClosed, 9000f, CancellationToken.None));

            Assert.AreEqual(9000f, Stored().CurrentHP);
        }

        [Test]
        public void SaveSession_HpOverrideInfinite_WrittenAsGiven()
        {
            SeedAndLoad(_rig, MakeSeed());

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.ZoneClosed, float.PositiveInfinity, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            Assert.AreEqual(float.PositiveInfinity, Stored().CurrentHP);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_HpOverrideNaN_DatabaseErrorOneErrorNothingWritten()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            LogAssert.Expect(LogType.Error, new Regex(@"hpOverride is NaN; nothing written"));

            Task<CharacterSaveResult> task = _rig.Service.SaveSession(CHAR_A, SessionEndReason.ZoneClosed, float.NaN, CancellationToken.None);
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, _counting.UpdateCount, "No store call.");
            Assert.AreEqual(0, _rig.Queue.ActiveCharacterCount, "No queue entry.");
            Assert.AreEqual(loaded.SaveVersion, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- conflict (CR-CP-6, AC-CP-20)

        [Test]
        public void SaveSession_StoreRowAdvancedBehind_ConflictOneWarningEntryVersionUnchanged()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            uint n = loaded.SaveVersion;
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, n + 5u));
            LogAssert.Expect(LogType.Warning, new Regex(@"CR-CP-6.*EC-CP-4"));

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, result);
            Assert.AreEqual(n + 5u, Stored().SaveVersion, "Nothing was written.");
            Assert.AreEqual(1, _counting.UpdateCount, "No retry.");

            // The entry still expects n: with the row put back, the next save is a Success.
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, n));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));
            Assert.AreEqual(n + 1u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_SecondCallWhileFirstHeld_FirstSuccessSecondConflictStoredRecordIsTheFirsts()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            uint n = loaded.SaveVersion;
            _rig.Stats.SetCurrentHP(_rig.Entity, 100f);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> first = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            _rig.Stats.SetCurrentHP(_rig.Entity, 200f);
            Task<CharacterSaveResult> second = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);

            // R4: the second snapshot was taken at call time, not at its queue turn (the first is still held).
            Assert.AreEqual(2, _rig.Inventory.ExportCallCount, "Both snapshots are taken before the release.");
            Assert.AreEqual(2, _rig.Zones.CallCount, "Both zone reads happen before the release.");
            LogAssert.Expect(LogType.Warning, new Regex(@"CR-CP-6.*EC-CP-4"));

            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, Complete(second));
            CharacterRecord r = Stored();
            Assert.AreEqual(100f, r.CurrentHP);
            Assert.AreEqual(n + 1u, r.SaveVersion);
            Assert.AreEqual(2, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- reload and independence

        [Test]
        public void SaveSession_ReloadWhileAWriteIsInFlight_TheWriteAdvancesTheNewEntryAndALaterSaveSucceeds()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            uint n = loaded.SaveVersion;
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> inFlight = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            Assert.IsFalse(inFlight.IsCompleted);

            // The reload sees the stored row at version n and replaces the entry (version n) while the write is pending.
            CharacterRecord reloaded = LoadInto(_rig);
            Assert.AreEqual(n, reloaded.SaveVersion);
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(inFlight));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)),
                "The completed write advanced the entry to n + 1.");
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_ReloadAfterASaveCommitted_TheEntryIsAtTheStoredVersionAndALaterSaveSucceeds()
        {
            CharacterRecord loaded = SeedAndLoad(_rig, MakeSeed());
            uint n = loaded.SaveVersion;
            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));

            CharacterRecord reloaded = LoadInto(_rig);

            Assert.AreEqual(n + 1u, reloaded.SaveVersion);
            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_TwoCharacters_ASecondCharacterSavesWhileTheFirstIsHeld()
        {
            SeedAndLoad(_rig, MakeSeed());
            SeedAndLoadCharacterB(_rig);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> heldA = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            Assert.IsFalse(heldA.IsCompleted);

            Task<CharacterSaveResult> b = _rig.Service.SaveSession(CHAR_B, SessionEndReason.CleanLogout, CancellationToken.None);

            Assert.AreEqual(CharacterSaveResult.Success, Complete(b));
            Assert.IsFalse(heldA.IsCompleted, "Character A is still held.");
            release();
            Assert.AreEqual(CharacterSaveResult.Success, Complete(heldA));
            Assert.AreEqual(2, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- failure (CR-CP-11)

        [TestCase(SessionEndReason.CleanLogout)]
        [TestCase(SessionEndReason.SessionTTLExpiry)]
        [TestCase(SessionEndReason.ZoneClosed)]
        public void SaveSession_StoreFails_DatabaseErrorOneErrorTaskNotFaulted(SessionEndReason reason)
        {
            SeedAndLoad(_rig, MakeSeed());
            _store.FailNext(new InvalidOperationException("database down"));
            LogAssert.Expect(LogType.Error, new Regex(@"SaveSession \(" + Regex.Escape(reason.ToString()) + @"\) for .* failed"));

            Task<CharacterSaveResult> task = _rig.Service.SaveSession(CHAR_A, reason, CancellationToken.None);
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(task.IsCanceled);
            Assert.AreEqual(1, _counting.UpdateCount);

            // Nothing was torn down: the next save re-reads the stored version (the outcome was unknown) and succeeds.
            LogAssert.Expect(LogType.Warning, new Regex(@"re-read the stored SaveVersion"));
            Assert.AreEqual(CharacterSaveResult.Success, Complete(_rig.Service.SaveSession(CHAR_A, reason, CancellationToken.None)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_StoreNeverCompletes_DatabaseErrorAfterTheInjectedTimeoutWithAnErrorLog()
        {
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100), null);
            SeedAndLoad(rig, MakeSeed());
            _store.NeverCompleteNext();
            LogAssert.Expect(LogType.Error, new Regex(@"SaveSession \(CleanLogout\) for .* failed.*TimeoutException"));

            Task<CharacterSaveResult> task = rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            CharacterSaveResult result = Complete(task);

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(1, _counting.UpdateCount);
            WaitUntil(() => rig.Queue.ActiveCharacterCount == 0, "the queue to drain");
            LogAssert.NoUnexpectedReceived();
        }

        // R3, updated by Story 008 (CR-CP-6, EC-CP-5): the abandoned update commits after the timeout, so the entry is
        // behind the stored row; the unknown-version mark makes the next save re-read the stored version instead of conflicting.
        [Test]
        public void SaveSession_TimedOutUpdateLaterCommits_NextSaveReReadsTheStoredVersionAndSucceeds()
        {
            Rig rig = BuildRig(TimeSpan.FromMilliseconds(100), null);
            CharacterRecord loaded = SeedAndLoad(rig, MakeSeed());
            uint n = loaded.SaveVersion;
            Action release = _store.HoldNext();
            LogAssert.Expect(LogType.Error, new Regex(@"SaveSession \(CleanLogout\) for .* failed.*TimeoutException"));

            CharacterSaveResult timedOut = Complete(rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, timedOut);
            release();
            WaitUntil(() => Stored().SaveVersion == n + 1u, "the abandoned update to commit");
            LogAssert.Expect(LogType.Warning, new Regex(@"re-read the stored SaveVersion"));

            CharacterSaveResult next = Complete(rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, next, "The entry expected n, the stored row was n + 1: the mark made the save re-read it.");
            Assert.AreEqual(n + 2u, Stored().SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_TokenCancelledWhileStoreHeld_DatabaseErrorNotCancelled()
        {
            SeedAndLoad(_rig, MakeSeed());
            _store.HoldNext();
            LogAssert.Expect(LogType.Error, new Regex(@"SaveSession \(CleanLogout\) for .* failed"));
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSaveResult> task = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, cts.Token);
                Assert.IsFalse(task.IsCompleted);

                cts.Cancel();

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(task));
                Assert.IsFalse(task.IsCanceled);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SaveSession_TokenCancelledWhileWaitingInTheQueue_DatabaseErrorOneWarningTheWriteNeverRuns()
        {
            SeedAndLoad(_rig, MakeSeed());
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> first = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            Assert.IsFalse(first.IsCompleted);
            LogAssert.Expect(LogType.Warning, new Regex(@"SaveSession \(SessionTTLExpiry\) for .* was cancelled before its write ran; nothing written"));
            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSaveResult> second = _rig.Service.SaveSession(CHAR_A, SessionEndReason.SessionTTLExpiry, cts.Token);
                Assert.IsFalse(second.IsCompleted, "Waiting behind the held first save.");

                cts.Cancel();

                Assert.AreEqual(CharacterSaveResult.DatabaseError, Complete(second));
                Assert.IsFalse(second.IsCanceled);
                Assert.AreEqual(1, _counting.UpdateCount, "Only the first save reached the store.");
            }

            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            Assert.AreEqual(1, _counting.UpdateCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- latency (ADR-006 Decision 4)

        // Relies on Story 006's queue starting an idle character's write inline: WriteAsync reads start = utcNow()
        // inside the SaveSession call, so the clock is still at T1 then, and only afterwards does the test advance it.
        [TestCase(49, 0)]
        [TestCase(50, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 2)]
        public void SaveSession_WriteLatencyMilliseconds_LogsWarningOrErrorByThreshold(int milliseconds, int expectedLog)
        {
            SeedAndLoad(_rig, MakeSeed());
            _serviceTime = T1;
            Action release = _store.HoldNext();
            if (expectedLog == 1)
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"took .* ms"));
            }
            else if (expectedLog == 2)
            {
                LogAssert.Expect(LogType.Error, new Regex(@"took .* ms"));
            }

            Task<CharacterSaveResult> task = _rig.Service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            _serviceTime = T1.AddMilliseconds(milliseconds);
            release();

            Assert.AreEqual(CharacterSaveResult.Success, Complete(task));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- construction and wiring

        [Test]
        public void Constructor_NullDependency_ThrowsArgumentNullException()
        {
            Rig r = _rig;
            Func<DateTime> clock = () => T1;
            TimeSpan timeout = TimeSpan.FromSeconds(5);
            var queue = new CharacterWriteQueue();

            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(null, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, queue, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, null, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, queue, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, null, r.Inventory, r.CurrencySystem, r.Zones, queue, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, null, r.CurrencySystem, r.Zones, queue, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, null, r.Zones, queue, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, null, queue, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, null, clock, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, queue, null, timeout));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, null, clock));
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, (Func<DateTime>)null));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Constructor_NonPositiveWriteTimeout_ThrowsArgumentOutOfRangeException(int milliseconds)
        {
            Rig r = _rig;

            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterPersistenceService(
                _counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, new CharacterWriteQueue(), () => T1, TimeSpan.FromMilliseconds(milliseconds)));
        }

        [Test]
        public void Constructor_ConvenienceOverload_BuildsAServiceThatSaves()
        {
            Rig r = _rig;
            var service = new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem, r.Zones, () => _serviceTime);
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            CharacterLoadResult loaded = Complete(service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, service.ApplyLoadedRecord(r.Entity, loaded.Record));

            Assert.AreEqual(CharacterSaveResult.Success, Complete(service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None)));
        }

        [Test]
        public void SaveSession_ServiceBuiltWithoutZoneLocatorAndQueue_ThrowsInvalidOperationExceptionSynchronously()
        {
            Rig r = _rig;
            var service = new CharacterPersistenceService(_counting, r.Stats, r.Leveling, r.Inventory, r.CurrencySystem);

            Assert.Throws<InvalidOperationException>(() => service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, 0f, CancellationToken.None));
            Assert.AreEqual(0, _counting.UpdateCount);
        }

        // ---------------------------------------------------------------- Currency.TryGetState

        [Test]
        public void CurrencyTryGetState_RegisteredCharacter_ReturnsBalanceAndVersion()
        {
            var currency = new CurrencySystem();
            currency.RestoreCharacter(CHAR_A, 1250u, 9u);

            bool found = currency.TryGetState(CHAR_A, out uint balance, out uint version);

            Assert.IsTrue(found);
            Assert.AreEqual(1250u, balance);
            Assert.AreEqual(9u, version);
        }

        [Test]
        public void CurrencyTryGetState_UnregisteredCharacter_ReturnsFalseWithZeros()
        {
            var currency = new CurrencySystem();

            bool found = currency.TryGetState(CHAR_UNKNOWN, out uint balance, out uint version);

            Assert.IsFalse(found);
            Assert.AreEqual(0u, balance);
            Assert.AreEqual(0u, version);
        }
    }
}
