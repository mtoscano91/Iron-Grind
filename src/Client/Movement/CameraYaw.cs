using UnityEngine;
using IronGrind.MovementSystem;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// The camera yaw driven by horizontal camera-drag (movement-system.md CR-MOV-3, AC-MOV-4; Movement Story 011).
    /// Called every frame with <see cref="TouchZoneRouter.CameraDragDeltaPx"/>. Positive x raises the yaw (clockwise
    /// seen from above, the same sense as the joystick angle). Allocation-free.
    /// </summary>
    public sealed class CameraYaw
    {
        private const float FULL_TURN_DEG = 360f;

        private readonly float _sensitivityDegPerPx;

        /// <summary>Creates a camera yaw of 0 degrees.</summary>
        /// <param name="tuning">TK-MOV-5 (<see cref="MovementTuningValues.CameraDragSensitivity"/>) is read from it.</param>
        public CameraYaw(MovementTuningValues tuning)
        {
            _sensitivityDegPerPx = tuning.CameraDragSensitivity;
            YawDeg = 0f;
        }

        /// <summary>The yaw in degrees, wrapped to [0, 360). Initial value 0.</summary>
        public float YawDeg { get; private set; }

        /// <summary>
        /// The camera's horizontal forward: <c>AngleAxis(YawDeg, up) * forward</c>, a unit vector with Y = 0.
        /// </summary>
        public Vector3 FlatForward => Quaternion.AngleAxis(YawDeg, Vector3.up) * Vector3.forward;

        /// <summary>
        /// AC-MOV-4: <c>YawDeg += deltaPx.x * CAMERA_DRAG_SENSITIVITY</c>; <c>deltaPx.y</c> is ignored (CR-MOV-3 is
        /// horizontal only). The result is wrapped with <see cref="Mathf.Repeat"/> so a delta of any size lands in
        /// [0, 360) (a single +/-360 step would not be enough), then guarded against the float rounding case where
        /// Repeat returns exactly 360. A non-finite change (NaN or infinite delta) is ignored, so the yaw never
        /// becomes NaN.
        /// </summary>
        /// <param name="deltaPx">The frame's camera drag delta in pixels.</param>
        public void ApplyDrag(Vector2 deltaPx)
        {
            float change = deltaPx.x * _sensitivityDegPerPx;
            if (float.IsNaN(change) || float.IsInfinity(change))
            {
                return;
            }

            float yaw = Mathf.Repeat(YawDeg + change, FULL_TURN_DEG);
            if (yaw >= FULL_TURN_DEG)
            {
                yaw -= FULL_TURN_DEG;
            }

            YawDeg = yaw;
        }
    }
}
