namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The mob fields the zone process does not own (current position, health, combat state), read through
    /// <see cref="IZoneEntityStateSource"/> when a snapshot is built (Zone Instancing Story 007). Values are in wire
    /// units, as in <see cref="ZonePlayerStateView"/>. The mob type comes from <see cref="IZoneMobRoster"/>.
    /// </summary>
    public readonly struct ZoneMobStateView
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

        /// <summary>True while in combat.</summary>
        public readonly bool IsInCombat;

        /// <summary>Raw normalized cycle timer, 0 to 10,000 (0 for a mob without a cycle).</summary>
        public readonly ushort CycleTimer;

        /// <summary>Creates a view.</summary>
        public ZoneMobStateView(short posX, short posY, short posZ, short rotX, short rotY, short rotZ, short rotW,
            short facingAngle, int currentHp, int maxHp, bool isInCombat, ushort cycleTimer)
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
            IsInCombat = isInCombat;
            CycleTimer = cycleTimer;
        }
    }
}
