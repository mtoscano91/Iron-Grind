using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// Factory for creating test <see cref="ItemDefinition"/> instances in EditMode tests.
    /// Uses <see cref="ScriptableObject.CreateInstance{T}"/> followed by the internal
    /// <c>SetForTesting</c> seam to populate private fields without going through
    /// the Unity Inspector serialization path.
    /// </summary>
    /// <remarks>
    /// <b>Caller responsibility:</b> Every instance created here must be destroyed after
    /// the test via <c>Object.DestroyImmediate(instance)</c> to prevent Unity's asset
    /// registry from accumulating leaked objects between test runs.
    /// </remarks>
    internal static class ItemDefinitionBuilder
    {
        /// <summary>
        /// Creates a minimal <see cref="ItemDefinition"/> suitable for database lookup tests.
        /// </summary>
        /// <param name="itemId">Raw uint identifier. Use 0 only if specifically testing the Invalid sentinel.</param>
        /// <param name="displayName">Display name stored on the definition.</param>
        /// <param name="category">Top-level item category.</param>
        /// <param name="sellPriceGold">Gold sell value (default 10).</param>
        /// <param name="isUpgradeable">Whether the item participates in enhancement (default false).</param>
        /// <param name="stackLimit">Max items per inventory slot (default 1).</param>
        /// <param name="equipmentData">Optional equipment sub-schema; null for consumables.</param>
        /// <param name="consumableData">Optional consumable sub-schema; null for equipment.</param>
        /// <returns>
        /// A new <see cref="ItemDefinition"/> Unity object. The caller must call
        /// <c>Object.DestroyImmediate</c> on it in <c>[TearDown]</c>.
        /// </returns>
        internal static ItemDefinition Build(
            uint itemId,
            string displayName,
            ItemCategory category,
            int sellPriceGold    = 10,
            bool isUpgradeable   = false,
            int stackLimit       = 1,
            EquipmentData equipmentData   = null,
            ConsumableData consumableData = null)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.SetForTesting(
                new ItemID(itemId),
                displayName,
                category,
                sellPriceGold,
                isUpgradeable,
                stackLimit,
                equipmentData,
                consumableData);
            return def;
        }
    }
}
