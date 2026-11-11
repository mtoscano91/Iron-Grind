using System;

namespace IronGrind.Client.Session
{
    /// <summary>
    /// Receives the events of an <see cref="IZoneClientTransport"/>. A listener interface is used instead of C# events
    /// because <see cref="ReadOnlySpan{T}"/> cannot be a delegate type argument (no <c>Action&lt;ReadOnlySpan&lt;byte&gt;&gt;</c>).
    /// All callbacks must be raised on the thread that calls <see cref="ClientZoneSession.Update"/> (the Unity main thread).
    /// </summary>
    public interface IZoneClientTransportListener
    {
        /// <summary>The transport connected to the zone server.</summary>
        void OnConnected();

        /// <summary>
        /// A full message (10-byte envelope followed by the body) arrived. The span is only valid during the call;
        /// the listener copies whatever it keeps.
        /// </summary>
        /// <param name="message">Envelope plus body.</param>
        void OnMessageReceived(ReadOnlySpan<byte> message);

        /// <summary>
        /// The connection closed or could not be established. An adapter may or may not raise this after a deliberate
        /// <see cref="IZoneClientTransport.Stop"/>; the session ignores it in that case.
        /// </summary>
        void OnDisconnected();
    }

    /// <summary>
    /// Client-side zone transport seam used by <see cref="ClientZoneSession"/> (Authentication Story 018). The session
    /// owns the envelopes: it writes the 10-byte envelope itself and hands over the finished message.
    /// </summary>
    /// <remarks>
    /// The NGO adapter (Part 2, <c>NgoZoneClientTransport</c>) sends and receives unnamed messages through
    /// <c>CustomMessagingManager</c> per ADR-004 Amendment 1, and chooses the delivery class from the
    /// <c>MessageTypeId</c> in the first two bytes of the message (R-OD messages reliable, Heartbeat unreliable).
    /// </remarks>
    public interface IZoneClientTransport
    {
        /// <summary>Sets the single listener. Called once by the <see cref="ClientZoneSession"/> constructor.</summary>
        /// <param name="listener">The listener to notify.</param>
        void SetListener(IZoneClientTransportListener listener);

        /// <summary>Starts a client connection to <paramref name="host"/>:<paramref name="port"/>. Completion is reported through the listener.</summary>
        /// <param name="host">Host name or IP address.</param>
        /// <param name="port">Zone server port.</param>
        void Start(string host, ushort port);

        /// <summary>Shuts the connection down. Safe to call when not connected.</summary>
        void Stop();

        /// <summary>
        /// Sends one full message (envelope plus body). The span is only valid during the call, so the adapter
        /// copies or consumes it synchronously.
        /// </summary>
        /// <param name="message">Envelope plus body.</param>
        void Send(ReadOnlySpan<byte> message);
    }
}
