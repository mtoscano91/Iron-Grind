using System;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Equipment-specific sub-schema embedded in an <see cref="ItemDefinition"/> whose
    /// <see cref="ItemDefinition.ItemCategory"/> is <see cref="ItemCategory.Equipment"/>.
    /// <see cref="ItemDefinition.EquipmentData"/> is <c>null</c> for consumable items.
    /// </summary>
    /// <remarks>
    /// Declared as a <c>class</c> (not struct) so that Unity's <c>[SerializeReference]</c>
    /// on <see cref="ItemDefinition"/> can represent a true <c>null</c> for non-equipment
    /// items. Without <c>[SerializeReference]</c>, the Unity serializer would construct a
    /// default instance and null-checks would silently pass on consumable items.
    ///
    /// <para>Nullable-workaround fields: Unity's serializer cannot serialize <c>StatID?</c>
    /// or <c>ItemID?</c> directly. A companion <c>bool _hasX</c> field gates each optional
    /// value, exposed as a <c>Nullable&lt;T&gt;</c> property.</para>
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// if (item.EquipmentData?.EquipRequirementStat is StatID requiredStat)
    ///     Validate(requiredStat, item.EquipmentData.EquipRequirementMin);
    /// </code>
    /// </remarks>
    [Serializable]
    public class EquipmentData
    {
        [SerializeField] private GearSlot _gearSlot;
        [SerializeField] private GearTier _gearTier;
        [SerializeField] private StatModifierEntry[] _statModifiers;
        [SerializeField] private ElementType _elementType;
        [SerializeField] private int _elementalDamage;

        // Nullable StatID workaround — serializer cannot store Nullable<StatID>.
        [SerializeField] private bool _hasEquipRequirementStat;
        [SerializeField] private StatID _equipRequirementStat;
        [SerializeField] private float _equipRequirementMin;

        // Nullable ItemID workaround — serializer cannot store Nullable<ItemID>.
        [SerializeField] private bool _hasMergeResultItemID;
        [SerializeField] private ItemID _mergeResultItemID;

        /// <summary>Body slot this item occupies when equipped.</summary>
        public GearSlot GearSlot => _gearSlot;

        /// <summary>Material tier determining the item's stat ceiling and enhancement track.</summary>
        public GearTier GearTier => _gearTier;

        /// <summary>
        /// Flat stat bonuses granted when this item is equipped. Never <c>null</c> — returns
        /// <see cref="Array.Empty{T}"/> when no modifiers are configured.
        /// </summary>
        public StatModifierEntry[] StatModifiers => _statModifiers ?? Array.Empty<StatModifierEntry>();

        /// <summary>Elemental damage type. <see cref="ElementType.None"/> for non-elemental weapons.</summary>
        public ElementType ElementType => _elementType;

        /// <summary>Flat elemental bonus damage dealt on hit. Zero for non-elemental items.</summary>
        public int ElementalDamage => _elementalDamage;

        /// <summary>
        /// Minimum value of <see cref="EquipRequirementStat"/> the character must meet to equip
        /// this item. <c>null</c> when no stat requirement exists.
        /// </summary>
        public StatID? EquipRequirementStat =>
            _hasEquipRequirementStat ? _equipRequirementStat : (StatID?)null;

        /// <summary>
        /// Minimum threshold of <see cref="EquipRequirementStat"/> required to equip the item.
        /// Ignored when <see cref="EquipRequirementStat"/> is <c>null</c>.
        /// </summary>
        public float EquipRequirementMin => _equipRequirementMin;

        /// <summary>
        /// The <see cref="IronGrind.CharacterStats.ItemID"/> of the item produced when two copies
        /// of this item are merged. <c>null</c> when the item is not mergeable.
        /// </summary>
        public ItemID? MergeResultItemID =>
            _hasMergeResultItemID ? _mergeResultItemID : (ItemID?)null;
    }
}
