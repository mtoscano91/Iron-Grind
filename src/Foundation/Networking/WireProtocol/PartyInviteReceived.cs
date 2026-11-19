using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-target invite, <b>wire name <c>PartyInviteReceived</c></b>: <c>{ uint inviterCharacterId;
    /// uint inviterEntityId; string inviterName; byte inviterLevel; uint inviteExpiryTick; }</c>.
    /// The name is a ushort byte count plus at most 24 UTF-8 bytes (cut at a character boundary on encode), so the
    /// body is 15 to 39 bytes. Direction ServerToOwningClient, R-OD priority path.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> <c>0xE173</c> (Party System Story 006). A null <see cref="InviterName"/> encodes
    /// as an empty name (15-byte body).
    /// </remarks>
    public readonly struct PartyInviteReceived
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE173;

        /// <summary>Maximum name length in UTF-8 bytes (equals <see cref="EntityState.MaxNameBytes"/>).</summary>
        public const int MaxNameBytes = EntityState.MaxNameBytes;

        /// <summary>Smallest body (empty name): 4 + 4 + 2 + 0 + 1 + 4 = 15 bytes.</summary>
        public const int MinBodySize = 15;

        /// <summary>Largest body (24-byte name): 39 bytes.</summary>
        public const int MaxBodySize = MinBodySize + MaxNameBytes;

        /// <summary>Raw CharacterID of the inviter.</summary>
        public readonly uint InviterCharacterId;

        /// <summary>Raw EntityID of the inviter.</summary>
        public readonly uint InviterEntityId;

        /// <summary>Inviter name (null reads as empty; cut to 24 UTF-8 bytes on encode).</summary>
        public readonly string InviterName;

        /// <summary>Inviter level.</summary>
        public readonly byte InviterLevel;

        /// <summary>Server tick at which the invite expires.</summary>
        public readonly uint InviteExpiryTick;

        /// <summary>Creates the message.</summary>
        public PartyInviteReceived(uint inviterCharacterId, uint inviterEntityId, string inviterName,
            byte inviterLevel, uint inviteExpiryTick)
        {
            InviterCharacterId = inviterCharacterId;
            InviterEntityId = inviterEntityId;
            InviterName = inviterName;
            InviterLevel = inviterLevel;
            InviteExpiryTick = inviteExpiryTick;
        }

        /// <summary>
        /// Returns the exact encoded body size (15 plus the UTF-8 byte count of the name after the cut to 24 bytes).
        /// </summary>
        public int GetBodySize()
        {
            return MinBodySize + WireStringCodec.GetEncodedByteCount(InviterName, MaxNameBytes);
        }

        /// <summary>
        /// Writes the body at offset 0 and reports the bytes written. False (nothing written) if the destination is
        /// shorter than <see cref="GetBodySize"/>. No allocation.
        /// </summary>
        public bool Write(Span<byte> destination, out int written)
        {
            written = 0;
            int size = GetBodySize();
            if (destination.Length < size)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), InviterCharacterId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), InviterEntityId);
            int offset = WireStringCodec.WriteString(destination, 8, InviterName, MaxNameBytes);
            destination[offset] = InviterLevel;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 1, 4), InviteExpiryTick);
            written = size;
            return true;
        }

        /// <summary>
        /// Reads a body; false (no throw) on truncation anywhere, a name length above 24, or missing trailing bytes.
        /// Decoding allocates the name string (like <see cref="EntityState"/>): a known exception to the
        /// zero-allocation guardrail.
        /// </summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyInviteReceived message)
        {
            message = default;
            if (source.Length < MinBodySize
                || !WireStringCodec.TryReadString(source, 8, MaxNameBytes, out string name, out int offset)
                || source.Length - offset < 5)
            {
                return false;
            }

            message = new PartyInviteReceived(
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)),
                name, source[offset],
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset + 1, 4)));
            return true;
        }
    }
}
