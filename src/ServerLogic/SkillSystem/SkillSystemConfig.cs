using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Designer-editable Skill System knobs MANA_COST_MULTIPLIER and CAST_STALE_TICK_TOLERANCE (skill-system.md
    /// Tuning Knobs; Skill System Story 001). Systems take a <see cref="SkillSystemConfigValues"/> built by
    /// <see cref="SkillSystemConfigValues.FromAsset"/>, which clamps each knob into its safe range.
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    [CreateAssetMenu(fileName = "SkillSystemConfig", menuName = "IronGrind/Skill System Config")]
    public class SkillSystemConfig : ScriptableObject
    {
        [SerializeField] private float _manaCostMultiplier = SkillSystemConfigValues.MANA_COST_MULTIPLIER_DEFAULT;
        [SerializeField] private int _castStaleTickTolerance = SkillSystemConfigValues.CAST_STALE_TICK_TOLERANCE_DEFAULT;

        /// <summary>MANA_COST_MULTIPLIER as authored.</summary>
        public float ManaCostMultiplier => _manaCostMultiplier;

        /// <summary>CAST_STALE_TICK_TOLERANCE as authored, in ticks.</summary>
        public int CastStaleTickTolerance => _castStaleTickTolerance;

        /// <summary>Sets every knob; test seam only.</summary>
        internal void SetForTesting(float manaCostMultiplier, int castStaleTickTolerance)
        {
            _manaCostMultiplier = manaCostMultiplier;
            _castStaleTickTolerance = castStaleTickTolerance;
        }

#if UNITY_EDITOR
        // Editor feedback: logs the same out-of-range warnings FromAsset logs. Serialized values are unchanged.
        private void OnValidate()
        {
            SkillSystemConfigValues.FromAsset(this);
        }
#endif
    }
}
