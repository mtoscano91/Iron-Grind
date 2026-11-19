using System;
using System.Buffers.Binary;
using IronGrind.PartySystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// One 11-byte member slot inside <see cref="PartyStateUpdate"/> (networking-wire-protocol.md Party System
    /// Messages):
    /// <c>{ uint characterId; uint entityId; byte level; PartyMemberStatus status; bool xpEligible; }</c>.
    /// An empty slot is all zero (<c>CharacterId == 0</c>).
    /// </summary>
    public readonly struct PartyMemberSlot
    {
        /// <summary>Wire size of one slot: 4 + 4 + 1 + 1 + 1 = 11 bytes.</summary>
        public const int WireSize = 11;

        /// <summary>Raw CharacterID; 0 = empty slot.</summary>
        public readonly uint CharacterId;

        /// <summary>Raw EntityID; 0 = not in the current zone.</summary>
        public readonly uint EntityId;

        /// <summary>Member level; 0 for an empty slot.</summary>
        public readonly byte Level;

        /// <summary>Member status (Story 004 enum, same values as the wire enum).</summary>
        public readonly PartyMemberStatus Status;

        /// <summary>True if the member passes the CR-PS-5 eligibility check at the last state update.</summary>
        public readonly bool XpEligible;

        /// <summary>Creates a slot.</summary>
        public PartyMemberSlot(uint characterId, uint entityId, byte level, PartyMemberStatus status, bool xpEligible)
        {
            CharacterId = characterId;
            EntityId = entityId;
            Level = level;
            Status = status;
            XpEligible = xpEligible;
        }

        /// <summary>
        /// Writes the slot at offset 0; false (nothing written) if the destination is shorter than
        /// <see cref="WireSize"/>.
        /// </summary>
        public bool Write(Span<byte> destination)
        {
            if (destination.Length < WireSize)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), CharacterId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), EntityId);
            destination[8] = Level;
            destination[9] = (byte)Status;
            destination[10] = XpEligible ? (byte)1 : (byte)0;
            return true;
        }

        /// <summary>
        /// Reads a slot from the start of <paramref name="source"/>; false if shorter than <see cref="WireSize"/>.
        /// An unknown status byte reads as Online and sets <paramref name="hadUnknownStatus"/>
        /// (wire spec PartyMemberStatus).
        /// </summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyMemberSlot slot, out bool hadUnknownStatus)
        {
            slot = default;
            hadUnknownStatus = false;
            if (source.Length < WireSize)
            {
                return false;
            }

            byte statusByte = source[9];
            PartyMemberStatus status = PartyMemberStatus.Online;
            if (statusByte <= (byte)PartyMemberStatus.Dead)
            {
                status = (PartyMemberStatus)statusByte;
            }
            else
            {
                hadUnknownStatus = true;
            }

            slot = new PartyMemberSlot(
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)),
                source[8],
                status,
                source[10] != 0);
            return true;
        }
    }
}
