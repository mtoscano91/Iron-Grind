namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Immutable snapshot of the mob facts the Loot Table System needs (design/gdd/loot-table-system.md
    /// CR-LT-3 / CR-LT-4 / F-LT-3): its type, maximum HP and world position.
    /// </summary>
    public readonly struct MobInfo
    {
        /// <summary>Initializes a new <see cref="MobInfo"/>.</summary>
        /// <param name="mobTypeId">The mob's type, used to select its loot table.</param>
        /// <param name="maxHp">The mob's maximum HP, used for the tag threshold (F-LT-3).</param>
        /// <param name="position">The mob's world position, used for ground-item placement.</param>
        public MobInfo(MobTypeID mobTypeId, int maxHp, UnityEngine.Vector3 position)
        {
            MobTypeId = mobTypeId;
            MaxHP = maxHp;
            Position = position;
        }

        /// <summary>The mob's type.</summary>
        public MobTypeID MobTypeId { get; }

        /// <summary>The mob's maximum HP.</summary>
        public int MaxHP { get; }

        /// <summary>The mob's world position.</summary>
        public UnityEngine.Vector3 Position { get; }
    }
}
