namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Top-level auto-attack state of one player (design/gdd/auto-attack-combat.md, States and Transitions).
    /// Every state except <see cref="Idle"/> runs the action timer.
    /// </summary>
    public enum AutoAttackState
    {
        /// <summary>No active attack. The action timer is frozen.</summary>
        Idle = 0,

        /// <summary>A valid target is selected and the toggle is ON. The action timer runs.</summary>
        CombatActive = 1,

        /// <summary>Active, and the last fire attempt missed out of range. The timer runs; a hit returns to CombatActive.</summary>
        OutOfRange = 2,

        /// <summary>Active, but <c>SetCombatProhibited</c> is set. The timer runs; no auto fires.</summary>
        Prohibited = 3,
    }
}
