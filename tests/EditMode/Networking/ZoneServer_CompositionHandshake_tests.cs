using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Events;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EnemyAICompositionFixtures = IronGrind.Tests.EditMode.EnemyAI.EnemyAICompositionFixtures;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Gateway Story 012, AC-6 / AC-AUTH-21 (e), composition level: a gateway-admitted character's handshake loads the
    /// record through the real <see cref="CharacterPersistenceService"/>, which then serves the registered name, and the
    /// session ends Connected with that character.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneServer_CompositionHandshake_Tests
    {
        private const uint CLIENT = 3101u;
        private const uint CHARACTER = 601u;
        private const uint ACCOUNT = 1601u;
        private const string NAME = "Brakka";
        private const byte CLASS_WARRIOR = 0;
        private const int FREE_POINTS_PER_LEVEL = 2;
        private const int MAX_WAIT_STEPS = 200;
        private const int WAIT_STEP_MS = 5;

        private sealed class NamedRecordStore : ICharacterRecordStore
        {
            private readonly CharacterRecord _record;

            public NamedRecordStore(CharacterRecord record)
            {
                _record = record;
            }

            public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct) =>
                Task.FromResult(id == _record.CharacterID ? _record.Clone() : null);

            public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct) => Task.FromResult(false);

            public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct) => Task.FromResult(0);
        }

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 GetTownRespawnPoint(ZoneID zone) { return Vector3.zero; }
        }

        private static bool IsConnected(ZoneInstancingWiring wiring) =>
            wiring.Sessions.TryGetSessionState(ACCOUNT, out SessionState state) && state == SessionState.Connected;

        [Test]
        public void test_composition_handshake_for_an_admitted_character_loads_its_registered_name_and_connects_the_session()
        {
            // Arrange
            CharacterRecord record = CharacterStubDefaults.CreateRecord(new AccountID(ACCOUNT), new CharacterID(CHARACTER), CLASS_WARRIOR);
            record.CharacterName = NAME;
            var registry = new ClassRegistry();
            registry.RegisterClass(CLASS_WARRIOR, new ClassDefinition(0, 0, 0, 0, FREE_POINTS_PER_LEVEL, Array.Empty<uint>()));
            var rig = new WiRig();
            rig.Comp.ClassRegistryOverride = registry;
            rig.Comp.RespawnProviderOverride = new FakeRespawn();
            rig.Comp.RecordStoreOverride = new NamedRecordStore(record);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;

            var token = new byte[SessionHandshake.TOKEN_LENGTH];
            for (int i = 0; i < token.Length; i++)
            {
                token[i] = (byte)(i + 1);
            }

            wiring.GatewayAdmit.Handle(1u, new AccountID(ACCOUNT), new CharacterID(CHARACTER), token, rig.Comp.TickLoop.ServerTickNumber);
            Assert.IsTrue(rig.Net.Dispatcher.AddConnection(CLIENT));
            rig.Net.ClientListener.AddClient(CLIENT);
            var body = new byte[SessionHandshake.BodySize];
            SessionHandshakeCodec.WriteBody(body, new SessionHandshake(new CharacterID(CHARACTER), wiring.Pool.Zone, token));

            // Act: record in the receive callback, then tick until the load completes and the player is admitted.
            wiring.HandshakeIntake.OnConnectionMessage(CLIENT, SessionHandshake.MessageTypeId, EntityID.Invalid, body);
            LogAssert.Expect(LogType.Error, new Regex("NavMesh unavailable"));
            for (int i = 0; i < MAX_WAIT_STEPS && !IsConnected(wiring); i++)
            {
                rig.Net.FireTick();
                if (!IsConnected(wiring))
                {
                    Thread.Sleep(WAIT_STEP_MS);
                }
            }

            // Assert
            Assert.IsTrue(IsConnected(wiring), "the session is Connected after the player is admitted");
            Assert.IsTrue(wiring.Sessions.TryGetCharacterId(ACCOUNT, out uint connectedCharacter));
            Assert.AreEqual(CHARACTER, connectedCharacter);
            var names = (ICharacterNameLookup)rig.Comp.PersistenceService;
            Assert.IsTrue(names.TryGetCharacterName(new CharacterID(CHARACTER), out string name));
            Assert.AreEqual(NAME, name);

            // AC-AUTH-21 (e), TD-100 closed by Story 015: the production state source serves the entered player's name,
            // and the LoggingZoneEntityStateSource stand-in is no longer in the chain (it always returns false, so a true
            // read here proves it was replaced).
            Assert.IsTrue(wiring.Pool.TryGetPlayerAtSlot(0, out EntityID entity, out _));
            Assert.IsTrue(rig.Comp.EntityStateSource.TryGetPlayerState(entity, out ZonePlayerStateView view));
            Assert.AreEqual(NAME, view.CharacterName);
        }

        private sealed class StubStateSource : IZoneEntityStateSource
        {
            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                view = new ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, false, 0, 0, "stub", 0u);
                return true;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = default;
                return false;
            }
        }

        [Test]
        public void test_composition_entity_state_source_override_replaces_the_inner_source()
        {
            // Arrange
            var rig = new WiRig();
            rig.Comp.EntityStateSourceOverride = new StubStateSource();

            // Act
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            // Assert: the override is read (no production source was bound over it).
            Assert.IsTrue(rig.Comp.EntityStateSource.TryGetPlayerState(new EntityID(1u), out ZonePlayerStateView view));
            Assert.AreEqual("stub", view.CharacterName);
            Assert.IsFalse(rig.Comp.EntityStateSource.TryGetMobState(new EntityID(1u), out _));
        }

        [Test]
        public void test_composition_entity_state_source_serves_a_roster_mob_after_startup()
        {
            // Arrange: the production source is bound at startup; a mob is added to the wiring's roster.
            var rig = new WiRig();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            ZoneInstancingWiring wiring = rig.Comp.ZoneInstancing;
            EntityID mob = wiring.MobRoster.TryAddMob(wiring.Pool.Zone, EnemyAICompositionFixtures.MobType, Vector3.zero);
            Assert.AreNotEqual(EntityID.Invalid, mob);

            // Enemy AI Story 013: the store seeds the mob from the spawn event (TD-102: HP is no longer 0).
            rig.Comp.MobEvents.RaiseMobSpawned(new MobSpawnedArgs(rig.Comp.ZoneId, mob, EnemyAICompositionFixtures.MobType, Vector3.zero));

            // Act
            bool found = rig.Comp.EntityStateSource.TryGetMobState(mob, out ZoneMobStateView view);

            // Assert: the seeded HP and MaxHP of the registry definition; a Dormant mob is not in combat
            // (the rig passes a navigation seam, so the combat reader is the player-only one: always false for a mob).
            Assert.IsTrue(found);
            Assert.AreEqual(EnemyAICompositionFixtures.MOB_HP, view.CurrentHp);
            Assert.AreEqual(EnemyAICompositionFixtures.MOB_HP, view.MaxHp);
            Assert.IsFalse(view.IsInCombat);
        }
    }
}
