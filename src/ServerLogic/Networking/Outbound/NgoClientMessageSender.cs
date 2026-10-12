using System;
using Unity.Collections;
using Unity.Netcode;

namespace IronGrind.Networking
{
    /// <summary>
    /// The NGO implementation of <see cref="IClientMessageSendSeam"/> (Networking Core Story 039, ADR-004
    /// Decision 2 and Amendment 1): one unnamed custom message per call, through the server
    /// <see cref="NetworkManager"/>'s <c>CustomMessagingManager</c>. It is the only file of <c>src/</c> that
    /// names <c>SendUnnamedMessage</c> or <c>FastBufferWriter</c>. A plain class, not a MonoBehaviour
    /// (ADR-012 Decision 4); the server composition root creates it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>When the bytes leave.</b> <c>SendUnnamedMessage</c> only queues the message in NGO's per-client send
    /// queue; the bytes go on the wire in <c>PostLateUpdate</c> of the frame (engine reference, "NGO 2.13",
    /// read in source).
    /// </para>
    /// <para>
    /// <b>Delivery.</b> <see cref="NetworkChannel.ReliableUnordered"/> maps to <c>NetworkDelivery.Reliable</c>, but
    /// <c>UnityTransport</c> has no reliable-unordered pipeline: <c>Reliable</c> is ordered in practice
    /// (ADR-004 Amendment 1). A reliable send queue that overflows makes NGO disconnect the client; the
    /// disconnect then arrives through Story 038's adapter like any other.
    /// </para>
    /// <para>
    /// <b>Failure behaviour, read in the installed NGO 2.13.3 source (Library/PackageCache), not run unless a
    /// VS item says so.</b> (1) The <see cref="NetworkManager"/> is not listening or has no
    /// <c>CustomMessagingManager</c> (never started, or shut down): <see cref="Send"/> throws
    /// <see cref="InvalidOperationException"/> and sends nothing; it is not swallowed, so the outbox's
    /// catch records it once per client per tick. (2) A client id NGO does not know (never connected, or
    /// already removed from NGO's connection tables): NGO does not throw and does not log at the default log
    /// level; the message is skipped inside <c>NetworkMessageManager.SendPreSerializedMessage</c>
    /// (<c>GetMessageVersion</c> returns -1 for an unknown client, or the id is in its disconnected set).
    /// The call returns normally and nothing goes on the wire, so a stale id is invisible here; the outbox
    /// drops sends to removed clients before the seam. (3) A message above NGO's non-fragmented limit
    /// (<c>NonFragmentedMessageMaxSize</c> 1296 minus NGO's headers) on any delivery but
    /// <c>ReliableFragmentedSequenced</c>: NGO throws <see cref="OverflowException"/> from
    /// <c>ValidateMessageSize</c> in builds with the <c>DEBUG</c> symbol only (read in source, not run unless
    /// Story 039 VS-5 shows it); without <c>DEBUG</c> the check is compiled out. The outbox never sends more
    /// than 10 + 512 bytes. (4) A server-only call: <c>SendUnnamedMessage</c> from a client manager to any id
    /// other than the server throws <see cref="ArgumentException"/> in NGO; this class is for the server.
    /// </para>
    /// <para>
    /// <b>No unsafe code.</b> <c>IronGrind.ServerLogic</c> has <c>allowUnsafeCode: false</c> and does not
    /// reference <c>Unity.Collections</c>. <c>FastBufferWriter.WriteBytesSafe</c> has overloads taking
    /// <c>byte[]</c>, <c>NativeArray</c> and <c>NativeList</c> (the last is in the unreferenced assembly, which
    /// would stop the compile with CS0012), and none takes a <see cref="ReadOnlySpan{T}"/>. So the message is
    /// written byte by byte with <c>TryBeginWrite</c> + <c>WriteByte</c> (signatures read in
    /// <c>Runtime/Serialization/FastBufferWriter.cs</c>): no managed array, no managed allocation per message.
    /// </para>
    /// <para>Tick thread only; not thread safe.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var sender = new NgoClientMessageSender(networkManager);
    /// var outbox = new ClientMessageOutbox(sender);
    /// // the Outbound step of the tick calls outbox.Flush(currentTick)
    /// </code>
    /// </example>
    public sealed class NgoClientMessageSender : IClientMessageSendSeam
    {
        private readonly NetworkManager _networkManager;

        /// <summary>Creates the sender over a started server <see cref="NetworkManager"/>.</summary>
        /// <param name="networkManager">The server manager; not null. The caller owns and starts it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="networkManager"/> is null.</exception>
        public NgoClientMessageSender(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        /// <summary>
        /// The one channel-to-delivery table (ADR-004 Decision 2 and Amendment 1):
        /// <see cref="NetworkChannel.ReliableOrdered"/> to <c>ReliableSequenced</c>,
        /// <see cref="NetworkChannel.ReliableUnordered"/> to <c>Reliable</c>,
        /// <see cref="NetworkChannel.Unreliable"/> to <c>Unreliable</c>.
        /// </summary>
        /// <param name="channel">The project channel.</param>
        /// <returns>The NGO delivery.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is not a defined value.</exception>
        public static NetworkDelivery ToNetworkDelivery(NetworkChannel channel)
        {
            switch (channel)
            {
                case NetworkChannel.ReliableOrdered:
                    return NetworkDelivery.ReliableSequenced;
                case NetworkChannel.ReliableUnordered:
                    return NetworkDelivery.Reliable;
                case NetworkChannel.Unreliable:
                    return NetworkDelivery.Unreliable;
                default:
                    throw new ArgumentOutOfRangeException(nameof(channel), channel,
                        "NgoClientMessageSender: no NetworkDelivery is defined for this channel value.");
            }
        }

        /// <summary>
        /// Queues one unnamed message for one client: a <c>FastBufferWriter</c> created with
        /// <c>Allocator.Temp</c>, filled with <paramref name="message"/> and disposed before the call returns,
        /// and exactly one <c>SendUnnamedMessage(clientId, writer, delivery)</c>.
        /// </summary>
        /// <param name="clientId">The NGO client id (the id the adapter hands out).</param>
        /// <param name="message">The 10-byte envelope followed by the body; valid only during the call.</param>
        /// <param name="channel">The delivery guarantee, mapped by <see cref="ToNetworkDelivery"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is not a defined value.</exception>
        /// <exception cref="ArgumentException"><paramref name="message"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">
        /// The manager is not listening or has no <c>CustomMessagingManager</c>; nothing was sent.
        /// </exception>
        /// <exception cref="OverflowException">NGO's size check, in builds with <c>DEBUG</c> only (see the type remarks).</exception>
        public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
        {
            NetworkDelivery delivery = ToNetworkDelivery(channel);
            if (message.Length == 0)
            {
                throw new ArgumentException("NgoClientMessageSender.Send: the message is empty.", nameof(message));
            }

            CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
            if (messaging == null || !_networkManager.IsListening)
            {
                throw new InvalidOperationException(
                    "NgoClientMessageSender.Send: the NetworkManager is not listening or has no CustomMessagingManager; nothing was sent to client "
                    + clientId + ".");
            }

            using (var writer = new FastBufferWriter(message.Length, Allocator.Temp))
            {
                if (!writer.TryBeginWrite(message.Length))
                {
                    throw new InvalidOperationException(
                        "NgoClientMessageSender.Send: the writer refused " + message.Length + " bytes.");
                }

                for (int i = 0; i < message.Length; i++)
                {
                    writer.WriteByte(message[i]);
                }

                messaging.SendUnnamedMessage(clientId, writer, delivery);
            }
        }
    }
}
