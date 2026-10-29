using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Stand-in <see cref="IMovementGeometryErrorSink"/> that logs one error per report, until Zone Instancing
    /// decides how to react to a stuck entity (Movement Story 007).
    /// </summary>
    /// <example>
    /// <code>
    /// IMovementGeometryErrorSink sink = new LoggingMovementGeometryErrorSink();
    /// sink.ReportStuck(entityId, position);
    /// </code>
    /// </example>
    public sealed class LoggingMovementGeometryErrorSink : IMovementGeometryErrorSink
    {
        /// <inheritdoc/>
        public void ReportStuck(EntityID entity, Vector3 committedPosition)
        {
            Debug.LogError($"[Movement] Entity {entity.RawValue} is stuck off the NavMesh at {committedPosition}; no walkable point within the recovery radius");
        }
    }
}
