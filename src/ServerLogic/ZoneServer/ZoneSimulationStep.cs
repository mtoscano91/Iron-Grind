using System;
using System.Collections.Generic;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The ordered simulation step of the zone tick (phase 3 of <see cref="ZoneTickPipeline"/>): runs each step of
    /// an explicit list in list order. The composition root owns the list and appends game systems in the order it
    /// lists them (manifest: phase 3 order is listed by the composition root). Each entry runs in its own
    /// <c>try</c>/<c>catch</c> (Status Effects Story 009): a failed entry logs one error
    /// (<c>[ZoneSimulationStep] EntryFailed: index=N type=TYPE tick=N - ExceptionType: message</c>) and the next
    /// entry still runs the same tick (a null entry fails the same way, logged with an empty type). Allocates nothing
    /// on the success path; the failure message is built only in the catch. Main thread only. The list must not
    /// change while <see cref="Run"/> runs: the composition fills it at S5, before the first tick.
    /// </summary>
    /// <example>
    /// <code>
    /// var steps = new List&lt;IZoneSimulationStep&gt;();
    /// var simulation = new ZoneSimulationStep(steps);
    /// steps.Add(combatStep);            // the composition root, at S5, before the first tick
    /// simulation.Run(currentTick, deltaTime);
    /// </code>
    /// </example>
    public sealed class ZoneSimulationStep : IZoneSimulationStep
    {
        private readonly IReadOnlyList<IZoneSimulationStep> _steps;

        /// <summary>Creates the step over a list the caller keeps filling (it is read on every run).</summary>
        /// <param name="steps">The ordered steps; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="steps"/> is null.</exception>
        public ZoneSimulationStep(IReadOnlyList<IZoneSimulationStep> steps)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }

        /// <summary>Number of steps currently in the list.</summary>
        public int Count => _steps.Count;

        /// <inheritdoc/>
        public void Run(uint currentTick, float deltaTime)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                IZoneSimulationStep step = _steps[i];
                try
                {
                    step.Run(currentTick, deltaTime);
                }
                catch (Exception ex)
                {
                    LogEntryFailed(i, step, currentTick, ex);
                }
            }
        }

        // Called only from a catch block, so the message is built only when an entry failed.
        private static void LogEntryFailed(int index, IZoneSimulationStep step, uint tick, Exception ex)
        {
            try
            {
                Debug.LogError($"[ZoneSimulationStep] EntryFailed: index={index} type={step?.GetType().Name} tick={tick} - {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception)
            {
                // Logging must not break the tick: a throwing Message getter or log handler would otherwise
                // escape Run and skip the later entries.
            }
        }
    }
}
