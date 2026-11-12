using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Zone navigation service: owns the zone's NavMesh data instance and the fixed agent pool
    /// (CR-NAV-1 to CR-NAV-4) and claims and releases agents on the <see cref="MobEventBus"/> (CR-NAV-5, CR-NAV-6):
    /// <see cref="Initialize"/>, <see cref="Teardown"/>, the torn-down guards, and the spawn and death handlers.
    /// The tick and sync bodies are Story 005, and <c>INavigationProvider</c> is implemented in Story 004.
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
    /// <para>
    /// The bus handlers are subscribed once, at the end of a successful <see cref="Initialize"/>, and removed first
    /// thing in <see cref="Teardown"/>. The bus raises over a snapshot, so a handler can still run once after
    /// <see cref="Teardown"/>: a spawn then logs an error and claims nothing, a death is an unknown-entity warning.
    /// </para>
    /// <para>
    /// A throwing agent never leaks from the pool: a failed claim disables the agent and returns it, and a release
    /// always returns it. The exception still propagates to the bus, which logs it.
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

        // Per-entity state, pre-sized to the pool in the constructor. _activeAgents is filled by claims (Story 003);
        // requests and failure counters are filled by Stories 005 and 006.
        private readonly Dictionary<EntityID, INavAgent> _activeAgents;
        private readonly Queue<EntityID> _pendingRequests = new Queue<EntityID>();
        private readonly Dictionary<EntityID, Vector3> _latestRequests;
        private readonly Dictionary<EntityID, int> _failureCounts;

        private NavMeshDataInstance _instance;
        private ZoneID _zone;
        private bool _initializeAttempted;
        private bool _initialized;
        private bool _tornDown;

        /// <summary>Creates the service. Nothing is created or subscribed until <see cref="Initialize"/>.</summary>
        /// <param name="config">The navigation config asset.</param>
        /// <param name="agentFactory">Creates the pooled agents.</param>
        /// <param name="speedSource">Mob move speed source, read on every claim.</param>
        /// <param name="meshQuery">NavMesh sampling seam (used by later stories).</param>
        /// <param name="instanceOps">Validity check and removal of the NavMesh data instance.</param>
        /// <param name="eventBus">Per-zone mob event bus; the handlers subscribe at the end of <see cref="Initialize"/>.</param>
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
            _activeAgents = new Dictionary<EntityID, INavAgent>(_config.MaxAgentPoolSize);
            _latestRequests = new Dictionary<EntityID, Vector3>(_config.MaxAgentPoolSize);
            _failureCounts = new Dictionary<EntityID, int>(_config.MaxAgentPoolSize);
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

            // Last step: a throwing Initialize subscribes nothing. Named methods, subscribed once.
            _eventBus.OnMobSpawned += HandleMobSpawned;
            _eventBus.OnMobDied += HandleMobDied;
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

            // Removing a handler that was never added is a no-op on the bus.
            _eventBus.OnMobSpawned -= HandleMobSpawned;
            _eventBus.OnMobDied -= HandleMobDied;

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

        // Claim (CR-NAV-5, ADR-003 Decisions 3, 5, 6). Check order is fixed by the story; the speed lookup runs
        // before any agent is touched. The success path builds no strings and allocates nothing.
        private void HandleMobSpawned(MobSpawnedArgs args)
        {
            if (args.Zone != _zone)
            {
                Debug.LogWarning(LOG_PREFIX + "MobSpawned for " + args.Zone + " ignored; this service serves " + _zone);
                return;
            }

            if (!IsAcceptingSpawns)
            {
                Debug.LogError(LOG_PREFIX + "post-teardown MobSpawned for " + args.Entity + " ignored; nothing claimed.");
                return;
            }

            if (_activeAgents.ContainsKey(args.Entity))
            {
                Debug.LogError(LOG_PREFIX + "Duplicate MobSpawned for " + args.Entity
                    + "; already registered, nothing claimed (EC-NAV-2).");
                return;
            }

            if (!TryResolveSpeed(args, out float speed))
            {
                return;
            }

            if (_pool.Count == 0)
            {
                Debug.LogError(LOG_PREFIX + "Agent pool exhausted: no agent for " + args.Entity
                    + "; the mob gets safe defaults (ADR-003 Decision 5).");
                return;
            }

            TryClaimAgent(args, speed);
        }

        // Speed lookup, finiteness check (before Mathf.Clamp, which passes NaN through) and CR-NAV-8 clamp. Touches
        // no agent. Returns false after logging an error when the type is unknown or the speed is not finite.
        private bool TryResolveSpeed(MobSpawnedArgs args, out float clampedSpeed)
        {
            if (!_speedSource.TryGetMoveSpeed(args.MobType, out float speed) || float.IsNaN(speed)
                || float.IsInfinity(speed))
            {
                Debug.LogError(LOG_PREFIX + "No valid move speed for " + args.Entity + " of type " + args.MobType
                    + "; mob not registered.");
                clampedSpeed = 0f;
                return false;
            }

            clampedSpeed = Mathf.Clamp(speed, NavigationConfigValues.AGENT_SPEED_MIN, NavigationConfigValues.AGENT_SPEED_MAX);
            if (clampedSpeed != speed)
            {
                Debug.LogWarning(LOG_PREFIX + "Move speed " + speed + " of " + args.MobType + " for " + args.Entity
                    + " clamped to " + clampedSpeed + " (CR-NAV-8).");
            }

            return true;
        }

        // Pool -> Registered (ADR-003 Decision 6). The pool is known to be non-empty. If any agent call throws, the
        // agent is disabled and returned to the pool and the exception propagates (the bus logs it); a false Warp
        // is a failed claim with an error log. Nothing is registered unless every step succeeded.
        private void TryClaimAgent(MobSpawnedArgs args, float speed)
        {
            INavAgent agent = _pool.Pop();
            bool warped;
            try
            {
                agent.Enabled = true;
                warped = agent.Warp(args.SpawnPosition);
                if (warped)
                {
                    agent.Speed = speed;
                    agent.IsStopped = true;
                    _activeAgents[args.Entity] = agent;
                }
            }
            catch
            {
                TryDisable(agent);
                _pool.Push(agent);
                _activeAgents.Remove(args.Entity);
                throw;
            }

            if (!warped)
            {
                TryDisable(agent);
                _pool.Push(agent);
                Debug.LogError(LOG_PREFIX + "Warp failed for " + _zone + " " + args.Entity + " at "
                    + args.SpawnPosition + "; mob not registered.");
            }
        }

        // Release (CR-NAV-6). Unknown or already released entities are a warning and a no-op. _pendingRequests is a
        // queue and is not searched; Story 005's drain skips entries for entities no longer active. The entity
        // leaves every collection first, and the agent always returns to the pool, even if an agent call throws
        // (the exception propagates and the bus logs it).
        private void HandleMobDied(MobDiedArgs args)
        {
            if (args.Zone != _zone)
            {
                Debug.LogWarning(LOG_PREFIX + "MobDied for " + args.Zone + " ignored; this service serves " + _zone);
                return;
            }

            if (!_activeAgents.TryGetValue(args.Entity, out INavAgent agent))
            {
                Debug.LogWarning(LOG_PREFIX + "MobDied for unknown or already released " + args.Entity + "; ignored.");
                return;
            }

            _activeAgents.Remove(args.Entity);
            _latestRequests.Remove(args.Entity);
            _failureCounts.Remove(args.Entity);
            try
            {
                agent.IsStopped = true;
                agent.ResetPath();
            }
            finally
            {
                TryDisable(agent);
                _pool.Push(agent);
            }
        }

        // Disables an agent; a throwing agent is logged, never propagated, so cleanup always completes.
        private static void TryDisable(INavAgent agent)
        {
            try
            {
                agent.Enabled = false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
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
                TryDisable(_createdAgents[i]);
            }
        }
    }
}
