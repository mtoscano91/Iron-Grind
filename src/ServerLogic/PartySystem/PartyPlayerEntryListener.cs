using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Registers an admitted player as a solo party (Party System Story 009 AC-6). A token-path reconnect keeps
    /// its slot and never reaches this listener. Composed into the entry composite by Story 011.
    /// </summary>
    public sealed class PartyPlayerEntryListener : IZonePlayerEntryListener
    {
        /// <summary>Log prefix.</summary>
        public const string LOG_PREFIX = "[PartyEntry] ";

        private readonly PartyRegistry _registry;
        private readonly ZoneSlotPool _pool;

        /// <summary>Creates the listener.</summary>
        /// <param name="registry">The party registry.</param>
        /// <param name="pool">The zone slot pool (slot to character).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PartyPlayerEntryListener(PartyRegistry registry, ZoneSlotPool pool)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <inheritdoc/>
        public void OnPlayerEntering(byte slot, EntityID id)
        {
            if (!_pool.TryGetPlayer(id, out ZonePlayerSlot player))
            {
                Debug.LogWarning(LOG_PREFIX + "no pool slot for entity " + id + "; not registered.");
                return;
            }

            PartyRegistryResult result = _registry.RegisterPlayer(player.Character, id, out _);
            if (result != PartyRegistryResult.Ok)
            {
                Debug.LogWarning(LOG_PREFIX + "registering " + player.Character + " failed: " + result + "; no change.");
            }
        }
    }
}
