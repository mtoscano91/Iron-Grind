using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.EquipmentSystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 016: the real-database verification run. Each test drives
    /// <see cref="CharacterPersistenceService"/> over the Npgsql store (ADR-006 Validation Criteria: no mock substitution) and
    /// asserts what its Part A counterpart (named in the test's doc comment) asserts, plus the two gates (AC-CP-21 concurrency,
    /// write latency), AC-CP-15 / AC-CP-26 over a rebuilt "fresh process state", AC-CP-19 against a second connection and
    /// AC-CP-5 against a locked table. Explicit: needs a local PostgreSQL with the Story 013 schema and <c>IRONGRIND_DEV_PG</c>
    /// (tools/postgres/README.md). Every test uses its own id(s) in 9_001_600 to 9_001_699 and deletes the rows in SetUp and
    /// TearDown. The test assembly names no Npgsql type (ADR-012 Decision 1).
    /// </summary>
    [TestFixture]
    [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG — tools/postgres/README.md")]
    [Category("Database")]
    internal sealed class CharacterPersistence_RealDatabase_Tests
    {
        // ---------------------------------------------------------------- ids (Story 016 owns 9_001_600 - 9_001_699)
        private const uint ID_AC13 = 9_001_601u;
        private const uint ID_AC14 = 9_001_602u;
        private const uint ID_AC15 = 9_001_603u;
        private const uint ID_AC16 = 9_001_604u;
        private const uint ID_AC17 = 9_001_605u;
        private const uint ID_AC18 = 9_001_606u;
        private const uint ID_AC19 = 9_001_607u;
        private const uint ID_AC20 = 9_001_608u;
        private const uint ID_AC21 = 9_001_609u;
        private const uint ID_LATENCY = 9_001_610u;
        private const uint ID_AC22 = 9_001_611u;
        private const uint ID_AC23 = 9_001_612u;
        private const uint ID_AC24 = 9_001_613u;
        private const uint ID_AC25 = 9_001_614u;
        private const uint ID_AC26 = 9_001_615u;
        private const uint ID_AC30 = 9_001_616u;
        private const uint ID_AC31 = 9_001_617u;
        private const uint ID_AC5 = 9_001_618u;

        private static readonly uint[] ALL_IDS =
        {
            ID_AC13, ID_AC14, ID_AC15, ID_AC16, ID_AC17, ID_AC18, ID_AC19, ID_AC20, ID_AC21, ID_LATENCY,
            ID_AC22, ID_AC23, ID_AC24, ID_AC25, ID_AC26, ID_AC30, ID_AC31, ID_AC5,
        };

        // ---------------------------------------------------------------- constants
        private static readonly DateTime FIXED_NOW = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private static readonly ItemID UNKNOWN_ITEM = new ItemID(999u);
        private static readonly ItemID SWORD = new ItemID(401u);
        private const uint CLIENT_ID = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;
        private const int WAIT_MS = 15000;
        private const int CONCURRENT_WAIT_MS = 60000;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int EXPECTED_TTL_SECONDS = 300;
        private const int START_LEVEL = 10;
        private const int START_EXPERIENCE = 50;
        private const int SEED_EXPERIENCE = 0;
        private const int POST_LEVEL_UP_EXPERIENCE = 3;
        private const int WRITE_TIMEOUT_SECONDS = CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS;
        private const int CONCURRENT_CALL_COUNT = 50;
        private const int LATENCY_CALL_COUNT = 100;
        // ADR-006 Validation Criteria: P95 <= 50 ms. A literal on purpose: retuning PERSISTENCE_WRITE_WARNING_MS must not move the gate.
        private const double LATENCY_P95_LIMIT_MS = 50.0;
        // Story 016 AC-CP-5: the caller's load token ends the blocked fetch (CR-CP-8); the store's 5 s command timeout does not (TD-084).
        private const double LOAD_LOCKED_MIN_MS = CharacterPersistenceConstants.CHARACTER_LOAD_TIMEOUT_SECONDS * 1000.0;
        private const double LOAD_LOCKED_TOLERANCE_MS = 250.0; // timer vs Stopwatch granularity
        private const double LOAD_LOCKED_LIMIT_MS = 12000.0;
        private const float FLOAT_TOLERANCE = 1e-6f;
        private const float SEED_CURRENT_HP = 650f;
        private const float AC20_FIRST_HP = 100f;
        private const float AC20_SECOND_HP = 200f;
        private const float AC25_CURRENT_HP = 300f;
        private const float AC30_CURRENT_MP = 77f;
        private const float AC31_CURRENT_HP = 350f;
        private const uint VERSION_MISMATCH_OFFSET = 1000u; // AC-CP-14 decorator: an expected version nothing matches, so the issued UPDATE writes nothing
        private const byte MAX_ENHANCEMENT_LEVEL = 10;
        private const byte PLUS_NINE = 9;
        private const int STUB_MAX_HP = 400;
        private const int STUB_ATTACK_POWER = 30;
        private const int BASE_MAX_HP = 500;
        private const string BACKDATE_INTERVAL_SQL = "interval '1 hour'"; // backdates a timestamp so "later" is not an artefact of two NOW() calls

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string SERVICE_FAILED_LOG = @"SaveIrreversibleOutcome \(LevelUp\) for .* failed";
        private const string SERVICE_CONFLICT_LOG = @"CRITICAL: SaveIrreversibleOutcome \(LevelUp\) for .* conflicted";
        private const string SESSION_CONFLICT_WARNING = @"CR-CP-6.*EC-CP-4";

        // ---------------------------------------------------------------- test-side store decorators

        /// <summary>Forwards to the inner store; the next update can be held until the test releases it (the Npgsql store has no <c>HoldNext</c>).</summary>
        private sealed class HoldingStore : ICharacterRecordStore
        {
            private readonly ICharacterRecordStore _inner;
            private TaskCompletionSource<bool> _gate;
            private int _updateCalls;

            public HoldingStore(ICharacterRecordStore inner)
            {
                _inner = inner;
            }

            public int UpdateCalls => Volatile.Read(ref _updateCalls);

            /// <summary>Holds the next update; returns the action that releases it.</summary>
            public Action HoldNext()
            {
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _gate, gate);
                return () => gate.TrySetResult(true);
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct) => _inner.FetchAsync(id, ct);

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => _inner.InsertAsync(record, ct);

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                Interlocked.Increment(ref _updateCalls);
                TaskCompletionSource<bool> gate = Interlocked.Exchange(ref _gate, null);
                return gate == null ? _inner.UpdateAsync(record, expectedSaveVersion, ct) : Held(gate.Task, record, expectedSaveVersion, ct);
            }

            private async Task<int> Held(Task gate, CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                await gate.ConfigureAwait(false);
                return await _inner.UpdateAsync(record, expectedSaveVersion, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// AC-CP-14's write failure: issues the Npgsql UPDATE with an expected version nothing matches (so the call reaches the database
        /// and writes nothing), then throws. The Part A test uses the in-memory store's <c>FailNext</c>, which fails before any write;
        /// throwing after a committing UPDATE would be the unknown-outcome case (AC-CP-33), not AC-CP-14.
        /// </summary>
        private sealed class ThrowAfterIssuedUpdateStore : ICharacterRecordStore
        {
            private readonly ICharacterRecordStore _inner;

            public ThrowAfterIssuedUpdateStore(ICharacterRecordStore inner)
            {
                _inner = inner;
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct) => _inner.FetchAsync(id, ct);

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => _inner.InsertAsync(record, ct);

            public async Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                await _inner.UpdateAsync(record, expectedSaveVersion + VERSION_MISMATCH_OFFSET, ct).ConfigureAwait(false);
                throw new InvalidOperationException("database down (injected after the UPDATE was issued)");
            }
        }

        /// <summary>Records the Stopwatch-tick start and end of every update, and its duration; thread-safe.</summary>
        private sealed class IntervalStore : ICharacterRecordStore
        {
            private readonly ICharacterRecordStore _inner;
            private readonly object _lock = new object();
            private readonly List<(long Start, long End)> _intervals = new List<(long, long)>();

            public IntervalStore(ICharacterRecordStore inner)
            {
                _inner = inner;
            }

            public List<(long Start, long End)> Intervals
            {
                get
                {
                    lock (_lock)
                    {
                        return new List<(long, long)>(_intervals);
                    }
                }
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct) => _inner.FetchAsync(id, ct);

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => _inner.InsertAsync(record, ct);

            public async Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    return await _inner.UpdateAsync(record, expectedSaveVersion, ct).ConfigureAwait(false);
                }
                finally
                {
                    long end = Stopwatch.GetTimestamp();
                    lock (_lock)
                    {
                        _intervals.Add((start, end));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- rig

        private sealed class EquipmentRig
        {
            public FakeItemDatabase Db;
            public InventoryService Inventory;
            public EquipmentService Equipment;
        }

        private sealed class Rig
        {
            public LevelingService Leveling;
            public IronGrind.CharacterStats.CharacterStats Stats;
            public CurrencySystem CurrencySystem;
            public RecordingInventory Inventory;
            public FakeZoneLocator Zones;
            public CharacterPersistenceService Service;
            public EntityID Entity;
            public EquipmentRig EquipRig;
            public FakeEquipmentService FakeEquipment; // null when EquipRig is set
        }

        private sealed class LevelUpOutcome
        {
            public int FinalLevel;
        }

        private string _connectionString;
        private readonly List<IDisposable> _dataSources = new List<IDisposable>();
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();

        // Coordinator harness (AC-CP-13, 14, 17), as in CharacterPersistence_IrreversibleOutcome_Tests.
        private TickCompletionQueue _queue;
        private CharacterMutationGate _gate;
        private NetworkTestObserver _observer;
        private IrreversibleOutcomeCoordinator _coordinator;
        private uint _tick;
        private int _deliveries;
        private int _reverts;
        private readonly List<(uint ClientId, DisconnectReason Reason)> _disconnects = new List<(uint, DisconnectReason)>();
        private readonly List<(uint ClientId, int Ttl)> _preserves = new List<(uint, int)>();
        private int _capturedLevel;
        private int _capturedExperience;
        private Task<CharacterSaveResult> _lastTask;

        [SetUp]
        public void SetUp()
        {
            _connectionString = null; // NUnit reuses the fixture instance; a test ignored here must not clean with the last one's string
            _connectionString = PgTestDatabase.ConnectionStringOrIgnore();
            PgTestDatabase.DeleteCharacters(_connectionString, ALL_IDS);
            _tick = 0u;
            _deliveries = 0;
            _reverts = 0;
            _disconnects.Clear();
            _preserves.Clear();
            _lastTask = null;
            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            _queue = new TickCompletionQueue(LONG_WATCHDOG_TICKS);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);
        }

        [TearDown]
        public void TearDown()
        {
            // Runs also when SetUp ignored the test; then there is nothing to clean. Each step runs even if an earlier one throws.
            LogAssert.ignoreFailingMessages = false;
            try
            {
                DisposeAllDataSources();
                foreach (ItemDefinition definition in _created)
                {
                    UnityEngine.Object.DestroyImmediate(definition);
                }

                _created.Clear();
            }
            finally
            {
                if (!string.IsNullOrEmpty(_connectionString))
                {
                    PgTestDatabase.DeleteCharacters(_connectionString, ALL_IDS);
                }
            }
        }

        private ICharacterRecordStore NewStore()
        {
            var created = NpgsqlRecordStoreTestAccess.CreateStoreWithOwnedDataSource(_connectionString);
            _dataSources.Add(created.DataSource);
            return created.Store;
        }

        // Disposes every data source even if one throws; rethrows the first failure after all were tried.
        private void DisposeAllDataSources()
        {
            Exception first = null;
            foreach (IDisposable dataSource in _dataSources)
            {
                try
                {
                    dataSource.Dispose();
                }
                catch (Exception ex)
                {
                    first = first ?? ex;
                }
            }

            _dataSources.Clear();
            if (first != null)
            {
                throw first;
            }
        }

        private Rig BuildRig(ICharacterRecordStore store, CharacterID realEquipmentFor = default)
        {
            var rig = new Rig();
            rig.Leveling = new LevelingService(BuildXpThresholds(), CreateClassRegistry());
            rig.Stats = CharacterStatsFixture.CreateWithLeveling(rig.Leveling);
            rig.Leveling.AttachCharacterStats(rig.Stats);
            rig.Entity = CharacterStatsFixture.PlayerEntityId;
            rig.CurrencySystem = new CurrencySystem();
            rig.Inventory = new RecordingInventory { ExportResult = InventorySnapshot.Empty };
            rig.Zones = new FakeZoneLocator();
            IEquipmentService equipment;
            if (realEquipmentFor != default(CharacterID))
            {
                rig.EquipRig = new EquipmentRig();
                rig.EquipRig.Db = new FakeItemDatabase();
                rig.EquipRig.Inventory = new InventoryService(rig.EquipRig.Db, () => 0u, MAX_ENHANCEMENT_LEVEL);
                rig.EquipRig.Inventory.RegisterCharacter(realEquipmentFor);
                var bonuses = new EnhancementBonusProvider(EnhancementConfig.Default);
                rig.EquipRig.Equipment = new EquipmentService(rig.EquipRig.Inventory, rig.Stats, rig.EquipRig.Db, bonuses, EnhancementConfig.Default);
                equipment = rig.EquipRig.Equipment;
            }
            else
            {
                rig.FakeEquipment = new FakeEquipmentService();
                equipment = rig.FakeEquipment;
            }

            rig.Service = new CharacterPersistenceService(
                store,
                rig.Stats,
                rig.Leveling,
                rig.Inventory,
                rig.CurrencySystem,
                equipment,
                rig.Zones,
                new CharacterWriteQueue(),
                () => FIXED_NOW,
                TimeSpan.FromSeconds(WRITE_TIMEOUT_SECONDS));
            return rig;
        }

        private void AddItem(Rig rig, ItemID id, GearSlot slot, GearTier tier, StatID stat, float flat)
        {
            EquipmentData data = EquipmentData.CreateForTesting(slot, tier, new[] { StatModifierEntry.CreateForTesting(stat, flat) });
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, "Item" + id.RawValue, ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            rig.EquipRig.Db.Add(definition);
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

        private static int[] BuildXpThresholds()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD;
            t[61] = int.MaxValue;
            return t;
        }

        // Blocking on the main thread is safe only because every await under the service, queue, store and decorators uses
        // ConfigureAwait(false); a captured UnitySynchronizationContext would turn this into a timeout failure.
        private static T Complete<T>(Task<T> task, int waitMs = WAIT_MS)
        {
            Assert.IsTrue(task.Wait(waitMs), "The task did not complete within " + waitMs + " ms.");
            return task.Result;
        }

        private static void Complete(Task task, int waitMs = WAIT_MS)
        {
            Assert.IsTrue(task.Wait(waitMs), "The task did not complete within " + waitMs + " ms.");
        }

        private static void WaitUntil(Func<bool> condition, string what)
        {
            Assert.IsTrue(SpinWait.SpinUntil(condition, WAIT_MS), "Timed out waiting for: " + what);
        }

        private static CharacterRecord MakeSeed(uint rawId)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, new CharacterID(rawId), WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = START_LEVEL;
            r.Experience = SEED_EXPERIENCE;
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
            r.CurrentHP = SEED_CURRENT_HP;
            r.CurrentMP = 100f;
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.LastZoneID = LOADED_ZONE;
            r.GearSlots[0] = new GearSlotRecord(new ItemID(700u), 3);
            r.GearSlots[4] = new GearSlotRecord(new ItemID(701u), 0);
            return r;
        }

        private static CharacterLoadResult LoadAndApply(Rig rig, CharacterID id)
        {
            CharacterLoadResult result = Complete(rig.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Service.ApplyLoadedRecord(rig.Entity, result.Record));
            return result;
        }

        // Inserts the seed through the store, loads it into the rig. Returns the loaded record.
        private static CharacterRecord SeedAndLoad(Rig rig, ICharacterRecordStore store, uint rawId)
        {
            Assert.IsTrue(Complete(store.InsertAsync(MakeSeed(rawId), CancellationToken.None)));
            return LoadAndApply(rig, new CharacterID(rawId)).Record;
        }

        private static CharacterRecord Stored(ICharacterRecordStore store, uint rawId) =>
            Complete(store.FetchAsync(new CharacterID(rawId), CancellationToken.None));

        private long RawVersion(uint rawId) =>
            PgTestDatabase.ScalarLong(_connectionString, "SELECT save_version FROM character_records WHERE character_id = " + rawId);

        // ---------------------------------------------------------------- coordinator harness

        private void DrainNextTick()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private LevelUpOutcome ApplyLevelUps(Rig rig, int levelUps)
        {
            _capturedLevel = rig.Stats.GetBaseStat(rig.Entity, StatID.Level);
            _capturedExperience = rig.Stats.GetBaseStat(rig.Entity, StatID.Experience);
            int level = _capturedLevel;
            for (int i = 0; i < levelUps; i++)
            {
                level++;
                rig.Stats.SetBaseStat(rig.Entity, StatID.Level, level);
                rig.Stats.SetBaseStat(rig.Entity, StatID.Experience, POST_LEVEL_UP_EXPERIENCE);
            }

            return new LevelUpOutcome { FinalLevel = level };
        }

        private IrreversibleOutcomeBeginResult BeginLevelUp(Rig rig, ICharacterPersistence persistence, CharacterID charId, int levelUps = 1)
        {
            Func<LevelUpOutcome, CancellationToken, Task<CharacterSaveResult>> persist =
                IrreversibleOutcomePersistStep.StartWrite<LevelUpOutcome>(persistence, charId, IrreversibleOutcomeTrigger.LevelUp);
            return _coordinator.Begin<LevelUpOutcome, CharacterSaveResult>(
                CLIENT_ID,
                charId,
                () => true,
                () => { },
                () => ApplyLevelUps(rig, levelUps),
                (outcome, token) => _lastTask = persist(outcome, token),
                IrreversibleOutcomePersistStep.IsSuccess,
                outcome => _deliveries++,
                () =>
                {
                    _reverts++;
                    rig.Stats.SetBaseStat(rig.Entity, StatID.Level, _capturedLevel);
                    rig.Stats.SetBaseStat(rig.Entity, StatID.Experience, _capturedExperience);
                },
                (id, reason) => _disconnects.Add((id, reason)),
                (id, ttl) => _preserves.Add((id, ttl)));
        }

        // A begin that did not start the write leaves _lastTask null: fail with a clear message, not a NullReferenceException.
        private void WaitForLastTask(string what)
        {
            Assert.IsNotNull(_lastTask, "The coordinator did not start the persist step.");
            WaitUntil(() => _lastTask.IsCompleted, what);
        }

        private void AssertNoFailureEffects()
        {
            Assert.AreEqual(0, _reverts);
            Assert.AreEqual(0, _disconnects.Count);
            Assert.AreEqual(0, _preserves.Count);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        private void AssertFailureEffectsOnce(Rig rig, CharacterID charId)
        {
            Assert.AreEqual(0, _deliveries, "The outcome must not be delivered.");
            Assert.AreEqual(1, _reverts);
            Assert.AreEqual(START_LEVEL, rig.Stats.GetBaseStat(rig.Entity, StatID.Level));
            Assert.AreEqual(START_EXPERIENCE, rig.Stats.GetBaseStat(rig.Entity, StatID.Experience));
            Assert.AreEqual(1, _disconnects.Count);
            Assert.AreEqual(CLIENT_ID, _disconnects[0].ClientId);
            Assert.AreEqual(DisconnectReason.Other, _disconnects[0].Reason);
            Assert.AreEqual(1, _preserves.Count);
            Assert.AreEqual(CLIENT_ID, _preserves[0].ClientId);
            Assert.AreEqual(EXPECTED_TTL_SECONDS, _preserves[0].Ttl);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
            Assert.IsFalse(_gate.IsHeld(charId), "The gate must be open after the failure was handled.");
            Assert.AreEqual(0, _queue.InFlightCount, "No write may still be tracked.");
        }

        // Loads the seed into the rig and gives the live character the test Experience (as the Part A SeedAndLoad).
        private CharacterRecord SeedLoadWithExperience(Rig rig, ICharacterRecordStore store, uint rawId)
        {
            CharacterRecord loaded = SeedAndLoad(rig, store, rawId);
            rig.Stats.SetBaseStat(rig.Entity, StatID.Experience, START_EXPERIENCE);
            Assert.AreEqual(START_LEVEL, rig.Stats.GetBaseStat(rig.Entity, StatID.Level));
            return loaded;
        }

        // ================================================================ Part A curated

        /// <summary>
        /// AC-CP-13 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_IrreversibleOutcome_tests.cs</c>,
        /// <c>Test_AcCp13_HeldWrite_NoDeliveryUntilReleasedThenDeliveredOnNextDrain</c>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp13_HeldWrite_NoDeliveryUntilReleasedThenDeliveredOnNextDrain()
        {
            var id = new CharacterID(ID_AC13);
            var store = new HoldingStore(NewStore());
            Rig rig = BuildRig(store);
            SeedLoadWithExperience(rig, store, ID_AC13);
            Action release = store.HoldNext();

            try
            {
                IrreversibleOutcomeBeginResult result = BeginLevelUp(rig, rig.Service, id);
                DrainNextTick();
                DrainNextTick();
                DrainNextTick();

                Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
                Assert.AreEqual(0, _deliveries, "No outcome while the write is held.");
                Assert.IsTrue(_gate.IsHeld(id));
                Assert.AreEqual(1, _queue.InFlightCount);
                AssertNoFailureEffects();
            }
            finally
            {
                release(); // a failed assertion must not leak the held call
            }

            WaitForLastTask("the held write to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.Success, _lastTask.Result);
            Assert.AreEqual(1, _deliveries);
            Assert.IsFalse(_gate.IsHeld(id));
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.AreEqual(START_LEVEL + 1, Stored(store, ID_AC13).Level);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// AC-CP-14 against PostgreSQL (the DatabaseError variant). Part A counterpart:
        /// <c>CharacterPersistence_IrreversibleOutcome_tests.cs</c>, <c>Test_AcCp14_DatabaseError_FromLevel10_RevertsDisconnectsAlertsPreservesOnce</c>.
        /// The failure comes from <see cref="ThrowAfterIssuedUpdateStore"/>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp14_DatabaseError_FromLevel10_RevertsDisconnectsAlertsPreservesOnce()
        {
            var id = new CharacterID(ID_AC14);
            ICharacterRecordStore real = NewStore();
            Rig rig = BuildRig(new ThrowAfterIssuedUpdateStore(real));
            CharacterRecord loaded = SeedLoadWithExperience(rig, real, ID_AC14);
            LogAssert.Expect(LogType.Error, new Regex(SERVICE_FAILED_LOG));
            LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + CAUSE_NON_SUCCESS));

            BeginLevelUp(rig, rig.Service, id);
            WaitForLastTask("the failed write to complete");
            Assert.IsTrue(_gate.IsHeld(id), "The gate stays closed until the result is handled on a tick.");
            DrainNextTick();
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.DatabaseError, _lastTask.Result);
            AssertFailureEffectsOnce(rig, id);
            CharacterRecord stored = Stored(real, ID_AC14);
            Assert.AreEqual(START_LEVEL, stored.Level);
            Assert.AreEqual(SEED_EXPERIENCE, stored.Experience);
            Assert.AreEqual((long)loaded.SaveVersion, RawVersion(ID_AC14), "the issued UPDATE wrote nothing");
        }

        /// <summary>
        /// AC-CP-16 against PostgreSQL: the TTL-expiry save. Part A counterpart: <c>CharacterPersistence_SessionEnd_tests.cs</c>,
        /// <c>Test_AcCp16_TtlExpiry_TickBeforeExpiryStartsNothing_ExpiryTickSavesOnceAndReleasesOnceAfterDrain</c>. Differs: that test
        /// drives <c>SessionEndCoordinator</c> (state machine, release steps), which Story 016 does not need; this test calls
        /// <c>SaveSession(SessionTTLExpiry)</c> directly and asserts the GDD row (LastSavedAtUtc updated, SaveVersion incremented). Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp16_TtlExpirySave_LastSavedUpdatedAndVersionIncremented()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            CharacterRecord loaded = SeedAndLoad(rig, store, ID_AC16);
            PgTestDatabase.Execute(_connectionString, "UPDATE character_records SET last_saved_at_utc = last_saved_at_utc - " + BACKDATE_INTERVAL_SQL + " WHERE character_id = " + ID_AC16);
            CharacterRecord backdated = Stored(store, ID_AC16);

            CharacterSaveResult result = Complete(rig.Service.SaveSession(new CharacterID(ID_AC16), SessionEndReason.SessionTTLExpiry, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord stored = Stored(store, ID_AC16);
            Assert.Greater(stored.LastSavedAtUtc, backdated.LastSavedAtUtc);
            Assert.AreEqual(loaded.SaveVersion + 1u, stored.SaveVersion);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// AC-CP-17 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_IrreversibleOutcome_tests.cs</c>,
        /// <c>Test_AcCp17_TwoLevelUpsInOneOutcome_OneLevelUpWriteAndStoredLevelIsTheFinalOne</c>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp17_TwoLevelUpsInOneOutcome_OneWriteAndStoredLevelIsTheFinalOne()
        {
            var id = new CharacterID(ID_AC17);
            var counting = new CountingStore(NewStore());
            Rig rig = BuildRig(counting);
            SeedLoadWithExperience(rig, counting, ID_AC17);

            BeginLevelUp(rig, rig.Service, id, levelUps: 2);
            WaitForLastTask("the write to complete");
            DrainNextTick();

            Assert.AreEqual(1, counting.UpdateCount, "one SaveIrreversibleOutcome write");
            Assert.AreEqual(CharacterSaveResult.Success, _lastTask.Result);
            CharacterRecord stored = Stored(counting, ID_AC17);
            Assert.AreEqual(START_LEVEL + 2, stored.Level);
            Assert.AreEqual(POST_LEVEL_UP_EXPERIENCE, stored.Experience);
            Assert.AreEqual(1, _deliveries);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// AC-CP-18 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_SaveSession_tests.cs</c>,
        /// <c>SaveSession_Success_StoredVersionPlusOneNewSavedTimeCreatedTimeKept</c>. The store clock is the database's NOW(). Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp18_SaveSessionSuccess_StoredVersionPlusOneCreatedTimeKept()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            CharacterRecord loaded = SeedAndLoad(rig, store, ID_AC18);
            PgTestDatabase.Execute(_connectionString, "UPDATE character_records SET last_saved_at_utc = last_saved_at_utc - " + BACKDATE_INTERVAL_SQL + ", "
                + "created_at_utc = created_at_utc - " + BACKDATE_INTERVAL_SQL + " WHERE character_id = " + ID_AC18);
            CharacterRecord backdated = Stored(store, ID_AC18);

            CharacterSaveResult result = Complete(rig.Service.SaveSession(new CharacterID(ID_AC18), SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord r = Stored(store, ID_AC18);
            Assert.AreEqual(loaded.SaveVersion + 1u, r.SaveVersion);
            Assert.Greater(r.LastSavedAtUtc, backdated.LastSavedAtUtc);
            Assert.AreEqual(backdated.CreatedAtUtc, r.CreatedAtUtc);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// AC-CP-20 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_SaveSession_tests.cs</c>,
        /// <c>SaveSession_SecondCallWhileFirstHeld_FirstSuccessSecondConflictStoredRecordIsTheFirsts</c>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp20_SecondSaveSessionWhileFirstHeld_FirstSuccessSecondConflictStoredRecordIsTheFirsts()
        {
            var id = new CharacterID(ID_AC20);
            var holding = new HoldingStore(NewStore());
            Rig rig = BuildRig(holding);
            CharacterRecord loaded = SeedAndLoad(rig, holding, ID_AC20);
            uint n = loaded.SaveVersion;
            rig.Stats.SetCurrentHP(rig.Entity, AC20_FIRST_HP);
            Action release = holding.HoldNext();
            Task<CharacterSaveResult> first;
            Task<CharacterSaveResult> second;
            try
            {
                first = rig.Service.SaveSession(id, SessionEndReason.CleanLogout, CancellationToken.None);
                rig.Stats.SetCurrentHP(rig.Entity, AC20_SECOND_HP);
                second = rig.Service.SaveSession(id, SessionEndReason.CleanLogout, CancellationToken.None);
                Assert.IsFalse(first.IsCompleted);
                Assert.IsFalse(second.IsCompleted);
                LogAssert.Expect(LogType.Warning, new Regex(SESSION_CONFLICT_WARNING));
            }
            finally
            {
                release(); // a failed assertion must not leak the held call
            }

            Assert.AreEqual(CharacterSaveResult.Success, Complete(first));
            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, Complete(second));
            CharacterRecord r = Stored(holding, ID_AC20);
            Assert.AreEqual(AC20_FIRST_HP, r.CurrentHP);
            Assert.AreEqual(n + 1u, r.SaveVersion);
            Assert.AreEqual(2, holding.UpdateCalls);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// AC-CP-22 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_LoadCore_tests.cs</c>,
        /// <c>LoadCharacter_FreshStub_LoadsAndAppliesThroughSamePath</c> (MaxHP 400, AttackPower 30), plus the GDD row's gold 0 and
        /// "record exists". Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp22_CreateStub_RecordExistsAndLoadsWithStubStats()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            var id = new CharacterID(ID_AC22);

            Complete(rig.Service.CreateStub(ACCOUNT_A, id, WARRIOR_CLASS_TYPE, CancellationToken.None));

            Assert.IsNotNull(Stored(store, ID_AC22), "the stub row exists");
            CharacterLoadResult result = LoadAndApply(rig, id);
            Assert.AreEqual(STUB_MAX_HP, rig.Stats.GetBaseStat(rig.Entity, StatID.MaxHP));
            Assert.AreEqual(STUB_ATTACK_POWER, rig.Stats.GetBaseStat(rig.Entity, StatID.AttackPower));
            Assert.AreEqual(0u, result.Record.GoldBalance);
            Assert.IsTrue(rig.CurrencySystem.TryGetState(id, out uint balance, out _));
            Assert.AreEqual(0u, balance);
        }

        /// <summary>
        /// AC-CP-23 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_LoadCore_tests.cs</c>,
        /// <c>LoadCharacter_FreshStub_LoadsAndAppliesThroughSamePath</c> (Success, <c>ZoneID.Invalid</c>, apply, MaxHP 400, AttackPower 30). Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp23_FreshStub_LoadsAndAppliesThroughSamePath()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            var id = new CharacterID(ID_AC23);
            Complete(rig.Service.CreateStub(ACCOUNT_A, id, WARRIOR_CLASS_TYPE, CancellationToken.None));

            CharacterLoadResult result = Complete(rig.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(ZoneID.Invalid, result.LastZoneID);
            rig.Service.ApplyLoadedRecord(rig.Entity, result.Record);

            Assert.AreEqual(STUB_MAX_HP, rig.Stats.GetBaseStat(rig.Entity, StatID.MaxHP));
            Assert.AreEqual(STUB_ATTACK_POWER, rig.Stats.GetBaseStat(rig.Entity, StatID.AttackPower));
        }

        /// <summary>
        /// AC-CP-24 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_LoadEquipment_tests.cs</c>,
        /// <c>test_apply_hp_650_clamps_to_500_after_item_miss_lowers_max_hp_AC_CP_24</c> (real Equipment System; the unknown chest item is
        /// dropped; effective MaxHP 500; HP 500). The record goes through the database and <c>LoadCharacter</c>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp24_Hp650ClampsTo500_AfterItemMissLowersMaxHp()
        {
            ICharacterRecordStore store = NewStore();
            var id = new CharacterID(ID_AC24);
            Rig rig = BuildRig(store, id);
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, id, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.MaxHP = BASE_MAX_HP;
            r.CurrentHP = SEED_CURRENT_HP;
            r.GearSlots[(int)GearSlot.Chest] = new GearSlotRecord(UNKNOWN_ITEM, 0);
            Assert.IsTrue(Complete(store.InsertAsync(r, CancellationToken.None)));
            CharacterLoadResult loaded = Complete(rig.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, loaded.Code);
            LogAssert.Expect(LogType.Error, new Regex(@"RestoreSlots: .*slot " + GearSlot.Chest + " item " + Regex.Escape(UNKNOWN_ITEM.ToString())));

            rig.Service.ApplyLoadedRecord(rig.Entity, loaded.Record);

            Assert.AreEqual(BASE_MAX_HP, rig.Stats.GetEffectiveStat(rig.Entity, StatID.MaxHP));
            Assert.AreEqual((float)BASE_MAX_HP, rig.Stats.GetCurrentHP(rig.Entity));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// AC-CP-25 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_LoadRestore_tests.cs</c>,
        /// <c>ApplyLoadedRecord_SavedHp_ClampedToEffectiveMaxHp</c> case (300f, 400, 300f). The record goes through the database. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp25_Hp300WithMaxHp400_RestoresUnchanged()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            var id = new CharacterID(ID_AC25);
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, id, WARRIOR_CLASS_TYPE);
            r.MaxHP = STUB_MAX_HP;
            r.CurrentHP = AC25_CURRENT_HP;
            Assert.IsTrue(Complete(store.InsertAsync(r, CancellationToken.None)));

            CharacterLoadResult loaded = Complete(rig.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            CharacterLoadCode code = rig.Service.ApplyLoadedRecord(rig.Entity, loaded.Record);

            Assert.AreEqual(CharacterLoadCode.Success, code);
            Assert.AreEqual(STUB_MAX_HP, rig.Stats.GetEffectiveStat(rig.Entity, StatID.MaxHP));
            Assert.AreEqual(AC25_CURRENT_HP, rig.Stats.GetCurrentHP(rig.Entity));
        }

        /// <summary>
        /// AC-CP-30 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_SaveSession_tests.cs</c>,
        /// <c>SaveSession_HpOverrideZeroWithHp650_WritesZeroHpAndUntouchedMp</c>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp30_HpOverrideZeroWithHp650_WritesZeroHpAndUntouchedMp()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            CharacterRecord loaded = SeedAndLoad(rig, store, ID_AC30);
            rig.Stats.SetCurrentMP(rig.Entity, AC30_CURRENT_MP);

            Complete(rig.Service.SaveSession(new CharacterID(ID_AC30), SessionEndReason.ZoneClosed, 0f, CancellationToken.None));

            CharacterRecord r = Stored(store, ID_AC30);
            Assert.AreEqual(0f, r.CurrentHP);
            Assert.AreEqual(AC30_CURRENT_MP, r.CurrentMP);
            Assert.AreEqual(loaded.SaveVersion + 1u, r.SaveVersion);
            Assert.AreEqual(SEED_CURRENT_HP, rig.Stats.GetCurrentHP(rig.Entity), "The live HP is not changed by the save.");
        }

        /// <summary>
        /// AC-CP-31 against PostgreSQL. Part A counterpart: <c>CharacterPersistence_SaveSession_tests.cs</c>,
        /// <c>SaveSession_NoOverrideWithHp350_WritesHp350</c>. Story 016.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp31_NoOverrideWithHp350_WritesHp350()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            SeedAndLoad(rig, store, ID_AC31);
            rig.Stats.SetCurrentHP(rig.Entity, AC31_CURRENT_HP);

            Complete(rig.Service.SaveSession(new CharacterID(ID_AC31), SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(AC31_CURRENT_HP, Stored(store, ID_AC31).CurrentHP);
        }

        // ================================================================ AC-CP-15 (real)

        /// <summary>
        /// AC-CP-15 (real), Story 016: save on a clean logout, then load with a new service, a new Npgsql store (new data source) and
        /// new in-memory systems over the same database. Mirrors <c>CharacterPersistence_SaveSession_tests.cs</c>
        /// <c>SaveSession_ThenLoadIntoFreshSystems_EveryPersistedFieldMatches</c>; adds all 7 gear slots, <c>CurrentHP/MP</c> and a
        /// <c>CreatedAtUtc</c> comparison against the value before logout.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp15_CleanLogoutThenFreshProcessLoad_EveryPersistedFieldMatches()
        {
            var id = new CharacterID(ID_AC15);
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            CharacterRecord loaded = SeedAndLoad(rig, store, ID_AC15);
            uint n = loaded.SaveVersion;
            // Pre-logout values, each different from the seed, so a field the round trip drops cannot pass by accident.
            var ints = new (StatID Stat, int Value)[]
            {
                (StatID.Level, 11), (StatID.Experience, 37), (StatID.Strength, 55), (StatID.Dexterity, 44),
                (StatID.Vitality, 33), (StatID.Intelligence, 22), (StatID.MaxHP, 900), (StatID.MaxMP, 300),
                (StatID.AttackPower, 77), (StatID.Defense, 66), (StatID.MagicDefense, 58),
            };
            var floats = new (StatID Stat, float Value)[]
            {
                (StatID.CritChance, 0.33f), (StatID.CritMultiplier, 2.5f), (StatID.AttackRange, 3.5f),
                (StatID.AttackSpeedMultiplier, 1.2f), (StatID.MovementSpeed, 6.5f),
            };
            const int heldPoints = 3;
            const float currentHp = 123f;
            const float currentMp = 45f;
            const uint goldBalance = 4321u;
            const uint goldVersion = 17u;
            var lastZone = new ZoneID(42u);
            const int bagSlotA = 2;
            const int bagSlotB = 11;
            var bagItemA = new ItemID(5u);
            var bagItemB = new ItemID(12u);
            const int bagCountA = 4;
            const int bagCountB = 1;
            const byte bagLevelB = 3;
            // Gear changed before logout (the seed has slots 0 and 4): exported through the fake equipment service.
            var gear = new SavedGearSlot[CharacterPersistenceConstants.GEAR_SLOT_COUNT];
            for (int slot = 0; slot < gear.Length; slot++)
            {
                gear[slot] = SavedGearSlot.Empty;
            }

            gear[1] = new SavedGearSlot(new ItemID(702u), 5);
            gear[6] = new SavedGearSlot(new ItemID(703u), 2);

            IronGrind.CharacterStats.CharacterStats s = rig.Stats;
            EntityID e = rig.Entity;
            foreach ((StatID stat, int value) in ints)
            {
                s.SetBaseStat(e, stat, value);
            }

            foreach ((StatID stat, float value) in floats)
            {
                s.SetBaseStatFloat(e, stat, value);
            }

            rig.Leveling.RestoreLevelingState(e, new LevelingStateSnapshot(heldPoints));
            s.SetCurrentHP(e, currentHp);
            s.SetCurrentMP(e, currentMp);
            rig.CurrencySystem.RestoreCharacter(id, goldBalance, goldVersion);
            rig.Zones.Zone = lastZone;
            rig.FakeEquipment.ExportOverride = gear;
            rig.Inventory.ExportResult = new InventorySnapshot(new List<InventorySnapshotEntry>
            {
                new InventorySnapshotEntry(bagSlotA, bagItemA.RawValue, bagCountA, 0),
                new InventorySnapshotEntry(bagSlotB, bagItemB.RawValue, bagCountB, bagLevelB),
            });
            Assert.AreEqual(CharacterSaveResult.Success, Complete(rig.Service.SaveSession(id, SessionEndReason.CleanLogout, CancellationToken.None)));

            // The "fresh process state": nothing of the first rig, service, store or data source is reused.
            DisposeAllDataSources();
            ICharacterRecordStore freshStore = NewStore();
            Rig fresh = BuildRig(freshStore);
            CharacterLoadResult result = Complete(fresh.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, fresh.Service.ApplyLoadedRecord(fresh.Entity, result.Record));

            IronGrind.CharacterStats.CharacterStats f = fresh.Stats;
            EntityID fe = fresh.Entity;
            Assert.AreEqual(lastZone, result.LastZoneID);
            Assert.AreEqual(n + 1u, result.Record.SaveVersion);
            Assert.AreEqual(loaded.CreatedAtUtc, result.Record.CreatedAtUtc, "CreatedAtUtc unchanged");
            foreach ((StatID stat, int value) in ints)
            {
                Assert.AreEqual(value, f.GetBaseStat(fe, stat), stat.ToString());
            }

            foreach ((StatID stat, float value) in floats)
            {
                Assert.AreEqual(value, f.GetBaseStatFloat(fe, stat), FLOAT_TOLERANCE, stat.ToString());
            }

            Assert.AreEqual(heldPoints, fresh.Leveling.GetHeldFreePoints(fe));
            Assert.AreEqual(currentHp, f.GetCurrentHP(fe), FLOAT_TOLERANCE);
            Assert.AreEqual(currentMp, f.GetCurrentMP(fe), FLOAT_TOLERANCE);
            Assert.IsTrue(fresh.CurrencySystem.TryGetState(id, out uint balance, out uint version));
            Assert.AreEqual(goldBalance, balance);
            Assert.AreEqual(goldVersion, version);
            for (int slot = 0; slot < CharacterPersistenceConstants.GEAR_SLOT_COUNT; slot++)
            {
                Assert.AreEqual(gear[slot].ItemId, result.Record.GearSlots[slot].ItemId, "gear item, slot " + slot);
                Assert.AreEqual(gear[slot].EnhancementLevel, result.Record.GearSlots[slot].EnhancementLevel, "gear level, slot " + slot);
            }

            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                bool first = i == bagSlotA;
                bool second = i == bagSlotB;
                Assert.AreEqual(first ? bagItemA : second ? bagItemB : new ItemID(0u), result.Record.InventorySlots[i], "inventory item, slot " + i);
                Assert.AreEqual(first ? bagCountA : second ? bagCountB : 0, result.Record.InventoryItemCounts[i], "inventory count, slot " + i);
                Assert.AreEqual(second ? bagLevelB : (byte)0, result.Record.InventoryEnhancementLevels[i], "inventory level, slot " + i);
            }

            Assert.AreEqual(2, fresh.Inventory.ImportCalls[0].Snapshot.Slots.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ================================================================ AC-CP-19 (real)

        /// <summary>
        /// AC-CP-19 (real), Story 016: a second connection, opened by the test, bumps <c>save_version</c> before
        /// <c>SaveIrreversibleOutcome</c> writes. Related: Story 014 <c>CharacterPersistence_NpgsqlRecordStore_tests.cs</c>
        /// (store-level, no service) and Part A <c>CharacterPersistence_SaveIrreversible_tests.cs</c> (conflict, one critical log).
        /// </summary>
        [Test]
        public void RealDatabase_AcCp19_SecondConnectionBumpsVersion_SaveIrreversibleOutcomeConflictsWithOneCriticalLog()
        {
            var id = new CharacterID(ID_AC19);
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            CharacterRecord loaded = SeedAndLoad(rig, store, ID_AC19);
            PgTestDatabase.Execute(_connectionString, "UPDATE character_records SET save_version = save_version + 1 WHERE character_id = " + ID_AC19);
            rig.Stats.SetBaseStat(rig.Entity, StatID.Level, START_LEVEL + 1); // what the write would store if it went through
            LogAssert.Expect(LogType.Error, new Regex(SERVICE_CONFLICT_LOG));

            CharacterSaveResult result = Complete(rig.Service.SaveIrreversibleOutcome(id, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, result);
            Assert.AreEqual((long)loaded.SaveVersion + 1L, RawVersion(ID_AC19), "the row keeps the value the second connection gave it");
            Assert.AreEqual(loaded.Level, Stored(store, ID_AC19).Level, "the conflicted write did not overwrite the row");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Collects every log message (from any thread) while it is alive, and keeps Unity from failing the test on them; the test
        /// asserts on <see cref="Errors"/> instead. Dispose unsubscribes and restores <c>LogAssert.ignoreFailingMessages</c>.
        /// </summary>
        private sealed class LogCapture : IDisposable
        {
            private readonly object _lock = new object();
            private readonly List<(LogType Type, string Message)> _entries = new List<(LogType, string)>();

            public LogCapture()
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceivedThreaded += OnLog;
            }

            /// <summary>The Error, Exception and Assert messages received so far.</summary>
            public List<string> Errors
            {
                get
                {
                    lock (_lock)
                    {
                        return _entries.Where(x => x.Type != LogType.Log && x.Type != LogType.Warning).Select(x => x.Type + ": " + x.Message).ToList();
                    }
                }
            }

            public void Dispose()
            {
                Application.logMessageReceivedThreaded -= OnLog;
                LogAssert.ignoreFailingMessages = false;
            }

            private void OnLog(string message, string stackTrace, LogType type)
            {
                lock (_lock)
                {
                    _entries.Add((type, message));
                }
            }
        }

        // ================================================================ gates

        /// <summary>
        /// AC-CP-21 concurrency gate, Story 016 / ADR-006: 50 <c>SaveIrreversibleOutcome</c> calls for one character, started without
        /// awaiting in between, then <c>Task.WhenAll</c>. None faults; each result is Success or ConcurrencyConflict; stored
        /// <c>save_version</c> = start + successes; no two recorded update intervals overlap. Part A (two-call, in-memory form):
        /// Story 006's write-queue suite. Each conflict logs one Critical error; a <see cref="LogCapture"/> collects them and the test
        /// asserts that every error is a conflict log and that there is one per conflicted call. At least one call must succeed.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp21_FiftyConcurrentSaveIrreversibleOutcome_NoOverlapNoFaultVersionIsStartPlusSuccesses()
        {
            var id = new CharacterID(ID_AC21);
            var interval = new IntervalStore(NewStore());
            Rig rig = BuildRig(interval);
            CharacterRecord loaded = SeedAndLoad(rig, interval, ID_AC21);

            var tasks = new Task<CharacterSaveResult>[CONCURRENT_CALL_COUNT];
            bool finished;
            List<string> errors;
            using (var capture = new LogCapture())
            {
                for (int i = 0; i < CONCURRENT_CALL_COUNT; i++)
                {
                    tasks[i] = rig.Service.SaveIrreversibleOutcome(id, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None);
                }

                try
                {
                    finished = Task.WaitAll(tasks, CONCURRENT_WAIT_MS);
                }
                catch (AggregateException)
                {
                    finished = true; // a fault is reported per task below
                }

                errors = capture.Errors;
            }

            Assert.IsTrue(finished, "The 50 calls did not all end within " + CONCURRENT_WAIT_MS + " ms.");
            int successes = 0;
            for (int i = 0; i < CONCURRENT_CALL_COUNT; i++)
            {
                Assert.IsFalse(tasks[i].IsFaulted, "call " + i + " faulted: " + tasks[i].Exception);
                Assert.IsFalse(tasks[i].IsCanceled, "call " + i + " was cancelled");
                CharacterSaveResult r = tasks[i].Result;
                Assert.IsTrue(r == CharacterSaveResult.Success || r == CharacterSaveResult.ConcurrencyConflict, "call " + i + " returned " + r);
                if (r == CharacterSaveResult.Success)
                {
                    successes++;
                }
            }

            int conflicts = CONCURRENT_CALL_COUNT - successes;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "CONCURRENCY_GATE n={0} successes={1} conflicts={2}", CONCURRENT_CALL_COUNT, successes, conflicts));
            Assert.GreaterOrEqual(successes, 1, "at least the first call must win, or the gate proves no write reached the database");
            var conflictLog = new Regex(SERVICE_CONFLICT_LOG);
            foreach (string error in errors)
            {
                StringAssert.IsMatch(SERVICE_CONFLICT_LOG, error, "only conflict logs are expected");
            }

            Assert.AreEqual(conflicts, errors.Count(x => conflictLog.IsMatch(x)), "one Critical log per conflicted call");
            Assert.AreEqual((long)loaded.SaveVersion + successes, RawVersion(ID_AC21), "save_version = start + successes");
            List<(long Start, long End)> intervals = interval.Intervals.OrderBy(x => x.Start).ToList();
            Assert.AreEqual(CONCURRENT_CALL_COUNT, intervals.Count, "one recorded update per call");
            for (int i = 1; i < intervals.Count; i++)
            {
                Assert.GreaterOrEqual(intervals[i].Start, intervals[i - 1].End, "update " + i + " started before update " + (i - 1) + " ended");
            }
        }

        /// <summary>
        /// Write-latency gate, Story 016 / ADR-006: 100 consecutive awaited <c>SaveIrreversibleOutcome</c> calls; the Stopwatch brackets
        /// the inner store's <c>UpdateAsync</c> only. Logs one <c>LATENCY_GATE</c> line and asserts P95 at or below the 50 ms warning limit.
        /// </summary>
        [Test]
        public void RealDatabase_LatencyGate_HundredConsecutiveSaves_P95AtMostFiftyMs()
        {
            var id = new CharacterID(ID_LATENCY);
            var interval = new IntervalStore(NewStore());
            Rig rig = BuildRig(interval);
            SeedAndLoad(rig, interval, ID_LATENCY);

            for (int i = 0; i < LATENCY_CALL_COUNT; i++)
            {
                Assert.AreEqual(CharacterSaveResult.Success,
                    Complete(rig.Service.SaveIrreversibleOutcome(id, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None)), "save " + i);
            }

            List<double> ms = interval.Intervals.Select(x => (x.End - x.Start) * 1000.0 / Stopwatch.Frequency).OrderBy(x => x).ToList();
            Assert.AreEqual(LATENCY_CALL_COUNT, ms.Count);
            double p50 = Percentile(ms, 0.50);
            double p95 = Percentile(ms, 0.95);
            double p99 = Percentile(ms, 0.99);
            double max = ms[ms.Count - 1];
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture, "LATENCY_GATE n={0} p50={1:F2} p95={2:F2} p99={3:F2} max={4:F2} ms", ms.Count, p50, p95, p99, max));

            Assert.LessOrEqual(p95, LATENCY_P95_LIMIT_MS, "P95 of the store update must stay within the ADR-006 gate");
        }

        // Nearest-rank percentile of an ascending list.
        private static double Percentile(List<double> ascending, double fraction)
        {
            int rank = (int)Math.Ceiling(fraction * ascending.Count);
            return ascending[Math.Max(1, Math.Min(rank, ascending.Count)) - 1];
        }

        // ================================================================ AC-CP-26

        /// <summary>
        /// AC-CP-26 (end to end), Story 016: a +9 weapon equipped through a real <c>EquipmentService</c> is saved with
        /// <c>SaveSession</c>; the in-memory systems, the service, the store and the data source are discarded and built again; the
        /// load puts the weapon in the same gear slot at +9.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp26_PlusNineWeaponSavedThenEverythingRebuilt_LoadsInSameSlotAtPlusNine()
        {
            var id = new CharacterID(ID_AC26);
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store, id);
            AddItem(rig, SWORD, GearSlot.Weapon, GearTier.Iron, StatID.AttackPower, 12f);
            Complete(rig.Service.CreateStub(ACCOUNT_A, id, WARRIOR_CLASS_TYPE, CancellationToken.None));
            LoadAndApply(rig, id);
            MoveItemInResult placed = rig.EquipRig.Inventory.MoveItemIn(id, SWORD, PLUS_NINE);
            Assert.IsTrue(placed.Success, "test setup: the +9 sword is in the bag");
            rig.EquipRig.Equipment.Equip(id, GearSlot.Weapon, SWORD, placed.SlotIndex);
            Assert.AreEqual(SWORD, rig.EquipRig.Equipment.GetEquipmentSlotState(id, GearSlot.Weapon).ItemId, "test setup: equipped");
            Assert.AreEqual(PLUS_NINE, rig.EquipRig.Equipment.GetSlotEnhancementLevel(id, GearSlot.Weapon));

            Assert.AreEqual(CharacterSaveResult.Success, Complete(rig.Service.SaveSession(id, SessionEndReason.CleanLogout, CancellationToken.None)));

            DisposeAllDataSources();
            ICharacterRecordStore freshStore = NewStore();
            Rig fresh = BuildRig(freshStore, id);
            AddItem(fresh, SWORD, GearSlot.Weapon, GearTier.Iron, StatID.AttackPower, 12f);
            CharacterLoadResult result = LoadAndApply(fresh, id);

            Assert.AreEqual(SWORD, result.Record.GearSlots[(int)GearSlot.Weapon].ItemId, "stored weapon slot");
            Assert.AreEqual(PLUS_NINE, result.Record.GearSlots[(int)GearSlot.Weapon].EnhancementLevel);
            Assert.AreEqual(SWORD, fresh.EquipRig.Equipment.GetEquipmentSlotState(id, GearSlot.Weapon).ItemId, "live weapon slot");
            Assert.AreEqual(PLUS_NINE, fresh.EquipRig.Equipment.GetSlotEnhancementLevel(id, GearSlot.Weapon));
            LogAssert.NoUnexpectedReceived();
        }

        // ================================================================ AC-CP-5 (real)

        /// <summary>
        /// AC-CP-5 (real), Story 016: a second connection holds <c>LOCK TABLE character_records IN ACCESS EXCLUSIVE MODE</c> in an open
        /// transaction (a row lock does not block the store's plain SELECT in PostgreSQL). The caller's
        /// <c>CHARACTER_LOAD_TIMEOUT_SECONDS</c> token ends the load (CR-CP-8), which returns <c>DatabaseError</c> after at least 10 s
        /// and within 12 s, under <c>CONNECTING_TIMEOUT_SECONDS</c> (30 s). The store's 5 s command timeout does not end the blocked
        /// async fetch (observed 2026-10-28, TD-084). The elapsed time is logged. Logs are collected by a <see cref="LogCapture"/>: none may
        /// be an exception or an assert; any error lines are written to the log for the evidence document.
        /// </summary>
        [Test]
        public void RealDatabase_AcCp5_LoadWhileTableLockedBySecondConnection_DatabaseErrorAtTheLoadTimeout()
        {
            ICharacterRecordStore store = NewStore();
            Rig rig = BuildRig(store);
            Assert.IsTrue(Complete(store.InsertAsync(MakeSeed(ID_AC5), CancellationToken.None)));
            DbConnection lockConnection = NpgsqlRecordStoreTestAccess.OpenConnection(_connectionString);
            CharacterLoadResult result;
            double elapsedMs;
            List<string> errors;
            try
            {
                PgTestDatabase.Execute(lockConnection, "BEGIN");
                PgTestDatabase.Execute(lockConnection, "LOCK TABLE character_records IN ACCESS EXCLUSIVE MODE");
                using (var capture = new LogCapture())
                {
                    // The watch starts before the token's timer, so the timer cannot appear to fire early.
                    var watch = Stopwatch.StartNew();
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CharacterPersistenceConstants.CHARACTER_LOAD_TIMEOUT_SECONDS)))
                    {
                        Task<CharacterLoadResult> task = rig.Service.LoadCharacter(new CharacterID(ID_AC5), ACCOUNT_A, cts.Token);
                        result = Complete(task);
                        elapsedMs = watch.Elapsed.TotalMilliseconds;
                    }

                    errors = capture.Errors;
                }
            }
            finally
            {
                try
                {
                    PgTestDatabase.Execute(lockConnection, "ROLLBACK");
                }
                finally
                {
                    lockConnection.Dispose();
                }
            }

            Debug.Log(string.Format(CultureInfo.InvariantCulture, "AC-CP-5 locked-table load: code={0} elapsed={1:F0} ms errors={2} [{3}]",
                result.Code, elapsedMs, errors.Count, string.Join(" | ", errors)));
            Assert.AreEqual(CharacterLoadCode.DatabaseError, result.Code);
            Assert.IsFalse(errors.Any(x => x.StartsWith(LogType.Exception + ":") || x.StartsWith(LogType.Assert + ":")), "no exception or assert logged");
            Assert.GreaterOrEqual(elapsedMs, LOAD_LOCKED_MIN_MS - LOAD_LOCKED_TOLERANCE_MS, "the load timeout, not an earlier store fault, ends the load");
            Assert.Less(elapsedMs, LOAD_LOCKED_LIMIT_MS, "DatabaseError must arrive within 12 s of the call, under CONNECTING_TIMEOUT_SECONDS");
        }
    }
}
