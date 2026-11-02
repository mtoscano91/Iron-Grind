using System;
using System.Collections;
using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// A live, allocation-free read-only view over a zone's player roster and mob roster, used as the
    /// <c>validZoneEntityIds</c> set for <see cref="TargetSlotTracker.ProcessSetTarget"/> so that any valid
    /// entity present in the current zone (player or mob, not only mobs) passes the zone-validity check of
    /// networking-relevance-filter.md RFR-3a (self is rejected earlier by RFR-5). Auto-Attack Combat Story 006
    /// code review.
    /// </summary>
    /// <remarks>
    /// Holds references to the two live lists (the zone pool returns the same reused instance on every
    /// call and refreshes it on allocate/release), so the view always reflects the current roster and
    /// reading it allocates nothing. Not thread-safe; read it on the server tick thread only.
    /// </remarks>
    public sealed class ZoneEntityIdView : IReadOnlyCollection<EntityID>
    {
        private readonly IReadOnlyList<EntityID> _players;
        private readonly IReadOnlyList<EntityID> _mobs;

        /// <summary>Creates a view over the two live lists (networking-relevance-filter.md RFR-3a).</summary>
        /// <param name="players">The zone's live player EntityID list.</param>
        /// <param name="mobs">The zone's live mob EntityID list.</param>
        /// <exception cref="ArgumentNullException">Either list is null.</exception>
        public ZoneEntityIdView(IReadOnlyList<EntityID> players, IReadOnlyList<EntityID> mobs)
        {
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _mobs = mobs ?? throw new ArgumentNullException(nameof(mobs));
        }

        /// <summary>Total number of entities currently in both lists.</summary>
        public int Count => _players.Count + _mobs.Count;

        /// <summary>
        /// True if <paramref name="id"/> is currently in the player or mob list. Non-allocating
        /// (indexed loops; RFR-3a).
        /// </summary>
        /// <param name="id">The EntityID to look for.</param>
        public bool Contains(EntityID id)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] == id)
                {
                    return true;
                }
            }

            for (int i = 0; i < _mobs.Count; i++)
            {
                if (_mobs[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Enumerates players then mobs. Allocates (iterator); hot paths should use <see cref="Contains"/>.</summary>
        public IEnumerator<EntityID> GetEnumerator()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                yield return _players[i];
            }

            for (int i = 0; i < _mobs.Count; i++)
            {
                yield return _mobs[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
