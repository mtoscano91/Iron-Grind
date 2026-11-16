using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>Adapter: <see cref="IZoneMobTransformReader"/> over a <see cref="MobRuntimeStore"/>.</summary>
    public sealed class MobRuntimeStoreTransformReader : IZoneMobTransformReader
    {
        private readonly MobRuntimeStore _store;

        /// <summary>Creates the adapter.</summary>
        /// <param name="store">The store.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
        public MobRuntimeStoreTransformReader(MobRuntimeStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc/>
        public bool TryGetTransform(EntityID id, out Vector3 position, out short facing)
        {
            if (_store.TryGet(id, out MobRuntimeEntry entry))
            {
                position = entry.CommittedPosition;
                facing = entry.FacingAngle;
                return true;
            }

            position = default;
            facing = 0;
            return false;
        }
    }
}
