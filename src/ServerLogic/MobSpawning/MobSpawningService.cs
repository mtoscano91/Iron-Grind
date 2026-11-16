using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// Per-zone spawn point runtime: the T-2 population sweep, the MobDied transition, the per-tick linger and
    /// respawn countdowns, and the deferred retry queue (design/gdd/mob-spawning.md CR-MS-3, CR-MS-4 transitions
    /// 1-7, CR-MS-5, CR-MS-6, CR-MS-7, CR-MS-9, F-MS-1; Mob Spawning Stories 002-003; TR-ms-002 to TR-ms-006;
    /// ADR-010, ADR-014). Tick thread only; <see cref="IZoneMobRoster.GetMobCount"/> and
    /// <see cref="IZoneMobRoster.TryAddMob"/> are called back to back with no yield between them.
    /// Cancellation and teardown belong to Story 004; placing <see cref="Tick"/> in the phase 3 order to Story 006.
    /// </summary>
    public sealed class MobSpawningService
    {
        private const string LOG_PREFIX = "[MobSpawning] ";
        private const int MAX_POINTS = ZoneSlotPool.MAX_MOBS_PER_ZONE;

        private readonly ZoneID _zone;
        private readonly SpawnPointEntry[] _entries;
        private readonly IZoneMobRoster _roster;
        private readonly MobEventBus _bus;
        private readonly IMobDefinitionRegistry _registry;
        private readonly MobSpawningConfigValues _config;
        private readonly int _deathLingerTicks;
        private readonly SpawnPoint[] _points = new SpawnPoint[MAX_POINTS];
        private readonly List<int> _deferred = new List<int>(MAX_POINTS);
        private readonly int _pointCount;
        private bool _populated;
        private int _retryCounter;

        /// <summary>Builds the runtime for one zone and subscribes to <see cref="MobEventBus.OnMobDied"/>.</summary>
        /// <param name="zone">The zone this service spawns into.</param>
        /// <param name="entries">The validated authored entries, in declaration order (at most MAX_MOBS_PER_ZONE).
        /// Copied once here, so later changes to the caller's list have no effect.</param>
        /// <param name="roster">Zone Instancing's mob API.</param>
        /// <param name="bus">The zone's mob event bus.</param>
        /// <param name="registry">Mob definition lookup (the tier for F-MS-1).</param>
        /// <param name="config">The Mob Spawning knobs; must pass <see cref="MobSpawningConfigValues.Validate"/>
        /// (which logs one error per bad knob).</param>
        /// <param name="deathLingerTicks">DEATH_LINGER_TICKS from the Enemy AI config; must be &gt; 0.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is <see cref="ZoneID.Invalid"/>, more
        /// entries than MAX_MOBS_PER_ZONE, or <paramref name="config"/> fails validation.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="deathLingerTicks"/> is &lt;= 0.</exception>
        public MobSpawningService(ZoneID zone, IReadOnlyList<SpawnPointEntry> entries, IZoneMobRoster roster,
            MobEventBus bus, IMobDefinitionRegistry registry, MobSpawningConfigValues config, int deathLingerTicks)
        {
            ValidateArgs(zone, entries, config, deathLingerTicks);
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _zone = zone;
            _config = config;
            _deathLingerTicks = deathLingerTicks;
            _pointCount = entries.Count;
            _entries = new SpawnPointEntry[_pointCount];
            for (int i = 0; i < _pointCount; i++)
            {
                _entries[i] = entries[i];
                _points[i] = new SpawnPoint
                {
                    State = SpawnPointState.Uninitialized,
                    CurrentEntityID = EntityID.Invalid,
                    TimerTick = 0,
                    EntryIndex = i,
                };
            }

            _bus.OnMobDied += HandleMobDied;
        }

        private static void ValidateArgs(ZoneID zone, IReadOnlyList<SpawnPointEntry> entries,
            MobSpawningConfigValues config, int deathLingerTicks)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (zone.Equals(ZoneID.Invalid))
            {
                throw new ArgumentException("Zone must not be ZoneID.Invalid.", nameof(zone));
            }

            if (entries.Count > MAX_POINTS)
            {
                throw new ArgumentException("More spawn entries than MAX_MOBS_PER_ZONE.", nameof(entries));
            }

            if (deathLingerTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deathLingerTicks), deathLingerTicks, "Must be > 0.");
            }

            if (!config.Validate())
            {
                throw new ArgumentException("MobSpawningConfigValues failed validation (see the logged errors).",
                    nameof(config));
            }
        }

        /// <summary>Number of runtime spawn points (the entry count).</summary>
        internal int PointCount => _pointCount;

        /// <summary>Number of point indices on the deferred list.</summary>
        internal int DeferredCount => _deferred.Count;

        /// <summary>Ticks since the last deferred poll (0 to interval - 1); tests only.</summary>
        internal int RetryCounter => _retryCounter;

        /// <summary>Copy of the runtime point at <paramref name="index"/> (declaration order).</summary>
        internal SpawnPoint GetPoint(int index) => _points[index];

        /// <summary>The deferred list's point index at <paramref name="listIndex"/>.</summary>
        internal int GetDeferredPointIndex(int listIndex) => _deferred[listIndex];

        /// <summary>
        /// T-2 sweep (CR-MS-7): in declaration order, each Uninitialized point gets a roster count check, then a
        /// <c>TryAddMob</c>. At cap or on an invalid id the point is Deferred. A second call logs one warning and does nothing.
        /// </summary>
        public void PopulateOnActive()
        {
            if (_populated)
            {
                Debug.LogWarning(LOG_PREFIX + "PopulateOnActive called twice for zone " + _zone
                    + "; ignored (already populated).");
                return;
            }

            _populated = true;
            for (int i = 0; i < _pointCount; i++)
            {
                if (_points[i].State != SpawnPointState.Uninitialized)
                {
                    continue;
                }

                if (TrySpawn(i) == SpawnResult.AtCap)
                {
                    Defer(i);
                }
            }
        }

        /// <summary>
        /// Advances every spawn point by one server tick (CR-MS-4 transitions 4-7, CR-MS-6, CR-MS-9, F-MS-1;
        /// Mob Spawning Story 003). One pass over the points: Dying counts down to a RemoveMob and Respawning
        /// counts down to a spawn (or a deferral at the cap). Then the retry counter advances and, every
        /// SPAWN_DENSITY_RETRY_INTERVAL_TICKS-th call, the deferred list is polled (after the pass, so a point
        /// deferred this tick can fill this tick). Live, Deferred, Uninitialized and Cancelled points are untouched.
        /// Allocation-free except on log paths. If a MobSpawned subscriber raises MobDied for another point during
        /// the pass, that point's linger starts this tick when its index is above the current one, else next tick.
        /// </summary>
        public void Tick()
        {
            for (int i = 0; i < _pointCount; i++)
            {
                SpawnPointState state = _points[i].State;
                if (state == SpawnPointState.Dying)
                {
                    TickDying(i);
                }
                else if (state == SpawnPointState.Respawning)
                {
                    TickRespawning(i);
                }
            }

            _retryCounter++;
            if (_retryCounter >= _config.SpawnDensityRetryIntervalTicks)
            {
                _retryCounter = 0;
                PollDeferred();
            }
        }

        private void TickDying(int i)
        {
            _points[i].TimerTick--;
            if (_points[i].TimerTick > 0)
            {
                return;
            }

            _roster.RemoveMob(_zone, _points[i].CurrentEntityID);
            _points[i].CurrentEntityID = EntityID.Invalid;
            _points[i].State = SpawnPointState.Respawning;
            _points[i].TimerTick = EffectiveRespawnDelay(_entries[_points[i].EntryIndex]);
        }

        private void TickRespawning(int i)
        {
            _points[i].TimerTick--;
            if (_points[i].TimerTick > 0)
            {
                return;
            }

            if (TrySpawn(i) == SpawnResult.AtCap)
            {
                Defer(i);
            }
        }

        /// <summary>F-MS-1: override when above zero (no registry call), else the tier delay; registry miss logs and uses Common.</summary>
        private int EffectiveRespawnDelay(SpawnPointEntry entry)
        {
            if (entry.RespawnDelayOverrideTicks > 0)
            {
                return entry.RespawnDelayOverrideTicks;
            }

            MobDefinition def = _registry.GetDefinition(entry.MobTypeID);
            if (def == null)
            {
                Debug.LogError(LOG_PREFIX + "no mob definition for SpawnPointID " + entry.SpawnPointID
                    + ", MobTypeID " + entry.MobTypeID.RawValue + " in zone " + _zone + "; using the Common respawn delay.");
                return _config.RespawnDelayCommonTicks;
            }

            return _config.RespawnDelayForTier(def.MobTier);
        }

        /// <summary>
        /// Polls the deferred list in order. The GetMobCount check sits right before each TryAddMob. At the cap
        /// polling stops: within one poll the count only rises, so no later point could fit either.
        /// A swap-removed slot is re-examined (the index does not advance) so the swapped-in point is not skipped.
        /// </summary>
        private void PollDeferred()
        {
            int j = 0;
            while (j < _deferred.Count)
            {
                int pointIndex = _deferred[j];
                SpawnResult result = TrySpawn(pointIndex);
                if (result == SpawnResult.AtCap)
                {
                    return;
                }

                if (result == SpawnResult.Failed)
                {
                    j++;
                    continue;
                }

                int last = _deferred.Count - 1;
                _deferred[j] = _deferred[last];
                _deferred.RemoveAt(last);
            }
        }

        private enum SpawnResult : byte
        {
            Spawned,
            AtCap,
            Failed,
        }

        /// <summary>
        /// Count check then TryAddMob, back to back (CR-MS-3). Spawned: the point is Live and MobSpawned raised.
        /// AtCap: nothing changed, the caller decides. Failed: the shared guard has logged and deferred the point.
        /// </summary>
        private SpawnResult TrySpawn(int pointIndex)
        {
            if (_roster.GetMobCount(_zone) >= MAX_POINTS)
            {
                return SpawnResult.AtCap;
            }

            SpawnPointEntry entry = _entries[_points[pointIndex].EntryIndex];
            EntityID id = _roster.TryAddMob(_zone, entry.MobTypeID, entry.Position);
            if (id.Equals(EntityID.Invalid))
            {
                GuardInvalidAdd(pointIndex);
                return SpawnResult.Failed;
            }

            _points[pointIndex].State = SpawnPointState.Live;
            _points[pointIndex].CurrentEntityID = id;
            _points[pointIndex].TimerTick = 0;
            _bus.RaiseMobSpawned(new MobSpawnedArgs(_zone, id, entry.MobTypeID, entry.Position));
            return SpawnResult.Spawned;
        }

        /// <summary>
        /// Shared guard for a TryAddMob that returned Invalid after a passing count check (AC-MS-13): logs
        /// UnexpectedTryAddMobFailure and defers the point unless it is already on the deferred list.
        /// </summary>
        private void GuardInvalidAdd(int pointIndex)
        {
            uint spawnPointId = _entries[_points[pointIndex].EntryIndex].SpawnPointID;
            Debug.LogError(LOG_PREFIX + "UnexpectedTryAddMobFailure: SpawnPointID " + spawnPointId
                + " in zone " + _zone + "; TryAddMob returned Invalid below the cap. Point stays deferred.");
            if (_points[pointIndex].State != SpawnPointState.Deferred)
            {
                Defer(pointIndex);
            }
        }

        private void Defer(int pointIndex)
        {
            _points[pointIndex].State = SpawnPointState.Deferred;
            _points[pointIndex].CurrentEntityID = EntityID.Invalid;
            _points[pointIndex].TimerTick = 0;
            _deferred.Add(pointIndex);
        }

        private void HandleMobDied(MobDiedArgs args)
        {
            if (!args.Zone.Equals(_zone))
            {
                Debug.LogWarning(LOG_PREFIX + "MobDied for a foreign zone ignored: entity " + args.Entity
                    + " in zone " + args.Zone + ", this service is zone " + _zone + ".");
                return;
            }

            for (int i = 0; i < _pointCount; i++)
            {
                SpawnPointState state = _points[i].State;
                if ((state != SpawnPointState.Live && state != SpawnPointState.Dying)
                    || !_points[i].CurrentEntityID.Equals(args.Entity))
                {
                    continue;
                }

                if (state == SpawnPointState.Live)
                {
                    _points[i].State = SpawnPointState.Dying;
                    _points[i].TimerTick = _deathLingerTicks;
                }

                return;
            }
        }
    }
}
