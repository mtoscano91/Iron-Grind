using System.Text.RegularExpressions;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Tests for <c>DevEvidenceLog</c> (Authentication Story 017). Also hosts the guard other log-asserting fixtures call
    /// at SetUp so a leaked <c>Enabled</c> flag cannot add lines to their log assertions.
    /// </summary>
    [TestFixture]
    internal sealed class DevEvidenceLog_Tests
    {
        private bool _saved;

        /// <summary>Fails the calling fixture's SetUp when the evidence log was left enabled by an earlier test.</summary>
        internal static void AssertDisabled()
        {
            Assert.IsFalse(DevEvidenceLog.Enabled, "DevEvidenceLog.Enabled leaked from an earlier test or the host.");
        }

        [SetUp]
        public void SetUp()
        {
            _saved = DevEvidenceLog.Enabled;
            DevEvidenceLog.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            DevEvidenceLog.Enabled = _saved;
        }

        [Test]
        public void test_dev_evidence_log_disabled_writes_no_line()
        {
            // Act
            DevEvidenceLog.Write("[Zone]", "Admit tick=3");

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_dev_evidence_log_enabled_writes_one_line_with_prefix_utc_timestamp_and_text()
        {
            // Arrange
            DevEvidenceLog.Enabled = true;
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Zone\] \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z Admit tick=3$"));

            // Act
            DevEvidenceLog.Write("[Zone]", "Admit tick=3");

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_dev_evidence_log_guard_passes_when_disabled()
        {
            Assert.DoesNotThrow(AssertDisabled);
        }
    }
}
