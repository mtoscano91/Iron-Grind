using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Seam over the <see cref="NavMeshAgent"/> members the navigation service uses, so service logic runs in EditMode
    /// without a real agent. Members map 1:1 to <see cref="NavMeshAgent"/>; the Unity adapter is Story 009.
    /// </summary>
    public interface INavAgent
    {
        /// <summary>Whether the agent component is enabled.</summary>
        bool Enabled { get; set; }

        /// <summary>Whether the agent is stopped.</summary>
        bool IsStopped { get; set; }

        /// <summary>Movement speed in m/s.</summary>
        float Speed { get; set; }

        /// <summary>Whether the agent moves its transform itself.</summary>
        bool UpdatePosition { get; set; }

        /// <summary>Whether the agent rotates its transform itself.</summary>
        bool UpdateRotation { get; set; }

        /// <summary>Whether the agent slows down near its destination.</summary>
        bool AutoBraking { get; set; }

        /// <summary>Turns obstacle avoidance off for this agent.</summary>
        void DisableObstacleAvoidance();

        /// <summary>Whether the agent is on a NavMesh.</summary>
        bool IsOnNavMesh { get; }

        /// <summary>Whether a path request is still being computed.</summary>
        bool PathPending { get; }

        /// <summary>The status of the current path.</summary>
        NavMeshPathStatus PathStatus { get; }

        /// <summary>Whether the current path is stale.</summary>
        bool IsPathStale { get; }

        /// <summary>The velocity the agent wants to move with.</summary>
        Vector3 DesiredVelocity { get; }

        /// <summary>The position the simulation will move the agent to next.</summary>
        Vector3 NextPosition { get; set; }

        /// <summary>Teleports the agent onto the NavMesh at the position.</summary>
        /// <param name="position">World-space position.</param>
        /// <returns>True when the warp succeeded.</returns>
        bool Warp(Vector3 position);

        /// <summary>Requests a path to the target.</summary>
        /// <param name="target">World-space destination.</param>
        /// <returns>True when the request was accepted.</returns>
        bool SetDestination(Vector3 target);

        /// <summary>Clears the current path.</summary>
        void ResetPath();
    }
}
