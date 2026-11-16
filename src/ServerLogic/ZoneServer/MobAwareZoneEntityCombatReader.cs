using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.EnemyAI;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneEntityCombatReader"/> that answers for mobs from <see cref="EnemyAISystem"/> and delegates every other
    /// entity to an inner reader (Enemy AI Story 012, AC-5; zone-instancing.md CR-ZI step 6 <c>isInCombat</c>).
    /// A mob in Pursuing, WindingUp, AttackExecution or Recovering reads <see cref="AutoAttackState.CombatActive"/>; Dormant,
    /// Returning and Dead read <see cref="AutoAttackState.Idle"/>. A mob has no auto-attack cycle: timer and cycle read 0.
    /// </summary>
    /// <remarks>Allocates nothing. Wired into the zone in Story 013.</remarks>
    /// <example>
    /// <code>
    /// IZoneEntityCombatReader reader = new MobAwareZoneEntityCombatReader(enemyAi, new AutoAttackZoneEntityCombatReader(combat));
    /// </code>
    /// </example>
    public sealed class MobAwareZoneEntityCombatReader : IZoneEntityCombatReader
    {
        private readonly EnemyAISystem _enemyAi;
        private readonly IZoneEntityCombatReader _inner;

        /// <summary>Creates the reader.</summary>
        /// <param name="enemyAi">Enemy AI, the source of mob states.</param>
        /// <param name="inner">The reader used for every entity Enemy AI does not know.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public MobAwareZoneEntityCombatReader(EnemyAISystem enemyAi, IZoneEntityCombatReader inner)
        {
            _enemyAi = enemyAi ?? throw new ArgumentNullException(nameof(enemyAi));
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <inheritdoc />
        public AutoAttackState GetState(EntityID entityId)
        {
            if (!_enemyAi.TryGetMobState(entityId, out MobState state))
            {
                return _inner.GetState(entityId);
            }

            switch (state)
            {
                case MobState.Pursuing:
                case MobState.WindingUp:
                case MobState.AttackExecution:
                case MobState.Recovering:
                    return AutoAttackState.CombatActive;
                default:
                    return AutoAttackState.Idle;
            }
        }

        /// <inheritdoc />
        public int GetActionTimer(EntityID entityId)
        {
            return _enemyAi.TryGetMobState(entityId, out MobState _) ? 0 : _inner.GetActionTimer(entityId);
        }

        /// <inheritdoc />
        public int GetCycleTicks(EntityID entityId)
        {
            return _enemyAi.TryGetMobState(entityId, out MobState _) ? 0 : _inner.GetCycleTicks(entityId);
        }
    }
}
