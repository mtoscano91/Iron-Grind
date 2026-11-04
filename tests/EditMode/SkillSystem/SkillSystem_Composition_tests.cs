using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace IronGrind.Tests.EditMode.SkillSystemTests
{
    /// <summary>
    /// Skill System Story 011 Part A: the production composition wires the Skill System (step order, request handler,
    /// entry / release / level-up hooks, cooldown snapshot forwarding, seams by type). Built over the real
    /// <see cref="ZoneServerComposition"/> through <see cref="WiRig"/>; no I/O. Pipeline cases are in the Part B region.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_Composition_Tests
    {
        private const uint CLIENT_ONE = 11u;
        private const uint CLIENT_TWO = 12u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint SKILL_ID_BASE = 100u;
        private const int SKILL_COUNT = 3;
        private const int LEVEL_UNLOCK_LATE = 5;
        private const int LEVEL_UP_TO = 5;
        private const int LOCKED_SLOT = 2;
        private const byte CLASS_WARRIOR = 0;
        private const uint ENVELOPE_TICK = 0u;
        private const uint SEQUENCE = 1u;
        private static readonly int[] UNLOCK_LEVELS = { 1, 1, LEVEL_UNLOCK_LATE };

        private SkillDatabase _database;
        private readonly List<WiRig> _rigs = new List<WiRig>();

        // The full RunTeardown is not usable here: with players admitted its close step 2 logs a SaveSession error (the
        // rig has no persistence). So this disposes what the Dispose pass would for the Skill System: the registered
        // SkillSystemTeardown, found in the composition's disposable list, in reverse order like the pass does.
        private void TearDownRig(WiRig rig)
        {
            _rigs.Remove(rig);
            FieldInfo field = typeof(ZoneServerComposition).GetField("_disposables", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "_disposables list");
            var disposables = (List<IDisposable>)field.GetValue(rig.Comp);
            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                if (disposables[i] is SkillSystemTeardown teardown)
                {
                    teardown.Dispose();
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (WiRig rig in _rigs.ToArray())
            {
                TearDownRig(rig);
            }

            _rigs.Clear();
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }
        }

        private WiRig StartRig(uint[] classOrder = null, int[] unlockLevels = null)
        {
            var ids = new uint[SKILL_COUNT];
            var defs = new SkillDefinition[SKILL_COUNT];
            for (int i = 0; i < SKILL_COUNT; i++)
            {
                ids[i] = classOrder != null ? classOrder[i] : SKILL_ID_BASE + (uint)i;
                defs[i] = new SkillDefinition { ID = ids[i], UnlockLevel = (unlockLevels ?? UNLOCK_LEVELS)[i] };
            }

            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0, ids));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);

            var rig = new WiRig();
            _rigs.Add(rig);
            rig.Comp.ClassRegistryOverride = registry;
            rig.Comp.SkillDatabaseOverride = _database;
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            return rig;
        }

        private static EntityID AddPlayer(WiRig rig, uint client, out byte slot)
        {
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            Assert.IsTrue(pool.TryAllocatePlayer(pool.Zone, new CharacterID(client), new AccountID(client + ACCOUNT_OFFSET), client,
                out slot, out EntityID id));
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(client));
            rig.Net.ClientListener.AddClient(client);
            rig.Comp.GuardChain.RegisterEntityOwnership(client, id);
            rig.Comp.GuardChain.MarkSessionReady(client);
            rig.Comp.ZoneInstancing.Directory.Add(client, new CharacterID(client));
            return id;
        }

        // Adds the player, applies a Level 1 record through the zone's own Persistence applier (as admission does after
        // the slot is allocated, Character Persistence Story 018), then runs the real entry completion.
        private static EntityID Admit(WiRig rig, uint client)
        {
            EntityID id = AddPlayer(rig, client, out byte slot);
            CharacterRecord record = CharacterStubDefaults.CreateRecord(new AccountID(client + ACCOUNT_OFFSET), new CharacterID(client), CLASS_WARRIOR);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Comp.PersistenceService.ApplyLoadedRecord(id, record));
            rig.Comp.ZoneInstancing.EntryCompletion.OnPlayerAdmitted(client, slot, id);
            return id;
        }

        private static bool TryDecodeSnapshot(WiRig rig, uint client, out SkillCooldownSnapshot snapshot)
        {
            snapshot = default;
            foreach ((uint to, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (to != client || !MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope envelope)
                    || envelope.MessageTypeId != SkillCooldownSnapshot.MessageTypeId)
                {
                    continue;
                }

                return SkillCooldownSnapshotCodec.TryReadBody(data.AsSpan(ServerMessageEnvelope.WireSize), out snapshot);
            }

            return false;
        }

        // Depth-limited field walk: the first instance of T reachable from root, or null.
        private static T FindInstance<T>(object root, int depth = 4) where T : class
        {
            return Find(root, typeof(T), depth, new HashSet<object>()) as T;
        }

        private static object Find(object node, Type target, int depth, HashSet<object> seen)
        {
            if (node == null || depth < 0 || node.GetType().IsPrimitive || node is string || !seen.Add(node))
            {
                return null;
            }

            if (target.IsInstanceOfType(node))
            {
                return node;
            }

            if (node is IEnumerable sequence && !(node is IDictionary))
            {
                foreach (object item in sequence)
                {
                    object hit = Find(item, target, depth - 1, seen);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }

            for (Type type = node.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType.IsPrimitive || field.FieldType.IsEnum)
                    {
                        continue;
                    }

                    object hit = Find(field.GetValue(node), target, depth - 1, seen);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }

            return null;
        }

        // The value of the first field of the object whose type is assignable to T (no deep walk).
        private static T DirectField<T>(object owner) where T : class
        {
            foreach (FieldInfo field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.GetValue(owner) is T value)
                {
                    return value;
                }
            }

            return null;
        }

        private sealed class IntakeProbeStep : IZoneSimulationStep
        {
            private readonly SkillCastIntake _intake;
            private readonly int _slot;
            public bool Saw;
            public SkillCastRequest Request;

            public IntakeProbeStep(SkillCastIntake intake, int slot)
            {
                _intake = intake;
                _slot = slot;
            }

            public void Run(uint currentTick, float deltaTime)
            {
                if (_intake.TryGetPending(_slot, out PendingSkillCast cast))
                {
                    Saw = true;
                    Request = cast.Request;
                }
            }
        }

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return Vector3.zero; }
        }

        private sealed class RecordingResponder : IZoneEntryResponder
        {
            public int ReadyCalls;
            public int SnapshotCalls;
            public uint ReadyClient;
            public uint SnapshotClient;
            public EntityID SnapshotEntity;

            public void SendSessionReady(uint clientId) { ReadyCalls++; ReadyClient = clientId; }

            public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId)
            {
                SnapshotCalls++;
                SnapshotClient = clientId;
                SnapshotEntity = entityId;
            }
        }

        // ---- Step order ----

        [Test]
        public void test_composition_step_order_is_movement_then_combat_then_skill()
        {
            WiRig rig = StartRig();
            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;

            int movement = steps.IndexOf(rig.Comp.Movement);
            int combat = steps.IndexOf(rig.Comp.AutoAttackCombat);
            int skill = steps.IndexOf(rig.Comp.SkillStep);

            Assert.IsNotNull(rig.Comp.SkillStep);
            Assert.GreaterOrEqual(movement, 0);
            Assert.Greater(combat, movement, "Combat after Movement");
            Assert.Greater(skill, combat, "Skill after Combat");
        }

        // ---- Handler registered ----

        [Test]
        public void test_composition_skill_cast_request_through_dispatcher_reaches_the_intake()
        {
            WiRig rig = StartRig();
            EntityID player = AddPlayer(rig, CLIENT_ONE, out byte slot);
            var probe = new IntakeProbeStep(rig.Comp.SkillIntake, slot);
            int skillIndex = rig.Comp.SimulationSteps.IndexOf(rig.Comp.SkillStep);
            rig.Comp.SimulationSteps.Insert(skillIndex, probe);

            var message = new byte[ClientEntityMessageEnvelope.WireSize + SkillCastRequest.BodySize];
            var envelope = new ClientEntityMessageEnvelope(SkillCastRequest.MessageTypeId, SEQUENCE, ENVELOPE_TICK, player.RawValue);
            MessageEnvelopeCodec.Write(message, in envelope);
            SkillCastRequestCodec.WriteBody(message.AsSpan(ClientEntityMessageEnvelope.WireSize),
                new SkillCastRequest(player.RawValue, SKILL_ID_BASE, 0u, ENVELOPE_TICK));
            Assert.IsTrue(rig.Net.Intake.TryAccept(CLIENT_ONE, message));

            rig.Net.FireTick();

            Assert.IsTrue(probe.Saw, "stored in dispatch, visible before the Skill step of the same tick");
            Assert.AreEqual(SKILL_ID_BASE, probe.Request.SkillId);
        }

        // ---- Spawn before snapshot ----

        [Test]
        public void test_composition_admitted_player_snapshot_carries_class_skill_ids_with_zero_expiries()
        {
            // Registered in a non-ascending id order: the snapshot must follow class order, not id order.
            uint[] classOrder = { SKILL_ID_BASE + 2u, SKILL_ID_BASE, SKILL_ID_BASE + 1u };
            WiRig rig = StartRig(classOrder, new[] { LEVEL_UNLOCK_LATE, 1, 1 });
            using (var log = new WiLogCapture())
            {
                Admit(rig, CLIENT_ONE);
                LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
                rig.Net.FireTick();

                Assert.IsTrue(TryDecodeSnapshot(rig, CLIENT_ONE, out SkillCooldownSnapshot snapshot), "snapshot was sent");
                for (int i = 0; i < SKILL_COUNT; i++)
                {
                    Assert.AreEqual(new SkillCooldownEntry(classOrder[i], 0u), snapshot.GetEntry(i), "entry " + i);
                }

                foreach (string line in log.Lines)
                {
                    StringAssert.DoesNotContain("no skill store", line);
                }
            }
        }

        // ---- Despawn ----

        [Test]
        public void test_composition_release_removes_the_store_and_readmit_has_no_cooldowns()
        {
            WiRig rig = StartRig();
            EntityID first = Admit(rig, CLIENT_ONE);
            Assert.IsTrue(rig.Comp.SkillService.HasRecord(first));
            Assert.IsTrue(rig.Comp.SkillService.SetCooldownForTesting(first, 0, 999u));

            Assert.IsTrue(rig.Comp.ZoneInstancing.Pool.ReleasePlayer(first));

            Assert.IsFalse(rig.Comp.SkillService.HasRecord(first), "store removed on release");
            EntityID second = Admit(rig, CLIENT_TWO);
            Assert.AreNotEqual(first, second, "a fresh admission has a new EntityID");
            Assert.IsTrue(rig.Comp.SkillService.TryGetCooldownExpiry(second, SKILL_ID_BASE, out uint expiry));
            Assert.AreEqual(0u, expiry);
        }

        // ---- Level-up subscription ----

        private static Delegate[] LevelUpSubscribers(LevelingService leveling, out Action<LevelUpEventArgs> raise)
        {
            FieldInfo field = typeof(LevelingService).GetField("OnLevelUp", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "OnLevelUp backing field");
            raise = (Action<LevelUpEventArgs>)field.GetValue(leveling);
            return raise?.GetInvocationList() ?? new Delegate[0];
        }

        [Test]
        public void test_composition_level_up_unlocks_the_skill_with_exactly_one_skill_subscriber()
        {
            WiRig rig = StartRig();
            EntityID player = Admit(rig, CLIENT_ONE);
            Assert.IsTrue(rig.Comp.SkillService.TryGetSkill(player, LOCKED_SLOT, out SkillInstance before));
            Assert.IsFalse(before.IsUnlocked, "locked at spawn");

            Delegate[] subscribers = LevelUpSubscribers(rig.Comp.ZoneLeveling, out Action<LevelUpEventArgs> raise);
            int skillSubscribers = 0;
            foreach (Delegate subscriber in subscribers)
            {
                if (ReferenceEquals(subscriber.Target, rig.Comp.SkillService))
                {
                    skillSubscribers++;
                }
            }

            Assert.AreEqual(1, skillSubscribers);
            foreach (Delegate subscriber in subscribers)
            {
                if (ReferenceEquals(subscriber.Target, rig.Comp.SkillService))
                {
                    Assert.AreEqual("HandleLevelUp", subscriber.Method.Name, "the Story 003 handler");
                    StringAssert.DoesNotContain("<", subscriber.Method.Name, "not a lambda or closure");
                }
            }

            raise(new LevelUpEventArgs(player, LEVEL_UP_TO));

            Assert.IsTrue(rig.Comp.SkillService.TryGetSkill(player, LOCKED_SLOT, out SkillInstance after));
            Assert.IsTrue(after.IsUnlocked, "unlocked by OnLevelUp");
        }

        [Test]
        public void test_composition_level_up_for_one_unlocking_skill_sends_exactly_one_unlock_notification()
        {
            WiRig rig = StartRig();
            EntityID player = Admit(rig, CLIENT_ONE);
            LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
            rig.Net.FireTick();
            rig.Net.Seam.Sent.Clear();
            LevelUpSubscribers(rig.Comp.ZoneLeveling, out Action<LevelUpEventArgs> raise);

            raise(new LevelUpEventArgs(player, LEVEL_UP_TO));
            rig.Net.FireTick();

            int notifications = 0;
            foreach ((uint to, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (to == CLIENT_ONE && MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope e)
                    && e.MessageTypeId == SkillUnlockNotification.MessageTypeId)
                {
                    notifications++;
                    Assert.IsTrue(SkillUnlockNotificationCodec.TryReadBody(data.AsSpan(ServerMessageEnvelope.WireSize), out SkillUnlockNotification n));
                    Assert.AreEqual(SKILL_ID_BASE + 2u, n.SkillId.Value);
                }
            }

            Assert.AreEqual(1, notifications);
        }

        [Test]
        public void test_composition_teardown_leaves_no_skill_subscriber_on_zone_leveling()
        {
            WiRig rig = StartRig();
            SkillSystemService service = rig.Comp.SkillService;
            LevelingService leveling = rig.Comp.ZoneLeveling;
            int before = 0;
            foreach (Delegate d in LevelUpSubscribers(leveling, out _))
            {
                before += ReferenceEquals(d.Target, service) ? 1 : 0;
            }

            Assert.AreEqual(1, before, "subscribed while running");

            TearDownRig(rig);

            int after = 0;
            foreach (Delegate d in LevelUpSubscribers(leveling, out _))
            {
                after += ReferenceEquals(d.Target, service) ? 1 : 0;
            }

            Assert.AreEqual(0, after, "Dispose pass unsubscribed the Skill System");
        }

        // ---- Wiring by type ----

        [Test]
        public void test_composition_seams_are_the_expected_production_and_stand_in_types()
        {
            WiRig rig = StartRig();

            Assert.IsInstanceOf<StandInAutoAttackDamageApplier>(rig.Comp.AutoAttackInnerDamageApplier);
            Assert.IsNotInstanceOf<DamageOutputApplier>(rig.Comp.AutoAttackInnerDamageApplier, "Decision B: inner applier");
            Assert.IsInstanceOf<ForwardingZoneEntryResponder>(rig.Comp.EntryResponder);
            Assert.IsInstanceOf<SkillCooldownSnapshotResponder>(rig.Comp.EntryResponder.Target);

            var damageStep = FindInstance<SkillDamageExecutionStep>(rig.Comp.SkillExecutor);
            Assert.IsNotNull(damageStep, "damage step inside the executor");
            Assert.IsNotNull(FindInstance<CompositeSkillExecutionStep>(rig.Comp.SkillExecutor));
            Assert.IsNotNull(FindInstance<SkillEffectExecutionStep>(rig.Comp.SkillExecutor));
            Assert.IsNotNull(FindInstance<StatusEffectsSkillEffectSink>(rig.Comp.SkillExecutor));
            Assert.IsNotNull(FindInstance<CharacterStatsSkillStatSource>(damageStep));
            Assert.IsNotNull(FindInstance<StandInSkillDamageCalculator>(damageStep));
            Assert.IsNull(FindInstance<DamageCalculatorSkillAdapter>(damageStep), "adapter stays unwired");
            Assert.IsNotNull(FindInstance<StandInAutoAttackDamageApplier>(damageStep));
            Assert.IsInstanceOf<StandInAutoAttackDamageApplier>(DirectField<IAutoAttackDamageApplier>(damageStep), "inner applier, not the wrapper");
            Assert.AreSame(rig.Comp.AutoAttackCombat, FindInstance<AutoAttackCombatSystem>(damageStep));
            Assert.AreSame(rig.Comp.AutoAttackFaceOutput, FindInstance<AutoFaceEmitter>(damageStep));
            Assert.IsNotNull(FindInstance<SkillCastOutputEmitter>(rig.Comp.SkillStep));

            AutoAttackCombatSystem combat = rig.Comp.AutoAttackCombat;
            Assert.AreSame(combat, DirectField<IAutoAttackSkillUseSink>(damageStep), "one combat instance is the skill-use sink");
            Assert.AreSame(combat, DirectField<IMobKillReporter>(damageStep), "and the kill reporter");
            var effectStep = FindInstance<SkillEffectExecutionStep>(rig.Comp.SkillExecutor);
            ISkillStatSource damageStats = DirectField<ISkillStatSource>(damageStep);
            Assert.IsInstanceOf<CharacterStatsSkillStatSource>(damageStats);
            Assert.AreSame(damageStats, DirectField<ISkillStatSource>(effectStep), "one stat source shared by the damage and effect steps");
            Assert.IsInstanceOf<StandInAutoAttackXpAward>(DirectField<IAutoAttackXpAward>(damageStep));
            Assert.IsInstanceOf<CharacterStatsSkillManaSink>(DirectField<ISkillManaSink>(rig.Comp.SkillExecutor));
            Assert.IsNotNull(FindInstance<NoPartyZonePresence>(rig.Comp.SkillStep, 6), "no-party presence");
            Assert.IsNotNull(FindInstance<NoSilenceSource>(rig.Comp.SkillStep), "no-silence source");

            SkillCastOutputEmitter stepOutput = FindInstance<SkillCastOutputEmitter>(rig.Comp.SkillStep);
            Assert.AreSame(stepOutput, FindInstance<SkillCastOutputEmitter>(rig.Comp.SkillExecutor), "one shared emitter");
        }

        // ---- Forwarding responder ----

        [Test]
        public void test_forwarding_responder_before_target_set_reaches_the_logging_stub()
        {
            var recording = new RecordingResponder();
            var responder = new ForwardingZoneEntryResponder(recording);
            var entity = new EntityID(7u);

            responder.SendSessionReady(CLIENT_ONE);
            responder.SendSkillCooldownSnapshot(CLIENT_TWO, entity);

            Assert.AreSame(recording, responder.Target);
            Assert.AreEqual(1, recording.ReadyCalls);
            Assert.AreEqual(CLIENT_ONE, recording.ReadyClient);
            Assert.AreEqual(1, recording.SnapshotCalls);
            Assert.AreEqual(CLIENT_TWO, recording.SnapshotClient);
            Assert.AreEqual(entity, recording.SnapshotEntity);
        }

        [Test]
        public void test_forwarding_responder_default_target_is_the_logging_stub()
        {
            var responder = new ForwardingZoneEntryResponder();
            using (var log = new WiLogCapture())
            {
                responder.SendSessionReady(CLIENT_ONE);

                Assert.IsInstanceOf<LoggingZoneEntryResponder>(responder.Target);
                CollectionAssert.Contains(log.Lines, LoggingZoneEntryResponder.NOT_IMPLEMENTED_MESSAGE);
            }
        }

        [Test]
        public void test_forwarding_responder_after_target_set_forwards_both_calls()
        {
            var responder = new ForwardingZoneEntryResponder();
            var target = new RecordingResponder();
            responder.Target = target;

            responder.SendSessionReady(CLIENT_ONE);
            responder.SendSkillCooldownSnapshot(CLIENT_TWO, new EntityID(1u));

            Assert.AreEqual(1, target.ReadyCalls);
            Assert.AreEqual(CLIENT_ONE, target.ReadyClient);
            Assert.AreEqual(1, target.SnapshotCalls);
            Assert.AreEqual(CLIENT_TWO, target.SnapshotClient);
            Assert.AreEqual(new EntityID(1u), target.SnapshotEntity);
        }

        [Test]
        public void test_forwarding_responder_null_target_throws()
        {
            var responder = new ForwardingZoneEntryResponder();

            Assert.Throws<ArgumentNullException>(() => responder.Target = null);
        }

        [Test]
        public void test_composition_session_ready_reaches_inner_stub_and_snapshot_is_sent()
        {
            WiRig rig = StartRig();
            EntityID player = AddPlayer(rig, CLIENT_TWO, out _);
            rig.Comp.SkillService.OnEntitySpawned(player, ClassType.Warrior);
            using (var log = new WiLogCapture())
            {
                rig.Comp.EntryResponder.SendSessionReady(CLIENT_TWO);
                rig.Comp.EntryResponder.SendSkillCooldownSnapshot(CLIENT_TWO, player);
                rig.Net.FireTick();

                CollectionAssert.Contains(log.Lines, LoggingZoneEntryResponder.NOT_IMPLEMENTED_MESSAGE, "SessionReady reaches the inner stub");
            }

            // The snapshot is proven by decoding it from the sent bytes (the inner stub only sees SessionReady).
            Assert.IsTrue(TryDecodeSnapshot(rig, CLIENT_TWO, out _), "snapshot sent");
        }

        // ---- Part B ----
        // Part B (real tick pipeline): EC-SK-1, AC-SK-49 / EC-SK-6, same-tick result batch, Decision B DamageEvent test,
        // buff / heal integration, CR-SK-23 same-batch via the real RUBatchWriter.

        private const uint SKILL_SELF_BUFF = 200u;
        private const uint SKILL_SELF_HEAL = 201u;
        private const uint SKILL_HIT = 202u;
        private const uint SKILL_LEVEL_TWO = 203u;
        private const uint BUFF_RAW = 7u;
        private const uint BUFF_DURATION = 100u;
        private const int COOLDOWN_TICKS = 40;
        private const int FIXED_DAMAGE = 25;
        private const float HEAL_MULTIPLIER = 2f;
        private const int INTELLIGENCE_AT_L1 = 10;
        private const float RANGE = 10f;
        private const float MOB_DISTANCE = 1f;
        private const uint FUTURE_TICK_MARGIN = 0u;

        private sealed class FixedSkillDamage : ISkillDamageCalculator
        {
            public int Calls;

            public IronGrind.DamageCalculation.DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId,
                IronGrind.DamageCalculation.DamageContext context)
            {
                Calls++;
                return new IronGrind.DamageCalculation.DamageResult(FIXED_DAMAGE, 0, FIXED_DAMAGE, false, false, context, false);
            }
        }

        // Replaces the logging inner state source: players alive, one mob alive at MOB_DISTANCE on +X.
        private sealed class AliveZoneState : IronGrind.ServerLogic.ZoneInstancing.IZoneEntityStateSource
        {
            public bool TryGetPlayerState(EntityID id, out IronGrind.ServerLogic.ZoneInstancing.ZonePlayerStateView view)
            {
                view = new IronGrind.ServerLogic.ZoneInstancing.ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0, 100, 100, 100, 100, 1,
                    false, 0, 0, "p", 0u);
                return true;
            }

            public bool TryGetMobState(EntityID id, out IronGrind.ServerLogic.ZoneInstancing.ZoneMobStateView view)
            {
                view = new IronGrind.ServerLogic.ZoneInstancing.ZoneMobStateView((short)Mathf.RoundToInt(MOB_DISTANCE * 100f), 0, 0, 0, 0, 0, 0, 0,
                    100, 100, false, 0);
                return true;
            }
        }

        private sealed class ActionStep : IZoneSimulationStep
        {
            private readonly Action _action;
            public ActionStep(Action action) { _action = action; }
            public void Run(uint currentTick, float deltaTime) { _action(); }
        }

        private sealed class World
        {
            public WiRig Rig;
            public EntityID Player;
            public byte Slot;
            public EntityID Mob;
            public FixedSkillDamage Damage;
        }

        private static SkillDefinition SelfSkill(uint id, int unlock, SkillEffectType effect)
        {
            return new SkillDefinition
            {
                ID = id,
                UnlockLevel = unlock,
                CooldownTicks = COOLDOWN_TICKS,
                TargetConstraint = TargetConstraint.Self,
                SkillEffectType = effect,
                HealMultiplier = HEAL_MULTIPLIER,
                BuffID = (BuffID)BUFF_RAW,
                BuffDefinition = new BuffDefinition((BuffID)BUFF_RAW, default, BUFF_DURATION, 1f, 0f)
            };
        }

        private static SkillDefinition HitSkill()
        {
            return new SkillDefinition
            {
                ID = SKILL_HIT,
                UnlockLevel = 1,
                CooldownTicks = COOLDOWN_TICKS,
                RangeUnits = RANGE,
                TargetConstraint = TargetConstraint.SingleHostile,
                SkillEffectType = SkillEffectType.PhysicalDamage,
                DamageMultiplier = 1f,
                DamageBaseStat = DamageStat.AttackPower
            };
        }

        // Starts the composition over the given roster, admits one player at L1 with real stats, allocates one mob,
        // uses AliveZoneState as the inner state source (EntityStateSourceOverride) and runs one warm-up tick (the expected NavMesh error).
        private World StartWorld(params SkillDefinition[] defs)
        {
            var ids = new uint[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                ids[i] = defs[i].ID;
            }

            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0, ids));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(defs);

            var world = new World { Damage = new FixedSkillDamage(), Rig = new WiRig() };
            WiRig rig = world.Rig;
            rig.Comp.ClassRegistryOverride = registry;
            rig.Comp.SkillDatabaseOverride = _database;
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.SkillDamageCalculatorOverride = world.Damage;
            rig.Comp.EntityStateSourceOverride = new AliveZoneState();
            _rigs.Add(rig);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            world.Player = AddPlayer(rig, CLIENT_ONE, out world.Slot);
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            Assert.IsTrue(pool.TryAllocateMob(pool.Zone, out _, out world.Mob));
            LevelingService leveling = rig.Comp.ZoneLeveling;
            leveling.RegisterPlayerEntity(world.Player);
            leveling.InitializeAtL1(world.Player, CLASS_WARRIOR);
            rig.Comp.SkillService.OnEntitySpawned(world.Player, ClassType.Warrior);
            rig.Comp.ZoneCharacterStats.SetBaseStatFloat(world.Player, StatID.AttackRange, RANGE);
            rig.Comp.ZoneCharacterStats.SetBaseStatFloat(world.Player, StatID.AttackSpeedMultiplier, 1f);
            rig.Comp.ZoneInstancing.EntryCompletion.OnPlayerAdmitted(CLIENT_ONE, world.Slot, world.Player);

            LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
            rig.Net.FireTick();
            Assert.IsTrue(rig.Comp.ZoneCharacterStats.GetCurrentHP(world.Player) > 0f, "caster alive");
            rig.Net.Seam.Sent.Clear();
            return world;
        }

        private static void SendCast(World world, uint skillId, EntityID target)
        {
            uint tick = world.Rig.Comp.TickLoop.ServerTickNumber + FUTURE_TICK_MARGIN;
            var message = new byte[ClientEntityMessageEnvelope.WireSize + SkillCastRequest.BodySize];
            var envelope = new ClientEntityMessageEnvelope(SkillCastRequest.MessageTypeId, SEQUENCE, tick, world.Player.RawValue);
            MessageEnvelopeCodec.Write(message, in envelope);
            SkillCastRequestCodec.WriteBody(message.AsSpan(ClientEntityMessageEnvelope.WireSize),
                new SkillCastRequest(world.Player.RawValue, skillId, target.RawValue, tick));
            Assert.IsTrue(world.Rig.Net.Intake.TryAccept(CLIENT_ONE, message));
        }

        // Every R-U batch sub-message sent to the client since the last Sent.Clear(), in wire order.
        private static List<KeyValuePair<ushort, byte[]>> SubMessagesTo(WiRig rig, uint client)
        {
            var result = new List<KeyValuePair<ushort, byte[]>>();
            foreach ((uint to, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (to != client || !MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope envelope)
                    || envelope.MessageTypeId != RUBatchWriter.TICK_BATCH_RU)
                {
                    continue;
                }

                Assert.IsTrue(BatchHeaderCodec.TryRead(data, out _, out ushort count));
                int offset = BatchHeaderCodec.HeaderSize;
                for (int i = 0; i < count; i++)
                {
                    Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(new ReadOnlySpan<byte>(data, offset, data.Length - offset),
                        out ushort bodySize, out ushort type));
                    offset += BatchSubMessageFraming.SubMessageHeaderSize;
                    result.Add(new KeyValuePair<ushort, byte[]>(type, new ReadOnlySpan<byte>(data, offset, bodySize).ToArray()));
                    offset += bodySize;
                }
            }

            return result;
        }

        private static int IndexOfType(List<KeyValuePair<ushort, byte[]>> messages, ushort type)
        {
            return messages.FindIndex(m => m.Key == type);
        }

        private static SkillCastResult ResultOf(List<KeyValuePair<ushort, byte[]>> messages)
        {
            int index = IndexOfType(messages, SkillCastResult.MessageTypeId);
            Assert.GreaterOrEqual(index, 0, "a SkillCastResult is in the batch");
            Assert.IsTrue(SkillCastResultCodec.TryReadBody(messages[index].Value, out SkillCastResult result));
            return result;
        }

        private static IZoneSimulationStep BeforeSkill(WiRig rig, Action action)
        {
            var step = new ActionStep(action);
            rig.Comp.SimulationSteps.Insert(rig.Comp.SimulationSteps.IndexOf(rig.Comp.SkillStep), step);
            return step;
        }

        [Test]
        public void test_composition_auto_and_cast_same_tick_both_resolve_auto_first_and_timer_is_zero()
        {
            World world = StartWorld(SelfSkill(SKILL_SELF_BUFF, 1, SkillEffectType.ApplyBuff));
            WiRig rig = world.Rig;
            AutoAttackCombatSystem combat = rig.Comp.AutoAttackCombat;
            combat.SetAutoAttack(world.Player, true);
            combat.SetTarget(world.Player, world.Mob);
            int cycle = combat.GetCycleTicks(world.Player);
            Assert.Greater(cycle, 1);
            for (int i = 0; i < cycle - 1; i++)
            {
                rig.Net.FireTick();
            }

            Assert.AreEqual(cycle - 1, combat.GetActionTimer(world.Player), "one tick before the auto fires");
            int timerAtSkill = -1;
            bool castStillPending = false;
            BeforeSkill(rig, () =>
            {
                timerAtSkill = combat.GetActionTimer(world.Player);
                castStillPending = rig.Comp.SkillIntake.TryGetPending(world.Slot, out _);
            });
            rig.Net.Seam.Sent.Clear();
            SendCast(world, SKILL_SELF_BUFF, world.Player);

            rig.Net.FireTick();

            Assert.AreEqual(0, timerAtSkill, "the auto already fired (timer reset) when the Skill step starts");
            Assert.IsTrue(castStillPending, "the cast is not resolved until the Skill step");
            Assert.AreEqual(0, combat.GetActionTimer(world.Player), "timer 0 after the tick");
            List<KeyValuePair<ushort, byte[]>> batch = SubMessagesTo(rig, CLIENT_ONE);
            // Hit detection is geometric (range + facing, no random roll), so one result per fired auto is deterministic.
            Assert.AreEqual(1, batch.FindAll(m => m.Key == HitCheckResult.MessageTypeId).Count, "exactly one hit result");
            Assert.IsTrue(ResultOf(batch).CastAccepted, "the cast resolved");
        }

        [Test]
        public void test_composition_level_up_before_the_skill_step_makes_the_level_two_skill_castable_on_the_same_tick()
        {
            World world = StartWorld(SelfSkill(SKILL_LEVEL_TWO, 2, SkillEffectType.ApplyBuff));
            WiRig rig = world.Rig;
            Assert.AreEqual(1, rig.Comp.ZoneCharacterStats.GetBaseStat(world.Player, StatID.Level), "starts at level 1");
            Assert.IsTrue(rig.Comp.SkillService.TryGetSkill(world.Player, 0, out SkillInstance lockedBefore), "slot exists");
            Assert.IsFalse(lockedBefore.IsUnlocked, "level-2 skill locked before the XP");
            int levelUps = 0;
            rig.Comp.ZoneLeveling.OnLevelUp += args => levelUps++;
            // Earliest point before the Skill step: a step between Combat and Skill awards the XP through the real
            // CharacterStats / LevelingService (a Combat-step kill is unreachable: the stand-in damage never kills).
            BeforeSkill(rig, () =>
                rig.Comp.ZoneCharacterStats.AddExperience(world.Player, rig.Comp.ZoneLeveling.GetExperienceThreshold(world.Player)));
            SendCast(world, SKILL_LEVEL_TWO, world.Player);

            rig.Net.FireTick();

            Assert.AreEqual(1, levelUps, "OnLevelUp fired on tick T");
            Assert.AreEqual(2, rig.Comp.ZoneCharacterStats.GetBaseStat(world.Player, StatID.Level));
            SkillCastResult result = ResultOf(SubMessagesTo(rig, CLIENT_ONE));
            Assert.IsTrue(result.CastAccepted, "accepted, not SkillLocked (code " + result.RejectionCode + ")");
        }

        [Test]
        public void test_composition_level_two_skill_cast_without_level_up_is_rejected_skill_locked()
        {
            World world = StartWorld(SelfSkill(SKILL_LEVEL_TWO, 2, SkillEffectType.ApplyBuff));
            SendCast(world, SKILL_LEVEL_TWO, world.Player);

            world.Rig.Net.FireTick();

            SkillCastResult result = ResultOf(SubMessagesTo(world.Rig, CLIENT_ONE));
            Assert.IsFalse(result.CastAccepted);
            Assert.AreEqual((int)SkillCastRejectionCode.SkillLocked, (int)result.RejectionCode);
        }

        [Test]
        public void test_composition_cast_sent_on_tick_t_is_resolved_in_phase_three_and_its_result_is_in_the_same_batch()
        {
            World world = StartWorld(SelfSkill(SKILL_SELF_BUFF, 1, SkillEffectType.ApplyBuff));
            SendCast(world, SKILL_SELF_BUFF, world.Player);
            Assert.AreEqual(0, SubMessagesTo(world.Rig, CLIENT_ONE).Count, "nothing is resolved before the tick runs");

            world.Rig.Net.FireTick();

            SkillCastResult result = ResultOf(SubMessagesTo(world.Rig, CLIENT_ONE));
            Assert.AreEqual(SKILL_SELF_BUFF, result.SkillId);
            Assert.IsTrue(result.CastAccepted);
            Assert.IsFalse(world.Rig.Comp.SkillIntake.TryGetPending(world.Slot, out _), "intake emptied by the Skill step");
        }

        [Test]
        public void test_composition_accepted_cast_puts_result_then_cooldown_update_in_one_batch()
        {
            World world = StartWorld(SelfSkill(SKILL_SELF_BUFF, 1, SkillEffectType.ApplyBuff));
            SendCast(world, SKILL_SELF_BUFF, world.Player);

            world.Rig.Net.FireTick();

            int sent = 0;
            foreach ((uint to, byte[] data, NetworkChannel _) in world.Rig.Net.Seam.Sent)
            {
                if (to == CLIENT_ONE && MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope e) && e.MessageTypeId == RUBatchWriter.TICK_BATCH_RU)
                {
                    sent++;
                }
            }

            List<KeyValuePair<ushort, byte[]>> batch = SubMessagesTo(world.Rig, CLIENT_ONE);
            int resultIndex = IndexOfType(batch, SkillCastResult.MessageTypeId);
            int cooldownIndex = IndexOfType(batch, SkillCooldownUpdate.MessageTypeId);
            Assert.AreEqual(1, sent, "one R-U batch for the caster");
            Assert.GreaterOrEqual(resultIndex, 0);
            Assert.Greater(cooldownIndex, resultIndex, "result written first");
            Assert.IsTrue(SkillCooldownUpdateCodec.TryReadBody(batch[cooldownIndex].Value, out SkillCooldownUpdate update));
            Assert.AreEqual(SKILL_SELF_BUFF, update.SkillId);
            Assert.Greater(update.CooldownExpiryTick, 0u);
        }

        [Test]
        public void test_composition_skill_hit_through_the_inner_applier_sends_no_damage_event_or_self_damage_event()
        {
            World world = StartWorld(HitSkill());
            SendCast(world, SKILL_HIT, world.Mob);

            world.Rig.Net.FireTick();

            Assert.AreEqual(1, world.Damage.Calls, "the fake calculator produced the hit");
            List<KeyValuePair<ushort, byte[]>> batch = SubMessagesTo(world.Rig, CLIENT_ONE);
            SkillCastResult result = ResultOf(batch);
            Assert.IsTrue(result.CastAccepted, "code " + result.RejectionCode);
            Assert.AreEqual(FIXED_DAMAGE, result.DamageDealt);
            Assert.AreEqual(-1, IndexOfType(batch, DamageEvent.MessageTypeId), "no DamageEvent in the batch");
            foreach ((uint to, byte[] data, NetworkChannel _) in world.Rig.Net.Seam.Sent)
            {
                Assert.IsTrue(MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope e));
                Assert.AreNotEqual(SelfDamageEvent.MessageTypeId, e.MessageTypeId, "no SelfDamageEvent");
                Assert.AreNotEqual(DamageEvent.MessageTypeId, e.MessageTypeId);
            }
        }

        [Test]
        public void test_composition_auto_attack_wrapper_with_a_damaging_result_does_send_a_self_damage_event()
        {
            // Positive control for the check above: R-OD envelopes are top-level in this harness, and the production
            // wrapper (not the inner applier) emits a SelfDamageEvent for an applied hit.
            World world = StartWorld(HitSkill());
            var result = new IronGrind.DamageCalculation.DamageResult(FIXED_DAMAGE, 0, FIXED_DAMAGE, false, false,
                IronGrind.DamageCalculation.DamageContext.PhysicalAuto, false);

            world.Rig.Comp.AutoAttackDamageApplier.Apply(world.Player, world.Mob, in result);
            world.Rig.Net.FireTick();

            int selfDamage = 0;
            foreach ((uint to, byte[] data, NetworkChannel _) in world.Rig.Net.Seam.Sent)
            {
                if (to == CLIENT_ONE && MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope e)
                    && e.MessageTypeId == SelfDamageEvent.MessageTypeId)
                {
                    selfDamage++;
                }
            }

            Assert.AreEqual(1, selfDamage);
        }

        [Test]
        public void test_composition_buff_skill_reaches_the_real_status_effects_service()
        {
            World world = StartWorld(SelfSkill(SKILL_SELF_BUFF, 1, SkillEffectType.ApplyBuff));
            SendCast(world, SKILL_SELF_BUFF, world.Player);

            world.Rig.Net.FireTick();

            Assert.IsTrue(ResultOf(SubMessagesTo(world.Rig, CLIENT_ONE)).CastAccepted);
            Assert.IsTrue(world.Rig.Comp.StatusEffects.TryGetActiveEffect(world.Player, (BuffID)BUFF_RAW, out _));
        }

        [Test]
        public void test_composition_heal_skill_reports_the_delivered_heal_amount_in_the_accepted_result()
        {
            World world = StartWorld(SelfSkill(SKILL_SELF_HEAL, 1, SkillEffectType.InstantHeal));
            world.Rig.Comp.ZoneCharacterStats.SetCurrentHP(world.Player, 1f);
            SendCast(world, SKILL_SELF_HEAL, world.Player);

            world.Rig.Net.FireTick();

            SkillCastResult result = ResultOf(SubMessagesTo(world.Rig, CLIENT_ONE));
            Assert.IsTrue(result.CastAccepted, "code " + result.RejectionCode);
            Assert.AreEqual((int)(INTELLIGENCE_AT_L1 * HEAL_MULTIPLIER), result.HealAmount);
            Assert.Greater(world.Rig.Comp.ZoneCharacterStats.GetCurrentHP(world.Player), 1f);
        }

        [Test]
        public void test_composition_heal_skill_near_full_hp_reports_only_the_delivered_amount()
        {
            const int SHORT_OF_MAX = 5;
            World world = StartWorld(SelfSkill(SKILL_SELF_HEAL, 1, SkillEffectType.InstantHeal));
            int maxHp = world.Rig.Comp.ZoneCharacterStats.GetEffectiveStat(world.Player, StatID.MaxHP);
            world.Rig.Comp.ZoneCharacterStats.SetCurrentHP(world.Player, maxHp - SHORT_OF_MAX);
            SendCast(world, SKILL_SELF_HEAL, world.Player);

            world.Rig.Net.FireTick();

            SkillCastResult result = ResultOf(SubMessagesTo(world.Rig, CLIENT_ONE));
            Assert.IsTrue(result.CastAccepted, "code " + result.RejectionCode);
            Assert.AreEqual(SHORT_OF_MAX, result.HealAmount);
            Assert.AreEqual((float)maxHp, world.Rig.Comp.ZoneCharacterStats.GetCurrentHP(world.Player));
        }
    }
}
