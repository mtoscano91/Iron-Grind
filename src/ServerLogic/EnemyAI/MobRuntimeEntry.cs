using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>Per-mob runtime record held by <see cref="MobRuntimeStore"/> (Enemy AI Story 008).</summary>
    public readonly struct MobRuntimeEntry
    {
        /// <summary>Creates an entry.</summary>
        /// <param name="committedPosition">Committed world position.</param>
        /// <param name="facingAngle">Facing in degrees x10.</param>
        /// <param name="spawnPoint">Spawn position.</param>
        /// <param name="mobType">The mob's type.</param>
        /// <param name="variant">The spawn variant (Enraged flag, XP, tier shift).</param>
        public MobRuntimeEntry(Vector3 committedPosition, short facingAngle, Vector3 spawnPoint, MobTypeID mobType,
            MobSpawnVariant variant)
        {
            CommittedPosition = committedPosition;
            FacingAngle = facingAngle;
            SpawnPoint = spawnPoint;
            MobType = mobType;
            Variant = variant;
        }

        /// <summary>Committed (server-authoritative) world position.</summary>
        public Vector3 CommittedPosition { get; }

        /// <summary>Facing in degrees x10.</summary>
        public short FacingAngle { get; }

        /// <summary>Where the mob spawned.</summary>
        public Vector3 SpawnPoint { get; }

        /// <summary>The mob's type.</summary>
        public MobTypeID MobType { get; }

        /// <summary>The spawn variant.</summary>
        public MobSpawnVariant Variant { get; }

        /// <summary>Returns a copy with a new committed position and facing.</summary>
        /// <param name="position">New position.</param>
        /// <param name="facing">New facing (degrees x10).</param>
        /// <returns>The updated entry.</returns>
        public MobRuntimeEntry WithCommitted(Vector3 position, short facing)
        {
            return new MobRuntimeEntry(position, facing, SpawnPoint, MobType, Variant);
        }
    }
}
