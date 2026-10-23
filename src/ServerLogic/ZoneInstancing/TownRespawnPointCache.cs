using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// <see cref="ITownRespawnPointProvider"/> for one zone process (CR-ZI-11, AC-ZI-9, AC-ZI-10, AC-ZI-18).
    /// The constructor resolves the respawn point once: the authored point when it is valid; otherwise the centroid
    /// of valid walkable bounds (logs one <c>TownRespawnPointNotAuthored</c> warning); otherwise
    /// <see cref="Vector3.zero"/> (logs one <c>TownRespawnPointMissing</c> critical alert). Later calls for the own
    /// zone log nothing and allocate nothing.
    /// </summary>
    /// <remarks>
    /// An authored point is valid when every coordinate is finite and its absolute value is at most
    /// <see cref="ZoneDefinitionBoundsValidator.MAX_WALKABLE_HALF_EXTENT_M"/> (the position encoding limit,
    /// CR-NET-7.2); an invalid one is treated as not authored and the warning names the failing axis.
    /// The cache is keyed on the template and copies the values out of the definition, so it keeps answering after
    /// the zone closes or the asset is destroyed. A <see cref="ZoneID"/> other than the own one is a caller error: it
    /// returns <see cref="Vector3.zero"/> and logs a <c>TownRespawnPointMissing</c> alert on every call.
    /// Main thread only.
    /// </remarks>
    /// <example>
    /// <code>
    /// var cache = new TownRespawnPointCache(new ZoneID(1u), definition);
    /// Vector3 point = cache.GetTownRespawnPoint(new ZoneID(1u));
    /// </code>
    /// </example>
    public sealed class TownRespawnPointCache : ITownRespawnPointProvider
    {
        private const string LOG_PREFIX = "[ZoneInstancing]";
        private static readonly string[] AXES = { "x", "y", "z" };

        private readonly ZoneID _ownZone;
        private readonly ZoneTemplateID _template;

        // One entry in a zone process; the template-keyed dictionary is the CR-ZI-11 contract (Story 005 AC-1).
        private readonly Dictionary<ZoneTemplateID, Vector3> _points;

        /// <summary>Resolves and caches the respawn point of the loaded zone definition.</summary>
        /// <param name="ownZone">The <see cref="ZoneID"/> of this process; must not be <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="definition">The loaded definition; must not be null or destroyed.</param>
        /// <exception cref="ArgumentException"><paramref name="ownZone"/> is <see cref="ZoneID.Invalid"/>.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null or destroyed.</exception>
        public TownRespawnPointCache(ZoneID ownZone, ZoneDefinition definition)
        {
            if (ownZone == ZoneID.Invalid)
            {
                throw new ArgumentException("The own zone must not be ZoneID.Invalid.", nameof(ownZone));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            _ownZone = ownZone;
            _template = definition.TemplateId;
            _points = new Dictionary<ZoneTemplateID, Vector3>(1, new TemplateComparer());
            _points[_template] = Resolve(definition);
        }

        /// <summary>Returns the cached respawn point for the own zone.</summary>
        /// <param name="zone">The zone asked about; only the process's own zone is known.</param>
        /// <returns>
        /// The resolved point for the own zone; <see cref="Vector3.zero"/> (plus a critical alert on every call)
        /// for any other zone. Never throws.
        /// </returns>
        /// <example>
        /// <code>
        /// Vector3 point = cache.GetTownRespawnPoint(ownZone);
        /// </code>
        /// </example>
        public Vector3 GetTownRespawnPoint(ZoneID zone)
        {
            if (zone == _ownZone)
            {
                // The constructor always stores the own template's entry.
                return _points[_template];
            }

            Debug.LogError(LOG_PREFIX + " TownRespawnPointMissing: foreign zone " + zone.ToString()
                + " asked for a respawn point; this process serves " + _ownZone.ToString()
                + " (template " + _template.ToString() + ")");
            return Vector3.zero;
        }

        private Vector3 Resolve(ZoneDefinition definition)
        {
            string authoredFailure = null;
            if (definition.TownRespawnPointAuthored)
            {
                authoredFailure = CheckPoint(definition.TownRespawnPoint);
                if (authoredFailure == null)
                {
                    return definition.TownRespawnPoint;
                }
            }

            string reason = authoredFailure == null
                ? "has no authored respawn point"
                : "has an invalid authored respawn point (" + authoredFailure + ")";

            Vector3 min = definition.WalkableBoundsMin;
            Vector3 max = definition.WalkableBoundsMax;
            if (ZoneDefinitionBoundsValidator.AreBoundsValid(min, max, out string boundsFailure))
            {
                Vector3 centroid = (min + max) * 0.5f;
                Debug.LogWarning(LOG_PREFIX + " TownRespawnPointNotAuthored: template " + _template.ToString()
                    + " " + reason + "; using the bounds centroid " + Format(centroid.x) + ", "
                    + Format(centroid.y) + ", " + Format(centroid.z));
                return centroid;
            }

            Debug.LogError(LOG_PREFIX + " TownRespawnPointMissing: template " + _template.ToString()
                + " " + reason + " and no valid bounds (" + boundsFailure + "); returning zero");
            return Vector3.zero;
        }

        // Null when every coordinate is finite and within the encoding limit; otherwise the failing axis.
        private static string CheckPoint(Vector3 point)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                float value = point[axis];
                if (float.IsNaN(value) || float.IsInfinity(value)
                    || Mathf.Abs(value) > ZoneDefinitionBoundsValidator.MAX_WALKABLE_HALF_EXTENT_M)
                {
                    return "axis " + AXES[axis] + " = " + Format(value);
                }
            }

            return null;
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Compares the enum values directly so lookups never box under Mono.</summary>
        private sealed class TemplateComparer : IEqualityComparer<ZoneTemplateID>
        {
            public bool Equals(ZoneTemplateID x, ZoneTemplateID y)
            {
                return x == y;
            }

            public int GetHashCode(ZoneTemplateID obj)
            {
                return (int)obj;
            }
        }
    }
}
