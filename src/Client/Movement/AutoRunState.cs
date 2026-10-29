using System;
using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// The client-only auto-run flag, the client movement sub-state, the movement direction and the character facing
    /// (movement-system.md CR-MOV-3, CR-MOV-5, CR-MOV-8, F-MOV-3, F-MOV-5; AC-MOV-6, 12, 13, 19, 20; EC-MOV-5, 7, 9,
    /// 18; Movement Story 011). Driven once per network tick by MovementIntentProducer (Story 012); the server never sees
    /// the flag (CR-MOV-5). Allocation-free.
    /// </summary>
    public sealed class AutoRunState
    {
        // F-MOV-3 cancels on dot < 0. A joystick exactly 90 degrees off yields a dot of about +/-1e-7 from float
        // rounding in the quaternion rotation; this margin keeps that case "dot = 0, not cancelled" (AC-MOV-13).
        private const float CANCEL_DOT_EPSILON = 1e-4f;
        private const float MIN_FLAT_MAGNITUDE = 0.001f;

        private readonly IClientProhibitedSource _prohibited;
        private readonly CharacterFacing _facing = new CharacterFacing();
        private bool _wasProhibited;
        // AC-MOV-6: the camera forward when the joystick left rest; the held joystick is read against it until the
        // joystick is back at rest, so a camera drag mid-hold changes neither the facing nor the direction.
        private bool _hasLatchedForward;
        private Vector3 _latchedForward;

        /// <summary>Creates the state: flag off, <see cref="ClientMovementSubState.Idle"/>, direction zero.</summary>
        /// <param name="prohibited">The Prohibited (stun/root) source; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="prohibited"/> is null.</exception>
        public AutoRunState(IClientProhibitedSource prohibited)
        {
            _prohibited = prohibited ?? throw new ArgumentNullException(nameof(prohibited));
            SubState = ClientMovementSubState.Idle;
            MoveDirection = Vector3.zero;
        }

        /// <summary>The auto-run flag (CR-MOV-5).</summary>
        public bool IsActive { get; private set; }

        /// <summary>The client sub-state after the latest <see cref="Tick"/>.</summary>
        public ClientMovementSubState SubState { get; private set; }

        /// <summary>The movement direction after the latest <see cref="Tick"/>: a unit XZ vector, or zero.</summary>
        public Vector3 MoveDirection { get; private set; }

        /// <summary>The character facing (CR-MOV-3). Never null, never a zero vector (EC-MOV-18).</summary>
        public CharacterFacing Facing => _facing;

        /// <summary>
        /// AC-MOV-12: toggles the flag now, so <see cref="IsActive"/> reflects it at once; the direction changes on
        /// the next <see cref="Tick"/>. Ignored while Prohibited (AC-MOV-19); the source is read at press time.
        /// Skill taps never call this (EC-MOV-5).
        /// </summary>
        public void PressButton()
        {
            if (_prohibited.IsProhibited)
            {
                return;
            }

            IsActive = !IsActive;
        }

        /// <summary>
        /// Zone entry / reconnect (CR-MOV-5, called by Story 013): flag off, <see cref="ClientMovementSubState.Idle"/>,
        /// direction zero. The facing is kept.
        /// </summary>
        public void Reset()
        {
            IsActive = false;
            _wasProhibited = false;
            _hasLatchedForward = false;
            SubState = ClientMovementSubState.Idle;
            MoveDirection = Vector3.zero;
        }

        /// <summary>
        /// One network tick. Order: (0) at rest is evaluated (<c>!joystick.IsActive || joystick.IsDead</c>, F-MOV-5);
        /// at rest the latch is dropped, otherwise the camera forward is latched on the tick the joystick leaves rest
        /// and the joystick world direction is computed against that latched forward (AC-MOV-6: a camera drag
        /// mid-hold does not turn the character; the latch is kept through Prohibited); (1) Prohibited clears the
        /// flag, direction zero, sub-state Prohibited; (2) the first tick after Prohibited starts from Idle without
        /// re-arming the flag (EC-MOV-7); (3) F-MOV-3 cancel: flag on, joystick not at rest and
        /// <c>dot(joystickDirWorld, latchedForward) &lt; -CANCEL_DOT_EPSILON</c> clears the flag (the camera
        /// forward, not the previous facing); (4) facing = joystick world direction while held, else the camera
        /// forward (CR-MOV-3, AC-MOV-6); (5) direction and sub-state.
        /// </summary>
        /// <param name="joystick">The router's latest joystick state.</param>
        /// <param name="cameraFlatForward">The camera's horizontal forward (<see cref="CameraYaw.FlatForward"/>); a
        /// vector with no XZ part falls back to <see cref="Vector3.forward"/> (see <see cref="CameraRelativeDirection"/>).</param>
        public void Tick(JoystickState joystick, Vector3 cameraFlatForward)
        {
            bool atRest = !joystick.IsActive || joystick.IsDead;
            Vector3 joystickDir = Vector3.zero;
            if (atRest)
            {
                _hasLatchedForward = false;
            }
            else
            {
                if (!_hasLatchedForward)
                {
                    _latchedForward = cameraFlatForward;
                    _hasLatchedForward = true;
                }

                joystickDir = CameraRelativeDirection.WorldMoveDir(joystick.AngleDeg, _latchedForward);
            }

            if (_prohibited.IsProhibited)
            {
                IsActive = false;
                _wasProhibited = true;
                SubState = ClientMovementSubState.Prohibited;
                MoveDirection = Vector3.zero;
                UpdateFacing(atRest, joystickDir, cameraFlatForward);
                return;
            }

            if (_wasProhibited)
            {
                _wasProhibited = false;
                SubState = ClientMovementSubState.Idle;
            }

            if (IsActive && !atRest && FlatDot(joystickDir, _latchedForward) < -CANCEL_DOT_EPSILON)
            {
                IsActive = false;
            }

            UpdateFacing(atRest, joystickDir, cameraFlatForward);

            if (!atRest)
            {
                MoveDirection = joystickDir;
                SubState = IsActive ? ClientMovementSubState.MovingCombined : ClientMovementSubState.MovingJoystick;
            }
            else if (IsActive)
            {
                MoveDirection = _facing.Forward;
                SubState = ClientMovementSubState.MovingAutoRun;
            }
            else
            {
                MoveDirection = Vector3.zero;
                SubState = ClientMovementSubState.Idle;
            }
        }

        private void UpdateFacing(bool atRest, Vector3 joystickDir, Vector3 cameraFlatForward)
        {
            _facing.Face(atRest ? cameraFlatForward : joystickDir);
        }

        // Dot product of a unit XZ vector with the XZ part of the camera forward, normalised; Vector3.forward when
        // the camera forward has no horizontal part (the same fallback as CameraRelativeDirection).
        private static float FlatDot(Vector3 unitDir, Vector3 cameraForward)
        {
            float length = Mathf.Sqrt(cameraForward.x * cameraForward.x + cameraForward.z * cameraForward.z);
            if (length < MIN_FLAT_MAGNITUDE)
            {
                return unitDir.z;
            }

            return (unitDir.x * cameraForward.x + unitDir.z * cameraForward.z) / length;
        }
    }
}
