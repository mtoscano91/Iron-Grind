using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-party notice that the party was disbanded (CR-PS-9).
    /// <b>Wire name: <c>PartyDisbanded</c></b> (networking-wire-protocol.md Party System Messages):
    /// <c>{ uint partyId; }</c> - 4-byte body, R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE171</c> (Party System Story 006).</remarks>
    public readonly struct PartyDisbanded
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE171;

        /// <summary>Wire size of the body: 4 bytes.</summary>
        public const int BodySize = 4;

        /// <summary>Raw id: partyId.</summary>
        public readonly uint PartyId;

        /// <summary>Creates the message.</summary>
        public PartyDisbanded(uint partyId)
        {
            PartyId = partyId;
        }

        /// <summary>
        /// Writes the body at offset 0; false (nothing written) if the destination is shorter than
        /// <see cref="BodySize"/>.
        /// </summary>
        public bool Write(Span<byte> destination)
        {
            if (destination.Length < BodySize)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), PartyId);
            return true;
        }

        /// <summary>Reads a body; false (no throw) if the source is shorter than <see cref="BodySize"/>.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyDisbanded message)
        {
            if (source.Length < BodySize)
            {
                message = default;
                return false;
            }

            message = new PartyDisbanded(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)));
            return true;
        }
    }
}
