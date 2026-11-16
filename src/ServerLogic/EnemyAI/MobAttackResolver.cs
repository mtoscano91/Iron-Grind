using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Production <see cref="IMobAttackResolver"/> (Enemy AI Story 010; design/gdd/enemy-ai.md Interactions, CR-AI-6;
    /// hit-detection.md CR-HD-1, CR-HD-10, CR-HD-11). One mob swing: hit check, hit-result message, damage calculation
    /// (<see cref="DamageContext.PhysicalAuto"/>, base damage = the mob's AttackPower stat), then Character Stats
    /// <c>ApplyDamage</c> and the damage output.
    /// </summary>
    /// <remarks>
    /// <para>Tick thread. No per-call allocation.</para>
    /// <para>Every <c>CheckHit</c> result is emitted, with no miss-streak suppression: mob attacks are discrete swings.
    /// A hit on a target whose current HP is not above 0 stops after the hit result: no damage calculation, no
    /// <c>ApplyDamage</c>, no damage output.</para>
    /// <para>The resolver ignores <see cref="DamageResult.IsKill"/>: a player kill is signalled by Character Stats
    /// <c>OnEntityDied</c> (Death and Respawn owns the consequence); no XP is awarded and no event is raised here.</para>
    /// <para><c>CharacterStats.ApplyDamage</c> called while Character Stats is firing an event throws
    /// <see cref="InvalidOperationException"/> in Editor and development builds and silently skips the damage in release
    /// builds, so this resolver must not run from inside an <c>OnStatChanged</c> / <c>OnEntityDied</c> handler. The
    /// enemy AI tick does not.</para>
    /// <para>Exceptions propagate to the per-controller catch in <c>EnemyAISystem.Run</c>. Damage is applied before the
    /// damage output is sent, so a <c>DamageEvent</c> never reports damage that was not applied; the cost is that an
    /// <c>OnEntityDied</c> subscriber that throws inside <c>ApplyDamage</c> leaves the HP at 0 (Character Stats stays
    /// consistent) but the killing blow's <c>DamageEvent</c> is not sent.</para>
    /// <para>The attack range is not sanitised, unlike the player side (whose range is a mutable stat): it is the
    /// immutable <c>MobDefinition.AttackRange</c>, which <c>MobDefinitionValidator</c> requires to be finite and at
    /// least <c>ATTACK_RANGE_MIN</c>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var resolver = new MobAttackResolver(hitDetector, hitEmitter, calculator, stats, damageOutput);
    /// </code>
    /// </example>
    public sealed class MobAttackResolver : IMobAttackResolver
    {
        private readonly HitDetector _hitDetector;
        private readonly IHitResultEmitter _hitEmitter;
        private readonly IAutoAttackDamageCalculator _calculator;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly IAutoAttackDamageOutput _output;

        /// <summary>Creates the resolver.</summary>
        /// <param name="hitDetector">Hit check (range and facing).</param>
        /// <param name="hitEmitter">Sends the <c>HitCheckResult</c> for every attack.</param>
        /// <param name="calculator">Damage seam; the real one is <c>DamageCalculatorAutoAttackAdapter</c>.</param>
        /// <param name="stats">Character Stats: AttackPower read and damage applied.</param>
        /// <param name="output">Sends the <c>DamageEvent</c> of an applied hit.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public MobAttackResolver(
            HitDetector hitDetector,
            IHitResultEmitter hitEmitter,
            IAutoAttackDamageCalculator calculator,
            IronGrind.CharacterStats.CharacterStats stats,
            IAutoAttackDamageOutput output)
        {
            _hitDetector = hitDetector ?? throw new ArgumentNullException(nameof(hitDetector));
            _hitEmitter = hitEmitter ?? throw new ArgumentNullException(nameof(hitEmitter));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <inheritdoc />
        public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)
        {
            HitResult hit = _hitDetector.CheckHit(mobPos, targetPos, attackRange, capturedForward);
            _hitEmitter.Emit(mob, target, hit);
            if (hit != HitResult.Hit)
            {
                return;
            }

            // Dead target (HP 0, NaN or no record): the hit result is sent, but there is nothing to damage, so no
            // calculation (no dead-entity error), no ApplyDamage and no DamageEvent. MobController already skips dead
            // targets; this guards direct callers (user decision 2026-11-16, Story 010 code review).
            if (!(_stats.GetCurrentHP(target) > 0f))
            {
                return;
            }

            DamageResult damage = _calculator.Calculate(_stats.GetEffectiveStat(mob, StatID.AttackPower), mob, target);
            if (damage.FinalDamage <= 0)
            {
                return;
            }

            _stats.ApplyDamage(target, damage.FinalDamage);
            _output.Emit(mob, target, in damage);
        }
    }
}
