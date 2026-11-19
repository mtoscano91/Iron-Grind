using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client answer to an invite or disband request, <b>wire name <c>PartyInviteResult</c></b>:
    /// <c>{ byte resultCode; uint targetCharacterId; }</c> - 5-byte body, R-OD priority path, not cap-exempt.
    /// <c>targetCharacterId</c> is 0 when answering a <see cref="PartyDisbandRequest"/>.
    /// </summary>
    /// <remarks><see cref="MessageTypeId"/> <c>0xE177</c> (Party System Story 006).</remarks>
    public readonly struct PartyInviteResult
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE177;

        /// <summary>Wire size of the body: 1 + 4 = 5 bytes.</summary>
        public const int BodySize = 5;

        /// <summary>The result.</summary>
        public readonly PartyInviteResultCode ResultCode;

        /// <summary>Raw CharacterID of the invited character, or 0.</summary>
        public readonly uint TargetCharacterId;

        /// <summary>Creates the message.</summary>
        public PartyInviteResult(PartyInviteResultCode resultCode, uint targetCharacterId)
        {
            ResultCode = resultCode;
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

            destination[0] = (byte)ResultCode;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(1, 4), TargetCharacterId);
            return true;
        }

        /// <summary>Reads a body; false (no throw) if too short or the result code is above 8.</summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyInviteResult message)
        {
            message = default;
            if (source.Length < BodySize || source[0] > (byte)PartyInviteResultCode.Declined)
            {
                return false;
            }

            message = new PartyInviteResult((PartyInviteResultCode)source[0],
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(1, 4)));
            return true;
        }
    }
}
