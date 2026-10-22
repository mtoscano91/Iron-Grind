using System.Text.RegularExpressions;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>Networking Core Story 043 AC-1 (command line), AC-2 (defaults) and AC-3 (validation).</summary>
    [TestFixture]
    public sealed class ZoneServerConfigTests
    {
        private static readonly string[] VALID_ARGS =
        {
            "-zoneTemplate", "Forest", "-zoneId", "7", "-port", "7777", "-serverConfig", "cfg.json"
        };

        private static string Json(string host = "db.local", string extra = "")
        {
            string hostPart = host == null ? string.Empty : "\"Host\":\"" + host + "\",";
            string tail = extra.Length > 0 ? "," + extra : string.Empty;
            return "{" + hostPart + "\"Database\":\"irongrind\",\"Username\":\"svc\"" + tail + "}";
        }

        // Error texts carry no digits at all, so no numeric value can leak; a non-numeric value must be absent too.
        private static void AssertNoValueIn(string error, string value)
        {
            Assert.IsFalse(Regex.IsMatch(error, @"\d"), "Error text contains a digit: " + error);
            StringAssert.DoesNotContain(value, error);
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_args_parse_reads_all_four_flags()
        {
            bool ok = ZoneStartupArgs.Parse(VALID_ARGS, out ZoneStartupArgs args, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("Forest", args.ZoneTemplate);
            Assert.AreEqual(7u, args.ZoneId);
            Assert.AreEqual((ushort)7777, args.Port);
            Assert.AreEqual("cfg.json", args.ServerConfigPath);
        }

        [TestCase("-zoneTemplate")]
        [TestCase("-zoneId")]
        [TestCase("-port")]
        [TestCase("-serverConfig")]
        public void test_args_parse_missing_flag_fails_naming_it(string flag)
        {
            var list = new System.Collections.Generic.List<string>(VALID_ARGS);
            int i = list.IndexOf(flag);
            list.RemoveRange(i, 2);

            bool ok = ZoneStartupArgs.Parse(list.ToArray(), out ZoneStartupArgs args, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(args);
            StringAssert.Contains(flag, error);
        }

        [TestCase("-port", "70000")]
        [TestCase("-port", "-1")]
        [TestCase("-port", "0")]
        [TestCase("-port", "abc")]
        [TestCase("-zoneId", "abc")]
        [TestCase("-zoneId", "0")]
        [TestCase("-zoneId", "-5")]
        public void test_args_parse_invalid_value_fails_naming_the_flag_not_the_value(string flag, string value)
        {
            string[] args = (string[])VALID_ARGS.Clone();
            args[System.Array.IndexOf(args, flag) + 1] = value;

            bool ok = ZoneStartupArgs.Parse(args, out ZoneStartupArgs result, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            StringAssert.Contains(flag, error);
            AssertNoValueIn(error, value);
        }

        [TestCase("-port", "-zoneId")]
        [TestCase("-serverConfig", "-batchmode")]
        [TestCase("-zoneTemplate", "-port")]
        public void test_args_parse_flag_followed_by_another_flag_fails_naming_the_first(string flag, string next)
        {
            var list = new System.Collections.Generic.List<string>(VALID_ARGS);
            list[list.IndexOf(flag) + 1] = next;

            bool ok = ZoneStartupArgs.Parse(list.ToArray(), out ZoneStartupArgs result, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(result);
            StringAssert.StartsWith(flag + " ", error);
        }

        [Test]
        public void test_args_parse_duplicate_flag_fails_naming_it()
        {
            var list = new System.Collections.Generic.List<string>(VALID_ARGS) { "-port", "8888" };

            bool ok = ZoneStartupArgs.Parse(list.ToArray(), out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("-port", error);
        }

        [Test]
        public void test_args_parse_flag_without_value_at_end_fails_naming_it()
        {
            var list = new System.Collections.Generic.List<string>(VALID_ARGS);
            list.RemoveRange(list.IndexOf("-serverConfig"), 2);
            list.Add("-serverConfig");

            bool ok = ZoneStartupArgs.Parse(list.ToArray(), out _, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("-serverConfig", error);
        }

        [Test]
        public void test_args_parse_ignores_unknown_flags()
        {
            var list = new System.Collections.Generic.List<string> { "-batchmode", "-nographics", "-logFile", "x.log" };
            list.AddRange(VALID_ARGS);

            bool ok = ZoneStartupArgs.Parse(list.ToArray(), out ZoneStartupArgs args, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual((ushort)7777, args.Port);
        }

        [Test]
        public void test_args_parse_null_or_empty_fails_without_throwing()
        {
            Assert.IsFalse(ZoneStartupArgs.Parse(null, out _, out string nullError));
            Assert.IsNotEmpty(nullError);
            Assert.IsFalse(ZoneStartupArgs.Parse(new string[0], out _, out string emptyError));
            Assert.IsNotEmpty(emptyError);
        }

        // ---------------------------------------------------------------- AC-2

        [Test]
        public void test_config_with_only_required_fields_keeps_all_defaults()
        {
            bool ok = ZoneServerConfig.TryLoad(Json(), out ZoneServerConfig c, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("db.local", c.Host);
            Assert.AreEqual("irongrind", c.Database);
            Assert.AreEqual("svc", c.Username);
            Assert.AreEqual(5432, c.Port);
            Assert.AreEqual(5, c.Timeout);
            Assert.AreEqual(5, c.CommandTimeout);
            Assert.AreEqual(2, c.MinPoolSize);
            Assert.AreEqual(10, c.MaxPoolSize);
            Assert.AreEqual(60, c.ConnectionIdleLifetime);
            Assert.AreEqual(5000, c.DbWarmupTimeoutMs);
            Assert.AreEqual(100, c.NgoShutdownWaitFrames);
        }

        [Test]
        public void test_config_present_fields_override_defaults()
        {
            bool ok = ZoneServerConfig.TryLoad(
                Json(extra: "\"Port\":6543,\"MaxPoolSize\":20,\"NgoShutdownWaitFrames\":7"), out ZoneServerConfig c, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual(6543, c.Port);
            Assert.AreEqual(20, c.MaxPoolSize);
            Assert.AreEqual(7, c.NgoShutdownWaitFrames);
            Assert.AreEqual(2, c.MinPoolSize);
        }

        // ---------------------------------------------------------------- AC-3

        [Test]
        public void test_config_empty_host_fails_naming_host()
        {
            Assert.IsFalse(ZoneServerConfig.TryLoad(Json(host: ""), out ZoneServerConfig c, out string error));
            Assert.IsNull(c);
            StringAssert.Contains("Host", error);
        }

        [Test]
        public void test_config_missing_host_fails_naming_host()
        {
            Assert.IsFalse(ZoneServerConfig.TryLoad(Json(host: null), out _, out string error));
            StringAssert.Contains("Host", error);
        }

        [Test]
        public void test_config_empty_database_and_username_fail_naming_the_field()
        {
            Assert.IsFalse(ZoneServerConfig.TryLoad("{\"Host\":\"h\",\"Database\":\"\",\"Username\":\"u\"}", out _, out string e1));
            StringAssert.Contains("Database", e1);
            Assert.IsFalse(ZoneServerConfig.TryLoad("{\"Host\":\"h\",\"Database\":\"d\",\"Username\":\"\"}", out _, out string e2));
            StringAssert.Contains("Username", e2);
        }

        [TestCase("Port", "0")]
        [TestCase("Port", "65536")]
        [TestCase("Port", "-12345")]
        [TestCase("Timeout", "-7")]
        [TestCase("Timeout", "0")]
        [TestCase("CommandTimeout", "-8")]
        [TestCase("CommandTimeout", "0")]
        [TestCase("ConnectionIdleLifetime", "-9")]
        [TestCase("ConnectionIdleLifetime", "0")]
        [TestCase("DbWarmupTimeoutMs", "0")]
        [TestCase("DbWarmupTimeoutMs", "-4321")]
        [TestCase("NgoShutdownWaitFrames", "-1")]
        [TestCase("NgoShutdownWaitFrames", "0")]
        [TestCase("MaxPoolSize", "0")]
        [TestCase("MinPoolSize", "-3")]
        [TestCase("MinPoolSize", "11")]
        public void test_config_invalid_value_fails_naming_the_field_not_the_value(string field, string value)
        {
            bool ok = ZoneServerConfig.TryLoad(Json(extra: "\"" + field + "\":" + value), out ZoneServerConfig c, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(c);
            StringAssert.Contains(field, error);
            AssertNoValueIn(error, value);
        }

        [Test]
        public void test_config_min_pool_equal_to_max_pool_is_valid()
        {
            Assert.IsTrue(ZoneServerConfig.TryLoad(Json(extra: "\"MinPoolSize\":10,\"MaxPoolSize\":10"), out _, out string error), error);
        }

        [TestCase("\"Password\":\"hunter2\"")]
        [TestCase("\"password\" : \"hunter2\"")]
        [TestCase("\"PASSWORD\":\"hunter2\"")]
        [TestCase("\"PgPassword\":\"hunter2\"")]
        [TestCase("\"db_password\":\"hunter2\"")]
        [TestCase("\"DbPasswd\":\"hunter2\"")]
        [TestCase("\"pwd\":\"hunter2\"")]
        public void test_config_with_password_key_fails_naming_password_not_the_value(string field)
        {
            bool ok = ZoneServerConfig.TryLoad(Json(extra: field), out ZoneServerConfig c, out string error);

            Assert.IsFalse(ok);
            Assert.IsNull(c);
            StringAssert.Contains("Password", error);
            StringAssert.DoesNotContain("hunter2", error);
        }

        [Test]
        public void test_config_value_containing_password_is_not_a_password_key()
        {
            bool ok = ZoneServerConfig.TryLoad(
                "{\"Host\":\"h\",\"Database\":\"password_vault\",\"Username\":\"pwd_reader\"}", out ZoneServerConfig c, out string error);

            Assert.IsTrue(ok, error);
            Assert.AreEqual("password_vault", c.Database);
        }

        [TestCase("{not json")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("[1,2")]
        public void test_config_malformed_or_empty_text_fails_without_throwing(string text)
        {
            bool ok = false;
            ZoneServerConfig c = null;
            string error = null;

            Assert.DoesNotThrow(() => ok = ZoneServerConfig.TryLoad(text, out c, out error));

            Assert.IsFalse(ok);
            Assert.IsNull(c);
            Assert.IsNotEmpty(error);
        }
    }
}
