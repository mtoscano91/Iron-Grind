using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Simulation step that runs navigation phases 1 and 2 of the tick: sync agent positions to the committed
    /// positions, then drain the path request queue. This is the only place those phases run (ADR-002 Decision 1,
    /// EC-NAV-7); nothing else may call <see cref="ZoneNavigationService.Tick"/> or SyncAgentPositions. Story 010 adds
    /// the step to <c>_simulationSteps</c> before the phase 3 systems.
    /// </summary>
    public sealed class NavigationPhaseStep : IZoneSimulationStep
    {
        private readonly ZoneNavigationService _service;
        private readonly IMobCommittedPositionSource _source;
        private readonly Dictionary<EntityID, Vector3> _buffer;

        /// <summary>Creates the step.</summary>
        /// <param name="service">The zone navigation service.</param>
        /// <param name="source">Where the committed mob positions come from.</param>
        /// <param name="bufferCapacity">Initial buffer capacity; use the agent pool size.</param>
        /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
        public NavigationPhaseStep(
            ZoneNavigationService service,
            IMobCommittedPositionSource source,
            int bufferCapacity = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _buffer = new Dictionary<EntityID, Vector3>(bufferCapacity);
        }

        /// <inheritdoc />
        /// <remarks>
        /// If the source or the sync throws, that tick's drain does not run; the pipeline logs the exception and the
        /// next tick recovers (the queue is untouched).
        /// </remarks>
        public void Run(uint currentTick, float deltaTime)
        {
            _buffer.Clear();
            _source.CopyCommittedPositions(_buffer);
            _service.SyncAgentPositions(_buffer);
            _service.Tick();
        }
    }
}
