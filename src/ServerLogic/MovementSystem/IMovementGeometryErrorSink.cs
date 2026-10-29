using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Seam for the unrecoverable NavMesh stall (movement-system.md EC-MOV-11, Movement Story 007). Movement calls
    /// it once per stall when the recovery query also fails; what happens next (despawn, return to town) is Zone
    /// Instancing's decision. <see cref="LoggingMovementGeometryErrorSink"/> is the stand-in until then.
    /// </summary>
    /// <example>
    /// <code>
    /// var service = new MovementService(intents, speeds, sampler, slotIndex, tuning, new LoggingMovementGeometryErrorSink());
    /// </code>
    /// </example>
    public interface IMovementGeometryErrorSink
    {
        /// <summary>
        /// Reports an entity stuck off the NavMesh with no walkable point inside the recovery radius. It runs inside
        /// the tick loop: implementations must not throw and should return quickly.
        /// </summary>
        /// <param name="entity">The stuck entity.</param>
        /// <param name="committedPosition">Its last committed position.</param>
        void ReportStuck(EntityID entity, Vector3 committedPosition);
    }
}
