using System;
using IronGrind.MovementSystem;
using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Assigns touches to the joystick or camera-drag zone by their start position and produces the floating
    /// joystick state and the camera drag delta (movement-system.md CR-MOV-1, CR-MOV-2, F-MOV-5, EC-MOV-1 to
    /// EC-MOV-6; Movement Story 010). One owner per zone; a later touch in an owned zone, and a touch that began
    /// on a skill button, is ignored for its whole lifetime. Allocation-free per frame.
    /// </summary>
    /// <example>
    /// <code>
    /// var router = new TouchZoneRouter(tuning, hitTest);
    /// router.ProcessFrame(touchEvents, Screen.safeArea);
    /// if (!router.Joystick.IsDead) { /* use router.Joystick.AngleDeg */ }
    /// </code>
    /// </example>
    public sealed class TouchZoneRouter
    {
        private readonly MovementTuningValues _tuning;
        private readonly IUiHitTest _hitTest;

        private bool _hasJoystickOwner;
        private int _joystickOwnerId;
        private bool _hasCameraOwner;
        private int _cameraOwnerId;
        private Vector2 _cameraLastPosition;

        /// <summary>Creates the router.</summary>
        /// <param name="tuning">TK-MOV-1 and TK-MOV-2 are read from it.</param>
        /// <param name="hitTest">Skill button hit test; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hitTest"/> is null.</exception>
        public TouchZoneRouter(MovementTuningValues tuning, IUiHitTest hitTest)
        {
            _tuning = tuning;
            _hitTest = hitTest ?? throw new ArgumentNullException(nameof(hitTest));
            Joystick = JoystickState.Inactive;
        }

        /// <summary>The floating joystick after the latest <see cref="ProcessFrame"/>.</summary>
        public JoystickState Joystick { get; private set; }

        /// <summary>True while a camera drag touch is held.</summary>
        public bool CameraDragActive => _hasCameraOwner;

        /// <summary>Sum of the camera owner's Moved deltas in the latest frame, in pixels; zero when none.</summary>
        public Vector2 CameraDragDeltaPx { get; private set; }

        /// <summary>
        /// Applies one frame of touch events in the fixed order of AC-MOV-26: (1) Ended/Canceled of touches that
        /// began in an earlier frame, (2) all Began, (3) all Moved, (4) Ended/Canceled of touches that began this frame.
        /// </summary>
        /// <param name="events">This frame's touch events in arrival order.</param>
        /// <param name="safeArea">The screen safe area in pixels, bottom-left origin.</param>
        public void ProcessFrame(ReadOnlySpan<TouchInput> events, Rect safeArea)
        {
            CameraDragDeltaPx = Vector2.zero;
            float midX = safeArea.x + safeArea.width / 2f;

            // Pass 1: owners at this point all began in an earlier frame.
            for (int i = 0; i < events.Length; i++)
            {
                if (IsRelease(events[i].Phase))
                {
                    Release(events[i].Id);
                }
            }

            // Pass 2: Began.
            for (int i = 0; i < events.Length; i++)
            {
                TouchInput e = events[i];
                if (e.Phase != TouchInputPhase.Began || _hitTest.IsOnSkillButton(e.Position))
                {
                    continue;
                }

                if (e.Position.x < midX)
                {
                    if (!_hasJoystickOwner)
                    {
                        _hasJoystickOwner = true;
                        _joystickOwnerId = e.Id;
                        Joystick = JoystickState.FromDrag(e.Position, e.Position,
                            _tuning.JoystickRadiusPx, _tuning.DeadZoneNormalized);
                    }
                }
                else if (!_hasCameraOwner)
                {
                    _hasCameraOwner = true;
                    _cameraOwnerId = e.Id;
                    _cameraLastPosition = e.Position;
                }
            }

            // Pass 3: Moved.
            for (int i = 0; i < events.Length; i++)
            {
                TouchInput e = events[i];
                if (e.Phase != TouchInputPhase.Moved)
                {
                    continue;
                }

                if (_hasJoystickOwner && e.Id == _joystickOwnerId)
                {
                    Joystick = JoystickState.FromDrag(Joystick.Anchor, e.Position,
                        _tuning.JoystickRadiusPx, _tuning.DeadZoneNormalized);
                }
                else if (_hasCameraOwner && e.Id == _cameraOwnerId)
                {
                    CameraDragDeltaPx += e.Position - _cameraLastPosition;
                    _cameraLastPosition = e.Position;
                }
            }

            // Pass 4: releases of touches that began this frame (earlier owners were released in pass 1).
            for (int i = 0; i < events.Length; i++)
            {
                if (IsRelease(events[i].Phase))
                {
                    Release(events[i].Id);
                }
            }
        }

        /// <summary>
        /// Drops both owners as if every touch were canceled: the joystick becomes inactive, the camera drag ends
        /// and the drag delta is zero. For when the touch source loses touches without sending Canceled (app focus
        /// loss, source re-initialised); called by the touch source in Movement Story 013.
        /// </summary>
        public void Reset()
        {
            _hasJoystickOwner = false;
            _hasCameraOwner = false;
            Joystick = JoystickState.Inactive;
            CameraDragDeltaPx = Vector2.zero;
        }

        private static bool IsRelease(TouchInputPhase phase)
        {
            return phase == TouchInputPhase.Ended || phase == TouchInputPhase.Canceled;
        }

        private void Release(int id)
        {
            if (_hasJoystickOwner && id == _joystickOwnerId)
            {
                _hasJoystickOwner = false;
                Joystick = JoystickState.Inactive;
            }
            else if (_hasCameraOwner && id == _cameraOwnerId)
            {
                _hasCameraOwner = false;
            }
        }
    }
}
