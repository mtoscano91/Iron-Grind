using System;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server acknowledgement that the zone snapshot dual gate opened:
    /// <c>ZoneEntryAck { ZoneID zoneId; uint snapshotVersion; }</c> — 8-byte body, C-to-S, R-OD, connection-level
    /// (zone-instancing.md CR-ZI-8 step 11, CR-ZI-17; networking-wire-protocol.md, <c>ZoneEntryAck</c>). Zone
    /// Instancing Story 011.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no GDD or ADR assigns it; TD-011). The server only logs
    /// <see cref="SnapshotVersion"/>; it never checks it.
    /// </remarks>
    public readonly struct ZoneEntryAck : IEquatable<ZoneEntryAck>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE134;

        /// <summary>Wire size of the body: 4 + 4 = 8 bytes.</summary>
        public const int BodySize = 8;

        /// <summary>The zone the client acknowledges entering.</summary>
        public readonly ZoneID ZoneId;

        /// <summary>The snapshot version the client completed (logged only).</summary>
        public readonly uint SnapshotVersion;

        /// <summary>Initializes a new <see cref="ZoneEntryAck"/>.</summary>
        public ZoneEntryAck(ZoneID zoneId, uint snapshotVersion)
        {
            ZoneId = zoneId;
            SnapshotVersion = snapshotVersion;
        }

        /// <inheritdoc/>
        public bool Equals(ZoneEntryAck other) => ZoneId == other.ZoneId && SnapshotVersion == other.SnapshotVersion;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ZoneEntryAck other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ZoneId.GetHashCode() * 397 ^ (int)SnapshotVersion;

        /// <summary>Returns true if both messages have identical field values.</summary>
        public static bool operator ==(ZoneEntryAck left, ZoneEntryAck right) => left.Equals(right);

        /// <summary>Returns true if the messages differ in any field.</summary>
        public static bool operator !=(ZoneEntryAck left, ZoneEntryAck right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ZoneEntryAck(ZoneId={ZoneId}, SnapshotVersion={SnapshotVersion})";
    }
}
