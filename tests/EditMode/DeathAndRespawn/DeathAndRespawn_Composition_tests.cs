using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using ZoneStats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// Death and Respawn Story 008: zone composition wiring (AC-1 to AC-6) over <see cref="NavCompositionRig"/> with
    /// <c>RespawnProviderOverride</c> set to a fixed town point (no definition slot, so no
    /// <c>TownRespawnPointNotAuthored</c> warning). A player is allocated in the pool, seeded into Movement and given
    /// Character Stats (this rig loads no character); a kill is <c>ApplyDamage</c> through Character Stats.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_Composition_Tests
    {
        private const int MAX_HP = 100;
        private const int MAX_MP = 50;
        private static readonly Vector3 TOWN_POINT = new Vector3(1f, 0f, 1f);

        private readonly List<NavCompositionRig> _rigs = new List<NavCompositionRig>();
        private readonly List<Object> _assets = new List<Object>();
        private uint _nextCharacter;

        // Real-admission fixtures (T1 to T4): a saved record per character, kept by an in-memory store.
        private const uint ACCOUNT_OFFSET = 1000u;
        private const uint CLIENT_ALIVE = 41u;
        private const uint CLIENT_DEAD = 42u;
        private const uint CHARACTER_ALIVE = 41u;
        private const uint CHARACTER_DEAD = 42u;
        private const byte CLASS_WARRIOR = 0;
        private const int MAX_WAIT_STEPS = 200;
        private const int WAIT_STEP_MS = 5;
        private const float RESPAWN_DELAY_SECONDS_MIN = 5.0f;
        private KeepingStore _store;

        /// <summary>
        /// Keeps every record it is given; an update succeeds only on the stored version (as the SQL predicate does).
        /// </summary>
        private sealed class KeepingStore : ICharacterRecordStore
        {
            private readonly Dictionary<CharacterID, CharacterRecord> _records =
                new Dictionary<CharacterID, CharacterRecord>();
            private readonly object _lock = new object();

            public void Put(CharacterRecord record)
            {
                lock (_lock)
                {
                    _records[record.CharacterID] = record.Clone();
                }
            }

            public CharacterRecord Get(CharacterID id)
            {
                lock (_lock)
                {
                    return _records.TryGetValue(id, out CharacterRecord r) ? r.Clone() : null;
                }
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct) => Task.FromResult(Get(id));

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => Task.FromResult(false);

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
            {
                lock (_lock)
                {
                    if (!_records.TryGetValue(record.CharacterID, out CharacterRecord stored)
                        || stored.SaveVersion != expectedSaveVersion)
                    {
                        return Task.FromResult(0);
                    }

                    CharacterRecord kept = record.Clone();
                    kept.SaveVersion = unchecked(expectedSaveVersion + 1u);
                    _records[record.CharacterID] = kept;
                }

                return Task.FromResult(1);
            }
        }

        private sealed class AcceptAllValidator : IZoneEntryTokenValidator
        {
            public bool TryValidate(in ZoneEntryRequest request, out AccountID account)
            {
                account = new AccountID(request.Character.RawValue + ACCOUNT_OFFSET);
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _nextCharacter = 100u;
            _store = null;
        }

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

        private NavCompositionRig NewRig(float? delaySeconds = null)
        {
            var rig = new NavCompositionRig();
            _rigs.Add(rig);
            rig.Comp.RespawnProviderOverride = new RespawnStubs.Points();
            if (delaySeconds.HasValue)
            {
                var asset = ScriptableObject.CreateInstance<DeathAndRespawnConfig>();
                _assets.Add(asset);
                asset.SetForTests(delaySeconds.Value, DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT);
                rig.Comp.DeathAndRespawnConfigOverride = asset;
            }

            return rig;
        }

        private NavCompositionRig StartedRig(float? delaySeconds = null)
        {
            NavCompositionRig rig = NewRig(delaySeconds);
            rig.Start();
            Assert.IsTrue(rig.Comp.IsTickRunning, "startup must reach the tick");
            return rig;
        }

        private EntityID AddPlayer(NavCompositionRig rig)
        {
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;
            uint raw = _nextCharacter++;
            Assert.IsTrue(wiring.Pool.TryAllocatePlayer(wiring.Pool.Zone, new CharacterID(raw),
                new AccountID(raw + ACCOUNT_OFFSET), raw, out var slot, out EntityID player));
            rig.Comp.Movement.SeedForTesting((int)slot, player, new Vector3(5f, 0f, 5f), 0);
            ZoneStats stats = rig.Comp.ZoneCharacterStats;
            stats.SetBaseStat(player, StatID.MaxHP, MAX_HP);
            stats.SetBaseStat(player, StatID.MaxMP, MAX_MP);
            stats.SetCurrentHP(player, MAX_HP);
            stats.SetCurrentMP(player, 10f);
            return player;
        }

        private static void Kill(NavCompositionRig rig, EntityID player)
        {
            rig.Comp.ZoneCharacterStats.ApplyDamage(player, MAX_HP * 10f);
        }

        private static DeathState StateOf(NavCompositionRig rig, EntityID player)
        {
            Assert.IsTrue(rig.Comp.DeathAndRespawn.TryGetDeathState(player, out DeathState state));
            return state;
        }

        private static object FieldNamed(object owner, string name)
        {
            Assert.IsNotNull(owner);
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                | BindingFlags.Public);
            Assert.IsNotNull(field, owner.GetType().Name + " has no field " + name);
            return field.GetValue(owner);
        }

        // ---- real admission over the rig (a loaded character: name, persistence session, record store) ----

        private static CharacterRecord MakeRecord(uint character)
        {
            CharacterRecord record = CharacterStubDefaults.CreateRecord(new AccountID(character + ACCOUNT_OFFSET),
                new CharacterID(character), CLASS_WARRIOR);
            record.CharacterName = "Hero" + character;
            return record;
        }

        private static ClassRegistry Registry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, 2, Array.Empty<uint>()));
            return registry;
        }

        private NavCompositionRig StartedAdmissionRig(params uint[] characters)
        {
            NavCompositionRig rig = NewRig(RESPAWN_DELAY_SECONDS_MIN);
            _store = new KeepingStore();
            foreach (uint character in characters)
            {
                _store.Put(MakeRecord(character));
            }

            rig.Comp.ClassRegistryOverride = Registry();
            rig.Comp.RecordStoreOverride = _store;
            rig.Comp.EntryTokenValidatorOverride = new AcceptAllValidator();
            rig.Start();
            Assert.IsTrue(rig.Comp.IsTickRunning, "startup must reach the tick");
            return rig;
        }

        private static void TickUntil(NavCompositionRig rig, Func<bool> done, string what)
        {
            for (int i = 0; i < MAX_WAIT_STEPS && !done(); i++)
            {
                rig.Net.FireTick();
                if (!done())
                {
                    Thread.Sleep(WAIT_STEP_MS);
                }
            }

            Assert.IsTrue(done(), what);
        }

        // The handshake for the character on the connection, ticks until the slot exists, then the entry ack.
        private static EntityID Admit(NavCompositionRig rig, uint client, uint character, bool firstEntry)
        {
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(client));
            rig.Net.ClientListener.AddClient(client);
            ZoneSlotPool pool = rig.Comp.ZoneInstancing.Pool;
            rig.Comp.ZoneInstancing.Admission.BeginEntry(new ZoneEntryRequest(client, new CharacterID(character),
                pool.Zone, new byte[SessionTokenStore.TOKEN_LENGTH_BYTES]));
            if (firstEntry)
            {
                // The EditMode scene has no baked NavMesh (first entry of a rig only).
                LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
            }

            EntityID entity = default;
            TickUntil(rig, () =>
            {
                foreach (EntityID p in pool.GetPlayerEntityIDsInZone(pool.Zone))
                {
                    if (pool.TryGetPlayer(p, out ZonePlayerSlot slot) && slot.ClientId == client)
                    {
                        entity = p;
                        return true;
                    }
                }

                return false;
            }, "the character was admitted");

            var body = new byte[ZoneEntryAck.BodySize];
            ZoneEntryAckCodec.WriteBody(body, new ZoneEntryAck(pool.Zone, 0u));
            rig.Comp.ZoneInstancing.Reclamation.OnConnectionMessage(client, ZoneEntryAck.MessageTypeId, default, body);
            rig.Net.FireTick();
            return entity;
        }

        // Sends the zone snapshot to the client, flushes it on a tick, and returns the player's entry and the
        // snapshot version (the tick it was built on).
        private static EntityState SnapshotEntryOf(NavCompositionRig rig, uint client, EntityID player,
            out uint snapshotVersion)
        {
            rig.Net.Seam.Sent.Clear();
            rig.Comp.ZoneInstancing.SnapshotBuilder.SendSnapshot(client);
            rig.Net.FireTick();

            const int ENVELOPE = ServerMessageEnvelope.WireSize;
            var stream = new List<byte>();
            snapshotVersion = 0u;
            foreach ((uint _, byte[] data, NetworkChannel _) in rig.Net.Seam.Sent)
            {
                if (data.Length <= ENVELOPE
                    || BinaryPrimitives.ReadUInt16LittleEndian(data) != ZoneStateSnapshotFragment.MessageTypeId)
                {
                    continue;
                }

                Assert.IsTrue(ZoneStateSnapshotFragmentCodec.TryReadBody(
                    new ReadOnlySpan<byte>(data, ENVELOPE, data.Length - ENVELOPE),
                    out ZoneStateSnapshotFragment header, out ReadOnlySpan<byte> payload));
                snapshotVersion = header.SnapshotVersion;
                stream.AddRange(payload.ToArray());
            }

            Assert.Greater(stream.Count, 0, "a snapshot fragment was sent");
            byte[] bytes = stream.ToArray();
            int offset = 0;
            while (offset < bytes.Length)
            {
                Assert.IsTrue(EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(bytes, offset, bytes.Length - offset),
                    out EntityState state, out int consumed), "entry at offset " + offset + " did not decode");
                if (state.EntityId == player)
                {
                    return state;
                }

                offset += consumed;
            }

            Assert.Fail("the player is not in the snapshot");
            return default;
        }

        private static object FieldOfType(object owner, Type fieldType)
        {
            Assert.IsNotNull(owner);
            for (Type t = owner.GetType(); t != null; t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic
                    | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType == fieldType)
                    {
                        return f.GetValue(owner);
                    }
                }
            }

            Assert.Fail(owner.GetType().Name + " has no field of type " + fieldType.Name);
            return null;
        }

        // ---- AC-1: config ----

        [Test]
        public void test_composition_death_null_override_uses_defaults_and_logs_no_config_warning()
        {
            // Arrange
            NavCompositionRig rig = NewRig();
            using (var capture = new NavProblemCapture())
            {
                // Act
                rig.Start();
                EntityID player = AddPlayer(rig);
                uint deathTick = rig.Comp.TickLoop.ServerTickNumber;
                Kill(rig, player);

                // Assert
                DeathState state = StateOf(rig, player);
                Assert.AreEqual(DeathStateKind.Dead, state.CurrentState);
                Assert.AreEqual((long)deathTick, state.DeathTick);
                Assert.AreEqual(DeathAndRespawnConfigValues.Defaults.RespawnDelayTicks,
                    (int)(state.RespawnTick - state.DeathTick));
                Assert.AreEqual(0, capture.Problems.Count, capture.Joined);
            }
        }

        [Test]
        public void test_composition_death_valid_override_sets_the_respawn_tick_to_death_plus_the_override_ticks()
        {
            // Arrange
            const float DELAY_SECONDS = 9.0f;
            NavCompositionRig rig = StartedRig(DELAY_SECONDS);
            EntityID player = AddPlayer(rig);

            // Act
            Kill(rig, player);

            // Assert
            DeathState state = StateOf(rig, player);
            Assert.AreEqual(DeathAndRespawnConfigValues.ToTicks(DELAY_SECONDS),
                (int)(state.RespawnTick - state.DeathTick));
        }

        [Test]
        public void test_composition_death_invalid_override_fails_s5_and_the_zone_never_reaches_active()
        {
            // Arrange
            NavCompositionRig rig = NewRig(4.9f);
            LogAssert.Expect(LogType.Error, new Regex("RESPAWN_DELAY_SECONDS"));
            LogAssert.Expect(LogType.Error, new Regex("startup failed at S5: ArgumentException"));

            // Act
            rig.Start();

            // Assert
            Assert.IsFalse(rig.Comp.IsTickRunning);
            Assert.IsNull(rig.Comp.DeathAndRespawn);
        }

        // ---- AC-2: built per zone ----

        [Test]
        public void test_composition_death_service_is_built_and_a_kill_through_character_stats_makes_the_player_dead()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            EntityID player = AddPlayer(rig);
            Assert.IsNotNull(rig.Comp.DeathAndRespawn);
            Assert.IsFalse(rig.Comp.DeathAndRespawn.IsDead(player));

            // Act
            Kill(rig, player);

            // Assert
            Assert.IsTrue(rig.Comp.DeathAndRespawn.IsDead(player));
            Assert.IsTrue(rig.Comp.DeathStateReader.IsBound);
            Assert.IsTrue(rig.Comp.DeathStateReader.IsDead(player), "the forwarder reaches the service");
        }

        [Test]
        public void test_composition_death_without_a_respawn_provider_builds_no_service_and_nobody_is_dead()
        {
            // Arrange: no definition slot and no RespawnProviderOverride
            var rig = new NavCompositionRig();
            _rigs.Add(rig);
            LogAssert.Expect(LogType.Warning,
                new Regex(@"Death and Respawn not built: no town respawn point provider"));

            // Act
            rig.Start();
            EntityID player = AddPlayer(rig);

            // Assert
            Assert.IsNull(rig.Comp.DeathAndRespawn);
            Assert.IsNull(rig.Comp.DeathAndRespawnStep);
            Assert.IsFalse(rig.Comp.DeathStateReader.IsBound);
            Assert.IsFalse(rig.Comp.DeathStateReader.IsDead(new EntityID(1u)));
            foreach (IZoneSimulationStep step in rig.Comp.SimulationSteps)
            {
                Assert.IsNotInstanceOf<DeathAndRespawnPhaseStep>(step);
            }

            object composite = FieldOfType(rig.Comp.ZoneInstancing.Pool, typeof(IZoneSlotReleaseListener));
            var listeners = (IZoneSlotReleaseListener[])FieldOfType(composite, typeof(IZoneSlotReleaseListener[]));
            Assert.AreEqual(4, listeners.Length);
            Assert.AreSame(rig.Comp.AutoAttackCombat, listeners[listeners.Length - 1], "Combat is last");
            Assert.DoesNotThrow(() => Kill(rig, player));
        }

        // ---- AC-3: tick placement ----

        [Test]
        public void test_composition_death_step_is_listed_once_between_status_effects_and_movement()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            IList<IZoneSimulationStep> steps = rig.Comp.SimulationSteps;

            // Act
            int count = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] is DeathAndRespawnPhaseStep)
                {
                    count++;
                }
            }

            // Assert
            int death = steps.IndexOf(rig.Comp.DeathAndRespawnStep);
            Assert.AreEqual(1, count);
            Assert.Less(steps.IndexOf(rig.Comp.StatusEffects), death);
            Assert.Less(death, steps.IndexOf(rig.Comp.Movement));
            Assert.AreSame(rig.Comp.DeathAndRespawn, rig.Comp.DeathAndRespawnStep.Service);
        }

        // ---- AC-4: readers wired (one assertion per reader) ----

        [Test]
        public void test_composition_death_snapshot_builder_reads_the_bound_forwarder()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();

            // Act
            object reader = FieldOfType(rig.Comp.ZoneInstancing.SnapshotBuilder, typeof(IPlayerDeathStateReader));

            // Assert
            Assert.AreSame(rig.Comp.DeathStateReader, reader);
        }

        [Test]
        public void test_composition_death_zone_close_steps_read_the_bound_forwarder()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();

            // Act
            object reader = FieldOfType(rig.Comp.ZoneInstancing.CloseSteps, typeof(IPlayerDeathStateReader));

            // Assert
            Assert.AreSame(rig.Comp.DeathStateReader, reader);
        }

        [Test]
        public void test_composition_death_aggro_candidate_source_does_not_offer_a_dead_player()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            EntityID player = AddPlayer(rig);
            var source = (IAggroCandidateSource)FieldOfType(rig.Comp.EnemyAI, typeof(IAggroCandidateSource));
            var offered = new List<(EntityID Id, Vector3 Position)>();
            source.CopyPlayers(offered);
            Assert.AreEqual(1, offered.Count, "alive player is offered");
            offered.Clear();

            // Act
            Kill(rig, player);
            source.CopyPlayers(offered);

            // Assert
            Assert.AreEqual(0, offered.Count, "dead player is not offered");
        }

        [Test]
        public void test_composition_death_entity_state_source_reports_the_respawn_tick_of_a_dead_player()
        {
            // Arrange: a really admitted character, so the composed source has a player view to read
            NavCompositionRig rig = StartedAdmissionRig(CHARACTER_ALIVE);
            EntityID player = Admit(rig, CLIENT_ALIVE, CHARACTER_ALIVE, true);
            Assert.IsTrue(rig.Comp.EntityStateSource.TryGetPlayerState(player, out ZonePlayerStateView alive));

            // Act
            rig.Comp.ZoneCharacterStats.ApplyDamage(player, 1000000f);
            Assert.IsTrue(rig.Comp.EntityStateSource.TryGetPlayerState(player, out ZonePlayerStateView dead));

            // Assert
            Assert.AreEqual(0u, alive.RespawnTick);
            Assert.AreEqual((uint)StateOf(rig, player).RespawnTick, dead.RespawnTick);
            Assert.Greater(dead.RespawnTick, 0u);
        }

        [Test]
        public void test_composition_death_snapshot_marks_a_killed_player_dead_with_the_remaining_respawn_ticks()
        {
            // Arrange
            NavCompositionRig rig = StartedAdmissionRig(CHARACTER_ALIVE);
            EntityID player = Admit(rig, CLIENT_ALIVE, CHARACTER_ALIVE, true);
            EntityState before = SnapshotEntryOf(rig, CLIENT_ALIVE, player, out _);

            // Act
            rig.Comp.ZoneCharacterStats.ApplyDamage(player, 1000000f);
            EntityState after = SnapshotEntryOf(rig, CLIENT_ALIVE, player, out uint version);

            // Assert
            Assert.IsFalse(before.IsInDeadState);
            Assert.AreEqual(0u, before.RespawnTicksRemaining);
            Assert.IsTrue(after.IsInDeadState);
            Assert.AreEqual((uint)(StateOf(rig, player).RespawnTick - version), after.RespawnTicksRemaining);
        }

        [Test]
        public void test_composition_death_zone_close_saves_a_dead_player_with_zero_hp_and_an_alive_one_with_real_hp()
        {
            // Arrange
            NavCompositionRig rig = StartedAdmissionRig(CHARACTER_ALIVE, CHARACTER_DEAD);
            EntityID alive = Admit(rig, CLIENT_ALIVE, CHARACTER_ALIVE, true);
            EntityID dead = Admit(rig, CLIENT_DEAD, CHARACTER_DEAD, false);
            Kill(rig, dead);
            float aliveHp = rig.Comp.ZoneCharacterStats.GetCurrentHP(alive);

            // Act
            rig.Teardown();

            // Assert
            Assert.AreEqual(0f, _store.Get(new CharacterID(CHARACTER_DEAD)).CurrentHP);
            Assert.Greater(aliveHp, 0f);
            Assert.AreEqual(aliveHp, _store.Get(new CharacterID(CHARACTER_ALIVE)).CurrentHP);
        }

        [Test]
        public void test_composition_death_ghost_dying_is_saved_with_the_effective_max_hp()
        {
            // Arrange
            NavCompositionRig rig = StartedAdmissionRig(CHARACTER_ALIVE);
            EntityID player = Admit(rig, CLIENT_ALIVE, CHARACTER_ALIVE, true);
            uint account = CHARACTER_ALIVE + ACCOUNT_OFFSET;
            ConnectionStateMachine sessions = rig.Comp.ZoneInstancing.Sessions;
            uint tick = rig.Comp.TickLoop.ServerTickNumber;
            if (!sessions.TryGetSessionState(account, out _))
            {
                sessions.EnterConnecting(account, tick);
                sessions.CompleteAuthSuccess(account, CHARACTER_ALIVE, tick);
            }

            sessions.EvaluateTimeouts(tick + 100000u, 10u, 10u);
            Assert.IsTrue(sessions.TryGetSessionState(account, out SessionState state));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, state);
            Assert.IsTrue(rig.Comp.ZoneInstancing.Pool.SetSessionState(
                player, ZoneSessionState.Disconnected_SessionActive));
            float maxHp = rig.Comp.ZoneCharacterStats.GetEffectiveStat(player, StatID.MaxHP);

            // Act
            Kill(rig, player);
            TickUntil(rig, () => !rig.Comp.ZoneInstancing.SessionEnds.IsEndInFlight(account),
                "the ghost-death save ended");

            // Assert
            Assert.AreEqual(maxHp, _store.Get(new CharacterID(CHARACTER_ALIVE)).CurrentHP);
        }

        [Test]
        public void test_composition_death_release_listener_runs_the_service_before_auto_attack_combat()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            object composite = FieldOfType(rig.Comp.ZoneInstancing.Pool, typeof(IZoneSlotReleaseListener));
            var listeners = (IZoneSlotReleaseListener[])FieldOfType(composite, typeof(IZoneSlotReleaseListener[]));

            // Act
            int death = Array.IndexOf(listeners, rig.Comp.DeathAndRespawn);
            int combat = Array.IndexOf(listeners, rig.Comp.AutoAttackCombat);

            // Assert
            Assert.GreaterOrEqual(death, 0, "the service is in the composite");
            Assert.Less(death, combat);
            Assert.AreEqual(listeners.Length - 1, combat, "Combat stays last");
        }

        // ---- AC-4b: ghost death wiring ----

        [Test]
        public void test_composition_death_session_end_coordinator_gets_the_hp_source_and_max_hp_reader()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            SessionEndCoordinator ends = rig.Comp.ZoneInstancing.SessionEnds;

            // Act
            object hp = FieldNamed(ends, "_preDisconnectHp");
            var maxHp = (Func<EntityID, float>)FieldNamed(ends, "_effectiveMaxHp");
            EntityID player = AddPlayer(rig);

            // Assert
            Assert.IsInstanceOf<NoOpPreDisconnectHpSource>(hp);
            Assert.AreEqual((float)MAX_HP, maxHp(player));
            Assert.IsNull(FieldNamed(ends, "_broadcastGhostExpired"), "broadcastGhostExpired stays null");
            Assert.IsNull(FieldNamed(ends, "_sendZoneSessionEnded"), "sendZoneSessionEnded stays null");
        }

        // ---- AC-5: lifecycle ----

        [Test]
        public void test_composition_death_teardown_disposes_the_service_so_a_later_death_changes_nothing()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            EntityID player = AddPlayer(rig);
            DeathAndRespawnService service = rig.Comp.DeathAndRespawn;
            // The zone close saves the test player, who has no persisted record in this rig.
            LogAssert.Expect(LogType.Error, new Regex(@"zone close: SaveSession for .* \(CharacterNotFound\)"));

            // Act
            rig.Teardown();
            Assert.DoesNotThrow(() => rig.Comp.ZoneCharacterStats.ApplyDamage(player, MAX_HP * 10f));

            // Assert
            Assert.IsFalse(service.IsDead(player));
        }

        [Test]
        public void test_composition_death_slot_released_while_dead_is_alive_for_the_next_player()
        {
            // Arrange
            NavCompositionRig rig = StartedRig();
            EntityID first = AddPlayer(rig);
            Kill(rig, first);
            Assert.IsTrue(rig.Comp.DeathAndRespawn.IsDead(first));

            // Act
            Assert.IsTrue(rig.Comp.ZoneInstancing.Pool.ReleasePlayer(first));
            EntityID second = AddPlayer(rig);

            // Assert
            Assert.IsFalse(rig.Comp.DeathAndRespawn.IsDead(second));
            Assert.AreEqual(DeathStateKind.Alive, StateOf(rig, second).CurrentState);
        }

        // ---- AC-6: death, respawn, readers ----

        [Test]
        public void test_composition_death_player_respawns_at_the_town_point_with_full_vitals_on_the_exact_tick()
        {
            // Arrange
            NavCompositionRig rig = StartedRig(RESPAWN_DELAY_SECONDS_MIN);
            EntityID player = AddPlayer(rig);
            uint deathTick = rig.Comp.TickLoop.ServerTickNumber;
            // This rig has no NavMesh: once the respawned player is no longer prohibited, Movement logs this once.
            LogAssert.Expect(LogType.Error, new Regex(@"\[Movement\] NavMesh unavailable"));
            Kill(rig, player);
            DeathState dead = StateOf(rig, player);
            int delay = DeathAndRespawnConfigValues.ToTicks(RESPAWN_DELAY_SECONDS_MIN);
            Assert.AreEqual(DeathStateKind.Dead, dead.CurrentState);
            Assert.AreEqual((long)deathTick, dead.DeathTick);
            Assert.AreEqual((long)deathTick + delay, dead.RespawnTick);

            // Act
            for (int i = 0; i < delay - 1; i++)
            {
                rig.Net.FireTick();
            }

            bool deadOneTickEarly = rig.Comp.DeathAndRespawn.IsDead(player);
            rig.Net.FireTick();

            // Assert
            Assert.IsTrue(deadOneTickEarly, "still dead one tick before the respawn tick");
            Assert.IsFalse(rig.Comp.DeathAndRespawn.IsDead(player), "Alive on the respawn tick");
            Assert.AreEqual(dead.RespawnTick, (long)rig.Comp.TickLoop.ServerTickNumber);
            Assert.IsTrue(rig.Comp.Movement.TryGetPosition(player, out Vector3 position));
            Assert.AreEqual(TOWN_POINT, position);
            ZoneStats stats = rig.Comp.ZoneCharacterStats;
            Assert.AreEqual((float)stats.GetEffectiveStat(player, StatID.MaxHP), stats.GetCurrentHP(player));
            Assert.AreEqual((float)stats.GetEffectiveStat(player, StatID.MaxMP), stats.GetCurrentMP(player));
        }
    }
}
