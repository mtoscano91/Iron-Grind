using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// Harness-only adapter (ADR-012 Decision 7, Character Stats Story 009): implements the
    /// client's <see cref="ILocalPlayerStatsView"/> directly over an in-process stats object.
    /// A real client uses a mirror filled from state messages instead.
    /// </summary>
    internal sealed class HarnessStatsAdapter : ILocalPlayerStatsView
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        private readonly Dictionary<LocalPlayerStatChangedHandler, IronGrind.CharacterStats.CharacterStats.StatChangedHandler> _wrappers =
            new Dictionary<LocalPlayerStatChangedHandler, IronGrind.CharacterStats.CharacterStats.StatChangedHandler>();

        /// <summary>Creates the adapter over <paramref name="stats"/>.</summary>
        public HarnessStatsAdapter(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public int GetBaseStat(EntityID entityId, StatID statId) => _stats.GetBaseStat(entityId, statId);

        /// <inheritdoc />
        public void Subscribe(LocalPlayerStatChangedHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_wrappers.ContainsKey(handler))
                return;

            var wrapper = new IronGrind.CharacterStats.CharacterStats.StatChangedHandler(handler);
            _wrappers.Add(handler, wrapper);
            _stats.Subscribe(wrapper);
        }

        /// <inheritdoc />
        public void Unsubscribe(LocalPlayerStatChangedHandler handler)
        {
            if (handler == null)
                return;
            if (!_wrappers.TryGetValue(handler, out var wrapper))
                return;

            _stats.Unsubscribe(wrapper);
            _wrappers.Remove(handler);
        }
    }
}
