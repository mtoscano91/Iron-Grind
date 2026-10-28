using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 007 - death cleanup and instant heal
    /// (CR-SE-3, CR-SE-10, EC-SE-2, EC-SE-3; AC-SE-16, AC-SE-30; TR-se-005). Runs against real CharacterStats
    /// through a recording decorator; the death event path is the real one (entities are killed with ApplyDamage).
    /// Real CharacterStats cannot drive HP to 0 from a clamp, so the mid-pass tests use the recorder's ClampHpHook
    /// to kill the entity from inside the clamp call.
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_DeathAndInstantHeal_Tests
    {
        private const uint A_RAW_ID = 11u;
        private const uint B_RAW_ID = 12u;
        private const uint UNKNOWN_RAW_ID = 99u;
        private const int A_SLOT = 2;
        private const int B_SLOT = 5;
        private const uint BUFF_W_RAW_ID = 1u;
        private const uint BUFF_X_RAW_ID = 2u;
        private const uint BUFF_Y_RAW_ID = 3u;
        private const uint LONG_DURATION = 100u;
        private const uint ONE_TICK = 1u;
        private const int BASE_ATTACK = 20;
        private const int BASE_DEFENSE = 10;
        private const int BASE_MAX_HP = 400;
        private const int BASE_MAX_MP = 80;
        private const float PCT_NONE = 0f;
        private const float FLAT_AP = 10f;
        private const float FLAT_DEFENSE = 5f;
        private const float HP_BUFF_200 = 200f;
        private const float MP_BUFF_100 = 100f;
        private const int MAX_MP_WITH_100 = BASE_MAX_MP + 100;
        private const float REGEN = 5f;
        private const float KILL_HP = 1f;
        private const float OVERKILL = 1000f;
        private const float LOW_HP = 100f;
        private const float HEAL_AMOUNT = 100f;
        private const int ONE = 1;
        private const int TWO = 2;
        private const int THREE = 3;

        private static readonly EntityID A = new EntityID(A_RAW_ID);
        private static readonly EntityID B = new EntityID(B_RAW_ID);
        private static readonly EntityID Unknown = new EntityID(UNKNOWN_RAW_ID);
        private static readonly BuffID BuffW = (BuffID)BUFF_W_RAW_ID;
        private static readonly BuffID BuffX = (BuffID)BUFF_X_RAW_ID;
        private static readonly BuffID BuffY = (BuffID)BUFF_Y_RAW_ID;

        private RecordingBuffModifierLayer _recorder;
        private DictionaryEntitySlotIndex _slots;
        private StatusEffectsService _service;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private int _deathEvents;

        [SetUp]
        public void SetUp()
        {
            _slots = new DictionaryEntitySlotIndex();
            _slots.Add(A, A_SLOT);
            _stats = CharacterStatsFixture.Create();
            SetBases(A);
            _recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(_stats), new CharacterStatsResourceSink(_stats));
            _service = new StatusEffectsService(_recorder, _recorder, _slots, new CharacterStatsDeathSource(_stats));
            _deathEvents = 0;
            _stats.Subscribe((EntityID entity) => _deathEvents++);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        // ---- Helpers ----

        private void SetBases(EntityID id)
        {
            _stats.SetBaseStat(id, StatID.AttackPower, BASE_ATTACK);
            _stats.SetBaseStat(id, StatID.Defense, BASE_DEFENSE);
            _stats.SetBaseStat(id, StatID.MaxHP, BASE_MAX_HP);
            _stats.SetBaseStat(id, StatID.MaxMP, BASE_MAX_MP);
        }

        private void AddB()
        {
            _slots.Add(B, B_SLOT);
            SetBases(B);
        }

        private static StatModifier Mod(StatID stat, float flat)
        {
            return new StatModifier(stat, flat, PCT_NONE);
        }

        private static BuffDefinition Def(BuffID id, uint duration, float hpRegen, params StatModifier[] mods)
        {
            return new BuffDefinition(id, mods, duration, hpRegen, 0f);
        }

        /// <summary>A long buff on AttackPower and Defense (two stats) with HP regen.</summary>
        private static BuffDefinition RegenBuff(BuffID id)
        {
            return Def(id, LONG_DURATION, REGEN, Mod(StatID.AttackPower, FLAT_AP), Mod(StatID.Defense, FLAT_DEFENSE));
        }

        private void Apply(EntityID entity, in BuffDefinition def)
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(entity, entity, def));
        }

        /// <summary>Kills the entity through the real Character Stats event path.</summary>
        private void Kill(EntityID entity)
        {
            _stats.SetCurrentHP(entity, KILL_HP);
            _stats.ApplyDamage(entity, OVERKILL);
        }

        private int CountKind(LayerCallKind kind)
        {
            int n = 0;
            foreach (LayerCall call in _recorder.Calls)
            {
                if (call.Kind == kind)
                    n++;
            }

            return n;
        }

        private int CountRegenFor(EntityID entity)
        {
            int n = 0;
            foreach (LayerCall call in _recorder.Calls)
            {
                if (call.Kind == LayerCallKind.Regen && call.Entity.Equals(entity))
                    n++;
            }

            return n;
        }

        private ActiveEffect Row(EntityID entity, BuffID buff)
        {
            Assert.IsTrue(_service.TryGetActiveEffect(entity, buff, out ActiveEffect row), "row " + buff + " missing");
            return row;
        }

        // ---- Subscription seam ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_Constructor_SubscribesExactlyOnce()
        {
            var source = new FakeEntityDeathSource();

            using (new StatusEffectsService(_recorder, _recorder, _slots, source))
            {
                Assert.AreEqual(ONE, source.SubscribeCalls);
                Assert.AreEqual(ONE, source.HandlerCount);
                Assert.AreEqual(0, source.UnsubscribeCalls);
            }
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_Dispose_UnsubscribesTheSameHandlerAndSecondDisposeDoesNothing()
        {
            var source = new FakeEntityDeathSource();
            var service = new StatusEffectsService(_recorder, _recorder, _slots, source);

            service.Dispose();

            Assert.AreEqual(ONE, source.UnsubscribeCalls);
            Assert.AreEqual(0, source.HandlerCount, "the same delegate instance was removed");

            service.Dispose();

            Assert.AreEqual(ONE, source.UnsubscribeCalls, "second Dispose does nothing");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_AfterDispose_DeathLeavesRowsActiveAndFlagClear()
        {
            Apply(A, RegenBuff(BuffX));
            _service.Dispose();

            Kill(A);
            _service.OnServerTick();

            Assert.AreEqual(ONE, _deathEvents, "the death itself happened");
            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(ONE, _service.GetActiveEffectCount(A));
            Assert.AreEqual(LONG_DURATION - 1u, Row(A, BuffX).TicksRemaining, "still ticking normally");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_DeathBeforeDispose_StillCleanedByNextTick()
        {
            Apply(A, RegenBuff(BuffX));
            Kill(A);
            _service.Dispose();

            _service.OnServerTick();

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_UnsubscribeIgnoredDuringDispose_LaterDeathIgnored()
        {
            var source = new FakeEntityDeathSource { IgnoreUnsubscribe = true };
            var service = new StatusEffectsService(_recorder, _recorder, _slots, source);

            service.Dispose();
            source.RaiseDeath(A);

            Assert.AreEqual(ONE, source.HandlerCount, "the handler is still registered");
            Assert.IsFalse(service.IsEntityDead(A), "but it ignores deaths after Dispose");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_UnsubscribeThrowsDuringDispose_SecondDisposeRetries()
        {
            var source = new FakeEntityDeathSource { ThrowOnNextUnsubscribe = true };
            var service = new StatusEffectsService(_recorder, _recorder, _slots, source);

            Assert.Throws<InvalidOperationException>(() => service.Dispose());
            source.RaiseDeath(A);
            Assert.IsTrue(service.IsEntityDead(A), "not disposed: still handling deaths");

            service.Dispose();

            Assert.AreEqual(TWO, source.UnsubscribeCalls, "the second Dispose retried");
            Assert.AreEqual(0, source.HandlerCount);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_DeathOfUnknownEntity_IgnoredWithoutThrowing()
        {
            var source = new FakeEntityDeathSource();
            using (var service = new StatusEffectsService(_recorder, _recorder, _slots, source))
            {
                Assert.DoesNotThrow(() => source.RaiseDeath(Unknown));

                Assert.IsFalse(service.IsEntityDead(Unknown));
            }
        }

        // ---- AC-SE-16: death cleanup ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_Death_HandlerMakesNoRemoveCallAndRowsStayReadable() // AC-SE-16 (1)(2)
        {
            Apply(A, RegenBuff(BuffX));
            Apply(A, Def(BuffY, LONG_DURATION, 0f, Mod(StatID.AttackPower, FLAT_AP)));
            _recorder.Calls.Clear();
            int removesBefore = _recorder.RemoveCalls;

            Kill(A);

            Assert.AreEqual(removesBefore, _recorder.RemoveCalls, "zero RemoveBuffModifier calls during the handler");
            Assert.IsTrue(_service.IsEntityDead(A));
            Assert.AreEqual(TWO, _service.GetActiveEffects(A).Length);
            Assert.AreEqual(LONG_DURATION, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(THREE, _service.GetOccupiedSlots(A));
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_TickAfterDeath_OneRemovePerStatPerEffectRowsFreedNoRegen() // AC-SE-16 (3)(4)
        {
            Apply(A, RegenBuff(BuffX));
            Apply(A, Def(BuffY, LONG_DURATION, 0f, Mod(StatID.AttackPower, FLAT_AP)));
            Kill(A);
            _recorder.Calls.Clear();
            int regenBefore = _recorder.RegenCalls;
            int removesBefore = _recorder.RemoveCalls;

            _service.OnServerTick();

            Assert.AreEqual(THREE, _recorder.RemoveCalls - removesBefore, "one remove per stat per effect");
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(0, _recorder.RegenCalls - regenBefore, "pending rows get no regen");
            Assert.AreEqual(BASE_ATTACK, _stats.GetEffectiveStat(A, StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, _stats.GetEffectiveStat(A, StatID.Defense));
            Assert.IsTrue(_service.IsEntityDead(A), "the flag stays set after cleanup");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_DeathWithNoEffects_SetsFlagOnlyNoCalls()
        {
            _recorder.Calls.Clear();

            Kill(A);

            Assert.IsTrue(_service.IsEntityDead(A));
            Assert.AreEqual(0, _recorder.Calls.Count, "no layer or sink call");

            _service.OnServerTick();

            Assert.AreEqual(0, _recorder.Calls.Count, "the next tick has nothing to clean either");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_CleanupTick_RunsInsideTheTicksSingleStatBatch()
        {
            Apply(A, RegenBuff(BuffX));
            Kill(A);
            _recorder.LogBatchCalls = true;
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.AreEqual(ONE, CountKind(LayerCallKind.BeginBatch));
            Assert.AreEqual(ONE, CountKind(LayerCallKind.EndBatch));
            Assert.AreEqual(TWO, CountKind(LayerCallKind.Remove));
            Assert.AreEqual(LayerCallKind.BeginBatch, _recorder.Calls[0].Kind, "batch opened before the removes");
            Assert.AreEqual(LayerCallKind.EndBatch, _recorder.Calls[_recorder.Calls.Count - 1].Kind, "closed after them");
        }

        // ---- MP clamp on a dead entity ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_DeadEntityWithMaxMpBuff_CleanupLowersMpHpStaysZeroNoSecondDeath()
        {
            Apply(A, Def(BuffX, LONG_DURATION, 0f, Mod(StatID.MaxHP, HP_BUFF_200), Mod(StatID.MaxMP, MP_BUFF_100)));
            _stats.SetCurrentMP(A, MAX_MP_WITH_100);
            Kill(A);
            Assert.AreEqual(ONE, _deathEvents);

            _service.OnServerTick();

            Assert.AreEqual((float)BASE_MAX_MP, _stats.GetCurrentMP(A));
            Assert.AreEqual(0f, _stats.GetCurrentHP(A));
            Assert.AreEqual(ONE, _deathEvents, "no second death event");
            Assert.AreEqual(ONE, _recorder.ClampHpCalls);
            Assert.AreEqual(ONE, _recorder.ClampMpCalls);
        }

        // ---- Mid-pass death ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_ClampKillsEntityMidPass_RemainingRowsNotProcessedCleanedNextTick()
        {
            // Row order: W (MaxHP, expires this tick) then X (long, regen).
            Apply(A, Def(BuffW, ONE_TICK, 0f, Mod(StatID.MaxHP, HP_BUFF_200)));
            Apply(A, RegenBuff(BuffX));
            _recorder.ClampHpHook = entity =>
            {
                _recorder.ClampHpHook = null;
                Kill(entity);
            };
            _recorder.Calls.Clear();
            int regenBefore = _recorder.RegenCalls;

            _service.OnServerTick();

            Assert.IsTrue(_service.IsEntityDead(A));
            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffW, out _), "the expiring row was freed");
            Assert.AreEqual(LONG_DURATION, Row(A, BuffX).TicksRemaining, "second row not decremented this pass");
            Assert.AreEqual(0, _recorder.RegenCalls - regenBefore, "second row did not regenerate");

            int removesBefore = _recorder.RemoveCalls;
            _service.OnServerTick();

            Assert.AreEqual(TWO, _recorder.RemoveCalls - removesBefore, "X's two stats removed next tick");
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(0, _recorder.RegenCalls - regenBefore, "no regen on the cleanup tick either");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_LaterSlotDiesDuringEarlierSlot_SkippedThatPassCleanedNextTick()
        {
            AddB();
            Apply(A, Def(BuffW, ONE_TICK, 0f, Mod(StatID.MaxHP, HP_BUFF_200)));
            Apply(B, RegenBuff(BuffX));
            _recorder.ClampHpHook = entity =>
            {
                _recorder.ClampHpHook = null;
                Kill(B);
            };
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.IsFalse(_service.TryGetActiveEffect(A, BuffW, out _), "the earlier slot expired normally");
            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A), "the earlier slot's occupancy was released");
            Assert.AreEqual(BASE_MAX_HP, _stats.GetEffectiveStat(A, StatID.MaxHP), "its MaxHP modifier was removed");
            Assert.IsTrue(_service.IsEntityDead(B));
            Assert.AreEqual(LONG_DURATION, Row(B, BuffX).TicksRemaining, "later slot not decremented");
            Assert.AreEqual(0, CountRegenFor(B), "later slot did not regenerate");

            int removesBefore = _recorder.RemoveCalls;
            _service.OnServerTick();

            Assert.AreEqual(TWO, _recorder.RemoveCalls - removesBefore);
            Assert.AreEqual(0, _service.GetActiveEffectCount(B));
            Assert.AreEqual(0, _service.GetOccupiedSlots(B));
            Assert.AreEqual(0, CountRegenFor(B));
        }

        // ---- Respawn and the dead-flag lifetime ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_DeadFlagSurvivesCleanupUntilRespawn_ThenApplyAndHealSucceed()
        {
            Apply(A, RegenBuff(BuffX));
            Kill(A);
            _service.OnServerTick();

            Assert.IsTrue(_service.IsEntityDead(A), "flag stays set after the cleanup tick");
            Assert.AreEqual(ApplyResult.InvalidTarget, _service.ApplyEffect(A, A, RegenBuff(BuffY)));
            Assert.AreEqual(ApplyResult.InvalidTarget, _service.ApplyInstantHeal(A, HEAL_AMOUNT));

            _service.OnEntityRespawned(A);

            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(A, A, RegenBuff(BuffY)));
            Assert.AreEqual(ApplyResult.Success, _service.ApplyInstantHeal(A, HEAL_AMOUNT));
            Assert.AreEqual(HEAL_AMOUNT, _stats.GetCurrentHP(A), "the heal reached Character Stats");
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_RespawnBeforeCleanupTick_RowsKeptAndTickNormally()
        {
            // Documents a case Death & Respawn's timing rules out (T-3 is many ticks after T-1): cleanup is keyed on
            // the dead flag at the start of a tick, so clearing it first leaves the rows in place.
            Apply(A, RegenBuff(BuffX));
            Kill(A);
            _service.OnEntityRespawned(A);

            _service.OnServerTick();

            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(LONG_DURATION - 1u, Row(A, BuffX).TicksRemaining);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_RespawnOnAliveEntity_NoOpRowsUntouched()
        {
            Apply(A, RegenBuff(BuffX));

            Assert.DoesNotThrow(() => _service.OnEntityRespawned(A));

            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(ONE, _service.GetActiveEffectCount(A));
            Assert.AreEqual(LONG_DURATION, Row(A, BuffX).TicksRemaining);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_RespawnOnUnknownEntity_NoOpNeverThrows()
        {
            Assert.DoesNotThrow(() => _service.OnEntityRespawned(Unknown));

            Assert.IsFalse(_service.IsEntityDead(Unknown));
        }

        // ---- EC-SE-3 ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_ApplyEffectAfterDeathSameTick_InvalidTargetNoRowAdded() // EC-SE-3
        {
            Kill(A);

            ApplyResult result = _service.ApplyEffect(A, A, RegenBuff(BuffX));

            Assert.AreEqual(ApplyResult.InvalidTarget, result);
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _recorder.AddCalls);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_TwoEntitiesOnlyOneDies_OtherKeepsTicking()
        {
            AddB();
            Apply(A, RegenBuff(BuffX));
            Apply(B, RegenBuff(BuffX));
            Kill(B);
            _recorder.Calls.Clear();

            _service.OnServerTick();

            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(LONG_DURATION - 1u, Row(A, BuffX).TicksRemaining);
            Assert.AreEqual(ONE, CountRegenFor(A));
            Assert.AreEqual(0, CountRegenFor(B));
            Assert.AreEqual(0, _service.GetActiveEffectCount(B));
            Assert.AreEqual(ONE, _service.GetActiveEffectCount(A));
            Assert.AreEqual(TWO, _service.GetOccupiedSlots(A));
        }

        // ---- AC-SE-30: ApplyInstantHeal ----

        [Test]
        public void StatusEffects_DeathAndInstantHeal_InstantHeal_OneRegenCallNoRowOccupancyUnchangedSuccess() // AC-SE-30
        {
            Apply(A, Def(BuffX, LONG_DURATION, 0f, Mod(StatID.AttackPower, FLAT_AP)));
            _stats.SetCurrentHP(A, LOW_HP);
            _recorder.Calls.Clear();
            int rowsBefore = _service.GetActiveEffectCount(A);
            int occupiedBefore = _service.GetOccupiedSlots(A);

            ApplyResult result = _service.ApplyInstantHeal(A, HEAL_AMOUNT);

            Assert.AreEqual(ApplyResult.Success, result);
            Assert.AreEqual(ONE, CountKind(LayerCallKind.Regen));
            Assert.AreEqual(ONE, _recorder.Calls.Count, "nothing else was called");
            Assert.AreEqual(A, _recorder.Calls[0].Entity);
            Assert.AreEqual(HEAL_AMOUNT, _recorder.Calls[0].Amount);
            Assert.AreEqual(rowsBefore, _service.GetActiveEffectCount(A), "no row created");
            Assert.AreEqual(occupiedBefore, _service.GetOccupiedSlots(A));
            Assert.AreEqual(LOW_HP + HEAL_AMOUNT, _stats.GetCurrentHP(A));
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        public void StatusEffects_DeathAndInstantHeal_InstantHealNonPositiveOrNaNAmount_InvalidAmountNoCall(float amount)
        {
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.InvalidAmount, _service.ApplyInstantHeal(A, amount));
            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_InstantHealOnDeadEntity_InvalidTargetNoCall()
        {
            Kill(A);
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.InvalidTarget, _service.ApplyInstantHeal(A, HEAL_AMOUNT));
            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_InstantHealOnUnknownEntity_InvalidTargetNoCall()
        {
            _recorder.Calls.Clear();

            Assert.AreEqual(ApplyResult.InvalidTarget, _service.ApplyInstantHeal(Unknown, HEAL_AMOUNT));
            Assert.AreEqual(0, _recorder.Calls.Count);
        }

        [Test]
        public void StatusEffects_DeathAndInstantHeal_InstantHealBadAmountOnDeadEntity_AmountCheckedFirst()
        {
            Kill(A);

            Assert.AreEqual(ApplyResult.InvalidAmount, _service.ApplyInstantHeal(A, 0f));
        }
    }
}
