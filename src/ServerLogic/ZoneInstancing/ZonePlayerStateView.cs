namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The player fields the zone process does not own, read through <see cref="IZoneEntityStateSource"/> when a
    /// snapshot is built (Zone Instancing Story 007). Values are already in wire units: positions x100, rotation
    /// x32,767, cycle timer 0 to 10,000, facing angle x10 (see <c>EntityState</c>).
    /// </summary>
    public readonly struct ZonePlayerStateView
    {
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

        /// <summary>Raw facing angle (x10).</summary>
        public readonly short FacingAngle;

        /// <summary>Current hit points.</summary>
        public readonly int CurrentHp;

        /// <summary>Maximum hit points.</summary>
        public readonly int MaxHp;

        /// <summary>Current mana.</summary>
        public readonly int CurrentMp;

        /// <summary>Maximum mana.</summary>
        public readonly int MaxMp;

        /// <summary>Character level.</summary>
        public readonly int Level;

        /// <summary>True while in combat.</summary>
        public readonly bool IsInCombat;

        /// <summary>Raw normalized cycle timer, 0 to 10,000.</summary>
        public readonly ushort CycleTimer;

        /// <summary>Equipment appearance bit flags.</summary>
        public readonly byte EquipmentAppearanceFlags;

        /// <summary>Display name; the encoder cuts it to 24 UTF-8 bytes. A cached string, so reading it allocates nothing.</summary>
        public readonly string CharacterName;

        /// <summary>Server tick at which a dead character respawns; only read while Dead or Respawning.</summary>
        public readonly uint RespawnTick;

        /// <summary>Creates a view.</summary>
        public ZonePlayerStateView(short posX, short posY, short posZ, short rotX, short rotY, short rotZ, short rotW,
            short facingAngle, int currentHp, int maxHp, int currentMp, int maxMp, int level, bool isInCombat,
            ushort cycleTimer, byte equipmentAppearanceFlags, string characterName, uint respawnTick)
        {
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            RotX = rotX;
            RotY = rotY;
            RotZ = rotZ;
            RotW = rotW;
            FacingAngle = facingAngle;
            CurrentHp = currentHp;
            MaxHp = maxHp;
            CurrentMp = currentMp;
            MaxMp = maxMp;
            Level = level;
            IsInCombat = isInCombat;
            CycleTimer = cycleTimer;
            EquipmentAppearanceFlags = equipmentAppearanceFlags;
            CharacterName = characterName;
            RespawnTick = respawnTick;
        }
    }
}
