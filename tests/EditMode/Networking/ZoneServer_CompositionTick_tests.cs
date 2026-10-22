using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Networking Core Story 045: AC-15 (a throwing tick is contained in the composition) and the host source
    /// searches for AC-1, AC-6 and AC-14.
    /// </summary>
    [TestFixture]
    public sealed class ZoneServerCompositionTickTests
    {
        private static string HostPath()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "src", "ServerLogic", "ZoneServer", "ZoneServerHost.cs");
        }

        private static string HostCode()
        {
            Assert.IsTrue(File.Exists(HostPath()), "ZoneServerHost.cs not found");
            return StripComments(File.ReadAllText(HostPath()));
        }

        // Removes // and /* */ comments (a "//" inside a string literal is kept).
        private static string StripComments(string text)
        {
            var code = new StringBuilder(text.Length);
            int i = 0;
            bool inString = false;
            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (inString)
                {
                    code.Append(c);
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        code.Append(next);
                        i += 2;
                        continue;
                    }

                    if (c == '"')
                    {
                        inString = false;
                    }

                    i++;
                }
                else if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    code.Append(' ');
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                    }

                    code.Append(c);
                    i++;
                }
            }

            return code.ToString();
        }

        private static SuRig StartedRigWithThrowingTick(Exception toThrow)
        {
            var rig = new SuRig();
            rig.Now = 100.0;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Backend.TickLoop.RegisterTickDriven(delegate (float dt) { throw toThrow; });
            return rig;
        }

        // ---------------------------------------------------------------- AC-15

        [Test]
        public void test_throwing_tick_is_contained_logs_the_type_once_and_requests_fatal_once()
        {
            SuRig rig = StartedRigWithThrowingTick(new InvalidOperationException("secret-detail"));
            LogAssert.Expect(LogType.Error, "[ZoneServer] tick failed: InvalidOperationException");

            Assert.DoesNotThrow(() => rig.Net.FireTick());
            Assert.DoesNotThrow(() => rig.Net.FireTick());

            LogAssert.NoUnexpectedReceived();
            Assert.IsTrue(rig.Comp.TryBeginTeardown(), "A teardown was requested.");
            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "Requested once.");
        }

        [Test]
        public void test_throwing_tick_makes_the_exit_code_fatal()
        {
            SuRig rig = StartedRigWithThrowingTick(new ArgumentException("x"));
            LogAssert.Expect(LogType.Error, "[ZoneServer] tick failed: ArgumentException");

            rig.Net.FireTick();
            SuRig.Drive(rig.Comp.RunTeardown());

            Assert.Contains("Quit:" + (int)ZoneExitCode.Fatal, rig.Log);
        }

        [Test]
        public void test_a_teardown_already_requested_keeps_its_code_when_a_tick_throws()
        {
            SuRig rig = StartedRigWithThrowingTick(new InvalidOperationException());
            LogAssert.Expect(LogType.Error, "[ZoneServer] tick failed: InvalidOperationException");
            rig.Comp.RequestTeardown(ZoneExitCode.Clean);

            rig.Net.FireTick();
            SuRig.Drive(rig.Comp.RunTeardown());

            Assert.Contains("Quit:" + (int)ZoneExitCode.Clean, rig.Log);
        }

        [Test]
        public void test_a_non_throwing_tick_logs_and_requests_nothing()
        {
            var rig = new SuRig();
            rig.Now = 100.0;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            rig.Net.FireTick();

            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
        }

        // ---------------------------------------------------------------- AC-1

        [Test]
        public void test_host_source_constructs_no_server_system_types()
        {
            string code = HostCode();
            Assert.IsFalse(Regex.IsMatch(code, @"IronGrind\.(Networking|CharacterPersistence|Randomness)"),
                "The host must not name the Networking, CharacterPersistence or Randomness namespaces.");
            foreach (Match m in Regex.Matches(code, @"\bnew\s+([A-Za-z_][A-Za-z0-9_]*)"))
            {
                string type = m.Groups[1].Value;
                Assert.IsFalse(
                    type.StartsWith("Ngo", StringComparison.Ordinal) && type != "NgoZoneNetwork",
                    "Host constructs a Networking type: " + type);
                Assert.IsFalse(
                    type.StartsWith("Npgsql", StringComparison.Ordinal) || type.Contains("Random")
                    || type.Contains("Dispatcher") || type.Contains("Outbox") || type.Contains("TickLoop"),
                    "Host constructs a server system: " + type);
            }
        }

        // ---------------------------------------------------------------- AC-6

        [Test]
        public void test_teardown_after_seconds_is_parsed_only_inside_the_development_guard()
        {
            string code = HostCode();
            int flagIndex = code.IndexOf("-teardownAfterSeconds", StringComparison.Ordinal);
            Assert.GreaterOrEqual(flagIndex, 0, "Flag not found.");
            foreach (Match m in Regex.Matches(code, "-teardownAfterSeconds"))
            {
                Assert.IsTrue(IsInsideDevGuard(code, m.Index), "Flag outside #if DEVELOPMENT_BUILD || UNITY_EDITOR.");
            }

            foreach (Match m in Regex.Matches(code, @"\bReadTeardownTrigger\b|_teardownAfterSeconds"))
            {
                Assert.IsTrue(IsInsideDevGuard(code, m.Index) || IsCallSiteGuarded(code, m.Index),
                    "Trigger code outside the guard at index " + m.Index);
            }
        }

        // True when the position lies between "#if DEVELOPMENT_BUILD || UNITY_EDITOR" and its "#endif" (no nesting used).
        private static bool IsInsideDevGuard(string code, int index)
        {
            const string OPEN = "#if DEVELOPMENT_BUILD || UNITY_EDITOR";
            int open = code.LastIndexOf(OPEN, index, StringComparison.Ordinal);
            if (open < 0)
            {
                return false;
            }

            int close = code.IndexOf("#endif", open, StringComparison.Ordinal);
            return close > index;
        }

        private static bool IsCallSiteGuarded(string code, int index)
        {
            return IsInsideDevGuard(code, index);
        }

        // ---------------------------------------------------------------- AC-14

        [Test]
        public void test_host_never_interrupts_the_teardown_coroutine()
        {
            string code = HostCode();
            string[] forbidden =
            {
                @"\bStopAllCoroutines\b", @"\bStopCoroutine\b", @"SetActive\s*\(\s*false\s*\)",
                @"\benabled\s*=\s*false", @"(?<![A-Za-z_])Destroy\s*\(", @"DestroyImmediate\s*\("
            };
            foreach (string pattern in forbidden)
            {
                Assert.IsFalse(Regex.IsMatch(code, pattern), "Host source contains " + pattern);
            }
        }
    }
}
