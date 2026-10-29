using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Before-release hook of <see cref="ZoneSlotPool"/> (<c>zone-instancing.md</c> CR-ZI-4; Status Effects
    /// CR-SE-11, OQ-SE-2). A direct call on an injected interface per ADR-010: the order (cleanup, then slot
    /// release) is the contract, so it is not an event. Registered once with
    /// <see cref="ZoneSlotPool.SetReleaseListener"/>.
    /// </summary>
    /// <remarks>
    /// Called on the main thread from <see cref="ZoneSlotPool.ReleasePlayer"/> and
    /// <see cref="ZoneSlotPool.ReleaseMob"/> after the pool confirmed the id is held and before any slot field,
    /// index or free-list entry changes, so the pool's lookups still succeed during the call. Implementations must
    /// not throw (catch and log; the pool also catches and logs, then completes the release) and must not call back
    /// into the pool.
    /// </remarks>
    public interface IZoneSlotReleaseListener
    {
        /// <summary>Called once per successful release, before the slot changes.</summary>
        /// <param name="id">The EntityID whose slot is about to be released.</param>
        void BeforeRelease(EntityID id);
    }
}
