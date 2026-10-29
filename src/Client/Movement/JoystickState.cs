using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// The floating joystick as the router reports it (movement-system.md CR-MOV-2, F-MOV-5). When
    /// <see cref="IsActive"/> is false, or <see cref="IsDead"/> is true, the move direction is zero.
    /// </summary>
    public readonly struct JoystickState
    {
        /// <summary>The state with no joystick touch: direction zero.</summary>
        public static readonly JoystickState Inactive = new JoystickState(false, Vector2.zero, Vector2.zero, true, 0f);

        /// <summary>Creates a joystick state.</summary>
        /// <param name="isActive">True while a joystick touch is held.</param>
        /// <param name="anchor">The exact pixel where the touch began.</param>
        /// <param name="current">The touch's latest pixel.</param>
        /// <param name="isDead">True inside the dead zone.</param>
        /// <param name="angleDeg">Clockwise degrees from screen up in [0, 360); 0 when dead.</param>
        public JoystickState(bool isActive, Vector2 anchor, Vector2 current, bool isDead, float angleDeg)
        {
            IsActive = isActive;
            Anchor = anchor;
            Current = current;
            IsDead = isDead;
            AngleDeg = angleDeg;
        }

        /// <summary>True while a joystick touch is held.</summary>
        public bool IsActive { get; }

        /// <summary>The exact pixel where the touch began (floating joystick, no snap).</summary>
        public Vector2 Anchor { get; }

        /// <summary>The touch's latest pixel.</summary>
        public Vector2 Current { get; }

        /// <summary>True inside the dead zone (direction zero).</summary>
        public bool IsDead { get; }

        /// <summary><c>joystickAngle_deg</c>: clockwise from screen up, in [0, 360); 0 when dead.</summary>
        public float AngleDeg { get; }

        /// <summary>
        /// F-MOV-5: builds the state of a held touch. <c>normalizedDrag = |current - anchor| / radius</c>; below the
        /// dead zone the direction is zero, otherwise the angle is <c>atan2(offset.x, offset.y)</c> wrapped to [0, 360).
        /// </summary>
        /// <param name="anchor">The Began pixel.</param>
        /// <param name="current">The latest pixel.</param>
        /// <param name="radiusPx">TK-MOV-1.</param>
        /// <param name="deadZoneNormalized">TK-MOV-2.</param>
        /// <returns>An active state.</returns>
        public static JoystickState FromDrag(Vector2 anchor, Vector2 current, float radiusPx, float deadZoneNormalized)
        {
            Vector2 offset = current - anchor;
            // The cast rounds the ratio to float before comparing: Mono may keep it at higher precision, which
            // puts 12 / 80 below 0.15f and makes the AC-MOV-1 boundary (12 px at defaults) dead.
            float normalizedDrag = (float)(offset.magnitude / radiusPx);
            bool isDead = normalizedDrag < deadZoneNormalized;
            float angle = 0f;
            if (!isDead)
            {
                angle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }

                if (angle >= 360f)
                {
                    angle -= 360f;
                }
            }

            return new JoystickState(true, anchor, current, isDead, angle);
        }
    }
}
