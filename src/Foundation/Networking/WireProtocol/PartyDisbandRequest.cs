using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request to disband the sender party (leader only). <b>Wire name:
    /// <c>PartyDisbandRequest</c></b>: empty body (the server knows the party from SenderEntityID), R-OD priority path.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE176</c> (Party System Story 006).</remarks>
    public readonly struct PartyDisbandRequest
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE176;

        /// <summary>Wire size of the body: 0 bytes.</summary>
        public const int BodySize = 0;

        /// <summary>Writes the empty body; always true.</summary>
        public bool Write(Span<byte> destination)
        {
            return true;
        }

        /// <summary>
        /// Reads the empty body; accepts any span. Oversize rejection (a non-empty body) is the dispatcher job
        /// (Party System Stories 007 / 008), not this codec.
        /// </summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyDisbandRequest message)
        {
            message = default;
            return true;
        }
    }
}
