using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// <see cref="ITownRespawnPointProvider"/> for one zone process (CR-ZI-11, AC-ZI-9, AC-ZI-10, AC-ZI-18).
    /// The constructor resolves the respawn point once: the authored point; otherwise the centroid of valid walkable
    /// bounds (logs one <c>TownRespawnPointNotAuthored</c> warning); otherwise <see cref="Vector3.zero"/> (logs one
    /// <c>TownRespawnPointMissing</c> critical alert). Later calls for the own zone log nothing and allocate nothing.
    /// </summary>
    /// <remarks>
    /// The cache is keyed on the template and holds no lifecycle reference, so it keeps answering after the zone
    /// closes. A <see cref="ZoneID"/> other than the own one is a caller error: it returns
    /// <see cref="Vector3.zero"/> and logs a <c>TownRespawnPointMissing</c> alert on every call. Main thread only.
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

        private readonly ZoneID _ownZone;
        private readonly ZoneTemplateID _template;
        private readonly Dictionary<ZoneTemplateID, Vector3> _points;

        /// <summary>Resolves and caches the respawn point of the loaded zone definition.</summary>
        /// <param name="ownZone">The <see cref="ZoneID"/> of this process.</param>
        /// <param name="definition">The loaded definition; must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public TownRespawnPointCache(ZoneID ownZone, ZoneDefinition definition)
        {
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
            if (zone.Equals(_ownZone) && _points.TryGetValue(_template, out Vector3 point))
            {
                return point;
            }

            Debug.LogError(LOG_PREFIX + " TownRespawnPointMissing: foreign zone " + zone.ToString()
                + " asked for a respawn point; this process serves " + _ownZone.ToString()
                + " (template " + _template.ToString() + ")");
            return Vector3.zero;
        }

        private Vector3 Resolve(ZoneDefinition definition)
        {
            if (definition.TownRespawnPointAuthored)
            {
                return definition.TownRespawnPoint;
            }

            Vector3 min = definition.WalkableBoundsMin;
            Vector3 max = definition.WalkableBoundsMax;
            if (ZoneDefinitionBoundsValidator.AreBoundsValid(min, max, out string failure))
            {
                Vector3 centroid = (min + max) * 0.5f;
                Debug.LogWarning(LOG_PREFIX + " TownRespawnPointNotAuthored: template " + _template.ToString()
                    + " has no authored respawn point; using the bounds centroid "
                    + centroid.x.ToString("R", CultureInfo.InvariantCulture) + ", "
                    + centroid.y.ToString("R", CultureInfo.InvariantCulture) + ", "
                    + centroid.z.ToString("R", CultureInfo.InvariantCulture));
                return centroid;
            }

            Debug.LogError(LOG_PREFIX + " TownRespawnPointMissing: template " + _template.ToString()
                + " has no authored respawn point and no valid bounds (" + failure + "); returning zero");
            return Vector3.zero;
        }

        /// <summary>Compares the byte values so lookups never box the enum under Mono.</summary>
        private sealed class TemplateComparer : IEqualityComparer<ZoneTemplateID>
        {
            public bool Equals(ZoneTemplateID x, ZoneTemplateID y)
            {
                return (byte)x == (byte)y;
            }

            public int GetHashCode(ZoneTemplateID obj)
            {
                return (byte)obj;
            }
        }
    }
}
