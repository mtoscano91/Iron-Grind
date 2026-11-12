using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>EditMode tests for the dev per-tick timestamp log (Authentication Story 017, AC-AUTH-05a/05b).</summary>
    [TestFixture]
    internal sealed class TickTimestampLog_Tests
    {
        private const double SHORT_TICK_SECONDS = 0.002;

        private StringWriter _writer;
        private List<string> _lines;

        [SetUp]
        public void SetUp()
        {
            _writer = new StringWriter(CultureInfo.InvariantCulture);
            _lines = new List<string>();
        }

        private TickTimestampLog NewLog(int summaryInterval = 1200)
        {
            return new TickTimestampLog(
                _writer, _lines.Add, TickTimestampLog.TICK_DURATION_BUDGET_MS, TickTimestampLog.MISSED_TICK_GAP_MS, summaryInterval);
        }

        private string[] WrittenLines()
        {
            return _writer.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        [Test]
        public void test_tick_log_writes_header_once_and_one_line_per_record()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 1.0, 0.0025);
            log.Record(2u, 1.05, 0.004);

            // Assert
            string[] written = WrittenLines();
            Assert.AreEqual(3, written.Length);
            Assert.AreEqual("tick,ms,durationMs", written[0]);
            Assert.AreEqual("1,1000.000,2.500", written[1]);
            Assert.AreEqual("2,1050.000,4.000", written[2]);
        }

        [Test]
        public void test_tick_log_first_record_has_no_gap_warning()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 1000.0, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(0, _lines.Count);
            Assert.AreEqual(0.0, log.MaxGapMsThisWindow);
            Assert.AreEqual(0, log.TotalMissedTickCount);
        }

        [Test]
        public void test_tick_log_nominal_period_gaps_above_50ms_do_not_warn()
        {
            // Arrange: gaps of 50-60 ms are normal frame-boundary jitter at 20 Hz.
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 0.0, SHORT_TICK_SECONDS);
            log.Record(2u, 0.0596, SHORT_TICK_SECONDS);
            log.Record(3u, 0.1100, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(0, _lines.Count);
            Assert.AreEqual(0, log.TotalMissedTickCount);
            Assert.AreEqual(0, log.TotalOverBudgetCount);
        }

        [Test]
        public void test_tick_log_gap_just_under_missed_tick_threshold_does_not_warn()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 0.0, SHORT_TICK_SECONDS);
            log.Record(2u, 0.0999, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(0, _lines.Count);
            Assert.AreEqual(0, log.TotalMissedTickCount);
        }

        [Test]
        public void test_tick_log_gap_at_missed_tick_threshold_warns_with_tick_number()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 0.0, SHORT_TICK_SECONDS);
            log.Record(2u, 0.125, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(1, _lines.Count);
            Assert.AreEqual("tick gap 125.0 ms at tick=2", _lines[0]);
            Assert.AreEqual(1, log.TotalMissedTickCount);
            Assert.AreEqual(0, log.TotalOverBudgetCount);
        }

        [Test]
        public void test_tick_log_duration_just_under_budget_does_not_warn()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 0.0, 0.0499);

            // Assert
            Assert.AreEqual(0, _lines.Count);
            Assert.AreEqual(0, log.TotalOverBudgetCount);
        }

        [Test]
        public void test_tick_log_duration_over_budget_warns_with_tick_number()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(7u, 0.0, 0.0625);

            // Assert
            Assert.AreEqual(1, _lines.Count);
            Assert.AreEqual("tick duration 62.5 ms at tick=7", _lines[0]);
            Assert.AreEqual(1, log.TotalOverBudgetCount);
            Assert.AreEqual(0, log.TotalMissedTickCount);
        }

        [Test]
        public void test_tick_log_summary_after_interval_reports_window_maxima_then_resets()
        {
            // Arrange
            TickTimestampLog log = NewLog(summaryInterval: 4);

            // Act
            log.Record(1u, 0.0, 0.001);
            log.Record(2u, 0.04, 0.0125);
            log.Record(3u, 0.06, 0.003);
            log.Record(4u, 0.1, 0.002);

            // Assert
            Assert.AreEqual(1, _lines.Count);
            Assert.AreEqual("tick window ticks=4 maxGapMs=40.0 maxDurationMs=12.5", _lines[0]);
            Assert.AreEqual(0.0, log.MaxGapMsThisWindow);
            Assert.AreEqual(0.0, log.MaxDurationMsThisWindow);

            log.Record(5u, 0.13, 0.001);
            log.Record(6u, 0.175, 0.0075);
            log.Record(7u, 0.195, 0.001);
            log.Record(8u, 0.2, 0.001);

            Assert.AreEqual(2, _lines.Count);
            Assert.AreEqual("tick window ticks=4 maxGapMs=45.0 maxDurationMs=7.5", _lines[1]);
        }

        [Test]
        public void test_tick_log_gap_across_window_boundary_counts_in_new_window()
        {
            // Arrange
            TickTimestampLog log = NewLog(summaryInterval: 2);

            // Act
            log.Record(1u, 0.0, 0.001);
            log.Record(2u, 0.01, 0.001);
            log.Record(3u, 0.04, 0.001);

            // Assert
            Assert.AreEqual(1, _lines.Count);
            StringAssert.StartsWith("tick window ticks=2 maxGapMs=10.0", _lines[0]);
            Assert.AreEqual(30.0, log.MaxGapMsThisWindow, 0.001);
        }

        [Test]
        public void test_tick_log_uses_invariant_culture_under_comma_decimal_culture()
        {
            // Arrange
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                TickTimestampLog log = NewLog();

                // Act
                log.Record(1u, 1.2345, 0.0015);
                log.Record(2u, 1.25, 0.0015);

                // Assert
                string[] written = WrittenLines();
                Assert.AreEqual("1,1234.500,1.500", written[1]);
                Assert.AreEqual("2,1250.000,1.500", written[2]);
                Assert.AreEqual(0, _lines.Count);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void test_tick_log_constructor_rejects_null_writer_and_bad_thresholds()
        {
            Assert.Throws<ArgumentNullException>(() => new TickTimestampLog(null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickTimestampLog(_writer, null, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickTimestampLog(_writer, null, 50.0, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickTimestampLog(_writer, null, 50.0, 100.0, 0));
        }

        // ---- exact boundaries (strictly greater for duration, greater-or-equal for the gap) ----

        [Test]
        public void test_tick_log_duration_exactly_at_budget_does_not_warn()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act: 0.05 s is exactly 50 ms
            log.Record(1u, 1.0, 0.05);

            // Assert
            Assert.AreEqual(0, _lines.Count);
            Assert.AreEqual(0, log.TotalOverBudgetCount);
        }

        [Test]
        public void test_tick_log_duration_just_above_budget_warns_with_the_duration_prefix()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            log.Record(1u, 1.0, 0.0501);

            // Assert
            Assert.AreEqual(1, _lines.Count);
            StringAssert.StartsWith(TickTimestampLog.DURATION_WARNING_PREFIX, _lines[0]);
            Assert.AreEqual(1, log.TotalOverBudgetCount);
        }

        [Test]
        public void test_tick_log_gap_exactly_at_threshold_warns_with_the_gap_prefix()
        {
            // Arrange
            TickTimestampLog log = NewLog();
            log.Record(1u, 0.0, SHORT_TICK_SECONDS);

            // Act: 0.1 s is exactly 100 ms and the rule is >=
            log.Record(2u, 0.1, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(1, _lines.Count);
            StringAssert.StartsWith(TickTimestampLog.GAP_WARNING_PREFIX, _lines[0]);
            Assert.AreEqual(1, log.TotalMissedTickCount);
        }

        [Test]
        public void test_tick_log_gap_just_below_threshold_does_not_warn()
        {
            // Arrange
            TickTimestampLog log = NewLog();
            log.Record(1u, 0.0, SHORT_TICK_SECONDS);

            // Act
            log.Record(2u, 0.0999, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(0, _lines.Count);
            Assert.AreEqual(0, log.TotalMissedTickCount);
        }

        [Test]
        public void test_tick_log_summary_is_emitted_exactly_at_the_default_interval_and_not_before()
        {
            // Arrange
            TickTimestampLog log = NewLog();

            // Act
            for (uint tick = 1u; tick < TickTimestampLog.DEFAULT_SUMMARY_INTERVAL_TICKS; tick++)
            {
                log.Record(tick, tick * 0.05, SHORT_TICK_SECONDS);
            }

            int linesBefore = _lines.Count;
            log.Record((uint)TickTimestampLog.DEFAULT_SUMMARY_INTERVAL_TICKS, TickTimestampLog.DEFAULT_SUMMARY_INTERVAL_TICKS * 0.05, SHORT_TICK_SECONDS);

            // Assert
            Assert.AreEqual(0, linesBefore, "No summary at tick 1199.");
            Assert.AreEqual(1, _lines.Count);
            StringAssert.StartsWith(TickTimestampLog.SUMMARY_PREFIX, _lines[0]);
        }
    }
}
