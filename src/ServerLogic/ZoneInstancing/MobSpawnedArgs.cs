using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.Events
{
    /// <summary>
    /// The argument of <c>MobEventBus.OnMobSpawned</c>: one mob that has just spawned. A <c>readonly struct</c>, so
    /// raising the event does not allocate.
    /// </summary>
    public readonly struct MobSpawnedArgs
    {
        /// <summary>The zone the mob spawned in.</summary>
        public readonly ZoneID Zone;

        /// <summary>The spawned mob.</summary>
        public readonly EntityID Entity;

        /// <summary>The spawned mob's type.</summary>
        public readonly MobTypeID MobType;

        /// <summary>Where the mob spawned, in world units.</summary>
        public readonly Vector3 SpawnPosition;

        /// <summary>Builds the args.</summary>
        /// <param name="zone">The zone the mob spawned in.</param>
        /// <param name="entity">The spawned mob.</param>
        /// <param name="mobType">The mob's type.</param>
        /// <param name="spawnPosition">The spawn position in world units.</param>
        public MobSpawnedArgs(ZoneID zone, EntityID entity, MobTypeID mobType, Vector3 spawnPosition)
        {
            Zone = zone;
            Entity = entity;
            MobType = mobType;
            SpawnPosition = spawnPosition;
        }
    }
}
