namespace IronGrind.Client.Movement
{
    /// <summary>
    /// The client movement sub-states of movement-system.md "States and Transitions" (CR-MOV-5, AC-MOV-12, AC-MOV-13,
    /// AC-MOV-19), as <see cref="AutoRunState"/> reports them each tick (Movement Story 011).
    /// </summary>
    public enum ClientMovementSubState
    {
        /// <summary>No joystick intent and auto-run off: the direction is zero.</summary>
        Idle,
        /// <summary>The joystick is held (outside the dead zone) and auto-run is off.</summary>
        MovingJoystick,
        /// <summary>Auto-run is on and the joystick is at rest: the character moves along its facing.</summary>
        MovingAutoRun,
        /// <summary>Auto-run is on and the joystick is held: the joystick direction wins, the flag stays on.</summary>
        MovingCombined,
        /// <summary>Stun or root (CR-MOV-8): no forward intent; the auto-run flag is cleared.</summary>
        Prohibited
    }
}
