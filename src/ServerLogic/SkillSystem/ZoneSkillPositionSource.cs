using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// <see cref="ISkillPositionSource"/> over <see cref="IZoneEntityStateSource"/> (the movement-decorated source in
    /// production, so positions are this tick's). Wire positions are x100 fixed point; this divides by 100 (Story 005).
    /// </summary>
    public sealed class ZoneSkillPositionSource : ISkillPositionSource
    {
        private const float WIRE_SCALE = 100f;

        private readonly IZoneEntityStateSource _state;

        /// <summary>Creates the source.</summary>
        /// <param name="state">The zone entity state source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
        public ZoneSkillPositionSource(IZoneEntityStateSource state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <inheritdoc />
        public bool TryGetPositionXZ(EntityID entity, out float x, out float z)
        {
            if (_state.TryGetPlayerState(entity, out ZonePlayerStateView player))
            {
                x = player.PosX / WIRE_SCALE;
                z = player.PosZ / WIRE_SCALE;
                return true;
            }

            if (_state.TryGetMobState(entity, out ZoneMobStateView mob))
            {
                x = mob.PosX / WIRE_SCALE;
                z = mob.PosZ / WIRE_SCALE;
                return true;
            }

            x = 0f;
            z = 0f;
            return false;
        }
    }
}
