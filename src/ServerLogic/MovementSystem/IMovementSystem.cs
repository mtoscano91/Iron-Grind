using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The only way other systems change movement (movement-system.md CR-MOV-8 Prohibited state, CR-MOV-9 auto-face
    /// override; Movement Story 006). Callers are handed this interface (ADR-010: a direct call on an injected
    /// interface, no event and no static access). Reads (<c>TryGetPosition</c>, <c>TryGetFacing</c>) stay on
    /// <see cref="MovementService"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// IMovementSystem movement = service;
    /// movement.SetProhibited(playerId, true);
    /// movement.InjectFacingOverride(playerId, targetPosition - playerPosition);
    /// </code>
    /// </example>
    public interface IMovementSystem
    {
        /// <summary>
        /// Sets or clears the Prohibited state (CR-MOV-8). While set, the entity gets no position delta; facing
        /// still updates. Entry and exit do not touch a pending facing override.
        /// </summary>
        /// <param name="id">The entity.</param>
        /// <param name="prohibited">True to set, false to clear.</param>
        /// <returns>True when the entity has a movement slot; false (no log, no state change) otherwise.</returns>
        bool SetProhibited(EntityID id, bool prohibited);

        /// <summary>
        /// Queues a one-tick facing and direction override (CR-MOV-9). On the next tick the integrator uses the
        /// XZ part of <paramref name="direction"/> instead of the client intent and the stored facing points along
        /// it. A second call before that tick replaces the first. Any finite non-zero XZ part is accepted,
        /// however large or small. A zero XZ part or a non-finite component is a zero-direction override: still
        /// consumed, no movement, facing unchanged.
        /// </summary>
        /// <param name="id">The entity.</param>
        /// <param name="direction">World direction; Y is ignored.</param>
        /// <returns>True when the entity has a movement slot; false (no log, no state change) otherwise.</returns>
        bool InjectFacingOverride(EntityID id, Vector3 direction);
    }
}
