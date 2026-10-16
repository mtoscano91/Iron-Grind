using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The production <see cref="ICrossZoneRelayHandler"/> (ADR-015 Decision 7): hands a relayed body to every
    /// client of this zone through <see cref="IZoneClientBroadcastSink.EnqueueToAllClients"/>, not cap-exempt
    /// (AC-NC-47). Tick thread only. Allocates nothing.
    /// </summary>
    /// <example>
    /// <code>
    /// var handler = new ZoneClientRelayHandler(outbox);
    /// var pipeline = new ZoneTickPipeline(completion, inbox, handler, dispatcher, simulation, outbox, tickLoop);
    /// </code>
    /// </example>
    public sealed class ZoneClientRelayHandler : ICrossZoneRelayHandler
    {
        private readonly IZoneClientBroadcastSink _sink;

        /// <summary>Creates the handler.</summary>
        /// <param name="sink">Usually the zone's <see cref="ClientMessageOutbox"/>; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
        public ZoneClientRelayHandler(IZoneClientBroadcastSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <inheritdoc />
        public void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            _sink.EnqueueToAllClients(messageTypeId, body, false);
        }
    }
}
