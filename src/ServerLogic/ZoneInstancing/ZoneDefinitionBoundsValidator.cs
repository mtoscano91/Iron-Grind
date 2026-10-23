using System;
using System.Globalization;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Real <see cref="IZoneBoundsValidator"/> (CR-ZI-10, startup S6): checks the walkable bounds of the definition in
    /// a <see cref="ZoneDefinitionSlot"/>. Valid only when every coordinate is finite, its absolute value is at most
    /// <see cref="MAX_WALKABLE_HALF_EXTENT_M"/> (inclusive) and min is not above max on any axis. Main thread only.
    /// </summary>
    /// <remarks>
    /// Failure logs: an empty slot or malformed bounds (NaN, infinity, min above max) log
    /// <c>[ZoneInstancing] ZoneDefinitionLoadFailed</c>; a finite coordinate beyond the limit logs
    /// <c>[ZoneInstancing] ZoneBoundsExceedEncoding</c> naming the axis and value.
    /// </remarks>
    /// <example>
    /// <code>
    /// var validator = new ZoneDefinitionBoundsValidator(slot);
    /// bool ok = validator.Validate(zoneId);
    /// </code>
    /// </example>
    public sealed class ZoneDefinitionBoundsValidator : IZoneBoundsValidator
    {
        /// <summary>
        /// Largest allowed absolute coordinate, metres. Hard constraint from CR-ZI-10 and the position encoding
        /// (CR-NET-7.2), which cannot represent a larger value.
        /// </summary>
        public const float MAX_WALKABLE_HALF_EXTENT_M = 250f;

        private static readonly string[] AXES = { "x", "y", "z" };

        private readonly ZoneDefinitionSlot _slot;

        /// <summary>Creates the validator.</summary>
        /// <param name="slot">The slot the loader fills; must not be null.</param>
        public ZoneDefinitionBoundsValidator(ZoneDefinitionSlot slot)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            _slot = slot;
        }

        /// <inheritdoc/>
        public bool Validate(ZoneID zone)
        {
            ZoneDefinition definition = _slot.Current;
            if (definition == null)
            {
                Debug.LogError("[ZoneInstancing] ZoneDefinitionLoadFailed: no zone definition loaded");
                return false;
            }

            if (AreBoundsValid(definition.WalkableBoundsMin, definition.WalkableBoundsMax, out string failure))
            {
                return true;
            }

            Debug.LogError("[ZoneInstancing] " + failure + " (zone " + zone.ToString() + ")");
            return false;
        }

        /// <summary>Pure bounds check (CR-ZI-10).</summary>
        /// <param name="min">Minimum corner.</param>
        /// <param name="max">Maximum corner.</param>
        /// <param name="failure">Null when valid; otherwise the log text starting with the alert name.</param>
        /// <returns>True when the bounds are valid.</returns>
        public static bool AreBoundsValid(Vector3 min, Vector3 max, out string failure)
        {
            failure = null;
            for (int axis = 0; axis < 3; axis++)
            {
                if (!IsFinite(min[axis]) || !IsFinite(max[axis]))
                {
                    failure = "ZoneDefinitionLoadFailed: non-finite bound on axis " + AXES[axis];
                    return false;
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(min[axis]) > MAX_WALKABLE_HALF_EXTENT_M)
                {
                    failure = Exceeds("min", axis, min[axis]);
                    return false;
                }

                if (Mathf.Abs(max[axis]) > MAX_WALKABLE_HALF_EXTENT_M)
                {
                    failure = Exceeds("max", axis, max[axis]);
                    return false;
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (min[axis] > max[axis])
                {
                    failure = "ZoneDefinitionLoadFailed: min above max on axis " + AXES[axis];
                    return false;
                }
            }

            return true;
        }

        private static string Exceeds(string corner, int axis, float value)
        {
            return "ZoneBoundsExceedEncoding: " + corner + "." + AXES[axis] + " = "
                + value.ToString("R", CultureInfo.InvariantCulture) + " exceeds +/-"
                + MAX_WALKABLE_HALF_EXTENT_M.ToString("R", CultureInfo.InvariantCulture) + " m";
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
