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
    /// Implements <see cref="INavigationProvider"/> and runs navigation phases 1 and 2 through <see cref="Tick"/> and
    /// <see cref="SyncAgentPositions(IReadOnlyDictionary{EntityID, Vector3})"/>, called from <see cref="NavigationPhaseStep"/>.
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
    public sealed class ZoneNavigationService : IZoneNavigation, INavigationProvider
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

        // _queuedEntities: entities with a slot in _pendingRequests (the drain in Tick removes on dequeue).
        // _destinationRequested: entities for which SetDestination was called since the claim (IsPathStale "never set").
        private readonly HashSet<EntityID> _queuedEntities;
        private readonly HashSet<EntityID> _destinationRequested;

        // Per-entity path-health state (Story 006), one entry per registered agent: created at claim, removed at release
        // and Teardown. One dictionary of a private struct keeps TrackedStateCount simple. The health pass iterates
        // _activeAgents and writes here (a different dictionary), so no collection is modified while enumerated.
        private struct PathHealthState
        {
            public int PartialCount;
            public int OffMeshCount;
            public bool OffMeshWarned;
            public bool OffMeshErrorLogged;
            public Vector3 LastCommittedPosition;
            public bool HasTarget;
            public Vector3 LastTarget;
            public bool LastCallAccepted;
            public int StuckStreak;
            public bool StuckLogged;
        }

        private readonly Dictionary<EntityID, PathHealthState> _health;

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
            _queuedEntities = new HashSet<EntityID>(_config.MaxAgentPoolSize);
            _destinationRequested = new HashSet<EntityID>(_config.MaxAgentPoolSize);
            _health = new Dictionary<EntityID, PathHealthState>(_config.MaxAgentPoolSize);
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

        /// <summary>
        /// Total entries across the active, pending, latest-request, failure-counter, queued, requested and
        /// path-health (one entry per registered agent) collections.
        /// </summary>
        internal int TrackedStateCount =>
            _activeAgents.Count + _pendingRequests.Count + _latestRequests.Count + _failureCounts.Count
            + _queuedEntities.Count + _destinationRequested.Count + _health.Count;

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
                _queuedEntities.Clear();
                _destinationRequested.Clear();
                _health.Clear();
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
        /// Phase 2: runs the path-health pass (CR-NAV-15, EC-NAV-8, EC-NAV-10), then drains the path request queue
        /// (CR-NAV-14, ADR-003 Decision 4). Returns immediately after <see cref="Teardown"/>. Call only from
        /// <see cref="NavigationPhaseStep"/>.
        /// </summary>
        /// <remarks>
        /// Phase ownership: phase 1 (<see cref="SyncAgentPositions(Dictionary{EntityID, Vector3})"/>) only records the
        /// last committed position; phase 2 (this method) owns the health counters and every <c>Warp</c> after claim:
        /// health pass first, then the drain, so an agent issued this tick ends the tick with a partial count of 0.
        /// Issues at most MAX_PATH_UPDATES_PER_TICK agent calls; skipped entries do not count. Every dequeue, issued or
        /// skipped, removes the entity from the queued set. A throwing agent propagates out of the tick.
        /// </remarks>
        public void Tick()
        {
            if (_tornDown)
            {
                return;
            }

            RunHealthPass();

            int issued = 0;
            while (issued < _config.MaxPathUpdatesPerTick && _pendingRequests.Count > 0)
            {
                EntityID entity = _pendingRequests.Dequeue();
                _queuedEntities.Remove(entity);
                if (TryIssue(entity))
                {
                    issued++;
                }
            }
        }

        // Issues the agent call for one dequeued entity. Skips (false): not registered, off the mesh (the latest
        // request is kept so Enemy AI re-requests), no latest request (stopped or released). The latest request is
        // removed before the agent call (a deliberate order change from the story text, so a throwing agent leaves
        // consistent state); isStopped is cleared first. A false return logs one warning and still counts (true);
        // a throw propagates. Never reads velocity.
        private bool TryIssue(EntityID entity)
        {
            if (!_activeAgents.TryGetValue(entity, out INavAgent agent))
            {
                return false;
            }

            if (!agent.IsOnNavMesh)
            {
                return false;
            }

            if (!_latestRequests.TryGetValue(entity, out Vector3 destination))
            {
                return false;
            }

            _latestRequests.Remove(entity);
            agent.IsStopped = false;
            bool accepted = agent.SetDestination(destination);

            // Failure counter: consecutive rejected calls. Warn only on the 0 -> 1 transition; an accepted call removes
            // the entry (absent == 0).
            if (accepted)
            {
                _failureCounts.Remove(entity);
            }
            else
            {
                _failureCounts.TryGetValue(entity, out int failures);
                _failureCounts[entity] = failures + 1;
                if (failures == 0)
                {
                    Debug.LogWarning(LOG_PREFIX + "SetDestination rejected by the agent for " + entity + " target "
                        + destination + ".");
                }
            }

            // Health hook (CR-NAV-15, EC-NAV-8): any issued call resets the partial count; a different target resets the
            // stuck streak; remember the target and whether the call was accepted.
            if (_health.TryGetValue(entity, out PathHealthState state))
            {
                state.PartialCount = 0;
                // Exact component equality on purpose: Vector3 operator != has a ~1e-5 tolerance and would treat a
                // slightly different target as unchanged.
                if (!state.HasTarget || !state.LastTarget.Equals(destination))
                {
                    state.StuckStreak = 0;
                    state.StuckLogged = false;
                }

                state.HasTarget = true;
                state.LastTarget = destination;
                state.LastCallAccepted = accepted;
                _health[entity] = state;
            }

            return true;
        }

        // Health pass (phase 2, before the drain). Iterates _activeAgents and writes only to _health (and removes from
        // _destinationRequested, a different collection, after a successful recovery Warp).
        // Off-mesh (TryRecoverOffMesh): count consecutive off-mesh ticks; at NavOffMeshRecoveryTicks Warp to the last
        // committed position and restart the count (retry every N ticks); one warning per streak, one error per streak
        // when Warp returns false; a throwing Warp propagates. Off-mesh ticks do no partial / stuck counting, and
        // neither do pending ticks (UpdatePathCounters).
        private void RunHealthPass()
        {
            foreach (KeyValuePair<EntityID, INavAgent> pair in _activeAgents)
            {
                EntityID entity = pair.Key;
                INavAgent agent = pair.Value;
                if (!_health.TryGetValue(entity, out PathHealthState state))
                {
                    continue;
                }

                if (!agent.IsOnNavMesh)
                {
                    TryRecoverOffMesh(entity, agent, ref state);
                    continue;
                }

                state.OffMeshCount = 0;
                state.OffMeshWarned = false;
                state.OffMeshErrorLogged = false;

                if (!agent.PathPending)
                {
                    UpdatePathCounters(entity, agent, ref state);
                }

                _health[entity] = state;
            }
        }

        // One off-mesh tick. Returns true only when a recovery Warp was made and succeeded. Always stores the state.
        private bool TryRecoverOffMesh(EntityID entity, INavAgent agent, ref PathHealthState state)
        {
            state.OffMeshCount++;
            if (state.OffMeshCount < _config.NavOffMeshRecoveryTicks)
            {
                _health[entity] = state;
                return false;
            }

            state.OffMeshCount = 0;
            bool firstOfStreak = !state.OffMeshWarned;
            state.OffMeshWarned = true;
            Vector3 warpTo = state.LastCommittedPosition;
            _health[entity] = state; // stored before Warp, which may throw
            if (firstOfStreak)
            {
                Debug.LogWarning(LOG_PREFIX + "Agent for " + _zone + " " + entity
                    + " off the NavMesh; warping to last committed position " + warpTo + " (EC-NAV-10).");
            }

            if (agent.Warp(warpTo))
            {
                ResetAfterRecoveryWarp(entity, ref state);
                return true;
            }

            if (!state.OffMeshErrorLogged)
            {
                state.OffMeshErrorLogged = true; // one error per off-mesh streak, cleared when back on the mesh
                _health[entity] = state;
                Debug.LogError(LOG_PREFIX + "Off-mesh recovery Warp failed for " + _zone + " " + entity + " at "
                    + warpTo + ".");
            }

            return false;
        }

        // A real NavMeshAgent.Warp clears the path (verified in Story 009), so the old request is gone: report "never
        // set" (IsPathStale true) and let Enemy AI re-request. Counters restart with the new position.
        private void ResetAfterRecoveryWarp(EntityID entity, ref PathHealthState state)
        {
            _destinationRequested.Remove(entity);
            state.PartialCount = 0;
            state.StuckStreak = 0;
            state.StuckLogged = false;
            state.HasTarget = false;
            _health[entity] = state;
        }

        // Partial and stuck counters for an on-mesh, non-pending agent.
        // Partial: PathPartial and not stopped -> +1; PathComplete -> 0; PathInvalid leaves the count unchanged
        // (IsPathStale is already true through the PathInvalid row).
        // Stuck rule: the entity has an issued target, is not stopped, and (its last issued call was rejected OR
        // PathStatus is PathInvalid) -> streak +1 (target changes reset it in TryIssue); a valid path with an accepted
        // call resets it. A stopped agent is deliberately idle: streak reset, no counting (Stop also clears the target).
        // One error per streak at NavStuckRerouteDetectionTicks; no other action.
        private void UpdatePathCounters(EntityID entity, INavAgent agent, ref PathHealthState state)
        {
            NavMeshPathStatus status = agent.PathStatus;
            bool stopped = agent.IsStopped;
            if (status == NavMeshPathStatus.PathPartial)
            {
                if (!stopped)
                {
                    state.PartialCount++;
                }
            }
            else if (status == NavMeshPathStatus.PathComplete)
            {
                state.PartialCount = 0;
            }

            if (!state.HasTarget)
            {
                return;
            }

            // Reset: stopped (deliberately idle), or a valid path with an accepted call. Otherwise the streak grows.
            if (stopped || (state.LastCallAccepted && status != NavMeshPathStatus.PathInvalid))
            {
                state.StuckStreak = 0;
                state.StuckLogged = false;
                return;
            }

            state.StuckStreak++;
            if (state.StuckStreak >= _config.NavStuckRerouteDetectionTicks && !state.StuckLogged)
            {
                state.StuckLogged = true;
                Debug.LogError(LOG_PREFIX + "Agent for " + _zone + " " + entity + " stuck: no valid path to "
                    + state.LastTarget + " for " + state.StuckStreak + " ticks (EC-NAV-8).");
            }
        }

        /// <summary>
        /// Phase 1: sets each registered, on-mesh agent's nextPosition to its committed position (CR-NAV-12). Never
        /// warps; unknown and off-mesh entities are skipped silently. Returns immediately after
        /// <see cref="Teardown"/>. Call only from <see cref="NavigationPhaseStep"/>.
        /// </summary>
        /// <remarks>
        /// This overload enumerates through the interface, which boxes the enumerator; the phase step uses the
        /// internal Dictionary overload. A null argument is a no-op.
        /// </remarks>
        /// <param name="committedPositions">Committed world positions per entity.</param>
        public void SyncAgentPositions(IReadOnlyDictionary<EntityID, Vector3> committedPositions)
        {
            if (_tornDown || committedPositions == null)
            {
                return;
            }

            foreach (KeyValuePair<EntityID, Vector3> pair in committedPositions)
            {
                SyncOne(pair.Key, pair.Value);
            }
        }

        /// <summary>Allocation-free sync over the concrete dictionary the phase step reuses.</summary>
        internal void SyncAgentPositions(Dictionary<EntityID, Vector3> committedPositions)
        {
            if (_tornDown || committedPositions == null)
            {
                return;
            }

            foreach (KeyValuePair<EntityID, Vector3> pair in committedPositions)
            {
                SyncOne(pair.Key, pair.Value);
            }
        }

        private void SyncOne(EntityID entity, Vector3 position)
        {
            if (_activeAgents.TryGetValue(entity, out INavAgent agent) && agent.IsOnNavMesh)
            {
                agent.NextPosition = position;
                if (_health.TryGetValue(entity, out PathHealthState state))
                {
                    state.LastCommittedPosition = position;
                    _health[entity] = state;
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Only queues: stores the latest target (latest wins) and one queue slot per entity; the drain in Tick
        /// issues the real agent call. Unregistered entity or non-finite target: one warning, nothing queued.
        /// </remarks>
        public void SetDestination(EntityID agent, Vector3 target)
        {
            if (_tornDown || !TryGetAgent(agent, nameof(SetDestination), out _))
            {
                return;
            }

            if (!IsFinite(target))
            {
                Debug.LogWarning(LOG_PREFIX + "SetDestination for " + agent + " ignored: non-finite target " + target + ".");
                return;
            }

            _latestRequests[agent] = target;
            _destinationRequested.Add(agent);
            if (_queuedEntities.Add(agent))
            {
                _pendingRequests.Enqueue(agent);
            }
        }

        /// <inheritdoc/>
        /// <remarks>Clamps to the CR-NAV-8 range with one warning when clamped; a non-finite speed leaves the speed unchanged.</remarks>
        public void SetSpeed(EntityID agent, float speed)
        {
            if (_tornDown || !TryGetAgent(agent, nameof(SetSpeed), out INavAgent nav))
            {
                return;
            }

            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                Debug.LogWarning(LOG_PREFIX + "SetSpeed for " + agent + " ignored: non-finite speed " + speed + ".");
                return;
            }

            float clamped = Mathf.Clamp(speed, NavigationConfigValues.AGENT_SPEED_MIN, NavigationConfigValues.AGENT_SPEED_MAX);
            if (clamped != speed)
            {
                Debug.LogWarning(LOG_PREFIX + "SetSpeed " + speed + " for " + agent + " clamped to " + clamped + " (CR-NAV-8).");
            }

            nav.Speed = clamped;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Sets isStopped and keeps the current path (no ResetPath). A request not yet drained is discarded: the latest
        /// request is dropped (CR-NAV-9, EC-NAV-1) and <see cref="Resume"/> does not bring it back. A queue slot stays;
        /// the drain skips it.
        /// </remarks>
        public void Stop(EntityID agent)
        {
            if (_tornDown || !TryGetAgent(agent, nameof(Stop), out INavAgent nav))
            {
                return;
            }

            nav.IsStopped = true;
            _latestRequests.Remove(agent);

            // Forget the issued target so a rejected last call cannot keep growing the stuck streak while stopped.
            if (_health.TryGetValue(agent, out PathHealthState state))
            {
                state.HasTarget = false;
                state.StuckStreak = 0;
                state.StuckLogged = false;
                _health[agent] = state;
            }
        }

        /// <inheritdoc/>
        /// <remarks>Clears isStopped only; the preserved path is used, nothing is queued (CR-NAV-16).</remarks>
        public void Resume(EntityID agent)
        {
            if (_tornDown || !TryGetAgent(agent, nameof(Resume), out INavAgent nav))
            {
                return;
            }

            nav.IsStopped = false;
        }

        /// <inheritdoc/>
        /// <remarks>O(1), property reads only. Unregistered or torn down: true.</remarks>
        public bool IsPathStale(EntityID agent)
        {
            if (_tornDown || !TryGetAgent(agent, nameof(IsPathStale), out INavAgent nav))
            {
                return true;
            }

            // Precedence (CR-NAV-10): not registered (above) -> true; off the mesh -> true; a pending path -> false
            // (wins over invalid / never set); PathInvalid, the agent's own stale flag, or never requested -> true;
            // partial with count >= PartialPathTimeoutTicks -> true (F-NAV-2); otherwise false.
            // Between SetDestination and the drain the agent has not seen the request yet, so this reports the old
            // path's status (async by design, ADR-002 Decision 2).
            if (!nav.IsOnNavMesh)
            {
                return true;
            }

            if (nav.PathPending)
            {
                return false;
            }

            NavMeshPathStatus status = nav.PathStatus;
            if (status == NavMeshPathStatus.PathInvalid || nav.IsPathStale || !_destinationRequested.Contains(agent))
            {
                return true;
            }

            return status == NavMeshPathStatus.PathPartial && _health.TryGetValue(agent, out PathHealthState state)
                && state.PartialCount >= _config.PartialPathTimeoutTicks;
        }

        /// <summary>Consecutive partial-path health ticks for the entity; 0 when unregistered (test accessor).</summary>
        internal int PartialPathTickCount(EntityID agent) => _health.TryGetValue(agent, out PathHealthState s) ? s.PartialCount : 0;

        /// <summary>Consecutive off-mesh health ticks since the last warp or on-mesh tick; 0 when unregistered (test accessor).</summary>
        internal int OffMeshTickCount(EntityID agent) => _health.TryGetValue(agent, out PathHealthState s) ? s.OffMeshCount : 0;

        /// <summary>Current stuck re-route streak; 0 when unregistered (test accessor).</summary>
        internal int StuckStreak(EntityID agent) => _health.TryGetValue(agent, out PathHealthState s) ? s.StuckStreak : 0;

        /// <summary>Last committed position (spawn position until the first on-mesh sync); zero when unregistered (test accessor).</summary>
        internal Vector3 LastKnownCommittedPosition(EntityID agent) =>
            _health.TryGetValue(agent, out PathHealthState s) ? s.LastCommittedPosition : Vector3.zero;

        /// <summary>Consecutive rejected drain calls for the entity; 0 when none (test accessor).</summary>
        internal int FailureCount(EntityID agent) => _failureCounts.TryGetValue(agent, out int n) ? n : 0;

        /// <summary>
        /// The agent's desired velocity on the XZ plane (CR-NAV-11); never the smoothed <c>velocity</c>. The real agent
        /// zeroes <c>desiredVelocity</c> while stopped, so there is no stopped branch. Zero when the entity is not
        /// registered (one warning) or after <see cref="Teardown"/>.
        /// </summary>
        /// <param name="agent">The mob entity.</param>
        /// <returns>The velocity with Y set to zero, or zero.</returns>
        public Vector3 GetCurrentVelocity(EntityID agent)
        {
            if (_tornDown || !TryGetAgent(agent, nameof(GetCurrentVelocity), out INavAgent nav))
            {
                return Vector3.zero;
            }

            Vector3 v = nav.DesiredVelocity;
            return new Vector3(v.x, 0f, v.z);
        }

        /// <summary>Number of queue slots waiting for the drain (test accessor).</summary>
        internal int PendingRequestCount => _pendingRequests.Count;

        /// <summary>Reads the latest stored request (test accessor).</summary>
        internal bool HasLatestRequest(EntityID agent, out Vector3 target) => _latestRequests.TryGetValue(agent, out target);

        /// <summary>Whether SetDestination was accepted for the entity since its claim (test accessor).</summary>
        internal bool HasRequestedDestination(EntityID agent) => _destinationRequested.Contains(agent);

        /// <summary>Whether the entity holds a queue slot (test accessor).</summary>
        internal bool IsQueued(EntityID agent) => _queuedEntities.Contains(agent);

        // One dictionary lookup per call. One warning per call for an unregistered entity (CR-NAV-7 to 11, 16); the
        // string is built only on failure.
        private bool TryGetAgent(EntityID agent, string method, out INavAgent nav)
        {
            if (_activeAgents.TryGetValue(agent, out nav))
            {
                return true;
            }

            Debug.LogWarning(LOG_PREFIX + method + " for unregistered " + agent + "; safe default returned.");
            return false;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y)
                || float.IsNaN(v.z) || float.IsInfinity(v.z));
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
                    _health[args.Entity] = new PathHealthState { LastCommittedPosition = args.SpawnPosition };
                }
            }
            catch
            {
                TryDisable(agent);
                _pool.Push(agent);
                _activeAgents.Remove(args.Entity);
                _health.Remove(args.Entity);
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
        // queue and is not searched; the drain in Tick skips entries for entities no longer active. The entity
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
            _health.Remove(args.Entity);
            _destinationRequested.Remove(args.Entity); // _queuedEntities stays: the drain skips the stale slot
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
