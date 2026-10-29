using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Seam that tells the touch router whether a screen position is on a skill button (EC-MOV-4). The real
    /// implementation (UI Toolkit picking) comes in Movement Story 013; tests use a fake.
    /// </summary>
    public interface IUiHitTest
    {
        /// <summary>True when <paramref name="screenPosition"/> (screen pixels, bottom-left origin) is on a skill button.</summary>
        /// <param name="screenPosition">The position to test.</param>
        /// <returns>True on a skill button.</returns>
        bool IsOnSkillButton(Vector2 screenPosition);
    }
}
