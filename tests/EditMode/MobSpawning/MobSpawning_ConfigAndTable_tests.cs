using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.MobSpawning
{
    /// <summary>
    /// EditMode tests for Mob Spawning Story 001: <see cref="MobSpawningConfigValues"/> (AC-1 to AC-3),
    /// <see cref="ZoneSpawnTableValidator"/> (AC-5, AC-6) and the <see cref="ZoneDefinition.SpawnTable"/> field (AC-4).
    /// AC-7 (assembly) is covered by the ADR-012 boundary test.
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_ConfigAndTable_Tests
    {
        private const uint KNOWN_MOB_A = 1u;
        private const uint KNOWN_MOB_B = 2u;
        private const uint UNKNOWN_MOB = 99u;
        private static readonly Vector3 BOUNDS_MIN = new Vector3(-10f, 0f, -10f);
        private static readonly Vector3 BOUNDS_MAX = new Vector3(10f, 5f, 10f);
        private static readonly Vector3 INSIDE = new Vector3(0f, 1f, 0f);

        private readonly List<Object> _created = new List<Object>();
        private FakeRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new FakeRegistry(KNOWN_MOB_A, KNOWN_MOB_B);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private sealed class FakeRegistry : IMobDefinitionRegistry
        {
            private readonly HashSet<uint> _known = new HashSet<uint>();
            private readonly MobDefinition _def = new MobDefinition(
                mobTypeID: new MobTypeID(KNOWN_MOB_A), displayName: "Fake", baseMaxHP: 10, baseAttackPower: 1,
                baseDefense: 0, attackRange: 1f, moveSpeed: 1f, aggroRange: 5f, leashRange: 10f,
                attackWindUpTicks: 12, attackRecoveryTicks: 5, attackCooldownTicks: 20, killXP: 1,
                mobTier: MobTier.Common, enragedChance: 0f, enragedMaxHPMultiplier: 1f,
                enragedAttackPowerMultiplier: 1f, enragedXPMultiplier: 1f, enragedLootQualityBonusTier: 0);

            public FakeRegistry(params uint[] known)
            {
                foreach (uint k in known)
                {
                    _known.Add(k);
                }
            }

            public MobDefinition GetDefinition(MobTypeID id) => _known.Contains(id.RawValue) ? _def : null;
        }

        private static SpawnPointEntry Entry(uint id, uint mob = KNOWN_MOB_A, Vector3? pos = null, int over = 0)
        {
            return new SpawnPointEntry(id, new MobTypeID(mob), pos ?? INSIDE, over);
        }

        private ZoneSpawnTable MakeTable(ZoneTemplateID template, params SpawnPointEntry[] entries)
        {
            var t = ScriptableObject.CreateInstance<ZoneSpawnTable>();
            _created.Add(t);
            t.SetForTests(template, entries);
            return t;
        }

        private bool ValidateTable(params SpawnPointEntry[] entries)
        {
            return ZoneSpawnTableValidator.Validate(MakeTable(ZoneTemplateID.StartingZone, entries),
                ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX, _registry);
        }

        private static MobSpawningConfigValues V(int c = 60, int u = 120, int r = 300, int e = 600, int retry = 20)
        {
            return new MobSpawningConfigValues(c, u, r, e, retry);
        }

        private static void ExpectError(string pattern)
        {
            LogAssert.Expect(LogType.Error, new Regex(pattern));
        }

        private static void AssertValid(MobSpawningConfigValues v)
        {
            Assert.IsTrue(v.Validate());
        }

        private static void AssertOneErrorNaming(MobSpawningConfigValues v, string knob, int value)
        {
            ExpectError(knob + " = " + value);
            Assert.IsFalse(v.Validate());
        }

        // ---- QA 1 (AC-1) ----

        [Test]
        public void test_defaults_validAndExpectedValues()
        {
            MobSpawningConfigValues v = MobSpawningConfigValues.Defaults;
            AssertValid(v);
            Assert.AreEqual(60, v.RespawnDelayCommonTicks);
            Assert.AreEqual(120, v.RespawnDelayUncommonTicks);
            Assert.AreEqual(300, v.RespawnDelayRareTicks);
            Assert.AreEqual(600, v.RespawnDelayEliteTicks);
            Assert.AreEqual(20, v.SpawnDensityRetryIntervalTicks);
        }

        [Test]
        public void test_freshConfigAsset_fromAssetGivesDefaultsAndValid()
        {
            var asset = ScriptableObject.CreateInstance<MobSpawningConfig>();
            _created.Add(asset);
            MobSpawningConfigValues v = MobSpawningConfigValues.FromAsset(asset);
            AssertValid(v);
            Assert.AreEqual(60, v.RespawnDelayCommonTicks);
            Assert.AreEqual(600, v.RespawnDelayEliteTicks);
            Assert.AreEqual(20, v.SpawnDensityRetryIntervalTicks);
        }

        // ---- QA 2 (AC-2) ----

        [Test]
        public void test_commonBoundaries_minMaxPassOutsideFails()
        {
            AssertValid(V(c: 20));
            AssertValid(V(c: 300, u: 400, r: 500));
            AssertOneErrorNaming(V(c: 19), "RESPAWN_DELAY_COMMON_TICKS", 19);
            AssertOneErrorNaming(V(c: 301, u: 400, r: 500), "RESPAWN_DELAY_COMMON_TICKS", 301);
        }

        [Test]
        public void test_uncommonBoundaries_minMaxPassOutsideFails()
        {
            AssertValid(V(c: 20, u: 60));
            AssertValid(V(u: 600, r: 700, e: 800));
            AssertOneErrorNaming(V(c: 20, u: 59), "RESPAWN_DELAY_UNCOMMON_TICKS", 59);
            AssertOneErrorNaming(V(u: 601, r: 700, e: 800), "RESPAWN_DELAY_UNCOMMON_TICKS", 601);
        }

        [Test]
        public void test_rareBoundaries_minMaxPassOutsideFails()
        {
            AssertValid(V(c: 20, u: 60, r: 120));
            AssertValid(V(r: 1200, e: 1300));
            AssertOneErrorNaming(V(c: 20, u: 60, r: 119), "RESPAWN_DELAY_RARE_TICKS", 119);
            AssertOneErrorNaming(V(r: 1201, e: 1300), "RESPAWN_DELAY_RARE_TICKS", 1201);
        }

        [Test]
        public void test_eliteBoundaries_minMaxPassOutsideFails()
        {
            AssertValid(V(c: 20, u: 60, r: 120, e: 300));
            AssertValid(V(e: 3600));
            AssertOneErrorNaming(V(c: 20, u: 60, r: 120, e: 299), "RESPAWN_DELAY_ELITE_TICKS", 299);
            AssertOneErrorNaming(V(e: 3601), "RESPAWN_DELAY_ELITE_TICKS", 3601);
        }

        [Test]
        public void test_retryIntervalBoundaries_minMaxPassOutsideFails()
        {
            AssertValid(V(retry: 10));
            AssertValid(V(retry: 200));
            AssertOneErrorNaming(V(retry: 9), "SPAWN_DENSITY_RETRY_INTERVAL_TICKS", 9);
            AssertOneErrorNaming(V(retry: 201), "SPAWN_DENSITY_RETRY_INTERVAL_TICKS", 201);
        }

        // ---- QA 3 (AC-2) ----

        [Test]
        public void test_tierOrder_equalUncommonFailsAndStrictlyGreaterPasses()
        {
            AssertValid(V(c: 60, u: 61));
            ExpectError("RESPAWN_DELAY_UNCOMMON_TICKS = 100 must be greater than RESPAWN_DELAY_COMMON_TICKS = 100");
            Assert.IsFalse(V(c: 100, u: 100).Validate());
        }

        [Test]
        public void test_tierOrder_equalRareFailsNamingRareAndUncommon()
        {
            ExpectError("RESPAWN_DELAY_RARE_TICKS = 200 must be greater than RESPAWN_DELAY_UNCOMMON_TICKS = 200");
            Assert.IsFalse(V(u: 200, r: 200).Validate());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tierOrder_equalEliteFailsNamingEliteAndRare()
        {
            ExpectError("RESPAWN_DELAY_ELITE_TICKS = 400 must be greater than RESPAWN_DELAY_RARE_TICKS = 400");
            Assert.IsFalse(V(r: 400, e: 400).Validate());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_rangeAndOrder_bothBrokenLogsBothErrors()
        {
            // Arrange: Uncommon below its range AND not above Common.
            ExpectError("RESPAWN_DELAY_UNCOMMON_TICKS = 50 is outside");
            ExpectError("RESPAWN_DELAY_UNCOMMON_TICKS = 50 must be greater than RESPAWN_DELAY_COMMON_TICKS = 60");

            // Act / Assert
            Assert.IsFalse(V(u: 50).Validate());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_fromAsset_nullAssetWarnsAndReturnsDefaults()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no config asset; using defaults"));
            MobSpawningConfigValues v = MobSpawningConfigValues.FromAsset(null);
            Assert.AreEqual(MobSpawningConfigValues.RESPAWN_DELAY_COMMON_TICKS_DEFAULT, v.RespawnDelayCommonTicks);
            Assert.AreEqual(MobSpawningConfigValues.SPAWN_DENSITY_RETRY_INTERVAL_TICKS_DEFAULT,
                v.SpawnDensityRetryIntervalTicks);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 4 (AC-3) ----

        [Test]
        public void test_respawnDelayForTier_returnsConfiguredValuePerTier()
        {
            MobSpawningConfigValues v = V(c: 61, u: 121, r: 301, e: 601);
            Assert.AreEqual(61, v.RespawnDelayForTier(MobTier.Common));
            Assert.AreEqual(121, v.RespawnDelayForTier(MobTier.Uncommon));
            Assert.AreEqual(301, v.RespawnDelayForTier(MobTier.Rare));
            Assert.AreEqual(601, v.RespawnDelayForTier(MobTier.Elite));
        }

        [Test]
        public void test_respawnDelayForTier_undefinedTierLogsErrorAndReturnsCommon()
        {
            MobSpawningConfigValues v = V(c: 61);
            ExpectError("undefined MobTier 99");
            Assert.AreEqual(61, v.RespawnDelayForTier((MobTier)99));
        }

        // ---- QA 5 (AC-5) ----

        [Test]
        public void test_capacity_exactly150Passes()
        {
            Assert.AreEqual(150, ZoneSlotPool.MAX_MOBS_PER_ZONE);
            Assert.IsTrue(ValidateTable(MakeEntries(150)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_capacity_151FailsWithOverCapacityKey()
        {
            ExpectError("SpawnTableOverCapacity: 151 spawn points exceeds MAX_MOBS_PER_ZONE 150");
            Assert.IsFalse(ValidateTable(MakeEntries(151)));
        }

        private static SpawnPointEntry[] MakeEntries(int count)
        {
            var list = new SpawnPointEntry[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = Entry((uint)(i + 1));
            }

            return list;
        }

        // ---- QA 6 (AC-5) ----

        [Test]
        public void test_badEntry_duplicateIdFailsAndLogsId()
        {
            ExpectError("SpawnPointID 1: duplicate");
            Assert.IsFalse(ValidateTable(Entry(1), Entry(1)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_badEntry_unknownMobTypeFailsAndLogsId()
        {
            ExpectError("SpawnPointID 7: unknown");
            Assert.IsFalse(ValidateTable(Entry(1), Entry(7, mob: UNKNOWN_MOB)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void test_badEntry_oneMetreOutsideMaxOnAxisFailsAndLogsId(int axis)
        {
            Vector3 p = BOUNDS_MAX;
            p[axis] += 1f;
            ExpectError("SpawnPointID 4: position");
            Assert.IsFalse(ValidateTable(Entry(4, pos: p)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void test_badEntry_oneMetreOutsideMinOnAxisFailsAndLogsId(int axis)
        {
            Vector3 p = BOUNDS_MIN;
            p[axis] -= 1f;
            ExpectError("SpawnPointID 5: position");
            Assert.IsFalse(ValidateTable(Entry(5, pos: p)));
        }

        [Test]
        public void test_badEntry_negativeOverrideFailsAndLogsId()
        {
            ExpectError("SpawnPointID 6: negative");
            Assert.IsFalse(ValidateTable(Entry(6, over: -1)));
        }

        [Test]
        public void test_badEntry_nanPositionFailsAndLogsId()
        {
            ExpectError("SpawnPointID 8: position is NaN");
            Assert.IsFalse(ValidateTable(Entry(8, pos: new Vector3(float.NaN, 1f, 0f))));
        }

        [Test]
        public void test_badEntry_infinityPositionFailsAndLogsId()
        {
            ExpectError("SpawnPointID 9: position is NaN or infinite");
            Assert.IsFalse(ValidateTable(Entry(9, pos: new Vector3(0f, float.PositiveInfinity, 0f))));
        }

        [Test]
        public void test_badTable_wrongTemplateFails()
        {
            ZoneSpawnTable t = MakeTable((ZoneTemplateID)7, Entry(1));
            ExpectError("ZoneTemplateID .* differs from the zone.s");
            Assert.IsFalse(ZoneSpawnTableValidator.Validate(t, ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX,
                _registry));
        }

        // ---- QA 7 (AC-5, decision 2) ----

        [Test]
        public void test_allErrorsReported_threeBadEntriesGiveThreeErrors()
        {
            ExpectError("SpawnPointID 2: unknown");
            ExpectError("SpawnPointID 3: position");
            ExpectError("SpawnPointID 4: negative");
            bool ok = ValidateTable(
                Entry(1),
                Entry(2, mob: UNKNOWN_MOB),
                Entry(3, pos: new Vector3(50f, 1f, 0f)),
                Entry(4, over: -5));
            Assert.IsFalse(ok);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 8 (AC-5) ----

        [Test]
        public void test_bounds_pointsExactlyOnMinAndMaxPass()
        {
            Assert.IsTrue(ValidateTable(Entry(1, pos: BOUNDS_MIN), Entry(2, pos: BOUNDS_MAX)));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 9 (AC-5) ----

        [Test]
        public void test_emptyTable_passesWithOneWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("spawn table is empty"));
            Assert.IsTrue(ValidateTable());
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 10 (AC-6) ----

        [Test]
        public void test_bossGuard_overrideAboveZeroFailsWithKey()
        {
            ExpectError("BossSpawnPointNotSupported.*SpawnPointID 5");
            Assert.IsFalse(ValidateTable(Entry(5, over: 200)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validTable_twoEntriesPassesWithoutLogs()
        {
            Assert.IsTrue(ValidateTable(Entry(1), Entry(2, mob: KNOWN_MOB_B)));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 11 (AC-4) ----

        [Test]
        public void test_zoneDefinition_spawnTableNullUntilAssigned()
        {
            var def = ScriptableObject.CreateInstance<ZoneDefinition>();
            _created.Add(def);
            Assert.IsNull(def.SpawnTable);

            ZoneSpawnTable t = MakeTable(ZoneTemplateID.StartingZone, Entry(1));
            def.SetSpawnTableForTests(t);
            Assert.AreSame(t, def.SpawnTable);
        }

        // ---- Code review 2026-11-16: null inputs and combined faults ----

        [Test]
        public void test_validate_nullTableFailsWithError()
        {
            ExpectError("spawn table is null");
            Assert.IsFalse(ZoneSpawnTableValidator.Validate(null, ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX,
                _registry));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validateEntries_nullListFailsWithError()
        {
            ExpectError("entries is null");
            Assert.IsFalse(ZoneSpawnTableValidator.ValidateEntries(null, BOUNDS_MIN, BOUNDS_MAX, _registry));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_nullRegistryLogsOneErrorAndFails()
        {
            ExpectError("mob definition registry is null");
            ZoneSpawnTable t = MakeTable(ZoneTemplateID.StartingZone, Entry(1), Entry(2));
            Assert.IsFalse(ZoneSpawnTableValidator.Validate(t, ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX,
                null));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_table_nullEntriesArrayReadsAsEmpty()
        {
            ZoneSpawnTable t = MakeTable(ZoneTemplateID.StartingZone, null);
            Assert.IsNotNull(t.Entries);
            Assert.AreEqual(0, t.Entries.Count);
            LogAssert.Expect(LogType.Warning, new Regex("spawn table is empty"));
            Assert.IsTrue(ZoneSpawnTableValidator.Validate(t, ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX,
                _registry));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_wrongTemplateAndBadEntryLogsBoth()
        {
            ZoneSpawnTable t = MakeTable((ZoneTemplateID)7, Entry(3, over: -1));
            ExpectError("ZoneTemplateID .* differs from the zone.s");
            ExpectError("SpawnPointID 3: negative");
            Assert.IsFalse(ZoneSpawnTableValidator.Validate(t, ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX,
                _registry));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_oneEntryWithTwoFaultsLogsBoth()
        {
            ExpectError("SpawnPointID 4: position .* outside");
            ExpectError("SpawnPointID 4: negative");
            Assert.IsFalse(ValidateTable(Entry(4, pos: new Vector3(50f, 1f, 0f), over: -2)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_override_zeroPassesAndOneIsBossSpawnPoint()
        {
            Assert.IsTrue(ValidateTable(Entry(1, over: 0)));
            ExpectError("BossSpawnPointNotSupported: SpawnPointID 2 has RespawnDelayOverrideTicks 1");
            Assert.IsFalse(ValidateTable(Entry(2, over: 1)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_invertedBoundsFailWithoutCheckingEntries()
        {
            ExpectError("walkable bounds .* are not finite or not ordered");
            Assert.IsFalse(ZoneSpawnTableValidator.ValidateEntries(new[] { Entry(1) }, BOUNDS_MAX, BOUNDS_MIN,
                _registry));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_validate_nanBoundsFail()
        {
            ExpectError("walkable bounds .* are not finite or not ordered");
            Assert.IsFalse(ZoneSpawnTableValidator.ValidateEntries(new[] { Entry(1) },
                new Vector3(float.NaN, 0f, 0f), BOUNDS_MAX, _registry));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
