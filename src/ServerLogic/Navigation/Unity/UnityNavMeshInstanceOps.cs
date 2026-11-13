using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Unity adapter for <see cref="INavMeshInstanceOps"/>: reads <c>instance.valid</c> and calls
    /// <c>instance.Remove()</c> (Story 009, Story 002 seam). Holds no state.
    /// </summary>
    public sealed class UnityNavMeshInstanceOps : INavMeshInstanceOps
    {
        /// <inheritdoc/>
        public bool IsValid(NavMeshDataInstance instance)
        {
            return instance.valid;
        }

        /// <inheritdoc/>
        public void Remove(NavMeshDataInstance instance)
        {
            instance.Remove();
        }
    }
}
