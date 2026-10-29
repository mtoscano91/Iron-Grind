using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Designer-editable Movement System tuning knobs TK-MOV-1 to TK-MOV-6 (movement-system.md Tuning Knobs).
    /// One asset serves the zone server and the client, so it lives in <c>IronGrind.Foundation</c> (ADR-012).
    /// Systems never read this asset directly: they take a <see cref="MovementTuningValues"/> built by
    /// <see cref="MovementTuningValues.FromAsset"/>, which clamps each knob into its GDD safe range.
    /// </summary>
    /// <remarks>
    /// <c>[SerializeField]</c> is applied to private fields only — applying it to properties is a compile
    /// error in Unity 6.3. The field initialisers are the GDD defaults, so a new asset starts valid.
    /// </remarks>
    /// <example>
    /// <code>
    /// MovementTuningValues values = MovementTuningValues.FromAsset(tuningAsset);
    /// float deadZone = values.DeadZoneNormalized;
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "MovementTuning", menuName = "IronGrind/Movement Tuning")]
    public class MovementTuning : ScriptableObject
    {
        [SerializeField] private float _joystickRadiusPx = MovementTuningValues.JOYSTICK_RADIUS_PX_DEFAULT;
        [SerializeField] private float _deadZoneNormalized = MovementTuningValues.DEAD_ZONE_NORMALIZED_DEFAULT;
        [SerializeField] private float _baseMoveSpeed = MovementTuningValues.BASE_MOVE_SPEED_DEFAULT;
        [SerializeField] private int _staleTickTolerance = MovementTuningValues.STALE_TICK_TOLERANCE_DEFAULT;
        [SerializeField] private float _cameraDragSensitivity = MovementTuningValues.CAMERA_DRAG_SENSITIVITY_DEFAULT;
        [SerializeField] private int _navMeshStallRecoveryTicks = MovementTuningValues.NAVMESH_STALL_RECOVERY_TICKS_DEFAULT;

        /// <summary>TK-MOV-1: joystick visual radius in screen pixels, as authored.</summary>
        public float JoystickRadiusPx => _joystickRadiusPx;

        /// <summary>TK-MOV-2: dead zone as a fraction of the joystick radius, as authored.</summary>
        public float DeadZoneNormalized => _deadZoneNormalized;

        /// <summary>
        /// TK-MOV-3: base movement speed in units per second, as authored. A reference value only — the live base
        /// speed is the Character Stats starting value; see <see cref="MovementTuningValues.BaseMoveSpeed"/>.
        /// </summary>
        public float BaseMoveSpeed => _baseMoveSpeed;

        /// <summary>TK-MOV-4: how many ticks behind the server tick an intent may be, as authored.</summary>
        public int StaleTickTolerance => _staleTickTolerance;

        /// <summary>TK-MOV-5: camera yaw in degrees per pixel of horizontal drag, as authored.</summary>
        public float CameraDragSensitivity => _cameraDragSensitivity;

        /// <summary>TK-MOV-6: consecutive NavMesh misses before the recovery teleport, as authored.</summary>
        public int NavMeshStallRecoveryTicks => _navMeshStallRecoveryTicks;

        /// <summary>Sets every knob; test seam only (EditMode tests cannot write private serialized fields).</summary>
        internal void SetForTesting(float joystickRadiusPx, float deadZoneNormalized, float baseMoveSpeed,
            int staleTickTolerance, float cameraDragSensitivity, int navMeshStallRecoveryTicks)
        {
            _joystickRadiusPx = joystickRadiusPx;
            _deadZoneNormalized = deadZoneNormalized;
            _baseMoveSpeed = baseMoveSpeed;
            _staleTickTolerance = staleTickTolerance;
            _cameraDragSensitivity = cameraDragSensitivity;
            _navMeshStallRecoveryTicks = navMeshStallRecoveryTicks;
        }

#if UNITY_EDITOR
        // Editor feedback: logs the same out-of-range warnings FromAsset logs, so the designer sees them on edit.
        // The serialized values are not changed; the clamp happens when the values are built. Unity calls
        // OnValidate only in the Editor, so it is compiled only there.
        private void OnValidate()
        {
            MovementTuningValues.FromAsset(this);
        }
#endif
    }
}
