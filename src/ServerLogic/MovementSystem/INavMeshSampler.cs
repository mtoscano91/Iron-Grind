using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Seam over <c>NavMesh.SamplePosition</c> (movement-system.md CR-MOV-7, ADR-002 Decision 4) so the tick step
    /// is testable without a baked NavMesh. The production adapter is <see cref="UnityNavMeshSampler"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// if (sampler.TrySample(candidate, MovementService.NAVMESH_SAMPLE_RADIUS, out Vector3 hit))
    ///     committed = hit;
    /// </code>
    /// </example>
    public interface INavMeshSampler
    {
        /// <summary>Finds the nearest walkable point to <paramref name="candidate"/> within <paramref name="radius"/>.</summary>
        /// <param name="candidate">The integrated position to validate.</param>
        /// <param name="radius">Maximum search distance in meters.</param>
        /// <param name="hit">The walkable point (including its Y) when found; otherwise <see cref="Vector3.zero"/>.</param>
        /// <returns>True when a walkable point was found; false when the candidate is off the mesh.</returns>
        bool TrySample(Vector3 candidate, float radius, out Vector3 hit);
    }
}
