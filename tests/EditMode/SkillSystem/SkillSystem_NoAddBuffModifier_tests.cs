using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// Static check for Skill System Story 008, AC-SK-28: no file under <c>src/ServerLogic/SkillSystem/</c> calls the
    /// Character Stats buff-modifier add method; only Status Effects may. File I/O is allowed here and only here.
    /// </summary>
    [TestFixture]
    internal sealed class SkillSystem_NoAddBuffModifier_Tests
    {
        private const string FORBIDDEN_CALL = "AddBuffModifier(";
        private const string SKILL_DIR = "src/ServerLogic/SkillSystem";

        private static string FindSkillSystemDirectory()
        {
            string[] starts = { Directory.GetCurrentDirectory(), Application.dataPath };
            foreach (string start in starts)
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, SKILL_DIR);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }

                    dir = dir.Parent;
                }
            }

            return null;
        }

        /// <summary>True when the source text contains the forbidden call; pure, so the scanner itself is testable.</summary>
        internal static bool ContainsForbiddenCall(string source)
        {
            return source != null && source.Contains(FORBIDDEN_CALL);
        }

        [Test]
        public void test_nobuffmodifier_scanner_detects_a_forbidden_call()
        {
            Assert.That(ContainsForbiddenCall("stats." + FORBIDDEN_CALL + "id, stat, entry);"), Is.True, "positive control");
            Assert.That(ContainsForbiddenCall("stats.RemoveBuffModifier(id, stat, buff);"), Is.False);
            Assert.That(ContainsForbiddenCall(null), Is.False);
        }

        [Test]
        public void test_nobuffmodifier_skill_system_sources_never_call_add_buff_modifier()
        {
            string skillDir = FindSkillSystemDirectory();
            Assert.That(skillDir, Is.Not.Null, "could not locate " + SKILL_DIR + " from the working directory or Application.dataPath");
            Assert.That(Directory.Exists(skillDir), Is.True);
            string[] files = Directory.GetFiles(skillDir, "*.cs", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(0), "the search must cover real source files");

            var offenders = new System.Collections.Generic.List<string>();
            foreach (string file in files)
            {
                if (ContainsForbiddenCall(File.ReadAllText(file)))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }

            Assert.That(offenders, Is.Empty, "files calling " + FORBIDDEN_CALL + ": " + string.Join(", ", offenders));
        }
    }
}
