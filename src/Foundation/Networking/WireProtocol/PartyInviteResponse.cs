using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server answer to an invite, <b>wire name <c>PartyInviteResponse</c></b>:
    /// <c>{ uint inviterCharacterId; bool accepted; }</c> - 5-byte body, R-OD priority path. The strict 0 or 1
    /// <c>accepted</c> byte is intentional (Party System Story 006 AC-4): any other value is rejected.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE174</c> (Party System Story 006).</remarks>
    public readonly struct PartyInviteResponse
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE174;

        /// <summary>Wire size of the body: 4 + 1 = 5 bytes.</summary>
        public const int BodySize = 5;

        /// <summary>Raw CharacterID of the inviter (identifies the invite).</summary>
        public readonly uint InviterCharacterId;

        /// <summary>True = accept, false = decline.</summary>
        public readonly bool Accepted;

        /// <summary>Creates the message.</summary>
        public PartyInviteResponse(uint inviterCharacterId, bool accepted)
        {
            InviterCharacterId = inviterCharacterId;
            Accepted = accepted;
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

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), InviterCharacterId);
            destination[4] = Accepted ? (byte)1 : (byte)0;
            return true;
        }

        /// <summary>Reads a body; false (no throw) if too short or the accepted byte is not 0 or 1.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyInviteResponse message)
        {
            message = default;
            if (source.Length < BodySize || source[4] > 1)
            {
                return false;
            }

            message = new PartyInviteResponse(
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)), source[4] == 1);
            return true;
        }
    }
}
