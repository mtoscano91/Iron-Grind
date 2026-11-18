namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Death-related party member status (CR-DR-5, CR-DR-12, TR-dr-009). Server-internal, not a wire value;
    /// <see cref="Online"/> is 0 so an uninitialised value never reads as Dead.
    /// </summary>
    public enum PartyMemberDeathStatus : byte
    {
        /// <summary>The member is online and alive (respawn, CR-DR-12). The default value.</summary>
        Online = 0,

        /// <summary>The member is dead (CR-DR-5).</summary>
        Dead = 1
    }
}
