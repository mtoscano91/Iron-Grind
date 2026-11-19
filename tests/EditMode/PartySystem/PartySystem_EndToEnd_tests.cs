using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.PartySystem;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.SkillSystem;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using EnemyAICompositionFixtures = IronGrind.Tests.EditMode.EnemyAI.EnemyAICompositionFixtures;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// Party System Story 012 (production/epics/party-system/story-012-end-to-end-party-kill.md): two players form a
    /// party through the inbound dispatcher, share a skill kill's XP (F-PS-1, N = 2), then the party disbands on
    /// leave and the next kill pays the killer in full. Runs over the real started <see cref="ZoneServerComposition"/>
    /// (<see cref="WiRig"/>); EditMode, no I/O. The kill is a skill cast with a fixed killing damage calculator because
    /// the composition's random provider is entropy-seeded (an auto-attack kill would not be deterministic).
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_EndToEnd_Tests
    {
        private const uint CLIENT_A = 11u;
        private const uint CLIENT_B = 12u;
        private const uint CLIENT_C = 13u;
        private const uint ACCOUNT_OFFSET = 1000u;
        private const byte CLASS_WARRIOR = 0;
        private const uint SKILL_HIT = 202u;
        private const int COOLDOWN_TICKS = 5;
        private const int COOLDOWN_WAIT_TICKS = COOLDOWN_TICKS + 2;
        private const float RANGE = 10f;
        private const float MOB_DISTANCE = 1f;
        private const float CENTIMETERS_PER_METER = 100f;
        private const int STATE_VIEW_HP = 100;
        private const int PERF_TICKS = 200;
        private const double P95_FRACTION = 0.95;
        private const double MILLISECONDS_PER_SECOND = 1000.0;
        private const string EXPECTED_NAVMESH_ERROR = "NavMesh unavailable";
        private const int EXPECTED_LEVEL = 1;

        private readonly List<WiRig> _rigs = new List<WiRig>();
        private readonly Dictionary<uint, uint> _sequence = new Dictionary<uint, uint>();
        private SkillDatabase _database;

        // ---- Fixtures ----

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return Vector3.zero; }
        }

        /// <summary>Every hit is a kill for MOB_HP damage (an applied kill goes through the XP award path).</summary>
        private sealed class KillingDamage : ISkillDamageCalculator
        {
            public int Calls;

            public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
            {
                Calls++;
                int damage = EnemyAICompositionFixtures.MOB_HP;
                return new DamageResult(damage, 0, damage, false, true, context, false);
            }
        }

        /// <summary>
        /// Players alive; every mob alive at MOB_DISTANCE (so in skill range) unless the test has marked it dead (death
        /// is not hidden). With <see cref="KillingDamage"/> this fakes the kill itself: the test covers the party XP
        /// wiring end to end, not mob HP depletion or death detection (Combat / Enemy AI tests own those).
        /// </summary>
        private sealed class DeathAwareZoneState : IZoneEntityStateSource
        {
            public readonly HashSet<EntityID> DeadMobs = new HashSet<EntityID>();

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                view = new ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0, STATE_VIEW_HP, STATE_VIEW_HP, STATE_VIEW_HP, STATE_VIEW_HP, 1,
                    false, 0, 0, "p", 0u);
                return true;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = new ZoneMobStateView((short)Mathf.RoundToInt(MOB_DISTANCE * CENTIMETERS_PER_METER), 0, 0, 0, 0, 0, 0, 0,
                    STATE_VIEW_HP, STATE_VIEW_HP, false, 0);
                return !DeadMobs.Contains(id);
            }
        }

        /// <summary>Counts Warning / Error / Assert / Exception logs while alive (not LogAssert.NoUnexpectedReceived).</summary>
        private sealed class ProblemCounter : IDisposable
        {
            public int Count;

            public ProblemCounter() { Application.logMessageReceived += OnLog; }

            public void Dispose() { Application.logMessageReceived -= OnLog; }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Log)
                {
                    Count++;
                }
            }
        }

        private sealed class World
        {
            public WiRig Rig;
            public DeathAwareZoneState State;
            public KillingDamage Damage;
            public EntityID A;
            public EntityID B;
            public EntityID C;
        }

        // Records the Stopwatch timestamp at the point it is inserted (before) or the elapsed time since (after).
        private sealed class TimingProbe : IZoneSimulationStep
        {
            private readonly TimingProbe _start;
            public readonly List<long> Samples = new List<long>();
            public long Stamp;

            public TimingProbe(TimingProbe start) { _start = start; }

            public void Run(uint currentTick, float deltaTime)
            {
                if (_start == null)
                {
                    Stamp = Stopwatch.GetTimestamp();
                    return;
                }

                Samples.Add(Stopwatch.GetTimestamp() - _start.Stamp);
            }
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                foreach (WiRig rig in _rigs)
                {
                    DisposeSkillTeardown(rig);
                }
            }
            finally
            {
                _rigs.Clear();
                _sequence.Clear();
                if (_database != null)
                {
                    UnityEngine.Object.DestroyImmediate(_database);
                    _database = null;
                }
            }
        }

        // Same approach as SkillSystem_Composition_Tests: the full RunTeardown logs a SaveSession error with players admitted.
        // A null guard, not an Assert: an assert in TearDown would hide the test's own failure.
        private static void DisposeSkillTeardown(WiRig rig)
        {
            FieldInfo field = typeof(ZoneServerComposition).GetField("_disposables", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || !(field.GetValue(rig.Comp) is List<IDisposable> disposables))
            {
                Debug.LogWarning("[PartySystem012] ZoneServerComposition._disposables not found; Skill teardown skipped");
                return;
            }

            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                if (disposables[i] is SkillSystemTeardown teardown)
                {
                    teardown.Dispose();
                }
            }
        }

        // ---- Rig ----

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

        private EntityID Admit(WiRig rig, uint client)
        {
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            Assert.IsTrue(pool.TryAllocatePlayer(pool.Zone, new CharacterID(client), new AccountID(client + ACCOUNT_OFFSET), client,
                out byte slot, out EntityID id));
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(client));
            rig.Net.ClientListener.AddClient(client);
            rig.Comp.GuardChain.RegisterEntityOwnership(client, id);
            rig.Comp.GuardChain.MarkSessionReady(client);
            rig.Comp.ZoneInstancing.Directory.Add(client, new CharacterID(client));
            CharacterRecord record = CharacterStubDefaults.CreateRecord(new AccountID(client + ACCOUNT_OFFSET), new CharacterID(client), CLASS_WARRIOR);
            Assert.AreEqual(CharacterLoadCode.Success, rig.Comp.PersistenceService.ApplyLoadedRecord(id, record));
            rig.Comp.ZoneInstancing.EntryCompletion.OnPlayerAdmitted(client, slot, id);
            return id;
        }

        private EntityID AddMob(WiRig rig)
        {
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            EntityID mob = rig.Comp.ZoneInstancing.MobRoster.TryAddMob(pool.Zone, EnemyAICompositionFixtures.MobType, Vector3.zero);
            Assert.AreNotEqual(EntityID.Invalid, mob);
            rig.Comp.MobEvents.RaiseMobSpawned(new IronGrind.Events.MobSpawnedArgs(rig.Comp.ZoneId, mob, EnemyAICompositionFixtures.MobType, Vector3.zero));
            return mob;
        }

        // Starts the composition with the skill roster, admits A, B and the bystander C (Level 1) and runs the warm-up tick.
        private World StartWorld()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 0, new[] { SKILL_HIT }));
            _database = ScriptableObject.CreateInstance<SkillDatabase>();
            _database.SetForTesting(new[] { HitSkill() });

            var world = new World { Damage = new KillingDamage(), State = new DeathAwareZoneState(), Rig = new WiRig() };
            WiRig rig = world.Rig;
            _rigs.Add(rig);
            rig.Comp.ClassRegistryOverride = registry;
            rig.Comp.SkillDatabaseOverride = _database;
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.SkillDamageCalculatorOverride = world.Damage;
            rig.Comp.EntityStateSourceOverride = world.State;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            world.A = Admit(rig, CLIENT_A);
            world.B = Admit(rig, CLIENT_B);
            world.C = Admit(rig, CLIENT_C);

            LogAssert.Expect(LogType.Error, new Regex(EXPECTED_NAVMESH_ERROR));
            rig.Net.FireTick();
            rig.Net.Seam.Sent.Clear();
            return world;
        }

        // ---- Inbound ----

        private void Send(uint client, EntityID sender, ushort messageType, byte[] body, WiRig rig)
        {
            uint tick = rig.Comp.TickLoop.ServerTickNumber;
            _sequence.TryGetValue(client, out uint last);
            uint sequence = last + 1u;
            _sequence[client] = sequence;
            var message = new byte[ClientEntityMessageEnvelope.WireSize + body.Length];
            var envelope = new ClientEntityMessageEnvelope(messageType, sequence, tick, sender.RawValue);
            MessageEnvelopeCodec.Write(message, in envelope);
            body.CopyTo(message, ClientEntityMessageEnvelope.WireSize);
            Assert.IsTrue(rig.Net.Intake.TryAccept(client, message), "request accepted by the intake");
        }

        private void SendInvite(World w, uint client, EntityID sender, uint target)
        {
            var body = new byte[PartyInviteRequest.BodySize];
            Assert.IsTrue(new PartyInviteRequest(target).Write(body));
            Send(client, sender, PartyInviteRequest.MessageTypeId, body, w.Rig);
        }

        private void SendResponse(World w, uint client, EntityID sender, uint inviter, bool accepted)
        {
            var body = new byte[PartyInviteResponse.BodySize];
            Assert.IsTrue(new PartyInviteResponse(inviter, accepted).Write(body));
            Send(client, sender, PartyInviteResponse.MessageTypeId, body, w.Rig);
        }

        private void SendLeave(World w, uint client, EntityID sender)
        {
            var body = new byte[PartyLeaveRequest.BodySize];
            Assert.IsTrue(new PartyLeaveRequest().Write(body));
            Send(client, sender, PartyLeaveRequest.MessageTypeId, body, w.Rig);
        }

        private void SendCast(World w, uint client, EntityID caster, EntityID target)
        {
            uint tick = w.Rig.Comp.TickLoop.ServerTickNumber;
            var body = new byte[SkillCastRequest.BodySize];
            SkillCastRequestCodec.WriteBody(body, new SkillCastRequest(caster.RawValue, SKILL_HIT, target.RawValue, tick));
            Send(client, caster, SkillCastRequest.MessageTypeId, body, w.Rig);
        }

        // ---- Outbound ----

        // Every message sent to the client since the last Sent.Clear(): top-level bodies and R-U batch sub-messages, in order.
        private static List<KeyValuePair<ushort, byte[]>> MessagesTo(WiRig rig, uint client)
        {
            var result = new List<KeyValuePair<ushort, byte[]>>();
            foreach ((uint to, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (to != client || !MessageEnvelopeCodec.TryRead(data, out ServerMessageEnvelope envelope))
                {
                    continue;
                }

                if (envelope.MessageTypeId != RUBatchWriter.TICK_BATCH_RU)
                {
                    result.Add(new KeyValuePair<ushort, byte[]>(envelope.MessageTypeId,
                        data.AsSpan(ServerMessageEnvelope.WireSize).ToArray()));
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

        private static List<byte[]> BodiesOf(WiRig rig, uint client, ushort type)
        {
            var bodies = new List<byte[]>();
            foreach (KeyValuePair<ushort, byte[]> m in MessagesTo(rig, client))
            {
                if (m.Key == type)
                {
                    bodies.Add(m.Value);
                }
            }

            return bodies;
        }

        private static List<PartyStateUpdate> StatesTo(WiRig rig, uint client)
        {
            var states = new List<PartyStateUpdate>();
            foreach (byte[] body in BodiesOf(rig, client, PartyStateUpdate.MessageTypeId))
            {
                Assert.IsTrue(PartyStateUpdate.TryRead(body, out PartyStateUpdate update, out _), "PartyStateUpdate decodes");
                states.Add(update);
            }

            return states;
        }

        // ---- Scenario steps ----

        private static int Experience(World w, EntityID e)
        {
            return w.Rig.Comp.ZoneCharacterStats.GetBaseStat(e, StatID.Experience);
        }

        private static int LevelOf(World w, EntityID e)
        {
            return w.Rig.Comp.ZoneCharacterStats.GetBaseStat(e, StatID.Level);
        }

        private static int KillXpBase(World w, EntityID mob)
        {
            Assert.IsTrue(w.Rig.Comp.MobStore.TryGet(mob, out MobRuntimeEntry entry), "mob has a runtime store entry");
            return entry.Variant.EffectiveKillXP;
        }

        // The bystander C is never in the party: before every clear of the sent list, C must have had no party message.
        private static void ClearSent(World w)
        {
            foreach (KeyValuePair<ushort, byte[]> m in MessagesTo(w.Rig, CLIENT_C))
            {
                Assert.IsFalse(m.Key >= PartyStateUpdate.MessageTypeId && m.Key <= PartyInviteResult.MessageTypeId,
                    "bystander C got party message 0x" + m.Key.ToString("X4"));
            }

            w.Rig.Net.Seam.Sent.Clear();
        }

        // A invites B: B gets the invite naming A, A gets Sent; nobody has a non-solo update yet. AC-1, first half.
        private void InviteAndAssertDelivered(World w)
        {
            WiRig rig = w.Rig;
            SendInvite(w, CLIENT_A, w.A, CLIENT_B);
            rig.Net.FireTick();

            List<byte[]> received = BodiesOf(rig, CLIENT_B, PartyInviteReceived.MessageTypeId);
            Assert.AreEqual(1, received.Count, "B gets exactly one PartyInviteReceived");
            Assert.IsTrue(PartyInviteReceived.TryRead(received[0], out PartyInviteReceived invite));
            Assert.AreEqual(CLIENT_A, invite.InviterCharacterId);
            Assert.AreEqual(0, BodiesOf(rig, CLIENT_A, PartyInviteReceived.MessageTypeId).Count, "A gets no invite");

            // A successful invite answers the inviter with PartyInviteResult(Sent, target) (Story 007); B gets none.
            List<byte[]> results = BodiesOf(rig, CLIENT_A, PartyInviteResult.MessageTypeId);
            Assert.AreEqual(1, results.Count, "A gets exactly one PartyInviteResult");
            Assert.IsTrue(PartyInviteResult.TryRead(results[0], out PartyInviteResult result));
            Assert.AreEqual(PartyInviteResultCode.Sent, result.ResultCode, "the invite was delivered, not rejected");
            Assert.AreEqual(CLIENT_B, result.TargetCharacterId);
            Assert.AreEqual(0, BodiesOf(rig, CLIENT_B, PartyInviteResult.MessageTypeId).Count, "B gets no PartyInviteResult");

            foreach (uint client in new[] { CLIENT_A, CLIENT_B })
            {
                foreach (PartyStateUpdate update in StatesTo(rig, client))
                {
                    Assert.IsTrue(update.IsSoloParty, "no non-solo update before the response");
                }
            }

            ClearSent(w);
        }

        // B accepts: one PartyStateUpdate each, [A, B], leader A, same id. Returns the party id. AC-1, second half.
        private uint AcceptAndAssertFormed(World w)
        {
            WiRig rig = w.Rig;
            SendResponse(w, CLIENT_B, w.B, CLIENT_A, true);
            rig.Net.FireTick();

            List<PartyStateUpdate> forA = StatesTo(rig, CLIENT_A);
            List<PartyStateUpdate> forB = StatesTo(rig, CLIENT_B);
            Assert.AreEqual(1, forA.Count, "A gets exactly one PartyStateUpdate");
            Assert.AreEqual(1, forB.Count, "B gets exactly one PartyStateUpdate");
            foreach (PartyStateUpdate update in new[] { forA[0], forB[0] })
            {
                Assert.IsFalse(update.IsSoloParty);
                Assert.AreEqual(2, update.MemberCount);
                Assert.AreEqual(CLIENT_A, update.GetMember(0).CharacterId, "join order: A first");
                Assert.AreEqual(CLIENT_B, update.GetMember(1).CharacterId, "join order: B second");
                Assert.AreEqual(0, update.LeaderIndex, "A leads");
            }

            Assert.AreEqual(forA[0].PartyId, forB[0].PartyId, "same PartyID");
            Assert.AreNotEqual(0u, forA[0].PartyId);
            ClearSent(w);
            return forA[0].PartyId;
        }

        private uint FormParty(World w)
        {
            InviteAndAssertDelivered(w);
            return AcceptAndAssertFormed(w);
        }

        private void KillWithCast(World w, EntityID mob)
        {
            ClearSent(w);
            SendCast(w, CLIENT_A, w.A, mob);
            w.Rig.Net.FireTick();
            w.State.DeadMobs.Add(mob);
        }

        private static void AssertLevelOne(World w)
        {
            Assert.AreEqual(EXPECTED_LEVEL, LevelOf(w, w.A), "A did not level");
            Assert.AreEqual(EXPECTED_LEVEL, LevelOf(w, w.B), "B did not level");
        }

        // A kills the mob; A and B each gain the F-PS-1 share, C nothing. AC-2.
        private void KillAndAssertShared(World w, EntityID mob, int shared)
        {
            int aBefore = Experience(w, w.A);
            int bBefore = Experience(w, w.B);
            int cBefore = Experience(w, w.C);
            KillWithCast(w, mob);
            Assert.AreEqual(1, w.Damage.Calls, "the skill hit the mob once");
            Assert.AreEqual(aBefore + shared, Experience(w, w.A), "A gets the shared amount");
            Assert.AreEqual(bBefore + shared, Experience(w, w.B), "B gets the shared amount");
            Assert.AreEqual(cBefore, Experience(w, w.C), "bystander C gets nothing");
            AssertLevelOne(w);
        }

        // B leaves: the party auto-disbands; both get new distinct solo ids; only A gets PartyDisbanded. AC-3, first half.
        private void LeaveAndAssertDisbanded(World w, uint party)
        {
            ClearSent(w);
            SendLeave(w, CLIENT_B, w.B);
            w.Rig.Net.FireTick();

            List<PartyStateUpdate> forB = StatesTo(w.Rig, CLIENT_B);
            List<PartyStateUpdate> forA = StatesTo(w.Rig, CLIENT_A);
            Assert.AreEqual(1, forB.Count, "B gets one solo state");
            Assert.IsTrue(forB[0].IsSoloParty);
            Assert.AreEqual(1, forB[0].MemberCount);
            Assert.AreEqual(1, forA.Count, "A gets one solo state");
            Assert.IsTrue(forA[0].IsSoloParty);
            Assert.AreEqual(1, forA[0].MemberCount);

            List<byte[]> disbanded = BodiesOf(w.Rig, CLIENT_A, PartyDisbanded.MessageTypeId);
            Assert.AreEqual(1, disbanded.Count, "A gets PartyDisbanded");
            Assert.IsTrue(PartyDisbanded.TryRead(disbanded[0], out PartyDisbanded message));
            Assert.AreEqual(party, message.PartyId, "PartyDisbanded names the old party");
            Assert.AreEqual(0, BodiesOf(w.Rig, CLIENT_B, PartyDisbanded.MessageTypeId).Count, "the leaver gets no PartyDisbanded");

            Assert.AreNotEqual(0u, forA[0].PartyId);
            Assert.AreNotEqual(0u, forB[0].PartyId);
            Assert.AreNotEqual(forA[0].PartyId, forB[0].PartyId, "solo ids differ");
            Assert.AreNotEqual(party, forA[0].PartyId, "A's id is new");
            Assert.AreNotEqual(party, forB[0].PartyId, "B's id is new");
        }

        // After the skill cooldown, A kills again: full XP_base to A, nothing to B or C. AC-3, second half.
        private void SecondKillAndAssertFull(World w, EntityID mob, int xpBase)
        {
            for (int i = 0; i < COOLDOWN_WAIT_TICKS; i++)
            {
                w.Rig.Net.FireTick();
            }

            int aBefore = Experience(w, w.A);
            int bBefore = Experience(w, w.B);
            int cBefore = Experience(w, w.C);
            KillWithCast(w, mob);
            Assert.AreEqual(2, w.Damage.Calls, "the second cast hit the second mob");
            Assert.AreEqual(aBefore + xpBase, Experience(w, w.A), "A gets the full XP_base");
            Assert.AreEqual(bBefore, Experience(w, w.B), "B gets nothing");
            Assert.AreEqual(cBefore, Experience(w, w.C), "bystander C gets nothing");
            AssertLevelOne(w);
            ClearSent(w);
        }

        // ---- Tests ----

        [Test]
        public void test_party_system_end_to_end_invite_accept_kill_leave_and_second_kill_share_xp_as_designed()
        {
            // Arrange
            World w = StartWorld();
            EntityID mob = AddMob(w.Rig);
            EntityID secondMob = AddMob(w.Rig);
            int xpBase = KillXpBase(w, mob);
            Assert.Greater(xpBase, 0, "fixture mob is worth XP");
            Assert.Less(xpBase, w.Rig.Comp.ZoneLeveling.GetExperienceThreshold(w.A), "XP_base below A's L1 threshold");
            Assert.Less(xpBase, w.Rig.Comp.ZoneLeveling.GetExperienceThreshold(w.B), "XP_base below B's L1 threshold");
            float rate = PartySystemConfigValues.Defaults.XpPartyDeductionRate;
            int shared = Mathf.RoundToInt((1f - rate) * xpBase);

            using (var problems = new ProblemCounter())
            {
                // Act + Assert: AC-1 form, AC-2 share, AC-3 leave and second kill
                uint party = FormParty(w);
                KillAndAssertShared(w, mob, shared);
                LeaveAndAssertDisbanded(w, party);
                SecondKillAndAssertFull(w, secondMob, xpBase);

                // AC-4
                Assert.AreEqual(0, problems.Count, "no warning, error or exception from the first invite to the end");
            }
        }

        [Test]
        public void test_party_system_party_step_cost_is_logged_over_200_ticks_with_the_party_formed()
        {
            // Arrange
            World w = StartWorld();
            uint party = FormParty(w);
            IList<IZoneSimulationStep> steps = w.Rig.Comp.SimulationSteps;
            int index = steps.IndexOf(w.Rig.Comp.PartyTickStep);
            Assert.GreaterOrEqual(index, 0, "party step is listed");
            var before = new TimingProbe(null);
            var after = new TimingProbe(before);
            steps.Insert(index, before);
            steps.Insert(index + 2, after);
            Assert.AreSame(w.Rig.Comp.PartyTickStep, steps[index + 1], "probes sit directly around the party step");

            // Act
            for (int i = 0; i < PERF_TICKS; i++)
            {
                w.Rig.Net.FireTick();
            }

            // Assert: the measured work was the formed-party case, then log (no timing assertion, story AC-4).
            Assert.AreEqual(PERF_TICKS, after.Samples.Count, "one sample per tick");
            PartyID current = w.Rig.Comp.PartyRegistry.GetPartyID(new CharacterID(CLIENT_A));
            Assert.AreEqual(party, current.RawValue, "A is still in the party after the run");
            Assert.IsTrue(w.Rig.Comp.PartyRegistry.TryGetParty(current, out PartyState state));
            Assert.AreEqual(2, state.MemberCount, "the party still has both members");
            LogCost(after.Samples);
        }

        private static void LogCost(List<long> samples)
        {
            var millis = new List<double>(samples.Count);
            foreach (long ticks in samples)
            {
                millis.Add(ticks * MILLISECONDS_PER_SECOND / Stopwatch.Frequency);
            }

            millis.Sort();
            double max = millis[millis.Count - 1];
            double p95 = millis[(int)Math.Ceiling(P95_FRACTION * millis.Count) - 1];
            Debug.Log("[PartySystem012] party step max=" + max.ToString("F4") + " p95=" + p95.ToString("F4")
                + " (reference 0.1 ms)");
        }
    }
}
