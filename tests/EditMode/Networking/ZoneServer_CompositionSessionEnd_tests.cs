using System.Text.RegularExpressions;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Zone Instancing Story 016, composition level: AC-7 (the startup check on the connecting timeout vs. the persistence
    /// load watchdog) and AC-10 (the composition no longer constructs the activity and ghost stand-ins).
    /// </summary>
    [TestFixture]
    internal sealed class ZoneServer_CompositionSessionEnd_Tests
    {

        [Test]
        public void test_composition_startup_fails_when_the_connecting_timeout_does_not_exceed_the_load_watchdog()
        {
            // Arrange: 10 s x 20 Hz = 200 ticks = PERSISTENCE_WATCHDOG_TICKS.
            var rig = new WiRig();
            rig.Comp.ConnectingTimeoutSecondsOverride = 10;
            LogAssert.Expect(LogType.Error, new Regex(@"connecting timeout \(200 ticks\) must exceed the persistence load watchdog \(200 ticks\)"));
            LogAssert.Expect(LogType.Error, new Regex(@"startup failed at S5"));

            // Act
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            // Assert: S5 stopped before it constructed any server object.
            Assert.IsFalse(rig.Observed.Contains(nameof(InboundRequestDispatcher)), "no object was constructed at S5");
            Assert.IsNull(rig.Comp.ZoneInstancing);
        }

        [Test]
        public void test_composition_startup_default_connecting_timeout_passes_the_invariant()
        {
            var rig = new WiRig();

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.IsNotNull(rig.Comp.ZoneInstancing, "S5 constructed the Zone Instancing wiring");
        }

        [Test]
        public void test_composition_does_not_construct_the_activity_or_ghost_stand_ins()
        {
            // Arrange
            var rig = new WiRig();

            // Act
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            // Assert: the dispatcher reports to the very sink the wiring drains, and the ghost source is the session-machine one.
            var dispatcher = (InboundRequestDispatcher)rig.Net.Dispatcher;
            Assert.IsInstanceOf<RecordingConnectionActivitySink>(dispatcher.ActivitySink, "dispatcher activity sink");
            Assert.IsNotNull(rig.Comp.ZoneInstancing.ActivitySink, "wiring activity sink");
            Assert.AreSame(dispatcher.ActivitySink, rig.Comp.ZoneInstancing.ActivitySink, "same sink instance");
            Assert.IsInstanceOf<SessionMachineGhostSource>(rig.Comp.ZoneInstancing.GhostSource, "ghost source");
        }
    }
}
