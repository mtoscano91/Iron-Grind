using System;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Enemy AI phase 3 step (Stories 009 and 011): owns one <see cref="MobController"/> per spawned mob, ticks them in spawn order and
    /// caches the navigation velocity of mobs that move (Pursuing, Returning). <see cref="IntegrationStep"/> (phase 4) applies
    /// the cache. Construct it after <see cref="MobRuntimeStore"/> so the store has seeded the mob when the spawn handler runs.
    /// </summary>
    /// <remarks>
    /// <para>Death bridge (Story 011, CR-AI-13, ADR-010 note 2026-11-16): the system subscribes to
    /// <see cref="ICombatService.OnMobDied"/>. A mob enters Dead either through that event (tick recorded = the last
    /// <see cref="Run"/> tick, 0 before the first) or through the controller's own HP check at tick start (CR-AI-13). Whichever
    /// comes first runs the Dead-entry routine exactly once: <c>ResolveMobDrop(tierShift)</c>, then
    /// <see cref="MobEventBus.RaiseMobDied"/>. After DEATH_LINGER_TICKS the mob is despawned from this system and from
    /// <see cref="MobRuntimeStore"/>. <c>IZoneMobRoster.RemoveMob</c> belongs to Mob Spawning and is never called here. No XP is
    /// awarded here.</para>
    /// <para>Tick thread only. Allocates nothing per tick, except the log message on the warning and loot-failure paths.</para>
    /// </remarks>
    public sealed class EnemyAISystem : IZoneSimulationStep, IDisposable
    {
        private const string LOG_PREFIX = "[EnemyAISystem]";

        private readonly ZoneID _zone;
        private readonly MobEventBus _bus;
        private readonly MobRuntimeStore _store;
        private readonly IMobDefinitionRegistry _registry;
        private readonly INavigationProvider _navigation;
        private readonly IZoneEntityStatReader _stats;
        private readonly IMobAttackResolver _attackResolver;
        private readonly EnemyAIConfigValues _config;
        private readonly float _returnSpeedCap;
        private readonly IAggroCandidateSource _candidates;
        private readonly ICombatService _combat;
        private readonly ILootTableService _loot;
        private readonly List<MobController> _controllers;
        private readonly Dictionary<EntityID, int> _index;
        private readonly MobPositionIntegrationStep _integrationStep;
        private readonly List<EntityID> _pendingRemovals;
        private Vector3[] _velocities;
        private bool[] _hasVelocity;
        private bool[] _warned;
        private uint _lastRunTick;
        private bool _running;
        private bool _disposed;

        /// <summary>
        /// Creates the system and subscribes to <see cref="MobEventBus.OnMobSpawned"/> and <see cref="ICombatService.OnMobDied"/>.
        /// Combat must therefore be constructed before this system.
        /// </summary>
        /// <param name="zone">The zone this system serves; deaths reported for another zone are ignored with a warning.</param>
        /// <param name="bus">The zone's mob event bus.</param>
        /// <param name="store">Mob runtime store (seeds the mob before this system's handler runs).</param>
        /// <param name="registry">Mob definition registry.</param>
        /// <param name="navigation">Navigation provider; called in phase 3 only.</param>
        /// <param name="stats">Stat reader passed to every controller.</param>
        /// <param name="attackResolver">Attack seam passed to every controller.</param>
        /// <param name="config">Enemy AI knobs.</param>
        /// <param name="returnSpeedCap">RETURN_SPEED_CAP in m/s.</param>
        /// <param name="candidates">Player seam passed to every controller.</param>
        /// <param name="combat">Combat's kill broadcast (Story 011).</param>
        /// <param name="loot">Loot Table entry point called once per dead mob (Story 011).</param>
        /// <param name="capacity">Expected mob count; pre-sizes the list, caches and map (negative treated as 0). The caches grow if exceeded.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public EnemyAISystem(
            ZoneID zone,
            MobEventBus bus,
            MobRuntimeStore store,
            IMobDefinitionRegistry registry,
            INavigationProvider navigation,
            IZoneEntityStatReader stats,
            IMobAttackResolver attackResolver,
            EnemyAIConfigValues config,
            float returnSpeedCap,
            IAggroCandidateSource candidates,
            ICombatService combat,
            ILootTableService loot,
            int capacity)
        {
            _zone = zone;
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _attackResolver = attackResolver ?? throw new ArgumentNullException(nameof(attackResolver));
            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _loot = loot ?? throw new ArgumentNullException(nameof(loot));
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
            _combat.OnMobDied += HandleMobDied;
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
        /// Called during <see cref="Run"/> (e.g. by the Dead linger despawn, Story 011), the removal is deferred to the end
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
        /// A mob found Dead (CR-AI-13) runs the Dead-entry routine once; a Dead mob whose linger has elapsed is despawned.
        /// An exception from one controller is logged with its EntityID, that mob caches no velocity and the rest still tick.
        /// </summary>
        /// <param name="currentTick">The current tick.</param>
        /// <param name="deltaTime">Unused here (the integration step applies it).</param>
        public void Run(uint currentTick, float deltaTime)
        {
            _lastRunTick = currentTick;
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
                        if (state == MobState.Dead)
                        {
                            TickDead(controller, currentTick);
                        }
                        else if ((state == MobState.Pursuing || state == MobState.Returning)
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

        // Dead mob inside Run: run the routine if CR-AI-13 was the trigger, then despawn once the linger has elapsed.
        private void TickDead(MobController controller, uint currentTick)
        {
            RunDeathRoutine(controller);

            // long arithmetic: DeadEnteredTick is a long (-1 never reaches here, a Dead mob always has a recorded tick).
            // Both removals run even if the controller was already queued for removal by someone else, so the store entry
            // cannot leak (MobRuntimeStore.Remove is idempotent).
            if ((long)currentTick - controller.DeadEnteredTick >= _config.DeathLingerTicks)
            {
                Remove(controller.EntityID);
                _store.Remove(controller.EntityID);
            }
        }

        // The Dead-entry routine (CR-AI-13, CR-MS-5): loot first, then MobEventBus.MobDied. Runs once per mob.
        private void RunDeathRoutine(MobController controller)
        {
            if (controller.DeathRoutineRan)
            {
                return;
            }

            controller.DeathRoutineRan = true;
            EntityID id = controller.EntityID;
            int tierShift = controller.IsEnraged ? controller.Definition.EnragedLootQualityBonusTier : 0;
            try
            {
                _loot.ResolveMobDrop(id, tierShift);
            }
            catch (Exception ex)
            {
                LogLootFailed(id, ex);
            }

            _bus.RaiseMobDied(new MobDiedArgs(_zone, id));
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

        /// <summary>Unsubscribes from the bus and from Combat. Idempotent.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bus.OnMobSpawned -= HandleMobSpawned;
            _combat.OnMobDied -= HandleMobDied;
        }

        // Combat's kill event (runs inside Combat's step, after Enemy AI in the same tick).
        private void HandleMobDied(MobDeathContext context)
        {
            if (!context.ZoneId.Equals(_zone) || !_index.TryGetValue(context.MobEntityId, out int slot))
            {
                Debug.LogWarning($"{LOG_PREFIX} OnMobDied ignored: mob={context.MobEntityId} is not a mob of this zone.");
                return;
            }

            MobController controller = _controllers[slot];
            if (controller.State == MobState.Dead)
            {
                return;
            }

            // Combat runs after this system's Run in the same tick: drop the velocity this mob cached in that Run, or phase 4
            // would move the corpse one more tick (AC-3).
            _hasVelocity[slot] = false;
            controller.EnterState(MobState.Dead, _lastRunTick);
            RunDeathRoutine(controller);
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

        // Called only from a catch block. The death event is still raised afterwards (the spawn lifecycle must not stall).
        private static void LogLootFailed(EntityID id, Exception ex)
        {
            try
            {
                Debug.LogError($"{LOG_PREFIX} LootFailed: mob={id} - {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception)
            {
                // Logging must not break the tick.
            }
        }
    }
}
