using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Unity adapter for <see cref="INavAgent"/>: wraps one <see cref="NavMeshAgent"/> 1:1 (Story 009, ADR-002
    /// Decision 5, ADR-003 Decision 1). Every member forwards to the wrapped agent; no state is cached.
    /// </summary>
    public sealed class UnityNavAgent : INavAgent
    {
        /// <summary>Wraps the agent.</summary>
        /// <param name="agent">The agent to wrap; not null.</param>
        public UnityNavAgent(NavMeshAgent agent)
        {
            Agent = agent;
        }

        /// <summary>The wrapped agent (exposed for tests and the factory).</summary>
        public NavMeshAgent Agent { get; }

        /// <inheritdoc/>
        public bool Enabled
        {
            get => Agent.enabled;
            set => Agent.enabled = value;
        }

        /// <inheritdoc/>
        public bool IsStopped
        {
            get => Agent.isStopped;
            set => Agent.isStopped = value;
        }

        /// <inheritdoc/>
        public float Speed
        {
            get => Agent.speed;
            set => Agent.speed = value;
        }

        /// <inheritdoc/>
        public bool UpdatePosition
        {
            get => Agent.updatePosition;
            set => Agent.updatePosition = value;
        }

        /// <inheritdoc/>
        public bool UpdateRotation
        {
            get => Agent.updateRotation;
            set => Agent.updateRotation = value;
        }

        /// <inheritdoc/>
        public bool AutoBraking
        {
            get => Agent.autoBraking;
            set => Agent.autoBraking = value;
        }

        /// <inheritdoc/>
        public void DisableObstacleAvoidance()
        {
            Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        }

        /// <inheritdoc/>
        public bool IsOnNavMesh => Agent.isOnNavMesh;

        /// <inheritdoc/>
        public bool PathPending => Agent.pathPending;

        /// <inheritdoc/>
        public NavMeshPathStatus PathStatus => Agent.pathStatus;

        /// <inheritdoc/>
        public bool IsPathStale => Agent.isPathStale;

        /// <inheritdoc/>
        public Vector3 DesiredVelocity => Agent.desiredVelocity;

        /// <inheritdoc/>
        public Vector3 NextPosition
        {
            get => Agent.nextPosition;
            set => Agent.nextPosition = value;
        }

        /// <inheritdoc/>
        public bool Warp(Vector3 position)
        {
            return Agent.Warp(position);
        }

        /// <inheritdoc/>
        public bool SetDestination(Vector3 target)
        {
            return Agent.SetDestination(target);
        }

        /// <inheritdoc/>
        public void ResetPath()
        {
            Agent.ResetPath();
        }
    }
}
