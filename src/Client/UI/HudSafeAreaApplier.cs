using UnityEngine;
using UnityEngine.UIElements;

namespace IronGrind.UI
{
    /// <summary>
    /// Shared HUD infrastructure (ADR-005 "Safe area inset" pattern) -- converts
    /// <see cref="Screen.safeArea"/> physical pixels into panel logical units via
    /// <see cref="RuntimePanelUtils.ScreenToPanel"/> and applies them as margins on a HUD root
    /// element, plus the fixed 8dp interior margin (CR-HUD-17). Deliberately placed one
    /// namespace level above <c>IronGrind.UI.LevelingSystem</c> (parent <c>IronGrind.UI</c>)
    /// because every future HUD story (HP/MP bars, minimap, party frames, etc.) will need the
    /// exact same call -- this is cross-cutting HUD infrastructure, not Leveling-System-specific
    /// logic, even though Story 013 is the first to need and therefore write it.
    /// </summary>
    /// <remarks>
    /// Y-axis flip is required: <see cref="Screen.safeArea"/> uses bottom-left origin;
    /// UI Toolkit panel space uses top-left (ADR-005). Call once at startup
    /// (<c>Start</c>/<c>OnEnable</c>) -- this is a landscape-only project (Technical
    /// Preferences), so safe area changes mid-session are not expected, but the call is cheap
    /// enough to re-invoke on a dimension-change callback if a future story needs that.
    /// </remarks>
    public static class HudSafeAreaApplier
    {
        private const float InteriorMarginDp = 8f; // CR-HUD-17

        /// <summary>
        /// Applies the current <see cref="Screen.safeArea"/> plus the fixed 8dp interior
        /// margin as <paramref name="hudRoot"/>'s margins. No-op if <paramref name="hudRoot"/>
        /// is not yet attached to a panel (its <see cref="VisualElement.panel"/> is null).
        /// </summary>
        public static void Apply(VisualElement hudRoot)
        {
            if (hudRoot == null)
                return;

            IPanel panel = hudRoot.panel;
            if (panel == null)
                return; // Not yet attached -- caller should retry after the next layout pass.

            Rect sa = Screen.safeArea;

            Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(sa.xMin, Screen.height - sa.yMax));
            Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(sa.xMax, Screen.height - sa.yMin));
            Vector2 panelSize = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(Screen.width, Screen.height));

            hudRoot.style.marginLeft = topLeft.x + InteriorMarginDp;
            hudRoot.style.marginTop = topLeft.y + InteriorMarginDp;
            hudRoot.style.marginRight = (panelSize.x - bottomRight.x) + InteriorMarginDp;
            hudRoot.style.marginBottom = (panelSize.y - bottomRight.y) + InteriorMarginDp;
        }
    }
}
