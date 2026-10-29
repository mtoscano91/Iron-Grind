using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode tests for Status Effects Story 008 - ClearEntity (CR-SE-11, CR-SE-13, EC-SE-9; AC-SE-21, AC-SE-22,
    /// AC-SE-23), against real CharacterStats through the recording decorator.
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_ZoneDeparture_Tests
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
        private const int BASE_ATTACK = 20;
        private const int BASE_DEFENSE = 10;
        private const int BASE_MAX_HP = 400;
        private const int BASE_MAX_MP = 80;
        private const float PCT_NONE = 0f;
        private const float FLAT_AP = 10f;
        private const float FLAT_DEFENSE = 5f;
        private const float HP_BUFF_200 = 200f;
        private const float HP_WITH_BUFF = 600f;
        private const float KILL_HP = 1f;
        private const float OVERKILL = 1000f;
        private const int ONE = 1;
        private const int TWO = 2;
        private const int THREE = 3;

        private static readonly Regex CLEAR_ERROR = new Regex(@"\[StatusEffects\] ClearEntity");

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

        [SetUp]
        public void SetUp()
        {
            _slots = new DictionaryEntitySlotIndex();
            _slots.Add(A, A_SLOT);
            _slots.Add(B, B_SLOT);
            _stats = CharacterStatsFixture.Create();
            SetBases(A);
            SetBases(B);
            _recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(_stats), new CharacterStatsResourceSink(_stats));
            _service = new StatusEffectsService(_recorder, _recorder, _slots, new CharacterStatsDeathSource(_stats));
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private void SetBases(EntityID id)
        {
            _stats.SetBaseStat(id, StatID.AttackPower, BASE_ATTACK);
            _stats.SetBaseStat(id, StatID.Defense, BASE_DEFENSE);
            _stats.SetBaseStat(id, StatID.MaxHP, BASE_MAX_HP);
            _stats.SetBaseStat(id, StatID.MaxMP, BASE_MAX_MP);
        }

        private static BuffDefinition Def(BuffID id, StatID stat, float flat)
        {
            return new BuffDefinition(id, new[] { new StatModifier(stat, flat, PCT_NONE) }, LONG_DURATION, 0f, 0f);
        }

        private void Apply(EntityID target, EntityID caster, in BuffDefinition def)
        {
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(target, caster, def));
        }

        private void ApplyThreeBuffsToA()
        {
            Apply(A, A, Def(BuffW, StatID.AttackPower, FLAT_AP));
            Apply(A, A, Def(BuffX, StatID.Defense, FLAT_DEFENSE));
            Apply(A, A, Def(BuffY, StatID.MaxMP, FLAT_AP));
        }

        private void Kill(EntityID entity)
        {
            _stats.SetCurrentHP(entity, KILL_HP);
            _stats.ApplyDamage(entity, OVERKILL);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_ThreeBuffs_RemovesEachModifierAndRestoresBaseValues()
        {
            ApplyThreeBuffsToA();
            _recorder.Calls.Clear();

            _service.ClearEntity(A);

            Assert.AreEqual(THREE, _recorder.RemoveCalls);
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(BASE_ATTACK, _stats.GetEffectiveStat(A, StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, _stats.GetEffectiveStat(A, StatID.Defense));
            Assert.AreEqual(BASE_MAX_MP, _stats.GetEffectiveStat(A, StatID.MaxMP));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_RowsRemovedInOrderFromFront()
        {
            ApplyThreeBuffsToA();
            _recorder.Calls.Clear();

            _service.ClearEntity(A);

            Assert.AreEqual(BuffW, _recorder.Calls[0].Buff);
            Assert.AreEqual(BuffX, _recorder.Calls[1].Buff);
            Assert.AreEqual(BuffY, _recorder.Calls[2].Buff);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_DeadEntityThenClear_RemovesOncePerStatAndNextTickIsSilent()
        {
            Apply(A, A, new BuffDefinition(BuffW, new[]
            {
                new StatModifier(StatID.AttackPower, FLAT_AP, PCT_NONE),
                new StatModifier(StatID.Defense, FLAT_DEFENSE, PCT_NONE)
            }, LONG_DURATION, 0f, 0f));
            Kill(A);
            Assert.IsTrue(_service.IsEntityDead(A));
            _recorder.Calls.Clear();

            _service.ClearEntity(A);
            int removesAfterClear = _recorder.RemoveCalls;
            _recorder.Calls.Clear();
            _service.OnServerTick();

            Assert.AreEqual(TWO, removesAfterClear, "one remove per stat, not twice");
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(0, _recorder.Calls.Count, "the next tick makes no call for the entity");
        }

        [Test]
        public void StatusEffects_ZoneDeparture_CasterLeaves_TargetRowKeepsCasterAndKeepsTicking()
        {
            Apply(B, A, Def(BuffW, StatID.AttackPower, FLAT_AP));

            _service.ClearEntity(A);
            _service.OnServerTick();

            Assert.IsTrue(_service.TryGetActiveEffect(B, BuffW, out ActiveEffect row));
            Assert.AreEqual(A, row.CasterEntityId);
            Assert.AreEqual(LONG_DURATION - 1u, row.TicksRemaining);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_UnknownEntity_MakesNoCall()
        {
            _service.ClearEntity(Unknown);

            Assert.AreEqual(0, _recorder.Calls.Count);
            Assert.AreEqual(0, _recorder.BeginCalls);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_NoRows_MakesNoLayerCallButClearsDeadFlag()
        {
            Kill(A);
            Assert.IsTrue(_service.IsEntityDead(A));

            _service.ClearEntity(A);

            Assert.AreEqual(0, _recorder.BeginCalls);
            Assert.AreEqual(0, _recorder.Calls.Count);
            Assert.IsFalse(_service.IsEntityDead(A));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_SecondCall_IsNoOp()
        {
            ApplyThreeBuffsToA();
            _service.ClearEntity(A);
            _recorder.Calls.Clear();
            int beginsBefore = _recorder.BeginCalls;

            _service.ClearEntity(A);

            Assert.AreEqual(0, _recorder.Calls.Count);
            Assert.AreEqual(beginsBefore, _recorder.BeginCalls);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_MaxHpBuffCleared_RunsHpClamp()
        {
            Apply(A, A, Def(BuffW, StatID.MaxHP, HP_BUFF_200));
            _stats.SetCurrentHP(A, HP_WITH_BUFF);
            Assert.AreEqual(HP_WITH_BUFF, _stats.GetCurrentHP(A));

            _service.ClearEntity(A);

            Assert.AreEqual(ONE, _recorder.ClampHpCalls);
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(A));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_OwnBatch_WrapsRemovalsInOneBeginEnd()
        {
            ApplyThreeBuffsToA();
            int beginBefore = _recorder.BeginCalls;
            int endBefore = _recorder.EndCalls;

            _service.ClearEntity(A);

            Assert.AreEqual(beginBefore + ONE, _recorder.BeginCalls);
            Assert.AreEqual(endBefore + ONE, _recorder.EndCalls);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_BeginThrowsBecauseTransactionOpen_LogsAndStillRemovesAndClears()
        {
            ApplyThreeBuffsToA();
            _stats.BeginStatTransaction();
            LogAssert.Expect(LogType.Error, CLEAR_ERROR);

            _service.ClearEntity(A);
            _stats.EndStatTransaction();

            Assert.AreEqual(THREE, _recorder.RemoveCalls);
            Assert.AreEqual(0, _recorder.EndCalls, "no End call was made for a Begin that failed");
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.AreEqual(BASE_ATTACK, _stats.GetEffectiveStat(A, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_LayerRemoveThrows_LogsResetsSlotAndDoesNotThrow()
        {
            ApplyThreeBuffsToA();
            Kill(A);
            _recorder.ThrowOnRemoveNumber = TWO;
            LogAssert.Expect(LogType.Error, CLEAR_ERROR);

            Assert.DoesNotThrow(() => _service.ClearEntity(A));

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls, "the batch was still closed");
        }

        [Test]
        public void StatusEffects_ZoneDeparture_EndBatchThrows_LogsAndStillClears()
        {
            ApplyThreeBuffsToA();
            Kill(A);
            _recorder.ThrowOnEndNumber = _recorder.EndCalls + ONE;
            LogAssert.Expect(LogType.Error, CLEAR_ERROR);

            Assert.DoesNotThrow(() => _service.ClearEntity(A));

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.IsFalse(_service.IsEntityDead(A));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_ClampThrows_LogsAndStillClears()
        {
            Apply(A, A, Def(BuffW, StatID.MaxHP, HP_BUFF_200));
            Kill(A);
            _recorder.ThrowOnClampHpNumber = ONE;
            LogAssert.Expect(LogType.Error, CLEAR_ERROR);

            Assert.DoesNotThrow(() => _service.ClearEntity(A));

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.IsFalse(_service.IsEntityDead(A));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_DuringTick_ClearSlotNotYetReached_SkipsItsRowsAndUsesTickBatch()
        {
            // A_SLOT < B_SLOT: B is cleared during A's turn, before the loop reaches B.
            Apply(A, A, Def(BuffW, StatID.AttackPower, FLAT_AP));
            Apply(B, B, Def(BuffW, StatID.AttackPower, FLAT_AP));
            Apply(B, B, Def(BuffX, StatID.Defense, FLAT_DEFENSE));
            _service.InjectClearEntityDuringTick(B, A);
            int beginBefore = _recorder.BeginCalls;
            int endBefore = _recorder.EndCalls;

            _service.OnServerTick();

            Assert.AreEqual(TWO, _recorder.RemoveCalls, "each of B's modifiers removed once");
            Assert.AreEqual(0, _service.GetActiveEffectCount(B));
            Assert.AreEqual(0, _service.GetOccupiedSlots(B));
            Assert.AreEqual(beginBefore + ONE, _recorder.BeginCalls, "no own batch inside the tick");
            Assert.AreEqual(endBefore + ONE, _recorder.EndCalls);
            Assert.IsTrue(_service.TryGetActiveEffect(A, BuffW, out ActiveEffect row));
            Assert.AreEqual(LONG_DURATION - 1u, row.TicksRemaining);
            Assert.AreEqual(BASE_ATTACK, _stats.GetEffectiveStat(B, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_DuringTick_ClearSlotBeingProcessed_EndsItsLoopAndLaterSlotsTick()
        {
            ApplyThreeBuffsToA();
            Apply(B, B, Def(BuffW, StatID.AttackPower, FLAT_AP));
            _service.InjectClearEntityDuringTick(A, A);

            Assert.DoesNotThrow(() => _service.OnServerTick());

            Assert.AreEqual(THREE, _recorder.RemoveCalls, "each of A's modifiers removed once");
            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.IsTrue(_service.TryGetActiveEffect(B, BuffW, out ActiveEffect row));
            Assert.AreEqual(LONG_DURATION - 1u, row.TicksRemaining);
        }

        [Test]
        public void StatusEffects_ZoneDeparture_DuringTick_LayerThrows_ResetsSlotAndLaterSlotsTick()
        {
            ApplyThreeBuffsToA();
            Apply(B, B, Def(BuffW, StatID.AttackPower, FLAT_AP));
            _recorder.ThrowOnRemoveNumber = ONE;
            _service.InjectClearEntityDuringTick(A, A);
            LogAssert.Expect(LogType.Error, CLEAR_ERROR);

            Assert.DoesNotThrow(() => _service.OnServerTick());

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
            Assert.AreEqual(0, _service.GetOccupiedSlots(A));
            Assert.IsFalse(_service.IsEntityDead(A));
            Assert.IsTrue(_service.TryGetActiveEffect(B, BuffW, out ActiveEffect row));
            Assert.AreEqual(LONG_DURATION - 1u, row.TicksRemaining);
            Assert.AreEqual(_recorder.BeginCalls, _recorder.EndCalls, "the tick batch was still closed");
        }

        [Test]
        public void StatusEffects_ZoneDeparture_OtherEntityRowsUntouched()
        {
            Apply(A, A, Def(BuffW, StatID.AttackPower, FLAT_AP));
            Apply(B, B, Def(BuffW, StatID.AttackPower, FLAT_AP));

            _service.ClearEntity(A);

            Assert.AreEqual(ONE, _service.GetActiveEffectCount(B));
            Assert.AreEqual(BASE_ATTACK + FLAT_AP, _stats.GetEffectiveStat(B, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_ZoneDeparture_ReleaseListenerInterface_ForwardsToClearEntity()
        {
            ApplyThreeBuffsToA();

            ((IZoneSlotReleaseListener)_service).BeforeRelease(A);

            Assert.AreEqual(0, _service.GetActiveEffectCount(A));
        }
    }

    /// <summary>
    /// Sequence contract (CR-SE-11, CR-ZI-4): a real ZoneSlotPool with the real service as release listener.
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_ZoneDepartureSequence_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint LONG_DURATION = 100u;
        private const int BASE_ATTACK = 20;
        private const float FLAT_AP = 10f;
        private const float PCT_NONE = 0f;
        private const int ONE = 1;
        private const int BASE_MAX_HP = 400;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class NullPoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        /// <summary>Test-local resolver: players by player slot, mobs by MAX_PLAYERS + mob slot (OWL index).</summary>
        private sealed class PoolSlotIndex : IEntitySlotIndex
        {
            private readonly ZoneSlotPool _pool;
            private readonly Dictionary<EntityID, int> _mobs = new Dictionary<EntityID, int>();

            public PoolSlotIndex(ZoneSlotPool pool)
            {
                _pool = pool;
            }

            public void AddMob(EntityID id, ushort mobSlot)
            {
                _mobs[id] = _pool.GetOwlIndexForMobSlot(mobSlot);
            }

            public bool TryGetSlotIndex(EntityID id, out int index)
            {
                if (_pool.TryGetPlayerSlot(id, out byte slot))
                {
                    index = slot;
                    return true;
                }

                return _mobs.TryGetValue(id, out index);
            }
        }

        /// <summary>Records the pool state seen at call time, then forwards to the real service.</summary>
        private sealed class RecordingReleaseListener : IZoneSlotReleaseListener
        {
            private readonly ZoneSlotPool _pool;
            private readonly IZoneSlotReleaseListener _inner;

            public int Calls;
            public bool PlayerSlotHeld;
            public bool MobHeld;
            public int OccupiedPlayersAtCall;
            public int OccupiedMobsAtCall;

            public RecordingReleaseListener(ZoneSlotPool pool, IZoneSlotReleaseListener inner)
            {
                _pool = pool;
                _inner = inner;
            }

            public void BeforeRelease(EntityID id)
            {
                Calls++;
                PlayerSlotHeld = _pool.TryGetPlayerSlot(id, out _);
                IReadOnlyList<EntityID> mobs = _pool.GetMobEntityIDsInZone(new ZoneID(ZONE_RAW));
                MobHeld = false;
                for (int i = 0; i < mobs.Count; i++)
                {
                    if (mobs[i].Equals(id))
                        MobHeld = true;
                }

                OccupiedPlayersAtCall = _pool.OccupiedPlayerCount;
                OccupiedMobsAtCall = _pool.OccupiedMobCount;
                _inner.BeforeRelease(id);
            }
        }

        private ZoneSlotPool _pool;
        private PoolSlotIndex _index;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private RecordingBuffModifierLayer _recorder;
        private StatusEffectsService _service;
        private RecordingReleaseListener _hook;
        private uint _nextCharacter;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _nextCharacter = 1000u;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            _index = new PoolSlotIndex(_pool);
            _stats = CharacterStatsFixture.Create();
            _recorder = new RecordingBuffModifierLayer(
                new CharacterStatsBuffModifierLayer(_stats), new CharacterStatsResourceSink(_stats));
            _service = new StatusEffectsService(_recorder, _recorder, _index, new CharacterStatsDeathSource(_stats));
            _hook = new RecordingReleaseListener(_pool, _service);
            _pool.SetReleaseListener(_hook);
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        private EntityID AllocPlayer()
        {
            uint n = _nextCharacter++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(n), new AccountID(n + 5000u), n + 9000u, out _, out EntityID id));
            _stats.SetBaseStat(id, StatID.AttackPower, BASE_ATTACK);
            _stats.SetBaseStat(id, StatID.MaxHP, BASE_MAX_HP);
            return id;
        }

        private EntityID AllocMob(out ushort mobSlot)
        {
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out mobSlot, out EntityID id));
            _index.AddMob(id, mobSlot);
            _stats.SetBaseStat(id, StatID.AttackPower, BASE_ATTACK);
            return id;
        }

        private void Buff(EntityID id)
        {
            var def = new BuffDefinition((BuffID)ONE,
                new[] { new StatModifier(StatID.AttackPower, FLAT_AP, PCT_NONE) }, LONG_DURATION, 0f, 0f);
            Assert.AreEqual(ApplyResult.Success, _service.ApplyEffect(id, id, def));
        }

        [Test]
        public void StatusEffects_ZoneDepartureSequence_ReleasePlayer_ClearsRowsWhileSlotStillHeld()
        {
            EntityID player = AllocPlayer();
            Buff(player);
            int occupiedBefore = _pool.OccupiedPlayerCount;

            Assert.IsTrue(_pool.ReleasePlayer(player));

            Assert.AreEqual(ONE, _hook.Calls);
            Assert.IsTrue(_hook.PlayerSlotHeld, "TryGetPlayerSlot still succeeded during BeforeRelease");
            Assert.AreEqual(occupiedBefore, _hook.OccupiedPlayersAtCall);
            Assert.AreEqual(ONE, _recorder.RemoveCalls);
            Assert.AreEqual((float)BASE_ATTACK, _stats.GetEffectiveStat(player, StatID.AttackPower));
            Assert.IsFalse(_pool.TryGetPlayerSlot(player, out _));
        }

        [Test]
        public void StatusEffects_ZoneDepartureSequence_NewPlayerInFreedSlot_StartsWithNoRowsAndNotDead()
        {
            EntityID first = AllocPlayer();
            _pool.TryGetPlayerSlot(first, out byte firstSlot);
            Buff(first);
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer();

            Assert.IsTrue(_pool.TryGetPlayerSlot(second, out byte secondSlot));
            Assert.AreEqual(firstSlot, secondSlot, "the freed slot is reused");
            Assert.AreEqual(0, _service.GetActiveEffectCount(second));
            Assert.AreEqual(0, _service.GetOccupiedSlots(second));
            Assert.IsFalse(_service.IsEntityDead(second));
        }

        [Test]
        public void StatusEffects_ZoneDepartureSequence_DeadPlayerReleased_NewPlayerIsNotDead()
        {
            EntityID first = AllocPlayer();
            Buff(first);
            _stats.SetCurrentHP(first, 1f);
            _stats.ApplyDamage(first, 1000f);
            Assert.IsTrue(_service.IsEntityDead(first));
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer();

            Assert.IsFalse(_service.IsEntityDead(second));
            Assert.AreEqual(0, _service.GetActiveEffectCount(second));
        }

        [Test]
        public void StatusEffects_ZoneDepartureSequence_ReleaseMob_ClearsRowsWhileMobStillHeld()
        {
            EntityID mob = AllocMob(out _);
            Buff(mob);

            Assert.IsTrue(_pool.ReleaseMob(mob));

            Assert.AreEqual(ONE, _hook.Calls);
            Assert.IsTrue(_hook.MobHeld, "the mob was still in the roster during BeforeRelease");
            Assert.AreEqual(ONE, _hook.OccupiedMobsAtCall);
            Assert.AreEqual(ONE, _recorder.RemoveCalls);
            Assert.AreEqual((float)BASE_ATTACK, _stats.GetEffectiveStat(mob, StatID.AttackPower));
        }

        [Test]
        public void StatusEffects_ZoneDepartureSequence_NewMobInFreedSlot_StartsWithNoRowsAndNotDead()
        {
            EntityID first = AllocMob(out ushort firstSlot);
            Buff(first);
            _pool.ReleaseMob(first);

            EntityID second = AllocMob(out ushort secondSlot);

            Assert.AreEqual(firstSlot, secondSlot, "the freed mob slot is reused");
            Assert.AreEqual(0, _service.GetActiveEffectCount(second));
            Assert.AreEqual(0, _service.GetOccupiedSlots(second));
            Assert.IsFalse(_service.IsEntityDead(second));
        }
    }
}
