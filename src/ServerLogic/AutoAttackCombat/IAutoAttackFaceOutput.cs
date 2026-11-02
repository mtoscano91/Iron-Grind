using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Seam through which the combat system snaps an attacker's facing toward its target on the first auto after an
    /// activation (auto-attack-combat.md Rules 20-22; Auto-Attack Combat Story 009). The production implementation
    /// is <see cref="AutoFaceEmitter"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// faceOutput.Face(attackerId, targetPosition - attackerPosition);
    /// </code>
    /// </example>
    public interface IAutoAttackFaceOutput
    {
        /// <summary>
        /// Requests a facing snap for <paramref name="attacker"/> along <paramref name="direction"/> (world units, Y
        /// ignored). A zero or non-finite XZ part is a consumed no-op: nothing is applied or sent.
        /// </summary>
        /// <param name="attacker">The player whose facing snaps.</param>
        /// <param name="direction">Vector from the attacker toward the target.</param>
        void Face(EntityID attacker, Vector3 direction);
    }
}
