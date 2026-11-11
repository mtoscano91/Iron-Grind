using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Optional seam through which <see cref="ZoneEntryAdmission"/> reports the networking session transitions of one
    /// entry (Gateway Story 012): <c>Connecting</c> when the token validated and the load starts, <c>Connected</c> when
    /// the slot is allocated and the player is admitted, and a failed connecting when the entry ends otherwise. Keeps
    /// the zone instancing code free of the session state machine. Tick thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// var admission = new ZoneEntryAdmission(zone, pool, lifecycle, persistence, applier, queue, validator,
    ///     disconnector, outbox, continuation, directory, sessionSink: sessionSink);
    /// </code>
    /// </example>
    public interface IZoneEntrySessionSink
    {
        /// <summary>The token validated and the character load is about to start.</summary>
        /// <param name="account">The account the zone resolved from the token store.</param>
        /// <param name="clientId">The connection.</param>
        /// <param name="tick">The current server tick.</param>
        void OnTokenAccepted(AccountID account, uint clientId, uint tick);

        /// <summary>The slot is allocated and the player is handed to the entry continuation.</summary>
        /// <param name="account">The account.</param>
        /// <param name="character">The admitted character.</param>
        /// <param name="tick">The current server tick.</param>
        void OnEntryAdmitted(AccountID account, CharacterID character, uint tick);

        /// <summary>An entry that was reported by <see cref="OnTokenAccepted"/> ended without admission.</summary>
        /// <param name="account">The account.</param>
        /// <param name="trigger">One of <see cref="ZoneEntryAdmission.TRIGGER_LOAD_START_FAILED"/>,
        /// <see cref="ZoneEntryAdmission.TRIGGER_SUPERSEDED"/>, <see cref="ZoneEntryAdmission.TRIGGER_EVICTED"/>,
        /// <see cref="ZoneEntryAdmission.TRIGGER_CLIENT_DROPPED"/>, <see cref="ZoneEntryAdmission.TRIGGER_LOAD_FAILED"/>,
        /// <see cref="ZoneEntryAdmission.TRIGGER_RECHECK_REFUSED"/>, <see cref="ZoneEntryAdmission.TRIGGER_SLOT_ALLOCATION_FAILED"/>,
        /// <see cref="ZoneEntryAdmission.TRIGGER_APPLY_THREW"/> or <see cref="ZoneEntryAdmission.TRIGGER_APPLY_FAILED"/>.</param>
        void OnEntryFailed(AccountID account, string trigger);
    }
}
