using NUnit.Framework;

namespace IronGrind.Tests.EditMode
{
    // Minimal smoke test — confirms the test runner is wired up correctly.
    // Replace or supplement with real formula tests as systems are implemented.
    // Naming convention: [Scenario]_[ExpectedResult]
    public class SmokeTest
    {
        [Test]
        public void TestRunner_IsWiredUp_ReturnsTrue()
        {
            Assert.IsTrue(true, "If this fails, the test runner itself is broken.");
        }
    }
}
