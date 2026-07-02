using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// Factory for common CharacterStats test setups. Provides canonical entity IDs
    /// and a fresh container instance for each test.
    /// </summary>
    internal static class CharacterStatsFixture
    {
        /// <summary>Canonical player entity ID for tests that exercise player stat paths.</summary>
        public static readonly EntityID PlayerEntityId  = new EntityID(1001u);

        /// <summary>Canonical mob entity ID for tests that exercise mob stat paths and player-only field guards.</summary>
        public static readonly EntityID MobEntityId     = new EntityID(2001u);

        /// <summary>Second player entity ID for container-isolation tests.</summary>
        public static readonly EntityID PlayerBEntityId = new EntityID(1002u);

        /// <summary>
        /// Returns a fresh <see cref="IronGrind.CharacterStats.CharacterStats"/> instance
        /// with no entities or stats pre-populated.
        /// Uses a no-op <see cref="ILevelingService"/> stub: all entities are treated as
        /// non-player, the threshold is <see cref="int.MaxValue"/>, and notifications are discarded.
        /// </summary>
        public static IronGrind.CharacterStats.CharacterStats Create()
        {
            return new IronGrind.CharacterStats.CharacterStats(new NullLevelingService());
        }

        /// <summary>
        /// Returns a fresh <see cref="IronGrind.CharacterStats.CharacterStats"/> instance
        /// backed by the supplied <paramref name="levelingService"/>.
        /// Use this overload in tests that need to observe or control leveling behaviour.
        /// </summary>
        public static IronGrind.CharacterStats.CharacterStats CreateWithLeveling(ILevelingService levelingService)
        {
            return new IronGrind.CharacterStats.CharacterStats(levelingService);
        }

        // -----------------------------------------------------------------------
        // NullLevelingService — default stub for tests that don't exercise leveling.
        // IsPlayerEntity always returns false so AddExperience is a no-op by default.
        // -----------------------------------------------------------------------

        private sealed class NullLevelingService : ILevelingService
        {
            public bool IsPlayerEntity(EntityID entityId) => false;
            public int GetExperienceThreshold(EntityID entityId) => int.MaxValue;
            public void NotifyExperienceCrossedThreshold(EntityID entityId) { }
        }

        // -----------------------------------------------------------------------
        // Story 002 / 003: Modifier stack test seams.
        // These helpers write directly into the internal slot arrays so tests can
        // exercise GetEffectiveStat / GetEffectiveStatFloat and the lifecycle API
        // without worrying about preconditions from the public Add/Remove API.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Sets a float-schema base stat directly on the container — bypasses public API
        /// for test setup.
        /// </summary>
        public static void SetFloatBaseStat(
            IronGrind.CharacterStats.CharacterStats stats,
            EntityID entityId,
            StatID statId,
            float value)
        {
            stats.SetBaseStatFloat(entityId, statId, value);
        }

        /// <summary>
        /// Installs equipment modifiers for an entity-stat pair directly into internal storage,
        /// replacing any existing entries for that stat. Used by tests to seed state without
        /// going through the public AddEquipmentModifier lifecycle API.
        /// </summary>
        public static void SetEquipmentModifiers(
            IronGrind.CharacterStats.CharacterStats stats,
            EntityID entityId,
            StatID statId,
            params EquipmentModifierEntry[] modifiers)
        {
            int statIndex = (int)statId;

            if (!stats.EntityEquipModifiers.TryGetValue(entityId, out var equipSlots))
            {
                equipSlots = new EquipmentModifierEntry[CharacterStats.StatSlotCount][];
                stats.EntityEquipModifiers[entityId] = equipSlots;
            }

            var arr = new EquipmentModifierEntry[16];
            for (int i = 0; i < modifiers.Length; i++)
                arr[i] = modifiers[i];
            equipSlots[statIndex] = arr;

            if (!stats.EntityEquipCount.TryGetValue(entityId, out var counts))
            {
                counts = new int[CharacterStats.StatSlotCount];
                stats.EntityEquipCount[entityId] = counts;
            }

            counts[statIndex] = modifiers.Length;
        }

        /// <summary>
        /// Installs buff modifiers for an entity-stat pair directly into internal storage,
        /// replacing any existing entries for that stat. Used by tests to seed state without
        /// going through the public AddBuffModifier lifecycle API.
        /// </summary>
        public static void SetBuffModifiers(
            IronGrind.CharacterStats.CharacterStats stats,
            EntityID entityId,
            StatID statId,
            params BuffModifierEntry[] modifiers)
        {
            int statIndex = (int)statId;

            if (!stats.EntityBuffModifiers.TryGetValue(entityId, out var buffSlots))
            {
                buffSlots = new BuffModifierEntry[CharacterStats.StatSlotCount][];
                stats.EntityBuffModifiers[entityId] = buffSlots;
            }

            var arr = new BuffModifierEntry[32];
            for (int i = 0; i < modifiers.Length; i++)
                arr[i] = modifiers[i];
            buffSlots[statIndex] = arr;

            if (!stats.EntityBuffCount.TryGetValue(entityId, out var counts))
            {
                counts = new int[CharacterStats.StatSlotCount];
                stats.EntityBuffCount[entityId] = counts;
            }

            counts[statIndex] = modifiers.Length;
        }

        /// <summary>
        /// Clears all modifier arrays for an entity from internal storage (all stats, both layers).
        /// Used by AC-24 hysteresis tests to verify that GetEffectiveStat has no
        /// memory of prior cap state after modifiers are removed.
        /// </summary>
        public static void ClearModifiers(
            IronGrind.CharacterStats.CharacterStats stats,
            EntityID entityId)
        {
            stats.EntityEquipModifiers.Remove(entityId);
            stats.EntityEquipCount.Remove(entityId);
            stats.EntityBuffModifiers.Remove(entityId);
            stats.EntityBuffCount.Remove(entityId);
        }
    }
}
