using System;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Supplies one frame of <see cref="TouchInput"/> events to <see cref="ClientMovementController"/> (Movement
    /// Story 013). The game uses <see cref="EnhancedTouchSource"/>; the dev harness can swap in a mouse source.
    /// </summary>
    public interface ITouchFrameSource
    {
        /// <summary>Starts reading input (called from the controller's <c>OnEnable</c>).</summary>
        void Enable();

        /// <summary>Stops reading input (called from the controller's <c>OnDisable</c>).</summary>
        void Disable();

        /// <summary>This frame's touch events; the span is valid until the next call.</summary>
        /// <returns>The events, bottom-left screen pixels.</returns>
        ReadOnlySpan<TouchInput> ReadFrame();
    }
}
