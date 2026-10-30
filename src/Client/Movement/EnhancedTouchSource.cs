using System;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace IronGrind.Client.Movement
{
    /// <summary>One raw touch sample (id, phase, position) as read from the Input System; the testable seam of <see cref="EnhancedTouchSource"/>.</summary>
    internal readonly struct RawTouch
    {
        public RawTouch(int touchId, InputTouchPhase phase, Vector2 screenPosition)
        {
            TouchId = touchId;
            Phase = phase;
            ScreenPosition = screenPosition;
        }

        public int TouchId { get; }
        public InputTouchPhase Phase { get; }
        public Vector2 ScreenPosition { get; }
    }

    /// <summary>
    /// Adapts Input System EnhancedTouch to <see cref="TouchInput"/> events for <see cref="TouchZoneRouter"/>
    /// (Movement Story 013). Fills a pre-allocated buffer each frame, so reading allocates nothing. The id is
    /// <c>touch.touchId</c> (stable for the touch's lifetime), never <c>finger.index</c>; positions pass through
    /// unchanged (bottom-left origin).
    /// </summary>
    public sealed class EnhancedTouchSource : ITouchFrameSource
    {
        /// <summary>Default buffer size: the most simultaneous touches the source reports.</summary>
        public const int DEFAULT_CAPACITY = 10;

        private readonly TouchInput[] _buffer;

        /// <summary>Creates the source and pre-allocates its buffer.</summary>
        /// <param name="capacity">Maximum touches reported per frame; extra touches are dropped.</param>
        public EnhancedTouchSource(int capacity = DEFAULT_CAPACITY)
        {
            _buffer = new TouchInput[capacity];
        }

        /// <summary>Enables EnhancedTouch (call from <c>OnEnable</c>).</summary>
        public void Enable()
        {
            EnhancedTouchSupport.Enable();
        }

        /// <summary>Disables EnhancedTouch (call from <c>OnDisable</c>).</summary>
        public void Disable()
        {
            EnhancedTouchSupport.Disable();
        }

        /// <summary>
        /// This frame's touch events from <c>Touch.activeTouches</c>. The span is valid until the next call.
        /// </summary>
        /// <returns>The filled part of the internal buffer.</returns>
        public ReadOnlySpan<TouchInput> ReadFrame()
        {
            var touches = EnhancedTouch.activeTouches;
            int count = 0;
            for (int i = 0; i < touches.Count; i++)
            {
                EnhancedTouch touch = touches[i];
                if (!TryAppend(_buffer, ref count, touch.touchId, touch.phase, touch.screenPosition))
                {
                    break;
                }
            }

            return new ReadOnlySpan<TouchInput>(_buffer, 0, count);
        }

        /// <summary>
        /// Maps one raw touch. Began/Moved/Ended/Canceled map one-to-one, Stationary maps to Moved, None is skipped.
        /// </summary>
        /// <returns>False for <c>TouchPhase.None</c>.</returns>
        internal static bool TryMap(int touchId, InputTouchPhase phase, Vector2 screenPosition, out TouchInput result)
        {
            TouchInputPhase mapped;
            switch (phase)
            {
                case InputTouchPhase.Began:
                    mapped = TouchInputPhase.Began;
                    break;
                case InputTouchPhase.Moved:
                case InputTouchPhase.Stationary:
                    mapped = TouchInputPhase.Moved;
                    break;
                case InputTouchPhase.Ended:
                    mapped = TouchInputPhase.Ended;
                    break;
                case InputTouchPhase.Canceled:
                    mapped = TouchInputPhase.Canceled;
                    break;
                default:
                    result = default;
                    return false;
            }

            result = new TouchInput(touchId, mapped, screenPosition);
            return true;
        }

        /// <summary>
        /// Maps raw touches into <paramref name="buffer"/>: at most <c>buffer.Length</c> entries, skipping
        /// <c>None</c> entries (they do not count). Same path as <see cref="ReadFrame"/>.
        /// </summary>
        /// <returns>The number of entries written.</returns>
        internal static int Fill(ReadOnlySpan<RawTouch> raw, TouchInput[] buffer)
        {
            int count = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (!TryAppend(buffer, ref count, raw[i].TouchId, raw[i].Phase, raw[i].ScreenPosition))
                {
                    break;
                }
            }

            return count;
        }

        // Returns false once the buffer is full.
        private static bool TryAppend(TouchInput[] buffer, ref int count, int touchId, InputTouchPhase phase, Vector2 position)
        {
            if (count >= buffer.Length)
            {
                return false;
            }

            if (TryMap(touchId, phase, position, out TouchInput mapped))
            {
                buffer[count++] = mapped;
            }

            return true;
        }
    }
}
