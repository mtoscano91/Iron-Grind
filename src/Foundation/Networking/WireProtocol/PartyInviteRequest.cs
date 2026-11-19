using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request to invite a character (leader only, CR-PS-4); the failure answer is a
    /// PartyInviteResult. <b>Wire name: <c>PartyInviteRequest</c></b> (networking-wire-protocol.md Party System
    /// Messages):
    /// <c>{ uint targetCharacterId; }</c> - 4-byte body, R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE172</c> (Party System Story 006).</remarks>
    public readonly struct PartyInviteRequest
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE172;

        /// <summary>Wire size of the body: 4 bytes.</summary>
        public const int BodySize = 4;

        /// <summary>Raw id: targetCharacterId.</summary>
        public readonly uint TargetCharacterId;

        /// <summary>Creates the message.</summary>
        public PartyInviteRequest(uint targetCharacterId)
        {
            TargetCharacterId = targetCharacterId;
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

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), TargetCharacterId);
            return true;
        }

        /// <summary>Reads a body; false (no throw) if the source is shorter than <see cref="BodySize"/>.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyInviteRequest message)
        {
            if (source.Length < BodySize)
            {
                message = default;
                return false;
            }

            message = new PartyInviteRequest(BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)));
            return true;
        }
    }
}
