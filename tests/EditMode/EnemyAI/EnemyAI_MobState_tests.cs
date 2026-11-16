#nullable enable
#pragma warning disable CS0067 // the fake equipment service never raises its event
using System;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EquipmentSystem;
using IronGrind.Events;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZoneStats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode tests for Enemy AI Story 008: <see cref="MobRuntimeStore"/>, the transform-reader seam on
    /// <see cref="ZoneEntityStateSource"/> and the skill / movement-decorator lookups (AC-1..AC-5).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_MobState_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint OTHER_ZONE_RAW = 8u;
        private const uint KNOWN_TYPE_RAW = 1u;
        private const uint UNKNOWN_TYPE_RAW = 99u;
        private const uint MOB_RAW_ID = 2001u;
        private const int BASE_MAX_HP = 100;
        private const int BASE_ATTACK = 10;
        private const int BASE_DEFENSE = 5;
        private const int BASE_KILL_XP = 10;
        private const float ROTATION_SCALE = 32767f;
        private const int ENRAGED_MAX_HP = 150;
        private const int ENRAGED_ATTACK = 13;
        private const double NON_ENRAGED_ROLL = 0.5;
        private const int MANY_MOBS = 150;
        private const int CALLS = 100;

        private sealed class FakeRegistry : IMobDefinitionRegistry
        {
            public readonly Dictionary<MobTypeID, MobDefinition> Defs = new Dictionary<MobTypeID, MobDefinition>();

            public MobDefinition GetDefinition(MobTypeID id) => Defs.TryGetValue(id, out MobDefinition? d) ? d : null!;
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 100u;
        }

        private sealed class FakeListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeNames : ICharacterNameLookup
        {
            public bool TryGetCharacterName(CharacterID id, out string name)
            {
                name = string.Empty;
                return false;
            }
        }

        private sealed class FakeCombat : IZoneEntityCombatReader
        {
            public AutoAttackState GetState(EntityID entityId) => AutoAttackState.Idle;

            public int GetActionTimer(EntityID entityId) => 0;

            public int GetCycleTicks(EntityID entityId) => 0;
        }

        private sealed class FakeEquipment : IEquipmentService
        {
            public event Action<EquipmentAppearanceChangedEventArgs>? OnAppearanceChanged;

            public byte GetAppearanceFlags(CharacterID charId) => 0;

            public void RegisterCharacter(CharacterID charId, EntityID entityId) => throw new NotSupportedException();

            public void UnregisterCharacter(CharacterID charId) => throw new NotSupportedException();

            public EquipmentSlotState GetEquipmentSlotState(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public byte GetSlotEnhancementLevel(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public bool IsSlotTransitioning(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public ItemID GetEquippedWeaponItemID(CharacterID charId) => throw new NotSupportedException();

            public byte GetEquippedWeaponEnhancementLevel(CharacterID charId) => throw new NotSupportedException();

            public EquipResult Equip(CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex) => throw new NotSupportedException();

            public EquipResult Unequip(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

            public MergeResult RequestMerge(CharacterID charId, int slotIdx1, int slotIdx2, int slotIdx3) => throw new NotSupportedException();

            public GearRestoreResult RestoreSlots(CharacterID charId, ReadOnlySpan<SavedGearSlot> slots) => throw new NotSupportedException();

            public SavedGearSlot[] ExportSlots(CharacterID charId) => throw new NotSupportedException();
        }

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static MobDefinition Def(uint typeRaw, float chance)
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(typeRaw),
                displayName: "Test Mob",
                baseMaxHP: BASE_MAX_HP,
                baseAttackPower: BASE_ATTACK,
                baseDefense: BASE_DEFENSE,
                attackRange: 2f,
                moveSpeed: 3.5f,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: BASE_KILL_XP,
                mobTier: MobTier.Common,
                enragedChance: chance,
                enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f,
                enragedXPMultiplier: 1.5f,
                enragedLootQualityBonusTier: 1);
        }

        private ZoneStats _stats = null!;
        private MobEventBus _bus = null!;
        private FakeRegistry _registry = null!;
        private ScriptedRandomProvider _rng = null!;
        private MobRuntimeStore _store = null!;
        private ZoneSlotPool _pool = null!;
        private ZoneMobRoster _roster = null!;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.Create();
            _bus = new MobEventBus();
            _registry = new FakeRegistry();
            _registry.Defs[new MobTypeID(KNOWN_TYPE_RAW)] = Def(KNOWN_TYPE_RAW, 0.0f);
            _rng = new ScriptedRandomProvider();
            _store = new MobRuntimeStore(Zone, _bus, _registry, _rng, new CharacterStatsMobStatWriter(_stats), MANY_MOBS);
            var ticks = new FakeTicks();
            _pool = new ZoneSlotPool(Zone, ticks, new FakeListener());
            _roster = new ZoneMobRoster(_pool, ticks);
        }

        [TearDown]
        public void TearDown()
        {
            _store.Dispose();
        }

        private void Spawn(EntityID id, Vector3 pos, uint typeRaw = KNOWN_TYPE_RAW, ZoneID? zone = null, double roll = NON_ENRAGED_ROLL)
        {
            _rng.EnqueueDouble(roll);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(zone ?? Zone, id, new MobTypeID(typeRaw), pos));
        }

        private ZoneEntityStateSource BuildSource(IZoneMobTransformReader? reader)
        {
            return new ZoneEntityStateSource(_pool, _roster, new CharacterStatsZoneEntityStatReader(_stats), new FakeNames(),
                new FakeEquipment(), new FakeCombat(), reader);
        }

        private EntityID RosterMob(Vector3 pos)
        {
            EntityID id = _roster.TryAddMob(Zone, new MobTypeID(KNOWN_TYPE_RAW), pos);
            Assert.AreNotEqual(EntityID.Invalid, id);
            return id;
        }

        private static short Wire(float meters)
        {
            float c = Mathf.Clamp(meters, -WireFixedPointCodec.MaxPositionMeters, WireFixedPointCodec.MaxPositionMeters);
            return (short)Mathf.RoundToInt(c * 100f);
        }

        // QA 1
        [Test]
        public void test_spawn_createsEntryWithSpawnPositionFacingZeroTypeAndSpawnPoint()
        {
            var id = new EntityID(MOB_RAW_ID);
            var pos = new Vector3(1.5f, 0.25f, -2f);

            Spawn(id, pos);

            Assert.IsTrue(_store.TryGet(id, out MobRuntimeEntry e));
            Assert.AreEqual(pos, e.CommittedPosition);
            Assert.AreEqual(pos, e.SpawnPoint);
            Assert.AreEqual((short)0, e.FacingAngle);
            Assert.AreEqual(new MobTypeID(KNOWN_TYPE_RAW), e.MobType);
            Assert.IsFalse(e.Variant.IsEnraged);
            Assert.AreEqual(BASE_KILL_XP, e.Variant.EffectiveKillXP);
            Assert.AreEqual(0, e.Variant.TierShift);
            Assert.AreEqual(1, _store.Count);
        }

        // QA 2
        [Test]
        public void test_spawn_seedsStatsFromDefinition()
        {
            var id = new EntityID(MOB_RAW_ID);

            Spawn(id, Vector3.zero);

            Assert.AreEqual(BASE_MAX_HP, _stats.GetEffectiveStat(id, StatID.MaxHP));
            Assert.AreEqual(BASE_ATTACK, _stats.GetEffectiveStat(id, StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, _stats.GetEffectiveStat(id, StatID.Defense));
            Assert.AreEqual((float)BASE_MAX_HP, _stats.GetCurrentHP(id));
        }

        // QA 3
        [Test]
        public void test_spawn_enragedRoll_scaledStatsAndVariantFlag()
        {
            var id = new EntityID(MOB_RAW_ID);
            _registry.Defs[new MobTypeID(KNOWN_TYPE_RAW)] = Def(KNOWN_TYPE_RAW, 1.0f);

            Spawn(id, Vector3.zero, roll: 0.0);

            Assert.IsTrue(_store.TryGet(id, out MobRuntimeEntry e));
            Assert.IsTrue(e.Variant.IsEnraged);
            Assert.AreEqual(ENRAGED_MAX_HP, _stats.GetEffectiveStat(id, StatID.MaxHP));
            Assert.AreEqual(ENRAGED_ATTACK, _stats.GetEffectiveStat(id, StatID.AttackPower));
            Assert.AreEqual((float)ENRAGED_MAX_HP, _stats.GetCurrentHP(id));
        }

        // QA 4
        [Test]
        public void test_spawn_unknownType_oneErrorNoEntryNoStatsNoRandom()
        {
            var id = new EntityID(MOB_RAW_ID);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"^\[MobRuntimeStore\]"));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, id, new MobTypeID(UNKNOWN_TYPE_RAW), Vector3.zero));

            Assert.IsFalse(_store.TryGet(id, out _));
            Assert.AreEqual(0, _stats.GetEffectiveStat(id, StatID.MaxHP));
            Assert.AreEqual(0, _rng.DrawCount);
        }

        // QA 5
        [Test]
        public void test_spawn_foreignZone_oneWarningNoEntryNoStatsNoRandom()
        {
            var id = new EntityID(MOB_RAW_ID);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"^\[MobRuntimeStore\]"));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(new ZoneID(OTHER_ZONE_RAW), id, new MobTypeID(KNOWN_TYPE_RAW), Vector3.zero));

            Assert.AreEqual(0, _store.Count);
            Assert.AreEqual(0, _stats.GetEffectiveStat(id, StatID.MaxHP));
            Assert.AreEqual(0, _rng.DrawCount);
        }

        // QA 6
        [Test]
        public void test_spawn_duplicate_oneErrorEntryAndStatsUnchangedNoRandom()
        {
            var id = new EntityID(MOB_RAW_ID);
            var first = new Vector3(1f, 0f, 1f);
            Spawn(id, first);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"^\[MobRuntimeStore\]"));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, id, new MobTypeID(KNOWN_TYPE_RAW), new Vector3(9f, 0f, 9f)));

            Assert.IsTrue(_store.TryGet(id, out MobRuntimeEntry e));
            Assert.AreEqual(first, e.CommittedPosition);
            Assert.AreEqual(BASE_MAX_HP, _stats.GetEffectiveStat(id, StatID.MaxHP));
            Assert.AreEqual(1, _rng.DrawCount);
        }

        // QA 7
        [Test]
        public void test_dispose_laterRaiseAddsNothingAndSecondDisposeIsHarmless()
        {
            var id = new EntityID(MOB_RAW_ID);
            _store.Dispose();

            Spawn(id, Vector3.zero);
            Assert.DoesNotThrow(() => _store.Dispose());

            Assert.AreEqual(0, _store.Count);
            Assert.AreEqual(0, _rng.DrawCount);
            Assert.AreEqual(0, _stats.GetEffectiveStat(id, StatID.MaxHP));
        }

        // QA 8
        [Test]
        public void test_trySetCommitted_unknownId_falseWithoutLog()
        {
            bool ok = _store.TrySetCommitted(new EntityID(MOB_RAW_ID), Vector3.one, 10);

            Assert.IsFalse(ok);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_trySetCommitted_knownId_updatesPositionAndFacingKeepsRest()
        {
            var id = new EntityID(MOB_RAW_ID);
            Spawn(id, new Vector3(1f, 2f, 3f));

            bool ok = _store.TrySetCommitted(id, new Vector3(4f, 2f, 5f), 450);

            Assert.IsTrue(ok);
            _store.TryGet(id, out MobRuntimeEntry e);
            Assert.AreEqual(new Vector3(4f, 2f, 5f), e.CommittedPosition);
            Assert.AreEqual((short)450, e.FacingAngle);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), e.SpawnPoint);
        }

        // QA 9
        [Test]
        public void test_remove_thenTryGetFalseAndCountDrops()
        {
            var id = new EntityID(MOB_RAW_ID);
            Spawn(id, Vector3.zero);

            bool removed = _store.Remove(id);

            Assert.IsTrue(removed);
            Assert.IsFalse(_store.TryGet(id, out _));
            Assert.AreEqual(0, _store.Count);
            Assert.IsFalse(_store.Remove(id));
        }

        // QA 10
        [Test]
        public void test_copyCommittedPositions_oneEntryPerMobWithCommittedPositions()
        {
            var a = new EntityID(MOB_RAW_ID);
            var b = new EntityID(MOB_RAW_ID + 1u);
            Spawn(a, Vector3.zero);
            Spawn(b, Vector3.one);
            _store.TrySetCommitted(a, new Vector3(7f, 0f, 8f), 0);
            var into = new Dictionary<EntityID, Vector3>();

            _store.CopyCommittedPositions(into);

            Assert.AreEqual(2, into.Count);
            Assert.AreEqual(new Vector3(7f, 0f, 8f), into[a]);
            Assert.AreEqual(Vector3.one, into[b]);
        }

        // QA 11
        [Test]
        public void test_copyCommittedPositions_150Mobs_allocatesNothingOver100Calls()
        {
            for (uint i = 0; i < MANY_MOBS; i++)
            {
                Spawn(new EntityID(MOB_RAW_ID + i), new Vector3(i, 0f, 0f));
            }

            var into = new Dictionary<EntityID, Vector3>(MANY_MOBS);
            _store.CopyCommittedPositions(into);
            into.Clear();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < CALLS; i++)
            {
                into.Clear();
                _store.CopyCommittedPositions(into);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(MANY_MOBS, into.Count);
            Assert.AreEqual(0L, after - before);
        }

        // QA 12
        [Test]
        public void test_entityState_withReader_carriesSeededHpAndWirePosition()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, new Vector3(1.5f, 0.25f, -2f));
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            bool found = source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual((short)150, view.PosX);
            Assert.AreEqual((short)25, view.PosY);
            Assert.AreEqual((short)-200, view.PosZ);
            Assert.AreEqual(BASE_MAX_HP, view.MaxHp);
            Assert.AreEqual(BASE_MAX_HP, view.CurrentHp);
        }

        [Test]
        public void test_entityState_enragedMob_readsScaledHp()
        {
            _registry.Defs[new MobTypeID(KNOWN_TYPE_RAW)] = Def(KNOWN_TYPE_RAW, 1.0f);
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, Vector3.zero, roll: 0.0);
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.AreEqual(ENRAGED_MAX_HP, view.MaxHp);
            Assert.AreEqual(ENRAGED_MAX_HP, view.CurrentHp);
        }

        [Test]
        public void test_entityState_farPosition_clampsToWireLimit()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, new Vector3(1000000f, -1000000f, 1000000f));
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.AreEqual(Wire(WireFixedPointCodec.MaxPositionMeters), view.PosX);
            Assert.AreEqual(Wire(-WireFixedPointCodec.MaxPositionMeters), view.PosY);
            Assert.AreEqual(Wire(WireFixedPointCodec.MaxPositionMeters), view.PosZ);
        }

        [Test]
        public void test_entityState_farNegativeX_clampsToNegativeWireLimit()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, new Vector3(-1000000f, 0f, -1000000f));
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.AreEqual(Wire(-WireFixedPointCodec.MaxPositionMeters), view.PosX);
            Assert.AreEqual(Wire(-WireFixedPointCodec.MaxPositionMeters), view.PosZ);
        }

        [Test]
        public void test_entityState_facing0_rotationIsIdentity()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, Vector3.zero);
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.AreEqual((short)0, view.FacingAngle);
            Assert.AreEqual((short)0, view.RotX);
            Assert.AreEqual((short)0, view.RotY);
            Assert.AreEqual((short)0, view.RotZ);
            Assert.AreEqual((short)ROTATION_SCALE, view.RotW);
        }

        [Test]
        public void test_entityState_facing3599_rotationMatchesYaw()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, Vector3.zero);
            _store.TrySetCommitted(id, Vector3.zero, 3599);
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            source.TryGetMobState(id, out ZoneMobStateView view);

            Quaternion yaw = Quaternion.Euler(0f, 359.9f, 0f);
            Assert.AreEqual((short)3599, view.FacingAngle);
            Assert.AreEqual((short)Mathf.RoundToInt(yaw.x * ROTATION_SCALE), view.RotX);
            Assert.AreEqual((short)Mathf.RoundToInt(yaw.y * ROTATION_SCALE), view.RotY);
            Assert.AreEqual((short)Mathf.RoundToInt(yaw.z * ROTATION_SCALE), view.RotZ);
            Assert.AreEqual((short)Mathf.RoundToInt(yaw.w * ROTATION_SCALE), view.RotW);
        }

        [Test]
        public void test_entityState_facing900_rotationIsYaw90()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, Vector3.zero);
            _store.TrySetCommitted(id, Vector3.zero, 900);
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            source.TryGetMobState(id, out ZoneMobStateView view);

            Quaternion yaw = Quaternion.Euler(0f, 90f, 0f);
            Assert.AreEqual((short)900, view.FacingAngle);
            Assert.AreEqual((short)0, view.RotX);
            Assert.AreEqual((short)Mathf.RoundToInt(yaw.y * ROTATION_SCALE), view.RotY);
            Assert.AreEqual((short)0, view.RotZ);
            Assert.AreEqual((short)Mathf.RoundToInt(yaw.w * ROTATION_SCALE), view.RotW);
        }

        // QA 13
        [Test]
        public void test_entityState_readerLacksMob_zerosAndTrue()
        {
            EntityID id = RosterMob(Vector3.one);
            ZoneEntityStateSource source = BuildSource(new MobRuntimeStoreTransformReader(_store));

            bool found = source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual((short)0, view.PosX);
            Assert.AreEqual((short)0, view.PosZ);
            Assert.AreEqual((short)0, view.FacingAngle);
        }

        [Test]
        public void test_entityState_nullReader_zerosAndTrue()
        {
            EntityID id = RosterMob(Vector3.one);
            Spawn(id, new Vector3(5f, 0f, 5f));
            ZoneEntityStateSource source = BuildSource(null);

            bool found = source.TryGetMobState(id, out ZoneMobStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual((short)0, view.PosX);
            Assert.AreEqual((short)0, view.RotW);
        }

        // QA 14
        [Test]
        public void test_skillPositionSource_afterTrySetCommitted_returnsStorePositionNotSpawn()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, new Vector3(1f, 0f, 1f));
            _store.TrySetCommitted(id, new Vector3(3.5f, 0f, -4f), 0);
            var skill = new ZoneSkillPositionSource(BuildSource(new MobRuntimeStoreTransformReader(_store)));

            bool ok = skill.TryGetPositionXZ(id, out float x, out float z);

            Assert.IsTrue(ok);
            Assert.AreEqual(3.5f, x, 0.001f);
            Assert.AreEqual(-4f, z, 0.001f);
        }

        [Test]
        public void test_movementDecorator_passesStoreMobPositionThrough()
        {
            EntityID id = RosterMob(Vector3.zero);
            Spawn(id, new Vector3(1f, 0f, 1f));
            _store.TrySetCommitted(id, new Vector3(3.5f, 0f, -4f), 0);
            var decorator = new MovementZoneEntityStateSource(BuildSource(new MobRuntimeStoreTransformReader(_store)));

            bool found = decorator.TryGetMobState(id, out ZoneMobStateView view);

            Assert.IsTrue(found);
            Assert.AreEqual((short)350, view.PosX);
            Assert.AreEqual((short)-400, view.PosZ);
        }

        [Test]
        public void test_trySetCommitted_afterRemove_falseWithoutLog()
        {
            var id = new EntityID(MOB_RAW_ID);
            Spawn(id, Vector3.zero);
            _store.Remove(id);

            bool ok = _store.TrySetCommitted(id, Vector3.one, 10);

            Assert.IsFalse(ok);
            Assert.IsFalse(_store.TryGet(id, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_remove_unknownId_falseWithoutLog()
        {
            bool removed = _store.Remove(new EntityID(MOB_RAW_ID));

            Assert.IsFalse(removed);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_constructor_negativeCapacity_doesNotThrowAndStoreWorks()
        {
            MobRuntimeStore store = null!;
            Assert.DoesNotThrow(() => store = new MobRuntimeStore(Zone, _bus, _registry, _rng,
                new CharacterStatsMobStatWriter(_stats), -1));
            try
            {
                Assert.AreEqual(0, store.Count);
            }
            finally
            {
                store.Dispose();
            }
        }

        [Test]
        public void test_constructor_nullDependency_throws()
        {
            var w = new CharacterStatsMobStatWriter(_stats);
            Assert.Throws<ArgumentNullException>(() => new MobRuntimeStore(Zone, null!, _registry, _rng, w, 1));
            Assert.Throws<ArgumentNullException>(() => new MobRuntimeStore(Zone, _bus, null!, _rng, w, 1));
            Assert.Throws<ArgumentNullException>(() => new MobRuntimeStore(Zone, _bus, _registry, null!, w, 1));
            Assert.Throws<ArgumentNullException>(() => new MobRuntimeStore(Zone, _bus, _registry, _rng, null!, 1));
        }
    }
}
