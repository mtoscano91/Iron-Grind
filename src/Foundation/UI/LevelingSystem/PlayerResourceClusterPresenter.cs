using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using IronGrind.CharacterStats;

namespace IronGrind.UI.LevelingSystem
{
    /// <summary>
    /// AC-LS-47: drives the HUD Level Badge (Row 1) and XP Bar (Row 5) — the Player Resource
    /// Cluster's minimal Story 013 scope. HP/MP/charge/gold bars are reserved placeholder
    /// slots owned by other, not-yet-built stories (see HUD_Root.uxml / USS_HUD_Theme.uss).
    /// </summary>
    /// <remarks>
    /// <para><b>Fill pattern (ADR-005).</b> <c>xpBarFill</c>'s fill is driven exclusively via
    /// <c>style.scale</c> on the X axis (<see cref="UsageHints.DynamicTransform"/> set once at
    /// construction) — never <c>style.width</c> percentage. USS supplies
    /// <c>transform-origin: left center</c>.</para>
    /// <para><b>xpThresholds is caller-supplied, not read from <see cref="LevelingSystem.LevelingService"/>.</b>
    /// <see cref="LevelingSystem.LevelingService.GetExperienceThreshold"/> only exposes
    /// <c>XpThreshold[CurrentLevel + 1]</c> (the threshold to the NEXT level); the AC-LS-47 fill
    /// formula also needs <c>XpThreshold[CurrentLevel]</c> (the lower bound), which has no
    /// public accessor — <c>LevelingService</c>'s own <c>_xpThresholds</c> field is private, and
    /// widening it was out of scope ("only consumes existing APIs"). This presenter instead
    /// takes the SAME <see cref="IReadOnlyList{T}"/>&lt;int&gt; table the caller already passed
    /// into <c>new LevelingService(xpThresholds, ...)</c> and indexes it directly — the same
    /// "caller-supplied state for a forward dependency" idiom <c>IClassRegistry</c> already
    /// uses in this codebase.</para>
    /// <para><b>No explicit batching needed for the badge text.</b> During a Story 003 CR-2.9
    /// consecutive level-up, <c>CharacterStats.SetBaseStat(Level, ...)</c> fires
    /// <c>OnStatChanged</c> once per intermediate level, synchronously, all within the same C#
    /// call stack, before Unity's next repaint. <see cref="Render"/> re-renders
    /// <c>_levelBadgeLabel.text</c> on every firing — safe, because UI Toolkit only repaints
    /// the LAST property value written before the next frame; no visible flicker occurs. This
    /// is why the level-up overlay's own animation (<see cref="LevelUpOverlayPresenter"/>)
    /// needs explicit next-frame batching and this class does not — the overlay schedules a
    /// visible, time-extended effect per <c>OnLevelUp</c> firing, which WOULD play multiple
    /// times without batching.</para>
    /// </remarks>
    public sealed class PlayerResourceClusterPresenter : IDisposable
    {
        private const int MaxLevel = 60;

        private readonly VisualElement _xpBarFill;
        private readonly Label _levelBadgeLabel;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly IReadOnlyList<int> _xpThresholds;
        private readonly EntityID _entityId;

        private bool _disposed;

        public PlayerResourceClusterPresenter(
            VisualElement xpBarFill,
            Label levelBadgeLabel,
            IronGrind.CharacterStats.CharacterStats stats,
            IReadOnlyList<int> xpThresholds,
            EntityID entityId)
        {
            _xpBarFill = xpBarFill ?? throw new ArgumentNullException(nameof(xpBarFill));
            _levelBadgeLabel = levelBadgeLabel ?? throw new ArgumentNullException(nameof(levelBadgeLabel));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _xpThresholds = xpThresholds ?? throw new ArgumentNullException(nameof(xpThresholds));
            _entityId = entityId;

            // ADR-005 — set once at init, allows GPU-side transform update without tessellation.
            _xpBarFill.usageHints = UsageHints.DynamicTransform;

            _stats.Subscribe(OnStatChanged);

            Render(); // Reflect current state immediately, not just on the next change.
        }

        /// <summary>
        /// Re-renders the level badge and XP bar from current <see cref="CharacterStats"/>
        /// values. Public so <see cref="LevelUpOverlayPresenter"/> can force a refresh after its
        /// own transient XP-bar-flash effect (AC-LS-46's "flashes-white-then-resets" step)
        /// without duplicating the fill formula a second time.
        /// </summary>
        public void Render()
        {
            int level = _stats.GetBaseStat(_entityId, StatID.Level);

            if (level >= MaxLevel)
            {
                // AC-LS-47 L60 guard — bypass the formula entirely. After the CR-2.2a clamp
                // (Story 004/008), evaluating the formula here would produce a near-empty-bar
                // artifact. fill=1.0 constant; badge shows "MAX"; no XP text (this HUD never
                // shows XP numerics per hud.md Row 5 spec regardless of level).
                SetFill(1f);
                _levelBadgeLabel.text = "MAX";
                return;
            }

            _levelBadgeLabel.text = $"Lv.{level}";

            if (level < 0 || level + 1 >= _xpThresholds.Count)
            {
                // Defensive — should not happen for a valid [1,59] level; avoid IndexOutOfRange
                // rather than throw from a presenter.
                SetFill(0f);
                return;
            }

            int experience = _stats.GetBaseStat(_entityId, StatID.Experience);
            int thresholdAtLevel = _xpThresholds[level];
            int thresholdNextLevel = _xpThresholds[level + 1];
            int denominator = thresholdNextLevel - thresholdAtLevel;

            // AC-LS-47 — both operands cast to float before division.
            float fill = denominator == 0
                ? 0f
                : (float)(experience - thresholdAtLevel) / (float)denominator;

            SetFill(Mathf.Clamp01(fill)); // Defensive clamp — not spec-mandated, just avoids visual overflow.
        }

        private void SetFill(float fill01)
        {
            _xpBarFill.style.scale = new StyleScale(new Scale(new Vector3(fill01, 1f, 1f)));
        }

        private void OnStatChanged(EntityID entityId, StatID statId)
        {
            if (entityId != _entityId) return;
            if (statId != StatID.Level && statId != StatID.Experience) return;
            Render();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stats.Unsubscribe(OnStatChanged);
        }
    }
}
