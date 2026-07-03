using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Immutable data record for a single item type. Created as a Unity
    /// <see cref="ScriptableObject"/> asset and loaded into the <see cref="ItemDatabase"/>
    /// at runtime. One instance exists per item type across the entire application lifetime.
    /// </summary>
    /// <remarks>
    /// <para><b>Serialization constraints (Unity 6.3):</b>
    /// <list type="bullet">
    ///   <item><c>[SerializeField]</c> is applied to private fields only — applying it to
    ///   properties is a compile error in Unity 6.3.</item>
    ///   <item><see cref="_equipmentData"/> and <see cref="_consumableData"/> use
    ///   <c>[SerializeReference]</c> (not <c>[SerializeField]</c>) so the Unity serializer
    ///   preserves a true <c>null</c> reference for the inapplicable sub-schema. Without
    ///   <c>[SerializeReference]</c> the serializer instantiates a default object, causing
    ///   null-checks to silently pass on the wrong category.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// var def = itemDatabase.GetItem(new ItemID(42u));
    /// if (def?.EquipmentData != null)
    ///     ApplyEquipmentBonuses(entity, def.EquipmentData);
    /// </code>
    /// </remarks>
    [CreateAssetMenu(fileName = "NewItem", menuName = "IronGrind/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [SerializeField] private ItemID _itemId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _description;
        [SerializeField] private string _iconAddress;
        [SerializeField] private ItemCategory _itemCategory;
        [SerializeField] private int _sellPriceGold;
        [SerializeField] private bool _isUpgradeable;
        [SerializeField] private int _stackLimit;

        // [SerializeReference] — required for true null on the inapplicable sub-schema.
        // DO NOT change to [SerializeField]; see class remarks.
        [SerializeReference] private EquipmentData _equipmentData;
        [SerializeReference] private ConsumableData _consumableData;

        /// <summary>Unique identifier for this item type. Must match the key registered in the Item Database.</summary>
        public ItemID ItemId => _itemId;

        /// <summary>Localisation-ready display name shown in the player's inventory UI.</summary>
        public string DisplayName => _displayName;

        /// <summary>Flavour text shown in the item tooltip.</summary>
        public string Description => _description;

        /// <summary>
        /// Addressables address of the item's icon sprite. Resolved at runtime by the
        /// asset-loading layer — not loaded eagerly by this definition.
        /// </summary>
        public string IconAddress => _iconAddress;

        /// <summary>Top-level category. Determines which sub-schema is populated.</summary>
        public ItemCategory ItemCategory => _itemCategory;

        /// <summary>Gold value when sold to a vendor. Must be &gt;= 0.</summary>
        public int SellPriceGold => _sellPriceGold;

        /// <summary>Whether this item can enter the enhancement system.</summary>
        public bool IsUpgradeable => _isUpgradeable;

        /// <summary>
        /// Maximum number of this item that can occupy a single inventory slot.
        /// 1 for non-stackable items; &gt;1 for stackable consumables.
        /// </summary>
        public int StackLimit => _stackLimit;

        /// <summary>
        /// Equipment-specific data. <c>null</c> when <see cref="ItemCategory"/> is not
        /// <see cref="ItemCategory.Equipment"/>. Always check for <c>null</c> before use.
        /// </summary>
        public EquipmentData EquipmentData => _equipmentData;

        /// <summary>
        /// Consumable-specific data. <c>null</c> when <see cref="ItemCategory"/> is not
        /// <see cref="ItemCategory.Consumable"/>. Always check for <c>null</c> before use.
        /// </summary>
        public ConsumableData ConsumableData => _consumableData;

#if UNITY_EDITOR
        /// <summary>
        /// Test seam — for EditMode tests only. Sets all private fields directly,
        /// bypassing the Unity Inspector. Must not be called outside of test code.
        /// </summary>
        /// <param name="itemId">The item's unique identifier.</param>
        /// <param name="displayName">Display name shown in UI.</param>
        /// <param name="category">Top-level item category.</param>
        /// <param name="sellPriceGold">Gold sell value.</param>
        /// <param name="isUpgradeable">Whether the item enters the enhancement system.</param>
        /// <param name="stackLimit">Max stack size per inventory slot.</param>
        /// <param name="equipmentData">Equipment sub-schema; null for consumables.</param>
        /// <param name="consumableData">Consumable sub-schema; null for equipment.</param>
        internal void SetForTesting(
            ItemID itemId,
            string displayName,
            ItemCategory category,
            int sellPriceGold,
            bool isUpgradeable,
            int stackLimit,
            EquipmentData equipmentData = null,
            ConsumableData consumableData = null)
        {
            _itemId          = itemId;
            _displayName     = displayName;
            _itemCategory    = category;
            _sellPriceGold   = sellPriceGold;
            _isUpgradeable   = isUpgradeable;
            _stackLimit      = stackLimit;
            _equipmentData   = equipmentData;
            _consumableData  = consumableData;
        }
#endif
    }
}
