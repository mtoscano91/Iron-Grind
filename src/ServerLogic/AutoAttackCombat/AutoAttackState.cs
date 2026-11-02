namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Top-level auto-attack state of one player (design/gdd/auto-attack-combat.md, States and Transitions).
    /// Story 001 has the two states below; Story 003 adds <c>OUT_OF_RANGE</c> and <c>PROHIBITED</c>.
    /// </summary>
    public enum AutoAttackState
    {
        /// <summary>No active attack. The action timer is frozen.</summary>
        Idle = 0,

        /// <summary>A valid target is selected and the toggle is ON. The action timer runs.</summary>
        CombatActive = 1,
    }
}
