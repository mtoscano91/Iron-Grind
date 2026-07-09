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

#if UNITY_EDITOR
        /// <summary>
        /// Test seam — for EditMode tests only. Constructs an instance with both fields set
        /// directly, bypassing the Unity Inspector. Must not be called outside of test code.
        /// </summary>
        /// <param name="statId">The stat receiving the flat bonus.</param>
        /// <param name="flatBonus">The additive flat bonus value. May be zero or negative for
        /// error/warning-path validator tests.</param>
        internal static StatModifierEntry CreateForTesting(StatID statId, float flatBonus)
        {
            return new StatModifierEntry
            {
                _statId = statId,
                _flatBonus = flatBonus
            };
        }
#endif
    }
}
