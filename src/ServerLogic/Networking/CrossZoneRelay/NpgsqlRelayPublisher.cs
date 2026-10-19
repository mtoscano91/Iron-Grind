using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The PostgreSQL publisher of the cross-zone relay (ADR-015 Decision 2): one <c>pg_notify</c> per relayed
    /// message, started through the zone's pooled data source and tracked by <see cref="ITickCompletionQueue"/>, never
    /// blocked on. Tick thread only. Never blocks, never throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Flow.</b> <see cref="Publish"/> encodes the envelope (<see cref="RelayEnvelopeCodec"/>, this process's
    /// origin), takes a free slot, creates a <see cref="CancellationTokenSource"/> that cancels after
    /// <see cref="RELAY_PUBLISH_TIMEOUT_MS"/>, starts the command through the starter seam and hands the task to
    /// <see cref="ITickCompletionQueue.Track{T}"/>. The completion callback runs on the tick thread in a later
    /// <c>Drain</c>: it frees the slot, disposes the source (the queue never disposes a caller-owned one) and logs one
    /// warning for a failure, a cancellation or a timeout, nothing on success. A hung publish therefore frees its slot
    /// after the publish timeout, not after the 10 s queue watchdog.
    /// </para>
    /// <para>
    /// <b>Bound.</b> At most <c>maxPublishesInFlight</c> publishes are pending; a publish over the bound starts
    /// nothing and logs one warning. The slots, and the callback of each, are created once in the constructor, so a
    /// publish allocates no callback or closure; it does allocate the envelope string, the cancellation source, the
    /// command and its task (a few relayed messages per hour). The in-flight count is a field.
    /// </para>
    /// <para>
    /// <b>Data source.</b> The publisher never creates or disposes the <see cref="NpgsqlDataSource"/> it is given.
    /// <b>Threading.</b> Not thread-safe: every member is for the tick thread, the thread the queue was created on.
    /// Order and delivery are not guaranteed (ADR-015 Decision 6): publishes run on different pooled connections.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var publisher = new NpgsqlRelayPublisher(dataSource, completionQueue, origin);
    /// var broadcaster = new ServerWideBroadcaster(outbox, publisher, CrossZoneRelayAllowlist.CreateProduction());
    /// </code>
    /// </example>
    public sealed class NpgsqlRelayPublisher : ICrossZoneRelayPublisher
    {
        /// <summary>Default notification channel (tuning knob RELAY_CHANNEL_NAME).</summary>
        public const string RELAY_CHANNEL_NAME = "irongrind_relay";

        /// <summary>Default most publishes pending at once (tuning knob RELAY_MAX_PUBLISHES_IN_FLIGHT).</summary>
        public const int RELAY_MAX_PUBLISHES_IN_FLIGHT = 8;

        /// <summary>Default publish timeout in milliseconds, for the cancellation source and the command (tuning knob RELAY_PUBLISH_TIMEOUT_MS).</summary>
        public const int RELAY_PUBLISH_TIMEOUT_MS = 2000;

        private const string LOG_PREFIX = "[NpgsqlRelayPublisher]";

        private readonly IRelayNotifyCommandStarter _starter;
        private readonly ITickCompletionQueue _completionQueue;
        private readonly CrossZoneRelayOrigin _origin;
        private readonly string _channelName;
        private readonly int _maxPublishesInFlight;
        private readonly int _publishTimeoutMs;
        private readonly Func<int, CancellationTokenSource> _sourceFactory;
        private readonly PublishSlot[] _freeSlots;
        private int _freeCount;
        private int _inFlight;

        /// <summary>Creates the publisher over the zone's data source.</summary>
        /// <param name="dataSource">The zone's pooled data source; not null; never disposed here.</param>
        /// <param name="completionQueue">The tick's completion queue; not null.</param>
        /// <param name="origin">This process's origin id; not null.</param>
        /// <param name="channelName">The channel; the RELAY_CHANNEL_NAME knob; not empty.</param>
        /// <param name="maxPublishesInFlight">The in-flight bound; the RELAY_MAX_PUBLISHES_IN_FLIGHT knob; at least 1.</param>
        /// <param name="publishTimeoutMs">The publish timeout; the RELAY_PUBLISH_TIMEOUT_MS knob; at least 1.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="channelName"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A number is below 1.</exception>
        public NpgsqlRelayPublisher(
            NpgsqlDataSource dataSource,
            ITickCompletionQueue completionQueue,
            CrossZoneRelayOrigin origin,
            string channelName = RELAY_CHANNEL_NAME,
            int maxPublishesInFlight = RELAY_MAX_PUBLISHES_IN_FLIGHT,
            int publishTimeoutMs = RELAY_PUBLISH_TIMEOUT_MS)
            : this(new NpgsqlRelayNotifyCommandStarter(dataSource), completionQueue, origin, channelName, maxPublishesInFlight, publishTimeoutMs, null)
        {
        }

        /// <summary>Creates the publisher over seams. For tests; the sources factory replaces the timed source.</summary>
        internal NpgsqlRelayPublisher(
            IRelayNotifyCommandStarter starter,
            ITickCompletionQueue completionQueue,
            CrossZoneRelayOrigin origin,
            string channelName,
            int maxPublishesInFlight,
            int publishTimeoutMs,
            Func<int, CancellationTokenSource> sourceFactory)
        {
            _starter = starter ?? throw new ArgumentNullException(nameof(starter));
            _completionQueue = completionQueue ?? throw new ArgumentNullException(nameof(completionQueue));
            _origin = origin ?? throw new ArgumentNullException(nameof(origin));
            if (channelName == null)
            {
                throw new ArgumentNullException(nameof(channelName));
            }

            if (channelName.Length == 0)
            {
                throw new ArgumentException("The channel name must not be empty.", nameof(channelName));
            }

            if (maxPublishesInFlight < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPublishesInFlight));
            }

            if (publishTimeoutMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(publishTimeoutMs));
            }

            _channelName = channelName;
            _maxPublishesInFlight = maxPublishesInFlight;
            _publishTimeoutMs = publishTimeoutMs;
            _sourceFactory = sourceFactory ?? CreateTimedSource;
            _freeSlots = new PublishSlot[maxPublishesInFlight];
            for (int i = 0; i < maxPublishesInFlight; i++)
            {
                _freeSlots[i] = new PublishSlot(this);
            }

            _freeCount = maxPublishesInFlight;
        }

        /// <summary>Publishes started and not yet handled by the completion callback.</summary>
        public int InFlightCount
        {
            get { return _inFlight; }
        }

        /// <summary>Starts one <c>pg_notify</c> for the message and returns at once. Never throws.</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="body">The encoded body; valid only during the call.</param>
        public void Publish(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            PublishSlot slot = null;
            try
            {
                if (_freeCount == 0)
                {
                    Debug.LogWarning($"{LOG_PREFIX} PublishDropped: type=0x{messageTypeId:X4} - {_maxPublishesInFlight} publishes are already in flight; not sent to the other zones.");
                    return;
                }

                string payload = RelayEnvelopeCodec.Encode(messageTypeId, _origin.Value, body);
                _freeCount--;
                slot = _freeSlots[_freeCount];
                slot.InUse = true;
                _inFlight++;

                CancellationTokenSource source = _sourceFactory(_publishTimeoutMs);
                slot.Source = source;
                Task<int> task = _starter.Start(_channelName, payload, _publishTimeoutMs, source.Token);
                _completionQueue.Track(task, source, slot.Callback);
            }
            catch (Exception ex)
            {
                if (slot != null)
                {
                    TryCancel(slot.Source);
                    Release(slot);
                }

                Debug.LogWarning($"{LOG_PREFIX} PublishStartFailed: type=0x{messageTypeId:X4} - {ex.GetType().Name}: {ex.Message}; not sent to the other zones.");
            }
        }

        private static CancellationTokenSource CreateTimedSource(int timeoutMs)
        {
            return new CancellationTokenSource(timeoutMs);
        }

        private static void TryCancel(CancellationTokenSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            catch (Exception)
            {
                // The publish is already being abandoned; a failing cancel callback changes nothing.
            }
        }

        private void OnPublishFinished(PublishSlot slot, TickTaskResult<int> result)
        {
            if (!slot.InUse)
            {
                return;
            }

            Release(slot);
            switch (result.Status)
            {
                case TickTaskStatus.Completed:
                    break;
                case TickTaskStatus.Faulted:
                    Debug.LogWarning($"{LOG_PREFIX} PublishFailed: {result.Error.GetType().Name}: {result.Error.Message}; the other zones may not have received the message.");
                    break;
                default:
                    Debug.LogWarning($"{LOG_PREFIX} PublishTimedOut: status={result.Status} timeoutMs={_publishTimeoutMs}; the other zones may not have received the message.");
                    break;
            }
        }

        private void Release(PublishSlot slot)
        {
            CancellationTokenSource source = slot.Source;
            slot.Source = null;
            slot.InUse = false;
            if (source != null)
            {
                try
                {
                    source.Dispose();
                }
                catch (Exception)
                {
                    // Disposing a source twice or a failing registration changes nothing for the next publish.
                }
            }

            _freeSlots[_freeCount] = slot;
            _freeCount++;
            _inFlight--;
        }

        // One slot per allowed in-flight publish. The callback delegate is made once, so Publish allocates none.
        private sealed class PublishSlot
        {
            private readonly NpgsqlRelayPublisher _owner;

            internal readonly Action<TickTaskResult<int>> Callback;
            internal CancellationTokenSource Source;
            internal bool InUse;

            internal PublishSlot(NpgsqlRelayPublisher owner)
            {
                _owner = owner;
                Callback = OnFinished;
            }

            private void OnFinished(TickTaskResult<int> result)
            {
                _owner.OnPublishFinished(this, result);
            }
        }
    }
}
