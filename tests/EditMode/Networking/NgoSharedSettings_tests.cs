using IronGrind.Client.Movement;
using IronGrind.DevHarness.Session;
using IronGrind.Networking;
using NUnit.Framework;
using Unity.Netcode;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// The zone server and the client harness must configure NGO alike (one config hash). Authentication Story 017.
    /// </summary>
    [TestFixture]
    internal sealed class NgoSharedSettings_Tests
    {
        [Test]
        public void test_ngo_shared_settings_tick_rate_equals_server_and_client_tick_rates()
        {
            Assert.AreEqual((uint)ServerTickLoop.TICK_RATE_HZ, NgoSharedSettings.TICK_RATE);
            Assert.AreEqual(ClientMovementController.CLIENT_TICK_RATE_HZ, (int)NgoSharedSettings.TICK_RATE);
        }

        [Test]
        public void test_ngo_shared_settings_harness_tick_rate_equals_server_tick_loop_rate()
        {
            // Act
            NetworkConfig client = ZoneEntryDevHarness.CreateNetworkConfig(null);

            // Assert
            Assert.AreEqual((uint)ServerTickLoop.TICK_RATE_HZ, client.TickRate);
        }

        [Test]
        public void test_ngo_shared_settings_harness_config_matches_the_zone_side_fields()
        {
            // Arrange: the zone (NgoZoneNetwork) builds ConnectionApproval false, EnableSceneManagement false, no player
            // prefab, leaves ForceSamePrefabs at NGO's default and sets TickRate through Configure from the shared value.
            var zone = new NetworkConfig
            {
                PlayerPrefab = null,
                ConnectionApproval = false,
                EnableSceneManagement = false,
                TickRate = NgoSharedSettings.TICK_RATE,
            };

            // Act
            NetworkConfig client = ZoneEntryDevHarness.CreateNetworkConfig(null);

            // Assert
            Assert.AreEqual(zone.TickRate, client.TickRate);
            Assert.AreEqual(zone.ConnectionApproval, client.ConnectionApproval);
            Assert.AreEqual(zone.EnableSceneManagement, client.EnableSceneManagement);
            Assert.AreEqual(zone.ForceSamePrefabs, client.ForceSamePrefabs);
            Assert.IsNull(client.PlayerPrefab);
        }

        [Test]
        public void test_ngo_shared_settings_force_same_prefabs_constant_is_ngo_default()
        {
            Assert.AreEqual(new NetworkConfig().ForceSamePrefabs, NgoSharedSettings.FORCE_SAME_PREFABS);
        }
    }
}
