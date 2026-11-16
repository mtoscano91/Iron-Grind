using System;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// One authored spawn point of a zone template (CR-MS-2): id, mob type, position and an optional respawn delay
    /// override. An override above zero is the boss / event hook and is refused until OQ-MS-1 is decided.
    /// </summary>
    /// <remarks>Serialized private fields; Unity 6.3 rejects <c>[SerializeField]</c> on properties.</remarks>
    [Serializable]
    public struct SpawnPointEntry
    {
        [SerializeField] private uint _spawnPointId;
        [SerializeField] private uint _mobTypeId;
        [SerializeField] private Vector3 _position;
        [SerializeField] private int _respawnDelayOverrideTicks;

        /// <summary>Creates an entry (used by tests and tools).</summary>
        /// <param name="spawnPointId">Unique id within the table.</param>
        /// <param name="mobTypeId">The mob type spawned here.</param>
        /// <param name="position">World position, metres.</param>
        /// <param name="respawnDelayOverrideTicks">Respawn delay override; 0 means none.</param>
        public SpawnPointEntry(uint spawnPointId, MobTypeID mobTypeId, Vector3 position, int respawnDelayOverrideTicks)
        {
            _spawnPointId = spawnPointId;
            _mobTypeId = mobTypeId.RawValue;
            _position = position;
            _respawnDelayOverrideTicks = respawnDelayOverrideTicks;
        }

        /// <summary>The spawn point id, unique within its table.</summary>
        public uint SpawnPointID => _spawnPointId;

        /// <summary>The mob type spawned at this point.</summary>
        public MobTypeID MobTypeID => new MobTypeID(_mobTypeId);

        /// <summary>The spawn position, metres.</summary>
        public Vector3 Position => _position;

        /// <summary>Respawn delay override in ticks; 0 means none.</summary>
        public int RespawnDelayOverrideTicks => _respawnDelayOverrideTicks;
    }
}
