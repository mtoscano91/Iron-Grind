using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.Client.Movement;
using IronGrind.MovementSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.Client.Movement
{
    /// <summary>
    /// EditMode unit tests for Movement Story 011 - camera drag yaw, character facing and the client auto-run
    /// state: AC-MOV-4, 6, 12, 13, 19, 20 (client part), the client sub-states, F-MOV-3, CR-MOV-5 and
    /// EC-MOV-5, 7, 9, 18.
    /// </summary>
    [TestFixture]
    internal sealed class ClientMovement_CameraAndAutoRun_Tests
    {
        private const float YAW_TOLERANCE = 0.01f;
        private const float AC_MOV_4_TOLERANCE = 0.5f;
        private const float DIRECTION_TOLERANCE = 0.01f;
        private const float PX_FOR_QUARTER_TURN = 360f;      // 360 px * 0.25 deg/px = 90 deg
        private const float ANGLE_FORWARD_RIGHT = 45f;
        private const float ANGLE_RIGHT = 90f;
        private const float ANGLE_RIGHT_OF_REAR = 135f;
        private const float ANGLE_REAR_LEFT = 225f;
        private const float ANGLE_LEFT = 270f;
        private const float ANGLE_FORWARD_LEFT = 315f;
        private const float ANGLE_REAR = 180f;
        private const float SWEEP_STEP_DEG = 30f;
        private const int SWEEP_NO_CANCEL_STEPS = 4;          // 0, 30, 60, 90 deg
        private const float SWEEP_CANCEL_DEG = 120f;
        // The F-MOV-3 margin (dot < -1e-4, about 90.006 deg): cos(90.1 deg) = -0.0017 cancels, cos(89.99 deg) does not.
        private const float ANGLE_JUST_PAST_MARGIN = 90.1f;
        private const float ANGLE_JUST_BEFORE_90 = 89.99f;
        private const float DEAD_ZONE_DRAG_PX = 5f;
        private const float HELD_DRAG_PX = 80f;
        private const int AUTO_RUN_TICKS = 20;
        private const int ALLOC_ITERATIONS = 100;
        private const float ALLOC_DRAG_PX = 3f;
        private const float LARGE_DRAG_PX = 10000f;          // 2500 deg -> 340 deg after wrapping
        private const float LARGE_DRAG_EXPECTED_DEG = 340f;
        private const float BUTTON_X = 1700f;
        private const float BUTTON_Y = 150f;
        private const float BUTTON_SIZE = 100f;
        private const float SCREEN_WIDTH = 2000f;
        private const float SCREEN_HEIGHT = 1000f;
        private const int TOUCH_ID = 1;

        private sealed class FakeProhibitedSource : IClientProhibitedSource
        {
            public bool Value;

            public bool IsProhibited => Value;
        }

        private sealed class FakeHitTest : IUiHitTest
        {
            public readonly List<Rect> Buttons = new List<Rect>();

            public bool IsOnSkillButton(Vector2 screenPosition)
            {
                for (int i = 0; i < Buttons.Count; i++)
                {
                    if (Buttons[i].Contains(screenPosition))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private MovementTuningValues _tuning;
        private FakeProhibitedSource _prohibited;
        private AutoRunState _state;
        private CameraYaw _yaw;

        [SetUp]
        public void SetUp()
        {
            _tuning = MovementTuningValues.Defaults;
            _prohibited = new FakeProhibitedSource();
            _state = new AutoRunState(_prohibited);
            _yaw = new CameraYaw(_tuning);
        }

        private static JoystickState Held(float angleDeg) =>
            new JoystickState(true, Vector2.zero, Vector2.zero, false, angleDeg);

        private JoystickState InDeadZone() =>
            JoystickState.FromDrag(Vector2.zero, new Vector2(0f, DEAD_ZONE_DRAG_PX),
                _tuning.JoystickRadiusPx, _tuning.DeadZoneNormalized);

        private JoystickState HeldByDrag() =>
            JoystickState.FromDrag(Vector2.zero, new Vector2(0f, HELD_DRAG_PX),
                _tuning.JoystickRadiusPx, _tuning.DeadZoneNormalized);

        private static void AssertDirection(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, DIRECTION_TOLERANCE, "x");
            Assert.AreEqual(expected.y, actual.y, DIRECTION_TOLERANCE, "y");
            Assert.AreEqual(expected.z, actual.z, DIRECTION_TOLERANCE, "z");
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-4 camera yaw
        // ---------------------------------------------------------------------------------------

        [Test]
        public void CameraYaw_New_StartsAtZeroFacingForward()
        {
            Assert.AreEqual(0f, _yaw.YawDeg);
            AssertDirection(Vector3.forward, _yaw.FlatForward);
        }

        [TestCase(100f, 25f)]
        [TestCase(-40f, 350f)]
        [TestCase(0f, 0f)]
        public void CameraYaw_ApplyDrag_HorizontalDeltaTimesSensitivity(float dragPx, float expectedYawDeg)
        {
            _yaw.ApplyDrag(new Vector2(dragPx, 0f));

            Assert.AreEqual(expectedYawDeg, _yaw.YawDeg, AC_MOV_4_TOLERANCE);
        }

        [Test]
        public void CameraYaw_ApplyDrag_VerticalDeltaDoesNothing()
        {
            _yaw.ApplyDrag(new Vector2(0f, 100f));

            Assert.AreEqual(0f, _yaw.YawDeg);
        }

        [Test]
        public void CameraYaw_ApplyDrag_Yaw350Plus40PxWrapsToZero()
        {
            _yaw.ApplyDrag(new Vector2(-40f, 0f));
            Assert.AreEqual(350f, _yaw.YawDeg, YAW_TOLERANCE);

            _yaw.ApplyDrag(new Vector2(40f, 0f));

            Assert.AreEqual(0f, _yaw.YawDeg, YAW_TOLERANCE);
        }

        [Test]
        public void CameraYaw_ApplyDrag_LargePositiveDeltaWrapsIntoRange()
        {
            _yaw.ApplyDrag(new Vector2(LARGE_DRAG_PX, 0f));

            Assert.AreEqual(LARGE_DRAG_EXPECTED_DEG, _yaw.YawDeg, YAW_TOLERANCE);
        }

        [Test]
        public void CameraYaw_ApplyDrag_LargeNegativeDeltaStaysInRange()
        {
            _yaw.ApplyDrag(new Vector2(-LARGE_DRAG_PX, 0f));

            Assert.GreaterOrEqual(_yaw.YawDeg, 0f);
            Assert.Less(_yaw.YawDeg, 360f);
            Assert.AreEqual(360f - LARGE_DRAG_EXPECTED_DEG, _yaw.YawDeg, YAW_TOLERANCE);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void CameraYaw_ApplyDrag_NonFiniteDelta_IsIgnored(float dragPx)
        {
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));

            _yaw.ApplyDrag(new Vector2(dragPx, 0f));

            Assert.AreEqual(ANGLE_RIGHT, _yaw.YawDeg, YAW_TOLERANCE);
        }

        [Test]
        public void Facing_NaNCameraForwardAtRest_FacingUnchanged()
        {
            _state.Tick(JoystickState.Inactive, new Vector3(float.NaN, 0f, float.NaN));

            AssertDirection(Vector3.forward, _state.Facing.Forward);
            Assert.AreEqual(0f, _state.Facing.YawDeg);
        }

        [Test]
        public void Facing_NoHorizontalCameraForwardAtRest_FacingUnchanged()
        {
            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            _state.Tick(JoystickState.Inactive, Vector3.up);

            AssertDirection(Vector3.right, _state.Facing.Forward);
        }

        [Test]
        public void CameraYaw_FlatForward_QuarterTurnFacesRightAndIsUnitOnGround()
        {
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));

            AssertDirection(Vector3.right, _yaw.FlatForward);
            Assert.AreEqual(1f, _yaw.FlatForward.magnitude, DIRECTION_TOLERANCE);
            Assert.AreEqual(0f, _yaw.FlatForward.y, DIRECTION_TOLERANCE);
        }

        // ---------------------------------------------------------------------------------------
        // EC-MOV-18 facing, constructor, new state / Reset
        // ---------------------------------------------------------------------------------------

        [Test]
        public void CharacterFacing_New_ForwardIsVectorForwardYawZero()
        {
            var facing = new CharacterFacing();

            Assert.AreEqual(Vector3.forward, facing.Forward);
            Assert.AreEqual(0f, facing.YawDeg);
        }

        [Test]
        public void AutoRunState_NewFacing_IsVectorForward()
        {
            Assert.AreEqual(Vector3.forward, _state.Facing.Forward);
            Assert.AreEqual(0f, _state.Facing.YawDeg);
        }

        [Test]
        public void Ctor_NullProhibitedSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new AutoRunState(null));
        }

        [Test]
        public void AutoRunState_New_FlagOffIdleDirectionZero()
        {
            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        [Test]
        public void Reset_FlagOn_ClearsFlagKeepsFacingAndLeavesCameraYaw()
        {
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            Vector3 facingBefore = _state.Facing.Forward;
            float facingYawBefore = _state.Facing.YawDeg;
            float cameraYawBefore = _yaw.YawDeg;
            _state.PressButton();
            Assert.IsTrue(_state.IsActive);

            _state.Reset();

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
            Assert.AreEqual(facingBefore, _state.Facing.Forward);
            Assert.AreEqual(facingYawBefore, _state.Facing.YawDeg);
            Assert.AreEqual(cameraYawBefore, _yaw.YawDeg);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-6 / CR-MOV-3 facing
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Facing_JoystickHeldWhileCameraRotates_DoesNotFollowCamera()
        {
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            AssertDirection(Vector3.right, _state.Facing.Forward);
            Assert.AreEqual(ANGLE_RIGHT, _state.Facing.YawDeg, YAW_TOLERANCE);

            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);

            AssertDirection(Vector3.right, _state.Facing.Forward);
            AssertDirection(Vector3.right, _state.MoveDirection);
        }

        [Test]
        public void Facing_JoystickReheldAfterCameraRotated_UsesNewCameraForward()
        {
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));
            _state.Tick(JoystickState.Inactive, _yaw.FlatForward);

            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);

            AssertDirection(CameraRelativeDirection.WorldMoveDir(ANGLE_RIGHT, _yaw.FlatForward), _state.Facing.Forward);
        }

        [Test]
        public void Cancel_CameraRotatedMidHold_TestsAgainstLatchedForward()
        {
            // Joystick held straight ahead with auto-run on, then the camera turns 180 degrees. The joystick angle is
            // camera-relative, so the dot only depends on the angle and does not cancel either way; the latch shows
            // in the direction, which stays the latched forward instead of turning to the live camera's (0, 0, -1).
            _state.PressButton();
            _state.Tick(Held(0f), _yaw.FlatForward);
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN * 2f, 0f));

            _state.Tick(Held(0f), _yaw.FlatForward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
            AssertDirection(Vector3.forward, _state.MoveDirection);
        }

        [Test]
        public void Latch_KeptThroughProhibitedWhileJoystickHeld()
        {
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            _prohibited.Value = true;
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));

            _prohibited.Value = false;
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);

            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
            AssertDirection(Vector3.right, _state.MoveDirection);
            AssertDirection(Vector3.right, _state.Facing.Forward);
        }

        [Test]
        public void Latch_ClearedByReset()
        {
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            _state.Reset();
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));

            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);

            AssertDirection(CameraRelativeDirection.WorldMoveDir(ANGLE_RIGHT, _yaw.FlatForward), _state.MoveDirection);
            AssertDirection(Vector3.back, _state.MoveDirection);
        }

        [Test]
        public void Facing_JoystickReleased_EqualsCameraForwardNextTick()
        {
            _state.Tick(Held(ANGLE_RIGHT), _yaw.FlatForward);
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));

            _state.Tick(JoystickState.Inactive, _yaw.FlatForward);

            AssertDirection(_yaw.FlatForward, _state.Facing.Forward);
            Assert.AreEqual(_yaw.YawDeg, _state.Facing.YawDeg, YAW_TOLERANCE);
        }

        [Test]
        public void Facing_JoystickInDeadZone_FollowsCameraForward()
        {
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));

            _state.Tick(InDeadZone(), _yaw.FlatForward);

            AssertDirection(Vector3.right, _state.Facing.Forward);
        }

        [Test]
        public void Facing_TiltedCameraForward_StaysUnitOnGroundPlane()
        {
            _state.Tick(JoystickState.Inactive, new Vector3(1f, -0.5f, 0f));

            AssertDirection(Vector3.right, _state.Facing.Forward);
            Assert.AreEqual(1f, _state.Facing.Forward.magnitude, DIRECTION_TOLERANCE);
            Assert.AreEqual(0f, _state.Facing.Forward.y, DIRECTION_TOLERANCE);
        }

        [Test]
        public void Facing_YawIsAtan2WrappedTo360()
        {
            _state.Tick(Held(ANGLE_LEFT), Vector3.forward);

            Assert.AreEqual(ANGLE_LEFT, _state.Facing.YawDeg, YAW_TOLERANCE);
        }

        // ---------------------------------------------------------------------------------------
        // Sub-states
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SubState_NoInputNoFlag_IsIdleWithZeroDirection()
        {
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        [Test]
        public void SubState_JoystickHeldNoFlag_IsMovingJoystickAlongJoystickDirection()
        {
            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
            AssertDirection(Vector3.right, _state.MoveDirection);
        }

        [Test]
        public void SubState_JoystickFromDragHeld_IsMovingJoystick()
        {
            _state.Tick(HeldByDrag(), Vector3.forward);

            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
            AssertDirection(Vector3.forward, _state.MoveDirection);
        }

        [Test]
        public void SubState_DeadZoneJoystickNoFlag_IsIdle()
        {
            _state.Tick(InDeadZone(), Vector3.forward);

            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-12 auto-run toggle
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AutoRun_PressThen20IdleTicks_DirectionIsFacingEveryTick()
        {
            _yaw.ApplyDrag(new Vector2(PX_FOR_QUARTER_TURN, 0f));
            _state.PressButton();

            for (int i = 0; i < AUTO_RUN_TICKS; i++)
            {
                _state.Tick(JoystickState.Inactive, _yaw.FlatForward);

                Assert.IsTrue(_state.IsActive, "tick " + i);
                Assert.AreEqual(ClientMovementSubState.MovingAutoRun, _state.SubState, "tick " + i);
                AssertDirection(_state.Facing.Forward, _state.MoveDirection);
                AssertDirection(Vector3.right, _state.MoveDirection);
            }
        }

        [Test]
        public void AutoRun_SecondPress_ClearsFlagAndDirectionIsZeroNextTick()
        {
            _state.PressButton();
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            _state.PressButton();
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
        }

        [Test]
        public void AutoRun_PressBetweenTicks_FlagChangesNowDirectionOnNextTick()
        {
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            _state.PressButton();

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);

            _state.Tick(JoystickState.Inactive, Vector3.forward);

            AssertDirection(Vector3.forward, _state.MoveDirection);
        }

        [Test]
        public void AutoRun_PressBetweenTicksWhileHeld_SubStateChangesOnNextTick()
        {
            _state.Tick(Held(ANGLE_FORWARD_RIGHT), Vector3.forward);

            _state.PressButton();

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);

            _state.Tick(Held(ANGLE_FORWARD_RIGHT), Vector3.forward);

            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
        }

        // ---------------------------------------------------------------------------------------
        // Combined
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Combined_FlagOnJoystickForwardRight_JoystickWinsFlagStays()
        {
            _state.PressButton();

            _state.Tick(Held(ANGLE_FORWARD_RIGHT), Vector3.forward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
            AssertDirection(CameraRelativeDirection.WorldMoveDir(ANGLE_FORWARD_RIGHT, Vector3.forward), _state.MoveDirection);
        }

        [Test]
        public void Combined_JoystickReleased_BackToAutoRunForward()
        {
            _state.PressButton();
            _state.Tick(Held(ANGLE_FORWARD_RIGHT), Vector3.forward);

            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingAutoRun, _state.SubState);
            AssertDirection(Vector3.forward, _state.MoveDirection);
        }

        [Test]
        public void Combined_DeadZoneJoystickWithFlag_IsAutoRunNotCombined()
        {
            _state.PressButton();

            _state.Tick(InDeadZone(), Vector3.forward);

            Assert.AreEqual(ClientMovementSubState.MovingAutoRun, _state.SubState);
            AssertDirection(Vector3.forward, _state.MoveDirection);
            Assert.IsTrue(_state.IsActive);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-13 / F-MOV-3 rear-pull cancel
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Cancel_JoystickWorldRearOfCamera_ClearsFlagSameTickMovingJoystick()
        {
            // Camera forward (1, 0, 0); joystick world direction (-0.7, 0, 0.7): dot < 0.
            _state.PressButton();

            _state.Tick(Held(ANGLE_REAR_LEFT), Vector3.right);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
            AssertDirection(CameraRelativeDirection.WorldMoveDir(ANGLE_REAR_LEFT, Vector3.right), _state.MoveDirection);
            Assert.Less(_state.MoveDirection.x, 0f);
        }

        [Test]
        public void Cancel_Angle135WithCameraRight_Cancels()
        {
            _state.PressButton();

            _state.Tick(Held(ANGLE_RIGHT_OF_REAR), Vector3.right);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
        }

        [Test]
        public void Cancel_DotExactlyZero_DoesNotCancel()
        {
            // Camera forward (1, 0, 0); joystick world direction (0, 0, 1): dot = 0.
            _state.PressButton();

            _state.Tick(Held(ANGLE_LEFT), Vector3.right);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
            AssertDirection(Vector3.forward, _state.MoveDirection);
        }

        [Test]
        public void Cancel_DotZeroWithCameraForwardAndJoystickRight_DoesNotCancel()
        {
            _state.PressButton();

            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
        }

        [Test]
        public void Cancel_JoystickForwardOfCamera_DoesNotCancel()
        {
            // Camera forward (1, 0, 0); joystick world direction (0.7, 0, 0.7): dot > 0.
            _state.PressButton();

            _state.Tick(Held(ANGLE_FORWARD_LEFT), Vector3.right);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
        }

        [Test]
        public void Cancel_FlagOffAndJoystickRear_NothingToCancelStaysMovingJoystick()
        {
            _state.Tick(Held(ANGLE_REAR_LEFT), Vector3.right);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
        }

        [Test]
        public void Cancel_SlowSweepFrom0To120_FlagOnThrough90ClearedAt120()
        {
            _state.PressButton();

            for (int step = 0; step < SWEEP_NO_CANCEL_STEPS; step++)
            {
                float angle = step * SWEEP_STEP_DEG;
                _state.Tick(Held(angle), Vector3.forward);

                Assert.IsTrue(_state.IsActive, "angle " + angle);
                Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState, "angle " + angle);
            }

            _state.Tick(Held(SWEEP_CANCEL_DEG), Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
        }

        [Test]
        public void Cancel_JustPastMargin_Cancels()
        {
            _state.PressButton();

            _state.Tick(Held(ANGLE_JUST_PAST_MARGIN), Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
        }

        [Test]
        public void Cancel_JustBefore90_DoesNotCancel()
        {
            _state.PressButton();

            _state.Tick(Held(ANGLE_JUST_BEFORE_90), Vector3.forward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingCombined, _state.SubState);
        }

        [Test]
        public void Cancel_CameraForwardWithNoHorizontalPart_FallsBackToVectorForward()
        {
            // CameraRelativeDirection asserts on a camera forward with no XZ part and uses Vector3.forward; the
            // cancel test uses the same fallback, so a rear joystick still cancels.
            LogAssert.Expect(LogType.Assert, new Regex("no horizontal component"));
            _state.PressButton();

            _state.Tick(Held(ANGLE_REAR), Vector3.up);

            Assert.IsFalse(_state.IsActive);
            AssertDirection(Vector3.back, _state.MoveDirection);
        }

        [Test]
        public void Cancel_AfterCancel_ReleasingJoystickDoesNotRestartAutoRun()
        {
            _state.PressButton();
            _state.Tick(Held(SWEEP_CANCEL_DEG), Vector3.forward);

            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-19 / EC-MOV-7 Prohibited
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Prohibited_FlagOn_ClearsFlagSameTickDirectionZero()
        {
            _state.PressButton();
            _state.Tick(JoystickState.Inactive, Vector3.forward);
            _prohibited.Value = true;

            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Prohibited, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        [Test]
        public void Prohibited_EnteredDuringAutoRunAtRest_ClearsFlagDirectionZero()
        {
            _state.PressButton();
            _state.Tick(JoystickState.Inactive, Vector3.forward);
            Assert.AreEqual(ClientMovementSubState.MovingAutoRun, _state.SubState);
            _prohibited.Value = true;

            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Prohibited, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        [Test]
        public void Prohibited_JoystickHeld_FacingStillFollowsJoystick()
        {
            _prohibited.Value = true;

            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            AssertDirection(Vector3.right, _state.Facing.Forward);
        }

        [Test]
        public void Prohibited_JoystickAtRest_FacingFollowsCameraForward()
        {
            _prohibited.Value = true;

            _state.Tick(JoystickState.Inactive, Vector3.right);

            AssertDirection(Vector3.right, _state.Facing.Forward);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        [Test]
        public void Prohibited_ButtonPress_IsIgnored()
        {
            _prohibited.Value = true;

            _state.PressButton();
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Prohibited, _state.SubState);
        }

        [Test]
        public void Prohibited_Cleared_GoesToIdleAndDoesNotRearmFlag()
        {
            _state.PressButton();
            _prohibited.Value = true;
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            _prohibited.Value = false;
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.Idle, _state.SubState);
            Assert.AreEqual(Vector3.zero, _state.MoveDirection);
        }

        [Test]
        public void Prohibited_ClearedWithJoystickHeld_MovesSameTickWithoutFlag()
        {
            _state.PressButton();
            _prohibited.Value = true;
            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            _prohibited.Value = false;
            _state.Tick(Held(ANGLE_RIGHT), Vector3.forward);

            Assert.IsFalse(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingJoystick, _state.SubState);
            AssertDirection(Vector3.right, _state.MoveDirection);
        }

        [Test]
        public void Prohibited_PressAfterClear_TogglesAgain()
        {
            _prohibited.Value = true;
            _state.Tick(JoystickState.Inactive, Vector3.forward);
            _prohibited.Value = false;
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            _state.PressButton();
            _state.Tick(JoystickState.Inactive, Vector3.forward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingAutoRun, _state.SubState);
        }

        // ---------------------------------------------------------------------------------------
        // EC-MOV-5 skill taps, AC-MOV-20 / EC-MOV-9 server override
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SkillTap_ThroughRealRouter_NeverChangesFlag()
        {
            var hitTest = new FakeHitTest();
            hitTest.Buttons.Add(new Rect(BUTTON_X, BUTTON_Y, BUTTON_SIZE, BUTTON_SIZE));
            var router = new TouchZoneRouter(_tuning, hitTest);
            var area = new Rect(0f, 0f, SCREEN_WIDTH, SCREEN_HEIGHT);
            _state.PressButton();
            Vector2 onButton = new Vector2(BUTTON_X + 1f, BUTTON_Y + 1f);

            router.ProcessFrame(new[] { new TouchInput(TOUCH_ID, TouchInputPhase.Began, onButton) }, area);
            _state.Tick(router.Joystick, _yaw.FlatForward);
            router.ProcessFrame(new[] { new TouchInput(TOUCH_ID, TouchInputPhase.Ended, onButton) }, area);
            _state.Tick(router.Joystick, _yaw.FlatForward);

            Assert.IsTrue(_state.IsActive);
            Assert.AreEqual(ClientMovementSubState.MovingAutoRun, _state.SubState);
            AssertDirection(Vector3.forward, _state.MoveDirection);
        }

        [Test]
        public void ServerOverride_ClientKeepsSendingForwardIntent_FlagUnchanged()
        {
            _state.PressButton();

            for (int i = 0; i < AUTO_RUN_TICKS; i++)
            {
                _state.Tick(JoystickState.Inactive, Vector3.forward);

                Assert.IsTrue(_state.IsActive, "tick " + i);
                AssertDirection(Vector3.forward, _state.MoveDirection);
            }
        }

        // ---------------------------------------------------------------------------------------
        // No allocation
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ApplyDragAndTick_100Calls_AllocateNothing()
        {
            _state.PressButton();
            JoystickState joystick = HeldByDrag();
            Vector2 drag = new Vector2(ALLOC_DRAG_PX, 0f);

            // Warm-up call outside the measured region (JIT, first-use costs).
            _yaw.ApplyDrag(drag);
            _state.Tick(joystick, _yaw.FlatForward);

            Assert.That(() =>
            {
                for (int i = 0; i < ALLOC_ITERATIONS; i++)
                {
                    _yaw.ApplyDrag(drag);
                    _state.Tick(joystick, _yaw.FlatForward);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
