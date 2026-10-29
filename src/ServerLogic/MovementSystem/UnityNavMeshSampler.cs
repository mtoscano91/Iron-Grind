using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// <see cref="INavMeshSampler"/> over <c>NavMesh.SamplePosition</c> on all areas (CR-MOV-7). The only
    /// <c>UnityEngine.AI</c> user in the Movement System folder (AC-MOV-21); it never bakes or edits the mesh.
    /// </summary>
    /// <remarks>
    /// Main thread only (ADR-002 Decision 4). Not covered by an automated test: no baked NavMesh exists under
    /// <c>Assets/</c>, and <c>NavMesh.SamplePosition</c> in a <c>UNITY_SERVER</c> headless build is unverified, so
    /// its verification stays open under ADR-002 Verification Required.
    /// </remarks>
    /// <example>
    /// <code>
    /// INavMeshSampler sampler = new UnityNavMeshSampler();
    /// </code>
    /// </example>
    public sealed class UnityNavMeshSampler : INavMeshSampler
    {
        /// <inheritdoc />
        public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
        {
            if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, radius, NavMesh.AllAreas))
            {
                hit = navHit.position;
                return true;
            }

            hit = Vector3.zero;
            return false;
        }
    }
}
