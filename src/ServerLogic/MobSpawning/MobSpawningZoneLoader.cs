using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// Zone-load step for Mob Spawning (T-1; CR-MS-2, AC-MS-1, CR-NAV-13, AC-NAV-21). Reads the zone's
    /// <see cref="ZoneDefinition.SpawnTable"/>, validates it (<see cref="ZoneSpawnTableValidator"/>; the 150 cap fails
    /// before any NavMesh query), checks the points against the NavMesh (<see cref="SpawnPointValidator"/>) and builds
    /// a <see cref="MobSpawningService"/> from the valid points only, in authored order. Never calls the roster.
    /// </summary>
    /// <remarks>
    /// Precondition: Navigation's <c>Initialize</c> has run for the zone (ADR-002, main thread). On failure the caller
    /// must not mark the zone Active. The loader logs only a missing definition; every other failure is logged by the
    /// validators. Load time only (allocates the point lists once per zone start).
    /// </remarks>
    /// <example>
    /// <code>
    /// if (!MobSpawningZoneLoader.TryLoad(zone, slot.Current, registry, validator, roster, bus, config,
    ///         deathLingerTicks, out MobSpawningService spawner))
    /// {
    ///     return false; // zone initialisation fails
    /// }
    /// </code>
    /// </example>
    public static class MobSpawningZoneLoader
    {
        private const string LOG_PREFIX = "[MobSpawning] ";

        /// <summary>Log text for a zone with no <see cref="ZoneDefinition"/>.</summary>
        internal const string NO_DEFINITION_ERROR = LOG_PREFIX + "no zone definition for zone ";

        /// <summary>
        /// Validates the zone's spawn table and builds its spawner from the points that pass NavMesh validation.
        /// </summary>
        /// <param name="zone">The zone instance.</param>
        /// <param name="definition">The zone's definition; null logs one error and fails.</param>
        /// <param name="registry">Mob definition lookup for table validation; null fails (logged by the table validator).</param>
        /// <param name="validator">The NavMesh spawn point validator; not null.</param>
        /// <param name="roster">The zone's mob roster, passed to the service; not null.</param>
        /// <param name="bus">The zone's mob event bus, passed to the service; not null.</param>
        /// <param name="config">Mob Spawning config values.</param>
        /// <param name="deathLingerTicks">Enemy AI death linger, ticks (&gt; 0).</param>
        /// <param name="service">The built spawner; null on failure.</param>
        /// <returns>
        /// True when the service was built; false when zone initialisation must fail (null definition, null table,
        /// null <paramref name="registry"/>, any table validation failure, or a NavMesh abort; each logged once).
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="validator"/>, <paramref name="roster"/> or
        /// <paramref name="bus"/> is null (checked before anything else).</exception>
        /// <exception cref="ArgumentException">From the <see cref="MobSpawningService"/> constructor, after the NavMesh
        /// validation: <paramref name="zone"/> is <see cref="ZoneID.Invalid"/> or <paramref name="config"/> fails its own
        /// validation. These are programmer or config errors, so they fail fast instead of returning false.</exception>
        /// <exception cref="ArgumentOutOfRangeException">From the <see cref="MobSpawningService"/> constructor:
        /// <paramref name="deathLingerTicks"/> is not above 0.</exception>
        public static bool TryLoad(ZoneID zone, ZoneDefinition definition, IMobDefinitionRegistry registry,
            SpawnPointValidator validator, IZoneMobRoster roster, MobEventBus bus, MobSpawningConfigValues config,
            int deathLingerTicks, out MobSpawningService service)
        {
            service = null;
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            if (roster == null)
            {
                throw new ArgumentNullException(nameof(roster));
            }

            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            if (definition == null)
            {
                Debug.LogError(NO_DEFINITION_ERROR + zone + ".");
                return false;
            }

            ZoneSpawnTable table = definition.SpawnTable;
            if (!ZoneSpawnTableValidator.Validate(table, definition.TemplateId, definition.WalkableBoundsMin,
                    definition.WalkableBoundsMax, registry))
            {
                return false;
            }

            IReadOnlyList<SpawnPointEntry> entries = table.Entries;
            var inputs = new List<SpawnPointInput>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                inputs.Add(new SpawnPointInput(entries[i].SpawnPointID, entries[i].Position));
            }

            var validOut = new List<SpawnPointInput>(entries.Count);
            SpawnValidationResult result = validator.Validate(zone, inputs, validOut);
            if (result.ShouldAbort)
            {
                return false;
            }

            service = new MobSpawningService(zone, FilterValid(entries, validOut), roster, bus, registry, config,
                deathLingerTicks);
            return true;
        }

        // The authored entries whose SpawnPointID is in validOut, in authored order (IDs are unique per Story 001).
        private static List<SpawnPointEntry> FilterValid(IReadOnlyList<SpawnPointEntry> entries,
            List<SpawnPointInput> validOut)
        {
            var validIds = new HashSet<uint>();
            for (int i = 0; i < validOut.Count; i++)
            {
                validIds.Add(validOut[i].Id);
            }

            var kept = new List<SpawnPointEntry>(validOut.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (validIds.Contains(entries[i].SpawnPointID))
                {
                    kept.Add(entries[i]);
                }
            }

            return kept;
        }
    }
}
