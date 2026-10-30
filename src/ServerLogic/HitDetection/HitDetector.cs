using System;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.HitDetection
{
    /// <summary>
    /// Server-side range and facing check for one attack (design/gdd/hit-detection.md CR-HD-1 to CR-HD-4,
    /// F-HD-1, F-HD-2). Pure math on ground-plane (XZ) positions; Y is ignored. Stateless apart from the
    /// cached cosine of the facing tolerance, and allocation free. Hit Detection Story 001.
    /// </summary>
    /// <remarks>
    /// The cosine is a float, so a target exactly on the tolerance edge can land either side by rounding:
    /// at 90 degrees the cosine is about -4.4e-8, so a perpendicular target passes.
    /// </remarks>
    public sealed class HitDetector
    {
        private readonly float _cosTolerance;
        private readonly bool _facingDisabled;

        /// <summary>Creates a detector and caches the cosine of the configured facing tolerance.</summary>
        /// <param name="config">Tuning values.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public HitDetector(HitDetectionConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            _cosTolerance = Mathf.Cos(config.AttackFacingToleranceDeg * Mathf.Deg2Rad);
            // Comparing against -1 is not enough: rounding can leave a reversed diagonal dot just below it.
            _facingDisabled = config.AttackFacingToleranceDeg >= HitDetectionConfig.FACING_CHECK_DISABLED_DEG;
        }

        /// <summary>
        /// Checks range first, then the point-blank guard, then facing.
        /// </summary>
        /// <param name="attackerPos">Attacker world position; only X and Z are read.</param>
        /// <param name="targetPos">Target world position; only X and Z are read.</param>
        /// <param name="attackRange">
        /// Strike radius in world units. Not validated (EC-HD-4); callers check ATTACK_RANGE_MIN. A negative
        /// value acts as its absolute value, and NaN skips the range check.
        /// </param>
        /// <param name="capturedForward">
        /// Attacker forward direction captured at swing time, as a vector (not an angle), so this method
        /// is independent of any facing-angle convention. Precondition: unit length with Y = 0. It is
        /// neither normalized nor validated; a zero vector yields dot = 0 and so MissFacing at any
        /// tolerance below 90 degrees.
        /// </param>
        /// <returns><see cref="HitResult.Hit"/>, <see cref="HitResult.MissOutOfRange"/> or <see cref="HitResult.MissFacing"/>.</returns>
        public HitResult CheckHit(Vector3 attackerPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)
        {
            float dx = targetPos.x - attackerPos.x;
            float dz = targetPos.z - attackerPos.z;
            float dSq = dx * dx + dz * dz;

            if (dSq > attackRange * attackRange)
                return HitResult.MissOutOfRange;

            if (dSq < HitDetectionConfig.POINT_BLANK_EPSILON_SQ)
                return HitResult.Hit;

            if (_facingDisabled)
                return HitResult.Hit;

            float invDist = 1f / Mathf.Sqrt(dSq);
            float dot = capturedForward.x * (dx * invDist) + capturedForward.z * (dz * invDist);
            return dot >= _cosTolerance ? HitResult.Hit : HitResult.MissFacing;
        }
    }
}
