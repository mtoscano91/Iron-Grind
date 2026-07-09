using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode unit tests for the ItemDatabase runtime — Story 001 acceptance criteria.
    /// All 11 blocking ACs are covered, one test method per criterion.
    /// </summary>
    /// <remarks>
    /// Each test that triggers <c>Debug.LogError</c> uses <see cref="LogAssert.Expect"/>
    /// to mark the error as intentional. Unity Test Framework fails tests that emit
    /// unexpected errors, so every error-logging path must be declared.
    /// </remarks>
    [TestFixture]
    internal sealed class ItemDatabase_Core_Tests
    {
        private IronGrind.ItemDatabase.ItemDatabase _db;
        private List<ItemDefinition> _itemsToDestroy;

        [SetUp]
        public void SetUp()
        {
            _db = new IronGrind.ItemDatabase.ItemDatabase();
            _itemsToDestroy = new List<ItemDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var def in _itemsToDestroy)
            {
                if (def != null)
                    UnityEngine.Object.DestroyImmediate(def);
            }
            _itemsToDestroy.Clear();
        }

        // Helper — creates a definition and registers it for cleanup.
        private ItemDefinition MakeItem(uint id, string name, ItemCategory category)
        {
            var def = ItemDefinitionBuilder.Build(id, name, category);
            _itemsToDestroy.Add(def);
            return def;
        }

        // -----------------------------------------------------------------------
        // AC-1: GetItem(ItemID.Invalid) returns null — no exception, no error logged.
        // ItemID.Invalid is the valid "no item" sentinel, not an error condition.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_GetItem_InvalidSentinel_ReturnsNullWithNoError()
        {
            // Arrange — database must be ready so we isolate only the Invalid-sentinel path.
            _db.Initialize(Array.Empty<ItemDefinition>());

            // Act
            var result = _db.GetItem(ItemID.Invalid);

            // Assert — no LogAssert.Expect: if an error is logged here the test fails.
            Assert.IsNull(result, "GetItem(ItemID.Invalid) must return null silently.");
        }

        // -----------------------------------------------------------------------
        // AC-2: Two callers requesting the same ItemID receive the exact same reference.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_GetItem_SameIdCalledTwice_ReturnsSameReference()
        {
            // Arrange
            var def = MakeItem(1u, "Iron Sword", ItemCategory.Equipment);
            _db.Initialize(new[] { def });

            // Act
            var ref1 = _db.GetItem(new ItemID(1u));
            var ref2 = _db.GetItem(new ItemID(1u));

            // Assert
            Assert.IsTrue(
                ReferenceEquals(ref1, ref2),
                "Both calls must return the exact same ItemDefinition object reference.");
        }

        // -----------------------------------------------------------------------
        // AC-2 edge case: distinct ItemIDs must not alias to the same reference.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_GetItem_DifferentIds_ReturnsDistinctReferences()
        {
            // Arrange
            var defA = MakeItem(1u, "Iron Sword", ItemCategory.Equipment);
            var defB = MakeItem(2u, "Health Potion", ItemCategory.Consumable);
            _db.Initialize(new[] { defA, defB });

            // Act
            var refA = _db.GetItem(new ItemID(1u));
            var refB = _db.GetItem(new ItemID(2u));

            // Assert
            Assert.IsFalse(
                ReferenceEquals(refA, refB),
                "GetItem for distinct IDs must not return the same reference (no cross-ID aliasing).");
        }

        // -----------------------------------------------------------------------
        // AC-19: GetItemsByCategory with an unknown enum value returns empty list
        //        and logs a dev-build error. No exception thrown.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_GetItemsByCategory_UnknownEnumValue_ReturnsEmptyAndLogsError()
        {
            // Arrange
            _db.Initialize(Array.Empty<ItemDefinition>());

            // Expect — must be declared before the call that logs.
            // Note: ItemCategory is byte-backed (Rule byte range 0-255); 255 is used here
            // (not the story text's illustrative 999, which overflows byte and is a
            // compile-time error — CS0221 — for a constant enum cast).
            LogAssert.Expect(
                LogType.Error,
                "[ItemDatabase] GetItemsByCategory called with unknown ItemCategory value (255).");

            // Act
            var result = _db.GetItemsByCategory((ItemCategory)255);

            // Assert
            Assert.IsNotNull(result, "Result must not be null.");
            Assert.AreEqual(0, result.Count, "Result must be empty for an unknown category.");
        }

        // -----------------------------------------------------------------------
        // AC-28: GetItem before Initialize() — null returned, IsReady false, error logged.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_GetItem_BeforeInitialize_ReturnsNullAndLogsError()
        {
            // IsReady must be false at this point.
            Assert.IsFalse(_db.IsReady, "IsReady must be false before Initialize().");

            // Expect — must be declared before the call that logs.
            LogAssert.Expect(
                LogType.Error,
                "[ItemDatabase] GetItem called before Initialize(). IsReady == false.");

            // Act
            var result = _db.GetItem(new ItemID(1u));

            // Assert
            Assert.IsNull(result, "GetItem must return null when called before Initialize().");
            Assert.IsFalse(_db.IsReady, "IsReady must still be false after the failed query.");
        }

        // -----------------------------------------------------------------------
        // AC-29a: IsReady is true after Initialize() completes.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_IsReady_TrueAfterInitialize()
        {
            // Arrange / Act
            _db.Initialize(Array.Empty<ItemDefinition>());

            // Assert
            Assert.IsTrue(_db.IsReady, "IsReady must be true after Initialize() completes.");
        }

        // -----------------------------------------------------------------------
        // AC-29b: OnDatabaseReady fires exactly once — a second Initialize() does not re-fire.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_OnDatabaseReady_FiresExactlyOnce_SecondInitializeIsNoOp()
        {
            // Arrange
            int fireCount = 0;
            _db.OnDatabaseReady += () => fireCount++;

            // Act
            _db.Initialize(Array.Empty<ItemDefinition>());
            _db.Initialize(Array.Empty<ItemDefinition>()); // second call must be a no-op

            // Assert
            Assert.AreEqual(
                1, fireCount,
                "OnDatabaseReady must fire exactly once regardless of how many times Initialize() is called.");
        }

        // -----------------------------------------------------------------------
        // AC-29c: Handler added after IsReady == true is invoked synchronously
        //         before the += expression returns.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_OnDatabaseReady_LateSubscriber_InvokedSynchronously()
        {
            // Arrange — make the database ready first.
            _db.Initialize(Array.Empty<ItemDefinition>());

            bool wasCalled = false;

            // Act — subscribe after IsReady is already true.
            _db.OnDatabaseReady += () => wasCalled = true;

            // Assert — must be true synchronously; no coroutine, no frame delay.
            Assert.IsTrue(
                wasCalled,
                "A handler added via += when IsReady is already true must be invoked synchronously on the calling thread.");
        }

        // -----------------------------------------------------------------------
        // AC-36: TryGetItem happy path — returns true and ref-equals GetItem result.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_TryGetItem_KnownId_ReturnsTrueAndSameReference()
        {
            // Arrange
            var def = MakeItem(1u, "Health Potion", ItemCategory.Consumable);
            _db.Initialize(new[] { def });

            // Act
            bool found = _db.TryGetItem(new ItemID(1u), out var tryResult);
            var getResult = _db.GetItem(new ItemID(1u));

            // Assert
            Assert.IsTrue(found, "TryGetItem must return true for a registered item.");
            Assert.IsTrue(
                ReferenceEquals(tryResult, getResult),
                "TryGetItem and GetItem must return the exact same reference for the same ID.");
        }

        // -----------------------------------------------------------------------
        // AC-37: TryGetItem(ItemID.Invalid) — returns false, out param is null, no error.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_TryGetItem_InvalidSentinel_ReturnsFalseNullWithNoError()
        {
            // Arrange
            _db.Initialize(Array.Empty<ItemDefinition>());

            // Act — no LogAssert.Expect; an error here would fail the test.
            bool found = _db.TryGetItem(ItemID.Invalid, out var result);

            // Assert
            Assert.IsFalse(found, "TryGetItem(ItemID.Invalid) must return false.");
            Assert.IsNull(result, "TryGetItem(ItemID.Invalid) must set out param to null.");
        }

        // -----------------------------------------------------------------------
        // AC-38: TryGetItem before Initialize() — false, null, error logged.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_TryGetItem_BeforeInitialize_ReturnsFalseAndLogsError()
        {
            // Arrange — database is not initialized.
            Assert.IsFalse(_db.IsReady, "IsReady must be false before Initialize().");

            // Expect
            LogAssert.Expect(
                LogType.Error,
                "[ItemDatabase] TryGetItem called before Initialize(). IsReady == false.");

            // Act
            bool found = _db.TryGetItem(new ItemID(1u), out var result);

            // Assert
            Assert.IsFalse(found, "TryGetItem must return false when not initialized.");
            Assert.IsNull(result, "TryGetItem out param must be null when not initialized.");
            Assert.IsFalse(_db.IsReady, "IsReady must remain false after the failed query.");
        }

        // -----------------------------------------------------------------------
        // AC-40: GetItemsByCategory before Initialize() — empty, error logged.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDatabase_GetItemsByCategory_BeforeInitialize_ReturnsEmptyAndLogsError()
        {
            // Arrange — database is not initialized.
            Assert.IsFalse(_db.IsReady, "IsReady must be false before Initialize().");

            // Expect
            LogAssert.Expect(
                LogType.Error,
                "[ItemDatabase] GetItemsByCategory called before Initialize(). IsReady == false.");

            // Act
            var result = _db.GetItemsByCategory(ItemCategory.Equipment);

            // Assert
            Assert.IsNotNull(result, "Result must not be null.");
            Assert.AreEqual(0, result.Count, "Result must be empty when not initialized.");
        }
    }
}
