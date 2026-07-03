using System;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// A single stat bonus entry embedded in <see cref="EquipmentData.StatModifiers"/>.
    /// Represents a flat additive bonus applied to one <see cref="IronGrind.CharacterStats.StatID"/>
    /// when the parent item is equipped.
    /// </summary>
    /// <remarks>
    /// Must be a <c>[Serializable]</c> struct (not a ValueTuple) so Unity's serializer
    /// can inspect and render individual fields in the Inspector. All fields are private
    /// with <c>[SerializeField]</c> — Unity 6.3 compile error if <c>[SerializeField]</c> is
    /// applied to a property.
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// foreach (var entry in item.EquipmentData.StatModifiers)
    ///     totalBonus[entry.StatId] += entry.FlatBonus;
    /// </code>
    /// </remarks>
    [Serializable]
    public struct StatModifierEntry
    {
        [SerializeField] private StatID _statId;
        [SerializeField] private float _flatBonus;

        /// <summary>The stat receiving the flat bonus.</summary>
        public StatID StatId => _statId;

        /// <summary>
        /// Additive flat bonus applied to <see cref="StatId"/> when the item is equipped.
        /// Can be negative (a penalty).
        /// </summary>
        public float FlatBonus => _flatBonus;
    }
}
