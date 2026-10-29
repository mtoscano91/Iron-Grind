using System;
using IronGrind.CharacterStats;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// EC-MOV-19 guard between the speed seam and <see cref="MovementIntegrator"/>. A speed is valid when it is
    /// finite and inside the Character Stats MovementSpeed clamp range (F-MOV-2, [0.5, 20.0], read from the stat
    /// schema). An invalid read logs one error, then falls back to the caller's last valid speed; with none yet,
    /// the caller must not move the entity this tick. BASE_MOVE_SPEED is deliberately not a fallback.
    /// </summary>
    /// <example>
    /// <code>
    /// if (MovementSpeedResolver.TryResolve(source, id, ref lastValid[slot], out float vEff))
    ///     pos = MovementIntegrator.Integrate(pos, dir, vEff, ServerTickLoop.FIXED_DELTA_TIME);
    /// </code>
    /// </example>
    public static class MovementSpeedResolver
    {
        private const string LOG_PREFIX = "[Movement]";

        /// <summary>Reads and validates the speed for <paramref name="id"/>.</summary>
        /// <param name="source">The speed seam.</param>
        /// <param name="id">The entity.</param>
        /// <param name="lastValidSpeed">Caller-owned last valid speed (0f = none yet). Updated on a valid read.</param>
        /// <param name="vEff">The speed to integrate with; 0f when the method returns false.</param>
        /// <returns>True when <paramref name="vEff"/> is usable (fresh or fallback); false when the caller must skip movement.</returns>
        public static bool TryResolve(IMovementSpeedSource source, EntityID id, ref float lastValidSpeed, out float vEff)
        {
            float speed = 0f;
            bool read = false;
            Exception failure = null;
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
            // "Inside" comparisons so NaN fails; infinities are outside the range.
            if (read && failure == null && speed >= min && speed <= max)
            {
                lastValidSpeed = speed;
                vEff = speed;
                return true;
            }

            string reason = failure != null
                ? "source threw " + failure
                : read ? "value " + speed + " outside [" + min + ", " + max + "]" : "read failed";
            UnityEngine.Debug.LogError(LOG_PREFIX + " Invalid movement speed for " + id + ": " + reason
                + (lastValidSpeed > 0f ? "; using last valid speed " + lastValidSpeed : "; no valid speed yet, skipping movement"));

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
