using System;
using System.Diagnostics;
using System.Threading;
using Npgsql;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace IronGrind.Networking
{
    /// <summary>
    /// Verification host for Networking Core Story 041 (ADR-015 Verification 0, 1, 3 and 5). <b>This is a test call
    /// site, not the composition root</b>: the production wiring of the publisher and listener, and the ADR-009
    /// teardown calls to <c>NpgsqlRelayListener.Stop()</c>, belong to the composition-root story (open item C2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Server-only (the assembly is constrained to <c>UNITY_SERVER || UNITY_EDITOR</c>). Put it on one GameObject in
    /// <c>Assets/Scenes/Verification/RelayVerification.unity</c> and run two processes against one PostgreSQL.
    /// </para>
    /// <para>
    /// <b>Environment.</b> <c>IRONGRIND_DEV_PG</c>: an Npgsql connection string (never logged: it holds a password).
    /// For VR-5 the string must carry a small <c>Maximum Pool Size</c> (for example 2) so that
    /// <c>-relayHoldPoolConnections 2</c> exhausts the pool.
    /// </para>
    /// <para>
    /// <b>Command line.</b> <c>-relayZone &lt;name&gt;</c> (required); <c>-relayPublishIntervalSec &lt;n&gt;</c>
    /// (default 10, 0 = never publish); <c>-relayHoldPoolConnections &lt;n&gt;</c> (default 0, VR-5; clamped to the
    /// pool size); <c>-relayTickHz &lt;n&gt;</c> (default 30).
    /// </para>
    /// <para>
    /// <b>Threads.</b> Everything except <see cref="LoggingInbox.TryAdd"/> runs on the main thread. The inbox
    /// decorator runs on the listener thread and logs its managed thread id; the relayed-message handler logs the main
    /// thread's id, so a log shows the notification crossing from the listener thread to the tick.
    /// </para>
    /// <para>
    /// Ownership: the host owns the <see cref="NpgsqlDataSource"/> (the publisher does not dispose it) and disposes it
    /// in <see cref="OnDestroy"/> after stopping the listener.
    /// </para>
    /// </remarks>
    public sealed class RelayVerificationHost : MonoBehaviour
    {
        private const string LOG_PREFIX = "[RelayVerificationHost]";
        private const string ENV_CONNECTION = "IRONGRIND_DEV_PG";
        private const int DEFAULT_PUBLISH_INTERVAL_SEC = 10;
        private const int DEFAULT_TICK_HZ = 30;
        private const double SLOW_TICK_MS = 50.0;
        private const long SUMMARY_PERIOD_MS = 1000;

        private string _zone;
        private int _publishIntervalSec;
        private int _tickHz;
        private double _tickSeconds;
        private double _accumulator;
        private uint _tick;
        private int _mainThreadId;
        private int _publishCounter;
        private double _nextPublishAt;

        private NpgsqlDataSource _dataSource;
        private NpgsqlConnection[] _held = Array.Empty<NpgsqlConnection>();
        private TickCompletionQueue _queue;
        private CrossZoneRelayInbox _inbox;
        private ServerWideBroadcaster _broadcaster;
        private NpgsqlRelayListener _listener;
        private RelayHandler _handler;
        private readonly byte[] _bodyBuffer = new byte[ServerBroadcast_Enhancement9.MaxBodySize];

        private readonly Stopwatch _tickWatch = new Stopwatch();
        private readonly Stopwatch _summaryWatch = new Stopwatch();
        private double _maxTickMs;
        private double _sumTickMs;
        private int _tickCount;

        private void Start()
        {
            string connectionString = Environment.GetEnvironmentVariable(ENV_CONNECTION);
            if (string.IsNullOrEmpty(connectionString))
            {
                Debug.LogError($"{LOG_PREFIX} {ENV_CONNECTION} is not set; host disabled.");
                enabled = false;
                return;
            }

            _zone = GetArg("-relayZone");
            if (string.IsNullOrEmpty(_zone))
            {
                Debug.LogError($"{LOG_PREFIX} -relayZone <name> is required; host disabled.");
                enabled = false;
                return;
            }

            _publishIntervalSec = GetIntArg("-relayPublishIntervalSec", DEFAULT_PUBLISH_INTERVAL_SEC);
            _tickHz = Math.Max(1, GetIntArg("-relayTickHz", DEFAULT_TICK_HZ));
            int hold = Math.Max(0, GetIntArg("-relayHoldPoolConnections", 0));
            _tickSeconds = 1.0 / _tickHz;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = _tickHz;

            try
            {
                var allowlist = CrossZoneRelayAllowlist.CreateProduction();
                var origin = new CrossZoneRelayOrigin();
                _dataSource = NpgsqlDataSource.Create(connectionString);
                _queue = new TickCompletionQueue();   // main thread: the tick thread
                _inbox = new CrossZoneRelayInbox(allowlist.LargestMaxBodyBytes);
                var receiver = new CrossZoneRelayReceiver(allowlist, new LoggingInbox(_inbox, _zone), origin);
                var publisher = new NpgsqlRelayPublisher(_dataSource, _queue, origin);
                _broadcaster = new ServerWideBroadcaster(new LoggingSink(_zone), publisher, allowlist);
                _handler = new RelayHandler(_zone, _mainThreadId);

                if (hold > 0)
                {
                    HoldPoolConnections(connectionString, hold);
                }

                _listener = new NpgsqlRelayListener(connectionString, receiver);
                _listener.Start();
                Debug.Log($"{LOG_PREFIX} Started: zone={_zone} origin={origin.Hex} mainThread={_mainThreadId} tickHz={_tickHz} publishEverySec={_publishIntervalSec} heldConnections={_held.Length}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} StartFailed: {ex.GetType().Name}: {ex.Message}; host disabled.");
                enabled = false;
                return;
            }

            _nextPublishAt = Time.realtimeSinceStartupAsDouble + Math.Max(1, _publishIntervalSec);
            _summaryWatch.Start();
        }

        private void HoldPoolConnections(string connectionString, int requested)
        {
            int maxPool = new NpgsqlConnectionStringBuilder(connectionString).MaxPoolSize;
            int count = Math.Min(requested, maxPool);
            if (count < requested)
            {
                Debug.LogWarning($"{LOG_PREFIX} HoldClamped: requested={requested} maxPoolSize={maxPool}");
            }

            _held = new NpgsqlConnection[count];
            for (int i = 0; i < count; i++)
            {
                _held[i] = _dataSource.OpenConnection();   // verification only: startup, not the tick
            }

            Debug.Log($"{LOG_PREFIX} PoolHeld: held={count} maxPoolSize={maxPool}; further pooled opens wait for a free connection.");
        }

        private void Update()
        {
            _accumulator += Time.unscaledDeltaTime;
            int guard = 0;
            while (_accumulator >= _tickSeconds && guard++ < 5)
            {
                _accumulator -= _tickSeconds;
                RunTick();
            }

            if (guard >= 5)
            {
                _accumulator = 0;
            }

            if (_summaryWatch.ElapsedMilliseconds >= SUMMARY_PERIOD_MS && _tickCount > 0)
            {
                Debug.Log($"{LOG_PREFIX} TickSummary: zone={_zone} ticks={_tickCount} maxMs={_maxTickMs:F3} avgMs={_sumTickMs / _tickCount:F3} inFlightPublishes={_queue.InFlightCount} inboxCount={_inbox.Count}");
                _maxTickMs = 0;
                _sumTickMs = 0;
                _tickCount = 0;
                _summaryWatch.Restart();
            }
        }

        private void RunTick()
        {
            _tick++;
            _tickWatch.Restart();

            _queue.Drain(_tick);
            _inbox.Drain(_tick, _handler);
            PublishIfDue();

            _tickWatch.Stop();
            double ms = _tickWatch.Elapsed.TotalMilliseconds;
            _sumTickMs += ms;
            _tickCount++;
            if (ms > _maxTickMs)
            {
                _maxTickMs = ms;
            }

            if (ms > SLOW_TICK_MS)
            {
                Debug.LogWarning($"{LOG_PREFIX} SlowTick: zone={_zone} tick={_tick} ms={ms:F3}");
            }
        }

        private void PublishIfDue()
        {
            if (_publishIntervalSec <= 0 || Time.realtimeSinceStartupAsDouble < _nextPublishAt)
            {
                return;
            }

            _nextPublishAt += _publishIntervalSec;
            _publishCounter++;
            string player = _zone + "-" + _publishCounter;   // the counter makes a duplicate delivery visible
            int length = ServerBroadcast_Enhancement9Codec.WriteBody(_bodyBuffer, player, "Test Sword");
            Debug.Log($"{LOG_PREFIX} Publishing: zone={_zone} tick={_tick} player=\"{player}\"");
            _broadcaster.Broadcast(ServerBroadcast_Enhancement9.MessageTypeId, new ReadOnlySpan<byte>(_bodyBuffer, 0, length));
        }

        private void OnDestroy()
        {
            try
            {
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} StopFailed: {ex.GetType().Name}: {ex.Message}");
            }

            for (int i = 0; i < _held.Length; i++)
            {
                try
                {
                    _held[i]?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LOG_PREFIX} HeldDisposeFailed: {ex.GetType().Name}");
                }
            }

            _held = Array.Empty<NpgsqlConnection>();
            try
            {
                _dataSource?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_PREFIX} DataSourceDisposeFailed: {ex.GetType().Name}");
            }

            _dataSource = null;
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static int GetIntArg(string name, int fallback)
        {
            string value = GetArg(name);
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        /// <summary>Logs each local delivery; stands in for the zone's client outbox.</summary>
        private sealed class LoggingSink : IZoneClientBroadcastSink
        {
            private readonly string _zone;

            public LoggingSink(string zone)
            {
                _zone = zone;
            }

            public void EnqueueToAllClients(ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                ServerBroadcast_Enhancement9Codec.TryReadBody(body, out string player, out string item);
                Debug.Log($"{LOG_PREFIX} LocalDelivery: zone={_zone} type=0x{messageTypeId:X4} player=\"{player}\" item=\"{item}\" capExempt={isCapExempt}");
            }
        }

        /// <summary>Logs each relayed message drained on the tick, with the main thread id.</summary>
        private sealed class RelayHandler : ICrossZoneRelayHandler
        {
            private readonly string _zone;
            private readonly int _mainThreadId;

            public RelayHandler(string zone, int mainThreadId)
            {
                _zone = zone;
                _mainThreadId = mainThreadId;
            }

            public void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                ServerBroadcast_Enhancement9Codec.TryReadBody(body, out string player, out string item);
                Debug.Log($"{LOG_PREFIX} RelayedOnTick: zone={_zone} type=0x{messageTypeId:X4} player=\"{player}\" item=\"{item}\" thread={Thread.CurrentThread.ManagedThreadId} (main={_mainThreadId})");
            }
        }

        /// <summary>
        /// VERIFICATION ONLY. Decorates the real inbox to log the calling thread of <c>TryAdd</c>. The production
        /// <see cref="CrossZoneRelayInbox.TryAdd"/> must not log (ADR-015 Decision 3); this wrapper exists to prove
        /// that the receiver runs on the listener thread and not on the main thread (VR-1).
        /// </summary>
        private sealed class LoggingInbox : ICrossZoneRelayInbox
        {
            private readonly CrossZoneRelayInbox _inner;
            private readonly string _zone;

            public LoggingInbox(CrossZoneRelayInbox inner, string zone)
            {
                _inner = inner;
                _zone = zone;
            }

            public bool TryAdd(ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                Thread t = Thread.CurrentThread;
                bool added = _inner.TryAdd(messageTypeId, body);
                Debug.Log($"{LOG_PREFIX} InboxTryAdd: zone={_zone} type=0x{messageTypeId:X4} added={added} thread={t.ManagedThreadId} name=\"{t.Name}\"");
                return added;
            }

            public int Drain(uint currentTick, ICrossZoneRelayHandler handler)
            {
                return _inner.Drain(currentTick, handler);
            }
        }
    }
}
