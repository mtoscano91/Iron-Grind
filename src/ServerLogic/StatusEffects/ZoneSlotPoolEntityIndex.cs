using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Production <see cref="IEntitySlotIndex"/> over <see cref="ZoneSlotPool"/> (Status Effects Story 009, CR-SE-15).
    /// A player maps to its player slot [0, MaxPlayers); a mob maps to its OWL index
    /// (<c>MaxPlayers + mobSlot</c>, [50, 199] with the default sizes), so players and mobs never share an index.
    /// A released or unknown entity has no slot. Allocation-free.
    /// </summary>
    /// <example>
    /// <code>
    /// var index = new ZoneSlotPoolEntityIndex(pool);
    /// if (index.TryGetSlotIndex(entityId, out int slot)) { /* slot is the table row group */ }
    /// </code>
    /// </example>
    public sealed class ZoneSlotPoolEntityIndex : IEntitySlotIndex
    {
        private readonly ZoneSlotPool _pool;

        /// <summary>Creates the index over a pool.</summary>
        /// <param name="pool">The zone's slot pool; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pool"/> is null.</exception>
        /// <exception cref="ArgumentException">The pool has more slots than the effect table
        /// (<see cref="StatusEffectsConstants.MAX_ENTITY_SLOTS"/>): its highest indices would silently get no effects.</exception>
        public ZoneSlotPoolEntityIndex(ZoneSlotPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (pool.MaxPlayers + pool.MaxMobs > StatusEffectsConstants.MAX_ENTITY_SLOTS)
            {
                throw new ArgumentException(
                    $"pool has {pool.MaxPlayers + pool.MaxMobs} slots; the effect table holds {StatusEffectsConstants.MAX_ENTITY_SLOTS}.",
                    nameof(pool));
            }
        }

        /// <inheritdoc/>
        public bool TryGetSlotIndex(EntityID id, out int index)
        {
            if (_pool.TryGetPlayerSlot(id, out byte playerSlot))
            {
                index = playerSlot;
                return true;
            }

            if (_pool.TryGetMobSlot(id, out ushort mobSlot))
            {
                index = _pool.GetOwlIndexForMobSlot(mobSlot);
                return true;
            }

            index = -1;
            return false;
        }
    }
}
