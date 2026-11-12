using System;
using System.Globalization;
using System.IO;

namespace IronGrind.Networking
{
    /// <summary>
    /// Dev-only per-tick timestamp log for the zone server (Authentication Story 017, AC-AUTH-05b). Writes one CSV line
    /// <c>tick,ms,durationMs</c> per <see cref="Record"/>: the tick's start time and how long the tick took to process.
    /// It warns on a tick whose duration exceeds the tick budget (AC-AUTH-05a: "the zone tick completes within its
    /// 50 ms budget") and on a gap between tick starts large enough to mean a whole tick was missed; it emits a window
    /// summary every N records. Gaps around the nominal period do not warn: at 20 Hz the nominal gap is itself 50 ms,
    /// and ticks fire on frame boundaries, so gaps of 50–60 ms are normal. Pure C#: it does not own the writer (the
    /// caller disposes it) and never throws out of <see cref="Record"/> because of an I/O failure.
    /// </summary>
    public sealed class TickTimestampLog
    {
        /// <summary>Default tick processing budget in milliseconds; a longer tick warns (AC-AUTH-05a).</summary>
        public const double TICK_DURATION_BUDGET_MS = 50.0;

        /// <summary>
        /// Default gap between tick starts, in milliseconds, at or above which a tick counts as missed: two periods at
        /// 20 Hz.
        /// </summary>
        public const double MISSED_TICK_GAP_MS = 100.0;

        /// <summary>Default summary interval in records: 60 s at 20 Hz.</summary>
        public const int DEFAULT_SUMMARY_INTERVAL_TICKS = 1200;

        /// <summary>Prefix of the warning emitted for a tick over the duration budget.</summary>
        public const string DURATION_WARNING_PREFIX = "tick duration ";

        /// <summary>Prefix of the warning emitted for a missed tick.</summary>
        public const string GAP_WARNING_PREFIX = "tick gap ";

        /// <summary>Prefix of the window summary line.</summary>
        public const string SUMMARY_PREFIX = "tick window ";

        private const double MS_PER_SECOND = 1000.0;
        private const string HEADER = "tick,ms,durationMs";
        private const string MS_FORMAT = "F3";
        private const string SUMMARY_FORMAT = "F1";

        private readonly TextWriter _writer;
        private readonly Action<string> _sink;
        private readonly double _durationBudgetMs;
        private readonly double _missedTickGapMs;
        private readonly int _summaryIntervalTicks;
        private bool _headerWritten;
        private bool _hasPrevious;
        private double _previousSeconds;
        private int _windowTicks;
        private bool _writerFailed;

        /// <summary>Creates a log.</summary>
        /// <param name="writer">Receives the CSV lines. Not owned: the caller flushes and disposes it.</param>
        /// <param name="warningSink">Receives warnings and window summaries (may be null).</param>
        /// <param name="durationBudgetMs">A tick duration strictly greater than this many milliseconds warns.</param>
        /// <param name="missedTickGapMs">A gap between tick starts of at least this many milliseconds warns.</param>
        /// <param name="summaryIntervalTicks">Records per summary window; must be at least 1.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A threshold or interval is not positive.</exception>
        public TickTimestampLog(
            TextWriter writer,
            Action<string> warningSink,
            double durationBudgetMs = TICK_DURATION_BUDGET_MS,
            double missedTickGapMs = MISSED_TICK_GAP_MS,
            int summaryIntervalTicks = DEFAULT_SUMMARY_INTERVAL_TICKS)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            if (durationBudgetMs <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationBudgetMs));
            }

            if (missedTickGapMs <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(missedTickGapMs));
            }

            if (summaryIntervalTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(summaryIntervalTicks));
            }

            _sink = warningSink;
            _durationBudgetMs = durationBudgetMs;
            _missedTickGapMs = missedTickGapMs;
            _summaryIntervalTicks = summaryIntervalTicks;
        }

        /// <summary>Largest gap between tick starts in the current summary window, in ms (0 before a second record).</summary>
        public double MaxGapMsThisWindow { get; private set; }

        /// <summary>Largest tick duration in the current summary window, in milliseconds.</summary>
        public double MaxDurationMsThisWindow { get; private set; }

        /// <summary>Total number of ticks whose duration exceeded the budget since creation.</summary>
        public int TotalOverBudgetCount { get; private set; }

        /// <summary>Total number of gaps at or above the missed-tick threshold since creation.</summary>
        public int TotalMissedTickCount { get; private set; }

        /// <summary>Records one tick. Writes the header before the first line.</summary>
        /// <param name="tick">The server tick number.</param>
        /// <param name="startSeconds">The time the tick started, in seconds (monotonic).</param>
        /// <param name="durationSeconds">How long the tick took to process, in seconds.</param>
        public void Record(uint tick, double startSeconds, double durationSeconds)
        {
            double durationMs = durationSeconds * MS_PER_SECOND;
            WriteLine(tick, startSeconds * MS_PER_SECOND, durationMs);

            if (durationMs > MaxDurationMsThisWindow)
            {
                MaxDurationMsThisWindow = durationMs;
            }

            if (durationMs > _durationBudgetMs)
            {
                TotalOverBudgetCount++;
                Emit(DURATION_WARNING_PREFIX + durationMs.ToString(SUMMARY_FORMAT, CultureInfo.InvariantCulture)
                    + " ms at tick=" + tick.ToString(CultureInfo.InvariantCulture));
            }

            if (_hasPrevious)
            {
                double gapMs = (startSeconds - _previousSeconds) * MS_PER_SECOND;
                if (gapMs > MaxGapMsThisWindow)
                {
                    MaxGapMsThisWindow = gapMs;
                }

                if (gapMs >= _missedTickGapMs)
                {
                    TotalMissedTickCount++;
                    Emit(GAP_WARNING_PREFIX + gapMs.ToString(SUMMARY_FORMAT, CultureInfo.InvariantCulture)
                        + " ms at tick=" + tick.ToString(CultureInfo.InvariantCulture));
                }
            }

            _hasPrevious = true;
            _previousSeconds = startSeconds;
            _windowTicks++;
            if (_windowTicks >= _summaryIntervalTicks)
            {
                Emit(SUMMARY_PREFIX + "ticks=" + _windowTicks.ToString(CultureInfo.InvariantCulture)
                    + " maxGapMs=" + MaxGapMsThisWindow.ToString(SUMMARY_FORMAT, CultureInfo.InvariantCulture)
                    + " maxDurationMs=" + MaxDurationMsThisWindow.ToString(SUMMARY_FORMAT, CultureInfo.InvariantCulture));
                _windowTicks = 0;
                MaxGapMsThisWindow = 0.0;
                MaxDurationMsThisWindow = 0.0;
                Flush();
            }
        }

        /// <summary>Flushes the writer; an I/O failure is swallowed and stops further writes.</summary>
        public void Flush()
        {
            if (_writerFailed)
            {
                return;
            }

            try
            {
                _writer.Flush();
            }
            catch (Exception)
            {
                _writerFailed = true;
            }
        }

        private void WriteLine(uint tick, double ms, double durationMs)
        {
            if (_writerFailed)
            {
                return;
            }

            try
            {
                if (!_headerWritten)
                {
                    _writer.WriteLine(HEADER);
                    _headerWritten = true;
                }

                _writer.WriteLine(tick.ToString(CultureInfo.InvariantCulture) + ","
                    + ms.ToString(MS_FORMAT, CultureInfo.InvariantCulture) + ","
                    + durationMs.ToString(MS_FORMAT, CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                _writerFailed = true;
            }
        }

        private void Emit(string line)
        {
            _sink?.Invoke(line);
        }
    }
}
