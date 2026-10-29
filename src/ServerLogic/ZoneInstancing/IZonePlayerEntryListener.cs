using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Entry hook of <see cref="ZoneEntryCompletion"/> (Movement Story 008; ADR-010 direct call). Registered once with
    /// <see cref="ZoneEntryCompletion.SetEntryListener"/>.
    /// </summary>
    /// <remarks>
    /// Called on the main thread from <see cref="ZoneEntryCompletion.OnPlayerAdmitted"/> after the slot lookup and
    /// the directory mapping, and before the zone snapshot is sent, so the snapshot can carry state registered here.
    /// Implementations must not throw (an exception is caught by the completion, logged, and the remaining entry
    /// steps are skipped).
    /// </remarks>
    public interface IZonePlayerEntryListener
    {
        /// <summary>Called once per admitted player, before the snapshot.</summary>
        /// <param name="slot">The player's slot index.</param>
        /// <param name="id">The player's EntityID.</param>
        void OnPlayerEntering(byte slot, EntityID id);
    }
}
