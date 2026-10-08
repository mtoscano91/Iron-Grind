using System;
using System.Collections.Generic;
using IronGrind.Currency;

namespace IronGrind.NpcInteraction
{
    /// <summary>
    /// Per-character NPC interaction session flag shared by the Enhancement System and the NPC Shop
    /// (enhancement-system.md CR-ENH-16, CR-ENH-17; npc-shop.md CR-SHOP-3). One flag per character for
    /// all NPCs: opening any NPC silently replaces an existing session. A session has a wall-clock
    /// lifetime measured from the successful open and never extended by activity (AC-ENH-39); expiry
    /// is lazy, checked on read. Closing, a zone transition and the end of the player's session each
    /// clear it (AC-ENH-29, AC-ENH-30). The flag is read only at <c>ConfirmEnhancement</c> validation,
    /// so nothing here cancels an in-flight attempt (CR-ENH-17 "In-flight attempts"); there are no
    /// events or callbacks. Time comes only from the injected clock.
    /// <para>
    /// Not thread-safe: intended for the single server tick thread. Enhancement Story 006.
    /// </para>
    /// </summary>
    public sealed class NpcInteractionSessionTracker : INpcInteractionSessions
    {
        private readonly ITownHubQuery _townHub;
        private readonly Func<double> _clockSeconds;
        private readonly double _lifetimeSeconds;
        private readonly Dictionary<CharacterID, Session> _sessions = new Dictionary<CharacterID, Session>();

        // One open session: the NPC it was opened with and the clock reading at open.
        private readonly struct Session
        {
            public Session(uint npcId, double openTimeSeconds)
            {
                NpcId = npcId;
                OpenTimeSeconds = openTimeSeconds;
            }

            public uint NpcId { get; }
            public double OpenTimeSeconds { get; }
        }

        /// <summary>Creates the tracker over its collaborators.</summary>
        /// <param name="townHub">Town hub query (CR-ENH-16).</param>
        /// <param name="clockSeconds">Wall-clock seconds source; read once per successful open and once per read of a session.</param>
        /// <param name="lifetimeSeconds">
        /// Session lifetime in seconds (AC-ENH-39). Production wiring passes
        /// <c>IronGrind.Networking.CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS</c>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="townHub"/> or <paramref name="clockSeconds"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetimeSeconds"/> is NaN, infinite or not greater than zero.</exception>
        public NpcInteractionSessionTracker(ITownHubQuery townHub, Func<double> clockSeconds, double lifetimeSeconds)
        {
            if (townHub == null) throw new ArgumentNullException(nameof(townHub));
            if (clockSeconds == null) throw new ArgumentNullException(nameof(clockSeconds));
            if (double.IsNaN(lifetimeSeconds) || double.IsInfinity(lifetimeSeconds) || lifetimeSeconds <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds), "Lifetime must be finite and greater than zero.");
            _townHub = townHub;
            _clockSeconds = clockSeconds;
            _lifetimeSeconds = lifetimeSeconds;
        }

        /// <summary>
        /// Opens an NPC interaction session (CR-ENH-16, CR-SHOP-3). If the character is not in the town
        /// hub, returns <see cref="NpcInteractionOpenResult.RejectedNotInTownHub"/> and changes nothing
        /// (an existing session is left as it is). Otherwise silently replaces any existing session
        /// (any NPC) and starts a fresh lifetime. <paramref name="npcId"/> is stored and not validated;
        /// unknown-id rejection is deferred (Story 010 / NPC authoring, npc-shop.md OQ-NS-1).
        /// </summary>
        /// <param name="charId">The character opening the interaction.</param>
        /// <param name="npcId">The NPC opened; stored as given.</param>
        /// <returns>The open result.</returns>
        public NpcInteractionOpenResult Open(CharacterID charId, uint npcId)
        {
            if (!_townHub.IsInTownHub(charId)) return NpcInteractionOpenResult.RejectedNotInTownHub;
            _sessions[charId] = new Session(npcId, _clockSeconds());
            return NpcInteractionOpenResult.NPCInteractionOpened;
        }

        /// <summary>Clears the character's session (AC-ENH-29). No effect when there is none.</summary>
        /// <param name="charId">The character closing the interaction.</param>
        public void Close(CharacterID charId)
        {
            _sessions.Remove(charId);
        }

        /// <summary>Clears the character's session on a zone transition (AC-ENH-30). No effect when there is none.</summary>
        /// <param name="charId">The character that changed zone.</param>
        public void NotifyZoneTransition(CharacterID charId)
        {
            _sessions.Remove(charId);
        }

        /// <summary>
        /// Clears the character's session when the player's session ends: logout, or the disconnected
        /// session's expiry, not the disconnect itself (CR-ENH-17). No effect when there is none.
        /// </summary>
        /// <param name="charId">The character whose session ended.</param>
        public void NotifySessionEnded(CharacterID charId)
        {
            _sessions.Remove(charId);
        }

        /// <summary>
        /// True iff the character has a session that has not expired (CR-ENH-17, AC-ENH-39): active while
        /// now is before open time plus lifetime, expired from exactly that moment on. An expired entry
        /// is removed when detected. Reading never extends the lifetime.
        /// </summary>
        /// <param name="charId">The character to query.</param>
        /// <returns>Whether an active session exists.</returns>
        public bool IsActive(CharacterID charId)
        {
            uint npcId;
            return TryGetActiveNpcId(charId, out npcId);
        }

        /// <summary>
        /// Gets the NPC id of the character's active session, under the same expiry rule as
        /// <see cref="IsActive"/>.
        /// </summary>
        /// <param name="charId">The character to query.</param>
        /// <param name="npcId">The stored NPC id when active; otherwise 0.</param>
        /// <returns>True iff the session is active.</returns>
        public bool TryGetActiveNpcId(CharacterID charId, out uint npcId)
        {
            npcId = 0;
            Session session;
            if (!_sessions.TryGetValue(charId, out session)) return false;
            if (_clockSeconds() >= session.OpenTimeSeconds + _lifetimeSeconds)
            {
                _sessions.Remove(charId);
                return false;
            }
            npcId = session.NpcId;
            return true;
        }
    }
}
