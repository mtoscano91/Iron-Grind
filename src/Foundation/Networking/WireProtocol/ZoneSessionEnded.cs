using System;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client notice that the client's zone session ended: <c>ZoneSessionEnded { ZoneID zoneId;
    /// DisconnectReason reason; int gracePeriodSeconds; }</c> — 9-byte body, S-to-C, R-OD (networking-wire-protocol.md,
    /// <c>ZoneSessionEnded</c>; zone-instancing.md CR-ZI-9). Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no GDD or ADR assigns it; TD-011). An unknown reason byte decodes
    /// as <see cref="DisconnectReason.Other"/> via <see cref="WireEnumCodec.DecodeDisconnectReason"/>.
    /// </remarks>
    public readonly struct ZoneSessionEnded : IEquatable<ZoneSessionEnded>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE132;

        /// <summary>Wire size of the body: 4 + 1 + 4 = 9 bytes.</summary>
        public const int BodySize = 9;

        /// <summary>The zone whose session ended.</summary>
        public readonly ZoneID ZoneId;

        /// <summary>Why the session ended.</summary>
        public readonly DisconnectReason Reason;

        /// <summary>Seconds the client has to reconnect (raw wire value).</summary>
        public readonly int GracePeriodSeconds;

        /// <summary>Initializes a new <see cref="ZoneSessionEnded"/>.</summary>
        public ZoneSessionEnded(ZoneID zoneId, DisconnectReason reason, int gracePeriodSeconds)
        {
            ZoneId = zoneId;
            Reason = reason;
            GracePeriodSeconds = gracePeriodSeconds;
        }

        /// <inheritdoc/>
        public bool Equals(ZoneSessionEnded other)
            => ZoneId == other.ZoneId && Reason == other.Reason && GracePeriodSeconds == other.GracePeriodSeconds;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ZoneSessionEnded other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (ZoneId.GetHashCode() * 397 ^ (int)Reason) * 397 ^ GracePeriodSeconds;

        /// <summary>Returns true if both messages have identical field values.</summary>
        public static bool operator ==(ZoneSessionEnded left, ZoneSessionEnded right) => left.Equals(right);

        /// <summary>Returns true if the messages differ in any field.</summary>
        public static bool operator !=(ZoneSessionEnded left, ZoneSessionEnded right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ZoneSessionEnded(ZoneId={ZoneId}, Reason={Reason}, GracePeriodSeconds={GracePeriodSeconds})";
    }
}
