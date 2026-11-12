using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using UnityEngine;
using UnityEngine.AI;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Checks a zone's spawn points against the NavMesh at zone load (CR-NAV-13, EC-NAV-5, AC-NAV-21). Main thread
    /// only. Computes <see cref="SpawnValidationResult.ShouldAbort"/>; aborting zone initialisation is the caller's job.
    /// </summary>
    /// <example>
    /// <code>
    /// var validator = new SpawnPointValidator(query, config);
    /// SpawnValidationResult r = validator.Validate(zone, points, validPoints);
    /// if (r.ShouldAbort) { /* refuse to serve the zone */ }
    /// </code>
    /// </example>
    public sealed class SpawnPointValidator
    {
        private const string LOG_PREFIX = "[Navigation] ";

        private readonly INavMeshQuery _query;
        private readonly float _sampleRadius;

        // The minimum valid fraction in basis points. A float comparison fails for fractions that float stores
        // slightly high (0.55f is 0.5500000119, so 11 of 20 valid would wrongly abort); integer percent loses
        // fractions such as 0.905. Basis points are exact for any 4-decimal fraction.
        private readonly int _minValidBasisPoints;

        /// <summary>Creates the validator and reads the config values once.</summary>
        /// <param name="query">The NavMesh query seam; not null.</param>
        /// <param name="config">The Navigation config asset; not null.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SpawnPointValidator(INavMeshQuery query, NavigationConfig config)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            NavigationConfigValues values = NavigationConfigValues.FromAsset(config);
            _sampleRadius = values.SpawnSampleRadius;
            _minValidBasisPoints = Mathf.RoundToInt(values.MinValidSpawnFraction * 10000f);
        }

        /// <summary>
        /// Samples every spawn point and appends the valid ones, with their authored positions and in declaration
        /// order, to <paramref name="validOut"/> (cleared first).
        /// </summary>
        /// <param name="zone">The zone, for log messages.</param>
        /// <param name="points">The declared spawn points; not null.</param>
        /// <param name="validOut">Receives the valid points; owned by the caller, not null.</param>
        /// <returns>The counts and the abort flag.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="points"/> or <paramref name="validOut"/> is null.</exception>
        public SpawnValidationResult Validate(
            ZoneID zone, IReadOnlyList<SpawnPointInput> points, List<SpawnPointInput> validOut)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (validOut == null)
            {
                throw new ArgumentNullException(nameof(validOut));
            }

            validOut.Clear();
            int total = points.Count;
            if (total == 0)
            {
                Debug.LogWarning(LOG_PREFIX + "Zone " + zone
                    + " has an empty spawn table — no mobs will spawn. Verify this is intentional.");
                return new SpawnValidationResult(0, 0, false);
            }

            int valid = CountValid(zone, points, validOut);
            bool abort = (long)valid * 10000 < (long)_minValidBasisPoints * total;
            if (abort)
            {
                Debug.LogError(LOG_PREFIX + "CRITICAL: zone " + zone + " has only " + valid + " of " + total
                    + " valid spawn points; the zone cannot serve.");
            }

            return new SpawnValidationResult(valid, total, abort);
        }

        // Samples each point (non-finite positions are invalid and never queried), appends the valid ones to
        // validOut in declaration order, logs one error per invalid point, and returns the valid count.
        private int CountValid(ZoneID zone, IReadOnlyList<SpawnPointInput> points, List<SpawnPointInput> validOut)
        {
            int valid = 0;
            for (int i = 0; i < points.Count; i++)
            {
                SpawnPointInput point = points[i];
                Vector3 p = point.Position;
                bool finite = IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z);
                if (finite && _query.SamplePosition(p, _sampleRadius, NavMesh.AllAreas, out _))
                {
                    validOut.Add(point);
                    valid++;
                }
                else
                {
                    Debug.LogError(LOG_PREFIX + "Spawn point off the NavMesh: zone " + zone + ", spawn point "
                        + point.Id + ", position " + p.ToString("F3"));
                }
            }

            return valid;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
