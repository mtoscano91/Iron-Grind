using System;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Consumable-specific sub-schema embedded in an <see cref="ItemDefinition"/> whose
    /// <see cref="ItemDefinition.ItemCategory"/> is <see cref="ItemCategory.Consumable"/>.
    /// <see cref="ItemDefinition.ConsumableData"/> is <c>null</c> for equipment items.
    /// </summary>
    /// <remarks>
    /// Declared as a <c>class</c> (not struct) so that Unity's <c>[SerializeReference]</c>
    /// on <see cref="ItemDefinition"/> can represent a true <c>null</c> for non-consumable
    /// items. Without <c>[SerializeReference]</c>, the Unity serializer would construct a
    /// default instance and null-checks would silently pass on equipment items.
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// if (item.ConsumableData != null)
    /// {
    ///     ApplyEffect(item.ConsumableData.EffectType, item.ConsumableData.EffectMagnitude);
    ///     StartCooldown(item.ConsumableData.CooldownSeconds);
    /// }
    /// </code>
    /// </remarks>
    [Serializable]
    public class ConsumableData
    {
        [SerializeField] private EffectType _effectType;
        [SerializeField] private float _effectMagnitude;
        [SerializeField] private float _cooldownSeconds;

        /// <summary>The gameplay effect triggered when this consumable is used.</summary>
        public EffectType EffectType => _effectType;

        /// <summary>
        /// Magnitude of the effect (e.g., HP restored). Interpreted by the consumable
        /// use system according to <see cref="EffectType"/>.
        /// </summary>
        public float EffectMagnitude => _effectMagnitude;

        /// <summary>
        /// Seconds before this item can be used again after one use. Zero means no cooldown.
        /// </summary>
        public float CooldownSeconds => _cooldownSeconds;
    }
}
