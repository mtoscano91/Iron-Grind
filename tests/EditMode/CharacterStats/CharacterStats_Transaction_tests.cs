using System;
using IronGrind.CharacterStats;
using NUnit.Framework;
using UnityEngine.TestTools;
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
    ///   REVISED (Leveling System Story 007): Mid-transaction Rollback  Writes REVERTED to their
    ///     pre-transaction values; deferred events discarded; transaction closed. Root-cause fix
    ///     coordinated with Class System AC-CS-24 — see RollbackStatTransaction's doc comment.
    ///   NEW (Story 007 revision): Same stat written twice in one transaction reverts to the
    ///     TRUE pre-transaction value, not an intermediate write.
    ///   NEW (Story 007 revision): Float-schema stat rollback reverts via FloatStatValues.
    ///   NEW (Story 007 revision): CurrentHP/CurrentMP rollback reverts via their own dictionaries.
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
        // REVISED (Leveling System Story 007): Mid-transaction Rollback — writes REVERTED to
        // their pre-transaction values; deferred events discarded. Was previously
        // "...PreservesWritesDiscardsEvents" — flipped per the Story 007 root-cause fix (see
        // RollbackStatTransaction's doc comment and story-007-transaction-api.md's Revision Note).
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_RollbackStatTransaction_MidTransaction_RevertsWritesDiscardsEvents()
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

            // Assert — write reverted to its pre-transaction value
            Assert.AreEqual(10, _stats.GetBaseStat(entity, StatID.Vitality),
                "Rollback must revert base stat writes — GetBaseStat(VIT) must return to its pre-transaction value of 10.");

            // Assert — deferred event discarded
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Vitality),
                "Rollback must discard deferred events — OnStatChanged(VIT) must not have fired.");

            // Assert — transaction is closed; EndStatTransaction now throws
            Assert.Throws<InvalidOperationException>(() => _stats.EndStatTransaction(),
                "EndStatTransaction must throw after Rollback has already closed the transaction.");
        }

        // -----------------------------------------------------------------------
        // NEW (Story 007 revision): Same stat written twice in one transaction reverts to the
        // TRUE pre-transaction value, not the intermediate write — proves only the FIRST write
        // per (EntityID, StatID) pair is snapshotted.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_RollbackStatTransaction_StatWrittenTwiceInTransaction_RevertsToTruePreTransactionValue()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.SetBaseStat(entity, StatID.Vitality, 10);
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 40);
            _stats.SetBaseStat(entity, StatID.Vitality, 54);
            _stats.RollbackStatTransaction();

            // Assert
            Assert.AreEqual(10, _stats.GetBaseStat(entity, StatID.Vitality),
                "Rollback must revert to the value BEFORE the transaction's first write (10), not the intermediate write (40).");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Vitality),
                "Rollback must discard deferred events regardless of how many writes occurred.");
        }

        // -----------------------------------------------------------------------
        // NEW (Story 007 revision): Float-schema stat rollback — restores via FloatStatValues,
        // not the int[] array (a different backing store than SetBaseStat).
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_RollbackStatTransaction_FloatSchemaStat_RevertsToPreTransactionValue()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.SetBaseStatFloat(entity, StatID.CritChance, 0.10f);
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            _stats.SetBaseStatFloat(entity, StatID.CritChance, 0.55f);
            _stats.RollbackStatTransaction();

            // Assert
            Assert.AreEqual(0.10f, _stats.GetBaseStatFloat(entity, StatID.CritChance), 1e-6f,
                "Rollback must revert a float-schema stat to its pre-transaction value (FloatStatValues backing store).");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.CritChance),
                "Rollback must discard the deferred event for the float-schema stat.");
        }

        // -----------------------------------------------------------------------
        // NEW (Story 007 revision): CurrentHP/CurrentMP rollback — restores via the _currentHp/
        // _currentMp dictionaries, the third and final backing store Rollback must handle.
        // -----------------------------------------------------------------------

        [Test]
        public void CharacterStats_RollbackStatTransaction_CurrentHpAndCurrentMp_RevertToPreTransactionValues()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.SetBaseStat(entity, StatID.MaxHP, 1000);
            _stats.SetBaseStat(entity, StatID.MaxMP, 500);
            _stats.SetCurrentHP(entity, 800f);
            _stats.SetCurrentMP(entity, 300f);
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            _stats.SetCurrentHP(entity, 250f);
            _stats.SetCurrentMP(entity, 50f);
            _stats.RollbackStatTransaction();

            // Assert
            Assert.AreEqual(800f, _stats.GetCurrentHP(entity),
                "Rollback must revert CurrentHP to its pre-transaction value.");
            Assert.AreEqual(300f, _stats.GetCurrentMP(entity),
                "Rollback must revert CurrentMP to its pre-transaction value.");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.CurrentHP),
                "Rollback must discard the deferred event for CurrentHP.");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.CurrentMP),
                "Rollback must discard the deferred event for CurrentMP.");
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

        // -----------------------------------------------------------------------
        // Status Effects Story 006: buff modifier events are deferred inside a transaction.
        // Buff modifier writes are NOT snapshotted - Rollback discards events, keeps modifiers.
        // -----------------------------------------------------------------------

        private static readonly BuffID DeferralBuff = (BuffID)601u;
        private const float DeferralFlatBonus    = 25f;
        private const int   DeferralBuffDuration = 10;

        private static BuffModifierEntry MakeDeferralEntry(float flat) =>
            new BuffModifierEntry(flat, 0f, DeferralBuffDuration, DeferralBuff);

        [Test]
        public void CharacterStats_Transaction_AddThenRemoveBuffSameStat_NoEventsUntilEndThenOne()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _recorder.Subscribe(_stats);

            // Act
            _stats.BeginStatTransaction();
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(DeferralFlatBonus));
            _stats.RemoveBuffModifier(entity, StatID.Strength, DeferralBuff);

            // Assert
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Strength),
                "No buff modifier event may fire inside a transaction.");
            _stats.EndStatTransaction();
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Strength],
                "Add + Remove on the same (entity, stat) dedupe to exactly one event at End.");
        }

        [Test]
        public void CharacterStats_Transaction_BuffsOnTwoStats_OneEventEachAtEnd()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _recorder.Subscribe(_stats);

            // Act
            _stats.BeginStatTransaction();
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(DeferralFlatBonus));
            _stats.AddBuffModifier(entity, StatID.MaxHP, MakeDeferralEntry(DeferralFlatBonus));
            Assert.AreEqual(0, _recorder.FiredCount.Count, "Both events must be deferred.");
            _stats.EndStatTransaction();

            // Assert
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Strength]);
            Assert.AreEqual(1, _recorder.FiredCount[StatID.MaxHP]);
        }

        [Test]
        public void CharacterStats_Transaction_SameBuffIdOverwrite_EventDeferred()
        {
            // Arrange
            const float RefreshedBonus = 40f;
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(DeferralFlatBonus));
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(RefreshedBonus));
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Strength),
                "Overwrite event must be deferred inside a transaction.");
            _stats.EndStatTransaction();

            // Assert
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Strength]);
        }

        [Test]
        public void CharacterStats_RollbackStatTransaction_BuffModifierWrite_DiscardsEventKeepsModifier()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            int before = _stats.GetEffectiveStat(entity, StatID.Strength);
            _recorder.Subscribe(_stats);

            // Act
            _stats.BeginStatTransaction();
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(DeferralFlatBonus));
            _stats.RollbackStatTransaction();

            // Assert
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Strength),
                "Rollback must discard the deferred buff modifier event.");
            Assert.AreEqual(before + (int)DeferralFlatBonus, _stats.GetEffectiveStat(entity, StatID.Strength),
                "Buff modifier writes are not snapshotted - the modifier stays after Rollback.");
        }

        [Test]
        public void CharacterStats_BuffModifier_OutsideTransaction_AddAndRemoveEachFireOneEvent()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _recorder.Subscribe(_stats);

            // Act / Assert
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(DeferralFlatBonus));
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Strength], "Add fires immediately outside a transaction.");
            _stats.RemoveBuffModifier(entity, StatID.Strength, DeferralBuff);
            Assert.AreEqual(2, _recorder.FiredCount[StatID.Strength], "Remove fires immediately outside a transaction.");
        }

        // -----------------------------------------------------------------------
        // Code-review fixes (Status Effects Story 006): B1 capacity, W2 throwing subscriber,
        // QA gaps. The deferred queue and rollback snapshot grow by doubling (no 18-pair cap).
        // -----------------------------------------------------------------------

        private const int   LargeEntityCount     = 200;
        private const int   StatsPerEntity       = 8;
        private const uint  LargeEntityBase      = 5000u;
        private const int   RollbackEntityCount  = 50;
        private const int   RollbackBaseVit      = 10;
        private const int   RollbackBaseMaxHp    = 100;
        private const int   RollbackWrittenVit   = 77;
        private const int   RollbackWrittenMaxHp = 999;
        private static readonly BuffID LargeBuff = (BuffID)602u;

        private static BuffModifierEntry MakeLargeEntry() =>
            new BuffModifierEntry(DeferralFlatBonus, 0f, DeferralBuffDuration, LargeBuff);

        [Test]
        public void CharacterStats_Transaction_200EntitiesBy8Stats_FiresExactly1600EventsAtEnd()
        {
            // Arrange
            int fired = 0;
            var seen = new System.Collections.Generic.HashSet<(uint, StatID)>();
            _stats.Subscribe((IronGrind.CharacterStats.CharacterStats.StatChangedHandler)((id, stat) =>
            {
                fired++;
                seen.Add((id.RawValue, stat));
            }));

            // Act
            _stats.BeginStatTransaction();
            for (int e = 0; e < LargeEntityCount; e++)
            {
                EntityID id = new EntityID(LargeEntityBase + (uint)e);
                _stats.AddBuffModifier(id, StatID.Strength,     MakeLargeEntry());
                _stats.AddBuffModifier(id, StatID.Dexterity,    MakeLargeEntry());
                _stats.AddBuffModifier(id, StatID.Vitality,     MakeLargeEntry());
                _stats.AddBuffModifier(id, StatID.Intelligence, MakeLargeEntry());
                _stats.RemoveBuffModifier(id, StatID.Intelligence, LargeBuff); // same pair again
                _stats.SetBaseStat(id, StatID.MaxHP, 1);
                _stats.SetBaseStat(id, StatID.MaxMP, 1);
                _stats.SetBaseStat(id, StatID.Level, 1);
                _stats.SetBaseStat(id, StatID.Experience, 1);
                _stats.SetBaseStat(id, StatID.Experience, 2); // duplicate write, still one event
            }
            Assert.AreEqual(0, fired, "No event may fire before EndStatTransaction.");
            _stats.EndStatTransaction();

            // Assert
            Assert.AreEqual(LargeEntityCount * StatsPerEntity, fired,
                "Exactly one event per unique (entity, stat) pair.");
            Assert.AreEqual(LargeEntityCount * StatsPerEntity, seen.Count,
                "Every delivered event must be a distinct pair.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CharacterStats_Transaction_EventsFireInFirstWriteOrder()
        {
            // Arrange
            var order = new System.Collections.Generic.List<StatID>();
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.Subscribe((IronGrind.CharacterStats.CharacterStats.StatChangedHandler)((id, stat) => order.Add(stat)));

            // Act
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 1);
            _stats.SetBaseStat(entity, StatID.MaxHP, 1);
            _stats.SetBaseStat(entity, StatID.Vitality, 2);
            _stats.SetBaseStat(entity, StatID.Strength, 1);
            _stats.EndStatTransaction();

            // Assert
            CollectionAssert.AreEqual(new[] { StatID.Vitality, StatID.MaxHP, StatID.Strength }, order);
        }

        [Test]
        public void CharacterStats_RollbackStatTransaction_ManyEntitiesBeyondOldCapacity_RestoresAll()
        {
            // Arrange
            for (int e = 0; e < RollbackEntityCount; e++)
            {
                EntityID id = new EntityID(LargeEntityBase + (uint)e);
                _stats.SetBaseStat(id, StatID.Vitality, RollbackBaseVit);
                _stats.SetBaseStat(id, StatID.MaxHP, RollbackBaseMaxHp);
            }
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            for (int e = 0; e < RollbackEntityCount; e++)
            {
                EntityID id = new EntityID(LargeEntityBase + (uint)e);
                _stats.SetBaseStat(id, StatID.Vitality, RollbackWrittenVit);
                _stats.SetBaseStat(id, StatID.MaxHP, RollbackWrittenMaxHp);
            }
            _stats.RollbackStatTransaction();

            // Assert
            for (int e = 0; e < RollbackEntityCount; e++)
            {
                EntityID id = new EntityID(LargeEntityBase + (uint)e);
                Assert.AreEqual(RollbackBaseVit, _stats.GetBaseStat(id, StatID.Vitality), "Vitality entity " + e);
                Assert.AreEqual(RollbackBaseMaxHp, _stats.GetBaseStat(id, StatID.MaxHP), "MaxHP entity " + e);
            }
            Assert.AreEqual(0, _recorder.FiredCount.Count, "Rollback must discard every deferred event.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CharacterStats_Transaction_ReusedAfterEnd_DoesNotLeakDedupState()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 5);
            _stats.EndStatTransaction();
            _recorder.Subscribe(_stats);

            // Act - the same pair in a second transaction must fire again
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Vitality, 6);
            _stats.EndStatTransaction();

            // Assert
            Assert.AreEqual(1, _recorder.FiredCount[StatID.Vitality]);
        }

        [Test]
        public void CharacterStats_EndStatTransaction_SubscriberThrowsOnFirstEvent_DeliversRestRethrowsAndCloses()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            var delivered = new System.Collections.Generic.List<StatID>();
            _stats.Subscribe((IronGrind.CharacterStats.CharacterStats.StatChangedHandler)((id, stat) =>
            {
                delivered.Add(stat);
                if (stat == StatID.Strength)
                    throw new InvalidOperationException("subscriber failure");
            }));
            _stats.BeginStatTransaction();
            _stats.SetBaseStat(entity, StatID.Strength, 1);
            _stats.SetBaseStat(entity, StatID.Dexterity, 1);
            _stats.SetBaseStat(entity, StatID.Vitality, 1);

            // Act / Assert
            var ex = Assert.Throws<InvalidOperationException>(() => _stats.EndStatTransaction());
            Assert.AreEqual("subscriber failure", ex.Message);
            CollectionAssert.AreEqual(new[] { StatID.Strength, StatID.Dexterity, StatID.Vitality }, delivered,
                "Every deferred pair must still be delivered after the first subscriber exception.");
            Assert.DoesNotThrow(() => _stats.BeginStatTransaction(), "Transaction must be closed after End.");
            _stats.RollbackStatTransaction();
        }

        [Test]
        public void CharacterStats_RollbackStatTransaction_BuffRemoveInsideTransaction_ModifierStaysRemovedEventDiscarded()
        {
            // Arrange
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.AddBuffModifier(entity, StatID.Strength, MakeDeferralEntry(DeferralFlatBonus));
            int withBuff = _stats.GetEffectiveStat(entity, StatID.Strength);
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            _stats.RemoveBuffModifier(entity, StatID.Strength, DeferralBuff);
            _stats.RollbackStatTransaction();

            // Assert
            Assert.AreEqual(withBuff - (int)DeferralFlatBonus, _stats.GetEffectiveStat(entity, StatID.Strength),
                "Buff removal is not snapshotted - the modifier stays removed after Rollback.");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.Strength), "Deferred event must be discarded.");
        }

        [Test]
        public void CharacterStats_RollbackStatTransaction_AfterClampCurrentMPToMax_RestoresPreTransactionMp()
        {
            // Arrange
            const int   BaseMaxMp    = 500;
            const float MaxMpBuff    = 100f;
            const float FullBuffedMp = 600f;
            EntityID entity = CharacterStatsFixture.PlayerEntityId;
            _stats.SetBaseStat(entity, StatID.MaxMP, BaseMaxMp);
            _stats.AddBuffModifier(entity, StatID.MaxMP, new BuffModifierEntry(MaxMpBuff, 0f, DeferralBuffDuration, DeferralBuff));
            _stats.SetCurrentMP(entity, FullBuffedMp);
            _stats.RemoveBuffModifier(entity, StatID.MaxMP, DeferralBuff);
            _recorder.Subscribe(_stats);
            _recorder.Reset();

            // Act
            _stats.BeginStatTransaction();
            _stats.ClampCurrentMPToMax(entity);
            Assert.AreEqual((float)BaseMaxMp, _stats.GetCurrentMP(entity), 0.0f,
                "Clamp applies immediately inside the transaction.");
            _stats.RollbackStatTransaction();

            // Assert
            Assert.AreEqual(FullBuffedMp, _stats.GetCurrentMP(entity), 0.0f,
                "Rollback must restore CurrentMP to its pre-transaction value.");
            Assert.IsFalse(_recorder.FiredCount.ContainsKey(StatID.CurrentMP), "Deferred event must be discarded.");
        }

        [Test]
        public void CharacterStats_ClampCurrentMPToMax_UnknownEntity_IsNoOp()
        {
            // Arrange
            EntityID unknown = new EntityID(987654u);
            _recorder.Subscribe(_stats);

            // Act / Assert
            Assert.DoesNotThrow(() => _stats.ClampCurrentMPToMax(unknown));
            Assert.AreEqual(0f, _stats.GetCurrentMP(unknown), 0.0f);
            Assert.AreEqual(0, _recorder.FiredCount.Count, "No event for an unregistered entity.");
        }
    }
}
