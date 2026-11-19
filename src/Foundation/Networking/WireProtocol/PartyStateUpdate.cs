using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-party full party snapshot, <b>wire name <c>PartyStateUpdate</c></b>
    /// (networking-wire-protocol.md Party System Messages): partyId 4, sequenceId 4, isSoloParty 1, memberCount 1,
    /// leaderIndex 1, rrNextIndex 1, then 4 fixed <see cref="PartyMemberSlot"/>s = 56-byte body.
    /// Direction ServerToParty, R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> <c>0xE170</c> (Party System Story 006). The decoder validates only the length and
    /// <c>memberCount</c> (0 to 4). <c>leaderIndex</c> and <c>rrNextIndex</c> are NOT range-checked against
    /// <c>memberCount</c> here: the consumer must range-check them before use (wire spec). A non-zero
    /// <c>isSoloParty</c> byte decodes as true (lenient, as in EntityRespawned and EntityStateCodec).
    /// </remarks>
    public readonly struct PartyStateUpdate
    {
        /// <summary>Wire <c>MessageTypeID</c>.</summary>
        public const ushort MessageTypeId = 0xE170;

        /// <summary>Fixed slot count (MAX_PARTY_SIZE).</summary>
        public const int SlotCount = 4;

        /// <summary>Header size: 4 + 4 + 1 + 1 + 1 + 1 = 12 bytes.</summary>
        public const int HeaderSize = 12;

        /// <summary>Wire size of the body: 12 + 4 * 11 = 56 bytes.</summary>
        public const int BodySize = HeaderSize + SlotCount * PartyMemberSlot.WireSize;

        /// <summary>Raw PartyID.</summary>
        public readonly uint PartyId;

        /// <summary>Per-party monotonic version (stale discard via IsNewerVersion).</summary>
        public readonly uint SequenceId;

        /// <summary>True when the party has exactly one member.</summary>
        public readonly bool IsSoloParty;

        /// <summary>Number of occupied slots (0 to 4).</summary>
        public readonly byte MemberCount;

        /// <summary>Index of the leader slot (not range-checked by the decoder).</summary>
        public readonly byte LeaderIndex;

        /// <summary>Round-robin cursor (not range-checked by the decoder).</summary>
        public readonly byte RrNextIndex;

        /// <summary>Slot 0.</summary>
        public readonly PartyMemberSlot Member0;

        /// <summary>Slot 1.</summary>
        public readonly PartyMemberSlot Member1;

        /// <summary>Slot 2.</summary>
        public readonly PartyMemberSlot Member2;

        /// <summary>Slot 3.</summary>
        public readonly PartyMemberSlot Member3;

        /// <summary>Creates a snapshot.</summary>
        public PartyStateUpdate(uint partyId, uint sequenceId, bool isSoloParty, byte memberCount, byte leaderIndex,
            byte rrNextIndex, PartyMemberSlot member0, PartyMemberSlot member1, PartyMemberSlot member2,
            PartyMemberSlot member3)
        {
            PartyId = partyId;
            SequenceId = sequenceId;
            IsSoloParty = isSoloParty;
            MemberCount = memberCount;
            LeaderIndex = leaderIndex;
            RrNextIndex = rrNextIndex;
            Member0 = member0;
            Member1 = member1;
            Member2 = member2;
            Member3 = member3;
        }

        /// <summary>
        /// Returns slot <paramref name="index"/> (0 to 3); throws <see cref="ArgumentOutOfRangeException"/> otherwise.
        /// </summary>
        public PartyMemberSlot GetMember(int index)
        {
            switch (index)
            {
                case 0: return Member0;
                case 1: return Member1;
                case 2: return Member2;
                case 3: return Member3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>
        /// Writes the body at offset 0; false (nothing written) if the destination is shorter than
        /// <see cref="BodySize"/>. No allocation.
        /// </summary>
        public bool Write(Span<byte> destination)
        {
            if (destination.Length < BodySize)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), PartyId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), SequenceId);
            destination[8] = IsSoloParty ? (byte)1 : (byte)0;
            destination[9] = MemberCount;
            destination[10] = LeaderIndex;
            destination[11] = RrNextIndex;
            const int Step = PartyMemberSlot.WireSize;
            Member0.Write(destination.Slice(HeaderSize, Step));
            Member1.Write(destination.Slice(HeaderSize + Step, Step));
            Member2.Write(destination.Slice(HeaderSize + 2 * Step, Step));
            Member3.Write(destination.Slice(HeaderSize + 3 * Step, Step));
            return true;
        }

        /// <summary>
        /// Reads a body. False if shorter than <see cref="BodySize"/> or memberCount is above 4. An unknown status
        /// byte in any slot decodes as Online and sets <paramref name="hadUnknownStatus"/> (wire spec rule); the read
        /// still succeeds.
        /// </summary>
        public static bool TryRead(ReadOnlySpan<byte> source, out PartyStateUpdate message, out bool hadUnknownStatus)
        {
            message = default;
            hadUnknownStatus = false;
            if (source.Length < BodySize || source[9] > SlotCount)
            {
                return false;
            }

            // The slot TryRead results are ignored on purpose: the full 56-byte length is already checked above,
            // so each 11-byte slice is always long enough and cannot fail.
            const int Step = PartyMemberSlot.WireSize;
            PartyMemberSlot.TryRead(source.Slice(HeaderSize), out PartyMemberSlot m0, out bool u0);
            PartyMemberSlot.TryRead(source.Slice(HeaderSize + Step), out PartyMemberSlot m1, out bool u1);
            PartyMemberSlot.TryRead(source.Slice(HeaderSize + 2 * Step), out PartyMemberSlot m2, out bool u2);
            PartyMemberSlot.TryRead(source.Slice(HeaderSize + 3 * Step), out PartyMemberSlot m3, out bool u3);
            hadUnknownStatus = u0 || u1 || u2 || u3;
            message = new PartyStateUpdate(
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)),
                source[8] != 0, source[9], source[10], source[11], m0, m1, m2, m3);
            return true;
        }
    }
}
