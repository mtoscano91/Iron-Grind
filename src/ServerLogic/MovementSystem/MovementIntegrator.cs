using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The F-MOV-1 position integrator (movement-system.md CR-MOV-4, F-MOV-1, AC-MOV-7 to AC-MOV-10): one
    /// fixed-tick step along the ground plane. Pure and stateless, with no logging and no allocation.
    /// </summary>
    /// <example>
    /// <code>
    /// Vector3 next = MovementIntegrator.Integrate(oldPos, intentDir, vEff, ServerTickLoop.FIXED_DELTA_TIME);
    /// </code>
    /// </example>
    public static class MovementIntegrator
    {
        /// <summary>
        /// Returns <c>oldPos + normalize(inputDir.xz) * vEff * dt</c> with Y kept at <c>oldPos.y</c>.
        /// </summary>
        /// <param name="oldPos">The committed position at the start of the tick.</param>
        /// <param name="inputDir">Desired direction. Y is ignored; the length need not be 1.</param>
        /// <param name="vEff">Effective speed in meters per second.</param>
        /// <param name="dt">Tick delta in seconds (<c>ServerTickLoop.FIXED_DELTA_TIME</c>).</param>
        /// <returns>The candidate position. If X and Z of <paramref name="inputDir"/> are both exactly 0 the
        /// result is <paramref name="oldPos"/> without normalizing (AC-MOV-7).</returns>
        /// <remarks>
        /// Normalization uses <see cref="Vector3.normalized"/> semantics: a direction with magnitude below
        /// 1e-5 becomes zero, so such a vector passes the exact-zero guard but produces no movement. The wire
        /// never carries one (shorts scaled by 32,767 give a smallest non-zero component of about 3e-5).
        /// A non-finite X or Z (NaN or ±Infinity) returns <paramref name="oldPos"/>: an infinite component would
        /// normalize to NaN and corrupt the position. <paramref name="vEff"/> and <paramref name="dt"/> are trusted
        /// (<see cref="MovementSpeedResolver"/> validates the speed; dt is the fixed tick delta).
        /// </remarks>
        public static Vector3 Integrate(Vector3 oldPos, Vector3 inputDir, float vEff, float dt)
        {
            if (inputDir.x == 0f && inputDir.z == 0f)
                return oldPos;
            if (!float.IsFinite(inputDir.x) || !float.IsFinite(inputDir.z))
                return oldPos;

            Vector3 dir = new Vector3(inputDir.x, 0f, inputDir.z).normalized;
            float step = vEff * dt;
            return new Vector3(oldPos.x + dir.x * step, oldPos.y, oldPos.z + dir.z * step);
        }
    }
}
