using System;
using IronGrind.CharacterStats;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// EC-MOV-19 guard between the speed seam and <see cref="MovementIntegrator"/>. A speed is valid when it is
    /// finite and inside the Character Stats MovementSpeed clamp range (F-MOV-2, [0.5, 20.0], read from the stat
    /// schema). An invalid read falls back to the caller's last valid speed; with none yet, the caller must not
    /// move the entity this tick. BASE_MOVE_SPEED is deliberately not a fallback. The logging overload logs one
    /// error per invalid read; the non-logging overload leaves logging to the caller (MovementService logs once
    /// per valid-to-failing transition).
    /// </summary>
    /// <example>
    /// <code>
    /// if (MovementSpeedResolver.TryResolve(source, id, ref lastValid[slot], out float vEff, out bool readFailed))
    ///     pos = MovementIntegrator.Integrate(pos, dir, vEff, ServerTickLoop.FIXED_DELTA_TIME);
    /// </code>
    /// </example>
    public static class MovementSpeedResolver
    {
        private const string LOG_PREFIX = "[Movement]";

        /// <summary>Why a speed read was invalid (<see cref="FailureReason.None"/> = valid).</summary>
        internal enum FailureReason
        {
            None,
            Threw,
            ReadFailed,
            OutOfRange
        }

        /// <summary>Reads and validates the speed for <paramref name="id"/>, logging one error per invalid read.</summary>
        /// <param name="source">The speed seam.</param>
        /// <param name="id">The entity.</param>
        /// <param name="lastValidSpeed">Caller-owned last valid speed (0f = none yet). Updated on a valid read.</param>
        /// <param name="vEff">The speed to integrate with; 0f when the method returns false.</param>
        /// <returns>True when <paramref name="vEff"/> is usable (fresh or fallback); false when the caller must skip movement.</returns>
        public static bool TryResolve(IMovementSpeedSource source, EntityID id, ref float lastValidSpeed, out float vEff)
        {
            bool usable = ResolveCore(source, id, ref lastValidSpeed, out vEff, out FailureReason reason,
                out float badValue, out Exception failure);
            if (reason == FailureReason.None)
                return usable;

            UnityEngine.Debug.LogError(DescribeFailure(id, reason, badValue, failure, lastValidSpeed));
            return usable;
        }

        /// <summary>
        /// Builds the invalid-speed error text shared by the logging overload and <see cref="MovementService"/>:
        /// the cause (exception, out-of-range value with the valid range, or a failed read) and the fallback taken.
        /// Allocates; call on the failure path only.
        /// </summary>
        internal static string DescribeFailure(EntityID id, FailureReason reason, float badValue, Exception failure,
            float lastValidSpeed)
        {
            float min = StatSchema.GetStatMin(StatID.MovementSpeed);
            float max = StatSchema.GetStatMax(StatID.MovementSpeed);
            string text = reason == FailureReason.Threw
                ? "source threw " + failure
                : reason == FailureReason.OutOfRange ? "value " + badValue + " outside [" + min + ", " + max + "]" : "read failed";
            return LOG_PREFIX + " Invalid movement speed for " + id + ": " + text
                + (lastValidSpeed > 0f ? "; using last valid speed " + lastValidSpeed : "; no valid speed yet, skipping movement");
        }

        /// <summary>
        /// Same validation and fallback as the logging overload, but never logs. The success path allocates nothing.
        /// </summary>
        /// <param name="source">The speed seam.</param>
        /// <param name="id">The entity.</param>
        /// <param name="lastValidSpeed">Caller-owned last valid speed (0f = none yet). Updated on a valid read.</param>
        /// <param name="vEff">The speed to integrate with; 0f when the method returns false.</param>
        /// <param name="readFailed">True when this read was invalid (the result may still be the last valid speed).</param>
        /// <returns>True when <paramref name="vEff"/> is usable (fresh or fallback); false when the caller must skip movement.</returns>
        public static bool TryResolve(IMovementSpeedSource source, EntityID id, ref float lastValidSpeed, out float vEff, out bool readFailed)
        {
            bool usable = ResolveCore(source, id, ref lastValidSpeed, out vEff, out FailureReason reason, out _, out _);
            readFailed = reason != FailureReason.None;
            return usable;
        }

        /// <summary>
        /// Validation and fallback without logging, also returning the failure cause (for callers that log on their
        /// own schedule, such as <see cref="MovementService"/>). Allocates nothing on the success path.
        /// </summary>
        internal static bool ResolveCore(IMovementSpeedSource source, EntityID id, ref float lastValidSpeed,
            out float vEff, out FailureReason reason, out float badValue, out Exception failure)
        {
            float speed = 0f;
            bool read = false;
            failure = null;
            try
            {
                read = source.TryGetMovementSpeed(id, out speed);
            }
            catch (Exception e)
            {
                failure = e;
            }

            float min = StatSchema.GetStatMin(StatID.MovementSpeed);
            float max = StatSchema.GetStatMax(StatID.MovementSpeed);
            badValue = speed;
            // "Inside" comparisons so NaN fails; infinities are outside the range.
            if (read && failure == null && speed >= min && speed <= max)
            {
                lastValidSpeed = speed;
                vEff = speed;
                reason = FailureReason.None;
                return true;
            }

            reason = failure != null ? FailureReason.Threw : read ? FailureReason.OutOfRange : FailureReason.ReadFailed;
            if (lastValidSpeed > 0f)
            {
                vEff = lastValidSpeed;
                return true;
            }

            vEff = 0f;
            return false;
        }
    }
}
