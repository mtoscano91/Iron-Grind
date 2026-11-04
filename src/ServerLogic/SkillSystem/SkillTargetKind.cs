namespace IronGrind.SkillSystem
{
    /// <summary>What a skill target is, for the V-7 friendly / hostile check (Story 005).</summary>
    public enum SkillTargetKind : byte
    {
        /// <summary>A player: friendly at MVP (any player in the zone).</summary>
        Player = 0,

        /// <summary>A mob: hostile.</summary>
        Mob = 1,
    }
}
