using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>Seam over <c>NavMesh.SamplePosition</c> (CR-NAV-13), so spawn validation runs in EditMode.</summary>
    public interface INavMeshQuery
    {
        /// <summary>Finds the nearest NavMesh point to <paramref name="position"/> within <paramref name="maxDistance"/>.</summary>
        /// <param name="position">World-space position to sample near.</param>
        /// <param name="maxDistance">Search radius in world units.</param>
        /// <param name="areaMask">NavMesh area mask.</param>
        /// <param name="hitPosition">The nearest NavMesh point when found.</param>
        /// <returns>True when a point was found.</returns>
        bool SamplePosition(Vector3 position, float maxDistance, int areaMask, out Vector3 hitPosition);
    }
}
