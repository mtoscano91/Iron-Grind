using IronGrind.DevHarness.Session;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.DevHarness
{
    /// <summary>EditMode tests for <see cref="ZoneEntryHarnessSettings"/> (Authentication Story 017: trimming and overrides).</summary>
    [TestFixture]
    internal sealed class ZoneEntryHarnessSettings_Tests
    {
        private static ZoneEntryHarnessSettings Resolve(string[] args, bool loadDriverMode = false)
        {
            return ZoneEntryHarnessSettings.Resolve(
                args, "cfgUser", "cfg pass ", "cfgChar", "http://gw", "127.0.0.1 ", loadDriverMode);
        }

        [Test]
        public void test_harness_settings_config_values_are_trimmed_except_the_password()
        {
            // Arrange / Act
            ZoneEntryHarnessSettings s = ZoneEntryHarnessSettings.Resolve(
                new string[0], " user ", " pass ", "char\t", " http://gw ", "127.0.0.1 ", false);

            // Assert
            Assert.AreEqual("user", s.Username);
            Assert.AreEqual(" pass ", s.Password);
            Assert.AreEqual("char", s.CharacterName);
            Assert.AreEqual("http://gw", s.GatewayUrl);
            Assert.AreEqual("127.0.0.1", s.ZoneHostOverride);
        }

        [Test]
        public void test_harness_settings_command_line_overrides_win_and_are_trimmed()
        {
            // Arrange
            string[] args =
            {
                "exe", ZoneEntryHarnessSettings.ARG_USER, " argUser ", ZoneEntryHarnessSettings.ARG_CHARACTER, "argChar ",
                ZoneEntryHarnessSettings.ARG_PASSWORD, "argPass", ZoneEntryHarnessSettings.ARG_GATEWAY, "http://other",
                ZoneEntryHarnessSettings.ARG_ZONE_HOST, "10.0.0.5 ",
            };

            // Act
            ZoneEntryHarnessSettings s = Resolve(args);

            // Assert
            Assert.AreEqual("argUser", s.Username);
            Assert.AreEqual("argChar", s.CharacterName);
            Assert.AreEqual("argPass", s.Password);
            Assert.AreEqual("http://other", s.GatewayUrl);
            Assert.AreEqual("10.0.0.5", s.ZoneHostOverride);
        }

        [Test]
        public void test_harness_settings_empty_override_falls_back_to_config()
        {
            // Arrange
            string[] args = { ZoneEntryHarnessSettings.ARG_USER, "", ZoneEntryHarnessSettings.ARG_CHARACTER, "  " };

            // Act
            ZoneEntryHarnessSettings s = Resolve(args);

            // Assert
            Assert.AreEqual("cfgUser", s.Username);
            Assert.AreEqual("cfgChar", s.CharacterName);
        }

        [Test]
        public void test_harness_settings_missing_everywhere_resolves_to_empty_never_null()
        {
            // Act
            ZoneEntryHarnessSettings s = ZoneEntryHarnessSettings.Resolve(null, null, null, null, null, null, false);

            // Assert
            Assert.AreEqual("", s.Username);
            Assert.AreEqual("", s.Password);
            Assert.AreEqual("", s.CharacterName);
            Assert.AreEqual("", s.GatewayUrl);
            Assert.AreEqual("", s.ZoneHostOverride);
            Assert.IsFalse(s.LoadDriver);
        }

        [Test]
        public void test_harness_settings_flag_without_a_value_at_the_end_is_ignored_and_load_driver_flag_works()
        {
            // Arrange: a trailing -zeUser has no value; the load driver flag takes none.
            string[] args = { ZoneEntryHarnessSettings.ARG_LOAD_DRIVER, ZoneEntryHarnessSettings.ARG_USER };

            // Act
            ZoneEntryHarnessSettings s = Resolve(args);

            // Assert
            Assert.AreEqual("cfgUser", s.Username);
            Assert.IsTrue(s.LoadDriver);
            Assert.IsTrue(Resolve(new string[0], true).LoadDriver, "Scene flag also selects the load driver.");
            Assert.IsFalse(Resolve(new string[0]).LoadDriver);
        }
    }
}
