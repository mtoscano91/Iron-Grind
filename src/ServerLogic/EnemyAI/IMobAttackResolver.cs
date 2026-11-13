using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Attack seam for Enemy AI (CR-AI-6). <see cref="MobController"/> calls it once per attack, on the tick the
    /// mob enters AttackExecution. Story 010 implements it over <c>HitDetector.CheckHit</c> and <c>DamageCalculator</c>;
    /// tests use a recording fake.
    /// </summary>
    public interface IMobAttackResolver
    {
        /// <summary>
        /// Resolves one mob attack: hit detection against the target using the forward vector captured at wind-up
        /// start (CR-AI-4), then damage. Called only when the target position is readable and finite.
        /// </summary>
        /// <param name="mob">The attacking mob's entity id.</param>
        /// <param name="target">The target player's entity id.</param>
        /// <param name="mobPos">The mob's world position at the attack tick.</param>
        /// <param name="targetPos">The target's world position at the attack tick.</param>
        /// <param name="attackRange">The mob's attack range in metres.</param>
        /// <param name="capturedForward">Unit forward vector captured when the wind-up began.</param>
        void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward);
    }
}
