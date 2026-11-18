namespace IronGrind.CharacterPersistence
{
    /// <summary>Why a session is being saved through <see cref="ICharacterPersistence.SaveSession(IronGrind.Currency.CharacterID, SessionEndReason, System.Threading.CancellationToken)"/> (CR-CP-1).</summary>
    public enum SessionEndReason : byte
    {
        /// <summary>The player logged out normally.</summary>
        CleanLogout = 0,

        /// <summary>The session time-to-live expired.</summary>
        SessionTTLExpiry = 1,

        /// <summary>The zone the character was in closed.</summary>
        ZoneClosed = 2,

        /// <summary>The gateway evicted the session (<c>Evict</c> link message, Authentication Story 014).</summary>
        Evicted = 3,

        /// <summary>
        /// A disconnected ghost died; the session ends with the respawn HP (Death and Respawn Story 010).
        /// </summary>
        GhostDeath = 4,
    }
}
