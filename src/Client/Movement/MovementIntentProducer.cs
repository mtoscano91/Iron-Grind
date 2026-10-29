using System;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Turns the client movement state into at most one <see cref="MovementIntentMessage"/> per network tick
    /// (movement-system.md CR-MOV-5, CR-MOV-8, CR-MOV-10, EC-MOV-15, EC-MOV-21; AC-MOV-1 wire part, AC-MOV-14;
    /// Movement Story 012). Called once per tick by the client loop (Story 013). Drives <see cref="AutoRunState"/>
    /// itself. Allocation-free on the normal path; only the non-unit warning builds a string.
    /// </summary>
    public sealed class MovementIntentProducer
    {
        private readonly AutoRunState _state;
        private readonly IMovementIntentSink _sink;
        // Bools rather than sentinel values: tick 0 and facing 0 are both valid.
        private bool _hasProcessedTick;
        private uint _lastProcessedTick;
        private bool _hasSent;
        private short _lastSentFacing;

        /// <summary>Creates a producer with nothing sent and no tick processed.</summary>
        /// <param name="state">The client movement state this producer ticks; not null.</param>
        /// <param name="sink">Where messages go; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="sink"/> is null.</exception>
        public MovementIntentProducer(AutoRunState state, IMovementIntentSink sink)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>
        /// One network tick (CR-MOV-10). Order: (1) a tick already processed and <c>serverTick &lt;= last</c> does
        /// nothing (repeated or older tick); a forward jump is fine; (2) <see cref="AutoRunState.Tick"/> and record
        /// the tick; (3) encode the facing; (4) send when the move direction is not zero, or the encoded facing
        /// differs from the last sent one, or nothing was sent since construction / <see cref="Reset"/>; otherwise no
        /// message (CR-MOV-10 "may suppress"); (5) build, send, remember the facing. No extra message on release
        /// (the server treats a missing message as zero input); while Prohibited the state's direction is zero, so
        /// only facing changes are sent, with direction (0, 0) (CR-MOV-8).
        /// </summary>
        /// <param name="serverTick">The server tick this intent is for (NGO <c>NetworkTickSystem</c>).</param>
        /// <param name="joystick">The router's latest joystick state.</param>
        /// <param name="cameraFlatForward">The camera's horizontal forward (<see cref="CameraYaw.FlatForward"/>).</param>
        public void OnTick(uint serverTick, JoystickState joystick, Vector3 cameraFlatForward)
        {
            if (_hasProcessedTick && serverTick <= _lastProcessedTick)
            {
                return;
            }

            _state.Tick(joystick, cameraFlatForward);
            _lastProcessedTick = serverTick;
            _hasProcessedTick = true;

            float yawDeg = _state.Facing.YawDeg;
            short facing = MovementIntentMessageCodec.EncodeFacing(yawDeg);
            Vector3 direction = _state.MoveDirection;
            // Components compared explicitly: AutoRunState sets the direction to exactly zero or a unit vector, and
            // this avoids Vector3's approximate equality.
            bool isMoving = direction.x != 0f || direction.z != 0f;

            if (!isMoving && _hasSent && facing == _lastSentFacing)
            {
                return;
            }

            MovementIntentMessage message = BuildMessage(direction, yawDeg, serverTick);
            _sink.Send(in message);
            _hasSent = true;
            _lastSentFacing = facing;
        }

        /// <summary>
        /// EC-MOV-15 (zone entry / reconnect, called by Story 013): forgets the last sent facing and the last
        /// processed tick, and resets the auto-run state (flag off). The next tick always sends and may use any tick
        /// number.
        /// </summary>
        public void Reset()
        {
            _hasProcessedTick = false;
            _lastProcessedTick = 0u;
            _hasSent = false;
            _lastSentFacing = 0;
            _state.Reset();
        }

        /// <summary>
        /// CR-MOV-8 / CR-MOV-10 / EC-MOV-21: encodes one message with the Foundation codec (no second encoder). The
        /// direction is the XZ part of <paramref name="moveDirection"/>; when the codec reports a non-unit input the
        /// substituted (32767, 0) is sent and one warning is logged per message (wire GDD pre-encode guard).
        /// </summary>
        /// <param name="moveDirection">The movement direction (unit XZ, or zero).</param>
        /// <param name="facingDeg">The facing yaw in degrees.</param>
        /// <param name="serverTick">Copied to <see cref="MovementIntentMessage.TickNumber"/>.</param>
        /// <returns>The encoded message.</returns>
        internal static MovementIntentMessage BuildMessage(Vector3 moveDirection, float facingDeg, uint serverTick)
        {
            (short x, short z) = MovementIntentMessageCodec.EncodeDirection(
                new Vector2(moveDirection.x, moveDirection.z), out bool wasNonUnit);
            if (wasNonUnit)
            {
                // Anomaly path only: formatting here is allowed to allocate.
                Debug.LogWarning("MovementIntentProducer: non-unit movement direction (" + moveDirection.x + ", "
                    + moveDirection.z + ") replaced by (" + x + ", " + z + ")");
            }

            return new MovementIntentMessage(x, z, MovementIntentMessageCodec.EncodeFacing(facingDeg), serverTick);
        }
    }
}
