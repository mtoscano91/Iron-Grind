using System;
using System.Collections;
using System.Collections.Generic;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The zone server composition root, lifecycle and teardown part (ADR-009 Amendment 1 Decisions 3 and 5,
    /// Networking Core Story 042). A trigger only calls <see cref="RequestTeardown"/>; the host's Update
    /// starts <see cref="RunTeardown"/> once when <see cref="TryBeginTeardown"/> returns true. Startup
    /// (Story 043) and the real object graph (Story 044) are added later.
    /// </summary>
    /// <remarks>
    /// Main thread only (the tick thread). Teardown order: 1 tick unsubscribe, 2a listener stop, 3 navigation,
    /// 4 CR-ZI-12, 4b outbound flush, 5 queue drain, 6 transport stop + NGO shutdown + wait, 7 Dispose pass,
    /// 8 quit. Each step has its own try/catch; a failure logs the step and exception type name only and the
    /// teardown goes on; the exit code is never changed by a failure. A destroyed NGO manager
    /// (<see cref="IZoneNetwork.IsAlive"/> false) skips the tick unsubscribe and the NGO shutdown.
    /// <para>
    /// Internally the non-yielding steps are numbered 1-8 (<c>_step</c>, <see cref="RunStepsForTest"/>):
    /// internal 1 = ADR 1, 2 = 2a, 3 = 3, 4 = 4, 5 = 4b, 6 = 5, 7 = 6 (without the wait), 8 = 7. ADR step 8
    /// (quit) and the step-6 wait live only in <see cref="RunTeardown"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var composition = new ZoneServerComposition(app, network, navigation, closeSteps, 100);
    /// app.SubscribeWantsToQuit(composition.OnWantsToQuit);
    /// // host Update:
    /// if (composition.TryBeginTeardown()) { StartCoroutine(composition.RunTeardown()); }
    /// // host OnDestroy:
    /// composition.TeardownNow();
    /// </code>
    /// </example>
    public sealed class ZoneServerComposition : IDisposable
    {
        private const string LOG_PREFIX = "[ZoneServer]";
        private const int LAST_INTERNAL_STEP = 8;
        private const int TRANSPORT_AND_NGO_STEP = 7;

        // Internal step numbers 1..8 and the names the ADR gives them (used in logs).
        private static readonly string[] STEP_NAMES = { "", "1", "2a", "3", "4", "4b", "5", "6", "7" };

        // The watchdog duration (ADR-011 Decision 2): 200 ticks / 20 Hz = 10 s.
        private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromSeconds(
            (double)TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS / ServerTickLoop.TICK_RATE_HZ);

        private readonly IZoneApplication _application;
        private readonly IZoneNetwork _network;
        private readonly IZoneNavigation _navigation;
        private readonly IZoneCloseSteps _closeSteps;
        private readonly int _ngoShutdownWaitFrames;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        private ITickCompletionQueue _queue;
        private IZoneOutboundStep _outbound;
        private IZoneRelayListener _listener;
        private IZoneInboundTransport _transport;
        private Func<uint> _currentTick;
        private Action _tickHandler;

        private bool _teardownRequested;
        private bool _beginConsumed;
        private ZoneExitCode _exitCode = ZoneExitCode.Clean;
        private int _step;
        private bool _runStarted;
        private bool _syncDone;
        private bool _quitAllowed;

        /// <summary>Creates the composition around its Unity-facing seams.</summary>
        /// <param name="application">Application seam. May be null in tests of other steps.</param>
        /// <param name="network">NGO seam. May be null (a missing manager skips its steps).</param>
        /// <param name="navigation">Navigation seam (a stand-in until its epic ships). May be null.</param>
        /// <param name="closeSteps">CR-ZI-12 seam (a stand-in until its epic ships). May be null.</param>
        /// <param name="ngoShutdownWaitFrames">Server config NGO_SHUTDOWN_WAIT_FRAMES; must be greater than 0.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ngoShutdownWaitFrames"/> is not positive.</exception>
        public ZoneServerComposition(
            IZoneApplication application,
            IZoneNetwork network,
            IZoneNavigation navigation,
            IZoneCloseSteps closeSteps,
            int ngoShutdownWaitFrames)
        {
            if (ngoShutdownWaitFrames <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ngoShutdownWaitFrames), "Must be greater than 0.");
            }

            _application = application;
            _network = network;
            _navigation = navigation;
            _closeSteps = closeSteps;
            _ngoShutdownWaitFrames = ngoShutdownWaitFrames;
        }

        /// <summary>
        /// Records the first exit code and sets the request flag; ignores later codes. Tears nothing down.
        /// Safe to call any number of times from any state.
        /// </summary>
        /// <param name="code">The exit code to quit with.</param>
        public void RequestTeardown(ZoneExitCode code)
        {
            if (_teardownRequested)
            {
                return;
            }

            _exitCode = code;
            _teardownRequested = true;
        }

        /// <summary>
        /// Host contract: true exactly once, on the first call after <see cref="RequestTeardown"/>; false
        /// before any request and on every later call.
        /// </summary>
        /// <returns>True when the host should start <see cref="RunTeardown"/> now.</returns>
        public bool TryBeginTeardown()
        {
            if (!_teardownRequested || _beginConsumed)
            {
                return false;
            }

            _beginConsumed = true;
            return true;
        }

        /// <summary>
        /// Teardown steps 1-8 as a coroutine; only the NGO shutdown wait of step 6 yields. A second call, or a
        /// call after <see cref="TeardownNow"/>/<see cref="Dispose"/>, yields nothing and does nothing.
        /// </summary>
        /// <returns>The coroutine for the host to run.</returns>
        public IEnumerator RunTeardown()
        {
            if (_runStarted || _syncDone)
            {
                yield break;
            }

            _runStarted = true;

            for (int n = 1; n <= 7; n++)
            {
                RunStep(n);
            }

            // The wait sits outside any try/catch (C# forbids yield inside a try with a catch).
            int frames = 0;
            while (!_syncDone && frames < _ngoShutdownWaitFrames && IsShutdownPending())
            {
                frames++;
                yield return null;
            }

            if (_syncDone)
            {
                yield break;
            }

            if (frames >= _ngoShutdownWaitFrames && IsShutdownPending())
            {
                Debug.LogWarning(LOG_PREFIX + " NGO shutdown still in progress after "
                    + _ngoShutdownWaitFrames + " frames; going on.");
            }

            RunStep(LAST_INTERNAL_STEP);

            _quitAllowed = true;
            try
            {
                _application?.Quit((int)_exitCode);
            }
            catch (Exception ex)
            {
                Debug.LogError(LOG_PREFIX + " teardown step 8 failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// Steps 1-7 synchronously, for the host's OnDestroy: step 6 issues the NGO shutdown without waiting
        /// and the process is never quit. Runs only the steps not yet done; does nothing when finished.
        /// </summary>
        public void TeardownNow()
        {
            if (_syncDone)
            {
                return;
            }

            for (int n = 1; n <= LAST_INTERNAL_STEP; n++)
            {
                RunStep(n);
            }

            _syncDone = true;
        }

        /// <summary>Same as <see cref="TeardownNow"/>: steps 1-7, no quit, idempotent.</summary>
        public void Dispose()
        {
            TeardownNow();
        }

        /// <summary>
        /// Application.wantsToQuit handler. True in the Editor and once teardown step 8 set the quit flag;
        /// otherwise requests a Clean teardown and returns false. Does no teardown work itself.
        /// </summary>
        /// <returns>True when the application may quit now.</returns>
        public bool OnWantsToQuit()
        {
            if (_application != null && _application.IsEditor)
            {
                return true;
            }

            if (_quitAllowed)
            {
                return true;
            }

            RequestTeardown(ZoneExitCode.Clean);
            return false;
        }

        /// <summary>
        /// Appends an object to the Dispose pass (teardown step 7), which disposes in reverse order. Register
        /// the data source first so it is disposed last. Story 044 calls this from startup S4/S5.
        /// </summary>
        /// <param name="disposable">The object to dispose; null is ignored.</param>
        internal void RegisterDisposable(IDisposable disposable)
        {
            if (disposable != null)
            {
                _disposables.Add(disposable);
            }
        }

        /// <summary>
        /// Sets the owned objects the teardown touches. Story 043/044 call this from startup once they have
        /// built the objects; tests call it with fakes. Any argument may be null (its step is skipped).
        /// </summary>
        /// <param name="queue">Completion queue drained at step 5.</param>
        /// <param name="outbound">Outbound step flushed once at step 4b.</param>
        /// <param name="listener">Relay listener stopped at step 2a.</param>
        /// <param name="transport">Inbound transport stopped at step 6.</param>
        /// <param name="currentTick">Source of the last tick number passed to the flush.</param>
        /// <param name="tickHandler">The delegate subscribed to the network tick; step 1 unsubscribes it.</param>
        internal void AttachOwnedObjects(
            ITickCompletionQueue queue,
            IZoneOutboundStep outbound,
            IZoneRelayListener listener,
            IZoneInboundTransport transport,
            Func<uint> currentTick,
            Action tickHandler)
        {
            _queue = queue;
            _outbound = outbound;
            _listener = listener;
            _transport = transport;
            _currentTick = currentTick;
            _tickHandler = tickHandler;
        }

        /// <summary>
        /// Test hook: runs the non-yielding steps 1..<paramref name="lastStep"/> (internal numbering: 1 tick
        /// unsubscribe, 2 listener, 3 navigation, 4 close steps, 5 flush, 6 drain, 7 transport + NGO shutdown,
        /// 8 Dispose pass) to simulate a teardown stopped part-way.
        /// </summary>
        /// <param name="lastStep">The last internal step to run, 1 to 8.</param>
        internal void RunStepsForTest(int lastStep)
        {
            for (int n = 1; n <= lastStep && n <= LAST_INTERNAL_STEP; n++)
            {
                RunStep(n);
            }
        }

        private bool IsShutdownPending()
        {
            try
            {
                return IsNetworkAlive() && _network.ShutdownInProgress;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsNetworkAlive()
        {
            return _network != null && _network.IsAlive;
        }

        private void RunStep(int n)
        {
            if (_step >= n)
            {
                return;
            }

            // Marked before the body runs, so a step that re-enters TeardownNow() is not run a second time.
            _step = n;
            try
            {
                RunStepBody(n);
            }
            catch (Exception ex)
            {
                LogStepFailure(n, ex);
            }
        }

        private void RunStepBody(int n)
        {
            switch (n)
            {
                case 1:
                    UnsubscribeTick();
                    break;
                case 2:
                    _listener?.Stop();
                    break;
                case 3:
                    _navigation?.Teardown();
                    break;
                case 4:
                    _closeSteps?.RunCloseSteps();
                    break;
                case 5:
                    FlushOutbound();
                    break;
                case 6:
                    _queue?.DrainOnShutdown(DRAIN_TIMEOUT);
                    break;
                case TRANSPORT_AND_NGO_STEP:
                    StopTransportAndShutDownNetwork();
                    break;
                case LAST_INTERNAL_STEP:
                    DisposePass();
                    break;
            }
        }

        private static void LogStepFailure(int n, Exception ex)
        {
            Debug.LogError(LOG_PREFIX + " teardown step " + STEP_NAMES[n] + " failed: " + ex.GetType().Name);
        }

        private void UnsubscribeTick()
        {
            if (_tickHandler != null && IsNetworkAlive())
            {
                _network.UnsubscribeTick(_tickHandler);
            }
        }

        private void FlushOutbound()
        {
            if (_outbound != null)
            {
                _outbound.Flush(_currentTick != null ? _currentTick() : 0u);
            }
        }

        // The transport stop has its own try so a failure there never skips the NGO shutdown.
        private void StopTransportAndShutDownNetwork()
        {
            try
            {
                _transport?.Stop();
            }
            catch (Exception ex)
            {
                LogStepFailure(TRANSPORT_AND_NGO_STEP, ex);
            }

            if (IsNetworkAlive())
            {
                _network.Shutdown();
            }
        }

        private void DisposePass()
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i].Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogError(LOG_PREFIX + " teardown step 7 Dispose failed: " + ex.GetType().Name);
                }
            }

            _disposables.Clear();
        }
    }
}
