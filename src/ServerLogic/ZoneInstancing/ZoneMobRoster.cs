using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// <see cref="IZoneMobRoster"/> over an injected <see cref="ZoneSlotPool"/> (CR-ZI-13, EC-ZI-10).
    /// Keeps each mob's type and spawn position in arrays indexed by mob slot, allocated once.
    /// Main thread only; O(1) and allocation-free per call except on warning paths.
    /// </summary>
    /// <remarks>
    /// The roster must be the only mob allocator of its pool. <see cref="GetMobCount"/> and the cap
    /// check read the pool's counters, while <see cref="RemoveMob"/> and <see cref="TryGetMob"/> know
    /// only mobs added through <see cref="TryAddMob"/>: a mob allocated directly on the pool is counted
    /// but is unknown to the roster.
    /// </remarks>
    public sealed class ZoneMobRoster : IZoneMobRoster
    {
        /// <summary>Minimum ticks between <c>MobSlotExhausted</c> warnings: 60 s x 20 Hz.</summary>
        public const uint MOB_SLOT_EXHAUSTED_LOG_INTERVAL_TICKS = 1200u;

        private const string LOG_PREFIX = "[ZoneMobRoster]";

        private readonly ZoneSlotPool _pool;
        private readonly IServerTickSource _ticks;
        private readonly uint _logInterval;
        private readonly MobTypeID[] _types;
        private readonly Vector3[] _positions;
        private readonly Dictionary<EntityID, ushort> _slots;

        private bool _hasLoggedExhausted;
        private uint _lastExhaustedLogTick;
        private bool _foreignCountWarned;

        /// <summary>Creates a roster over <paramref name="pool"/>.</summary>
        /// <param name="pool">The slot pool; its mob slots and EntityID counter are used. Not changed.</param>
        /// <param name="ticks">Tick source for the log rate limit.</param>
        /// <param name="exhaustedLogIntervalTicks">Minimum ticks between exhaustion warnings; must be above 0.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pool"/> or <paramref name="ticks"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="exhaustedLogIntervalTicks"/> is 0.</exception>
        public ZoneMobRoster(ZoneSlotPool pool, IServerTickSource ticks,
            uint exhaustedLogIntervalTicks = MOB_SLOT_EXHAUSTED_LOG_INTERVAL_TICKS)
        {
            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (ticks == null)
            {
                throw new ArgumentNullException(nameof(ticks));
            }

            if (exhaustedLogIntervalTicks == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(exhaustedLogIntervalTicks), exhaustedLogIntervalTicks,
                    $"{LOG_PREFIX} interval must be above 0.");
            }

            _pool = pool;
            _ticks = ticks;
            _logInterval = exhaustedLogIntervalTicks;
            _types = new MobTypeID[pool.MaxMobs];
            _positions = new Vector3[pool.MaxMobs];
            _slots = new Dictionary<EntityID, ushort>(pool.MaxMobs);
        }

        /// <inheritdoc />
        public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition)
        {
            if (_pool.TryAllocateMob(zone, out ushort slot, out EntityID id))
            {
                _types[slot] = type;
                _positions[slot] = spawnPosition;
                _slots.Add(id, slot);
                return id;
            }

            // Cap-full only when the zone is ours and every slot is taken; a spent counter or a
            // foreign zone is reported by the pool.
            if (zone == _pool.Zone && _pool.OccupiedMobCount == _pool.MaxMobs)
            {
                LogExhaustedRateLimited();
            }

            return EntityID.Invalid;
        }

        /// <inheritdoc />
        public void RemoveMob(ZoneID zone, EntityID id)
        {
            if (zone != _pool.Zone)
            {
                Debug.LogWarning($"{LOG_PREFIX} RemoveMob rejected: foreign zone {zone} (this roster serves {_pool.Zone}).");
                return;
            }

            if (!_slots.TryGetValue(id, out ushort slot))
            {
                Debug.LogWarning($"{LOG_PREFIX} RemoveMob ignored: unknown or already removed {id}.");
                return;
            }

            if (!_pool.ReleaseMob(id))
            {
                // Only possible if the pool was changed behind the roster; drop the stale entry anyway.
                Debug.LogWarning($"{LOG_PREFIX} RemoveMob: {id} was not held by the pool; roster entry dropped.");
            }

            _types[slot] = default;
            _positions[slot] = Vector3.zero;
            _slots.Remove(id);
        }

        /// <inheritdoc />
        public int GetMobCount(ZoneID zone)
        {
            if (zone == _pool.Zone)
            {
                return _pool.OccupiedMobCount;
            }

            if (!_foreignCountWarned)
            {
                _foreignCountWarned = true;
                Debug.LogWarning($"{LOG_PREFIX} GetMobCount rejected: foreign zone {zone} (this roster serves {_pool.Zone}); further calls are silent.");
            }

            return 0;
        }

        /// <inheritdoc />
        public bool TryGetMob(EntityID id, out MobTypeID type, out Vector3 spawnPosition)
        {
            if (_slots.TryGetValue(id, out ushort slot))
            {
                type = _types[slot];
                spawnPosition = _positions[slot];
                return true;
            }

            type = default;
            spawnPosition = Vector3.zero;
            return false;
        }

        private void LogExhaustedRateLimited()
        {
            uint now = _ticks.ServerTickNumber;
            if (_hasLoggedExhausted && unchecked((uint)(now - _lastExhaustedLogTick)) < _logInterval)
            {
                return;
            }

            _hasLoggedExhausted = true;
            _lastExhaustedLogTick = now;
            Debug.LogWarning($"{LOG_PREFIX} MobSlotExhausted: {_pool.Zone} has all {_pool.MaxMobs} mob slots taken (tick {now}); spawn refused (EC-ZI-10).");
        }
    }
}
