using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Receives a player that passed admission (Zone Instancing Story 009), so the entry can continue with
    /// CR-ZI-8 steps 6-10 (snapshot, SessionReady, party, PlayerJoinedZone). Story 010 implements it.
    /// </summary>
    public interface IZoneEntryContinuation
    {
        /// <summary>
        /// The player holds a slot and its loaded record is applied. Called on the tick thread from the load's
        /// completion callback. Implementations must not throw: an exception leaves the slot occupied with no reply.
        /// </summary>
        /// <param name="clientId">The player's connection.</param>
        /// <param name="slot">The allocated player slot index.</param>
        /// <param name="entityId">The minted EntityID.</param>
        void OnPlayerAdmitted(uint clientId, byte slot, EntityID entityId);
    }
}
