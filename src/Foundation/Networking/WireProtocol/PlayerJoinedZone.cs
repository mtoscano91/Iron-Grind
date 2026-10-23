using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-zone announcement that a player joined: <c>PlayerJoinedZone { EntityID entityId; string characterName;
    /// int level; short posX, posY, posZ; }</c> — variable body of 16 to 40 bytes, R-OD, Pillar 3 (networking-wire-protocol.md,
    /// <c>PlayerJoinedZone</c>; zone-instancing.md CR-ZI-8 step 6). Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no GDD or ADR assigns it; TD-011). The name is a <c>ushort</c>
    /// UTF-8 byte count plus UTF-8 bytes, at most <see cref="MaxNameBytes"/>, cut at a character boundary on encode.
    /// Positions are raw fixed-point centimeters (x100) as on <see cref="EntityPositionUpdate"/>.
    /// </para>
    /// <para>
    /// <b>Scope:</b> the audience is the joining player's own zone only (routing direction
    /// <see cref="MessageDirection.ServerToAllZoneClients"/>), unlike <see cref="ServerBroadcast_Enhancement9"/>,
    /// which reaches every connected client in every zone.
    /// </para>
    /// </remarks>
    public readonly struct PlayerJoinedZone : IEquatable<PlayerJoinedZone>
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE133;

        /// <summary>Maximum UTF-8 byte length of the character name, excluding its length prefix.</summary>
        public const int MaxNameBytes = 24;

        /// <summary>Width of the name's length prefix: a <see cref="ushort"/>, 2 bytes.</summary>
        public const int LengthPrefixSize = WireStringCodec.LengthPrefixSize;

        /// <summary>Size of the fixed fields: entityId 4 + level 4 + 3 x short 6 = 14 bytes.</summary>
        public const int FixedFieldsSize = WireIdCodec.EntityIdWireSize + 4 + 6;

        /// <summary>Minimum body size: empty name, 14 + 2 = 16 bytes.</summary>
        public const int MinBodySize = FixedFieldsSize + LengthPrefixSize;

        /// <summary>Maximum body size: full name, 16 + 24 = 40 bytes.</summary>
        public const int MaxBodySize = MinBodySize + MaxNameBytes;

        /// <summary>The joining player's entity.</summary>
        public readonly EntityID EntityId;

        /// <summary>Display name (never null; cut to <see cref="MaxNameBytes"/> UTF-8 bytes on encode).</summary>
        public readonly string CharacterName;

        /// <summary>The character's level.</summary>
        public readonly int Level;

        /// <summary>Raw X position (x100, centimeters).</summary>
        public readonly short PosX;

        /// <summary>Raw Y position (x100, centimeters).</summary>
        public readonly short PosY;

        /// <summary>Raw Z position (x100, centimeters).</summary>
        public readonly short PosZ;

        /// <summary>Initializes a new <see cref="PlayerJoinedZone"/>; a null name becomes empty.</summary>
        public PlayerJoinedZone(EntityID entityId, string characterName, int level, short posX, short posY, short posZ)
        {
            EntityId = entityId;
            CharacterName = characterName ?? string.Empty;
            Level = level;
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
        }

        /// <inheritdoc/>
        public bool Equals(PlayerJoinedZone other)
            => EntityId == other.EntityId && string.Equals(CharacterName, other.CharacterName, StringComparison.Ordinal)
               && Level == other.Level && PosX == other.PosX && PosY == other.PosY && PosZ == other.PosZ;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PlayerJoinedZone other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ (CharacterName?.GetHashCode() ?? 0);
            h = (h * 397) ^ Level;
            h = (h * 397) ^ PosX;
            h = (h * 397) ^ PosY;
            h = (h * 397) ^ PosZ;
            return h;
        }

        /// <summary>Returns true if both messages have identical field values.</summary>
        public static bool operator ==(PlayerJoinedZone left, PlayerJoinedZone right) => left.Equals(right);

        /// <summary>Returns true if the messages differ in any field.</summary>
        public static bool operator !=(PlayerJoinedZone left, PlayerJoinedZone right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"PlayerJoinedZone(EntityId={EntityId}, Name={CharacterName}, Level={Level}, Pos=({PosX},{PosY},{PosZ}))";
    }
}
