using System;
using IronGrind.Networking;
using IronGrind.ServerLogic.DeathAndRespawn;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Phase 3 step of Death and Respawn (Death and Respawn Story 008, ADR-014): runs the respawn countdown once per
    /// tick. Listed after Status Effects and before Movement, so a player respawned on tick T is teleported and free
    /// to move on T, and targetable by Enemy AI and Auto-Attack on T. Allocation-free.
    /// </summary>
    /// <example>
    /// <code>
    /// var step = new DeathAndRespawnPhaseStep(service);   // listed in AddPhaseThreeSteps
    /// </code>
    /// </example>
    public sealed class DeathAndRespawnPhaseStep : IZoneSimulationStep
    {
        /// <summary>Creates the step.</summary>
        /// <param name="service">The service to tick.</param>
        /// <exception cref="ArgumentNullException"><paramref name="service"/> is null.</exception>
        public DeathAndRespawnPhaseStep(DeathAndRespawnService service)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>The service to tick.</summary>
        public DeathAndRespawnService Service { get; }

        /// <summary>Runs the countdown for <paramref name="currentTick"/>.</summary>
        /// <param name="currentTick">The server tick number of this tick.</param>
        /// <param name="deltaTime">Unused.</param>
        public void Run(uint currentTick, float deltaTime)
        {
            Service.Tick((long)currentTick);
        }
    }
}
