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
    /// Character Persistence Story 011 (ADR-001 Amendment A2): the <see cref="IPendingPurchaseStore"/> contract tests. They
    /// hold for every purchase store; a subclass supplies the store, a way to seed a character row, a way to read it
    /// back and, if it needs one, a clean-up (Story 015 subclasses this for the Npgsql store). Calls are awaited with a bounded wait.
    /// </summary>
    internal abstract class PendingPurchaseStoreContractTests
    {
        // Test-only ids (9_000_000 and up), like the record store contract.
        protected static readonly CharacterID CHAR_A = new CharacterID(9_000_201u);
        protected static readonly CharacterID CHAR_B = new CharacterID(9_000_202u);
        protected static readonly CharacterID CHAR_C = new CharacterID(9_000_203u);
        protected static readonly AccountID ACCOUNT_A = new AccountID(7u);
        protected const uint STORED_GOLD = 500u;
        protected const uint STORED_GOLD_VERSION = 4u;
        protected const uint STORED_SAVE_VERSION = 7u;
        protected const uint AFTER_GOLD = 300u;
        protected const uint AFTER_GOLD_VERSION = 5u;
        protected const uint REFUND_GOLD = 500u;
        protected const uint REFUND_GOLD_VERSION = 6u;
        protected const uint OTHER_GOLD = 999u;
        protected const uint OTHER_GOLD_VERSION = 99u;
        protected const uint ABOVE_STORED_GOLD = 600u;
        protected const uint LOWER_GOLD_VERSION = 1u;
        protected const uint COST = 200u;
        protected const uint REQUEST_ID = 11u;
        protected const uint REQUEST_ID_BASE = 5000u;
        protected const uint ITEM_ID = 42u;
        protected const int QUANTITY = 2;
        protected const long UNKNOWN_PURCHASE_ID = 987654L;
        protected const long INPUT_ID = 777L;
        protected const int WAIT_MS = 5000;

        private uint _requestCounter;

        /// <summary>The store under test, made by <see cref="CreateStore"/>.</summary>
        protected IPendingPurchaseStore Purchases;

        /// <summary>Makes the store for one test.</summary>
        protected abstract IPendingPurchaseStore CreateStore();

        /// <summary>Stores a character row (the store may stamp its own timestamps).</summary>
        protected abstract void SeedCharacter(CharacterRecord record);

        /// <summary>Reads a stored character row back; null when there is none.</summary>
        protected abstract CharacterRecord ReadCharacter(CharacterID id);

        /// <summary>Removes whatever the test left behind (a database store deletes its rows); nothing by default.</summary>
        protected virtual void CleanUp()
        {
        }

        [SetUp]
        public void SetUpPurchaseContract()
        {
            _requestCounter = 0u;
            Purchases = CreateStore();
        }

        [TearDown]
        public void TearDownPurchaseContract()
        {
            CleanUp();
        }

        protected void SeedStandard(CharacterID id)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, id, 3);
            r.CharacterName = "Brann" + id.RawValue;
            r.GoldBalance = STORED_GOLD;
            r.GoldVersion = STORED_GOLD_VERSION;
            r.SaveVersion = STORED_SAVE_VERSION;
            SeedCharacter(r);
        }

        protected static PendingPurchaseRecord NewRecord(CharacterID id, uint requestId = REQUEST_ID)
        {
            return new PendingPurchaseRecord(0L, id, requestId, ITEM_ID, QUANTITY, COST, PendingPurchaseState.GoldDebited);
        }

        protected uint NextRequestId()
        {
            return REQUEST_ID_BASE + _requestCounter++;
        }

        protected static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within 5 s.");
            return task.Result;
        }

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

            Assert.IsTrue(task.IsCompleted, "The task did not end within 5 s.");
        }

        /// <summary>Begins a purchase with a request id that no earlier call of this test used.</summary>
        protected long Begin(CharacterID id, uint gold, uint goldVersion)
        {
            return BeginRequest(id, NextRequestId(), gold, goldVersion);
        }

        protected long BeginRequest(CharacterID id, uint requestId, uint gold, uint goldVersion)
        {
            return Complete(Purchases.BeginPurchaseAsync(NewRecord(id, requestId), gold, goldVersion, CancellationToken.None));
        }

        protected IReadOnlyList<PendingPurchaseRecord> Outstanding(CharacterID id)
        {
            return Complete(Purchases.LoadOutstandingAsync(id, CancellationToken.None));
        }

        protected PendingPurchaseWriteOutcome Refund(long purchaseId, CharacterID id, uint gold, uint goldVersion)
        {
            return Complete(Purchases.RefundPurchaseAsync(purchaseId, id, gold, goldVersion, CancellationToken.None));
        }

        protected PendingPurchaseWriteOutcome CompleteRecord(long purchaseId, CharacterID id)
        {
            return Complete(Purchases.CompletePurchaseAsync(purchaseId, id, CancellationToken.None));
        }

        protected void AssertGold(CharacterID id, uint gold, uint goldVersion, uint saveVersion = STORED_SAVE_VERSION)
        {
            CharacterRecord r = ReadCharacter(id);
            Assert.IsNotNull(r);
            Assert.AreEqual(gold, r.GoldBalance, "gold balance");
            Assert.AreEqual(goldVersion, r.GoldVersion, "gold version");
            Assert.AreEqual(saveVersion, r.SaveVersion, "save version");
        }

        [Test]
        public void BeginPurchase_StoredCharacter_WritesGoldKeepsSaveVersionAndStoresOneGoldDebitedRecord()
        {
            SeedStandard(CHAR_A);

            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            Assert.Greater(id, 0L);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            IReadOnlyList<PendingPurchaseRecord> list = Outstanding(CHAR_A);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(id, list[0].Id);
            Assert.AreEqual(PendingPurchaseState.GoldDebited, list[0].State);
            Assert.AreEqual(CHAR_A, list[0].CharId);
            Assert.AreEqual(COST, list[0].TotalCost);
            Assert.AreEqual(ITEM_ID, list[0].ItemId);
            Assert.AreEqual(QUANTITY, list[0].Quantity);
        }

        [Test]
        public void BeginPurchase_GoldBalanceZero_StoresZero()
        {
            SeedStandard(CHAR_A);

            long id = Begin(CHAR_A, 0u, AFTER_GOLD_VERSION);

            Assert.Greater(id, 0L);
            AssertGold(CHAR_A, 0u, AFTER_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_GoldBalanceAboveStored_IsWrittenAsGivenWithNoCheck()
        {
            SeedStandard(CHAR_A);

            long id = Begin(CHAR_A, ABOVE_STORED_GOLD, AFTER_GOLD_VERSION);

            Assert.Greater(id, 0L);
            AssertGold(CHAR_A, ABOVE_STORED_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_GoldVersionAtOrBelowStored_IsWrittenAsGiven()
        {
            SeedStandard(CHAR_A);

            Assert.Greater(Begin(CHAR_A, AFTER_GOLD, STORED_GOLD_VERSION), 0L);
            AssertGold(CHAR_A, AFTER_GOLD, STORED_GOLD_VERSION);
            Assert.Greater(Begin(CHAR_A, AFTER_GOLD, LOWER_GOLD_VERSION), 0L);
            AssertGold(CHAR_A, AFTER_GOLD, LOWER_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_UnknownCharacter_ReturnsZeroAndStoresNothing()
        {
            SeedStandard(CHAR_A);

            long id = Begin(CHAR_B, AFTER_GOLD, AFTER_GOLD_VERSION);

            Assert.AreEqual(0L, id);
            Assert.IsNull(ReadCharacter(CHAR_B));
            Assert.AreEqual(0, Outstanding(CHAR_B).Count);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_ThreeCalls_GiveDistinctIncreasingIds()
        {
            SeedStandard(CHAR_A);

            long first = Begin(CHAR_A, 400u, 5u);
            long second = Begin(CHAR_A, AFTER_GOLD, 6u);
            long third = Begin(CHAR_A, 200u, 7u);

            Assert.Greater(first, 0L);
            Assert.Greater(second, first);
            Assert.Greater(third, second);
            Assert.AreEqual(3, Outstanding(CHAR_A).Count);
        }

        [Test]
        public void BeginPurchase_SameRequestIdTwice_ReturnsSameIdAndWritesNothingTheSecondTime()
        {
            SeedStandard(CHAR_A);
            long first = BeginRequest(CHAR_A, REQUEST_ID, AFTER_GOLD, AFTER_GOLD_VERSION);

            long second = BeginRequest(CHAR_A, REQUEST_ID, OTHER_GOLD, OTHER_GOLD_VERSION);

            Assert.Greater(first, 0L);
            Assert.AreEqual(first, second);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_SameRequestIdForAnotherCharacter_IsANewPurchase()
        {
            SeedStandard(CHAR_A);
            SeedStandard(CHAR_B);
            long a = BeginRequest(CHAR_A, REQUEST_ID, AFTER_GOLD, AFTER_GOLD_VERSION);

            long b = BeginRequest(CHAR_B, REQUEST_ID, AFTER_GOLD, AFTER_GOLD_VERSION);

            Assert.AreNotEqual(a, b);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            Assert.AreEqual(1, Outstanding(CHAR_B).Count);
        }

        [Test]
        public void BeginPurchase_InputIdAndState_AreIgnored()
        {
            SeedStandard(CHAR_A);
            var record = new PendingPurchaseRecord(INPUT_ID, CHAR_A, REQUEST_ID, ITEM_ID, QUANTITY, COST, PendingPurchaseState.Refunded);

            long id = Complete(Purchases.BeginPurchaseAsync(record, AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.Greater(id, 0L);
            Assert.AreNotEqual(INPUT_ID, id);
            IReadOnlyList<PendingPurchaseRecord> list = Outstanding(CHAR_A);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(id, list[0].Id);
            Assert.AreEqual(PendingPurchaseState.GoldDebited, list[0].State);
        }

        [Test]
        public void BeginPurchase_WritingGold_LeavesTimestampsAndOtherFieldsOfTheRowUntouched()
        {
            SeedStandard(CHAR_A);
            CharacterRecord before = ReadCharacter(CHAR_A);

            Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterRecord after = ReadCharacter(CHAR_A);
            Assert.AreEqual(before.CreatedAtUtc, after.CreatedAtUtc);
            Assert.AreEqual(before.LastSavedAtUtc, after.LastSavedAtUtc);
            Assert.AreEqual(before.Level, after.Level);
            Assert.AreEqual(before.CharacterName, after.CharacterName);
        }

        [Test]
        public void CompletePurchase_ExistingRecord_RemovesItLeavesGoldAndRepeatReturnsNotFound()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            PendingPurchaseWriteOutcome first = CompleteRecord(id, CHAR_A);
            PendingPurchaseWriteOutcome second = CompleteRecord(id, CHAR_A);

            Assert.AreEqual(PendingPurchaseWriteOutcome.Applied, first);
            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, second);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void CompletePurchase_UnknownId_ReturnsNotFoundAndChangesNothing()
        {
            SeedStandard(CHAR_A);

            PendingPurchaseWriteOutcome outcome = CompleteRecord(UNKNOWN_PURCHASE_ID, CHAR_A);

            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, outcome);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void CompletePurchase_OtherCharactersId_ReturnsOtherCharacterAndKeepsRecord()
        {
            SeedStandard(CHAR_A);
            SeedStandard(CHAR_B);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            PendingPurchaseWriteOutcome outcome = CompleteRecord(id, CHAR_B);

            Assert.AreEqual(PendingPurchaseWriteOutcome.OtherCharacter, outcome);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            AssertGold(CHAR_B, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_ExistingRecord_RemovesItWritesGoldAndRepeatChangesNothing()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            PendingPurchaseWriteOutcome first = Refund(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);
            AssertGold(CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);
            PendingPurchaseWriteOutcome second = Refund(id, CHAR_A, OTHER_GOLD, OTHER_GOLD_VERSION);

            Assert.AreEqual(PendingPurchaseWriteOutcome.Applied, first);
            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, second);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_IdThatNeverExisted_ReturnsNotFoundWritesNoGold()
        {
            SeedStandard(CHAR_A);
            SeedStandard(CHAR_B);

            PendingPurchaseWriteOutcome sameCharacter = Refund(UNKNOWN_PURCHASE_ID, CHAR_A, OTHER_GOLD, OTHER_GOLD_VERSION);
            PendingPurchaseWriteOutcome otherCharacter = Refund(UNKNOWN_PURCHASE_ID, CHAR_B, OTHER_GOLD, OTHER_GOLD_VERSION);

            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, sameCharacter);
            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, otherCharacter);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
            AssertGold(CHAR_B, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_OtherCharactersPurchase_ReturnsOtherCharacterAndChangesNothing()
        {
            SeedStandard(CHAR_A);
            SeedStandard(CHAR_B);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            PendingPurchaseWriteOutcome outcome = Refund(id, CHAR_B, OTHER_GOLD, OTHER_GOLD_VERSION);

            Assert.AreEqual(PendingPurchaseWriteOutcome.OtherCharacter, outcome);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            AssertGold(CHAR_B, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void CompleteThenRefund_SameId_RefundReturnsNotFoundAndWritesNoGold()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            CompleteRecord(id, CHAR_A);

            PendingPurchaseWriteOutcome outcome = Refund(id, CHAR_A, OTHER_GOLD, OTHER_GOLD_VERSION);

            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, outcome);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void RefundThenComplete_SameId_CompleteReturnsNotFoundAndGoldStaysRefunded()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            Refund(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);

            PendingPurchaseWriteOutcome outcome = CompleteRecord(id, CHAR_A);

            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, outcome);
            AssertGold(CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);
        }

        [Test]
        public void LoadOutstanding_TwoForATwoForBNoneForC_ReturnsOnlyTheCharactersOwnAndEmptyNotNull()
        {
            SeedStandard(CHAR_A);
            SeedStandard(CHAR_B);
            long a1 = Begin(CHAR_A, 400u, 5u);
            long a2 = Begin(CHAR_A, AFTER_GOLD, 6u);
            long b1 = Begin(CHAR_B, 400u, 5u);

            IReadOnlyList<PendingPurchaseRecord> forA = Outstanding(CHAR_A);
            IReadOnlyList<PendingPurchaseRecord> forB = Outstanding(CHAR_B);
            IReadOnlyList<PendingPurchaseRecord> forC = Outstanding(CHAR_C);

            Assert.AreEqual(2, forA.Count);
            CollectionAssert.AreEquivalent(new[] { a1, a2 }, new[] { forA[0].Id, forA[1].Id });
            Assert.AreEqual(1, forB.Count);
            Assert.AreEqual(b1, forB[0].Id);
            Assert.IsNotNull(forC);
            Assert.AreEqual(0, forC.Count);
        }

        [Test]
        public void LoadOutstanding_AfterCompleteAndRefund_ExcludesTheRemovedRecords()
        {
            SeedStandard(CHAR_A);
            long kept = Begin(CHAR_A, 400u, 5u);
            long completed = Begin(CHAR_A, AFTER_GOLD, 6u);
            long refunded = Begin(CHAR_A, 200u, 7u);
            CompleteRecord(completed, CHAR_A);
            Refund(refunded, CHAR_A, REFUND_GOLD, 8u);

            IReadOnlyList<PendingPurchaseRecord> list = Outstanding(CHAR_A);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(kept, list[0].Id);
        }

        [Test]
        public void LoadOutstanding_ReturnsAscendingIds()
        {
            SeedStandard(CHAR_A);
            for (int i = 0; i < 5; i++)
            {
                Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            }

            IReadOnlyList<PendingPurchaseRecord> list = Outstanding(CHAR_A);

            Assert.AreEqual(5, list.Count);
            for (int i = 1; i < list.Count; i++)
            {
                Assert.Greater(list[i].Id, list[i - 1].Id);
            }
        }

        [Test]
        public void LoadOutstanding_ReturnedListIsACopy()
        {
            SeedStandard(CHAR_A);
            Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            IReadOnlyList<PendingPurchaseRecord> first = Outstanding(CHAR_A);

            if (first is IList<PendingPurchaseRecord> mutable && !mutable.IsReadOnly)
            {
                mutable.Clear();
            }

            IReadOnlyList<PendingPurchaseRecord> second = Outstanding(CHAR_A);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(1, second.Count);
        }

        [Test]
        public void PurchaseCalls_AlreadyCancelledToken_AreCancelledAndApplyNothing()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Task<long> begin = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A, NextRequestId()), OTHER_GOLD, OTHER_GOLD_VERSION, cts.Token);
            Task<PendingPurchaseWriteOutcome> complete = Purchases.CompletePurchaseAsync(id, CHAR_A, cts.Token);
            Task<PendingPurchaseWriteOutcome> refund = Purchases.RefundPurchaseAsync(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, cts.Token);
            Task<IReadOnlyList<PendingPurchaseRecord>> load = Purchases.LoadOutstandingAsync(CHAR_A, cts.Token);

            Assert.IsTrue(begin.IsCanceled);
            Assert.IsTrue(complete.IsCanceled);
            Assert.IsTrue(refund.IsCanceled);
            Assert.IsTrue(load.IsCanceled);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }
    }

    /// <summary>The in-memory run of the purchase store contract, plus the in-memory store's fault controls.</summary>
    [TestFixture]
    internal sealed class CharacterPersistence_PendingPurchaseStore_InMemory_Tests : PendingPurchaseStoreContractTests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);
        private const int PARALLEL_BEGINS = 32;
        private InMemoryCharacterRecordStore _store;

        protected override IPendingPurchaseStore CreateStore()
        {
            _store = new InMemoryCharacterRecordStore(() => T1);
            return _store;
        }

        protected override void SeedCharacter(CharacterRecord record)
        {
            Assert.IsTrue(Complete(_store.InsertAsync(record, CancellationToken.None)));
        }

        protected override CharacterRecord ReadCharacter(CharacterID id)
        {
            return Complete(_store.FetchAsync(id, CancellationToken.None));
        }

        // A Begin that must still get the armed fault, and so leaves no record behind.
        private void AssertFailAfterInsertStillArmed()
        {
            int before = Outstanding(CHAR_A).Count;
            Task<long> task = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A, NextRequestId()), OTHER_GOLD, OTHER_GOLD_VERSION, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted, "the armed fault must still be pending");
            Assert.AreEqual(before, Outstanding(CHAR_A).Count);
        }

        [Test]
        public void BeginPurchase_FailNextAfterInsert_FaultsAndAppliesNeitherRecordNorGold()
        {
            SeedStandard(CHAR_A);
            var boom = new InvalidOperationException("after insert");
            _store.FailNextAfterInsert(boom);

            Task<long> task = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted);
            Assert.AreSame(boom, task.Exception.InnerException);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_FailNext_FaultsAndAppliesNothing()
        {
            SeedStandard(CHAR_A);
            _store.FailNext(new InvalidOperationException("whole call"));

            Task<long> task = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_FailNextAfterInsert_FaultsAndLeavesRecordAndGold()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            _store.FailNextAfterInsert(new InvalidOperationException("after delete"));

            Task<PendingPurchaseWriteOutcome> task = Purchases.RefundPurchaseAsync(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            Assert.AreEqual(id, Outstanding(CHAR_A)[0].Id);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_FailNext_FaultsAndLeavesRecordAndGold()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            _store.FailNext(new InvalidOperationException("whole call"));

            Task<PendingPurchaseWriteOutcome> task = Purchases.RefundPurchaseAsync(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void FailNextAfterInsert_FetchOutstandingAndUnknownIdComplete_DoNotConsumeIt()
        {
            SeedStandard(CHAR_A);
            Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            _store.FailNextAfterInsert(new InvalidOperationException("armed"));

            Assert.IsNotNull(Complete(_store.FetchAsync(CHAR_A, CancellationToken.None)));
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, CompleteRecord(UNKNOWN_PURCHASE_ID, CHAR_A));

            AssertFailAfterInsertStillArmed();
        }

        [Test]
        public void FailNextAfterInsert_BeginForUnknownCharacter_IsNotConsumed()
        {
            SeedStandard(CHAR_A);
            _store.FailNextAfterInsert(new InvalidOperationException("armed"));

            Assert.AreEqual(0L, Begin(CHAR_B, AFTER_GOLD, AFTER_GOLD_VERSION));

            AssertFailAfterInsertStillArmed();
        }

        [Test]
        public void FailNextAfterInsert_RefundReturningNotFound_IsNotConsumed()
        {
            SeedStandard(CHAR_A);
            _store.FailNextAfterInsert(new InvalidOperationException("armed"));

            Assert.AreEqual(PendingPurchaseWriteOutcome.NotFound, Refund(UNKNOWN_PURCHASE_ID, CHAR_A, OTHER_GOLD, OTHER_GOLD_VERSION));

            AssertFailAfterInsertStillArmed();
        }

        [Test]
        public void FailNextAfterInsert_RefundReturningOtherCharacter_IsNotConsumed()
        {
            SeedStandard(CHAR_A);
            SeedStandard(CHAR_B);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            _store.FailNextAfterInsert(new InvalidOperationException("armed"));

            Assert.AreEqual(PendingPurchaseWriteOutcome.OtherCharacter, Refund(id, CHAR_B, OTHER_GOLD, OTHER_GOLD_VERSION));

            AssertFailAfterInsertStillArmed();
        }

        [Test]
        public void FailNextAfterInsert_CompleteOfAnExistingRecord_IsNotConsumed()
        {
            SeedStandard(CHAR_A);
            long id = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            _store.FailNextAfterInsert(new InvalidOperationException("armed"));

            Assert.AreEqual(PendingPurchaseWriteOutcome.Applied, CompleteRecord(id, CHAR_A));

            AssertFailAfterInsertStillArmed();
        }

        [Test]
        public void FailNextAndFailNextAfterInsert_BothArmed_FireIndependentlyInOrder()
        {
            SeedStandard(CHAR_A);
            var whole = new InvalidOperationException("whole call");
            var afterInsert = new InvalidOperationException("after insert");
            _store.FailNext(whole);
            _store.FailNextAfterInsert(afterInsert);

            Task<long> first = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A, NextRequestId()), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);
            Settle(first);
            Task<long> second = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A, NextRequestId()), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);
            Settle(second);
            long third = Begin(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            Assert.AreSame(whole, first.Exception.InnerException);
            Assert.AreSame(afterInsert, second.Exception.InnerException);
            Assert.Greater(third, 0L);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
        }

        [Test]
        public void BeginPurchase_HoldNext_CompletesOnlyAfterReleaseAndThenApplies()
        {
            SeedStandard(CHAR_A);
            Action release = _store.HoldNext();

            Task<long> task = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);

            Assert.IsFalse(task.IsCompleted);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
            release();
            Assert.Greater(Complete(task), 0L);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_HeldThenSaveVersionOverwritten_WritesGoldAsGivenAndKeepsTheChangedSaveVersion()
        {
            const uint CHANGED_SAVE_VERSION = 9u;
            SeedStandard(CHAR_A);
            Action release = _store.HoldNext();
            Task<long> task = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, CHANGED_SAVE_VERSION));

            release();

            Assert.Greater(Complete(task), 0L);
            AssertGold(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION, CHANGED_SAVE_VERSION);
        }

        [Test]
        public void BeginPurchase_HeldThenTokenCancelledBeforeRelease_IsCancelledAndApplyNothing()
        {
            SeedStandard(CHAR_A);
            var cts = new CancellationTokenSource();
            Action release = _store.HoldNext();
            Task<long> task = Purchases.BeginPurchaseAsync(NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, cts.Token);

            cts.Cancel();
            release();
            Settle(task);

            Assert.IsTrue(task.IsCanceled);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertGold(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_ParallelCallsWithDistinctRequestIds_GiveDistinctIdsAndOneRecordEach()
        {
            SeedStandard(CHAR_A);
            var tasks = new Task<long>[PARALLEL_BEGINS];
            for (int i = 0; i < PARALLEL_BEGINS; i++)
            {
                uint requestId = NextRequestId();
                tasks[i] = Task.Run(() => Purchases.BeginPurchaseAsync(NewRecord(CHAR_A, requestId), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));
            }

            Assert.IsTrue(Task.WaitAll(tasks, WAIT_MS), "The parallel begins did not finish within 5 s.");

            var ids = new HashSet<long>();
            foreach (Task<long> t in tasks)
            {
                Assert.Greater(t.Result, 0L);
                ids.Add(t.Result);
            }

            Assert.AreEqual(PARALLEL_BEGINS, ids.Count);
            Assert.AreEqual(PARALLEL_BEGINS, Outstanding(CHAR_A).Count);
        }
    }

    /// <summary>
    /// An <see cref="ICharacterRecordStore"/> and <see cref="IPendingPurchaseStore"/> forwarding to an in-memory store; its
    /// <c>BeginPurchaseAsync</c> can ignore the token it is given, as a commit that wins against the service's timeout cancel.
    /// </summary>
    internal sealed class TokenIgnoringPurchaseStore : ICharacterRecordStore, IPendingPurchaseStore
    {
        private readonly InMemoryCharacterRecordStore _inner;

        public TokenIgnoringPurchaseStore(InMemoryCharacterRecordStore inner)
        {
            _inner = inner;
        }

        public bool IgnoreBeginToken { get; set; }

        public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct) => _inner.FetchAsync(id, ct);

        public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => _inner.InsertAsync(record, ct);

        public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct) => _inner.UpdateAsync(record, expectedSaveVersion, ct);

        public Task<long> BeginPurchaseAsync(PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct)
            => _inner.BeginPurchaseAsync(record, goldBalance, goldVersion, IgnoreBeginToken ? CancellationToken.None : ct);

        public Task<PendingPurchaseWriteOutcome> CompletePurchaseAsync(long purchaseId, CharacterID charId, CancellationToken ct)
            => _inner.CompletePurchaseAsync(purchaseId, charId, ct);

        public Task<PendingPurchaseWriteOutcome> RefundPurchaseAsync(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct)
            => _inner.RefundPurchaseAsync(purchaseId, charId, goldBalance, goldVersion, ct);

        public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingAsync(CharacterID charId, CancellationToken ct)
            => _inner.LoadOutstandingAsync(charId, ct);
    }

    /// <summary>
    /// Character Persistence Story 011: the purchase methods of <see cref="CharacterPersistenceService"/> over the in-memory
    /// store (ADR-001 Amendment A2): queueing, outcome classification, idempotent retry and independence from the character
    /// record. The service is fully wired (the purchase writes need the write queue). The only wall-clock elements are the
    /// short injected write timeouts (100 ms) in the timeout tests.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_PendingPurchase_Service_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_B = new CharacterID(202u);
        private static readonly CharacterID CHAR_UNKNOWN = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;
        private const uint STORED_GOLD = 500u;
        private const uint STORED_GOLD_VERSION = 4u;
        private const uint STORED_SAVE_VERSION = 7u;
        private const uint AFTER_GOLD = 300u;
        private const uint AFTER_GOLD_VERSION = 5u;
        private const uint REFUND_GOLD = 500u;
        private const uint REFUND_GOLD_VERSION = 6u;
        private const uint OTHER_GOLD = 999u;
        private const uint OTHER_GOLD_VERSION = 99u;
        private const uint COST = 200u;
        private const uint REQUEST_ID = 11u;
        private const uint ITEM_ID = 42u;
        private const int QUANTITY = 2;
        private const long UNKNOWN_PURCHASE_ID = 987654L;
        private const int WAIT_MS = 5000;
        private const int SHORT_TIMEOUT_MS = 100;

        private sealed class Rig
        {
            public CharacterPersistenceService Service;
            public RecordingInventory Inventory;
            public CurrencySystem Currency;
        }

        private InMemoryCharacterRecordStore _store;
        private Rig _rig;
        private CharacterPersistenceService _service;
        private uint _requestCounter;

        [SetUp]
        public void SetUp()
        {
            _requestCounter = 0u;
            _store = new InMemoryCharacterRecordStore(() => T1);
            _rig = BuildRig(_store, TimeSpan.FromSeconds(5));
            _service = _rig.Service;
            Seed(CHAR_A);
            Seed(CHAR_B);
        }

        private static Rig BuildRig(ICharacterRecordStore store, TimeSpan writeTimeout)
        {
            var leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            var rig = new Rig
            {
                Currency = new CurrencySystem(),
                Inventory = new RecordingInventory { ExportResult = InventorySnapshot.Empty },
            };
            rig.Service = new CharacterPersistenceService(
                store, stats, leveling, rig.Inventory, rig.Currency, new FakeEquipmentService(), new FakeZoneLocator(), new CharacterWriteQueue(), () => T1, writeTimeout);
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

        private void Seed(CharacterID id)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, id, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann" + id.RawValue;
            // Same validated values as the SaveSession tests' seed, so the wired service can load the character.
            r.Level = 10;
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
            r.LastZoneID = LOADED_ZONE;
            r.GoldBalance = STORED_GOLD;
            r.GoldVersion = STORED_GOLD_VERSION;
            r.SaveVersion = STORED_SAVE_VERSION;
            Assert.IsTrue(Complete(_store.InsertAsync(r, CancellationToken.None)));
        }

        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within 5 s.");
            return task.Result;
        }

        private static void Settle(Task task)
        {
            try
            {
                task.Wait(WAIT_MS);
            }
            catch (AggregateException)
            {
                // The outcome is the test's to inspect.
            }

            Assert.IsTrue(task.IsCompleted, "The task did not end within 5 s.");
        }

        private uint NextRequestId()
        {
            return REQUEST_ID + _requestCounter++;
        }

        private static PendingPurchaseRecord NewRecord(CharacterID id, uint requestId = REQUEST_ID)
        {
            return new PendingPurchaseRecord(0L, id, requestId, ITEM_ID, QUANTITY, COST, PendingPurchaseState.GoldDebited);
        }

        private static CancellationToken Cancelled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            return cts.Token;
        }

        // Each store failure, unknown outcome and unknown character logs exactly one error.
        private static void ExpectError(string label)
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[CharacterPersistence\] " + label + " for "));
        }

        private CharacterRecord Stored(CharacterID id)
        {
            return Complete(_store.FetchAsync(id, CancellationToken.None));
        }

        private void AssertStored(CharacterID id, uint gold, uint goldVersion, uint saveVersion = STORED_SAVE_VERSION)
        {
            CharacterRecord r = Stored(id);
            Assert.AreEqual(gold, r.GoldBalance, "gold balance");
            Assert.AreEqual(goldVersion, r.GoldVersion, "gold version");
            Assert.AreEqual(saveVersion, r.SaveVersion, "save version");
        }

        private IReadOnlyList<PendingPurchaseRecord> Outstanding(CharacterID id)
        {
            return Complete(_service.LoadOutstandingPurchases(id, CancellationToken.None));
        }

        private long BeginOk(CharacterID id, uint gold, uint goldVersion)
        {
            return BeginOkRequest(id, NextRequestId(), gold, goldVersion);
        }

        private long BeginOkRequest(CharacterID id, uint requestId, uint gold, uint goldVersion)
        {
            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(id, NewRecord(id, requestId), gold, goldVersion, CancellationToken.None));
            Assert.AreEqual(PendingPurchaseResult.Success, r.Result);
            return r.PurchaseId;
        }

        private CharacterRecord LoadInto(Rig rig, CharacterID id)
        {
            CharacterLoadResult result = Complete(rig.Service.LoadCharacter(id, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Service.ApplyLoadedRecord(CharacterStatsFixture.PlayerEntityId, result.Record));
            return result.Record;
        }

        // ---------------------------------------------------------------- BeginPurchase

        [Test]
        public void BeginPurchase_StoredCharacter_ReturnsSuccessWithIdAndStoresGoldKeepingSaveVersion()
        {
            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.Success, r.Result);
            Assert.Greater(r.PurchaseId, 0L);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
        }

        [Test]
        public void BeginPurchase_ThreeCalls_GiveDistinctIncreasingIds()
        {
            long first = BeginOk(CHAR_A, 400u, 5u);
            long second = BeginOk(CHAR_A, AFTER_GOLD, 6u);
            long third = BeginOk(CHAR_A, 200u, 7u);

            Assert.Greater(second, first);
            Assert.Greater(third, second);
        }

        [Test]
        public void BeginPurchase_GoldAboveStoredAndZero_AreWrittenAsGiven()
        {
            BeginOk(CHAR_A, 600u, 5u);
            AssertStored(CHAR_A, 600u, 5u);

            BeginOk(CHAR_A, 0u, 6u);
            AssertStored(CHAR_A, 0u, 6u);
        }

        [Test]
        public void BeginPurchase_UnknownCharacter_ReturnsDatabaseErrorWithIdZeroAndStoresNothing()
        {
            ExpectError("BeginPurchase");

            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(CHAR_UNKNOWN, NewRecord(CHAR_UNKNOWN), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.DatabaseError, r.Result);
            Assert.AreEqual(0L, r.PurchaseId);
            Assert.AreEqual(0, Outstanding(CHAR_UNKNOWN).Count);
        }

        [Test]
        public void BeginPurchase_FailNextAfterInsert_ReturnsDatabaseErrorAndAppliesNothing()
        {
            ExpectError("BeginPurchase");
            _store.FailNextAfterInsert(new InvalidOperationException("after insert"));

            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.DatabaseError, r.Result);
            Assert.AreEqual(0L, r.PurchaseId);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_FailNext_ReturnsDatabaseErrorAndAppliesNothing()
        {
            ExpectError("BeginPurchase");
            _store.FailNext(new InvalidOperationException("whole call"));

            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.DatabaseError, r.Result);
            Assert.AreEqual(0L, r.PurchaseId);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_TokenCancelledAtEntry_ReturnsDatabaseErrorAndNeverCallsTheStore()
        {
            _store.FailNext(new InvalidOperationException("must stay armed"));

            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, Cancelled()));

            Assert.AreEqual(PendingPurchaseResult.DatabaseError, r.Result);
            Assert.AreEqual(0L, r.PurchaseId);
            Task<CharacterRecord> probe = _store.FetchAsync(CHAR_A, CancellationToken.None);
            Settle(probe);
            Assert.IsTrue(probe.IsFaulted, "the store was called: the armed fault was consumed");
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_TokenCancelledWhileWaitingInTheQueue_ReturnsDatabaseErrorAndAppliesNothing()
        {
            LoadInto(_rig, CHAR_A);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> save = _service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            var cts = new CancellationTokenSource();
            Task<PendingPurchaseBeginResult> begin = _service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, cts.Token);
            Assert.IsFalse(begin.IsCompleted);

            cts.Cancel();
            PendingPurchaseBeginResult r = Complete(begin);
            release();
            Assert.AreEqual(CharacterSaveResult.Success, Complete(save));

            Assert.AreEqual(PendingPurchaseResult.DatabaseError, r.Result);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            Assert.AreEqual(STORED_GOLD, Stored(CHAR_A).GoldBalance);
        }

        [Test]
        public void BeginPurchase_RecordOfAnotherCharacter_ThrowsArgumentExceptionSynchronously()
        {
            Assert.Throws<ArgumentException>(() => _service.BeginPurchase(CHAR_A, NewRecord(CHAR_B), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            Assert.AreEqual(0, Outstanding(CHAR_B).Count);
        }

        [Test]
        public void BeginPurchase_RetryWithTheSameRequestId_ReturnsSuccessWithTheSameIdAndWritesNothing()
        {
            long first = BeginOkRequest(CHAR_A, REQUEST_ID, AFTER_GOLD, AFTER_GOLD_VERSION);

            long second = BeginOkRequest(CHAR_A, REQUEST_ID, OTHER_GOLD, OTHER_GOLD_VERSION);

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        // ---------------------------------------------------------------- CompletePurchase

        [Test]
        public void CompletePurchase_ExistingAndRepeat_BothReturnSuccessAndGoldIsUnchanged()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult first = Complete(_service.CompletePurchase(id, CHAR_A, CancellationToken.None));
            CharacterSaveResult second = Complete(_service.CompletePurchase(id, CHAR_A, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, first);
            Assert.AreEqual(CharacterSaveResult.Success, second);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void CompletePurchase_IdThatNeverExisted_ReturnsSuccessAndChangesNothing()
        {
            CharacterSaveResult result = Complete(_service.CompletePurchase(UNKNOWN_PURCHASE_ID, CHAR_A, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            AssertStored(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void CompletePurchase_PurchaseOfAnotherCharacter_ReturnsCharacterNotFoundAndKeepsTheRecord()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult result = Complete(_service.CompletePurchase(id, CHAR_B, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void CompletePurchase_FailNext_ReturnsDatabaseErrorAndKeepsTheRecord()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            ExpectError("CompletePurchase");
            _store.FailNext(new InvalidOperationException("whole call"));

            CharacterSaveResult result = Complete(_service.CompletePurchase(id, CHAR_A, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
        }

        [Test]
        public void CompletePurchase_TokenCancelledAtEntry_ReturnsDatabaseErrorAndKeepsTheRecord()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult result = Complete(_service.CompletePurchase(id, CHAR_A, Cancelled()));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        // ---------------------------------------------------------------- RefundPurchase

        [Test]
        public void RefundPurchase_ExistingAndRepeat_RemovesRecordWritesGoldOnceAndBothReturnSuccess()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult first = Complete(_service.RefundPurchase(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));
            AssertStored(CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);
            CharacterSaveResult second = Complete(_service.RefundPurchase(id, CHAR_A, OTHER_GOLD, OTHER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, first);
            Assert.AreEqual(CharacterSaveResult.Success, second);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_IdThatNeverExisted_ReturnsSuccessAndWritesNoGold()
        {
            CharacterSaveResult result = Complete(_service.RefundPurchase(UNKNOWN_PURCHASE_ID, CHAR_A, OTHER_GOLD, OTHER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            AssertStored(CHAR_A, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_FailNextAfterInsert_ReturnsDatabaseErrorAndLeavesRecordAndGold()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            ExpectError("RefundPurchase");
            _store.FailNextAfterInsert(new InvalidOperationException("after delete"));

            CharacterSaveResult result = Complete(_service.RefundPurchase(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_FailNext_ReturnsDatabaseErrorAndLeavesRecordAndGold()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            ExpectError("RefundPurchase");
            _store.FailNext(new InvalidOperationException("whole call"));

            CharacterSaveResult result = Complete(_service.RefundPurchase(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_PurchaseOfAnotherCharacter_ReturnsCharacterNotFoundAndChangesNothing()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult result = Complete(_service.RefundPurchase(id, CHAR_B, OTHER_GOLD, OTHER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            AssertStored(CHAR_B, STORED_GOLD, STORED_GOLD_VERSION);
        }

        [Test]
        public void RefundPurchase_TokenCancelledAtEntry_ReturnsDatabaseErrorAndAppliesNothing()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult result = Complete(_service.RefundPurchase(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, Cancelled()));

            Assert.AreEqual(CharacterSaveResult.DatabaseError, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        // ---------------------------------------------------------------- LoadOutstandingPurchases

        [Test]
        public void LoadOutstandingPurchases_TwoForATwoForBNoneForUnknown_ReturnsOnlyTheCharactersOwnAndEmptyNotNull()
        {
            long a1 = BeginOk(CHAR_A, 400u, 5u);
            long a2 = BeginOk(CHAR_A, AFTER_GOLD, 6u);
            long b1 = BeginOk(CHAR_B, 400u, 5u);

            IReadOnlyList<PendingPurchaseRecord> forA = Outstanding(CHAR_A);
            IReadOnlyList<PendingPurchaseRecord> forB = Outstanding(CHAR_B);
            IReadOnlyList<PendingPurchaseRecord> none = Outstanding(CHAR_UNKNOWN);

            Assert.AreEqual(2, forA.Count);
            Assert.AreEqual(a1, forA[0].Id);
            Assert.AreEqual(a2, forA[1].Id);
            Assert.AreEqual(1, forB.Count);
            Assert.AreEqual(b1, forB[0].Id);
            Assert.IsNotNull(none);
            Assert.AreEqual(0, none.Count);
        }

        [Test]
        public void LoadOutstandingPurchases_StoreFailure_FaultsTheTaskWithTheStoresException()
        {
            var boom = new InvalidOperationException("load failed");
            _store.FailNext(boom);

            Task<IReadOnlyList<PendingPurchaseRecord>> task = _service.LoadOutstandingPurchases(CHAR_A, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted);
            Assert.AreSame(boom, task.Exception.GetBaseException());
        }

        [Test]
        public void LoadOutstandingPurchases_TokenCancelledAtEntry_ReturnsACancelledTask()
        {
            Task<IReadOnlyList<PendingPurchaseRecord>> task = _service.LoadOutstandingPurchases(CHAR_A, Cancelled());

            Assert.IsTrue(task.IsCanceled, "status " + task.Status);
        }

        [Test]
        public void LoadOutstandingPurchases_StoreNeverCompletes_FaultsWithTimeoutException()
        {
            Rig rig = BuildRig(_store, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));
            _store.NeverCompleteNext();

            Task<IReadOnlyList<PendingPurchaseRecord>> task = rig.Service.LoadOutstandingPurchases(CHAR_A, CancellationToken.None);
            Settle(task);

            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<TimeoutException>(task.Exception.GetBaseException());
        }

        // ---------------------------------------------------------------- OutcomeUnknown and retry

        [Test]
        public void BeginPurchase_StoreNeverCompletes_ReturnsOutcomeUnknownAndARetryInsertsOnce()
        {
            Rig rig = BuildRig(_store, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));
            ExpectError("BeginPurchase");
            _store.NeverCompleteNext();

            PendingPurchaseBeginResult first = Complete(rig.Service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));
            PendingPurchaseBeginResult retry = Complete(rig.Service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.OutcomeUnknown, first.Result);
            Assert.AreEqual(0L, first.PurchaseId);
            Assert.AreEqual(PendingPurchaseResult.Success, retry.Result);
            Assert.Greater(retry.PurchaseId, 0L);
            Assert.AreEqual(1, Complete(rig.Service.LoadOutstandingPurchases(CHAR_A, CancellationToken.None)).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void CompletePurchase_StoreNeverCompletes_ReturnsOutcomeUnknownAndTheRecordStays()
        {
            Rig rig = BuildRig(_store, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            ExpectError("CompletePurchase");
            _store.NeverCompleteNext();

            CharacterSaveResult result = Complete(rig.Service.CompletePurchase(id, CHAR_A, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.OutcomeUnknown, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
        }

        [Test]
        public void RefundPurchase_StoreNeverCompletes_ReturnsOutcomeUnknownAndNothingIsApplied()
        {
            Rig rig = BuildRig(_store, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
            ExpectError("RefundPurchase");
            _store.NeverCompleteNext();

            CharacterSaveResult result = Complete(rig.Service.RefundPurchase(id, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.OutcomeUnknown, result);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        [Test]
        public void BeginPurchase_TimesOutThenCommitsLate_RetryReturnsSuccessWithTheSameIdAndOneRecord()
        {
            var ignoring = new TokenIgnoringPurchaseStore(_store) { IgnoreBeginToken = true };
            Rig rig = BuildRig(ignoring, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));
            ExpectError("BeginPurchase");
            Action release = _store.HoldNext();

            PendingPurchaseBeginResult first = Complete(rig.Service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));
            release();
            PendingPurchaseBeginResult retry = Complete(rig.Service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.OutcomeUnknown, first.Result);
            Assert.AreEqual(PendingPurchaseResult.Success, retry.Result);
            IReadOnlyList<PendingPurchaseRecord> list = Complete(rig.Service.LoadOutstandingPurchases(CHAR_A, CancellationToken.None));
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(list[0].Id, retry.PurchaseId);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);
        }

        // ---------------------------------------------------------------- wiring

        [Test]
        public void PurchaseWrites_ServiceWithoutWriteQueue_ThrowInvalidOperationExceptionSynchronously()
        {
            var bare = new CharacterPersistenceService(_store);

            Assert.Throws<InvalidOperationException>(() => bare.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => bare.CompletePurchase(1L, CHAR_A, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => bare.RefundPurchase(1L, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));
        }

        [Test]
        public void PurchaseMethods_StoreWithoutPurchaseSeam_ThrowInvalidOperationExceptionSynchronously()
        {
            CharacterPersistenceService service = BuildRig(new CountingStore(_store), TimeSpan.FromSeconds(5)).Service;

            Assert.Throws<InvalidOperationException>(() => service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => service.CompletePurchase(1L, CHAR_A, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => service.RefundPurchase(1L, CHAR_A, REFUND_GOLD, REFUND_GOLD_VERSION, CancellationToken.None));
            Assert.Throws<InvalidOperationException>(() => service.LoadOutstandingPurchases(CHAR_A, CancellationToken.None));
        }

        [Test]
        public void FailNextAfterInsert_FetchThroughTheStore_DoesNotConsumeIt()
        {
            ExpectError("BeginPurchase");
            _store.FailNextAfterInsert(new InvalidOperationException("armed"));
            Assert.IsNotNull(Stored(CHAR_A));

            PendingPurchaseBeginResult r = Complete(_service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.DatabaseError, r.Result);
            Assert.AreEqual(0, Outstanding(CHAR_A).Count);
        }

        // ---------------------------------------------------------------- queue ordering

        [Test]
        public void BeginPurchase_QueuedBehindAHeldSave_RunsAfterTheSaveSoThePurchaseGoldSurvives()
        {
            LoadInto(_rig, CHAR_A);
            Action release = _store.HoldNext();
            Task<CharacterSaveResult> save = _service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None);
            Task<PendingPurchaseBeginResult> begin = _service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None);

            Assert.IsFalse(begin.IsCompleted, "the purchase must wait for the earlier save");
            release();
            CharacterSaveResult saveResult = Complete(save);
            PendingPurchaseBeginResult beginResult = Complete(begin);

            Assert.AreEqual(CharacterSaveResult.Success, saveResult);
            Assert.AreEqual(PendingPurchaseResult.Success, beginResult.Result);
            AssertStored(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION, STORED_SAVE_VERSION + 1u);
            Assert.AreEqual(1, Outstanding(CHAR_A).Count);
        }

        // ---------------------------------------------------------------- independence from the character record

        [Test]
        public void SaveSession_AfterBeginPurchaseWithPrePurchaseSaveVersion_SucceedsAdvancesSaveVersionByOneAndRecordStaysOutstanding()
        {
            CharacterRecord loaded = LoadInto(_rig, CHAR_A);
            Assert.AreEqual(STORED_SAVE_VERSION, loaded.SaveVersion);
            PendingPurchaseBeginResult begin = Complete(_service.BeginPurchase(CHAR_A, NewRecord(CHAR_A), AFTER_GOLD, AFTER_GOLD_VERSION, CancellationToken.None));

            CharacterSaveResult save = Complete(_service.SaveSession(CHAR_A, SessionEndReason.CleanLogout, CancellationToken.None));

            Assert.AreEqual(PendingPurchaseResult.Success, begin.Result);
            Assert.AreEqual(CharacterSaveResult.Success, save);
            Assert.AreEqual(STORED_SAVE_VERSION + 1u, Stored(CHAR_A).SaveVersion);
            IReadOnlyList<PendingPurchaseRecord> outstanding = Outstanding(CHAR_A);
            Assert.AreEqual(1, outstanding.Count);
            Assert.AreEqual(begin.PurchaseId, outstanding[0].Id);
        }

        [Test]
        public void LoadCharacter_WithAnOutstandingPurchase_SucceedsAndLeavesTheRecord()
        {
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterLoadResult load = Complete(_service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));

            Assert.AreEqual(CharacterLoadCode.Success, load.Code);
            IReadOnlyList<PendingPurchaseRecord> outstanding = Outstanding(CHAR_A);
            Assert.AreEqual(1, outstanding.Count);
            Assert.AreEqual(id, outstanding[0].Id);
        }

        [Test]
        public void SaveIrreversibleOutcome_WithAnOutstandingPurchase_SucceedsAndLeavesTheRecord()
        {
            LoadInto(_rig, CHAR_A);
            long id = BeginOk(CHAR_A, AFTER_GOLD, AFTER_GOLD_VERSION);

            CharacterSaveResult result = Complete(_service.SaveIrreversibleOutcome(CHAR_A, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            IReadOnlyList<PendingPurchaseRecord> outstanding = Outstanding(CHAR_A);
            Assert.AreEqual(1, outstanding.Count);
            Assert.AreEqual(id, outstanding[0].Id);
        }
    }
}
