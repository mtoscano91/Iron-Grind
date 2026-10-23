using System;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client rejection of a zone join: <c>ZoneFullResponse { ZoneID zoneId; JoinRejectedReason reason;
    /// byte retryAfterSeconds; }</c> — 6-byte body, S-to-C, R-OD (networking-wire-protocol.md, Zone Instancing Messages;
    /// zone-instancing.md CR-ZI-7). Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no GDD or ADR assigns it; TD-011).
    /// </remarks>
    public readonly struct ZoneFullResponse : IEquatable<ZoneFullResponse>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE130;

        /// <summary>Wire size of the body: 4 + 1 + 1 = 6 bytes.</summary>
        public const int BodySize = 6;

        /// <summary>The zone the client tried to join.</summary>
        public readonly ZoneID ZoneId;

        /// <summary>Why the join was rejected.</summary>
        public readonly JoinRejectedReason Reason;

        /// <summary>Seconds the client should wait before retrying.</summary>
        public readonly byte RetryAfterSeconds;

        /// <summary>Initializes a new <see cref="ZoneFullResponse"/>.</summary>
        public ZoneFullResponse(ZoneID zoneId, JoinRejectedReason reason, byte retryAfterSeconds)
        {
            ZoneId = zoneId;
            Reason = reason;
            RetryAfterSeconds = retryAfterSeconds;
        }

        /// <inheritdoc/>
        public bool Equals(ZoneFullResponse other)
            => ZoneId == other.ZoneId && Reason == other.Reason && RetryAfterSeconds == other.RetryAfterSeconds;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ZoneFullResponse other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (ZoneId.GetHashCode() * 397 ^ (int)Reason) * 397 ^ RetryAfterSeconds;

        /// <summary>Returns true if both messages have identical field values.</summary>
        public static bool operator ==(ZoneFullResponse left, ZoneFullResponse right) => left.Equals(right);

        /// <summary>Returns true if the messages differ in any field.</summary>
        public static bool operator !=(ZoneFullResponse left, ZoneFullResponse right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ZoneFullResponse(ZoneId={ZoneId}, Reason={Reason}, RetryAfterSeconds={RetryAfterSeconds})";
    }
}
