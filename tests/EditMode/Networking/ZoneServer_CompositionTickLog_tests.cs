using System;
using System.IO;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>Composition wiring of the dev tick timestamp log through <c>TickLogWriterFactory</c> (Authentication Story 017).</summary>
    [TestFixture]
    internal sealed class ZoneServerCompositionTickLog_Tests
    {
        private sealed class TrackingWriter : StringWriter
        {
            public int DisposeCount;

            protected override void Dispose(bool disposing)
            {
                DisposeCount++;
                base.Dispose(disposing);
            }
        }

        [SetUp]
        public void SetUp()
        {
            DevEvidenceLog_Tests.AssertDisabled();
        }

        private static string[] Lines(TextWriter writer)
        {
            return writer.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        [Test]
        public void test_composition_tick_log_factory_null_opens_no_writer_and_ticks_run()
        {
            // Arrange
            var rig = new SuRig();
            rig.Now = 10.0;

            // Act
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Net.FireTick();

            // Assert
            Assert.IsNull(rig.Comp.TickLogWriterFactory);
            Assert.IsTrue(rig.Comp.TryBeginTeardown() == false, "A tick without a log requests no teardown.");
        }

        [Test]
        public void test_composition_tick_log_factory_runs_at_startup_before_the_tick_subscription()
        {
            // Arrange
            var rig = new SuRig();
            var writer = new TrackingWriter();
            Action subscribedWhenOpened = null;
            bool opened = false;
            rig.Comp.TickLogWriterFactory = args =>
            {
                opened = true;
                subscribedWhenOpened = rig.Net.SubscribedHandler;
                Assert.AreEqual(7, args.ZoneId);
                return writer;
            };

            // Act
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            // Assert
            Assert.IsTrue(opened);
            Assert.IsNull(subscribedWhenOpened, "The log opens before S9 subscribes the tick handler.");
            Assert.IsNotNull(rig.Net.SubscribedHandler);
        }

        [Test]
        public void test_composition_tick_log_records_one_line_per_tick_after_the_header()
        {
            // Arrange
            var rig = new SuRig();
            var writer = new TrackingWriter();
            rig.Comp.TickLogWriterFactory = args => writer;
            rig.Now = 100.0;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            // Act
            rig.Net.FireTick();
            rig.Now = 100.05;
            rig.Net.FireTick();

            // Assert
            string[] lines = Lines(writer);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("tick,ms,durationMs", lines[0]);
        }

        [Test]
        public void test_composition_tick_log_is_disposed_with_the_composition()
        {
            // Arrange
            var rig = new SuRig();
            var writer = new TrackingWriter();
            rig.Comp.TickLogWriterFactory = args => writer;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            Assert.AreEqual(0, writer.DisposeCount);

            // Act
            rig.Comp.Dispose();

            // Assert
            Assert.AreEqual(1, writer.DisposeCount);
        }

        [Test]
        public void test_composition_tick_log_records_a_tick_that_threw_and_the_throw_still_requests_fatal_once()
        {
            // Arrange
            var rig = new SuRig();
            var writer = new TrackingWriter();
            rig.Comp.TickLogWriterFactory = args => writer;
            rig.Backend.TickLoop.RegisterTickDriven(delegate (float dt) { throw new InvalidOperationException("x"); });
            rig.Now = 5.0;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            LogAssert.Expect(LogType.Error, "[ZoneServer] tick failed: InvalidOperationException");

            // Act
            rig.Net.FireTick();

            // Assert
            Assert.AreEqual(2, Lines(writer).Length, "Header and the failing tick's line.");
            Assert.IsTrue(rig.Comp.TryBeginTeardown());
        }

        [Test]
        public void test_composition_tick_log_failing_writer_does_not_fail_the_tick()
        {
            // Arrange
            var rig = new SuRig();
            rig.Comp.TickLogWriterFactory = args => new ThrowingWriter();
            rig.Now = 5.0;
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            // Act / Assert
            Assert.DoesNotThrow(() => rig.Net.FireTick());
            LogAssert.NoUnexpectedReceived();
            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "A log I/O failure is not a tick failure.");
        }

        [Test]
        public void test_composition_tick_log_throwing_factory_runs_the_zone_without_the_log()
        {
            // Arrange
            var rig = new SuRig();
            rig.Comp.TickLogWriterFactory = args => throw new InvalidOperationException("no disk");
            LogAssert.Expect(LogType.Warning, "[ZoneServer] tick log disabled: InvalidOperationException");

            // Act
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Net.FireTick();

            // Assert
            Assert.IsNotNull(rig.Net.SubscribedHandler, "Startup still reached S9.");
            Assert.IsFalse(rig.Comp.TryBeginTeardown());
        }

        private sealed class ThrowingWriter : StringWriter
        {
            public override void WriteLine(string value)
            {
                throw new IOException("disk full");
            }

            public override void Flush()
            {
                throw new IOException("disk full");
            }
        }
    }
}
