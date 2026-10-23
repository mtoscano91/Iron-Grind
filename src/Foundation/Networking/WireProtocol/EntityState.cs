using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// One entity entry inside a <see cref="ZoneStateSnapshotFragment"/> payload — a tagged union, <b>not</b> a message
    /// (no MessageTypeID, no routing row). <c>entityType</c> is always the first byte (networking-wire-protocol.md,
    /// Bulk-Transfer Messages; zone-instancing.md CR-ZI-8). Zone Instancing Story 006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layout: shared header of <see cref="HeaderSize"/> = 33 bytes (entityType 1, entityId 4, pos 3 x short 6, rot 4 x short 8,
    /// currentHP 4, maxHP 4, isInCombat 1, isGhost 1, cycleTimer 2, facingAngle 2), then either the player fields
    /// (name ushort length + UTF-8 up to 24 bytes, level 4, currentMP 4, maxMP 4, equipmentAppearanceFlags 1, isInDeadState 1,
    /// respawnTicksRemaining 4: 20 to 44 bytes, body 53 to 77) or the mob field (mobTypeId 2: body 35).
    /// Entries carry no length prefix; walk a packed payload with the bytes-consumed result of
    /// <see cref="EntityStateCodec.TryReadBody"/>.
    /// </para>
    /// <para>
    /// Schemas hold raw wire values: positions x100 (short), rotation x32,767 (short), cycleTimer 0 to 10,000 (ushort),
    /// facingAngle x10 (short). Fields that do not belong to the entry's type are default and are not written.
    /// </para>
    /// </remarks>
    public readonly struct EntityState : IEquatable<EntityState>
    {
        /// <summary>Size of the shared header: 33 bytes.</summary>
        public const int HeaderSize = 1 + WireIdCodec.EntityIdWireSize + 6 + 8 + 4 + 4 + 1 + 1 + 2 + 2;

        /// <summary>Maximum UTF-8 byte length of the player name, excluding its length prefix.</summary>
        public const int MaxNameBytes = 24;

        /// <summary>Width of the name's length prefix: 2 bytes.</summary>
        public const int LengthPrefixSize = WireStringCodec.LengthPrefixSize;

        /// <summary>Size of the player fields after the name: level 4 + MP 4 + 4 + flags 1 + dead 1 + respawn 4 = 18 bytes.</summary>
        public const int PlayerTailSize = 4 + 4 + 4 + 1 + 1 + 4;

        /// <summary>Minimum player entry size (empty name): 33 + 2 + 18 = 53 bytes.</summary>
        public const int PlayerMinBodySize = HeaderSize + LengthPrefixSize + PlayerTailSize;

        /// <summary>Maximum player entry size (24-byte name): 77 bytes.</summary>
        public const int PlayerMaxBodySize = PlayerMinBodySize + MaxNameBytes;

        /// <summary>Mob entry size: 33 + 2 = 35 bytes.</summary>
        public const int MobBodySize = HeaderSize + 2;

        /// <summary>Which union member this entry is.</summary>
        public readonly EntityType Type;

        /// <summary>The entity's id.</summary>
        public readonly EntityID EntityId;

        /// <summary>Raw X position (x100).</summary>
        public readonly short PosX;

        /// <summary>Raw Y position (x100).</summary>
        public readonly short PosY;

        /// <summary>Raw Z position (x100).</summary>
        public readonly short PosZ;

        /// <summary>Raw rotation X (x32,767).</summary>
        public readonly short RotX;

        /// <summary>Raw rotation Y (x32,767).</summary>
        public readonly short RotY;

        /// <summary>Raw rotation Z (x32,767).</summary>
        public readonly short RotZ;

        /// <summary>Raw rotation W (x32,767).</summary>
        public readonly short RotW;

        /// <summary>Current hit points.</summary>
        public readonly int CurrentHp;

        /// <summary>Maximum hit points.</summary>
        public readonly int MaxHp;

        /// <summary>True while in combat.</summary>
        public readonly bool IsInCombat;

        /// <summary>True for a ghost player (always false for mobs).</summary>
        public readonly bool IsGhost;

        /// <summary>Raw normalized cycle timer, 0 to 10,000 (mobs without a cycle send 0).</summary>
        public readonly ushort CycleTimer;

        /// <summary>Raw facing angle (x10).</summary>
        public readonly short FacingAngle;

        /// <summary>Player only: display name (never null; empty for a mob).</summary>
        public readonly string CharacterName;

        /// <summary>Player only: level.</summary>
        public readonly int Level;

        /// <summary>Player only: current mana.</summary>
        public readonly int CurrentMp;

        /// <summary>Player only: maximum mana.</summary>
        public readonly int MaxMp;

        /// <summary>Player only: equipment appearance bit flags.</summary>
        public readonly byte EquipmentAppearanceFlags;

        /// <summary>Player only: true while dead.</summary>
        public readonly bool IsInDeadState;

        /// <summary>Player only: ticks until respawn.</summary>
        public readonly uint RespawnTicksRemaining;

        /// <summary>Mob only: mob type id.</summary>
        public readonly ushort MobTypeId;

        private EntityState(EntityType type, EntityID entityId, short posX, short posY, short posZ,
            short rotX, short rotY, short rotZ, short rotW, int currentHp, int maxHp, bool isInCombat, bool isGhost,
            ushort cycleTimer, short facingAngle, string characterName, int level, int currentMp, int maxMp,
            byte equipmentAppearanceFlags, bool isInDeadState, uint respawnTicksRemaining, ushort mobTypeId)
        {
            Type = type;
            EntityId = entityId;
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            RotX = rotX;
            RotY = rotY;
            RotZ = rotZ;
            RotW = rotW;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            IsInCombat = isInCombat;
            IsGhost = isGhost;
            CycleTimer = cycleTimer;
            FacingAngle = facingAngle;
            CharacterName = characterName ?? string.Empty;
            Level = level;
            CurrentMp = currentMp;
            MaxMp = maxMp;
            EquipmentAppearanceFlags = equipmentAppearanceFlags;
            IsInDeadState = isInDeadState;
            RespawnTicksRemaining = respawnTicksRemaining;
            MobTypeId = mobTypeId;
        }

        /// <summary>Creates a player entry (<see cref="EntityType.Player"/>); a null name becomes empty.</summary>
        public static EntityState CreatePlayer(EntityID entityId, short posX, short posY, short posZ,
            short rotX, short rotY, short rotZ, short rotW, int currentHp, int maxHp, bool isInCombat, bool isGhost,
            ushort cycleTimer, short facingAngle, string characterName, int level, int currentMp, int maxMp,
            byte equipmentAppearanceFlags, bool isInDeadState, uint respawnTicksRemaining)
            => new EntityState(EntityType.Player, entityId, posX, posY, posZ, rotX, rotY, rotZ, rotW, currentHp, maxHp,
                isInCombat, isGhost, cycleTimer, facingAngle, characterName, level, currentMp, maxMp,
                equipmentAppearanceFlags, isInDeadState, respawnTicksRemaining, 0);

        /// <summary>Creates a mob entry (<see cref="EntityType.Mob"/>); <c>isGhost</c> is always false for a mob.</summary>
        public static EntityState CreateMob(EntityID entityId, short posX, short posY, short posZ,
            short rotX, short rotY, short rotZ, short rotW, int currentHp, int maxHp, bool isInCombat,
            ushort cycleTimer, short facingAngle, ushort mobTypeId)
            => new EntityState(EntityType.Mob, entityId, posX, posY, posZ, rotX, rotY, rotZ, rotW, currentHp, maxHp,
                isInCombat, false, cycleTimer, facingAngle, string.Empty, 0, 0, 0, 0, false, 0, mobTypeId);

        /// <inheritdoc/>
        public bool Equals(EntityState other)
            => Type == other.Type && EntityId == other.EntityId && PosX == other.PosX && PosY == other.PosY && PosZ == other.PosZ
               && RotX == other.RotX && RotY == other.RotY && RotZ == other.RotZ && RotW == other.RotW
               && CurrentHp == other.CurrentHp && MaxHp == other.MaxHp && IsInCombat == other.IsInCombat
               && IsGhost == other.IsGhost && CycleTimer == other.CycleTimer && FacingAngle == other.FacingAngle
               && string.Equals(CharacterName, other.CharacterName, StringComparison.Ordinal)
               && Level == other.Level && CurrentMp == other.CurrentMp && MaxMp == other.MaxMp
               && EquipmentAppearanceFlags == other.EquipmentAppearanceFlags && IsInDeadState == other.IsInDeadState
               && RespawnTicksRemaining == other.RespawnTicksRemaining && MobTypeId == other.MobTypeId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EntityState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ (int)Type;
            h = (h * 397) ^ PosX;
            h = (h * 397) ^ CurrentHp;
            h = (h * 397) ^ (CharacterName?.GetHashCode() ?? 0);
            return h;
        }

        /// <summary>Returns true if both entries have identical field values.</summary>
        public static bool operator ==(EntityState left, EntityState right) => left.Equals(right);

        /// <summary>Returns true if the entries differ in any field.</summary>
        public static bool operator !=(EntityState left, EntityState right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"EntityState(Type={Type}, EntityId={EntityId}, HP={CurrentHp}/{MaxHp})";
    }
}
