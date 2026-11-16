using System;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>Adapter exposing <see cref="MovementService.TryGetPosition"/> as an <see cref="IZonePlayerPositionReader"/>.</summary>
    public sealed class MovementPlayerPositionReader : IZonePlayerPositionReader
    {
        private readonly MovementService _movement;

        /// <summary>Creates the adapter.</summary>
        /// <param name="movement">The zone's movement service. Must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="movement"/> is null.</exception>
        public MovementPlayerPositionReader(MovementService movement)
        {
            _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        }

        /// <inheritdoc/>
        public bool TryGetPosition(EntityID id, out Vector3 position) => _movement.TryGetPosition(id, out position);
    }
}
