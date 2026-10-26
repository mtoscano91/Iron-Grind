using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 002 (moved into a shared contract fixture by Story 014): the record store seam,
    /// CharacterStubDefaults and CreateStub. The tests here hold for every <see cref="ICharacterRecordStore"/>; a subclass
    /// supplies the store. Tests that assert the injected clock's exact value, use the in-memory store's fault controls,
    /// or rely on the in-memory store's uint wrap-around live in <see cref="CharacterPersistence_RecordStore_Tests"/>.
    /// Calls are awaited with a bounded wait, so a store that completes asynchronously passes too.
    /// </summary>
    internal abstract class CharacterRecordStoreContractTests
    {
        protected static readonly DateTime T1 = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
        protected static readonly DateTime T2 = new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc);
        // Test-only ids (9_000_000 and up): the Npgsql contract run uses the dev database and must never touch real low ids.
        protected const uint CHAR_A_RAW = 9_000_101u;
        protected const uint CHAR_B_RAW = 9_000_102u;
        protected static readonly CharacterID CHAR_A = new CharacterID(CHAR_A_RAW);
        protected static readonly CharacterID CHAR_B = new CharacterID(CHAR_B_RAW);
        protected static readonly AccountID ACCOUNT_A = new AccountID(7u);
        protected const uint VERSION_FOUR = 4u;
        protected const uint VERSION_NINE = 9u;
        protected static readonly DateTime T3 = new DateTime(2026, 10, 12, 11, 0, 0, DateTimeKind.Utc);
        protected const int GEAR_INDEX = 2;
        protected const int WAIT_MS = 5000;

        // What MakeRecord puts in a record, and other values for "changed" records.
        protected const int MADE_LEVEL = 12;
        protected const uint MADE_GOLD = 500u;
        protected const uint MADE_GEAR_ITEM = 77u;
        protected const uint MADE_INVENTORY_ITEM = 5u;
        protected const int MADE_INVENTORY_COUNT = 4;
        protected const byte MADE_INVENTORY_ENHANCEMENT = 2;
        protected const int OTHER_LEVEL = 31;
        protected const uint OTHER_ITEM = 900u;
        protected const int OTHER_COUNT = 9;
        protected const byte OTHER_ENHANCEMENT = 7;

        /// <summary>The clock of the in-memory store; stays available to every subclass (a database store ignores it).</summary>
        protected DateTime _now;

        /// <summary>The store under test, made by <see cref="CreateStore"/>.</summary>
        protected ICharacterRecordStore Store;

        /// <summary>A service over <see cref="Store"/>.</summary>
        protected CharacterPersistenceService Service;

        /// <summary>Makes the store for one test. Called by the base SetUp, after <c>_now</c> is set to T1.</summary>
        protected abstract ICharacterRecordStore CreateStore();

        [SetUp]
        public void SetUpContract()
        {
            _now = T1;
            Store = CreateStore();
            Service = new CharacterPersistenceService(Store);
        }

        protected static CharacterRecord MakeRecord(CharacterID id, uint saveVersion)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, id, 3);
            r.CharacterName = "Brann";
            r.Level = MADE_LEVEL;
            r.GoldBalance = MADE_GOLD;
            r.SaveVersion = saveVersion;
            r.GearSlots[GEAR_INDEX] = new GearSlotRecord(new ItemID(MADE_GEAR_ITEM), 3);
            r.InventorySlots[0] = new ItemID(MADE_INVENTORY_ITEM);
            r.InventoryItemCounts[0] = MADE_INVENTORY_COUNT;
            r.InventoryEnhancementLevels[0] = MADE_INVENTORY_ENHANCEMENT;
            return r;
        }

        internal static string Arrays(CharacterRecord r)
        {
            return string.Join(",", r.GearSlots.Select(g => g.ItemId.RawValue + ":" + g.EnhancementLevel))
                + "|" + string.Join(",", r.InventorySlots.Select(i => i.RawValue))
                + "|" + string.Join(",", r.InventoryItemCounts)
                + "|" + string.Join(",", r.InventoryEnhancementLevels);
        }

        internal static void AssertSame(CharacterRecord expected, CharacterRecord actual)
        {
            Assert.IsNotNull(actual);
            foreach (PropertyInfo p in typeof(CharacterRecord).GetProperties().Where(property => !property.PropertyType.IsArray))
            {
                Assert.AreEqual(p.GetValue(expected), p.GetValue(actual), p.Name);
            }

            Assert.AreEqual(Arrays(expected), Arrays(actual));
        }

        /// <summary>Waits (bounded) for a task to end, whatever its outcome, and asserts that it ended.</summary>
        protected static void Settle(Task task)
        {
            try
            {
                task.Wait(WAIT_MS);
            }
            catch (AggregateException)
            {
                // The outcome is the test's to inspect.
            }

            Assert.IsTrue(task.IsCompleted, "The task did not end within " + WAIT_MS + " ms.");
        }

        protected bool Insert(CharacterRecord r)
        {
            Task<bool> t = Store.InsertAsync(r, CancellationToken.None);
            Settle(t);
            return t.Result;
        }

        protected int Update(CharacterRecord r, uint expected)
        {
            Task<int> t = Store.UpdateAsync(r, expected, CancellationToken.None);
            Settle(t);
            return t.Result;
        }

        protected CharacterRecord Fetch(CharacterID id)
        {
            Task<CharacterRecord> t = Store.FetchAsync(id, CancellationToken.None);
            Settle(t);
            return t.Result;
        }

        protected static CancellationToken CancelledToken()
        {
            return new CancellationToken(true);
        }

        // ---------------------------------------------------------------- insert / fetch

        [Test]
        public void InMemoryStore_FetchMiss_ReturnsNull()
        {
            Assert.IsNull(Fetch(CHAR_A));
        }

        // ---------------------------------------------------------------- update

        [Test]
        public void InMemoryStore_UpdateWithStaleVersion_ReturnsZeroAndChangesNothing()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            _now = T2;
            Update(MakeRecord(CHAR_A, 0u), VERSION_FOUR);
            CharacterRecord before = Fetch(CHAR_A);
            CharacterRecord stale = MakeRecord(CHAR_A, 0u);
            stale.Level = 40;

            _now = T3;

            Assert.AreEqual(0, Update(stale, VERSION_FOUR));

            AssertSame(before, Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_UpdateUnknownId_ReturnsZero()
        {
            Assert.AreEqual(0, Update(MakeRecord(CHAR_B, 0u), 0u));
            Assert.IsNull(Fetch(CHAR_B));
        }

        // ---------------------------------------------------------------- isolation

        [Test]
        public void InMemoryStore_CallerMutatesRecordAfterInsert_StoredRecordUnchanged()
        {
            CharacterRecord r = MakeRecord(CHAR_A, 0u);
            Insert(r);

            r.GearSlots[GEAR_INDEX] = new GearSlotRecord(new ItemID(1u), 9);
            r.Level = 99;

            CharacterRecord fetched = Fetch(CHAR_A);
            Assert.AreEqual(MADE_LEVEL, fetched.Level);
            Assert.AreEqual(MADE_GEAR_ITEM, fetched.GearSlots[GEAR_INDEX].ItemId.RawValue);
            Assert.AreEqual(3, fetched.GearSlots[GEAR_INDEX].EnhancementLevel);
        }

        [Test]
        public void InMemoryStore_CallerMutatesFetchedRecord_SecondFetchShowsStoredValues()
        {
            Insert(MakeRecord(CHAR_A, 0u));
            CharacterRecord first = Fetch(CHAR_A);

            first.Level = 99;
            first.GearSlots[GEAR_INDEX] = GearSlotRecord.Empty;

            CharacterRecord second = Fetch(CHAR_A);
            Assert.AreEqual(MADE_LEVEL, second.Level);
            Assert.AreEqual(MADE_GEAR_ITEM, second.GearSlots[GEAR_INDEX].ItemId.RawValue);
        }

        [Test]
        public void InMemoryStore_CallerMutatesRecordAfterUpdate_StoredRecordUnchanged()
        {
            Insert(MakeRecord(CHAR_A, 0u));
            CharacterRecord r = MakeRecord(CHAR_A, 0u);
            r.Level = 20;
            Update(r, 0u);

            r.Level = 99;
            r.GearSlots[GEAR_INDEX] = GearSlotRecord.Empty;

            CharacterRecord fetched = Fetch(CHAR_A);
            Assert.AreEqual(20, fetched.Level);
            Assert.AreEqual(MADE_GEAR_ITEM, fetched.GearSlots[GEAR_INDEX].ItemId.RawValue);
        }

        // ---------------------------------------------------------------- cancellation

        [Test]
        public void InMemoryStore_InsertWithCancelledToken_IsCancelledAndWritesNothing()
        {
            Task<bool> t = Store.InsertAsync(MakeRecord(CHAR_A, 0u), CancelledToken());
            Settle(t);

            Assert.IsTrue(t.IsCanceled);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_FetchWithCancelledToken_IsCancelled()
        {
            Insert(MakeRecord(CHAR_A, 0u));

            Task<CharacterRecord> t = Store.FetchAsync(CHAR_A, CancelledToken());
            Settle(t);

            Assert.IsTrue(t.IsCanceled);
        }

        [Test]
        public void InMemoryStore_UpdateWithCancelledToken_IsCancelledAndWritesNothing()
        {
            Insert(MakeRecord(CHAR_A, 0u));
            CharacterRecord changed = MakeRecord(CHAR_A, 0u);
            changed.Level = 50;

            Task<int> t = Store.UpdateAsync(changed, 0u, CancelledToken());
            Settle(t);

            Assert.IsTrue(t.IsCanceled);
            Assert.AreEqual(MADE_LEVEL, Fetch(CHAR_A).Level);
            Assert.AreEqual(0u, Fetch(CHAR_A).SaveVersion);
        }

        // ---------------------------------------------------------------- argument checks

        [Test]
        public void InMemoryStore_NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Store.InsertAsync(null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => Store.UpdateAsync(null, 0u, CancellationToken.None));
        }

        [Test]
        public void ICharacterRecordStore_HasExactlyThreeMethods()
        {
            MethodInfo[] methods = typeof(ICharacterRecordStore).GetMethods();
            Assert.AreEqual(3, methods.Length);
            CollectionAssert.AreEquivalent(new[] { "FetchAsync", "InsertAsync", "UpdateAsync" }, methods.Select(m => m.Name).ToArray());
        }

        // ---------------------------------------------------------------- CharacterStubDefaults

        [Test]
        public void CharacterStubDefaults_Constants_EqualCrCp9Values()
        {
            Assert.AreEqual(1, CharacterStubDefaults.LEVEL);
            Assert.AreEqual(0, CharacterStubDefaults.EXPERIENCE);
            Assert.AreEqual(0, CharacterStubDefaults.HELD_FREE_POINTS);
            Assert.AreEqual(10, CharacterStubDefaults.BASE_ATTRIBUTE);
            Assert.AreEqual(400, CharacterStubDefaults.MAX_HP);
            Assert.AreEqual(220, CharacterStubDefaults.MAX_MP);
            Assert.AreEqual(30, CharacterStubDefaults.ATTACK_POWER);
            Assert.AreEqual(20, CharacterStubDefaults.DEFENSE);
            Assert.AreEqual(4, CharacterStubDefaults.MAGIC_DEFENSE);
            Assert.AreEqual(0.065f, CharacterStubDefaults.CRIT_CHANCE);
            Assert.AreEqual(1.5f, CharacterStubDefaults.CRIT_MULTIPLIER);
            Assert.AreEqual(3.0f, CharacterStubDefaults.ATTACK_RANGE);
            Assert.AreEqual(1.030f, CharacterStubDefaults.ATTACK_SPEED_MULTIPLIER);
            Assert.AreEqual(5.0f, CharacterStubDefaults.MOVEMENT_SPEED);
            Assert.AreEqual(400f, CharacterStubDefaults.CURRENT_HP);
            Assert.AreEqual(220f, CharacterStubDefaults.CURRENT_MP);
            Assert.AreEqual(0u, CharacterStubDefaults.GOLD_BALANCE);
            Assert.AreEqual(0u, CharacterStubDefaults.GOLD_VERSION);
            Assert.AreEqual(0u, CharacterStubDefaults.SAVE_VERSION);
        }

        [Test]
        public void CharacterStubDefaults_CreateRecord_SetsEveryField()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, 2);

            Assert.AreEqual(CHAR_A, r.CharacterID);
            Assert.AreEqual(ACCOUNT_A, r.AccountID);
            Assert.AreEqual(string.Empty, r.CharacterName);
            Assert.AreEqual((byte)2, r.ClassType);
            Assert.AreEqual(1, r.Level);
            Assert.AreEqual(0, r.Experience);
            Assert.AreEqual(0, r.HeldFreePoints);
            Assert.AreEqual(10, r.STR);
            Assert.AreEqual(10, r.DEX);
            Assert.AreEqual(10, r.VIT);
            Assert.AreEqual(10, r.INT);
            Assert.AreEqual(400, r.MaxHP);
            Assert.AreEqual(220, r.MaxMP);
            Assert.AreEqual(30, r.AttackPower);
            Assert.AreEqual(20, r.Defense);
            Assert.AreEqual(4, r.MagicDefense);
            Assert.AreEqual(0.065f, r.CritChance);
            Assert.AreEqual(1.5f, r.CritMultiplier);
            Assert.AreEqual(3.0f, r.AttackRange);
            Assert.AreEqual(1.030f, r.AttackSpeedMultiplier);
            Assert.AreEqual(5.0f, r.MovementSpeed);
            Assert.AreEqual(400f, r.CurrentHP);
            Assert.AreEqual(220f, r.CurrentMP);
            Assert.AreEqual(0u, r.GoldBalance);
            Assert.AreEqual(0u, r.GoldVersion);
            Assert.AreEqual(ZoneID.Invalid, r.LastZoneID);
            Assert.AreEqual(0u, r.SaveVersion);
            Assert.AreEqual(default(DateTime), r.CreatedAtUtc);
            Assert.AreEqual(default(DateTime), r.LastSavedAtUtc);
            Assert.IsTrue(r.GearSlots.All(g => g.ItemId == ItemID.Invalid && g.EnhancementLevel == 0));
            Assert.IsTrue(r.InventorySlots.All(i => i == ItemID.Invalid));
            Assert.IsTrue(r.InventoryItemCounts.All(c => c == 0));
            Assert.IsTrue(r.InventoryEnhancementLevels.All(l => l == 0));
        }

        // ---------------------------------------------------------------- CreateStub

        [Test]
        public void CreateStub_ExistingId_FaultsWithInvalidOperationExceptionAndKeepsRecord()
        {
            Insert(MakeRecord(CHAR_A, 0u));

            Task t = Service.CreateStub(ACCOUNT_A, CHAR_A, 1, CancellationToken.None);
            Settle(t);

            Assert.IsTrue(t.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(t.Exception.InnerException);
            Assert.AreEqual(MADE_GOLD, Fetch(CHAR_A).GoldBalance);
        }

        [Test]
        public void CreateStub_CancelledToken_IsCancelled()
        {
            Task t = Service.CreateStub(ACCOUNT_A, CHAR_A, 1, CancelledToken());
            Settle(t);

            Assert.IsTrue(t.IsCanceled);
            Assert.IsNull(Fetch(CHAR_A));
        }

        // ---------------------------------------------------------------- service

        [Test]
        public void CharacterPersistenceService_NullStore_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterPersistenceService(null));
        }

        // ---------------------------------------------------------------- rows are keyed by character

        [Test]
        public void InMemoryStore_TwoCharacters_AreKeptApart()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));

            Assert.IsNull(Fetch(CHAR_B), "a fetch for B must not return A's row");
            Assert.AreEqual(0, Update(MakeRecord(CHAR_B, 0u), VERSION_FOUR), "an update for B must not hit A's row");
            CharacterRecord b = MakeRecord(CHAR_B, 0u);
            b.Level = OTHER_LEVEL;
            Assert.IsTrue(Insert(b), "B has no row yet");

            Assert.AreEqual(MADE_LEVEL, Fetch(CHAR_A).Level);
            Assert.AreEqual(VERSION_FOUR, Fetch(CHAR_A).SaveVersion);
            Assert.AreEqual(OTHER_LEVEL, Fetch(CHAR_B).Level);
            Assert.AreEqual(CHAR_B, Fetch(CHAR_B).CharacterID);
        }

        [Test]
        public void InMemoryStore_UpdateOneCharacter_LeavesTheOtherUnchanged()
        {
            Insert(MakeRecord(CHAR_A, 0u));
            Insert(MakeRecord(CHAR_B, 0u));
            CharacterRecord before = Fetch(CHAR_B);
            CharacterRecord changed = MakeRecord(CHAR_A, 0u);
            changed.Level = OTHER_LEVEL;

            Assert.AreEqual(1, Update(changed, 0u));

            AssertSame(before, Fetch(CHAR_B));
            Assert.AreEqual(OTHER_LEVEL, Fetch(CHAR_A).Level);
        }

        // ---------------------------------------------------------------- version predicate, further cases

        [Test]
        public void InMemoryStore_UpdateWithExpectedVersionAboveStored_ReturnsZeroAndChangesNothing()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            CharacterRecord before = Fetch(CHAR_A);
            _now = T2;

            Assert.AreEqual(0, Update(MakeRecord(CHAR_A, 0u), VERSION_NINE));

            AssertSame(before, Fetch(CHAR_A));
        }

        // ---------------------------------------------------------------- isolation of the inventory arrays

        [Test]
        public void InMemoryStore_CallerMutatesInventoryArraysAfterInsert_StoredRecordUnchanged()
        {
            CharacterRecord r = MakeRecord(CHAR_A, 0u);
            Insert(r);

            r.InventorySlots[0] = new ItemID(OTHER_ITEM);
            r.InventoryItemCounts[0] = OTHER_COUNT;
            r.InventoryEnhancementLevels[0] = OTHER_ENHANCEMENT;

            CharacterRecord fetched = Fetch(CHAR_A);
            Assert.AreEqual(MADE_INVENTORY_ITEM, fetched.InventorySlots[0].RawValue);
            Assert.AreEqual(MADE_INVENTORY_COUNT, fetched.InventoryItemCounts[0]);
            Assert.AreEqual(MADE_INVENTORY_ENHANCEMENT, fetched.InventoryEnhancementLevels[0]);
        }

        [Test]
        public void InMemoryStore_CallerMutatesFetchedInventoryArrays_SecondFetchShowsStoredValues()
        {
            Insert(MakeRecord(CHAR_A, 0u));
            CharacterRecord first = Fetch(CHAR_A);

            first.InventorySlots[0] = new ItemID(OTHER_ITEM);
            first.InventoryItemCounts[0] = OTHER_COUNT;
            first.InventoryEnhancementLevels[0] = OTHER_ENHANCEMENT;

            CharacterRecord second = Fetch(CHAR_A);
            Assert.AreEqual(MADE_INVENTORY_ITEM, second.InventorySlots[0].RawValue);
            Assert.AreEqual(MADE_INVENTORY_COUNT, second.InventoryItemCounts[0]);
            Assert.AreEqual(MADE_INVENTORY_ENHANCEMENT, second.InventoryEnhancementLevels[0]);
        }

        // ---------------------------------------------------------------- source rules

        private static string[] CodeLines(string fileName)
        {
            // Application.dataPath is <repo>/Assets; the sources live in <repo>/src.
            string repo = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string path = Path.Combine(repo, "src", "ServerLogic", "CharacterPersistence", fileName);
            return File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//")).ToArray();
        }

        [Test]
        public void PersistenceSources_NeverReadTheSystemClock()
        {
            string repo = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string folder = Path.Combine(repo, "src", "ServerLogic", "CharacterPersistence");
            string[] files = Directory.GetFiles(folder, "*.cs");
            Assert.IsNotEmpty(files, "no source found under " + folder);

            foreach (string file in files)
            {
                foreach (string line in CodeLines(Path.GetFileName(file)))
                {
                    Assert.IsFalse(line.Contains("DateTime.UtcNow") || line.Contains("DateTime.Now"),
                        Path.GetFileName(file) + " reads the system clock: " + line.Trim());
                }
            }
        }

        [Test]
        public void CharacterPersistenceService_HasNoLevelOneLiteral()
        {
            // Distinctive CR-CP-9 values; the service must take them from CharacterStubDefaults.
            string[] literals = { "400", "220", "0.065", "1.030", "1.03f" };

            foreach (string line in CodeLines("CharacterPersistenceService.cs"))
            {
                foreach (string literal in literals)
                {
                    Assert.IsFalse(line.Contains(literal), "CharacterPersistenceService.cs has the literal " + literal + ": " + line.Trim());
                }
            }
        }
    }

    /// <summary>
    /// The contract fixture against <see cref="InMemoryCharacterRecordStore"/>, plus the tests that cannot run against a
    /// database store: they assert the injected clock's exact value, drive the in-memory fault controls
    /// (<c>FailNext</c>, <c>HoldNext</c>, <c>NeverCompleteNext</c>, <c>OverwriteSaveVersion</c>), or rely on the in-memory
    /// store's <c>uint</c> wrap-around of <c>SaveVersion</c>. Their bodies are Story 002's, unchanged.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_RecordStore_Tests : CharacterRecordStoreContractTests
    {
        private InMemoryCharacterRecordStore _store;

        protected override ICharacterRecordStore CreateStore()
        {
            _store = new InMemoryCharacterRecordStore(() => _now);
            return _store;
        }

        // ---------------------------------------------------------------- divergence from the database store

        [Test]
        public void InMemoryStore_InsertWithNegativeInventoryCount_AcceptsItAndReturnsItUnchanged()
        {
            // Pins the divergence: NpgsqlCharacterRecordStore rejects a negative count with ArgumentException
            // (NpgsqlStore_InsertWithNegativeInventoryCount_FaultsWithArgumentExceptionAndWritesNothing); the in-memory store does not.
            CharacterRecord r = MakeRecord(CHAR_A, 0u);
            r.InventoryItemCounts[0] = -1;

            Assert.IsTrue(Insert(r));

            Assert.AreEqual(-1, Fetch(CHAR_A).InventoryItemCounts[0]);
        }

        // ---------------------------------------------------------------- insert / fetch

        [Test]
        public void InMemoryStore_Insert_StoresRecordWithGivenVersionAndStampsBothTimestamps()
        {
            CharacterRecord r = MakeRecord(CHAR_A, VERSION_FOUR);

            Assert.IsTrue(Insert(r));

            CharacterRecord fetched = Fetch(CHAR_A);
            Assert.AreEqual(VERSION_FOUR, fetched.SaveVersion);
            Assert.AreEqual(T1, fetched.CreatedAtUtc);
            Assert.AreEqual(T1, fetched.LastSavedAtUtc);
            Assert.AreEqual(MADE_GOLD, fetched.GoldBalance);
            Assert.AreEqual(MADE_GEAR_ITEM, fetched.GearSlots[GEAR_INDEX].ItemId.RawValue);
        }

        [Test]
        public void InMemoryStore_InsertSameIdTwice_ReturnsFalseAndKeepsFirstRecord()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            CharacterRecord second = MakeRecord(CHAR_A, 0u);
            second.GoldBalance = 1u;
            _now = T2;

            Assert.IsFalse(Insert(second));

            CharacterRecord fetched = Fetch(CHAR_A);
            Assert.AreEqual(MADE_GOLD, fetched.GoldBalance);
            Assert.AreEqual(VERSION_FOUR, fetched.SaveVersion);
            Assert.AreEqual(T1, fetched.LastSavedAtUtc);
        }

        // ---------------------------------------------------------------- update

        [Test]
        public void InMemoryStore_UpdateWithMatchingVersion_IncrementsVersionAndStampsLastSaved()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            _now = T2;
            CharacterRecord incoming = MakeRecord(CHAR_A, 99u);
            incoming.Level = 13;
            incoming.CreatedAtUtc = T2;

            Assert.AreEqual(1, Update(incoming, VERSION_FOUR));

            CharacterRecord fetched = Fetch(CHAR_A);
            Assert.AreEqual(VERSION_FOUR + 1u, fetched.SaveVersion);
            Assert.AreEqual(T2, fetched.LastSavedAtUtc);
            Assert.AreEqual(T1, fetched.CreatedAtUtc);
            Assert.AreEqual(13, fetched.Level);
        }

        // A database BIGINT does not wrap: save_version + 1 at uint.MaxValue is 4294967296, which reads back as a corrupt record.
        [Test]
        public void InMemoryStore_UpdateAtMaxVersion_WrapsToZero()
        {
            Insert(MakeRecord(CHAR_A, uint.MaxValue));

            Assert.AreEqual(1, Update(MakeRecord(CHAR_A, 0u), uint.MaxValue));

            Assert.AreEqual(0u, Fetch(CHAR_A).SaveVersion);
        }

        // ---------------------------------------------------------------- cancellation

        [Test]
        public void InMemoryStore_CancelledCall_DoesNotConsumePendingFailNext()
        {
            var e = new InvalidOperationException("boom");
            _store.FailNext(e);

            Task<bool> cancelled = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancelledToken());
            Task<bool> next = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(cancelled.IsCanceled);
            Assert.IsTrue(next.IsFaulted);
            Assert.AreSame(e, next.Exception.InnerException);
        }

        // ---------------------------------------------------------------- FailNext

        [Test]
        public void InMemoryStore_FailNext_FaultsNextCallWithThatExceptionAndWritesNothing()
        {
            var e = new InvalidOperationException("boom");
            _store.FailNext(e);

            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(t.IsFaulted);
            Assert.AreSame(e, t.Exception.InnerException);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_FailNext_CallAfterSucceeds()
        {
            _store.FailNext(new InvalidOperationException("boom"));
            _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(Insert(MakeRecord(CHAR_A, 0u)));
        }

        [Test]
        public void InMemoryStore_FailNextNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _store.FailNext(null));
        }

        // ---------------------------------------------------------------- HoldNext

        [Test]
        public void InMemoryStore_HoldNext_TaskIncompleteUntilReleasedThenHasNormalResult()
        {
            Action release = _store.HoldNext();

            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsFalse(t.IsCompleted);
            release();
            Assert.IsTrue(t.IsCompleted);
            Assert.IsTrue(t.Result);
            Assert.IsNotNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_HoldNext_SecondReleaseIsNoOp()
        {
            Action release = _store.HoldNext();
            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);
            release();
            _now = T2;

            release();

            Assert.IsTrue(t.IsCompleted);
            Assert.IsTrue(t.Result);
            Assert.AreEqual(T1, Fetch(CHAR_A).LastSavedAtUtc);
        }

        [Test]
        public void InMemoryStore_HoldNext_ReleaseBeforeAnyCallDoesNothing()
        {
            Action release = _store.HoldNext();

            release();
            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsFalse(t.IsCompleted);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_HoldNext_RecordMutatedAfterCallIsNotWhatGetsWritten()
        {
            Action release = _store.HoldNext();
            CharacterRecord r = MakeRecord(CHAR_A, 0u);
            _store.InsertAsync(r, CancellationToken.None);

            r.Level = 99;
            release();

            Assert.AreEqual(MADE_LEVEL, Fetch(CHAR_A).Level);
        }

        [Test]
        public void InMemoryStore_HoldNext_TokenCancelledWhileHeldCancelsTaskAndReleaseWritesNothing()
        {
            using var cts = new CancellationTokenSource();
            Action release = _store.HoldNext();
            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), cts.Token);

            cts.Cancel();
            release();

            Assert.IsTrue(t.IsCanceled);
            Assert.IsNull(Fetch(CHAR_A));
        }

        // ---------------------------------------------------------------- NeverCompleteNext

        [Test]
        public void InMemoryStore_NeverCompleteNext_TaskStaysIncompleteAndWritesNothing()
        {
            _store.NeverCompleteNext();

            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsFalse(t.IsCompleted);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_NeverCompleteNext_CancellingTokenCancelsTask()
        {
            using var cts = new CancellationTokenSource();
            _store.NeverCompleteNext();
            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), cts.Token);

            cts.Cancel();

            Assert.IsTrue(t.IsCanceled);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_NeverCompleteNext_CallAfterIsNormal()
        {
            _store.NeverCompleteNext();
            _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(Insert(MakeRecord(CHAR_A, 0u)));
        }

        // ---------------------------------------------------------------- OverwriteSaveVersion

        [Test]
        public void InMemoryStore_OverwriteSaveVersion_MakesUpdateWithOldVersionReturnZero()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));

            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, VERSION_NINE));

            Assert.AreEqual(0, Update(MakeRecord(CHAR_A, 0u), VERSION_FOUR));
            Assert.AreEqual(VERSION_NINE, Fetch(CHAR_A).SaveVersion);
        }

        [Test]
        public void InMemoryStore_OverwriteSaveVersionUnknownId_ReturnsFalse()
        {
            Assert.IsFalse(_store.OverwriteSaveVersion(CHAR_A, VERSION_NINE));
        }

        // ---------------------------------------------------------------- argument checks and the clock

        [Test]
        public void InMemoryStore_NullClock_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new InMemoryCharacterRecordStore(null));
        }

        [Test]
        public void InMemoryStore_ClockThrows_FaultsTheTaskAndWritesNothing()
        {
            var clockFailure = new InvalidOperationException("clock");
            var store = new InMemoryCharacterRecordStore(() => throw clockFailure);

            Task<bool> t = store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(t.IsFaulted);
            Assert.AreSame(clockFailure, t.Exception.InnerException);
            Task<CharacterRecord> fetch = store.FetchAsync(CHAR_A, CancellationToken.None);
            Assert.IsTrue(fetch.IsCompleted);
            Assert.IsNull(fetch.Result);
        }

        [Test]
        public void InMemoryStore_InsertIgnoresTheCallersTimestamps()
        {
            CharacterRecord r = MakeRecord(CHAR_A, 0u);
            r.CreatedAtUtc = T2;
            r.LastSavedAtUtc = T3;

            Insert(r);

            Assert.AreEqual(T1, Fetch(CHAR_A).CreatedAtUtc);
            Assert.AreEqual(T1, Fetch(CHAR_A).LastSavedAtUtc);
        }

        [Test]
        public void InMemoryStore_ClockThrowsOnUpdate_FaultsTheTaskAndKeepsTheRow()
        {
            var clockFailure = new InvalidOperationException("clock");
            bool clockFails = false;
            var store = new InMemoryCharacterRecordStore(() => clockFails ? throw clockFailure : T1);
            store.InsertAsync(MakeRecord(CHAR_A, VERSION_FOUR), CancellationToken.None);
            CharacterRecord changed = MakeRecord(CHAR_A, 0u);
            changed.Level = OTHER_LEVEL;
            clockFails = true;

            Task<int> t = store.UpdateAsync(changed, VERSION_FOUR, CancellationToken.None);

            Assert.IsTrue(t.IsFaulted);
            Assert.AreSame(clockFailure, t.Exception.InnerException);
            Task<CharacterRecord> fetch = store.FetchAsync(CHAR_A, CancellationToken.None);
            Assert.IsTrue(fetch.IsCompleted);
            Assert.AreEqual(MADE_LEVEL, fetch.Result.Level);
            Assert.AreEqual(VERSION_FOUR, fetch.Result.SaveVersion);
        }

        // ---------------------------------------------------------------- CreateStub

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)255)]
        public void CreateStub_NewCharacter_StoresLevelOneRecordWithGivenClassByte(byte classType)
        {
            Task t = Service.CreateStub(ACCOUNT_A, CHAR_A, classType, CancellationToken.None);

            Assert.IsTrue(t.IsCompleted);
            Assert.AreEqual(TaskStatus.RanToCompletion, t.Status);
            // One baseline for every class byte: the stats do not depend on the class (CR-CP-9).
            CharacterRecord expected = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, 0);
            expected.ClassType = classType;
            expected.CreatedAtUtc = T1;
            expected.LastSavedAtUtc = T1;
            AssertSame(expected, Fetch(CHAR_A));
        }

        [Test]
        public void CreateStub_StoreFails_FaultsWithStoreException()
        {
            var e = new InvalidOperationException("db down");
            _store.FailNext(e);

            Task t = Service.CreateStub(ACCOUNT_A, CHAR_A, 1, CancellationToken.None);

            Assert.IsTrue(t.IsFaulted);
            Assert.AreSame(e, t.Exception.InnerException);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void CreateStub_StoreCallHeld_IsIncompleteUntilTheStoreIsReleased()
        {
            Action release = _store.HoldNext();

            Task t = Service.CreateStub(ACCOUNT_A, CHAR_A, 1, CancellationToken.None);

            Assert.IsFalse(t.IsCompleted, "CreateStub must not complete before its store call does");
            Assert.IsNull(Fetch(CHAR_A));
            release();
            // The store writes on release; CreateStub's own task finishes on a pool thread and is not waited for.
            Assert.IsNotNull(Fetch(CHAR_A));
        }

        // ---------------------------------------------------------------- fault controls on fetch and update

        [Test]
        public void InMemoryStore_FailNext_FaultsAFetch()
        {
            Insert(MakeRecord(CHAR_A, 0u));
            var e = new InvalidOperationException("boom");
            _store.FailNext(e);

            Task<CharacterRecord> t = _store.FetchAsync(CHAR_A, CancellationToken.None);

            Assert.IsTrue(t.IsFaulted);
            Assert.AreSame(e, t.Exception.InnerException);
            Assert.IsNotNull(Fetch(CHAR_A), "the call after it is normal");
        }

        [Test]
        public void InMemoryStore_FailNext_FaultsAnUpdateAndWritesNothing()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            var e = new InvalidOperationException("boom");
            CharacterRecord changed = MakeRecord(CHAR_A, 0u);
            changed.Level = OTHER_LEVEL;
            _store.FailNext(e);

            Task<int> t = _store.UpdateAsync(changed, VERSION_FOUR, CancellationToken.None);

            Assert.IsTrue(t.IsFaulted);
            Assert.AreSame(e, t.Exception.InnerException);
            Assert.AreEqual(MADE_LEVEL, Fetch(CHAR_A).Level);
            Assert.AreEqual(VERSION_FOUR, Fetch(CHAR_A).SaveVersion);
        }

        [Test]
        public void InMemoryStore_HoldNext_HeldUpdateWritesOnceHoweverOftenItIsReleased()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            CharacterRecord changed = MakeRecord(CHAR_A, 0u);
            changed.Level = OTHER_LEVEL;
            Action release = _store.HoldNext();
            Task<int> t = _store.UpdateAsync(changed, VERSION_FOUR, CancellationToken.None);
            Assert.IsFalse(t.IsCompleted);
            Assert.AreEqual(MADE_LEVEL, Fetch(CHAR_A).Level, "nothing is written while the call is held");

            release();
            _store.OverwriteSaveVersion(CHAR_A, VERSION_FOUR);
            release();

            // A second run of the update would find version 4 again and write a second time.
            Assert.IsTrue(t.IsCompleted);
            Assert.AreEqual(1, t.Result);
            Assert.AreEqual(VERSION_FOUR, Fetch(CHAR_A).SaveVersion, "the second release must not run the update again");
            Assert.AreEqual(OTHER_LEVEL, Fetch(CHAR_A).Level);
        }

        [Test]
        public void InMemoryStore_HoldNext_HeldFetchReturnsTheRowAsItIsAtRelease()
        {
            Action release = _store.HoldNext();
            Task<CharacterRecord> t = _store.FetchAsync(CHAR_A, CancellationToken.None);
            Assert.IsFalse(t.IsCompleted);
            Insert(MakeRecord(CHAR_A, 0u));

            release();

            Assert.IsTrue(t.IsCompleted);
            Assert.IsNotNull(t.Result);
        }

        [Test]
        public void InMemoryStore_NeverCompleteNext_AppliesToAnUpdateAndWritesNothing()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            CharacterRecord changed = MakeRecord(CHAR_A, 0u);
            changed.Level = OTHER_LEVEL;
            _store.NeverCompleteNext();

            Task<int> t = _store.UpdateAsync(changed, VERSION_FOUR, CancellationToken.None);

            Assert.IsFalse(t.IsCompleted);
            Assert.AreEqual(MADE_LEVEL, Fetch(CHAR_A).Level);
            Assert.AreEqual(VERSION_FOUR, Fetch(CHAR_A).SaveVersion);
        }

        // ---------------------------------------------------------------- fault controls: pending and replaced

        [Test]
        public void InMemoryStore_CancelledCall_DoesNotConsumePendingHoldNext()
        {
            Action release = _store.HoldNext();

            Task<bool> cancelled = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancelledToken());
            Task<bool> next = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(cancelled.IsCanceled);
            Assert.IsFalse(next.IsCompleted, "the hold is still pending for the next call");
            release();
            Assert.IsTrue(next.IsCompleted);
            Assert.IsTrue(next.Result);
        }

        [Test]
        public void InMemoryStore_CancelledCall_DoesNotConsumePendingNeverCompleteNext()
        {
            _store.NeverCompleteNext();

            Task<CharacterRecord> cancelled = _store.FetchAsync(CHAR_A, CancelledToken());
            Task<bool> next = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(cancelled.IsCanceled);
            Assert.IsFalse(next.IsCompleted);
            Assert.IsNull(Fetch(CHAR_A));
        }

        [Test]
        public void InMemoryStore_SecondControl_ReplacesThePendingOne()
        {
            var e = new InvalidOperationException("boom");
            _store.NeverCompleteNext();
            _store.FailNext(e);

            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            Assert.IsTrue(t.IsFaulted, "FailNext replaced NeverCompleteNext");
            Assert.AreSame(e, t.Exception.InnerException);
            Assert.IsTrue(Insert(MakeRecord(CHAR_A, 0u)), "one control, one call");
        }

        [Test]
        public void InMemoryStore_HoldNextReplaced_ItsReleaseActionDoesNothing()
        {
            Action release = _store.HoldNext();
            _store.NeverCompleteNext();
            Task<bool> t = _store.InsertAsync(MakeRecord(CHAR_A, 0u), CancellationToken.None);

            release();

            Assert.IsFalse(t.IsCompleted);
            Assert.IsNull(Fetch(CHAR_A));
        }
    }
}
