using System.Collections.Generic;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Production <see cref="IConnectionActivitySink"/> of the zone (Zone Instancing Story 016, ADR-014 Decision 2 step 1).
    /// <see cref="OnInboundActivity"/> runs inside the receive callback and only records the latest tick per client; the
    /// wiring drains the records on the tick (<see cref="TryTake"/>, <see cref="Clear"/>) and maps each client to its account.
    /// </summary>
    /// <remarks>
    /// Threading: the receive callback and the tick both run on the main thread (the dispatcher is not thread-safe,
    /// <c>InboundRequestDispatcher</c> remarks), so no lock is needed. Allocation: the dictionary is pre-sized at
    /// construction; recording or taking a client already within that capacity allocates nothing. The capacity is the
    /// player slot count because the dispatcher's live connections are bounded by it (<c>InboundRequestDispatcher</c>
    /// <c>SLOT_COUNT</c> = <c>ZoneBufferPool.MAX_PLAYERS_PER_ZONE</c> = 50 = <c>ZoneSlotPool.MAX_PLAYERS_PER_ZONE</c>).
    /// </remarks>
    /// <example>
    /// <code>
    /// var sink = new RecordingConnectionActivitySink(50);
    /// sink.OnInboundActivity(clientId: 5u, serverTick: 120u);
    /// if (sink.TryTake(5u, out uint tick)) { /* tick == 120 */ }
    /// </code>
    /// </example>
    internal sealed class RecordingConnectionActivitySink : IConnectionActivitySink
    {
        private readonly Dictionary<uint, uint> _latestTick;

        /// <summary>Creates the sink.</summary>
        /// <param name="capacity">Expected number of simultaneously active clients (pre-sizes the table).</param>
        public RecordingConnectionActivitySink(int capacity)
        {
            _latestTick = new Dictionary<uint, uint>(capacity < 1 ? 1 : capacity);
        }

        /// <summary>Number of clients with a recorded, undrained activity.</summary>
        public int Count => _latestTick.Count;

        /// <inheritdoc/>
        public void OnInboundActivity(uint clientId, uint serverTick)
        {
            _latestTick[clientId] = serverTick;
        }

        /// <summary>Removes and returns the latest recorded tick of one client.</summary>
        /// <param name="clientId">The connection.</param>
        /// <param name="serverTick">The latest tick recorded for it, or 0.</param>
        /// <returns>True when the client had a record.</returns>
        public bool TryTake(uint clientId, out uint serverTick)
        {
            if (_latestTick.TryGetValue(clientId, out serverTick))
            {
                _latestTick.Remove(clientId);
                return true;
            }

            return false;
        }

        /// <summary>Drops every remaining record (clients without a player slot are ignored).</summary>
        public void Clear()
        {
            _latestTick.Clear();
        }
    }
}
