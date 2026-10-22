using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterPersistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Networking Core Story 044, Database category: S3-S5 and S7 with the real backend (no warm-up or listener override)
    /// against the portable PostgreSQL. Explicit: needs <c>IRONGRIND_DEV_PG</c>; run with <c>-testFilter</c>
    /// (tools/postgres/README.md). The connection settings come from <c>IRONGRIND_DEV_PG</c> and reach the composition
    /// through the server config JSON and a fake environment. No Npgsql type is named here.
    /// </summary>
    [TestFixture]
    [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG - tools/postgres/README.md")]
    [Category("Database")]
    public sealed class ZoneServerCompositionDatabaseTests
    {
        private const int POLL_MS = 2000;
        private const uint ZONE_ID = 7u;
        private const string S4_FAILED = "[ZoneServer] startup failed at S4: StepReturnedFalse";

        private static string Json(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ConfigJson(ZoneConnectionFields f, string host, int port, int warmupMs)
        {
            return "{\"Host\":\"" + Json(host) + "\",\"Port\":" + port + ",\"Database\":\"" + Json(f.Database)
                + "\",\"Username\":\"" + Json(f.Username) + "\",\"DbWarmupTimeoutMs\":" + warmupMs
                + ",\"NgoShutdownWaitFrames\":2}";
        }

        private static int WaitForConnectionCount(string adminConnectionString, string applicationName, int expected)
        {
            var watch = Stopwatch.StartNew();
            int count;
            do
            {
                count = ZoneCompositionDatabaseTestAccess.CountConnections(adminConnectionString, applicationName);
                if (count == expected)
                {
                    break;
                }

                System.Threading.Thread.Sleep(50);
            }
            while (watch.ElapsedMilliseconds < POLL_MS);

            return count;
        }

        // Polls until at least `minimum` sessions of the application name have run LISTEN, up to `timeoutMs`.
        private static int WaitForListening(string adminConnectionString, string applicationName, int minimum, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            int count;
            do
            {
                count = ZoneCompositionDatabaseTestAccess.CountListeningConnections(adminConnectionString, applicationName);
                if (count >= minimum)
                {
                    break;
                }

                System.Threading.Thread.Sleep(50);
            }
            while (watch.ElapsedMilliseconds < timeoutMs);

            return count;
        }

        // ---------------------------------------------------------------- AC-11

        [Test]
        public void test_real_backend_starts_warms_up_and_starts_the_listener_then_leaves_no_connection_after_dispose()
        {
            string dev = PgTestDatabase.ConnectionStringOrIgnore();
            ZoneConnectionFields f = ZoneCompositionDatabaseTestAccess.Parse(dev);
            if (string.IsNullOrEmpty(f.Password))
            {
                Assert.Ignore("IRONGRIND_DEV_PG has no password; the composition requires one.");
            }

            string appName = ZoneCompositionDatabaseTestAccess.ApplicationNameOf(ZONE_ID);
            var rig = new WiRig(ConfigJson(f, f.Host, f.Port, 5000), true);
            rig.Env.Variables["IRONGRIND_PG_PASSWORD"] = f.Password;

            var watch = Stopwatch.StartNew();
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            watch.Stop();

            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "startup must succeed");
            Assert.Less(watch.ElapsedMilliseconds, 5000L, "warm-up within DbWarmupTimeoutMs");
            Assert.IsTrue(ZoneCompositionDatabaseTestAccess.ListenerIsRunning(rig.Comp));

            // The listener opens its own unpooled connection on its thread (SCRAM on Mono takes a moment) and then runs
            // LISTEN. Wait for that LISTEN session, so the teardown stops a listener that is in its Wait loop: a stop
            // during Open only ends at the join timeout, which ADR-015 allows and Story 041 covered. Counting all
            // sessions of the name is not enough, since MinPoolSize=2 lets the pool alone hold two.
            Assert.AreEqual(1, WaitForListening(dev, appName, 1, 10000), "the listener's LISTEN session");
            Assert.GreaterOrEqual(ZoneCompositionDatabaseTestAccess.CountConnections(dev, appName), 2,
                "the listener connection plus at least one pooled connection");

            rig.Comp.RequestTeardown(ZoneExitCode.Clean);
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
            SuRig.Drive(rig.Comp.RunTeardown());

            Assert.IsFalse(ZoneCompositionDatabaseTestAccess.ListenerIsRunning(rig.Comp));
            Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
            Assert.AreEqual(0, WaitForConnectionCount(dev, appName, 0), "no connection of this application name remains");
        }

        // ---------------------------------------------------------------- listener wake (ADR-015 note 2026-10-22)

        // Runs one Wait on a pool thread with an outer bound, so a regression fails the test instead of hanging the run.
        private static bool WaitBounded(IRelayListenerConnection connection, int outerMs)
        {
            Task<bool> wait = Task.Run(() => connection.Wait(500));
            Assert.IsTrue(wait.Wait(outerMs), "Wait returned within " + outerMs + " ms");
            return wait.Result;
        }

        [Test]
        public void test_listener_connection_delivers_relay_payloads_only_and_a_wake_ends_its_wait()
        {
            string dev = PgTestDatabase.ConnectionStringOrIgnore();
            // Mixed case: PostgreSQL reports the channel exactly as LISTENed (quoted identifier), and the filter is ordinal.
            string relay = "IronGrind_Relay_Test_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string wake = relay + "_wake";
            var delivered = new List<string>();
            var factory = new NpgsqlRelayListenerConnection.Factory(NpgsqlRelayListener.BuildListenerConnectionString(dev));
            IRelayListenerConnection connection = factory.Create();
            try
            {
                connection.NotificationHandler = delivered.Add;
                connection.Open();
                connection.Listen(relay);
                connection.ListenWake(wake);

                ZoneCompositionDatabaseTestAccess.Notify(dev, wake, "wake-payload");
                Assert.IsTrue(WaitBounded(connection, 3000), "a wake notification ends the wait");
                CollectionAssert.IsEmpty(delivered, "a wake payload never reaches the handler");

                ZoneCompositionDatabaseTestAccess.Notify(dev, relay, "relay-payload");
                Assert.IsTrue(WaitBounded(connection, 3000));
                CollectionAssert.AreEqual(new[] { "relay-payload" }, delivered);

                factory.SendWake(wake);
                Assert.IsTrue(WaitBounded(connection, 3000), "Factory.SendWake ends the wait");
                CollectionAssert.AreEqual(new[] { "relay-payload" }, delivered);
            }
            finally
            {
                connection.Dispose();
            }
        }

        [Test]
        public void test_wake_sender_to_an_unreachable_host_fails_within_the_connection_timeout()
        {
            // Stop() runs the wake on the stopping thread, so a dead server must not hold it beyond the connect timeout.
            var factory = new NpgsqlRelayListenerConnection.Factory(NpgsqlRelayListener.BuildListenerConnectionString(
                "Host=127.0.0.1;Port=1;Username=u;Password=unused;Database=d;Timeout=2"));
            var watch = Stopwatch.StartNew();

            Assert.Catch(() => factory.SendWake("irongrind_relay_wake_unreachable"));

            Assert.Less(watch.ElapsedMilliseconds, 3000L, "Timeout=2 plus 1 s");
        }

        // ---------------------------------------------------------------- AC-12

        [Test]
        public void test_wrong_password_fails_at_s4_within_the_bound_with_sqlstate_28p01_and_no_secret_in_the_logs()
        {
            string dev = PgTestDatabase.ConnectionStringOrIgnore();
            ZoneConnectionFields f = ZoneCompositionDatabaseTestAccess.Parse(dev);
            const string WRONG = "Wr0ng;Pass!word";
            const int WARMUP_MS = 5000;
            // The two error lines this failure must produce, and no other error (scram authentication on the dev server).
            LogAssert.Expect(LogType.Error, "[ZoneServer] S4: database warm-up failed: PostgresException SqlState=28P01");
            LogAssert.Expect(LogType.Error, S4_FAILED);
            using (var capture = new WiLogCapture())
            {
                var rig = new WiRig(ConfigJson(f, f.Host, f.Port, WARMUP_MS), true);
                rig.Env.Variables["IRONGRIND_PG_PASSWORD"] = WRONG;
                var watch = Stopwatch.StartNew();

                SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
                watch.Stop();
                bool failed = rig.Comp.TryBeginTeardown();
                rig.Comp.TeardownNow();

                if (!failed)
                {
                    Assert.Ignore("The server accepted a wrong password (trust authentication); AC-12 needs password authentication.");
                }

                Assert.Less(watch.ElapsedMilliseconds, WARMUP_MS + 1000L);
                Assert.IsTrue(capture.Lines.Exists(l => l.Contains("startup failed at S4")));
                Assert.IsTrue(capture.Lines.Exists(l => l.Contains("SqlState=28P01")), "SqlState 28P01 is logged");
                foreach (string line in capture.Lines)
                {
                    StringAssert.DoesNotContain(WRONG, line);
                    StringAssert.DoesNotContain("Password=", line);
                    StringAssert.DoesNotContain("Host=", line);
                }

                Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
            }
        }

        // ---------------------------------------------------------------- AC-13

        [Test]
        public void test_unreachable_host_fails_at_s4_and_never_hangs_past_the_warm_up_bound()
        {
            string dev = PgTestDatabase.ConnectionStringOrIgnore();
            ZoneConnectionFields f = ZoneCompositionDatabaseTestAccess.Parse(dev);
            const int WARMUP_MS = 2000;
            // A closed port either times out or faults (connection refused); either way one reason line, then S-fail.
            LogAssert.Expect(LogType.Error,
                new Regex(@"^\[ZoneServer\] S4: database warm-up (timed out|failed: \w+( SqlState=\w+)?)$"));
            LogAssert.Expect(LogType.Error, S4_FAILED);
            using (var capture = new WiLogCapture())
            {
                var rig = new WiRig(ConfigJson(f, "127.0.0.1", 1, WARMUP_MS), true);
                rig.Env.Variables["IRONGRIND_PG_PASSWORD"] = "Unreach;able!1";
                var watch = Stopwatch.StartNew();

                SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
                watch.Stop();

                Assert.IsTrue(rig.Comp.TryBeginTeardown(), "S-fail requests the teardown");
                rig.Comp.TeardownNow();
                Assert.Less(watch.ElapsedMilliseconds, WARMUP_MS + 1000L);
                Assert.IsTrue(capture.Lines.Exists(l => l.Contains("startup failed at S4")));
                foreach (string line in capture.Lines)
                {
                    StringAssert.DoesNotContain("Unreach;able", line);
                    StringAssert.DoesNotContain("Password=", line);
                }

                Assert.AreEqual(1, ZoneCompositionDatabaseTestAccess.DataSourceDisposeCount(rig.Comp));
            }
        }
    }
}
