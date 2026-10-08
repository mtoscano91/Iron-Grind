using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Sends the local player's respec request to the authority (ADR-012 Decision 7).
    /// Consumer: <c>RespecScreenPresenter</c>.
    /// </summary>
    public interface IRespecRequestSender
    {
        /// <summary>
        /// Requests a respec to <paramref name="newTotals"/>. An implementation may throw if the
        /// request is rejected synchronously (the harness adapter does, because it calls the
        /// service directly); a networked client implementation sends a message and the result
        /// arrives as state. The caller keeps a <c>try</c>/<c>catch</c>.
        /// </summary>
        /// <param name="entityId">The entity to respec.</param>
        /// <param name="newTotals">The requested totals for the four primary stats.</param>
        void RequestRespec(EntityID entityId, IReadOnlyDictionary<StatID, int> newTotals);
    }
}
