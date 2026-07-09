using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// Tracks OnStatChanged fire counts per StatID for use in event-subscription tests.
    /// Story 005 adds <c>Subscribe(CharacterStats stats)</c> to wire this recorder
    /// to the container's OnStatChanged event.
    /// </summary>
    internal sealed class StatEventRecorder
    {
        /// <summary>
        /// Number of times OnStatChanged has fired for each StatID since the last
        /// <see cref="Reset"/> call. Contains entries only for stats that have fired at least once.
        /// </summary>
        public Dictionary<StatID, int> FiredCount { get; } = new Dictionary<StatID, int>();

        /// <summary>Clears all recorded fire counts.</summary>
        public void Reset()
        {
            FiredCount.Clear();
        }

        /// <summary>
        /// Records one OnStatChanged firing for <paramref name="statId"/>.
        /// Called by the event handler wired via <see cref="Subscribe"/>.
        /// </summary>
        public void RecordFire(StatID statId)
        {
            if (FiredCount.TryGetValue(statId, out int count))
                FiredCount[statId] = count + 1;
            else
                FiredCount[statId] = 1;
        }

        // Adapter method: bridges StatChangedHandler(EntityID, StatID) → RecordFire(StatID).
        // Named method (not a lambda) so (IronGrind.CharacterStats.CharacterStats.StatChangedHandler)HandleStatChanged produces
        // reference-equal delegates on the same recorder instance — enabling correct Unsubscribe.
        private void HandleStatChanged(EntityID entityId, StatID statId) => RecordFire(statId);

        /// <summary>
        /// Subscribes this recorder to <paramref name="stats"/>.<c>OnStatChanged</c>.
        /// Each fire increments <see cref="FiredCount"/> for the changed stat.
        /// </summary>
        public void Subscribe(IronGrind.CharacterStats.CharacterStats stats) =>
            stats.Subscribe((IronGrind.CharacterStats.CharacterStats.StatChangedHandler)HandleStatChanged);

        /// <summary>
        /// Removes this recorder's handler from <paramref name="stats"/>.<c>OnStatChanged</c>.
        /// </summary>
        public void Unsubscribe(IronGrind.CharacterStats.CharacterStats stats) =>
            stats.Unsubscribe((IronGrind.CharacterStats.CharacterStats.StatChangedHandler)HandleStatChanged);
    }
}
