using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>The phase of one touch event (Movement Story 010).</summary>
    public enum TouchInputPhase
    {
        /// <summary>The finger went down.</summary>
        Began,
        /// <summary>The finger moved.</summary>
        Moved,
        /// <summary>The finger lifted.</summary>
        Ended,
        /// <summary>The system cancelled the touch.</summary>
        Canceled
    }

    /// <summary>
    /// One touch event as <see cref="TouchZoneRouter"/> takes it: a plain struct, so the router does not depend on
    /// the real touch source (chosen in Movement Story 013). Positions are Unity screen pixels, bottom-left origin.
    /// </summary>
    public readonly struct TouchInput
    {
        /// <summary>Creates a touch event.</summary>
        /// <param name="id">The finger id, stable for the touch's lifetime.</param>
        /// <param name="phase">The event phase.</param>
        /// <param name="position">The screen position in pixels.</param>
        public TouchInput(int id, TouchInputPhase phase, Vector2 position)
        {
            Id = id;
            Phase = phase;
            Position = position;
        }

        /// <summary>The finger id, stable for the touch's lifetime.</summary>
        public int Id { get; }

        /// <summary>The event phase.</summary>
        public TouchInputPhase Phase { get; }

        /// <summary>The screen position in pixels, bottom-left origin.</summary>
        public Vector2 Position { get; }
    }
}
