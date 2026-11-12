using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// The movement service Enemy AI uses to steer mobs (navigation-pathfinding.md; ADR-003 Decision 1): six methods,
    /// <see cref="Resume"/> included. Every call is allowed only in tick phase 3 (ADR-002), the AI phase.
    /// </summary>
    /// <example>
    /// <code>
    /// nav.SetSpeed(mob, 3.5f);
    /// nav.SetDestination(mob, playerPosition);   // async: no path on this tick
    /// Vector3 v = nav.GetCurrentVelocity(mob);
    /// </code>
    /// </example>
    public interface INavigationProvider
    {
        /// <summary>
        /// Requests a path to <paramref name="target"/>. Asynchronous: the path is not available on the same tick,
        /// and a path update may be deferred by the per-tick path budget. Tick phase 3 only.
        /// </summary>
        /// <param name="agent">The mob's entity id.</param>
        /// <param name="target">World-space destination.</param>
        void SetDestination(EntityID agent, Vector3 target);

        /// <summary>Sets the agent's movement speed in m/s. Tick phase 3 only.</summary>
        /// <param name="agent">The mob's entity id.</param>
        /// <param name="speed">Speed in m/s.</param>
        void SetSpeed(EntityID agent, float speed);

        /// <summary>Stops the agent in place, keeping its destination. Tick phase 3 only.</summary>
        /// <param name="agent">The mob's entity id.</param>
        void Stop(EntityID agent);

        /// <summary>Resumes a stopped agent toward its destination. Tick phase 3 only.</summary>
        /// <param name="agent">The mob's entity id.</param>
        void Resume(EntityID agent);

        /// <summary>True when the agent's path is stale (target moved or path invalidated). Tick phase 3 only.</summary>
        /// <param name="agent">The mob's entity id.</param>
        /// <returns>Whether the path is stale.</returns>
        bool IsPathStale(EntityID agent);

        /// <summary>The agent's current velocity in m/s. Tick phase 3 only.</summary>
        /// <param name="agent">The mob's entity id.</param>
        /// <returns>The velocity vector.</returns>
        Vector3 GetCurrentVelocity(EntityID agent);
    }
}
