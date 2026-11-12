using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Zone navigation service: owns the zone's NavMesh data instance and the fixed agent pool
    /// (CR-NAV-1 to CR-NAV-4). This story covers the lifecycle only: <see cref="Initialize"/>, <see cref="Teardown"/>
    /// and the torn-down guards. Claiming agents is Story 003, the tick and sync bodies are Story 005, and
    /// <c>INavigationProvider</c> is implemented in Story 004.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The service owns the instance passed to the first <see cref="Initialize"/> call from the moment of the call,
    /// also when it throws, so <see cref="Teardown"/> removes a valid instance whose pool fill failed. Only the first
    /// call is accepted: a later call (a second call, a retry after a failure, or a call after
    /// <see cref="Teardown"/>) logs an error and is ignored, and the instance it carried stays with the caller.
    /// </para>
    /// <para>
    /// Main thread only, and one zone per process (ADR-002 Decision 4): neither is checked here. The main-thread
    /// check is the Story 009 PlayMode test; the composition owns one zone per process.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var service = new ZoneNavigationService(config, factory, speeds, query, ops, bus);
    /// service.Initialize(zone, instance); // startup S6
    /// service.Teardown();                 // teardown step 3
    /// </code>
    /// </example>
    public sealed class ZoneNavigationService : IZoneNavigation
    {
        private const string LOG_PREFIX = "[Navigation] ";
        private const string POOL_ROOT_PREFIX = "NavigationPool_";

        private readonly NavigationConfigValues _config;
        private readonly INavAgentFactory _agentFactory;
        private readonly INavMobSpeedSource _speedSource;
        private readonly INavMeshQuery _meshQuery;
        private readonly INavMeshInstanceOps _instanceOps;
        private readonly MobEventBus _eventBus;

        // Every agent the pool created, pooled or active, for Teardown.
        private readonly List<INavAgent> _createdAgents;
        private readonly Stack<INavAgent> _pool;

        // Filled by later stories (003 claims, 005 requests, 006 failure handling).
        private readonly Dictionary<EntityID, INavAgent> _activeAgents = new Dictionary<EntityID, INavAgent>();
        private readonly Queue<EntityID> _pendingRequests = new Queue<EntityID>();
        private readonly Dictionary<EntityID, Vector3> _latestRequests = new Dictionary<EntityID, Vector3>();
        private readonly Dictionary<EntityID, int> _failureCounts = new Dictionary<EntityID, int>();

        private NavMeshDataInstance _instance;
        private ZoneID _zone;
        private bool _initializeAttempted;
        private bool _initialized;
        private bool _tornDown;

        /// <summary>Creates the service. Nothing is created or subscribed until <see cref="Initialize"/>.</summary>
        /// <param name="config">The navigation config asset.</param>
        /// <param name="agentFactory">Creates the pooled agents.</param>
        /// <param name="speedSource">Mob move speed source (used by later stories).</param>
        /// <param name="meshQuery">NavMesh sampling seam (used by later stories).</param>
        /// <param name="instanceOps">Validity check and removal of the NavMesh data instance.</param>
        /// <param name="eventBus">Per-zone mob event bus (subscribed in Story 003).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public ZoneNavigationService(
            NavigationConfig config,
            INavAgentFactory agentFactory,
            INavMobSpeedSource speedSource,
            INavMeshQuery meshQuery,
            INavMeshInstanceOps instanceOps,
            MobEventBus eventBus)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
            _speedSource = speedSource ?? throw new ArgumentNullException(nameof(speedSource));
            _meshQuery = meshQuery ?? throw new ArgumentNullException(nameof(meshQuery));
            _instanceOps = instanceOps ?? throw new ArgumentNullException(nameof(instanceOps));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _config = NavigationConfigValues.FromAsset(config);
            _createdAgents = new List<INavAgent>(_config.MaxAgentPoolSize);
            _pool = new Stack<INavAgent>(_config.MaxAgentPoolSize);
        }

        /// <summary>Number of agents sitting in the pool (enabled false).</summary>
        public int AvailableCount => _pool.Count;

        /// <summary>Number of agents currently claimed by a mob.</summary>
        public int ActiveCount => _activeAgents.Count;

        /// <summary>
        /// True only after a successful <see cref="Initialize"/> and before <see cref="Teardown"/> (EC-NAV-4 state,
        /// read by the Story 003 spawn handler).
        /// </summary>
        internal bool IsAcceptingSpawns => _initialized && !_tornDown;

        /// <summary>Total entries across the active, pending, latest-request and counter collections.</summary>
        internal int TrackedStateCount =>
            _activeAgents.Count + _pendingRequests.Count + _latestRequests.Count + _failureCounts.Count;

        /// <summary>The stored NavMesh data instance; <c>default</c> after <see cref="Teardown"/>.</summary>
        internal NavMeshDataInstance StoredInstance => _instance;

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">The instance is invalid (AC-NAV-01).</exception>
        public void Initialize(ZoneID zone, NavMeshDataInstance instance)
        {
            if (_tornDown)
            {
                Debug.LogError(LOG_PREFIX + "Initialize called after Teardown for " + zone
                    + "; ignored, the instance stays with the caller.");
                return;
            }

            if (_initializeAttempted)
            {
                // EC-NAV-6, also a retry after a failed call: refuse before touching the stored instance or the pool.
                Debug.LogError(LOG_PREFIX + "Initialize called twice for " + zone
                    + "; the first call is kept, the new instance stays with the caller.");
                return;
            }

            // Ownership starts with the call, even if validation or the pool fill throws.
            _initializeAttempted = true;
            _instance = instance;
            _zone = zone;

            if (!_instanceOps.IsValid(instance))
            {
                Debug.LogError(LOG_PREFIX + "Initialize received an invalid NavMesh instance for " + zone
                    + "; no mob can spawn without a mesh.");
                throw new InvalidOperationException("Invalid NavMeshDataInstance for " + zone);
            }

            FillPool(POOL_ROOT_PREFIX + zone.RawValue);
            _initialized = true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Order (CR-NAV-3, AC-NAV-02): every created agent disabled, collections cleared, the instance removed if
        /// valid, then reset to <c>default</c>. <c>_tornDown</c> is set first, and the removal runs in a
        /// <c>finally</c>, so a throwing agent can neither leave the process-global NavMesh data added nor let
        /// <see cref="Tick"/> run on a half-torn service. A second call does nothing.
        /// </remarks>
        public void Teardown()
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
            try
            {
                DisableAllAgents();
                _activeAgents.Clear();
                _pendingRequests.Clear();
                _latestRequests.Clear();
                _failureCounts.Clear();
                _pool.Clear();
                _createdAgents.Clear();
            }
            finally
            {
                if (_instanceOps.IsValid(_instance))
                {
                    _instanceOps.Remove(_instance);
                }

                _instance = default;
            }
        }

        /// <summary>
        /// Per-tick work. Returns immediately after <see cref="Teardown"/>. The body is a stub until Story 005.
        /// </summary>
        public void Tick()
        {
            if (_tornDown)
            {
                return;
            }
        }

        /// <summary>
        /// Syncs agent positions to the committed positions. Returns immediately after <see cref="Teardown"/>. The
        /// body is a stub until Story 005.
        /// </summary>
        /// <param name="committedPositions">Committed world positions per entity.</param>
        public void SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3> committedPositions)
        {
            if (_tornDown)
            {
                return;
            }
        }

        /// <summary>
        /// The agent's current velocity. Returns <see cref="Vector3.zero"/> after <see cref="Teardown"/>. The live
        /// value is wired in Story 005 (provider contract in Story 004).
        /// </summary>
        /// <param name="agent">The mob entity.</param>
        /// <returns>The velocity, or zero.</returns>
        public Vector3 GetCurrentVelocity(EntityID agent)
        {
            if (_tornDown)
            {
                return Vector3.zero;
            }

            return Vector3.zero; // Story 005 replaces this with the agent's desired velocity.
        }

        private void FillPool(string rootName)
        {
            int size = _config.MaxAgentPoolSize;
            for (int i = 0; i < size; i++)
            {
                INavAgent agent = _agentFactory.Create(rootName);
                _createdAgents.Add(agent); // tracked before configuring so Teardown reaches it
                agent.UpdatePosition = false;
                agent.UpdateRotation = false;
                agent.AutoBraking = false;
                agent.DisableObstacleAvoidance();
                agent.Enabled = false;
                _pool.Push(agent);
            }
        }

        // One failing agent must not stop the others from being disabled.
        private void DisableAllAgents()
        {
            for (int i = 0; i < _createdAgents.Count; i++)
            {
                try
                {
                    _createdAgents[i].Enabled = false;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
