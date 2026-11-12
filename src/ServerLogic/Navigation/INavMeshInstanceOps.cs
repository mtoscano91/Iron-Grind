using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Seam over the validity check and removal of a <see cref="NavMeshDataInstance"/>, so lifecycle logic runs in
    /// EditMode where every instance is <c>default</c>. The service never reads <c>.valid</c> or calls
    /// <c>.Remove()</c> on the struct directly. The Unity adapter is Story 009.
    /// </summary>
    /// <remarks>
    /// Implementations must not cache instances: <see cref="NavMeshDataInstance"/> is a struct passed by value, so
    /// <c>Remove()</c> only clears the adapter's own copy and the caller resets its stored copy afterwards.
    /// </remarks>
    public interface INavMeshInstanceOps
    {
        /// <summary>Whether the instance refers to NavMesh data that is currently added.</summary>
        /// <param name="instance">The instance to check.</param>
        /// <returns>True when the instance is valid.</returns>
        bool IsValid(NavMeshDataInstance instance);

        /// <summary>Removes the NavMesh data the instance refers to.</summary>
        /// <param name="instance">The instance to remove.</param>
        void Remove(NavMeshDataInstance instance);
    }
}
