#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Text;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Development-only probe for Story 045 AC-9 (ADR-009 A1-4, first measurement): counts
    /// <c>NetworkTickSystem.Tick</c> events per second of real time and builds a histogram of tick events per
    /// frame (events are grouped by <c>Time.frameCount</c>; frames with zero events are the frames elapsed
    /// minus the frames that had events). Compiled out of release builds. Main thread only.
    /// </summary>
    /// <remarks>
    /// The first second after the first tick is a warm-up: NGO catches up once after <c>StartServer</c> (22-23
    /// ticks and one 3-tick frame were seen in the 2026-10-22 runs), so those ticks are logged once as
    /// <c>[ZoneTickProbe] warm-up second excluded: ticks=K</c> and left out of the measurement (user decision
    /// 2026-10-22, Story 045). The measurement starts on the first frame after the warm-up.
    /// Logs <c>[ZoneTickProbe] second N: ticks=K</c> once per second, the histogram
    /// (<c>[ZoneTickProbe] ticks-per-frame histogram: 0=a 1=b 2=c ...</c>) every 10 s and from
    /// <see cref="Report"/>. Allocates only when it logs.
    /// </remarks>
    /// <example>
    /// <code>
    /// var probe = new ZoneTickProbe();
    /// network.Tick += probe.OnTick; // named method
    /// probe.Report();               // at shutdown
    /// </code>
    /// </example>
    internal sealed class ZoneTickProbe
    {
        private const int HISTOGRAM_BUCKETS = 8; // 1..7 exact, last bucket = 8 or more (0 is computed)
        private const double SUMMARY_PERIOD_SECONDS = 1.0;
        private const int HISTOGRAM_EVERY_SECONDS = 10;
        private const double WARM_UP_SECONDS = 1.0;

        private readonly int[] _histogram = new int[HISTOGRAM_BUCKETS];
        private readonly StringBuilder _text = new StringBuilder(128);
        private bool _warmUpStarted;
        private bool _warmUpDone;
        private double _warmUpStart;
        private int _warmUpFrame;
        private int _warmUpTicks;
        private bool _started;
        private int _firstFrame;
        private int _currentFrame;
        private int _ticksThisFrame;
        private int _framesWithTicks;
        private double _secondStart;
        private int _secondTicks;
        private int _secondsLogged;

        /// <summary>The tick handler to subscribe to <c>NetworkTickSystem.Tick</c>; one call per tick event.</summary>
        public void OnTick()
        {
            int frame = Time.frameCount;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_warmUpDone)
            {
                if (!_warmUpStarted)
                {
                    _warmUpStarted = true;
                    _warmUpStart = now;
                    _warmUpFrame = frame;
                }

                // Ends only on a new frame, so no frame's ticks are split between warm-up and measurement.
                if (frame == _warmUpFrame || now - _warmUpStart < WARM_UP_SECONDS)
                {
                    _warmUpFrame = frame;
                    _warmUpTicks++;
                    return;
                }

                _warmUpDone = true;
                Debug.Log("[ZoneTickProbe] warm-up second excluded: ticks=" + _warmUpTicks);
            }

            if (!_started)
            {
                _started = true;
                _firstFrame = frame;
                _currentFrame = frame;
                _secondStart = now;
            }

            if (frame != _currentFrame)
            {
                CommitFrame();
                _currentFrame = frame;
                if (now - _secondStart >= SUMMARY_PERIOD_SECONDS)
                {
                    LogSecond();
                    _secondStart = now;
                    if (_secondsLogged % HISTOGRAM_EVERY_SECONDS == 0)
                    {
                        LogHistogram(frame);
                    }
                }
            }

            _ticksThisFrame++;
            _secondTicks++;
        }

        /// <summary>Logs the histogram now (called at shutdown). Safe before any tick.</summary>
        public void Report()
        {
            if (!_started)
            {
                return;
            }

            CommitFrame();
            LogHistogram(Time.frameCount + 1); // the current frame is included, so its ticks are not an off-by-one
        }

        private void CommitFrame()
        {
            if (_ticksThisFrame > 0)
            {
                int bucket = _ticksThisFrame < HISTOGRAM_BUCKETS ? _ticksThisFrame : HISTOGRAM_BUCKETS - 1;
                _histogram[bucket]++;
                _framesWithTicks++;
                _ticksThisFrame = 0;
            }
        }

        private void LogSecond()
        {
            _secondsLogged++;
            Debug.Log("[ZoneTickProbe] second " + _secondsLogged + ": ticks=" + _secondTicks);
            _secondTicks = 0;
        }

        // frameNow is exclusive: the frames counted are _firstFrame .. frameNow - 1 (all committed).
        private void LogHistogram(int frameNow)
        {
            int zero = frameNow - _firstFrame - _framesWithTicks;
            if (zero < 0)
            {
                zero = 0;
            }

            _text.Length = 0;
            _text.Append("[ZoneTickProbe] ticks-per-frame histogram: 0=").Append(zero);
            for (int i = 1; i < HISTOGRAM_BUCKETS; i++)
            {
                _text.Append(' ').Append(i == HISTOGRAM_BUCKETS - 1 ? ">=" : string.Empty).Append(i).Append('=').Append(_histogram[i]);
            }

            Debug.Log(_text.ToString());
        }
    }
}
#endif
