using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// <see cref="ISkillTargetSource"/> over the zone's slot pool (in zone, kind) and the zone entity state source
    /// (alive = CurrentHp &gt; 0). Same precedent as <c>StandInAutoAttackTargetValidity</c> (Story 005).
    /// </summary>
    /// <remarks>
    /// A player or mob without state is reported as not alive. MVP gap carried to the Party System epic: every player
    /// counts as friendly (<c>IPartyZonePresence</c> is not queried).
    /// </remarks>
    public sealed class ZoneSkillTargetSource : ISkillTargetSource
    {
        private readonly ZoneSlotPool _pool;
        private readonly IZoneEntityStateSource _state;

        /// <summary>Creates the source.</summary>
        /// <param name="pool">The zone's slot pool.</param>
        /// <param name="state">The zone entity state source (use the movement-decorated one in the composition).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ZoneSkillTargetSource(ZoneSlotPool pool, IZoneEntityStateSource state)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <inheritdoc />
        public bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive)
        {
            if (_pool.TryGetPlayerSlot(target, out _))
            {
                kind = SkillTargetKind.Player;
                isAlive = _state.TryGetPlayerState(target, out ZonePlayerStateView player) && player.CurrentHp > 0;
                return true;
            }

            if (_pool.TryGetMobSlot(target, out _))
            {
                kind = SkillTargetKind.Mob;
                isAlive = _state.TryGetMobState(target, out ZoneMobStateView mob) && mob.CurrentHp > 0;
                return true;
            }

            kind = default;
            isAlive = false;
            return false;
        }
    }
}
