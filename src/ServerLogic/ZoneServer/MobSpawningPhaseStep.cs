using IronGrind.Networking;
using IronGrind.ServerLogic.MobSpawning;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Phase 3 step of Mob Spawning (Mob Spawning Story 006, ADR-014): calls <see cref="MobSpawningService.Tick"/> once
    /// per tick. Built at S5 and listed by the composition before Status Effects and Enemy AI; the spawner is set at S6,
    /// and until then (or when none is built) the step does nothing. Allocation-free.
    /// </summary>
    /// <example>
    /// <code>
    /// var step = new MobSpawningPhaseStep();   // S5: listed in AddPhaseThreeSteps
    /// step.Spawner = spawner;                  // S6
    /// </code>
    /// </example>
    public sealed class MobSpawningPhaseStep : IZoneSimulationStep
    {
        /// <summary>The spawner to tick; null until S6 builds it.</summary>
        public MobSpawningService Spawner { get; set; }

        /// <summary>Ticks the spawner, if any.</summary>
        /// <param name="currentTick">Unused (the spawner counts its own ticks).</param>
        /// <param name="deltaTime">Unused.</param>
        public void Run(uint currentTick, float deltaTime)
        {
            Spawner?.Tick();
        }
    }
}
