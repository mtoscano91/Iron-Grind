namespace IronGrind.Networking
{
    /// <summary>
    /// The three pre-allocated, fixed-size wire buffers held by a single client connection
    /// (CR-NET-7.7 Buffer Allocation): one for the R-U batch (<see cref="RUBatchWriter"/>), one
    /// for the CycleBroadcast U-U packet (<see cref="CycleBroadcastPacketWriter"/>), and one for
    /// the Position U-U packet (<see cref="PositionPacketWriter"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Allocated once, up front — <b>never</b> via <c>System.Buffers.ArrayPool&lt;T&gt;.Shared</c>
    /// (forbidden by CR-NET-7.7; under IL2CPP, <c>Rent()</c> may allocate when exhausted, causing
    /// GC spikes). <see cref="ZoneBufferPool"/> owns the lifetime of a fixed number of these
    /// instances, pre-allocated at zone-creation time (modeled here as construction time — no
    /// real Unity zone-lifecycle class exists yet in this codebase; a future story wires this to
    /// actual connection accept/close events).
    /// </para>
    /// <para>
    /// Each buffer is <see cref="BufferSize"/> bytes — <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/>
    /// + <see cref="BatchHeaderCodec.HeaderSize"/> (512 + 12 = 524), per CR-NET-7.7's explicit
    /// buffer-sizing formula. This is intentionally 12 bytes larger than the largest packet any
    /// writer in this folder will ever actually produce (which is bounded at
    /// <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/> = 512, header included) — the extra
    /// margin is exactly what CR-NET-7.7 specifies and costs nothing at this connection-scoped
    /// allocation size.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var clientBuffers = new ClientBufferSet();
    /// int bytesWritten = RUBatchWriter.Write(clientBuffers.RUBatchBuffer, seq, tick, damageEvents, goldSyncEvents, other, clientId);
    /// </code>
    /// </example>
    public sealed class ClientBufferSet
    {
        /// <summary>
        /// Per-buffer size in bytes: <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/> +
        /// <see cref="BatchHeaderCodec.HeaderSize"/> = 524 (CR-NET-7.7).
        /// </summary>
        public const int BufferSize = RUBatchWriter.MAX_MESSAGE_BODY_BYTES + BatchHeaderCodec.HeaderSize;

        /// <summary>The pre-allocated buffer for this connection's per-tick R-U batch packet.</summary>
        public readonly byte[] RUBatchBuffer = new byte[BufferSize];

        /// <summary>The pre-allocated buffer for this connection's per-tick CycleBroadcast U-U packet.</summary>
        public readonly byte[] CycleBroadcastBuffer = new byte[BufferSize];

        /// <summary>The pre-allocated buffer for this connection's per-tick Position U-U packet.</summary>
        public readonly byte[] PositionBuffer = new byte[BufferSize];
    }
}
