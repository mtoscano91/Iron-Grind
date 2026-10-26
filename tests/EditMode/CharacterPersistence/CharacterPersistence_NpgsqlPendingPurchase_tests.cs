using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Story 015: the shared purchase store contract (Story 011's 23 tests) against the real PostgreSQL store. Explicit:
    /// needs a local PostgreSQL with migrations 001-003 and <c>IRONGRIND_DEV_PG</c> (tools/postgres/README.md).
    /// </summary>
    [TestFixture]
    [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG — tools/postgres/README.md")]
    [Category("Database")]
    internal sealed class CharacterPersistence_NpgsqlPendingPurchaseStoreContract_Tests : PendingPurchaseStoreContractTests
    {
        private static readonly uint[] USED_IDS = { 9_000_201u, 9_000_202u, 9_000_203u };

        private string _connectionString;
        private IDisposable _dataSource;
        private ICharacterRecordStore _recordStore;

        protected override IPendingPurchaseStore CreateStore()
        {
            _connectionString = PgTestDatabase.ConnectionStringOrIgnore();
            PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            var created = NpgsqlRecordStoreTestAccess.CreateStoreWithOwnedDataSource(_connectionString);
            _dataSource = created.DataSource;
            _recordStore = created.Store;
            var purchases = created.Store as IPendingPurchaseStore;
            Assert.IsNotNull(purchases, "the Npgsql store must also be the purchase store");
            return purchases;
        }

        protected override void SeedCharacter(CharacterRecord record)
        {
            Assert.IsTrue(Complete(_recordStore.InsertAsync(record, CancellationToken.None)));
        }

        protected override CharacterRecord ReadCharacter(CharacterID id)
        {
            return Complete(_recordStore.FetchAsync(id, CancellationToken.None));
        }

        protected override void CleanUp()
        {
            // Runs also when the SetUp ignored the test; then there is nothing to clean.
            if (_dataSource != null)
            {
                _dataSource.Dispose();
                _dataSource = null;
            }

            if (!string.IsNullOrEmpty(_connectionString))
            {
                PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            }
        }
    }

    /// <summary>
    /// Story 015's own database criteria: one transaction (rollback), idempotent begin (also concurrent), unknown
    /// character, outcomes, load, migration 003 and cancellation. Explicit, like the contract fixture. Test-only ids (9_000_3xx).
    /// </summary>
    [TestFixture]
    [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG — tools/postgres/README.md")]
    [Category("Database")]
    internal sealed class CharacterPersistence_NpgsqlPendingPurchase_Tests
    {
        private const uint ID_ROLLBACK_BEGIN = 9_000_301u;
        private const uint ID_ROLLBACK_REFUND = 9_000_302u;
        private const uint ID_RETRY = 9_000_303u;
        private const uint ID_RETRY_COMPLETED = 9_000_304u;
        private const uint ID_CONCURRENT = 9_000_305u;
        private const uint ID_UNKNOWN = 9_000_306u;
        private const uint ID_GENERATED = 9_000_307u;
        private const uint ID_OUTCOME_A = 9_000_308u;
        private const uint ID_OUTCOME_B = 9_000_309u;
        private const uint ID_LOAD_A = 9_000_310u;
        private const uint ID_LOAD_B = 9_000_311u;
        private const uint ID_CANCEL = 9_000_312u;
        private const uint ID_CANCEL_COMPLETE = 9_000_313u;
        private const uint ID_CANCEL_REFUND = 9_000_314u;
        private const uint ID_CANCEL_LOAD = 9_000_315u;
        private const uint ID_SKIP_BEGIN = 9_000_316u;
        private const uint ID_SKIP_REFUND = 9_000_317u;
        private const uint ID_FAIL_DELETE = 9_000_318u;
        private const uint ID_CORRUPT = 9_000_319u;
        private const uint ID_OTHER_STATE = 9_000_320u;

        private const uint STORED_GOLD = 500u;
        private const uint STORED_GOLD_VERSION = 4u;
        private const uint STORED_SAVE_VERSION = 7u;
        private const uint FIRST_GOLD = 300u;
        private const uint FIRST_GOLD_VERSION = 5u;
        private const uint SECOND_GOLD = 100u;
        private const uint SECOND_GOLD_VERSION = 6u;
        private const uint REFUND_GOLD = 500u;
        private const uint REFUND_GOLD_VERSION = 6u;
        private const uint REQUEST = 11u;
        private const uint REQUEST_COUNT_BASE = 6000u;
        private const int CONCURRENT_REPEATS = 3;
        private const uint ITEM = 42u;
        private const int QUANTITY = 2;
        private const uint COST = 200u;
        private const uint LARGE_COST = 3_000_000_000u;
        private const uint LARGE_ITEM = 4_000_000_000u;
        private const uint LARGE_REQUEST = 4_100_000_000u;
        private const int WAIT_MS = 5000;
        private const int STILL_PENDING_MS = 500;
        private const int POLL_MS = 50;
        private const short COMPLETED_STATE = 2;
        private const string TRIGGER_NAME = "cp015_fail_update";
        private const string SKIP_TRIGGER_NAME = "cp015_skip_update";
        private const string DELETE_TRIGGER_NAME = "cp015_fail_delete";

        private static readonly uint[] USED_IDS =
        {
            ID_ROLLBACK_BEGIN, ID_ROLLBACK_REFUND, ID_RETRY, ID_RETRY_COMPLETED, ID_CONCURRENT, ID_UNKNOWN,
            ID_GENERATED, ID_OUTCOME_A, ID_OUTCOME_B, ID_LOAD_A, ID_LOAD_B, ID_CANCEL,
            ID_CANCEL_COMPLETE, ID_CANCEL_REFUND, ID_CANCEL_LOAD, ID_SKIP_BEGIN, ID_SKIP_REFUND, ID_FAIL_DELETE,
            ID_CORRUPT, ID_OTHER_STATE,
        };

        private static readonly AccountID ACCOUNT = new AccountID(7u);

        private string _connectionString;
        private IDisposable _dataSource;
        private ICharacterRecordStore _records;
        private IPendingPurchaseStore _purchases;
        private DbConnection _lockConnection;

        [SetUp]
        public void SetUp()
        {
            _connectionString = PgTestDatabase.ConnectionStringOrIgnore();
            DropTrigger();
            PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            var created = NpgsqlRecordStoreTestAccess.CreateStoreWithOwnedDataSource(_connectionString);
            _records = created.Store;
            _dataSource = created.DataSource;
            _purchases = created.Store as IPendingPurchaseStore;
            Assert.IsNotNull(_purchases, "the Npgsql store must also be the purchase store");
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseLock();
            if (_dataSource != null)
            {
                _dataSource.Dispose();
                _dataSource = null;
            }

            if (!string.IsNullOrEmpty(_connectionString))
            {
                // The trigger goes first: it must not outlive a failed test, and it would block nothing on DELETE anyway.
                DropTrigger();
                PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within " + WAIT_MS + " ms.");
            return task.Result;
        }

        private static void AssertFaults(Task task)
        {
            try
            {
                task.Wait(WAIT_MS);
            }
            catch (AggregateException)
            {
                // Inspected below.
            }

            Assert.IsTrue(task.IsCompleted, "The task did not end within " + WAIT_MS + " ms.");
            Assert.IsTrue(task.IsFaulted, "The task did not fault, status " + task.Status);
        }

        private static void AssertEndsCancelledWithin(Task task, int milliseconds)
        {
            try
            {
                task.Wait(milliseconds);
            }
            catch (AggregateException)
            {
                // Inspected below.
            }

            Assert.IsTrue(task.IsCompleted, "The task did not end within " + milliseconds + " ms, status " + task.Status);
            if (task.IsFaulted)
            {
                Assert.IsInstanceOf<OperationCanceledException>(task.Exception.GetBaseException());
            }
            else
            {
                Assert.IsTrue(task.IsCanceled, "The task neither faulted nor was cancelled, status " + task.Status);
            }
        }

        private static CharacterID Id(uint raw)
        {
            return new CharacterID(raw);
        }

        private void Seed(uint raw)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT, Id(raw), 3);
            r.CharacterName = "Brann" + raw;
            r.GoldBalance = STORED_GOLD;
            r.GoldVersion = STORED_GOLD_VERSION;
            r.SaveVersion = STORED_SAVE_VERSION;
            Assert.IsTrue(Complete(_records.InsertAsync(r, CancellationToken.None)));
        }

        private static PendingPurchaseRecord NewRecord(uint charRaw, uint requestId)
        {
            return new PendingPurchaseRecord(0L, Id(charRaw), requestId, ITEM, QUANTITY, COST, PendingPurchaseState.GoldDebited);
        }

        private Task<long> BeginAsync(uint charRaw, uint requestId, uint gold, uint goldVersion, CancellationToken ct)
        {
            return _purchases.BeginPurchaseAsync(NewRecord(charRaw, requestId), gold, goldVersion, ct);
        }

        private long Begin(uint charRaw, uint requestId, uint gold, uint goldVersion)
        {
            return Complete(BeginAsync(charRaw, requestId, gold, goldVersion, CancellationToken.None));
        }

        private long Raw(string sql)
        {
            return PgTestDatabase.ScalarLong(_connectionString, sql);
        }

        private long RowCount(uint charRaw)
        {
            return Raw("SELECT COUNT(*) FROM pending_purchases WHERE char_id = " + charRaw);
        }

        private void AssertGold(uint charRaw, uint gold, uint goldVersion)
        {
            string where = " FROM character_records WHERE character_id = " + charRaw;
            Assert.AreEqual((long)gold, Raw("SELECT gold_balance" + where), "gold balance");
            Assert.AreEqual((long)goldVersion, Raw("SELECT gold_version" + where), "gold version");
            Assert.AreEqual((long)STORED_SAVE_VERSION, Raw("SELECT save_version" + where), "save version");
        }

        private static void AssertFaultsWith<TException>(Task task) where TException : Exception
        {
            AssertFaults(task);
            Assert.IsInstanceOf<TException>(task.Exception.GetBaseException());
        }

        // Drops every test trigger and its function (the triggers first: a function cannot go while a trigger uses it).
        private void DropTrigger()
        {
            PgTestDatabase.Execute(_connectionString, "DROP TRIGGER IF EXISTS " + TRIGGER_NAME + " ON character_records");
            PgTestDatabase.Execute(_connectionString, "DROP TRIGGER IF EXISTS " + SKIP_TRIGGER_NAME + " ON character_records");
            PgTestDatabase.Execute(_connectionString, "DROP TRIGGER IF EXISTS " + DELETE_TRIGGER_NAME + " ON pending_purchases");
            PgTestDatabase.Execute(_connectionString, "DROP FUNCTION IF EXISTS " + TRIGGER_NAME + "()");
            PgTestDatabase.Execute(_connectionString, "DROP FUNCTION IF EXISTS " + SKIP_TRIGGER_NAME + "()");
            PgTestDatabase.Execute(_connectionString, "DROP FUNCTION IF EXISTS " + DELETE_TRIGGER_NAME + "()");
        }

        // A BEFORE ROW trigger on one row: its function runs body (plpgsql) for that row only.
        private void CreateRowTrigger(string name, string timingAndEvent, string table, string when, string body)
        {
            PgTestDatabase.Execute(_connectionString,
                "CREATE FUNCTION " + name + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN " + body + " END $$");
            PgTestDatabase.Execute(_connectionString,
                "CREATE TRIGGER " + name + " " + timingAndEvent + " ON " + table + " FOR EACH ROW WHEN (" + when + ") EXECUTE FUNCTION " + name + "()");
        }

        // The character's gold UPDATE raises.
        private void CreateFailingUpdateTrigger(uint charRaw)
        {
            CreateRowTrigger(TRIGGER_NAME, "BEFORE UPDATE", "character_records", "OLD.character_id = " + charRaw,
                "RAISE EXCEPTION 'cp015 test trigger';");
        }

        // The character's gold UPDATE silently changes 0 rows (a BEFORE trigger returning NULL skips the row).
        private void CreateSkippingUpdateTrigger(uint charRaw)
        {
            CreateRowTrigger(SKIP_TRIGGER_NAME, "BEFORE UPDATE", "character_records", "OLD.character_id = " + charRaw,
                "RETURN NULL;");
        }

        // A DELETE of the character's purchase rows raises.
        private void CreateFailingDeleteTrigger(uint charRaw)
        {
            CreateRowTrigger(DELETE_TRIGGER_NAME, "BEFORE DELETE", "pending_purchases", "OLD.char_id = " + charRaw,
                "RAISE EXCEPTION 'cp015 test delete trigger';");
        }

        // Runs a locking statement in a transaction of its own on a second connection; ReleaseLock ends it.
        private void HoldLock(string lockSql)
        {
            _lockConnection = NpgsqlRecordStoreTestAccess.OpenConnection(_connectionString);
            PgTestDatabase.Execute(_lockConnection, "BEGIN");
            PgTestDatabase.Execute(_lockConnection, lockSql);
        }

        // Holds a row lock on the character: the foreign-key check of an INSERT into pending_purchases (FOR KEY SHARE on
        // that row) waits for it.
        private void LockCharacterRow(uint charRaw)
        {
            HoldLock("SELECT character_id FROM character_records WHERE character_id = " + charRaw + " FOR UPDATE");
        }

        // Holds a row lock on one purchase: a DELETE of that row waits for it.
        private void LockPurchaseRow(long purchaseId)
        {
            HoldLock("SELECT id FROM pending_purchases WHERE id = " + purchaseId + " FOR UPDATE");
        }

        // Waits (bounded) until the given number of this database's backends wait on a lock while running a statement that
        // starts with the given text; proves the calls reached that statement.
        private void WaitForLockWaiters(int count, string statementStart)
        {
            string sql = "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = current_database() "
                + "AND wait_event_type = 'Lock' AND query LIKE '" + statementStart + "%'";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long seen = 0L;
            while (clock.ElapsedMilliseconds < WAIT_MS)
            {
                seen = Raw(sql);
                if (seen >= count)
                {
                    return;
                }

                Thread.Sleep(POLL_MS);
            }

            Assert.Fail("expected " + count + " backends waiting on a lock in '" + statementStart + "…', saw " + seen);
        }

        private void ReleaseLock()
        {
            if (_lockConnection == null)
            {
                return;
            }

            try
            {
                PgTestDatabase.Execute(_lockConnection, "ROLLBACK");
            }
            catch (Exception)
            {
                // The connection may already be unusable; disposing it ends the transaction anyway.
            }
            finally
            {
                _lockConnection.Dispose();
                _lockConnection = null;
            }
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void NpgsqlPurchase_BeginWithFailingGoldUpdate_FaultsAndLeavesNoRowAndCharacterUnchanged()
        {
            Seed(ID_ROLLBACK_BEGIN);
            CreateFailingUpdateTrigger(ID_ROLLBACK_BEGIN);

            Task<long> task = BeginAsync(ID_ROLLBACK_BEGIN, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION, CancellationToken.None);

            AssertFaults(task);
            Assert.AreEqual(0L, RowCount(ID_ROLLBACK_BEGIN), "the INSERT must have been rolled back");
            AssertGold(ID_ROLLBACK_BEGIN, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_RefundWithFailingGoldUpdate_FaultsAndKeepsTheRow()
        {
            Seed(ID_ROLLBACK_REFUND);
            long id = Begin(ID_ROLLBACK_REFUND, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            CreateFailingUpdateTrigger(ID_ROLLBACK_REFUND);

            Task<PendingPurchaseWriteOutcome> task = _purchases.RefundPurchaseAsync(id, Id(ID_ROLLBACK_REFUND), REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None);

            AssertFaults(task);
            Assert.AreEqual(1L, RowCount(ID_ROLLBACK_REFUND), "the DELETE must have been rolled back");
            AssertGold(ID_ROLLBACK_REFUND, FIRST_GOLD, FIRST_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_BeginRetryWithOtherGold_ReturnsSameIdOneRowAndFirstGold()
        {
            Seed(ID_RETRY);
            long first = Begin(ID_RETRY, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);

            long second = Begin(ID_RETRY, REQUEST, SECOND_GOLD, SECOND_GOLD_VERSION);

            Assert.Greater(first, 0L);
            Assert.AreEqual(first, second);
            Assert.AreEqual(1L, RowCount(ID_RETRY));
            AssertGold(ID_RETRY, FIRST_GOLD, FIRST_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_BeginAgainAfterComplete_InsertsANewLargerIdRecord()
        {
            Seed(ID_RETRY_COMPLETED);
            long first = Begin(ID_RETRY_COMPLETED, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            Assert.AreEqual(PendingPurchaseWriteOutcome.Applied,
                Complete(_purchases.CompletePurchaseAsync(first, Id(ID_RETRY_COMPLETED), CancellationToken.None)));

            long second = Begin(ID_RETRY_COMPLETED, REQUEST, SECOND_GOLD, SECOND_GOLD_VERSION);

            Assert.Greater(second, first, "idempotency covers GoldDebited records only");
            Assert.AreEqual(1L, RowCount(ID_RETRY_COMPLETED));
            AssertGold(ID_RETRY_COMPLETED, SECOND_GOLD, SECOND_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_ConcurrentBeginsOfOneRequest_BothReturnTheSameIdWithOneRowAndNoFault()
        {
            Seed(ID_CONCURRENT);
            try
            {
                for (uint i = 0u; i < CONCURRENT_REPEATS; i++)
                {
                    uint request = REQUEST_COUNT_BASE + i;
                    LockCharacterRow(ID_CONCURRENT);
                    Task<long> a = BeginAsync(ID_CONCURRENT, request, FIRST_GOLD, FIRST_GOLD_VERSION, CancellationToken.None);
                    Task<long> b = BeginAsync(ID_CONCURRENT, request, FIRST_GOLD, FIRST_GOLD_VERSION, CancellationToken.None);

                    WaitForLockWaiters(2, "INSERT INTO pending_purchases");
                    Assert.IsFalse(a.IsCompleted || b.IsCompleted, "both calls must still wait for the row lock");

                    ReleaseLock();
                    Assert.IsTrue(Task.WaitAll(new Task[] { a, b }, WAIT_MS), "both calls must complete after the lock is released");
                    Assert.IsFalse(a.IsFaulted || b.IsFaulted, "neither call may fault");
                    Assert.Greater(a.Result, 0L);
                    Assert.AreEqual(a.Result, b.Result);
                    Assert.AreEqual(1L, Raw("SELECT COUNT(*) FROM pending_purchases WHERE char_id = " + ID_CONCURRENT + " AND request_id = " + request));
                }
            }
            finally
            {
                ReleaseLock();
            }
        }

        [Test]
        public void NpgsqlPurchase_BeginForUnknownCharacter_ReturnsZeroWritesNothingAndDoesNotFault()
        {
            long id = Begin(ID_UNKNOWN, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);

            Assert.AreEqual(0L, id);
            Assert.AreEqual(0L, RowCount(ID_UNKNOWN));
        }

        [Test]
        public void NpgsqlPurchase_TwoBegins_ReturnPositiveIncreasingIds()
        {
            Seed(ID_GENERATED);

            long first = Begin(ID_GENERATED, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            long second = Begin(ID_GENERATED, REQUEST + 1u, SECOND_GOLD, SECOND_GOLD_VERSION);

            Assert.Greater(first, 0L);
            Assert.Greater(second, first);
        }

        [Test]
        public void NpgsqlPurchase_CompleteTwice_AppliedThenNotFound()
        {
            Seed(ID_OUTCOME_A);
            long id = Begin(ID_OUTCOME_A, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);

            PendingPurchaseWriteOutcome first = Complete(_purchases.CompletePurchaseAsync(id, Id(ID_OUTCOME_A), CancellationToken.None));
            PendingPurchaseWriteOutcome second = Complete(_purchases.CompletePurchaseAsync(id, Id(ID_OUTCOME_A), CancellationToken.None));

            Assert.AreEqual(PendingPurchaseWriteOutcome.Applied, first);
            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, second);
            Assert.AreEqual(0L, RowCount(ID_OUTCOME_A));
        }

        [Test]
        public void NpgsqlPurchase_RefundOfAnotherCharactersPurchase_ReturnsOtherCharacterAndWritesNothing()
        {
            Seed(ID_OUTCOME_A);
            Seed(ID_OUTCOME_B);
            long id = Begin(ID_OUTCOME_A, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);

            PendingPurchaseWriteOutcome outcome = Complete(_purchases.RefundPurchaseAsync(id, Id(ID_OUTCOME_B), REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));
            PendingPurchaseWriteOutcome completeOutcome = Complete(_purchases.CompletePurchaseAsync(id, Id(ID_OUTCOME_B), CancellationToken.None));

            Assert.AreEqual(PendingPurchaseWriteOutcome.OtherCharacter, outcome);
            Assert.AreEqual(PendingPurchaseWriteOutcome.OtherCharacter, completeOutcome);
            Assert.AreEqual(1L, RowCount(ID_OUTCOME_A), "the purchase must be untouched");
            AssertGold(ID_OUTCOME_A, FIRST_GOLD, FIRST_GOLD_VERSION);
            AssertGold(ID_OUTCOME_B, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_LoadOutstanding_ReturnsOnlyTheCharactersRowsAscendingWithFieldsRoundTripped()
        {
            Seed(ID_LOAD_A);
            Seed(ID_LOAD_B);
            long firstId = Complete(_purchases.BeginPurchaseAsync(
                new PendingPurchaseRecord(0L, Id(ID_LOAD_A), LARGE_REQUEST, LARGE_ITEM, QUANTITY, LARGE_COST, PendingPurchaseState.GoldDebited),
                FIRST_GOLD, FIRST_GOLD_VERSION, CancellationToken.None));
            Begin(ID_LOAD_B, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            long secondId = Begin(ID_LOAD_A, REQUEST, SECOND_GOLD, SECOND_GOLD_VERSION);

            var loaded = Complete(_purchases.LoadOutstandingAsync(Id(ID_LOAD_A), CancellationToken.None));

            Assert.IsNotNull(loaded);
            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual(firstId, loaded[0].Id);
            Assert.AreEqual(secondId, loaded[1].Id);
            Assert.Less(loaded[0].Id, loaded[1].Id);
            Assert.AreEqual(Id(ID_LOAD_A), loaded[0].CharId);
            Assert.AreEqual(LARGE_REQUEST, loaded[0].RequestId);
            Assert.AreEqual(LARGE_ITEM, loaded[0].ItemId);
            Assert.AreEqual(QUANTITY, loaded[0].Quantity);
            Assert.AreEqual(LARGE_COST, loaded[0].TotalCost);
            Assert.AreEqual(REQUEST, loaded[1].RequestId);
            Assert.AreEqual(PendingPurchaseState.GoldDebited, loaded[0].State);
            Assert.AreEqual(PendingPurchaseState.GoldDebited, loaded[1].State);
        }

        [Test]
        public void NpgsqlPurchase_Migration003_CreatesTheUniqueIndexOnCharAndRequest()
        {
            string definition;
            using (DbConnection connection = NpgsqlRecordStoreTestAccess.OpenConnection(_connectionString))
            {
                definition = Convert.ToString(PgTestDatabase.Scalar(connection,
                    "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_pending_purchases_char_request'"));
            }

            StringAssert.Contains("UNIQUE", definition);
            StringAssert.Contains("(char_id, request_id)", definition);
        }

        [Test]
        public void NpgsqlPurchase_BeginCancelledWhileWaitingForTheRowLock_EndsCancelledAndWritesNothing()
        {
            Seed(ID_CANCEL);
            LockCharacterRow(ID_CANCEL);
            using (var cts = new CancellationTokenSource())
            {
                Task<long> task = BeginAsync(ID_CANCEL, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION, cts.Token);
                Assert.IsFalse(task.Wait(STILL_PENDING_MS), "the call must be waiting for the row lock");

                cts.Cancel();

                AssertEndsCancelledWithin(task, WAIT_MS);
            }

            ReleaseLock();
            Assert.AreEqual(0L, RowCount(ID_CANCEL));
            AssertGold(ID_CANCEL, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_CompleteCancelledWhileWaitingForTheRowLock_EndsCancelledAndKeepsTheRow()
        {
            Seed(ID_CANCEL_COMPLETE);
            long id = Begin(ID_CANCEL_COMPLETE, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            LockPurchaseRow(id);
            using (var cts = new CancellationTokenSource())
            {
                Task<PendingPurchaseWriteOutcome> task = _purchases.CompletePurchaseAsync(id, Id(ID_CANCEL_COMPLETE), cts.Token);
                WaitForLockWaiters(1, "DELETE FROM pending_purchases");

                cts.Cancel();

                AssertEndsCancelledWithin(task, WAIT_MS);
            }

            ReleaseLock();
            Assert.AreEqual(1L, RowCount(ID_CANCEL_COMPLETE));
        }

        [Test]
        public void NpgsqlPurchase_RefundCancelledWhileWaitingForTheRowLock_EndsCancelledKeepsTheRowAndGold()
        {
            Seed(ID_CANCEL_REFUND);
            long id = Begin(ID_CANCEL_REFUND, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            LockPurchaseRow(id);
            using (var cts = new CancellationTokenSource())
            {
                Task<PendingPurchaseWriteOutcome> task = _purchases.RefundPurchaseAsync(id, Id(ID_CANCEL_REFUND), REFUND_GOLD, REFUND_GOLD_VERSION, cts.Token);
                WaitForLockWaiters(1, "DELETE FROM pending_purchases");

                cts.Cancel();

                AssertEndsCancelledWithin(task, WAIT_MS);
            }

            ReleaseLock();
            Assert.AreEqual(1L, RowCount(ID_CANCEL_REFUND));
            AssertGold(ID_CANCEL_REFUND, FIRST_GOLD, FIRST_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_LoadCancelledWhileWaitingForTheTableLock_EndsCancelled()
        {
            Seed(ID_CANCEL_LOAD);
            Begin(ID_CANCEL_LOAD, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            HoldLock("LOCK TABLE pending_purchases IN ACCESS EXCLUSIVE MODE");
            using (var cts = new CancellationTokenSource())
            {
                Task<IReadOnlyList<PendingPurchaseRecord>> task = _purchases.LoadOutstandingAsync(Id(ID_CANCEL_LOAD), cts.Token);
                WaitForLockWaiters(1, "SELECT id AS Id");

                cts.Cancel();

                AssertEndsCancelledWithin(task, WAIT_MS);
            }

            ReleaseLock();
        }

        [Test]
        public void NpgsqlPurchase_BeginWhoseGoldUpdateChangesNoRow_FaultsAndLeavesNoRow()
        {
            Seed(ID_SKIP_BEGIN);
            CreateSkippingUpdateTrigger(ID_SKIP_BEGIN);

            Task<long> task = BeginAsync(ID_SKIP_BEGIN, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION, CancellationToken.None);

            AssertFaultsWith<InvalidOperationException>(task);
            Assert.AreEqual(0L, RowCount(ID_SKIP_BEGIN), "no purchase row without its gold write");
            AssertGold(ID_SKIP_BEGIN, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_RefundWhoseGoldUpdateChangesNoRow_FaultsAndKeepsTheRow()
        {
            Seed(ID_SKIP_REFUND);
            long id = Begin(ID_SKIP_REFUND, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            CreateSkippingUpdateTrigger(ID_SKIP_REFUND);

            Task<PendingPurchaseWriteOutcome> task = _purchases.RefundPurchaseAsync(id, Id(ID_SKIP_REFUND), REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None);

            AssertFaultsWith<InvalidOperationException>(task);
            Assert.AreEqual(1L, RowCount(ID_SKIP_REFUND), "the DELETE must have been rolled back");
            AssertGold(ID_SKIP_REFUND, FIRST_GOLD, FIRST_GOLD_VERSION);
        }

        [Test]
        public void NpgsqlPurchase_CompleteWithFailingDelete_FaultsAndKeepsTheRow()
        {
            Seed(ID_FAIL_DELETE);
            long id = Begin(ID_FAIL_DELETE, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            CreateFailingDeleteTrigger(ID_FAIL_DELETE);

            Task<PendingPurchaseWriteOutcome> task = _purchases.CompletePurchaseAsync(id, Id(ID_FAIL_DELETE), CancellationToken.None);

            AssertFaults(task);
            Assert.AreEqual(1L, RowCount(ID_FAIL_DELETE));
        }

        [TestCase("request_id", -1L)]
        [TestCase("item_id", 4_294_967_296L)]
        [TestCase("total_cost", -1L)]
        public void NpgsqlPurchase_LoadOfAColumnOutsideTheUintRange_FaultsWithCorruptRecord(string column, long value)
        {
            Seed(ID_CORRUPT);
            long id = Begin(ID_CORRUPT, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            PgTestDatabase.Execute(_connectionString, "UPDATE pending_purchases SET " + column + " = " + value + " WHERE id = " + id);

            AssertFaultsWith<CharacterRecordCorruptException>(_purchases.LoadOutstandingAsync(Id(ID_CORRUPT), CancellationToken.None));
        }

        [Test]
        public void NpgsqlPurchase_RowInAnotherState_IsNeitherLoadedNorMatchedByARetry()
        {
            Seed(ID_OTHER_STATE);
            long stale = Begin(ID_OTHER_STATE, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION);
            long open = Begin(ID_OTHER_STATE, REQUEST + 1u, SECOND_GOLD, SECOND_GOLD_VERSION);
            PgTestDatabase.Execute(_connectionString, "UPDATE pending_purchases SET state = " + COMPLETED_STATE + " WHERE id = " + stale);

            var loaded = Complete(_purchases.LoadOutstandingAsync(Id(ID_OTHER_STATE), CancellationToken.None));
            Task<long> retry = BeginAsync(ID_OTHER_STATE, REQUEST, FIRST_GOLD, FIRST_GOLD_VERSION, CancellationToken.None);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(open, loaded[0].Id);
            AssertFaultsWith<InvalidOperationException>(retry);
            AssertGold(ID_OTHER_STATE, SECOND_GOLD, SECOND_GOLD_VERSION);
        }
    }
}
