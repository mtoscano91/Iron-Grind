using System;
using System.Collections.Generic;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The ordered simulation step of the zone tick (phase 3 of <see cref="ZoneTickPipeline"/>): runs each step of
    /// an explicit list in list order. The composition root owns the list and appends game systems in the order it
    /// lists them (manifest: phase 3 order is listed by the composition root); the list is empty until the game
    /// system stories add theirs. Allocates nothing per tick. Main thread only.
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
                _steps[i].Run(currentTick, deltaTime);
            }
        }
    }
}
