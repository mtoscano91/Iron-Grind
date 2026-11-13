using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>Unity adapter for <see cref="INavMeshQuery"/>: forwards to <c>NavMesh.SamplePosition</c> (Story 009).</summary>
    public sealed class UnityNavMeshQuery : INavMeshQuery
    {
        /// <inheritdoc/>
        /// <remarks>The area mask is forwarded unchanged; <paramref name="hitPosition"/> is <c>default</c> on a miss.</remarks>
        public bool SamplePosition(Vector3 position, float maxDistance, int areaMask, out Vector3 hitPosition)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, areaMask))
            {
                hitPosition = hit.position;
                return true;
            }

            hitPosition = default;
            return false;
        }
    }
}
