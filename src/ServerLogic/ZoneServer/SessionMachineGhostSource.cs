using System;
using System.Collections.Generic;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Production <see cref="IZoneGhostSessionSource"/> over the zone's <see cref="ConnectionStateMachine"/> (Zone
    /// Instancing Story 016): lists every <c>Disconnected_SessionActive</c> account with its <c>SessionExpiryTick</c>.
    /// <see cref="Refresh"/> snapshots once per tick so the TTL loop can end (and remove) sessions without shifting indices.
    /// </summary>
    /// <example>
    /// <code>
    /// var ghosts = new SessionMachineGhostSource(machine, capacity: 50);
    /// ghosts.Refresh();
    /// for (int i = 0; i &lt; ghosts.Count; i++) { ghosts.Get(i, out uint account, out uint expiry); }
    /// </code>
    /// </example>
    internal sealed class SessionMachineGhostSource : IZoneGhostSessionSource
    {
        private readonly ConnectionStateMachine _machine;
        private readonly List<uint> _accounts;
        private readonly List<uint> _expiries;

        /// <summary>Creates the source.</summary>
        /// <param name="machine">The zone's session machine.</param>
        /// <param name="capacity">Pre-sized list capacity (the zone's player capacity).</param>
        public SessionMachineGhostSource(ConnectionStateMachine machine, int capacity)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _accounts = new List<uint>(capacity < 1 ? 1 : capacity);
            _expiries = new List<uint>(capacity < 1 ? 1 : capacity);
        }

        /// <inheritdoc/>
        public int Count => _accounts.Count;

        /// <inheritdoc/>
        public void Refresh()
        {
            _machine.CollectSessionActiveGhosts(_accounts, _expiries);
        }

        /// <inheritdoc/>
        public void Get(int index, out uint accountId, out uint sessionExpiryTick)
        {
            accountId = _accounts[index];
            sessionExpiryTick = _expiries[index];
        }
    }
}
