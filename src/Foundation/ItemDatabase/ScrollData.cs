using System;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Enhancement Scroll sub-schema embedded in an <see cref="ItemDefinition"/> whose
    /// <see cref="ItemDefinition.ItemCategory"/> is <see cref="ItemCategory.Consumable"/>.
    /// <see cref="ItemDefinition.ScrollData"/> is <c>null</c> on every item that is not an
    /// Enhancement Scroll (equipment and potions).
    /// </summary>
    /// <remarks>
    /// Declared as a <c>class</c> (not struct) so that Unity's <c>[SerializeReference]</c>
    /// on <see cref="ItemDefinition"/> can represent a true <c>null</c>. Without
    /// <c>[SerializeReference]</c>, the Unity serializer would construct a default instance
    /// and <c>ScrollData != null</c> — the Enhancement System's scroll test — would be true
    /// on every record.
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// if (item.ScrollData != null &amp;&amp; item.ScrollData.TargetGearTier == gear.GearTier)
    ///     AllowEnhancement(gear, item);
    /// </code>
    /// </remarks>
    [Serializable]
    public class ScrollData
    {
        [SerializeField] private GearTier _targetGearTier;

        /// <summary>
        /// The gear tier this scroll can enhance. Must be a defined tier other than
        /// <see cref="GearTier.None"/> on a valid record.
        /// </summary>
        public GearTier TargetGearTier => _targetGearTier;

#if UNITY_EDITOR
        /// <summary>
        /// Test seam — for EditMode tests only. Constructs an instance with all fields set
        /// directly, bypassing the Unity Inspector. Must not be called outside of test code.
        /// </summary>
        /// <param name="targetGearTier">The gear tier the scroll targets. Pass
        /// <see cref="GearTier.None"/> or an undefined value to construct an intentionally
        /// invalid record for validator error-path tests.</param>
        internal static ScrollData CreateForTesting(GearTier targetGearTier)
        {
            return new ScrollData
            {
                _targetGearTier = targetGearTier
            };
        }
#endif
    }
}
