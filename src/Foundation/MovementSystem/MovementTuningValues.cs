using System;
using System.Globalization;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The Movement System tuning knobs TK-MOV-1 to TK-MOV-6 as the systems take them (movement-system.md Tuning
    /// Knobs). Every value is inside its GDD safe range: <see cref="Create"/> and <see cref="FromAsset"/> clamp an
    /// out-of-range value to the nearest bound and log one warning per clamped knob, naming the knob, the given
    /// value and the value used (an infinity clamps like any other out-of-range value); a NaN becomes the default,
    /// with one warning naming the knob and the default. Values inside the range log
    /// nothing. Shared by the zone server and the client (ADR-012), so it lives in <c>IronGrind.Foundation</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="BaseMoveSpeed"/> is a reference value only: the live base speed is the Character Stats starting
    /// value (<c>CharacterStubDefaults.MOVEMENT_SPEED</c>), read through <c>GetEffectiveStat</c>, so changing it here
    /// does not change movement speed. A test keeps the two equal. The warning strings are built only on the clamp
    /// path; a valid set of values allocates nothing.
    /// <para>
    /// Build values only with <see cref="Defaults"/>, <see cref="Create"/> or <see cref="FromAsset"/>, never with
    /// <c>default(MovementTuningValues)</c> or <c>new MovementTuningValues()</c>: a struct cannot hide its zero value,
    /// and that value (every knob 0) is outside every safe range.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// MovementTuningValues values = MovementTuningValues.FromAsset(tuningAsset);
    /// bool isStale = messageTick &lt; currentTick - (uint)values.StaleTickTolerance;
    /// </code>
    /// </example>
    public readonly struct MovementTuningValues
    {
        /// <summary>TK-MOV-1 default joystick radius, in screen pixels.</summary>
        public const float JOYSTICK_RADIUS_PX_DEFAULT = 80f;
        /// <summary>TK-MOV-1 safe range minimum.</summary>
        public const float JOYSTICK_RADIUS_PX_MIN = 60f;
        /// <summary>TK-MOV-1 safe range maximum.</summary>
        public const float JOYSTICK_RADIUS_PX_MAX = 120f;

        /// <summary>TK-MOV-2 default dead zone, as a fraction of the joystick radius.</summary>
        public const float DEAD_ZONE_NORMALIZED_DEFAULT = 0.15f;
        /// <summary>TK-MOV-2 safe range minimum.</summary>
        public const float DEAD_ZONE_NORMALIZED_MIN = 0.05f;
        /// <summary>TK-MOV-2 safe range maximum.</summary>
        public const float DEAD_ZONE_NORMALIZED_MAX = 0.30f;

        /// <summary>TK-MOV-3 default base movement speed, in units per second (registry <c>BASE_MOVE_SPEED</c>).</summary>
        public const float BASE_MOVE_SPEED_DEFAULT = 5.0f;
        /// <summary>TK-MOV-3 safe range minimum.</summary>
        public const float BASE_MOVE_SPEED_MIN = 4.0f;
        /// <summary>TK-MOV-3 safe range maximum.</summary>
        public const float BASE_MOVE_SPEED_MAX = 10.0f;

        /// <summary>TK-MOV-4 default stale tolerance, in ticks (F-MOV-7).</summary>
        public const int STALE_TICK_TOLERANCE_DEFAULT = 3;
        /// <summary>TK-MOV-4 safe range minimum.</summary>
        public const int STALE_TICK_TOLERANCE_MIN = 2;
        /// <summary>TK-MOV-4 safe range maximum.</summary>
        public const int STALE_TICK_TOLERANCE_MAX = 5;

        /// <summary>TK-MOV-5 default camera drag sensitivity, in degrees per pixel.</summary>
        public const float CAMERA_DRAG_SENSITIVITY_DEFAULT = 0.25f;
        /// <summary>TK-MOV-5 safe range minimum.</summary>
        public const float CAMERA_DRAG_SENSITIVITY_MIN = 0.10f;
        /// <summary>TK-MOV-5 safe range maximum.</summary>
        public const float CAMERA_DRAG_SENSITIVITY_MAX = 0.50f;

        /// <summary>TK-MOV-6 default consecutive NavMesh misses before the recovery teleport (EC-MOV-11).</summary>
        public const int NAVMESH_STALL_RECOVERY_TICKS_DEFAULT = 10;
        /// <summary>TK-MOV-6 safe range minimum.</summary>
        public const int NAVMESH_STALL_RECOVERY_TICKS_MIN = 5;
        /// <summary>TK-MOV-6 safe range maximum.</summary>
        public const int NAVMESH_STALL_RECOVERY_TICKS_MAX = 30;

        private const string LOG_PREFIX = "[MovementTuning] ";

        /// <summary>The GDD defaults of all six knobs.</summary>
        public static readonly MovementTuningValues Defaults = new MovementTuningValues(
            JOYSTICK_RADIUS_PX_DEFAULT, DEAD_ZONE_NORMALIZED_DEFAULT, BASE_MOVE_SPEED_DEFAULT,
            STALE_TICK_TOLERANCE_DEFAULT, CAMERA_DRAG_SENSITIVITY_DEFAULT, NAVMESH_STALL_RECOVERY_TICKS_DEFAULT);

        private MovementTuningValues(float joystickRadiusPx, float deadZoneNormalized, float baseMoveSpeed,
            int staleTickTolerance, float cameraDragSensitivity, int navMeshStallRecoveryTicks)
        {
            JoystickRadiusPx = joystickRadiusPx;
            DeadZoneNormalized = deadZoneNormalized;
            BaseMoveSpeed = baseMoveSpeed;
            StaleTickTolerance = staleTickTolerance;
            CameraDragSensitivity = cameraDragSensitivity;
            NavMeshStallRecoveryTicks = navMeshStallRecoveryTicks;
        }

        /// <summary>TK-MOV-1: joystick visual radius in screen pixels, in [60, 120].</summary>
        public float JoystickRadiusPx { get; }

        /// <summary>TK-MOV-2: dead zone as a fraction of the joystick radius (F-MOV-5), in [0.05, 0.30].</summary>
        public float DeadZoneNormalized { get; }

        /// <summary>
        /// TK-MOV-3: base movement speed in units per second, in [4.0, 10.0]. A reference value only: the live
        /// base speed is the Character Stats starting value, which this must equal (see the type remarks).
        /// </summary>
        public float BaseMoveSpeed { get; }

        /// <summary>TK-MOV-4: how many ticks behind the server tick an intent may be (F-MOV-7), in [2, 5].</summary>
        public int StaleTickTolerance { get; }

        /// <summary>TK-MOV-5: camera yaw in degrees per pixel of horizontal drag, in [0.10, 0.50].</summary>
        public float CameraDragSensitivity { get; }

        /// <summary>TK-MOV-6: consecutive NavMesh misses before the recovery teleport (EC-MOV-11), in [5, 30].</summary>
        public int NavMeshStallRecoveryTicks { get; }

        /// <summary>
        /// Builds the values from authored numbers, clamping each into its safe range with one warning per clamped
        /// knob (a NaN becomes the default).
        /// </summary>
        /// <param name="joystickRadiusPx">TK-MOV-1.</param>
        /// <param name="deadZoneNormalized">TK-MOV-2.</param>
        /// <param name="baseMoveSpeed">TK-MOV-3.</param>
        /// <param name="staleTickTolerance">TK-MOV-4.</param>
        /// <param name="cameraDragSensitivity">TK-MOV-5.</param>
        /// <param name="navMeshStallRecoveryTicks">TK-MOV-6.</param>
        /// <returns>The values, every knob inside its range.</returns>
        /// <example>
        /// <code>
        /// var values = MovementTuningValues.Create(80f, 0.15f, 5.0f, 3, 0.25f, 10);
        /// </code>
        /// </example>
        public static MovementTuningValues Create(float joystickRadiusPx, float deadZoneNormalized, float baseMoveSpeed,
            int staleTickTolerance, float cameraDragSensitivity, int navMeshStallRecoveryTicks)
        {
            return new MovementTuningValues(
                ClampKnob("JOYSTICK_RADIUS_PX", joystickRadiusPx,
                    JOYSTICK_RADIUS_PX_MIN, JOYSTICK_RADIUS_PX_MAX, JOYSTICK_RADIUS_PX_DEFAULT),
                ClampKnob("DEAD_ZONE_NORMALIZED", deadZoneNormalized,
                    DEAD_ZONE_NORMALIZED_MIN, DEAD_ZONE_NORMALIZED_MAX, DEAD_ZONE_NORMALIZED_DEFAULT),
                ClampKnob("BASE_MOVE_SPEED", baseMoveSpeed,
                    BASE_MOVE_SPEED_MIN, BASE_MOVE_SPEED_MAX, BASE_MOVE_SPEED_DEFAULT),
                ClampKnob("STALE_TICK_TOLERANCE", staleTickTolerance,
                    STALE_TICK_TOLERANCE_MIN, STALE_TICK_TOLERANCE_MAX),
                ClampKnob("CAMERA_DRAG_SENSITIVITY", cameraDragSensitivity,
                    CAMERA_DRAG_SENSITIVITY_MIN, CAMERA_DRAG_SENSITIVITY_MAX, CAMERA_DRAG_SENSITIVITY_DEFAULT),
                ClampKnob("NAVMESH_STALL_RECOVERY_TICKS", navMeshStallRecoveryTicks,
                    NAVMESH_STALL_RECOVERY_TICKS_MIN, NAVMESH_STALL_RECOVERY_TICKS_MAX));
        }

        /// <summary>Builds the values from a tuning asset, with the same clamping and warnings as <see cref="Create"/>.</summary>
        /// <param name="asset">The authored asset; not null.</param>
        /// <returns>The values, every knob inside its range.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
        /// <example>
        /// <code>
        /// MovementTuningValues values = MovementTuningValues.FromAsset(tuningAsset);
        /// </code>
        /// </example>
        public static MovementTuningValues FromAsset(MovementTuning asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            return Create(asset.JoystickRadiusPx, asset.DeadZoneNormalized, asset.BaseMoveSpeed,
                asset.StaleTickTolerance, asset.CameraDragSensitivity, asset.NavMeshStallRecoveryTicks);
        }

        private static float ClampKnob(string knob, float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value))
            {
                Debug.LogWarning(LOG_PREFIX + knob + " is NaN; using default " + Format(fallback));
                return fallback;
            }

            if (value < min || value > max)
            {
                float clamped = value < min ? min : max;
                Debug.LogWarning(LOG_PREFIX + knob + " " + Format(value) + " is outside [" + Format(min) + ", " + Format(max) + "]; using " + Format(clamped));
                return clamped;
            }

            return value;
        }

        private static int ClampKnob(string knob, int value, int min, int max)
        {
            if (value < min || value > max)
            {
                int clamped = value < min ? min : max;
                Debug.LogWarning(LOG_PREFIX + knob + " " + Format(value) + " is outside [" + Format(min) + ", " + Format(max) + "]; using " + Format(clamped));
                return clamped;
            }

            return value;
        }

        // Invariant culture, so the warning reads the same on every machine locale ("0.4", never "0,4").
        private static string Format(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
