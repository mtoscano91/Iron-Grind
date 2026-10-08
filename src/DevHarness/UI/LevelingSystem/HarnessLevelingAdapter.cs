using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// Harness-only adapter (ADR-012 Decision 7, Leveling Story 014): implements the client's
    /// read and request interfaces directly over an in-process <see cref="LevelingService"/>.
    /// A real client uses a mirror and a message sender instead.
    /// </summary>
    internal sealed class HarnessLevelingAdapter : ILocalPlayerLevelingView, IRespecRequestSender
    {
        private readonly LevelingService _levelingService;

        /// <summary>Creates the adapter over <paramref name="levelingService"/>.</summary>
        public HarnessLevelingAdapter(LevelingService levelingService)
        {
            _levelingService = levelingService ?? throw new ArgumentNullException(nameof(levelingService));
        }

        /// <inheritdoc />
        public int GetHeldFreePoints(EntityID entityId) => _levelingService.GetHeldFreePoints(entityId);

        /// <inheritdoc />
        public void RequestRespec(EntityID entityId, IReadOnlyDictionary<StatID, int> newTotals)
            => _levelingService.TryApplyRespec(entityId, newTotals);
    }
}
