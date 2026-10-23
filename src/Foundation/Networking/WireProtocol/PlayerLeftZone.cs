using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-zone announcement that a player left: <c>PlayerLeftZone { EntityID entityId; DisconnectType
    /// disconnectType; }</c> — 5-byte body, S-to-C, R-OD (networking-wire-protocol.md, <c>PlayerLeftZone</c>;
    /// zone-instancing.md CR-ZI-17 point 2). Zone Instancing Story 011.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no GDD or ADR assigns it; TD-011). An unknown
    /// <see cref="DisconnectType"/> byte decodes as <see cref="DisconnectType.Timeout"/>.
    /// </remarks>
    public readonly struct PlayerLeftZone : IEquatable<PlayerLeftZone>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE135;

        /// <summary>Wire size of the body: 4 + 1 = 5 bytes.</summary>
        public const int BodySize = 5;

        /// <summary>The player's entity that left.</summary>
        public readonly EntityID EntityId;

        /// <summary>How the player left.</summary>
        public readonly DisconnectType DisconnectType;

        /// <summary>Initializes a new <see cref="PlayerLeftZone"/>.</summary>
        public PlayerLeftZone(EntityID entityId, DisconnectType disconnectType)
        {
            EntityId = entityId;
            DisconnectType = disconnectType;
        }

        /// <inheritdoc/>
        public bool Equals(PlayerLeftZone other) => EntityId == other.EntityId && DisconnectType == other.DisconnectType;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PlayerLeftZone other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => EntityId.GetHashCode() * 397 ^ (int)DisconnectType;

        /// <summary>Returns true if both messages have identical field values.</summary>
        public static bool operator ==(PlayerLeftZone left, PlayerLeftZone right) => left.Equals(right);

        /// <summary>Returns true if the messages differ in any field.</summary>
        public static bool operator !=(PlayerLeftZone left, PlayerLeftZone right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"PlayerLeftZone(EntityId={EntityId}, DisconnectType={DisconnectType})";
    }
}
