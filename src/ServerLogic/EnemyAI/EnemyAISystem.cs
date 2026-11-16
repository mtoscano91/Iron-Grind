using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.Networking;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Enemy AI phase 3 step (Story 009): owns one <see cref="MobController"/> per spawned mob, ticks them in spawn order and
    /// caches the navigation velocity of mobs that move (Pursuing, Returning). <see cref="IntegrationStep"/> (phase 4) applies
    /// the cache. Construct it after <see cref="MobRuntimeStore"/> so the store has seeded the mob when the spawn handler runs.
    /// Tick thread only. Allocates nothing per tick.
    /// </summary>
    public sealed class EnemyAISystem : IZoneSimulationStep, IDisposable
    {
        private const string LOG_PREFIX = "[EnemyAISystem]";

        private readonly MobEventBus _bus;
        private readonly MobRuntimeStore _store;
        private readonly IMobDefinitionRegistry _registry;
        private readonly INavigationProvider _navigation;
        private readonly IZoneEntityStatReader _stats;
        private readonly IMobAttackResolver _attackResolver;
        private readonly EnemyAIConfigValues _config;
        private readonly float _returnSpeedCap;
        private readonly IAggroCandidateSource _candidates;
        private readonly List<MobController> _controllers;
        private readonly Dictionary<EntityID, int> _index;
        private readonly MobPositionIntegrationStep _integrationStep;
        private readonly List<EntityID> _pendingRemovals;
        private Vector3[] _velocities;
        private bool[] _hasVelocity;
        private bool[] _warned;
        private bool _running;
        private bool _disposed;

        /// <summary>Creates the system and subscribes to <see cref="MobEventBus.OnMobSpawned"/>.</summary>
        /// <param name="bus">The zone's mob event bus.</param>
        /// <param name="store">Mob runtime store (seeds the mob before this system's handler runs).</param>
        /// <param name="registry">Mob definition registry.</param>
        /// <param name="navigation">Navigation provider; called in phase 3 only.</param>
        /// <param name="stats">Stat reader passed to every controller.</param>
        /// <param name="attackResolver">Attack seam passed to every controller.</param>
        /// <param name="config">Enemy AI knobs.</param>
        /// <param name="returnSpeedCap">RETURN_SPEED_CAP in m/s.</param>
        /// <param name="candidates">Player seam passed to every controller.</param>
        /// <param name="capacity">Expected mob count; pre-sizes the list, caches and map (negative treated as 0). The caches grow if exceeded.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public EnemyAISystem(
            MobEventBus bus,
            MobRuntimeStore store,
            IMobDefinitionRegistry registry,
            INavigationProvider navigation,
            IZoneEntityStatReader stats,
            IMobAttackResolver attackResolver,
            EnemyAIConfigValues config,
            float returnSpeedCap,
            IAggroCandidateSource candidates,
            int capacity)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _attackResolver = attackResolver ?? throw new ArgumentNullException(nameof(attackResolver));
            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _config = config;
            _returnSpeedCap = returnSpeedCap;
            int size = capacity < 0 ? 0 : capacity;
            _controllers = new List<MobController>(size);
            _index = new Dictionary<EntityID, int>(size);
            _velocities = new Vector3[size];
            _hasVelocity = new bool[size];
            _warned = new bool[size];
            _pendingRemovals = new List<EntityID>(size);
            _integrationStep = new MobPositionIntegrationStep(this);
            _bus.OnMobSpawned += HandleMobSpawned;
        }

        /// <summary>Number of controllers.</summary>
        public int Count => _controllers.Count;

        /// <summary>The phase 4 step that applies the velocities cached by <see cref="Run"/>. Place it after the last phase 3 system.</summary>
        public IZoneSimulationStep IntegrationStep => _integrationStep;

        internal IReadOnlyList<MobController> Controllers => _controllers;

        internal MobRuntimeStore Store => _store;

        internal Vector3[] Velocities => _velocities;

        internal bool[] HasVelocity => _hasVelocity;

        internal bool[] Warned => _warned;

        /// <summary>Looks up a controller (tests).</summary>
        /// <param name="id">The mob.</param>
        /// <param name="controller">The controller when found.</param>
        /// <returns>True when found.</returns>
        internal bool TryGetController(EntityID id, out MobController controller)
        {
            if (_index.TryGetValue(id, out int i))
            {
                controller = _controllers[i];
                return true;
            }

            controller = null;
            return false;
        }

        /// <summary>
        /// Removes a mob's controller (swap-remove: the last controller takes its place, so the order stays deterministic).
        /// Called during <see cref="Run"/> (e.g. from a controller's death path, Story 011), the removal is deferred to the end
        /// of <see cref="Run"/>: the mob keeps its slot until then, so no controller is skipped or gets another's velocity, and
        /// it caches no velocity once queued.
        /// </summary>
        /// <param name="id">The mob.</param>
        /// <returns>True when removed or queued for removal; false for an unknown id or one already queued.</returns>
        public bool Remove(EntityID id)
        {
            if (!_index.TryGetValue(id, out int i))
            {
                return false;
            }

            if (_running)
            {
                if (_pendingRemovals.Contains(id))
                {
                    return false;
                }

                _pendingRemovals.Add(id);
                _hasVelocity[i] = false;
                return true;
            }

            RemoveAt(id, i);
            return true;
        }

        private void RemoveAt(EntityID id, int i)
        {
            int last = _controllers.Count - 1;
            if (i != last)
            {
                MobController moved = _controllers[last];
                _controllers[i] = moved;
                _index[moved.EntityID] = i;
                _velocities[i] = _velocities[last];
                _hasVelocity[i] = _hasVelocity[last];
                _warned[i] = _warned[last];
            }

            _controllers.RemoveAt(last);
            _index.Remove(id);
            _velocities[last] = default;
            _hasVelocity[last] = false;
            _warned[last] = false;
        }

        /// <summary>
        /// Phase 3: ticks every controller in spawn order and caches the velocity of Pursuing and Returning mobs.
        /// An exception from one controller is logged with its EntityID, that mob caches no velocity and the rest still tick.
        /// </summary>
        /// <param name="currentTick">The current tick.</param>
        /// <param name="deltaTime">Unused here (the integration step applies it).</param>
        public void Run(uint currentTick, float deltaTime)
        {
            _running = true;
            try
            {
                for (int i = 0; i < _controllers.Count; i++)
                {
                    MobController controller = _controllers[i];
                    _hasVelocity[i] = false;
                    try
                    {
                        controller.Tick(currentTick);
                        MobState state = controller.State;
                        if ((state == MobState.Pursuing || state == MobState.Returning)
                            && !IsPendingRemoval(controller.EntityID))
                        {
                            _velocities[i] = _navigation.GetCurrentVelocity(controller.EntityID);
                            _hasVelocity[i] = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _hasVelocity[i] = false;
                        LogTickFailed(controller.EntityID, currentTick, ex);
                    }
                }
            }
            finally
            {
                _running = false;
                ApplyPendingRemovals();
            }
        }

        private bool IsPendingRemoval(EntityID id)
        {
            for (int i = 0; i < _pendingRemovals.Count; i++)
            {
                if (_pendingRemovals[i].Equals(id))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyPendingRemovals()
        {
            for (int i = 0; i < _pendingRemovals.Count; i++)
            {
                EntityID id = _pendingRemovals[i];
                if (_index.TryGetValue(id, out int slot))
                {
                    RemoveAt(id, slot);
                }
            }

            _pendingRemovals.Clear();
        }

        /// <summary>Unsubscribes from the bus. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bus.OnMobSpawned -= HandleMobSpawned;
        }

        private void HandleMobSpawned(MobSpawnedArgs args)
        {
            if (!_store.TryGet(args.Entity, out MobRuntimeEntry entry) || _index.ContainsKey(args.Entity))
            {
                return;
            }

            MobDefinition definition = _registry.GetDefinition(entry.MobType);
            if (definition == null)
            {
                return;
            }

            var controller = new MobController(args.Entity, definition, entry.SpawnPoint, _navigation, _stats,
                _attackResolver, _config, _returnSpeedCap, _candidates);
            controller.IsEnraged = entry.Variant.IsEnraged;

            int slot = _controllers.Count;
            if (slot == _velocities.Length)
            {
                int newSize = slot == 0 ? 4 : slot * 2;
                Array.Resize(ref _velocities, newSize);
                Array.Resize(ref _hasVelocity, newSize);
                Array.Resize(ref _warned, newSize);
            }

            _controllers.Add(controller);
            _index[args.Entity] = slot;
            _velocities[slot] = default;
            _hasVelocity[slot] = false;
            _warned[slot] = false;
        }

        // Called only from a catch block, so the message is built only when a controller failed.
        private static void LogTickFailed(EntityID id, uint tick, Exception ex)
        {
            try
            {
                Debug.LogError($"{LOG_PREFIX} TickFailed: mob={id} tick={tick} - {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception)
            {
                // Logging must not break the tick.
            }
        }
    }
}
