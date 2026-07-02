using System;
using IronGrind.CharacterStats;
using NUnit.Framework;
using IronGrind.Tests.EditMode.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// Story 007 — Transaction API: BeginStatTransaction / EndStatTransaction / RollbackStatTransaction.
    ///
    /// Covers:
    ///   AC-33       Transaction defers OnStatChanged; fires once per stat at EndStatTransaction.
    ///   Gate: Dedup Same stat written twice → OnStatChanged fires exactly once.
    ///   Gate: Non-nestable  BeginStatTransaction while open → InvalidOperationException.
    ///   Gate: End/no-Begin  EndStatTransaction with no open transaction → InvalidOperationException.
    ///   Gate: Rollback no-op  RollbackStatTransaction with no open transaction → no-op.
    ///   NEW: Mid-transaction Rollback  Writes preserved; deferred events discarded; transaction closed.
    ///   NEW: Mid-transaction read  GetBaseStat returns the written value immediately during a transaction.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterStats_Transaction_Tests
    {
        private IronGrind.CharacterStats.CharacterStats _stats;
        private StatEventRecorder _recorder;

        [SetUp]
        public void SetUp()
        {
            _stats    = CharacterStatsFixture.Create();
            _recorder = new StatEventRecorder();
        }

        // -----------------------------------------------------------------------
        // AC-33: Transaction defers OnStatChanged; EndStatTransaction fires once per stat.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_BeginStatTransaction_DefersFire_EndFiresOnce()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _recorder.Subscribe(_stats);

            // Act / Assert — intermediate assertions prove deferral
            _stats.BeginStatTransaction();
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Vitality),
                "No OnStatChanged expected before any write.");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.MaxHP),
                "No OnStatChanged expected before any write.");

            _stats.SetBaseStat(entity, StatID.Vitality, 54);
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Vitality),
                "OnStatChanged for VIT must be deferred — not fired immediately.");

            _stats.SetBaseStat(entity, StatID.MaxHP, 1920);
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.MaxHP),
                "OnStatChanged for MaxHP must be deferred — not fired immediately.");

            _stats.EndStatTransaction();

            // Assert — events fired exactly once each at End
            Assert.AreEqual(1920, _stats.GetBaseStat(entity, StatID.MaxHP),
                "GetBaseStat(MaxHP) must reflect the written value after EndStatTransaction.");
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Vitality],
                "OnStatChanged(VIT) must fire exactly once at EndStatTransaction.");
            Assert.AreEqual(1, _recorder.FiredCount[StatID.MaxHP],
                "OnStatChanged(MaxHP) must fire exactly once at EndStatTransaction.");
        }

        // -----------------------------------------------------------------------
        // Gate: Deduplication — same stat written twice fires OnStatChanged exactly once.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_Transaction_Deduplication_SameStatFiresOnce()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _recorder.Subscribe(_stats);

            // Act
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 40);
            _stats.SetBaseStat(entity, StatID.Vitality, 54);
            _stats.EndStatTransaction();

            // Assert
            Assert.AreEqual(54, _stats.GetBaseStat(entity, StatID.Vitality),
                "Last write wins — GetBaseStat(VIT) must be 54.");
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Vitality],
                "Deduplication: OnStatChanged(VIT) must fire exactly once, not twice.");
        }

        // -----------------------------------------------------------------------
        // Gate: Non-nestable — BeginStatTransaction while open → InvalidOperationException.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_BeginStatTransaction_AlreadyOpen_ThrowsInvalidOperationException()
        {
            // Arrange
            _stats.BeginStatTransaction();

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _stats.BeginStatTransaction(),
                "BeginStatTransaction must throw when a transaction is already open.");

            // Verify state is not corrupted — EndStatTransaction still works after the throw.
            Assert.DoesNotThrow(() => _stats.EndStatTransaction(),
                "EndStatTransaction must succeed after the nested-Begin was rejected.");
        }

        // -----------------------------------------------------------------------
        // Gate: EndStatTransaction with no open transaction → InvalidOperationException.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_EndStatTransaction_NoOpenTransaction_ThrowsInvalidOperationException()
        {
            // Act / Assert — no prior Begin
            Assert.Throws<InvalidOperationException>(() => _stats.EndStatTransaction(),
                "EndStatTransaction must throw when no transaction is open.");
        }

        // -----------------------------------------------------------------------
        // Gate: RollbackStatTransaction with no open transaction → no-op.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_RollbackStatTransaction_NoOpenTransaction_IsNoOp()
        {
            // Arrange — seed a stat so we can verify it is unchanged after no-op rollback
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.SetBaseStat(entity, StatID.Vitality, 42);

            // Act / Assert — first call
            Assert.DoesNotThrow(() => _stats.RollbackStatTransaction(),
                "RollbackStatTransaction must not throw when no transaction is open.");

            // Consecutive calls — all no-ops
            Assert.DoesNotThrow(() => _stats.RollbackStatTransaction(),
                "Second consecutive Rollback with no open transaction must also be a no-op.");
            Assert.DoesNotThrow(() => _stats.RollbackStatTransaction(),
                "Third consecutive Rollback with no open transaction must also be a no-op.");

            // Assert no state change
            Assert.AreEqual(42, _stats.GetBaseStat(entity, StatID.Vitality),
                "No-op rollback must not modify any existing stat values.");
        }

        // -----------------------------------------------------------------------
        // NEW: Mid-transaction Rollback — writes preserved; deferred events discarded.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_RollbackStatTransaction_MidTransaction_PreservesWritesDiscardsEvents()
        {
            // Arrange — set baseline VIT before opening the transaction
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.SetBaseStat(entity, StatID.Vitality, 10);
            _recorder.Subscribe(_stats);
            _recorder.Reset(); // clear the fire from the baseline write

            // Act
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 54);
            _stats.RollbackStatTransaction();

            // Assert — write preserved
            Assert.AreEqual(54, _stats.GetBaseStat(entity, StatID.Vitality),
                "Rollback must preserve base stat writes — GetBaseStat(VIT) must be 54.");

            // Assert — deferred event discarded
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Vitality),
                "Rollback must discard deferred events — OnStatChanged(VIT) must not have fired.");

            // Assert — transaction is closed; EndStatTransaction now throws
            Assert.Throws<InvalidOperationException>(() => _stats.EndStatTransaction(),
                "EndStatTransaction must throw after Rollback has already closed the transaction.");
        }

        // -----------------------------------------------------------------------
        // NEW: GetBaseStat during open transaction returns the updated value immediately.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_SetBaseStat_DuringTransaction_GetBaseStatReturnsUpdatedValue()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;

            // Act
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 54);
            int midValue = _stats.GetBaseStat(entity, StatID.Vitality);
            _stats.EndStatTransaction();

            // Assert
            Assert.AreEqual(54, midValue,
                "Writes during a transaction are immediate — GetBaseStat must return 54 mid-transaction.");
        }
    }
}
