using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.PartySystem;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.DeathAndRespawn;
using IronGrind.Tests.EditMode.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// Party System Story 011: zone composition wiring (AC-1 to AC-7) over <see cref="NavCompositionRig"/> with a
    /// fixed town respawn provider (so Death and Respawn is built). The tick step and the notice sink also have
    /// direct unit tests.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_Composition_Tests
    {
        private const int CUSTOM_MAX_PARTY_SIZE = 3;
        private const int INVALID_MAX_PARTY_SIZE = 5;
        private const uint TICK = 7u;
        private const uint CHARACTER = 501u;
        private const uint CHARACTER_UNKNOWN = 999u;
        private const uint CLIENT = 601u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint PARTY_RAW = 77u;
        private const int MAX_DEPTH = 7;
        private const uint UNPOOLED_ENTITY = 9999u;

        private readonly List<NavCompositionRig> _rigs = new List<NavCompositionRig>();
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (NavCompositionRig rig in _rigs)
            {
                rig.Destroy();
            }

            _rigs.Clear();
            foreach (Object asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }

            _assets.Clear();
        }

        private NavCompositionRig NewRig(bool withRespawn = true, PartySystemConfigValues? config = null)
        {
            var rig = new NavCompositionRig();
            _rigs.Add(rig);
            if (withRespawn)
            {
                rig.Comp.RespawnProviderOverride = new RespawnStubs.Points();
            }

            if (config.HasValue)
            {
                var asset = ScriptableObject.CreateInstance<PartySystemConfig>();
                _assets.Add(asset);
                asset.SetForTests(config.Value);
                rig.Comp.PartySystemConfigOverride = asset;
            }

            return rig;
        }

        private NavCompositionRig StartedRig(bool withRespawn = true, PartySystemConfigValues? config = null)
        {
            NavCompositionRig rig = NewRig(withRespawn, config);
            rig.Start();
            Assert.IsTrue(rig.Comp.IsTickRunning, "startup must reach the tick");
            return rig;
        }

        private static PartySystemConfigValues ConfigWithSize(int size)
        {
            PartySystemConfigValues d = PartySystemConfigValues.Defaults;
            return new PartySystemConfigValues(size, d.XpPartyDeductionRate, d.LevelRangeGate, d.InviteTimeoutSeconds,
                d.InviteRetrySeconds, d.ReconnectGraceSeconds, d.HpDeltaThresholdFraction, d.DisbandTimeoutSeconds);
        }

        private static object FieldNamed(object owner, string name)
        {
            Assert.IsNotNull(owner);
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                | BindingFlags.Public);
            Assert.IsNotNull(field, owner.GetType().Name + " has no field " + name);
            return field.GetValue(owner);
        }

        // Depth-first search over the fields of IronGrind objects for the first instance whose type is named typeName.
        private static object FindInstance(object root, string typeName)
        {
            return Search(root, typeName, new HashSet<object>(), 0);
        }

        private static object Search(object node, string typeName, HashSet<object> seen, int depth)
        {
            if (node == null || depth > MAX_DEPTH || node is string || node is Object)
            {
                return null;
            }

            Type type = node.GetType();
            if (type.IsPrimitive || type.IsEnum || type.IsPointer)
            {
                return null;
            }

            if (type.Name == typeName)
            {
                return node;
            }

            if (!type.IsValueType && !seen.Add(node))
            {
                return null;
            }

            if (node is Array array)
            {
                foreach (object item in array)
                {
                    object hit = Search(item, typeName, seen, depth + 1);
                    if (hit != null)
                    {
                        return hit;
                    }
                }

                return null;
            }

            if (type.Namespace == null || !type.Namespace.StartsWith("IronGrind", StringComparison.Ordinal))
            {
                return null;
            }

            for (Type t = type; t != null; t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                    | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPrimitive || f.FieldType.IsPointer)
                    {
                        continue;
                    }

                    object hit = Search(f.GetValue(node), typeName, seen, depth + 1);
                    if (hit != null)
                    {
                        return hit;
                    }
                }
            }

            return null;
        }

        private static IReadOnlyList<IZoneSlotReleaseListener> ReleaseListeners(NavCompositionRig rig)
        {
            object composite = FindInstance(rig.Comp.ZoneInstancing.Pool, nameof(CompositeZoneSlotReleaseListener));
            Assert.IsNotNull(composite, "release composite found");
            return ((CompositeZoneSlotReleaseListener)composite).Listeners;
        }

        private static int IndexOfSame<T>(IReadOnlyList<T> list, object item)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfType<T>(IReadOnlyList<T> list, Type type)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (type.IsInstanceOfType(list[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        // ---- AC-1: built once, config ----

        [Test]
        public void test_party_composition_registry_is_built_and_shared_by_every_consumer()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();
            PartyRegistry registry = rig.Comp.PartyRegistry;

            // Assert
            Assert.IsNotNull(registry);
            Assert.AreSame(registry, FieldNamed(rig.Comp.PartyPresence, "_registry"));
            Assert.AreSame(registry, FieldNamed(rig.Comp.PartyExperienceSink, "_registry"));
            Assert.AreSame(registry, FieldNamed(FieldNamed(rig.Comp.DeathAndRespawn, "_party"), "_registry"));
            object releaseListener = FindInstance(rig.Comp.ZoneInstancing.Pool, nameof(PartySlotReleaseListener));
            Assert.AreSame(registry, FieldNamed(releaseListener, "_registry"));
            PartyTickStep step = rig.Comp.PartyTickStep;
            Assert.IsNotNull(step);
            Assert.AreEqual(1, rig.Observed.FindAll(n => n == nameof(PartyRegistry)).Count, "registry built once");
            Assert.AreEqual(1, rig.Observed.FindAll(n => n == nameof(PartyTickStep)).Count, "tick step built once");
            Assert.AreSame(registry, FieldNamed(step.Invites, "_registry"));
            Assert.AreSame(registry, FieldNamed(step.Membership, "_registry"));
            Assert.AreSame(registry, FieldNamed(step.Presence, "_registry"));
            Assert.AreSame(registry, FieldNamed(step.Broadcast, "_registry"));
            Assert.AreSame(registry, FieldNamed(step.Health, "_registry"));
        }

        [Test]
        public void test_party_composition_valid_override_sets_the_registry_max_party_size()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig(true, ConfigWithSize(CUSTOM_MAX_PARTY_SIZE));

            // Assert
            Assert.AreEqual(CUSTOM_MAX_PARTY_SIZE, rig.Comp.PartyRegistry.MaxPartySize);
        }

        [Test]
        public void test_party_composition_default_config_gives_the_default_max_party_size()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();

            // Assert
            Assert.AreEqual(PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT, rig.Comp.PartyRegistry.MaxPartySize);
        }

        [Test]
        public void test_party_composition_invalid_override_fails_s5_and_the_zone_never_reaches_active()
        {
            // Arrange
            NavCompositionRig rig = NewRig(true, ConfigWithSize(INVALID_MAX_PARTY_SIZE));
            LogAssert.Expect(LogType.Error, new Regex("MAX_PARTY_SIZE"));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S5: ArgumentException"));

            // Act
            rig.Start();

            // Assert
            Assert.IsFalse(rig.Comp.IsTickRunning);
            Assert.IsNull(rig.Comp.PartyTickStep);
        }

        // ---- AC-2: seams bound ----

        [Test]
        public void test_party_composition_wiring_party_is_the_registry_backed_presence()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();
            object party = FindInstance(rig.Comp.ZoneInstancing.EntryCompletion, nameof(PartyZonePresence));

            // Assert
            Assert.IsNotNull(party, "entry completion holds a PartyZonePresence");
            Assert.AreSame(rig.Comp.PartyPresence, party);
            Assert.IsNull(FindInstance(rig.Comp.ZoneInstancing.EntryCompletion, nameof(NoPartyZonePresence)));
        }

        [Test]
        public void test_party_composition_skill_party_source_holds_the_party_presence()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();
            object source = FindInstance(rig.Comp.SkillStep, "ZoneSkillCastPartySource");

            // Assert
            Assert.IsNotNull(source, "the skill output holds a ZoneSkillCastPartySource");
            Assert.AreSame(rig.Comp.PartyPresence, FieldNamed(source, "_presence"));
        }

        [Test]
        public void test_party_composition_death_and_respawn_status_sink_is_the_party_member_status_sink()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();

            // Assert
            Assert.IsInstanceOf<PartyMemberStatusSink>(FieldNamed(rig.Comp.DeathAndRespawn, "_party"));
        }

        [Test]
        public void test_party_composition_combat_and_skill_share_one_party_experience_distributor()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();
            object combatSink = FieldNamed(rig.Comp.AutoAttackCombat, "_experience");
            object damageStep = FindInstance(rig.Comp.SkillExecutor, "SkillDamageExecutionStep");

            // Assert
            Assert.IsNotNull(damageStep, "the skill executor holds a SkillDamageExecutionStep");
            Assert.IsInstanceOf<PartyExperienceDistributor>(combatSink);
            Assert.AreSame(rig.Comp.PartyExperienceSink, combatSink);
            Assert.AreSame(combatSink, FieldNamed(damageStep, "_experience"));
        }

        // ---- AC-3: requests registered ----

        [Test]
        public void test_party_composition_dispatcher_has_the_four_party_requests_registered()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            var dispatcher = (InboundRequestDispatcher)rig.Net.Dispatcher;

            // Assert
            Assert.IsTrue(dispatcher.IsRegistered(PartyInviteRequest.MessageTypeId), "PartyInviteRequest");
            Assert.IsTrue(dispatcher.IsRegistered(PartyInviteResponse.MessageTypeId), "PartyInviteResponse");
            Assert.IsTrue(dispatcher.IsRegistered(PartyLeaveRequest.MessageTypeId), "PartyLeaveRequest");
            Assert.IsTrue(dispatcher.IsRegistered(PartyDisbandRequest.MessageTypeId), "PartyDisbandRequest");
            Assert.AreEqual(0xE175, PartyLeaveRequest.MessageTypeId);
            Assert.AreEqual(0xE176, PartyDisbandRequest.MessageTypeId);
        }

        // ---- AC-4: tick step placement and behaviour ----

        [Test]
        public void test_party_composition_tick_step_is_listed_once_right_after_the_gateway_link_pump()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;

            // Act
            int count = 0;
            int pump = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] is PartyTickStep)
                {
                    count++;
                }

                if (steps[i].GetType().Name == "GatewayLinkPump")
                {
                    pump = i;
                }
            }

            // Assert
            Assert.AreEqual(1, count);
            Assert.GreaterOrEqual(pump, 0, "the link pump is listed");
            Assert.AreSame(rig.Comp.PartyTickStep, steps[pump + 1]);
            Assert.IsNotNull(rig.Comp.EnemyAI);
            Assert.AreSame(rig.Comp.EnemyAI.IntegrationStep, steps[steps.Count - 1], "the integration step stays last");
        }

        private sealed class OrderRecorder
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<uint> Ticks = new List<uint>();
            public string ThrowAt;

            public Action<uint> Step(string name)
            {
                return tick =>
                {
                    Calls.Add(name);
                    Ticks.Add(tick);
                    if (name == ThrowAt)
                    {
                        throw new InvalidOperationException("boom " + name);
                    }
                };
            }
        }

        private static PartyTickStep StepOver(OrderRecorder recorder)
        {
            return new PartyTickStep(recorder.Step("Tick"), recorder.Step("ApplyQueued"), recorder.Step("Poll"),
                recorder.Step("Flush"), recorder.Step("Run"));
        }

        [Test]
        public void test_party_tick_step_runs_the_five_sub_steps_in_order_with_the_tick()
        {
            // Arrange
            var recorder = new OrderRecorder();
            PartyTickStep step = StepOver(recorder);

            // Act
            step.Run(TICK, 0.05f);

            // Assert
            CollectionAssert.AreEqual(new[] { "Tick", "ApplyQueued", "Poll", "Flush", "Run" }, recorder.Calls);
            foreach (uint tick in recorder.Ticks)
            {
                Assert.AreEqual(TICK, tick);
            }
        }

        [Test]
        public void test_party_tick_step_a_throwing_sub_step_is_logged_and_the_later_ones_still_run()
        {
            // Arrange
            var recorder = new OrderRecorder { ThrowAt = "ApplyQueued" };
            PartyTickStep step = StepOver(recorder);
            LogAssert.Expect(LogType.Error, new Regex(@"\[PartyTickStep\] ApplyQueued failed at tick 7"));

            // Act
            step.Run(TICK, 0.05f);

            // Assert
            CollectionAssert.AreEqual(new[] { "Tick", "ApplyQueued", "Poll", "Flush", "Run" }, recorder.Calls);
        }

        [Test]
        public void test_party_tick_step_a_repeatedly_failing_sub_step_logs_only_the_first_failure()
        {
            // Arrange
            var recorder = new OrderRecorder { ThrowAt = "Poll" };
            PartyTickStep step = StepOver(recorder);
            LogAssert.Expect(LogType.Error, new Regex(@"\[PartyTickStep\] Poll failed at tick 7"));

            // Act
            step.Run(TICK, 0.05f);
            step.Run(TICK + 1u, 0.05f);

            // Assert
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(10, recorder.Calls.Count, "every sub-step still runs on both ticks");
        }

        [Test]
        public void test_party_tick_step_public_constructor_maps_each_argument_to_its_property()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            PartyTickStep composed = rig.Comp.PartyTickStep;

            // Act
            var step = new PartyTickStep(composed.Invites, composed.Membership, composed.Presence, composed.Broadcast,
                composed.Health);

            // Assert
            Assert.IsInstanceOf<PartyInviteService>(composed.Invites);
            Assert.IsInstanceOf<PartyMembershipStep>(composed.Membership);
            Assert.IsInstanceOf<PartyPresenceStep>(composed.Presence);
            Assert.IsInstanceOf<PartyBroadcastStep>(composed.Broadcast);
            Assert.IsInstanceOf<PartyHealthStreamStep>(composed.Health);
            Assert.AreSame(composed.Invites, step.Invites);
            Assert.AreSame(composed.Membership, step.Membership);
            Assert.AreSame(composed.Presence, step.Presence);
            Assert.AreSame(composed.Broadcast, step.Broadcast);
            Assert.AreSame(composed.Health, step.Health);
        }

        [Test]
        public void test_party_tick_step_delegate_constructor_leaves_the_collaborator_properties_null()
        {
            // Arrange / Act
            PartyTickStep step = StepOver(new OrderRecorder());

            // Assert
            Assert.IsNull(step.Invites);
            Assert.IsNull(step.Membership);
            Assert.IsNull(step.Presence);
            Assert.IsNull(step.Broadcast);
            Assert.IsNull(step.Health);
        }

        [Test]
        public void test_party_tick_step_null_collaborators_throw_naming_the_argument()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            PartyTickStep c = rig.Comp.PartyTickStep;

            // Act / Assert
            Assert.AreEqual("invites", Assert.Throws<ArgumentNullException>(
                () => new PartyTickStep(null, c.Membership, c.Presence, c.Broadcast, c.Health)).ParamName);
            Assert.AreEqual("membership", Assert.Throws<ArgumentNullException>(
                () => new PartyTickStep(c.Invites, null, c.Presence, c.Broadcast, c.Health)).ParamName);
            Assert.AreEqual("presence", Assert.Throws<ArgumentNullException>(
                () => new PartyTickStep(c.Invites, c.Membership, null, c.Broadcast, c.Health)).ParamName);
            Assert.AreEqual("broadcast", Assert.Throws<ArgumentNullException>(
                () => new PartyTickStep(c.Invites, c.Membership, c.Presence, null, c.Health)).ParamName);
            Assert.AreEqual("health", Assert.Throws<ArgumentNullException>(
                () => new PartyTickStep(c.Invites, c.Membership, c.Presence, c.Broadcast, null)).ParamName);
        }

        // ---- AC-5: listeners composed ----

        [Test]
        public void test_party_composition_release_composite_runs_party_after_death_and_before_combat()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            IReadOnlyList<IZoneSlotReleaseListener> listeners = ReleaseListeners(rig);

            // Act
            int death = IndexOfSame(listeners, rig.Comp.DeathAndRespawn);
            int party = IndexOfType(listeners, typeof(PartySlotReleaseListener));
            int combat = IndexOfSame(listeners, rig.Comp.AutoAttackCombat);

            // Assert
            Assert.GreaterOrEqual(death, 0);
            Assert.AreEqual(death + 1, party, "party directly after Death and Respawn");
            Assert.AreEqual(party + 1, combat);
            Assert.AreEqual(listeners.Count - 1, combat, "Combat stays last");
        }

        [Test]
        public void test_party_composition_release_composite_without_death_and_respawn_still_has_party_before_combat()
        {
            // Arrange
            NavCompositionRig rig = NewRig(false);
            LogAssert.Expect(LogType.Warning, new Regex("Death and Respawn not built"));
            rig.Start();

            // Act
            IReadOnlyList<IZoneSlotReleaseListener> listeners = ReleaseListeners(rig);
            int party = IndexOfType(listeners, typeof(PartySlotReleaseListener));
            int combat = IndexOfSame(listeners, rig.Comp.AutoAttackCombat);

            // Assert
            Assert.AreEqual(5, listeners.Count);
            Assert.GreaterOrEqual(party, 0);
            Assert.AreEqual(party + 1, combat);
            Assert.AreEqual(listeners.Count - 1, combat, "Combat stays last");
        }

        [Test]
        public void test_party_composition_entry_composite_is_movement_then_skill_then_party()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();

            // Act
            object composite = FindInstance(rig.Comp.ZoneInstancing.EntryCompletion,
                nameof(CompositeZonePlayerEntryListener));
            Assert.IsNotNull(composite, "entry composite found");
            IReadOnlyList<IZonePlayerEntryListener> listeners = ((CompositeZonePlayerEntryListener)composite).Listeners;

            // Assert
            Assert.AreEqual(3, listeners.Count);
            Assert.AreSame(rig.Comp.Movement, listeners[0]);
            Assert.AreEqual("SkillSpawnEntryListener", listeners[1].GetType().Name);
            Assert.IsInstanceOf<PartyPlayerEntryListener>(listeners[2]);
        }

        // ---- AC-6: XP-ineligible notice ----

        private sealed class Sent
        {
            public readonly uint ClientId;
            public readonly byte[] Data;

            public Sent(uint clientId, byte[] data)
            {
                ClientId = clientId;
                Data = data;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Packets = new List<Sent>();

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                Packets.Add(new Sent(clientId, message.ToArray()));
            }
        }

        private sealed class SinkRig
        {
            public FakeSeam Seam;
            public ClientMessageOutbox Outbox;
            public OutboxPartyXpIneligibleNoticeSink Sink;
            public NavCompositionRig Rig;
            public ZoneSlotPool Pool;
            public PartyRegistry Registry;
        }

        // Counts warnings and errors logged while act runs (the composed zone logs its own startup lines, so
        // LogAssert.NoUnexpectedReceived cannot be used after StartedRig).
        private static int WarningsAndErrorsDuring(Action act)
        {
            int count = 0;
            Application.LogCallback callback = (condition, stackTrace, type) =>
            {
                if (type != LogType.Log)
                {
                    count++;
                }
            };
            Application.logMessageReceived += callback;
            try
            {
                act();
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }

            return count;
        }

        private SinkRig NewSinkRig()
        {
            NavCompositionRig rig = StartedRig();
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            PartyRegistry registry = rig.Comp.PartyRegistry;
            var seam = new FakeSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(CLIENT);
            Assert.IsTrue(pool.TryAllocatePlayer(pool.Zone, new CharacterID(CHARACTER),
                new AccountID(CHARACTER + ACCOUNT_OFFSET), CLIENT, out _, out EntityID entity));
            Assert.AreEqual(PartyRegistryResult.Ok, registry.RegisterPlayer(new CharacterID(CHARACTER), entity, out _));
            return new SinkRig
            {
                Seam = seam,
                Outbox = outbox,
                Sink = new OutboxPartyXpIneligibleNoticeSink(registry, pool, outbox),
                Rig = rig,
                Pool = pool,
                Registry = registry,
            };
        }

        [Test]
        public void test_party_notice_sink_known_character_gets_one_0xE178_with_the_party_id()
        {
            // Arrange
            SinkRig sink = NewSinkRig();

            // Act
            sink.Sink.Notify(new CharacterID(CHARACTER), new PartyID(PARTY_RAW));
            sink.Outbox.Flush(TICK);

            // Assert
            Assert.AreEqual(1, sink.Seam.Packets.Count);
            Sent packet = sink.Seam.Packets[0];
            Assert.AreEqual(CLIENT, packet.ClientId);
            Assert.IsTrue(MessageEnvelopeCodec.TryRead(packet.Data, out ServerMessageEnvelope envelope));
            Assert.AreEqual(0xE178, envelope.MessageTypeId);
            Assert.IsTrue(PartyXpIneligibleNotice.TryRead(packet.Data.AsSpan(ServerMessageEnvelope.WireSize),
                out PartyXpIneligibleNotice notice));
            Assert.AreEqual(PARTY_RAW, notice.PartyId);
        }

        [Test]
        public void test_party_notice_sink_unknown_character_enqueues_nothing_and_logs_nothing()
        {
            // Arrange
            SinkRig sink = NewSinkRig();

            // Act
            int logged = WarningsAndErrorsDuring(() => sink.Sink.Notify(new CharacterID(CHARACTER_UNKNOWN), new PartyID(PARTY_RAW)));
            sink.Outbox.Flush(TICK);

            // Assert
            Assert.AreEqual(0, logged, "no warning or error from Notify");
            Assert.AreEqual(0, sink.Seam.Packets.Count);
        }

        [Test]
        public void test_party_notice_sink_registered_character_without_a_pool_slot_enqueues_nothing_and_logs_nothing()
        {
            // Arrange: registered with the party registry, but the entity was never allocated in the slot pool
            SinkRig sink = NewSinkRig();
            Assert.AreEqual(PartyRegistryResult.Ok, sink.Registry.RegisterPlayer(new CharacterID(CHARACTER_UNKNOWN),
                new EntityID(UNPOOLED_ENTITY), out _));

            // Act
            int logged = WarningsAndErrorsDuring(() => sink.Sink.Notify(new CharacterID(CHARACTER_UNKNOWN), new PartyID(PARTY_RAW)));
            sink.Outbox.Flush(TICK);

            // Assert
            Assert.AreEqual(0, logged, "no warning or error from Notify");
            Assert.AreEqual(0, sink.Seam.Packets.Count);
        }

        [Test]
        public void test_party_notice_sink_client_missing_from_the_outbox_enqueues_nothing_and_logs_nothing()
        {
            // Arrange: the character resolves to a client, but that client was never added to this outbox
            SinkRig sink = NewSinkRig();
            var seam = new FakeSeam();
            var emptyOutbox = new ClientMessageOutbox(seam);
            var other = new OutboxPartyXpIneligibleNoticeSink(sink.Registry, sink.Pool, emptyOutbox);

            // Act
            int logged = WarningsAndErrorsDuring(() => other.Notify(new CharacterID(CHARACTER), new PartyID(PARTY_RAW)));
            emptyOutbox.Flush(TICK);

            // Assert
            Assert.AreEqual(0, logged, "no warning or error from Notify");
            Assert.AreEqual(0, seam.Packets.Count);
        }

        [Test]
        public void test_party_composition_leader_transfer_runs_through_the_notifier_without_throwing()
        {
            // Arrange: two players in the composition's pool and registry, in one party
            SinkRig sink = NewSinkRig();
            var second = new CharacterID(CHARACTER + 1u);
            Assert.IsTrue(sink.Pool.TryAllocatePlayer(sink.Pool.Zone, second, new AccountID(CHARACTER + 1u + ACCOUNT_OFFSET),
                CLIENT + 1u, out _, out EntityID secondEntity));
            Assert.AreEqual(PartyRegistryResult.Ok, sink.Registry.RegisterPlayer(second, secondEntity, out _));
            Assert.AreEqual(PartyRegistryResult.Ok, sink.Registry.TryCreateParty(new CharacterID(CHARACTER), out PartyID party));
            Assert.AreEqual(PartyRegistryResult.Ok, sink.Registry.TryAddMember(party, second));
            Assert.IsTrue(sink.Registry.TryGetParty(party, out PartyState state));
            int leaderBefore = state.LeaderIndex;

            // Act
            bool changed = false;
            Assert.DoesNotThrow(() => changed = sink.Registry.TransferLeadership(party));

            // Assert
            Assert.IsTrue(changed);
            Assert.AreNotEqual(leaderBefore, state.LeaderIndex);
        }

        [Test]
        public void test_party_composition_registry_leader_change_listener_is_the_xp_ineligible_notifier()
        {
            // Arrange / Act
            NavCompositionRig rig = StartedRig();
            object listener = rig.Comp.PartyRegistry.LeaderListener;

            // Assert
            Assert.IsInstanceOf<PartyXpIneligibleNotifier>(listener);
            Assert.AreSame(rig.Comp.PartyNotifier, listener);
        }

        // ---- AC-7: no party drop bonus (AC-PS-16) ----

        private static bool IsPartyBonusName(string name)
        {
            return name.IndexOf("PartyBonus", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("PartySize", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CollectOffenders(Type type, List<string> offenders)
        {
            const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (MemberInfo member in type.GetMembers(ALL))
            {
                if (IsPartyBonusName(member.Name))
                {
                    offenders.Add(type.Name + "." + member.Name);
                }

                if (member is MethodBase method)
                {
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        if (parameter.Name != null && IsPartyBonusName(parameter.Name))
                        {
                            offenders.Add(type.Name + "." + member.Name + "(" + parameter.Name + ")");
                        }
                    }
                }
            }
        }

        [Test]
        public void test_party_loot_drop_path_carries_no_party_size_or_bonus_member()
        {
            // Arrange: the loot table service and every drop type in its namespace; LootDropDistributor is the
            // documented exclusion (its party size only routes Rare items to auction).
            Type service = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type t in assembly.GetTypes())
                {
                    if (t.Name == "LootTableService" && t.Namespace != null
                        && t.Namespace.StartsWith("IronGrind", StringComparison.Ordinal))
                    {
                        service = t;
                    }
                }
            }

            Assert.IsNotNull(service, "LootTableService found");
            var offenders = new List<string>();

            // Act
            CollectOffenders(service, offenders);
            foreach (MethodInfo method in service.GetMethods(BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Type p = parameter.ParameterType;
                    if (!p.IsPrimitive && !p.IsEnum && p.Namespace != null
                        && p.Namespace.StartsWith("IronGrind", StringComparison.Ordinal))
                    {
                        CollectOffenders(p, offenders);
                    }
                }
            }

            int inspected = 0;
            foreach (Type t in service.Assembly.GetTypes())
            {
                if (t.Namespace == service.Namespace && t.Name != "LootDropDistributor"
                    && t.Name.IndexOf("Drop", StringComparison.Ordinal) >= 0)
                {
                    inspected++;
                    CollectOffenders(t, offenders);
                }
            }

            // Assert
            Assert.Greater(inspected, 0, "the scan inspected at least one drop type");
            Assert.IsEmpty(offenders, "party size / bonus members on the drop path: " + string.Join(", ", offenders));
        }
    }
}
