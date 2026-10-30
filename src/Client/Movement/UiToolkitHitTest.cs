using UnityEngine;
using UnityEngine.UIElements;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// UI Toolkit implementation of <see cref="IUiHitTest"/> (EC-MOV-4; Movement Story 013): a position is "on a
    /// skill button" when the panel element picked there, or one of its ancestors, has the USS class
    /// <see cref="TOUCH_BLOCKER_CLASS"/>. Allocation-free.
    /// </summary>
    public sealed class UiToolkitHitTest : IUiHitTest
    {
        /// <summary>USS class that marks an element as a touch blocker (auto-run button, future skill buttons).</summary>
        public const string TOUCH_BLOCKER_CLASS = "touch-blocker";

        private VisualElement _root;

        /// <summary>Creates the hit test.</summary>
        /// <param name="root">Any element of the panel (may be null until attached); its <c>panel</c> is read at test time.</param>
        public UiToolkitHitTest(VisualElement root)
        {
            _root = root;
        }

        /// <summary>Retargets the hit test, e.g. after the UIDocument rebuilt its tree on re-enable.</summary>
        /// <param name="root">Any element of the panel, or null.</param>
        public void SetRoot(VisualElement root)
        {
            _root = root;
        }

        /// <summary>True when the element picked at the screen position (bottom-left origin) is a touch blocker.</summary>
        /// <param name="screenPosition">Screen pixels, bottom-left origin.</param>
        /// <returns>True on a touch blocker; false when the panel is not attached.</returns>
        public bool IsOnSkillButton(Vector2 screenPosition)
        {
            IPanel panel = _root?.panel;
            if (panel == null)
            {
                return false;
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return HasBlockerInAncestry(panel.Pick(panelPos));
        }

        /// <summary>True when <paramref name="element"/> or one of its ancestors has the touch-blocker class.</summary>
        internal static bool HasBlockerInAncestry(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.ClassListContains(TOUCH_BLOCKER_CLASS))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
