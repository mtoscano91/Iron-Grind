using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.Randomness;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Per-zone runtime state of every live mob: committed position, facing, spawn point, type and spawn variant
    /// (Enemy AI Story 008, TD-102 stats part). Seeds mob stats on spawn and serves committed positions to Navigation.
    /// </summary>
    /// <remarks>
    /// <para>Re-entrancy: the spawn handler must never be raised from inside a <c>CharacterStats.OnStatChanged</c>
    /// handler (the seeding writes would be re-entrant).</para>
    /// <para>Mob EntityIDs are never reused (<c>ZoneSlotPool.TryMintId</c> uses a counter that only goes up), so a stale
    /// stat modifier on a reused ID cannot happen. Subscribe this store before <c>EnemyAISystem</c> so controllers see
    /// seeded stats. Tick thread only.</para>
    /// <para>The seeder is not expected to throw (its arguments are checked here and the definition was validated at
    /// load). If it did, some stats could already be written, no entry is added, and the exception reaches the code
    /// that raised the spawn, so later subscribers on that raise would not run.</para>
    /// <para>After <see cref="Remove"/>, a new spawn with the same ID would be accepted and seeded again (stats
    /// written twice). This does not happen in practice because mob IDs are never reused.</para>
    /// </remarks>
    public sealed class MobRuntimeStore : IMobCommittedPositionSource, IDisposable
    {
        private const string LOG_PREFIX = "[MobRuntimeStore]";

        private readonly ZoneID _zone;
        private readonly MobEventBus _bus;
        private readonly IMobDefinitionRegistry _registry;
        private readonly IRandomProvider _random;
        private readonly IMobStatWriter _stats;
        private readonly Dictionary<EntityID, MobRuntimeEntry> _entries;
        private bool _disposed;

        /// <summary>Creates the store and subscribes to <see cref="MobEventBus.OnMobSpawned"/>.</summary>
        /// <param name="zone">The zone this store serves.</param>
        /// <param name="bus">The zone's mob event bus.</param>
        /// <param name="registry">Mob definition registry.</param>
        /// <param name="random">Process-level random provider (one draw per successful spawn).</param>
        /// <param name="stats">Mob stat writer.</param>
        /// <param name="capacity">Expected mob count; pre-sizes the dictionary (negative treated as 0).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        public MobRuntimeStore(ZoneID zone, MobEventBus bus, IMobDefinitionRegistry registry, IRandomProvider random,
            IMobStatWriter stats, int capacity)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _zone = zone;
            _entries = new Dictionary<EntityID, MobRuntimeEntry>(capacity < 0 ? 0 : capacity);
            _bus.OnMobSpawned += HandleMobSpawned;
        }

        /// <summary>Number of mobs held.</summary>
        public int Count => _entries.Count;

        /// <summary>Looks up a mob's runtime entry.</summary>
        /// <param name="id">The mob.</param>
        /// <param name="entry">The entry, or default.</param>
        /// <returns>True when found.</returns>
        public bool TryGet(EntityID id, out MobRuntimeEntry entry) => _entries.TryGetValue(id, out entry);

        /// <summary>Overwrites a mob's committed position and facing.</summary>
        /// <param name="id">The mob.</param>
        /// <param name="position">New committed position.</param>
        /// <param name="facing">New facing (degrees x10).</param>
        /// <returns>False (no log) for an unknown id.</returns>
        public bool TrySetCommitted(EntityID id, Vector3 position, short facing)
        {
            if (!_entries.TryGetValue(id, out MobRuntimeEntry entry))
            {
                return false;
            }

            _entries[id] = entry.WithCommitted(position, facing);
            return true;
        }

        /// <summary>Removes a mob.</summary>
        /// <param name="id">The mob.</param>
        /// <returns>True when it was present.</returns>
        public bool Remove(EntityID id) => _entries.Remove(id);

        /// <inheritdoc/>
        public void CopyCommittedPositions(Dictionary<EntityID, Vector3> into)
        {
            foreach (KeyValuePair<EntityID, MobRuntimeEntry> pair in _entries)
            {
                into[pair.Key] = pair.Value.CommittedPosition;
            }
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
            if (!args.Zone.Equals(_zone))
            {
                Debug.LogWarning($"{LOG_PREFIX} spawn for another zone ignored (mob {args.Entity}).");
                return;
            }

            if (_entries.ContainsKey(args.Entity))
            {
                Debug.LogError($"{LOG_PREFIX} duplicate spawn for mob {args.Entity} ignored.");
                return;
            }

            MobDefinition definition = _registry.GetDefinition(args.MobType);
            if (definition == null)
            {
                Debug.LogError($"{LOG_PREFIX} unknown mob type {args.MobType} for mob {args.Entity}; not seeded.");
                return;
            }

            MobSpawnVariant variant = MobSpawnSeeder.Seed(args.Entity, definition, _random, _stats);
            _entries[args.Entity] = new MobRuntimeEntry(args.SpawnPosition, 0, args.SpawnPosition, args.MobType, variant);
        }
    }
}
