using System;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request for a zone state snapshot: <c>ZoneSnapshotRequest { ZoneID zoneId; uint snapshotVersion; }</c>
    /// — 8-byte body, C-to-S, R-OD (networking-wire-protocol.md, Zone Instancing Messages; zone-instancing.md CR-ZI-8).
    /// Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no GDD or ADR assigns it; TD-011).
    /// </remarks>
    public readonly struct ZoneSnapshotRequest : IEquatable<ZoneSnapshotRequest>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE131;

        /// <summary>Wire size of the body: 4 + 4 = 8 bytes.</summary>
        public const int BodySize = 8;

        /// <summary>The zone whose snapshot is requested.</summary>
        public readonly ZoneID ZoneId;

        /// <summary>The snapshot version the client wants (or last saw).</summary>
        public readonly uint SnapshotVersion;

        /// <summary>Initializes a new <see cref="ZoneSnapshotRequest"/>.</summary>
        public ZoneSnapshotRequest(ZoneID zoneId, uint snapshotVersion)
        {
            ZoneId = zoneId;
            SnapshotVersion = snapshotVersion;
        }

        /// <inheritdoc/>
        public bool Equals(ZoneSnapshotRequest other) => ZoneId == other.ZoneId && SnapshotVersion == other.SnapshotVersion;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ZoneSnapshotRequest other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ZoneId.GetHashCode() * 397 ^ SnapshotVersion.GetHashCode();

        /// <summary>Returns true if both messages have identical field values.</summary>
        public static bool operator ==(ZoneSnapshotRequest left, ZoneSnapshotRequest right) => left.Equals(right);

        /// <summary>Returns true if the messages differ in any field.</summary>
        public static bool operator !=(ZoneSnapshotRequest left, ZoneSnapshotRequest right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ZoneSnapshotRequest(ZoneId={ZoneId}, SnapshotVersion={SnapshotVersion})";
    }
}
