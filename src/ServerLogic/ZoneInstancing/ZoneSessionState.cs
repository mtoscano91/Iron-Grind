namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// State of a session that holds a player slot (<c>zone-instancing.md</c> CR-ZI-3). Every state
    /// listed here counts toward zone capacity.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>IronGrind.Networking.SessionState</c>, whose numbering differs. Do not cast
    /// between the two; map explicitly.
    /// </remarks>
    public enum ZoneSessionState : byte
    {
        /// <summary>The client is connected and in the zone (the state written at slot allocation).</summary>
        Connected = 0,

        /// <summary>The connection dropped but the session is kept alive (a "ghost"); still holds the slot.</summary>
        Disconnected_SessionActive = 1,

        /// <summary>The character is dead and has not yet requested a respawn.</summary>
        Dead = 2,

        /// <summary>The character is respawning.</summary>
        Respawning = 3,
    }
}
