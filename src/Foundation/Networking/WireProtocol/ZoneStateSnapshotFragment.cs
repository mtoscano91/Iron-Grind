using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Header of one fragment of a zone state snapshot: <c>ZoneStateSnapshotFragment { uint snapshotVersion;
    /// ushort totalFragments; ushort fragmentIndex; byte[] payload; }</c> — body of 8 bytes plus a payload of at most
    /// <see cref="MaxPayloadBytes"/>, server to owning client, R-OD, bulk range <c>0xF000-0xFFFF</c>
    /// (networking-wire-protocol.md, Bulk-Transfer Messages; zone-instancing.md CR-ZI-8). Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This struct holds the 8-byte header only; <see cref="ZoneStateSnapshotFragmentCodec"/> writes the header plus a
    /// caller-supplied payload span and reads the header plus a payload slice of the input (no payload copy). The payload is
    /// a packed sequence of <see cref="EntityState"/> entries. <see cref="MessageTypeId"/> is <b>provisional</b> (TD-011).
    /// </para>
    /// <para>
    /// Cap exemption is the <c>isCapExempt</c> flag at enqueue time, and transport fragmentation of the reliable send
    /// belongs to the sender (Stories 007 / 014); neither is a codec concern.
    /// </para>
    /// </remarks>
    public readonly struct ZoneStateSnapshotFragment : IEquatable<ZoneStateSnapshotFragment>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c>, first id of the bulk range — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xF000;

        /// <summary>Maximum message body size in bytes (CR-NET-7.6).</summary>
        public const int MaxMessageBodyBytes = 512;

        /// <summary>Size of the header: 4 + 2 + 2 = 8 bytes.</summary>
        public const int HeaderSize = 8;

        /// <summary>Maximum payload size: <see cref="MaxMessageBodyBytes"/> - <see cref="HeaderSize"/> = 504 bytes.</summary>
        public const int MaxPayloadBytes = MaxMessageBodyBytes - HeaderSize;

        /// <summary>Version of the snapshot this fragment belongs to.</summary>
        public readonly uint SnapshotVersion;

        /// <summary>Number of fragments in the snapshot (at least 1).</summary>
        public readonly ushort TotalFragments;

        /// <summary>Zero-based index of this fragment, below <see cref="TotalFragments"/>.</summary>
        public readonly ushort FragmentIndex;

        /// <summary>Initializes a new header.</summary>
        public ZoneStateSnapshotFragment(uint snapshotVersion, ushort totalFragments, ushort fragmentIndex)
        {
            SnapshotVersion = snapshotVersion;
            TotalFragments = totalFragments;
            FragmentIndex = fragmentIndex;
        }

        /// <inheritdoc/>
        public bool Equals(ZoneStateSnapshotFragment other)
            => SnapshotVersion == other.SnapshotVersion && TotalFragments == other.TotalFragments && FragmentIndex == other.FragmentIndex;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ZoneStateSnapshotFragment other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (SnapshotVersion.GetHashCode() * 397 ^ TotalFragments) * 397 ^ FragmentIndex;

        /// <summary>Returns true if both headers have identical field values.</summary>
        public static bool operator ==(ZoneStateSnapshotFragment left, ZoneStateSnapshotFragment right) => left.Equals(right);

        /// <summary>Returns true if the headers differ in any field.</summary>
        public static bool operator !=(ZoneStateSnapshotFragment left, ZoneStateSnapshotFragment right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ZoneStateSnapshotFragment(Version={SnapshotVersion}, Index={FragmentIndex}/{TotalFragments})";
    }
}
