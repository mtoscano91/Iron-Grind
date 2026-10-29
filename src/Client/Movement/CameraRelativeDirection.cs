using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>F-MOV-4: turns the joystick angle and the camera facing into a world move direction.</summary>
    public static class CameraRelativeDirection
    {
        /// <summary>A camera forward whose horizontal part is shorter than this has no usable facing.</summary>
        public const float MIN_FLAT_FORWARD_MAGNITUDE = 0.001f;

        /// <summary>
        /// <c>worldMoveDir = normalize(AngleAxis(joystickAngle_deg, up) * normalize(camForward.xz))</c>. When the
        /// camera's horizontal forward is shorter than <see cref="MIN_FLAT_FORWARD_MAGNITUDE"/> (looking straight
        /// up or down), <see cref="Vector3.forward"/> is used as the flat forward, the joystick angle still
        /// applies, and a <see cref="Debug.Assert(bool, string)"/> fires in development builds.
        /// </summary>
        /// <param name="joystickAngleDeg">Clockwise degrees from screen up, [0, 360).</param>
        /// <param name="cameraForward">The camera's forward vector (any length, may be tilted).</param>
        /// <returns>A unit vector on the XZ plane.</returns>
        public static Vector3 WorldMoveDir(float joystickAngleDeg, Vector3 cameraForward)
        {
            Vector3 flat = new Vector3(cameraForward.x, 0f, cameraForward.z);
            if (flat.magnitude < MIN_FLAT_FORWARD_MAGNITUDE)
            {
                Debug.Assert(false, "CameraRelativeDirection: camera forward has no horizontal component; using Vector3.forward");
                flat = Vector3.forward;
            }

            Vector3 rotated = Quaternion.AngleAxis(joystickAngleDeg, Vector3.up) * flat.normalized;
            return rotated.normalized;
        }
    }
}
