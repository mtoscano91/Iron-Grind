using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Optional extension of <see cref="IZoneEntryTokenValidator"/> for the token-path reconnect (<c>networking-session-token.md</c>
    /// CR-TOK-9): resolves the account from the ghost session that plays the handshake's character and compares the presented token
    /// (fixed-time). <see cref="ZoneEntryAdmission"/> asks it only after <see cref="IZoneEntryTokenValidator.TryValidate"/> failed, so
    /// the fresh-login path is untouched. Tick thread; must not block or allocate.
    /// </summary>
    public interface IZoneReconnectTokenValidator
    {
        /// <summary>True when the request's character belongs to a <c>Disconnected_SessionActive</c> session and the token matches.</summary>
        /// <param name="request">The decoded handshake.</param>
        /// <param name="account">The ghost's account when this returns true; <see cref="AccountID.Invalid"/> otherwise.</param>
        /// <returns>True for a matching token-path reconnect. Never changes any state.</returns>
        bool TryValidateReconnect(in ZoneEntryRequest request, out AccountID account);
    }

    /// <summary>
    /// Session-layer seam of the token-path reconnect (ST-NET-1, CR-NET-6.4). Keeps the zone instancing code free of the session
    /// state machine. Tick thread only.
    /// </summary>
    public interface IZoneReconnectSessionSink
    {
        /// <summary>
        /// ST-NET-1 <c>Disconnected_SessionActive -&gt; Reconnecting</c> ("session token matches").
        /// </summary>
        /// <param name="account">The ghost's account.</param>
        /// <param name="tick">The current server tick.</param>
        /// <returns>False when the session may not reconnect now (not a ghost, or its end is in flight); nothing changed.</returns>
        bool TryBeginReconnect(AccountID account, uint tick);

        /// <summary>The CR-NET-6.4 sequence and <c>Reconnecting -&gt; Connected</c>. The token is not rotated again.</summary>
        /// <param name="account">The account.</param>
        /// <param name="character">The reconnecting character.</param>
        /// <param name="entityId">The ghost's entity, which stays.</param>
        /// <param name="tick">The current server tick.</param>
        void CompleteReconnect(AccountID account, CharacterID character, EntityID entityId, uint tick);

        /// <summary>
        /// Undoes a reconnect that did not finish (or a rebound slot that never acked): <c>Reconnecting</c> or <c>Connected</c> back to
        /// <c>Disconnected_SessionActive</c>, keeping the session's original <c>SessionExpiryTick</c> and failure count, so the normal TTL /
        /// session-end path (with its save) still ends it.
        /// </summary>
        /// <param name="account">The ghost's account.</param>
        /// <returns>False when the session is in no state that can revert (already a ghost, ended, unknown); nothing changed.</returns>
        bool RevertToGhost(AccountID account);
    }

    /// <summary>
    /// Entry continuation of the token-path reconnect (<c>zone-instancing.md</c> CR-ZI-8 steps 6-7): the player's entity and slot stay,
    /// so there is no <c>PlayerJoinedZone</c>, no new roster entry and no entry listener call.
    /// </summary>
    public interface IZoneReconnectContinuation
    {
        /// <summary>
        /// The slot is rebound to <paramref name="clientId"/>: map the connection, send the snapshot, <c>SessionReady</c> and the cooldown snapshot.
        /// An implementation that fails returns false after logging and leaves the directory as it found it; the caller then reverts the ghost.
        /// </summary>
        /// <param name="clientId">The new connection.</param>
        /// <param name="slot">The slot index.</param>
        /// <param name="entityId">The ghost's EntityID.</param>
        /// <returns>True when every step ran; false when a step failed (already logged).</returns>
        bool OnPlayerReconnected(uint clientId, byte slot, EntityID entityId);
    }
}
