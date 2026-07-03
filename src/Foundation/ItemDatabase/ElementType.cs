namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Elemental damage type carried by a weapon. Used in conjunction with
    /// <see cref="EquipmentData.ElementalDamage"/> to determine bonus elemental
    /// damage dealt on hit. <see cref="None"/> means no elemental bonus.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency.
    /// </remarks>
    public enum ElementType : byte
    {
        /// <summary>No elemental affinity. Weapon deals only physical damage.</summary>
        None      = 0,

        /// <summary>Fire element. Applies burn interactions where supported.</summary>
        Fire      = 1,

        /// <summary>Cold element. Applies chill interactions where supported.</summary>
        Cold      = 2,

        /// <summary>Lightning element. Applies shock interactions where supported.</summary>
        Lightning = 3,

        /// <summary>Poison element. Applies poison interactions where supported.</summary>
        Poison    = 4,
    }
}
