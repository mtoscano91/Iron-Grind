using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.Navigation
{
    /// <summary>
    /// EditMode unit tests for Navigation Story 001: NavigationConfig validation, MobEventBus and the stand-in speed
    /// source.
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_Config_Tests
    {
        private NavigationConfig _config;

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
                _config = null;
            }
        }

        // Builds values from the defaults with one knob replaced.
        private static NavigationConfigValues With(
            int? pool = null, int? pathUpdates = null, int? partialTimeout = null, float? returnCap = null,
            float? minFraction = null, int? stuck = null, int? offMesh = null, float? sampleRadius = null)
        {
            NavigationConfigValues d = NavigationConfigValues.Defaults;
            return new NavigationConfigValues(
                pool ?? d.MaxAgentPoolSize, pathUpdates ?? d.MaxPathUpdatesPerTick,
                partialTimeout ?? d.PartialPathTimeoutTicks, returnCap ?? d.ReturnSpeedCap,
                minFraction ?? d.MinValidSpawnFraction, stuck ?? d.NavStuckRerouteDetectionTicks,
                offMesh ?? d.NavOffMeshRecoveryTicks, sampleRadius ?? d.SpawnSampleRadius);
        }

        private static bool Validate(NavigationConfigValues values, out List<string> errors, out List<string> warnings)
        {
            errors = new List<string>();
            warnings = new List<string>();
            return NavigationConfigValues.Validate(values, errors, warnings);
        }

        private static void AssertRejected(NavigationConfigValues values, string knob)
        {
            bool ok = Validate(values, out List<string> errors, out _);
            Assert.That(ok, Is.False, knob);
            Assert.That(errors.Exists(e => e.Contains(knob)), Is.True, knob + " not named in errors");
        }

        private static void AssertAccepted(NavigationConfigValues values, string knob)
        {
            bool ok = Validate(values, out List<string> errors, out _);
            Assert.That(ok, Is.True, knob + ": " + string.Join("; ", errors));
        }

        // ---- Config defaults ----

        [Test]
        public void Defaults_MatchGddValues()
        {
            NavigationConfigValues d = NavigationConfigValues.Defaults;

            Assert.That(d.MaxAgentPoolSize, Is.EqualTo(150));
            Assert.That(d.MaxPathUpdatesPerTick, Is.EqualTo(20));
            Assert.That(d.PartialPathTimeoutTicks, Is.EqualTo(20));
            Assert.That(d.ReturnSpeedCap, Is.EqualTo(6.0f));
            Assert.That(d.MinValidSpawnFraction, Is.EqualTo(0.90f));
            Assert.That(d.NavStuckRerouteDetectionTicks, Is.EqualTo(10));
            Assert.That(d.NavOffMeshRecoveryTicks, Is.EqualTo(3));
            Assert.That(d.SpawnSampleRadius, Is.EqualTo(0.1f));
        }

        [Test]
        public void NewAsset_FromAssetEqualsDefaults_AndValidates()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();

            NavigationConfigValues v = NavigationConfigValues.FromAsset(_config);

            NavigationConfigValues d = NavigationConfigValues.Defaults;
            Assert.That(v.MaxAgentPoolSize, Is.EqualTo(d.MaxAgentPoolSize));
            Assert.That(v.MaxPathUpdatesPerTick, Is.EqualTo(d.MaxPathUpdatesPerTick));
            Assert.That(v.PartialPathTimeoutTicks, Is.EqualTo(d.PartialPathTimeoutTicks));
            Assert.That(v.ReturnSpeedCap, Is.EqualTo(d.ReturnSpeedCap));
            Assert.That(v.MinValidSpawnFraction, Is.EqualTo(d.MinValidSpawnFraction));
            Assert.That(v.NavStuckRerouteDetectionTicks, Is.EqualTo(d.NavStuckRerouteDetectionTicks));
            Assert.That(v.NavOffMeshRecoveryTicks, Is.EqualTo(d.NavOffMeshRecoveryTicks));
            Assert.That(v.SpawnSampleRadius, Is.EqualTo(d.SpawnSampleRadius));
            Assert.That(Validate(v, out List<string> errors, out List<string> warnings), Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void SetForTesting_RoundTripsAllKnobs()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _config.SetForTesting(new NavigationConfigValues(150, 7, 33, 4.5f, 0.75f, 12, 5, 0.25f));

            NavigationConfigValues v = NavigationConfigValues.FromAsset(_config);

            Assert.That(v.MaxAgentPoolSize, Is.EqualTo(150));
            Assert.That(v.MaxPathUpdatesPerTick, Is.EqualTo(7));
            Assert.That(v.PartialPathTimeoutTicks, Is.EqualTo(33));
            Assert.That(v.ReturnSpeedCap, Is.EqualTo(4.5f));
            Assert.That(v.MinValidSpawnFraction, Is.EqualTo(0.75f));
            Assert.That(v.NavStuckRerouteDetectionTicks, Is.EqualTo(12));
            Assert.That(v.NavOffMeshRecoveryTicks, Is.EqualTo(5));
            Assert.That(v.SpawnSampleRadius, Is.EqualTo(0.25f));
        }

        // ---- Provider contract (AC1) ----

        [Test]
        public void Provider_DeclaresExactlySixMethods()
        {
            var expected = new Dictionary<string, (Type[] Parameters, Type Return)>
            {
                { "SetDestination", (new[] { typeof(EntityID), typeof(Vector3) }, typeof(void)) },
                { "SetSpeed", (new[] { typeof(EntityID), typeof(float) }, typeof(void)) },
                { "Stop", (new[] { typeof(EntityID) }, typeof(void)) },
                { "Resume", (new[] { typeof(EntityID) }, typeof(void)) },
                { "IsPathStale", (new[] { typeof(EntityID) }, typeof(bool)) },
                { "GetCurrentVelocity", (new[] { typeof(EntityID) }, typeof(Vector3)) },
            };

            MethodInfo[] methods = typeof(INavigationProvider).GetMethods();

            Assert.That(methods.Length, Is.EqualTo(6));
            var names = new List<string>();
            foreach (MethodInfo m in methods)
            {
                names.Add(m.Name);
            }
            Assert.That(names, Is.EquivalentTo(expected.Keys));
            foreach (MethodInfo m in methods)
            {
                (Type[] parameters, Type returnType) = expected[m.Name];
                Type[] actual = Array.ConvertAll(m.GetParameters(), p => p.ParameterType);
                Assert.That(actual, Is.EqualTo(parameters), m.Name + " parameters");
                Assert.That(m.ReturnType, Is.EqualTo(returnType), m.Name + " return type");
            }
        }

        [Test]
        public void FromAsset_OutOfRangeValue_IsCopiedNotClamped()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _config.SetForTesting(With(returnCap: 99f));

            Assert.That(NavigationConfigValues.FromAsset(_config).ReturnSpeedCap, Is.EqualTo(99f));
        }

        [Test]
        public void FromAsset_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => NavigationConfigValues.FromAsset(null));
        }

        // ---- Config ranges ----

        [Test]
        public void PartialPathTimeout_BoundsPass_OutsideFails()
        {
            AssertAccepted(With(partialTimeout: 10), "PARTIAL_PATH_TIMEOUT_TICKS");
            AssertAccepted(With(partialTimeout: 200), "PARTIAL_PATH_TIMEOUT_TICKS");
            AssertRejected(With(partialTimeout: 9), "PARTIAL_PATH_TIMEOUT_TICKS");
            AssertRejected(With(partialTimeout: 201), "PARTIAL_PATH_TIMEOUT_TICKS");
        }

        [Test]
        public void ReturnSpeedCap_BoundsPass_OutsideFails()
        {
            AssertAccepted(With(returnCap: 1.0f), "RETURN_SPEED_CAP");
            AssertAccepted(With(returnCap: 10.0f), "RETURN_SPEED_CAP");
            AssertRejected(With(returnCap: 0.99f), "RETURN_SPEED_CAP");
            AssertRejected(With(returnCap: 10.01f), "RETURN_SPEED_CAP");
        }

        [Test]
        public void MinValidSpawnFraction_BoundsPass_OutsideFails()
        {
            AssertAccepted(With(minFraction: 0.50f), "MIN_VALID_SPAWN_FRACTION");
            AssertAccepted(With(minFraction: 1.00f), "MIN_VALID_SPAWN_FRACTION");
            AssertRejected(With(minFraction: 0.49f), "MIN_VALID_SPAWN_FRACTION");
            AssertRejected(With(minFraction: 1.01f), "MIN_VALID_SPAWN_FRACTION");
        }

        [Test]
        public void NavStuckRerouteDetection_BoundsPass_OutsideFails()
        {
            AssertAccepted(With(stuck: 3), "NAV_STUCK_REROUTE_DETECTION_TICKS");
            AssertAccepted(With(stuck: 60), "NAV_STUCK_REROUTE_DETECTION_TICKS");
            AssertRejected(With(stuck: 2), "NAV_STUCK_REROUTE_DETECTION_TICKS");
            AssertRejected(With(stuck: 61), "NAV_STUCK_REROUTE_DETECTION_TICKS");
        }

        [Test]
        public void NavOffMeshRecovery_BoundsPass_OutsideFails()
        {
            AssertAccepted(With(offMesh: 1), "NAV_OFF_MESH_RECOVERY_TICKS");
            AssertAccepted(With(offMesh: 10), "NAV_OFF_MESH_RECOVERY_TICKS");
            AssertRejected(With(offMesh: 0), "NAV_OFF_MESH_RECOVERY_TICKS");
            AssertRejected(With(offMesh: 11), "NAV_OFF_MESH_RECOVERY_TICKS");
        }

        [Test]
        public void SpawnSampleRadius_BoundsPass_OutsideFails()
        {
            AssertAccepted(With(sampleRadius: 0.01f), "SPAWN_SAMPLE_RADIUS");
            AssertAccepted(With(sampleRadius: 1.0f), "SPAWN_SAMPLE_RADIUS");
            AssertRejected(With(sampleRadius: 0.009f), "SPAWN_SAMPLE_RADIUS");
            AssertRejected(With(sampleRadius: 1.01f), "SPAWN_SAMPLE_RADIUS");
        }

        [Test]
        public void MaxPathUpdates_ZeroAnd151_Fail()
        {
            AssertRejected(With(pathUpdates: 0), "MAX_PATH_UPDATES_PER_TICK");
            AssertRejected(With(pathUpdates: 151), "MAX_PATH_UPDATES_PER_TICK");
        }

        [TestCase(1)]
        [TestCase(4)]
        public void MaxPathUpdates_OneToFour_PassesWithOneWarning(int value)
        {
            bool ok = Validate(With(pathUpdates: value), out List<string> errors, out List<string> warnings);

            Assert.That(ok, Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("MAX_PATH_UPDATES_PER_TICK"));
        }

        [TestCase(5)]
        [TestCase(150)]
        public void MaxPathUpdates_FiveAnd150_PassWithNoWarning(int value)
        {
            bool ok = Validate(With(pathUpdates: value), out List<string> errors, out List<string> warnings);

            Assert.That(ok, Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Is.Empty);
        }

        [TestCase("RETURN_SPEED_CAP")]
        [TestCase("MIN_VALID_SPAWN_FRACTION")]
        [TestCase("SPAWN_SAMPLE_RADIUS")]
        public void NaNKnob_Fails(string knob)
        {
            float nan = float.NaN;
            NavigationConfigValues values;
            switch (knob)
            {
                case "RETURN_SPEED_CAP": values = With(returnCap: nan); break;
                case "MIN_VALID_SPAWN_FRACTION": values = With(minFraction: nan); break;
                case "SPAWN_SAMPLE_RADIUS": values = With(sampleRadius: nan); break;
                default: throw new ArgumentException(knob);
            }

            AssertRejected(values, knob);
        }

        [Test]
        public void InfiniteReturnSpeedCap_Fails()
        {
            AssertRejected(With(returnCap: float.PositiveInfinity), "RETURN_SPEED_CAP");
            AssertRejected(With(returnCap: float.NegativeInfinity), "RETURN_SPEED_CAP");
        }

        // ---- Validate with pre-filled lists ----

        [Test]
        public void Validate_PrefilledErrors_ValidDefaults_ReturnsTrueAndKeepsOldEntry()
        {
            var errors = new List<string> { "old" };
            var warnings = new List<string>();

            bool ok = NavigationConfigValues.Validate(NavigationConfigValues.Defaults, errors, warnings);

            Assert.That(ok, Is.True);
            Assert.That(errors, Is.EqualTo(new[] { "old" }));
        }

        [Test]
        public void Validate_PrefilledErrors_InvalidKnob_ReturnsFalseAndAppends()
        {
            var errors = new List<string> { "old" };
            var warnings = new List<string>();

            bool ok = NavigationConfigValues.Validate(With(returnCap: 99f), errors, warnings);

            Assert.That(ok, Is.False);
            Assert.That(errors.Count, Is.EqualTo(2));
        }

        [Test]
        public void Validate_PrefilledWarnings_Appends()
        {
            var errors = new List<string>();
            var warnings = new List<string> { "old" };

            bool ok = NavigationConfigValues.Validate(With(pathUpdates: 1), errors, warnings);

            Assert.That(ok, Is.True);
            Assert.That(warnings.Count, Is.EqualTo(2));
        }

        // ---- Pool size parity ----

        [Test]
        public void PoolSize_EqualsMaxMobsPerZone_Passes()
        {
            Assert.That(NavigationConfigValues.Defaults.MaxAgentPoolSize, Is.EqualTo(ZoneSlotPool.MAX_MOBS_PER_ZONE));
            AssertAccepted(With(pool: ZoneSlotPool.MAX_MOBS_PER_ZONE), "MAX_AGENT_POOL_SIZE");
        }

        [Test]
        public void PoolSize_DiffersFromMaxMobsPerZone_Fails()
        {
            AssertRejected(With(pool: ZoneSlotPool.MAX_MOBS_PER_ZONE - 1), "MAX_AGENT_POOL_SIZE");
        }

        [Test]
        public void PoolSize_ZeroAndAboveMax_Fail()
        {
            AssertRejected(With(pool: 0), "MAX_AGENT_POOL_SIZE");
            AssertRejected(With(pool: 151), "MAX_AGENT_POOL_SIZE");
        }

        [Test]
        public void PoolSize_151_FailsWithRangeAndParityErrors()
        {
            bool ok = Validate(With(pool: 151), out List<string> errors, out _);

            Assert.That(ok, Is.False);
            Assert.That(errors.Count, Is.EqualTo(2));
            Assert.That(errors[0], Does.Contain("MAX_AGENT_POOL_SIZE"));
            Assert.That(errors[1], Does.Contain("MAX_AGENT_POOL_SIZE"));
        }

        [Test]
        public void PoolSize_One_FailsWithParityErrorOnly()
        {
            bool ok = Validate(With(pool: 1), out List<string> errors, out _);

            Assert.That(ok, Is.False);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.Contain("MAX_MOBS_PER_ZONE"));
        }

        // ---- Bus ----

        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly EntityID ENTITY = new EntityID(42u);
        private static readonly MobTypeID MOB_TYPE = new MobTypeID(9u);
        private static readonly Vector3 SPAWN_POS = new Vector3(1f, 2f, 3f);

        [Test]
        public void Bus_RaiseMobSpawned_BothHandlersReceiveSameArgs()
        {
            var bus = new MobEventBus();
            var received = new List<MobSpawnedArgs>();
            bus.OnMobSpawned += a => received.Add(a);
            bus.OnMobSpawned += a => received.Add(a);

            bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS));

            Assert.That(received.Count, Is.EqualTo(2));
            foreach (MobSpawnedArgs a in received)
            {
                Assert.That(a.Zone, Is.EqualTo(ZONE));
                Assert.That(a.Entity, Is.EqualTo(ENTITY));
                Assert.That(a.MobType, Is.EqualTo(MOB_TYPE));
                Assert.That(a.SpawnPosition, Is.EqualTo(SPAWN_POS));
            }
        }

        [Test]
        public void Bus_RaiseMobDied_BothHandlersReceiveSameArgs()
        {
            var bus = new MobEventBus();
            var received = new List<MobDiedArgs>();
            bus.OnMobDied += a => received.Add(a);
            bus.OnMobDied += a => received.Add(a);

            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));

            Assert.That(received.Count, Is.EqualTo(2));
            foreach (MobDiedArgs a in received)
            {
                Assert.That(a.Zone, Is.EqualTo(ZONE));
                Assert.That(a.Entity, Is.EqualTo(ENTITY));
            }
        }

        [Test]
        public void Bus_ThrowingSubscriber_SecondStillRuns_ExceptionLogged()
        {
            var bus = new MobEventBus();
            int secondCalls = 0;
            bus.OnMobDied += _ => throw new InvalidOperationException("boom");
            bus.OnMobDied += _ => secondCalls++;
            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));

            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void Bus_ThrowingSpawnedSubscriber_SecondStillRuns_ExceptionLogged()
        {
            var bus = new MobEventBus();
            int secondCalls = 0;
            bus.OnMobSpawned += _ => throw new InvalidOperationException("boom");
            bus.OnMobSpawned += _ => secondCalls++;
            LogAssert.Expect(LogType.Exception, new Regex("boom"));

            bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS));

            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void ArgTypes_AreReadonlyValueTypes()
        {
            foreach (Type t in new[] { typeof(MobSpawnedArgs), typeof(MobDiedArgs) })
            {
                Assert.That(t.IsValueType, Is.True, t.Name);
                Assert.That(t.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false), Is.True, t.Name);
                Assert.That(t.Namespace, Is.EqualTo("IronGrind.Events"), t.Name);
            }
        }

        // ---- Unsubscribe ----

        [Test]
        public void Unsubscribe_AddTwiceRemoveOnce_CalledOnce()
        {
            var bus = new MobEventBus();
            int calls = 0;
            Action<MobDiedArgs> handler = _ => calls++;
            bus.OnMobDied += handler;
            bus.OnMobDied += handler;
            bus.OnMobDied -= handler;

            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_SpawnedAddTwiceRemoveOnce_CalledOnce()
        {
            var bus = new MobEventBus();
            int calls = 0;
            Action<MobSpawnedArgs> handler = _ => calls++;
            bus.OnMobSpawned += handler;
            bus.OnMobSpawned += handler;
            bus.OnMobSpawned -= handler;

            bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_UnknownHandler_IsNoOp()
        {
            var bus = new MobEventBus();
            int calls = 0;
            bus.OnMobDied += _ => calls++;
            Action<MobDiedArgs> unknown = _ => { };

            bus.OnMobDied -= unknown;
            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Subscribe_Null_IsNoOp()
        {
            var bus = new MobEventBus();
            bus.OnMobDied += null;
            bus.OnMobSpawned += null;
            bus.OnMobDied -= null;

            Assert.DoesNotThrow(() => bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY)));
            Assert.DoesNotThrow(() => bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS)));
        }

        [Test]
        public void Bus_HandlerRemovedDuringRaise_StillRunsThatRaise_NotTheNext()
        {
            var bus = new MobEventBus();
            var log = new List<string>();
            Action<MobDiedArgs> b = _ => log.Add("B");
            Action<MobDiedArgs> c = _ => log.Add("C");
            Action<MobDiedArgs> a = _ =>
            {
                log.Add("A");
                bus.OnMobDied -= b;
            };
            bus.OnMobDied += a;
            bus.OnMobDied += b;
            bus.OnMobDied += c;

            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));
            Assert.That(log, Is.EqualTo(new[] { "A", "B", "C" }));

            log.Clear();
            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));
            Assert.That(log, Is.EqualTo(new[] { "A", "C" }));
        }

        [Test]
        public void Bus_HandlerAddedDuringRaise_FirstCalledOnNextRaise()
        {
            var bus = new MobEventBus();
            var log = new List<string>();
            Action<MobDiedArgs> d = _ => log.Add("D");
            bool added = false;
            Action<MobDiedArgs> a = _ =>
            {
                log.Add("A");
                if (!added)
                {
                    added = true;
                    bus.OnMobDied += d;
                }
            };
            bus.OnMobDied += a;

            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));
            Assert.That(log, Is.EqualTo(new[] { "A" }));

            log.Clear();
            bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY));
            Assert.That(log, Is.EqualTo(new[] { "A", "D" }));
        }

        [Test]
        public void Bus_RaiseWithNoSubscribers_DoesNotThrow()
        {
            var bus = new MobEventBus();

            Assert.DoesNotThrow(() => bus.RaiseMobDied(new MobDiedArgs(ZONE, ENTITY)));
            Assert.DoesNotThrow(() => bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS)));
        }

        [Test]
        public void Unsubscribe_SpawnedUnknownHandler_IsNoOp()
        {
            var bus = new MobEventBus();
            int calls = 0;
            bus.OnMobSpawned += _ => calls++;
            Action<MobSpawnedArgs> unknown = _ => { };

            bus.OnMobSpawned -= unknown;
            bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Unsubscribe_SpawnedOnlyHandler_RaiseCallsNothing()
        {
            var bus = new MobEventBus();
            int calls = 0;
            Action<MobSpawnedArgs> handler = _ => calls++;
            bus.OnMobSpawned += handler;
            bus.OnMobSpawned -= handler;

            Assert.DoesNotThrow(() => bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS)));
            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public void Unsubscribe_SpawnedAddABA_RemoveAOnce_RaisesAThenBOrderPreserved()
        {
            var bus = new MobEventBus();
            var log = new List<string>();
            Action<MobSpawnedArgs> a = _ => log.Add("A");
            Action<MobSpawnedArgs> b = _ => log.Add("B");
            bus.OnMobSpawned += a;
            bus.OnMobSpawned += b;
            bus.OnMobSpawned += a;
            bus.OnMobSpawned -= a;

            bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, ENTITY, MOB_TYPE, SPAWN_POS));

            Assert.That(log, Is.EqualTo(new[] { "A", "B" }));
        }
    }
}
