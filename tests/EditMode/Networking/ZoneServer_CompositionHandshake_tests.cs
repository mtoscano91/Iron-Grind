using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

            // The EntityState.characterName half of AC-AUTH-21 (e) is TD-100: production has no IZoneEntityStateSource,
            // only LoggingZoneEntityStateSource, so the snapshot source cannot be asserted here.
        }
    }
}
