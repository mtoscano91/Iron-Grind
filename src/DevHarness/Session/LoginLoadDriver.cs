using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.Authentication;
using IronGrind.Client.Session;

namespace IronGrind.DevHarness.Session
{
    /// <summary>
    /// Load driver for Authentication Story 017 AC-AUTH-05a (Story 018 AC-10): keeps N credential logins in flight
    /// for D seconds and logs each login's latency. Logins only, no zone entry. Slot k always logs in as
    /// <c>usernamePrefix_k</c>, so concurrent logins never evict each other; the accounts must exist already.
    /// Drive it with <see cref="Update"/> every frame.
    /// </summary>
    public sealed class LoginLoadDriver
    {
        private const double MS_PER_SECOND = 1000.0;
        private const string LOG_TAG = "[LoginLoad]";

        private readonly IZoneLoginService _login;
        private readonly IClientSessionClock _clock;
        private readonly Action<string> _log;
        private readonly string _usernamePrefix;
        private readonly string _password;
        private readonly int _concurrency;
        private readonly double _durationSeconds;
        private readonly bool[] _slotBusy;
        private readonly double[] _slotStart;
        private readonly Action<LoginResponse>[] _slotCallbacks;
        private readonly List<double> _latenciesMs = new List<double>();

        private double _endTime;
        private bool _started;
        private int _inFlight;
        private int _completed;
        private int _okCount;

        /// <summary>Creates the driver.</summary>
        /// <param name="login">Login seam.</param>
        /// <param name="clock">Monotonic clock (latency and duration).</param>
        /// <param name="log">Line sink (may be null). Never receives the password.</param>
        /// <param name="usernamePrefix">Accounts are <c>usernamePrefix_0</c> to <c>usernamePrefix_(N-1)</c>.</param>
        /// <param name="password">Password shared by the accounts; not logged.</param>
        /// <param name="concurrency">N, logins in flight at once (at least 1).</param>
        /// <param name="durationSeconds">D, seconds to keep launching logins (above 0).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">N or D is not positive.</exception>
        public LoginLoadDriver(
            IZoneLoginService login,
            IClientSessionClock clock,
            Action<string> log,
            string usernamePrefix,
            string password,
            int concurrency,
            double durationSeconds)
        {
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _usernamePrefix = usernamePrefix ?? throw new ArgumentNullException(nameof(usernamePrefix));
            _password = password ?? throw new ArgumentNullException(nameof(password));
            if (concurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(concurrency));
            }

            if (durationSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }

            _concurrency = concurrency;
            _durationSeconds = durationSeconds;
            _slotBusy = new bool[concurrency];
            _slotStart = new double[concurrency];
            _slotCallbacks = new Action<LoginResponse>[concurrency];
            for (int k = 0; k < concurrency; k++)
            {
                int slot = k;
                _slotCallbacks[k] = response => OnLoginDone(slot, response);
            }
        }

        /// <summary>True once the duration elapsed, every login finished and the summary was logged.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Starts the run.</summary>
        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _endTime = _clock.Now + _durationSeconds;
            Write(LOG_TAG + " start n=" + _concurrency + " d=" + _durationSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s");
        }

        /// <summary>Launches logins into free slots while time remains; logs the summary when all is done.</summary>
        public void Update()
        {
            if (!_started || IsFinished)
            {
                return;
            }

            if (_clock.Now < _endTime)
            {
                for (int k = 0; k < _concurrency; k++)
                {
                    if (!_slotBusy[k])
                    {
                        Launch(k);
                    }
                }
            }
            else if (_inFlight == 0)
            {
                IsFinished = true;
                LogSummary();
            }
        }

        private void Launch(int slot)
        {
            _slotBusy[slot] = true;
            _inFlight++;
            _slotStart[slot] = _clock.Now;
            _login.Login(_usernamePrefix + "_" + slot.ToString(CultureInfo.InvariantCulture), _password, null, _slotCallbacks[slot]);
        }

        private void OnLoginDone(int slot, LoginResponse response)
        {
            if (!_slotBusy[slot])
            {
                return;
            }

            double ms = (_clock.Now - _slotStart[slot]) * MS_PER_SECOND;
            _slotBusy[slot] = false;
            _inFlight--;
            _latenciesMs.Add(ms);
            if (response.Success)
            {
                _okCount++;
            }

            Write(LOG_TAG + " i=" + _completed + " ms=" + ms.ToString("F1", CultureInfo.InvariantCulture)
                + " ok=" + (response.Success ? "true" : "false"));
            _completed++;
        }

        private void LogSummary()
        {
            if (_latenciesMs.Count == 0)
            {
                Write(LOG_TAG + " summary count=0 ok=0");
                return;
            }

            var sorted = new List<double>(_latenciesMs);
            sorted.Sort();
            int n = sorted.Count;
            double median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
            Write(LOG_TAG + " summary count=" + n + " ok=" + _okCount
                + " min=" + sorted[0].ToString("F1", CultureInfo.InvariantCulture)
                + " median=" + median.ToString("F1", CultureInfo.InvariantCulture)
                + " max=" + sorted[n - 1].ToString("F1", CultureInfo.InvariantCulture) + " ms");
        }

        private void Write(string line)
        {
            _log?.Invoke(line);
        }
    }
}
