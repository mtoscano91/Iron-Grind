using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Orbit camera that follows a target at the yaw of <see cref="CameraYaw"/> (movement-system.md CR-MOV-3;
    /// Movement Story 013). Updates in <c>LateUpdate</c> so it runs after the target has moved.
    /// </summary>
    public sealed class MovementCameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _distance = 15f;
        [SerializeField] private float _pitchDeg = 45f;

        private CameraYaw _yaw;

        /// <summary>Sets the yaw source; with none the camera uses yaw 0.</summary>
        /// <param name="yaw">The yaw to follow, or null.</param>
        public void SetYawSource(CameraYaw yaw)
        {
            _yaw = yaw;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            float yawDeg = _yaw != null ? _yaw.YawDeg : 0f;
            Vector3 offset = Quaternion.Euler(_pitchDeg, yawDeg, 0f) * new Vector3(0f, 0f, -_distance);
            Vector3 targetPosition = _target.position;
            transform.position = targetPosition + offset;
            transform.LookAt(targetPosition);
        }
    }
}
