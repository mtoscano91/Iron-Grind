using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The F-MOV-6 encoding guard (movement-system.md F-MOV-6, EC-MOV-20): a server position can be committed only
    /// if its X and Z fit the ×100 short wire encoding, that is |x| and |z| at most
    /// <see cref="WireFixedPointCodec.MaxPositionMeters"/> (327.67). The movement tick checks a candidate before
    /// committing it, so a position never reaches the codec's own clamp-and-log path. Y is not checked (ground-plane
    /// movement; Y comes from the NavMesh). NaN on X or Z fails the check.
    /// </summary>
    /// <example>
    /// <code>
    /// if (!MovementPositionGuard.IsEncodable(candidate))
    ///     candidate = previousPosition; // hold, Story 007
    /// </code>
    /// </example>
    public static class MovementPositionGuard
    {
        /// <summary>True when <paramref name="candidate"/>'s X and Z are inside ±327.67; false otherwise, or for NaN.</summary>
        /// <param name="candidate">The candidate server position.</param>
        /// <returns>Whether the position can be committed and encoded.</returns>
        public static bool IsEncodable(Vector3 candidate)
        {
            // Written as "inside" comparisons so a NaN, for which every comparison is false, fails.
            return Mathf.Abs(candidate.x) <= WireFixedPointCodec.MaxPositionMeters
                && Mathf.Abs(candidate.z) <= WireFixedPointCodec.MaxPositionMeters;
        }
    }
}
