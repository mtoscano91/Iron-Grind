using System;
using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// <see cref="ITownRespawnPointProvider"/> over a <see cref="ZoneDefinitionSlot"/> (Movement Story 009). The
    /// definition is set at startup S6, after the object graph is built at S5, so the <see cref="TownRespawnPointCache"/>
    /// is created lazily on the first call that finds the slot filled and is reused afterwards.
    /// </summary>
    /// <remarks>
    /// An empty slot at a call logs one error and returns <see cref="Vector3.zero"/> (the interface's "none resolved"
    /// value); no cache is built and the next call retries. Never throws. Main thread only.
    /// </remarks>
    /// <example>
    /// <code>
    /// var provider = new ZoneDefinitionSlotRespawnPointProvider(ownZone, slot);
    /// Vector3 point = provider.GetTownRespawnPoint(ownZone);
    /// </code>
    /// </example>
    public sealed class ZoneDefinitionSlotRespawnPointProvider : ITownRespawnPointProvider
    {
        private const string LOG_PREFIX = "[ZoneInstancing]";

        private readonly ZoneID _ownZone;
        private readonly ZoneDefinitionSlot _slot;
        private TownRespawnPointCache _cache;

        /// <summary>Creates the provider.</summary>
        /// <param name="ownZone">The zone this process hosts.</param>
        /// <param name="slot">The holder the scene loader fills.</param>
        /// <exception cref="ArgumentNullException"><paramref name="slot"/> is null.</exception>
        public ZoneDefinitionSlotRespawnPointProvider(ZoneID ownZone, ZoneDefinitionSlot slot)
        {
            _ownZone = ownZone;
            _slot = slot ?? throw new ArgumentNullException(nameof(slot));
        }

        /// <summary>How many times the cache was built (0 or 1). Test hook.</summary>
        internal int CacheBuildCount { get; private set; }

        /// <summary>Returns the town respawn point, building the cache on first use.</summary>
        /// <param name="zone">The zone asked about.</param>
        /// <returns>The point; <see cref="Vector3.zero"/> when the definition is not loaded yet or the cache cannot be built.</returns>
        public Vector3 GetTownRespawnPoint(ZoneID zone)
        {
            if (_cache == null)
            {
                ZoneDefinition definition = _slot.Current;
                if (definition == null)
                {
                    Debug.LogError(LOG_PREFIX + " TownRespawnPointMissing: no zone definition is loaded yet; returning zero");
                    return Vector3.zero;
                }

                try
                {
                    _cache = new TownRespawnPointCache(_ownZone, definition);
                    CacheBuildCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError(LOG_PREFIX + " TownRespawnPointMissing: cache build failed (" + ex.GetType().Name + "); returning zero");
                    return Vector3.zero;
                }
            }

            return _cache.GetTownRespawnPoint(zone);
        }
    }
}
