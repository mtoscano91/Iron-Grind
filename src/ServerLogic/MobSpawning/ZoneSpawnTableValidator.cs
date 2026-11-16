using System.Collections.Generic;
using System.Globalization;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// Pure T-1 validation of a <see cref="ZoneSpawnTable"/> (CR-MS-2, AC-MS-1, OQ-MS-1). Any invalid entry fails the
    /// whole table; every failing entry is logged with its SpawnPointID and reason before returning false. Load time
    /// only (allocates a small id set per call).
    /// </summary>
    /// <example>
    /// <code>
    /// bool ok = ZoneSpawnTableValidator.Validate(definition.SpawnTable, definition.TemplateId,
    ///     definition.WalkableBoundsMin, definition.WalkableBoundsMax, registry);
    /// </code>
    /// </example>
    public static class ZoneSpawnTableValidator
    {
        /// <summary>Log key for a table with more than MAX_MOBS_PER_ZONE points.</summary>
        public const string OVER_CAPACITY_KEY = "SpawnTableOverCapacity";

        /// <summary>Log key for an entry with a respawn delay override above zero (OQ-MS-1).</summary>
        public const string BOSS_NOT_SUPPORTED_KEY = "BossSpawnPointNotSupported";

        private const string LOG_PREFIX = "[ZoneSpawnTableValidator] ";

        /// <summary>
        /// Validates a table asset: its template must match <paramref name="expectedTemplate"/>, then every entry is
        /// checked as in <see cref="ValidateEntries"/>. A template mismatch is logged and the entries are still checked.
        /// </summary>
        /// <param name="table">The table; null logs an error and fails.</param>
        /// <param name="expectedTemplate">The zone's template id.</param>
        /// <param name="boundsMin">Walkable bounds minimum (inclusive).</param>
        /// <param name="boundsMax">Walkable bounds maximum (inclusive).</param>
        /// <param name="registry">Mob definition lookup.</param>
        /// <returns>True when the template matches and every entry is valid.</returns>
        public static bool Validate(ZoneSpawnTable table, ZoneTemplateID expectedTemplate, Vector3 boundsMin,
            Vector3 boundsMax, IMobDefinitionRegistry registry)
        {
            if (table == null)
            {
                Debug.LogError(LOG_PREFIX + "spawn table is null.");
                return false;
            }

            bool ok = true;
            if (table.TemplateId != expectedTemplate)
            {
                Debug.LogError(LOG_PREFIX + "table ZoneTemplateID " + table.TemplateId + " differs from the zone's "
                    + expectedTemplate + ".");
                ok = false;
            }

            return ValidateEntries(table.Entries, boundsMin, boundsMax, registry) && ok;
        }

        /// <summary>
        /// Validates the entries only (no template check): cap, duplicate ids, mob type, finite position inside the
        /// inclusive bounds, override. Over the cap fails at once without per-entry checks; otherwise every entry is
        /// checked and every failure logged.
        /// </summary>
        /// <param name="entries">The spawn points; null logs an error and fails.</param>
        /// <param name="boundsMin">Walkable bounds minimum (inclusive, all 3 axes).</param>
        /// <param name="boundsMax">Walkable bounds maximum (inclusive, all 3 axes).</param>
        /// <param name="registry">Mob definition lookup; null logs one error and fails.</param>
        /// <returns>True when every entry is valid; an empty list is valid with one warning.</returns>
        public static bool ValidateEntries(IReadOnlyList<SpawnPointEntry> entries, Vector3 boundsMin,
            Vector3 boundsMax, IMobDefinitionRegistry registry)
        {
            if (!CheckInputs(entries, boundsMin, boundsMax, registry))
            {
                return false;
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning(LOG_PREFIX + "spawn table is empty; the zone will have no mobs.");
                return true;
            }

            bool ok = true;
            var seen = new HashSet<uint>();
            for (int i = 0; i < entries.Count; i++)
            {
                ok &= ValidateEntry(entries[i], seen, boundsMin, boundsMax, registry);
            }

            return ok;
        }

        // Null list, null registry, unusable bounds, over the cap: each fails before any entry is checked.
        private static bool CheckInputs(IReadOnlyList<SpawnPointEntry> entries, Vector3 boundsMin, Vector3 boundsMax,
            IMobDefinitionRegistry registry)
        {
            if (entries == null)
            {
                Debug.LogError(LOG_PREFIX + "entries is null.");
                return false;
            }

            if (registry == null)
            {
                Debug.LogError(LOG_PREFIX + "mob definition registry is null.");
                return false;
            }

            if (!IsFinite(boundsMin) || !IsFinite(boundsMax)
                || boundsMin.x > boundsMax.x || boundsMin.y > boundsMax.y || boundsMin.z > boundsMax.z)
            {
                Debug.LogError(LOG_PREFIX + "walkable bounds " + boundsMin.ToString("F2") + " .. "
                    + boundsMax.ToString("F2") + " are not finite or not ordered.");
                return false;
            }

            if (entries.Count > ZoneSlotPool.MAX_MOBS_PER_ZONE)
            {
                Debug.LogError(LOG_PREFIX + OVER_CAPACITY_KEY + ": " + entries.Count.ToString(CultureInfo.InvariantCulture)
                    + " spawn points exceeds MAX_MOBS_PER_ZONE "
                    + ZoneSlotPool.MAX_MOBS_PER_ZONE.ToString(CultureInfo.InvariantCulture) + ".");
                return false;
            }

            return true;
        }

        // Every check runs (no early return) so that each fault of the entry is logged.
        private static bool ValidateEntry(in SpawnPointEntry e, HashSet<uint> seen, Vector3 boundsMin,
            Vector3 boundsMax, IMobDefinitionRegistry registry)
        {
            string id = "SpawnPointID " + e.SpawnPointID.ToString(CultureInfo.InvariantCulture) + ": ";
            bool ok = true;

            if (!seen.Add(e.SpawnPointID))
            {
                Debug.LogError(LOG_PREFIX + id + "duplicate SpawnPointID.");
                ok = false;
            }

            if (registry.GetDefinition(e.MobTypeID) == null)
            {
                Debug.LogError(LOG_PREFIX + id + "unknown " + e.MobTypeID + ".");
                ok = false;
            }

            ok &= CheckPosition(id, e.Position, boundsMin, boundsMax);
            ok &= CheckOverride(id, e);
            return ok;
        }

        private static bool CheckPosition(string id, Vector3 p, Vector3 boundsMin, Vector3 boundsMax)
        {
            if (!IsFinite(p))
            {
                Debug.LogError(LOG_PREFIX + id + "position is NaN or infinite.");
                return false;
            }

            if (p.x < boundsMin.x || p.y < boundsMin.y || p.z < boundsMin.z
                || p.x > boundsMax.x || p.y > boundsMax.y || p.z > boundsMax.z)
            {
                Debug.LogError(LOG_PREFIX + id + "position " + p.ToString("F2") + " is outside the walkable bounds.");
                return false;
            }

            return true;
        }

        private static bool CheckOverride(string id, in SpawnPointEntry e)
        {
            if (e.RespawnDelayOverrideTicks < 0)
            {
                Debug.LogError(LOG_PREFIX + id + "negative RespawnDelayOverrideTicks "
                    + e.RespawnDelayOverrideTicks.ToString(CultureInfo.InvariantCulture) + ".");
                return false;
            }

            if (e.RespawnDelayOverrideTicks > 0)
            {
                Debug.LogError(LOG_PREFIX + BOSS_NOT_SUPPORTED_KEY + ": SpawnPointID "
                    + e.SpawnPointID.ToString(CultureInfo.InvariantCulture) + " has RespawnDelayOverrideTicks "
                    + e.RespawnDelayOverrideTicks.ToString(CultureInfo.InvariantCulture) + " (OQ-MS-1).");
                return false;
            }

            return true;
        }

        private static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }
    }
}
