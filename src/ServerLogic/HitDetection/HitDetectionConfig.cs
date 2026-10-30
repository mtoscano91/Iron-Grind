using System;
using UnityEngine;

namespace IronGrind.HitDetection
{
    /// <summary>
    /// Immutable, validated Hit Detection tuning values (design/gdd/hit-detection.md Tuning Knobs and
    /// constants table). Injected into <see cref="HitDetector"/> by constructor; there is no static
    /// lookup. Hit Detection Story 001.
    /// </summary>
    /// <remarks>
    /// The GDD's safe range for the facing tolerance is guidance: a value outside it logs one warning
    /// and is kept as given. Only values that break the cosine test are rejected.
    /// </remarks>
    public sealed class HitDetectionConfig
    {
        /// <summary>Default facing half-angle in degrees (the story's ATTACK_FACING_TOLERANCE_DEG).</summary>
        public const float ATTACK_FACING_TOLERANCE_DEG_DEFAULT = 45f;

        /// <summary>Lower end of the safe tuning range for the facing half-angle, in degrees.</summary>
        public const float ATTACK_FACING_TOLERANCE_DEG_SAFE_MIN = 45f;

        /// <summary>Upper end of the safe tuning range for the facing half-angle, in degrees.</summary>
        public const float ATTACK_FACING_TOLERANCE_DEG_SAFE_MAX = 90f;

        /// <summary>Squared ground distance below which the facing check is skipped (CR-HD-3), in units squared.</summary>
        public const float POINT_BLANK_EPSILON_SQ = 0.0005f;

        /// <summary>Smallest practical attack range, in units. Callers validate it; Hit Detection does not.</summary>
        public const float ATTACK_RANGE_MIN = 0.5f;

        /// <summary>Tolerance sentinel that disables the facing check (cos = -1), in degrees.</summary>
        public const float FACING_CHECK_DISABLED_DEG = 180f;

        /// <summary>Builds and validates a config.</summary>
        /// <param name="attackFacingToleranceDeg">Facing half-angle in degrees, in (0, 180]. Outside [45, 90] it warns once; 180 does not.</param>
        /// <exception cref="ArgumentOutOfRangeException">The value is NaN, infinite, or outside (0, 180].</exception>
        public HitDetectionConfig(float attackFacingToleranceDeg)
        {
            if (!(attackFacingToleranceDeg > 0f && attackFacingToleranceDeg <= FACING_CHECK_DISABLED_DEG))
                throw new ArgumentOutOfRangeException(nameof(attackFacingToleranceDeg), attackFacingToleranceDeg, "Must be in (0, 180].");

            if (attackFacingToleranceDeg != FACING_CHECK_DISABLED_DEG
                && (attackFacingToleranceDeg < ATTACK_FACING_TOLERANCE_DEG_SAFE_MIN
                    || attackFacingToleranceDeg > ATTACK_FACING_TOLERANCE_DEG_SAFE_MAX))
            {
                Debug.LogWarning($"HitDetectionConfig: attack facing tolerance {attackFacingToleranceDeg} is outside the safe range "
                    + $"[{ATTACK_FACING_TOLERANCE_DEG_SAFE_MIN}, {ATTACK_FACING_TOLERANCE_DEG_SAFE_MAX}] degrees; using it unclamped.");
            }

            AttackFacingToleranceDeg = attackFacingToleranceDeg;
        }

        /// <summary>The default configuration: a 45 degree facing half-angle.</summary>
        public static HitDetectionConfig Default { get; } = new HitDetectionConfig(ATTACK_FACING_TOLERANCE_DEG_DEFAULT);

        /// <summary>Facing half-angle in degrees (ATTACK_FACING_TOLERANCE_DEG).</summary>
        public float AttackFacingToleranceDeg { get; }
    }
}
