using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.Client.Movement;
using IronGrind.MovementSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Client.Movement
{
    /// <summary>
    /// EditMode unit tests for Movement Story 010 - touch zones, floating joystick (F-MOV-5) and camera-relative
    /// direction (F-MOV-4): AC-MOV-1 (client part), 2, 3, 5, 26 and EC-MOV-1 to EC-MOV-6.
    /// </summary>
    [TestFixture]
    internal sealed class ClientMovement_TouchInput_Tests
    {
        private const float SCREEN_WIDTH = 2000f;
        private const float SCREEN_HEIGHT = 1000f;
        private const float INSET = 44f;
        private const float MID_Y = 500f;
        private const float DIRECTION_TOLERANCE = 0.01f;
        private const float ANGLE_TOLERANCE = 0.01f;
        private const int ID_A = 1;
        private const int ID_B = 2;
        private const int ID_C = 3;
        private const int ID_UNKNOWN = 99;
        private const float LEFT_X = 300f;
        private const float RIGHT_X = 1500f;
        // AC-MOV-1 at defaults (radius 80 px, dead zone 0.15): the boundary is 12 px.
        private const float DEAD_ZONE_JUST_INSIDE_PX = 11.9f;
        private const float DEAD_ZONE_BOUNDARY_PX = 12.0f;
        // F-MOV-4 fallback threshold (0.001) on the camera forward's horizontal magnitude.
        private const float FLAT_JUST_BELOW_THRESHOLD = 0.0009f;
        private const float FLAT_JUST_ABOVE_THRESHOLD = 0.0011f;

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

        private FakeHitTest _hitTest;
        private TouchZoneRouter _router;
        private Rect _fullArea;

        [SetUp]
        public void SetUp()
        {
            _hitTest = new FakeHitTest();
            _router = new TouchZoneRouter(MovementTuningValues.Defaults, _hitTest);
            _fullArea = new Rect(0f, 0f, SCREEN_WIDTH, SCREEN_HEIGHT);
        }

        private static TouchInput Began(int id, float x, float y) =>
            new TouchInput(id, TouchInputPhase.Began, new Vector2(x, y));

        private static TouchInput Moved(int id, float x, float y) =>
            new TouchInput(id, TouchInputPhase.Moved, new Vector2(x, y));

        private static TouchInput Ended(int id, float x, float y) =>
            new TouchInput(id, TouchInputPhase.Ended, new Vector2(x, y));

        private static TouchInput Canceled(int id, float x, float y) =>
            new TouchInput(id, TouchInputPhase.Canceled, new Vector2(x, y));

        private void Frame(params TouchInput[] events) => _router.ProcessFrame(events, _fullArea);

        // ---------------------------------------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Ctor_NullHitTest_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new TouchZoneRouter(MovementTuningValues.Defaults, null));
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-3 / EC-MOV-3 zone assignment
        // ---------------------------------------------------------------------------------------

        [TestCase(999f, true)]
        [TestCase(1000f, false)]
        [TestCase(1001f, false)]
        public void Zone_FullSafeArea_SplitsAtMidpoint(float x, bool expectJoystick)
        {
            Frame(Began(ID_A, x, MID_Y));

            Assert.AreEqual(expectJoystick, _router.Joystick.IsActive);
            Assert.AreEqual(!expectJoystick, _router.CameraDragActive);
        }

        [TestCase(1021f, true)]
        [TestCase(1022f, false)]
        [TestCase(20f, true)]
        [TestCase(1990f, false)]
        public void Zone_LeftInsetSafeArea_MidpointShiftsAndInsetsBelongToTheirHalf(float x, bool expectJoystick)
        {
            var area = new Rect(INSET, 0f, SCREEN_WIDTH - INSET, SCREEN_HEIGHT);

            _router.ProcessFrame(new[] { Began(ID_A, x, MID_Y) }, area);

            Assert.AreEqual(expectJoystick, _router.Joystick.IsActive);
            Assert.AreEqual(!expectJoystick, _router.CameraDragActive);
        }

        [Test]
        public void Zone_YIsIgnored()
        {
            Frame(Began(ID_A, LEFT_X, SCREEN_HEIGHT - 1f));
            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.IsFalse(_router.CameraDragActive);
        }

        [Test]
        public void Zone_JoystickTouchDraggedIntoRightHalf_StaysJoystickWithNoCameraOutput()
        {
            Frame(Began(ID_A, 500f, MID_Y));
            Frame(Moved(ID_A, 1500f, MID_Y));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.IsFalse(_router.CameraDragActive);
            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);
        }

        [Test]
        public void Zone_RightHalfBegan_NeverActivatesJoystick()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Moved(ID_A, RIGHT_X + 200f, MID_Y));

            Assert.IsFalse(_router.Joystick.IsActive);
            Assert.IsTrue(_router.Joystick.IsDead);
        }

        [Test]
        public void Zone_LeftHalfBegan_NeverProducesCameraDrag()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X + 100f, MID_Y));

            Assert.IsFalse(_router.CameraDragActive);
            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);
        }

        // ---------------------------------------------------------------------------------------
        // EC-MOV-1 / EC-MOV-2 ownership
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Ownership_SecondLeftTouchWhileHeld_IsIgnored()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Began(ID_B, LEFT_X + 100f, MID_Y + 50f));
            Frame(Moved(ID_B, LEFT_X + 200f, MID_Y));

            Assert.AreEqual(new Vector2(LEFT_X, MID_Y), _router.Joystick.Anchor);
            Assert.AreEqual(new Vector2(LEFT_X, MID_Y), _router.Joystick.Current);
        }

        [Test]
        public void Ownership_IgnoredTouchNeverPromoted_EvenAfterOwnerLifts()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Began(ID_B, LEFT_X + 100f, MID_Y));
            Frame(Ended(ID_A, LEFT_X, MID_Y));
            Assert.IsFalse(_router.Joystick.IsActive);

            Frame(Moved(ID_B, LEFT_X + 150f, MID_Y));

            Assert.IsFalse(_router.Joystick.IsActive);
        }

        [Test]
        public void Ownership_NewTouchAfterOwnerLifted_BecomesOwner()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Ended(ID_A, LEFT_X, MID_Y));
            Frame(Began(ID_C, LEFT_X + 20f, MID_Y + 20f));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.AreEqual(new Vector2(LEFT_X + 20f, MID_Y + 20f), _router.Joystick.Anchor);
        }

        [Test]
        public void Ownership_SecondCameraTouchWhileHeld_IsIgnoredAndNeverPromoted()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Began(ID_B, RIGHT_X + 50f, MID_Y));
            Frame(Moved(ID_B, RIGHT_X + 80f, MID_Y));
            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);

            Frame(Ended(ID_A, RIGHT_X, MID_Y));
            Frame(Moved(ID_B, RIGHT_X + 90f, MID_Y));

            Assert.IsFalse(_router.CameraDragActive);
            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);
        }

        [Test]
        public void Ownership_UnknownIds_AreIgnoredWithoutException()
        {
            Assert.DoesNotThrow(() => Frame(
                Moved(ID_UNKNOWN, LEFT_X, MID_Y),
                Ended(ID_UNKNOWN, LEFT_X, MID_Y),
                Canceled(ID_UNKNOWN, RIGHT_X, MID_Y)));

            Assert.IsFalse(_router.Joystick.IsActive);
            Assert.IsFalse(_router.CameraDragActive);
        }

        [Test]
        public void Ownership_UnknownIdsDuringHeldTouches_DoNotDisturbThem()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y), Began(ID_B, RIGHT_X, MID_Y));
            Frame(Moved(ID_UNKNOWN, 10f, 10f), Ended(ID_UNKNOWN, 10f, 10f));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.IsTrue(_router.CameraDragActive);
        }

        [Test]
        public void Ownership_IgnoredTouchEnded_DoesNotReleaseEitherOwner()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y), Began(ID_B, RIGHT_X, MID_Y));
            Frame(Began(ID_C, LEFT_X + 100f, MID_Y), Began(ID_UNKNOWN, RIGHT_X + 100f, MID_Y));

            Frame(Ended(ID_C, LEFT_X + 100f, MID_Y), Canceled(ID_UNKNOWN, RIGHT_X + 100f, MID_Y));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.AreEqual(new Vector2(LEFT_X, MID_Y), _router.Joystick.Anchor);
            Assert.IsTrue(_router.CameraDragActive);
        }

        [Test]
        public void Ownership_ReusedIdAfterRelease_GetsFreshAnchorAndNoStaleCameraDelta()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y), Began(ID_B, RIGHT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X + 80f, MID_Y), Moved(ID_B, RIGHT_X + 50f, MID_Y));
            Frame(Ended(ID_A, LEFT_X + 80f, MID_Y), Ended(ID_B, RIGHT_X + 50f, MID_Y));

            Frame(Began(ID_A, LEFT_X + 30f, MID_Y + 30f), Began(ID_B, RIGHT_X + 200f, MID_Y));
            Frame(Moved(ID_B, RIGHT_X + 210f, MID_Y));

            Assert.AreEqual(new Vector2(LEFT_X + 30f, MID_Y + 30f), _router.Joystick.Anchor);
            Assert.IsTrue(_router.Joystick.IsDead);
            Assert.AreEqual(new Vector2(10f, 0f), _router.CameraDragDeltaPx);
        }

        [Test]
        public void Reset_DropsBothOwnersAndZeroesOutputs()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y), Began(ID_B, RIGHT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X + 80f, MID_Y), Moved(ID_B, RIGHT_X + 10f, MID_Y));

            _router.Reset();
            Frame(Moved(ID_A, LEFT_X + 90f, MID_Y), Moved(ID_B, RIGHT_X + 20f, MID_Y));

            Assert.IsFalse(_router.Joystick.IsActive);
            Assert.IsTrue(_router.Joystick.IsDead);
            Assert.IsFalse(_router.CameraDragActive);
            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-26 / EC-MOV-6 per-frame order
        // ---------------------------------------------------------------------------------------

        [Test]
        public void FrameOrder_SameFrameHandover_NewLeftTouchOwnsJoystick()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));

            Frame(Began(ID_B, LEFT_X + 100f, MID_Y + 100f), Ended(ID_A, LEFT_X, MID_Y));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.AreEqual(new Vector2(LEFT_X + 100f, MID_Y + 100f), _router.Joystick.Anchor);
        }

        [Test]
        public void FrameOrder_SameFrameHandover_EndedListedAfterBeganStillHandsOver()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));

            Frame(Ended(ID_A, LEFT_X, MID_Y), Began(ID_B, LEFT_X + 100f, MID_Y + 100f));

            Assert.AreEqual(new Vector2(LEFT_X + 100f, MID_Y + 100f), _router.Joystick.Anchor);
        }

        [Test]
        public void FrameOrder_LeftAndRightBeganPlusLeftMoved_BothArmAndMovedAppliesToJoystick()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y), Began(ID_B, LEFT_X, MID_Y), Moved(ID_B, LEFT_X + 80f, MID_Y));

            Assert.IsTrue(_router.CameraDragActive);
            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.AreEqual(new Vector2(LEFT_X + 80f, MID_Y), _router.Joystick.Current);
            Assert.IsFalse(_router.Joystick.IsDead);
        }

        [Test]
        public void FrameOrder_MovedBeforeBeganInSpan_StillAppliesAfterBegan()
        {
            Frame(Moved(ID_B, LEFT_X + 80f, MID_Y), Began(ID_B, LEFT_X, MID_Y));

            Assert.AreEqual(new Vector2(LEFT_X + 80f, MID_Y), _router.Joystick.Current);
        }

        [Test]
        public void FrameOrder_TouchBeganAndEndedInSameFrame_LeavesNothingActive()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y), Moved(ID_A, LEFT_X + 80f, MID_Y), Ended(ID_A, LEFT_X + 80f, MID_Y));

            Assert.IsFalse(_router.Joystick.IsActive);
        }

        [Test]
        public void FrameOrder_CameraTouchBeganMovedEndedInSameFrame_DeltaCountedAndDragEnded()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y), Moved(ID_A, RIGHT_X + 40f, MID_Y), Ended(ID_A, RIGHT_X + 40f, MID_Y));

            Assert.AreEqual(new Vector2(40f, 0f), _router.CameraDragDeltaPx);
            Assert.IsFalse(_router.CameraDragActive);
        }

        // ---------------------------------------------------------------------------------------
        // EC-MOV-4 skill buttons
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SkillButton_RightHalfBeganDuringCameraDrag_CameraKeepsOriginalTouchAndButtonClaimsNothing()
        {
            _hitTest.Buttons.Add(new Rect(1700f, 100f, 100f, 100f));
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Began(ID_B, 1750f, 150f));
            Frame(Moved(ID_B, 1760f, 150f));
            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);

            Frame(Moved(ID_A, RIGHT_X + 10f, MID_Y));
            Assert.IsTrue(_router.CameraDragActive);
            Assert.AreEqual(new Vector2(10f, 0f), _router.CameraDragDeltaPx);

            Frame(Ended(ID_A, RIGHT_X + 10f, MID_Y));
            Assert.IsFalse(_router.CameraDragActive);
        }

        [Test]
        public void SkillButton_ButtonTouchNeverBecomesCameraOwner_WhenCameraLifts()
        {
            _hitTest.Buttons.Add(new Rect(1700f, 100f, 100f, 100f));
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Began(ID_B, 1750f, 150f));
            Frame(Ended(ID_A, RIGHT_X, MID_Y));

            Frame(Moved(ID_B, 1760f, 150f));

            Assert.IsFalse(_router.CameraDragActive);
        }

        [Test]
        public void SkillButton_LeftHalfBegan_ProducesNoJoystick()
        {
            _hitTest.Buttons.Add(new Rect(100f, 100f, 100f, 100f));

            Frame(Began(ID_A, 150f, 150f));
            Frame(Moved(ID_A, 200f, 300f));

            Assert.IsFalse(_router.Joystick.IsActive);
        }

        [Test]
        public void SkillButton_LeftHalfBeganDuringJoystick_JoystickContinues()
        {
            _hitTest.Buttons.Add(new Rect(100f, 100f, 100f, 100f));
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Began(ID_B, 150f, 150f));
            Frame(Moved(ID_A, LEFT_X + 80f, MID_Y));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.AreEqual(new Vector2(LEFT_X, MID_Y), _router.Joystick.Anchor);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-2 anchor, AC-MOV-1 dead zone, angle, release
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Joystick_Anchor_IsTheExactBeganPixel()
        {
            Frame(Began(ID_A, 321f, 456f));

            Assert.AreEqual(new Vector2(321f, 456f), _router.Joystick.Anchor);
            Assert.AreEqual(new Vector2(321f, 456f), _router.Joystick.Current);
        }

        [Test]
        public void Joystick_JustBegan_IsDead()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));

            Assert.IsTrue(_router.Joystick.IsActive);
            Assert.IsTrue(_router.Joystick.IsDead);
            Assert.AreEqual(0f, _router.Joystick.AngleDeg);
        }

        [Test]
        public void Joystick_DeadZone_Offset11Point9IsDead()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X, MID_Y + DEAD_ZONE_JUST_INSIDE_PX));

            Assert.IsTrue(_router.Joystick.IsDead);
            Assert.AreEqual(0f, _router.Joystick.AngleDeg);
        }

        [Test]
        public void Joystick_DeadZone_Offset12Point0IsNotDead()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X, MID_Y + DEAD_ZONE_BOUNDARY_PX));

            Assert.IsFalse(_router.Joystick.IsDead);
            Assert.AreEqual(0f, _router.Joystick.AngleDeg, ANGLE_TOLERANCE);
        }

        [Test]
        public void Joystick_Angle_UpIsZero_RightIs90()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X, MID_Y + 80f));
            Assert.AreEqual(0f, _router.Joystick.AngleDeg, ANGLE_TOLERANCE);

            Frame(Moved(ID_A, LEFT_X + 80f, MID_Y));
            Assert.AreEqual(90f, _router.Joystick.AngleDeg, ANGLE_TOLERANCE);
        }

        [Test]
        public void Joystick_Angle_LeftIs270()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X - 80f, MID_Y));

            Assert.AreEqual(270f, _router.Joystick.AngleDeg, ANGLE_TOLERANCE);
        }

        [Test]
        public void Joystick_Angle_DownIs180()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X, MID_Y - 80f));

            Assert.AreEqual(180f, _router.Joystick.AngleDeg, ANGLE_TOLERANCE);
        }

        [Test]
        public void Joystick_Angle_StaysInZeroTo360ForAllQuadrants()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X - 50f, MID_Y + 50f));

            Assert.AreEqual(315f, _router.Joystick.AngleDeg, ANGLE_TOLERANCE);
        }

        [Test]
        public void Joystick_Ended_ZeroesDirectionTheSameFrame()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X + 80f, MID_Y));

            Frame(Ended(ID_A, LEFT_X + 80f, MID_Y));

            Assert.IsFalse(_router.Joystick.IsActive);
            Assert.IsTrue(_router.Joystick.IsDead);
            Assert.AreEqual(0f, _router.Joystick.AngleDeg);
        }

        [Test]
        public void Joystick_Canceled_ZeroesDirectionTheSameFrame()
        {
            Frame(Began(ID_A, LEFT_X, MID_Y));
            Frame(Moved(ID_A, LEFT_X + 80f, MID_Y));

            Frame(Canceled(ID_A, LEFT_X + 80f, MID_Y));

            Assert.IsFalse(_router.Joystick.IsActive);
            Assert.IsTrue(_router.Joystick.IsDead);
        }

        // ---------------------------------------------------------------------------------------
        // Camera drag delta
        // ---------------------------------------------------------------------------------------

        [Test]
        public void CameraDrag_DeltaIsSumOfMovedDeltasInTheFrame()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Moved(ID_A, RIGHT_X + 10f, MID_Y + 5f), Moved(ID_A, RIGHT_X + 15f, MID_Y + 5f));

            Assert.AreEqual(new Vector2(15f, 5f), _router.CameraDragDeltaPx);
            Assert.IsTrue(_router.CameraDragActive);
        }

        [Test]
        public void CameraDrag_DeltaResetsToZeroNextFrame_WhileStillActive()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Moved(ID_A, RIGHT_X + 10f, MID_Y));

            Frame();

            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);
            Assert.IsTrue(_router.CameraDragActive);
        }

        [Test]
        public void CameraDrag_DeltaContinuesFromPreviousFramePosition()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Moved(ID_A, RIGHT_X + 10f, MID_Y));
            Frame(Moved(ID_A, RIGHT_X + 25f, MID_Y));

            Assert.AreEqual(new Vector2(15f, 0f), _router.CameraDragDeltaPx);
        }

        [Test]
        public void CameraDrag_BeganFrameHasNoDelta()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));

            Assert.AreEqual(Vector2.zero, _router.CameraDragDeltaPx);
        }

        [Test]
        public void CameraDrag_Canceled_EndsDrag()
        {
            Frame(Began(ID_A, RIGHT_X, MID_Y));
            Frame(Canceled(ID_A, RIGHT_X, MID_Y));

            Assert.IsFalse(_router.CameraDragActive);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-5 camera-relative direction
        // ---------------------------------------------------------------------------------------

        private static void AssertDirection(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, DIRECTION_TOLERANCE, "x");
            Assert.AreEqual(expected.y, actual.y, DIRECTION_TOLERANCE, "y");
            Assert.AreEqual(expected.z, actual.z, DIRECTION_TOLERANCE, "z");
        }

        [Test]
        public void WorldMoveDir_TiltedCameraEastAndJoystickUp_IsEast()
        {
            AssertDirection(Vector3.right, CameraRelativeDirection.WorldMoveDir(0f, new Vector3(1f, -0.5f, 0f)));
        }

        [Test]
        public void WorldMoveDir_CameraNorthAndJoystickRight_IsEast()
        {
            AssertDirection(Vector3.right, CameraRelativeDirection.WorldMoveDir(90f, Vector3.forward));
        }

        [Test]
        public void WorldMoveDir_ResultIsUnitLengthOnGroundPlane()
        {
            Vector3 dir = CameraRelativeDirection.WorldMoveDir(135f, new Vector3(0.3f, -0.7f, 0.9f));

            Assert.AreEqual(1f, dir.magnitude, DIRECTION_TOLERANCE);
            Assert.AreEqual(0f, dir.y, DIRECTION_TOLERANCE);
        }

        [Test]
        public void WorldMoveDir_CameraStraightDownAndJoystickUp_FallsBackToForward()
        {
            LogAssert.Expect(LogType.Assert, new Regex("no horizontal component"));

            AssertDirection(Vector3.forward, CameraRelativeDirection.WorldMoveDir(0f, Vector3.down));
        }

        [Test]
        public void WorldMoveDir_FlatMagnitudeJustBelowThreshold_FallsBack()
        {
            LogAssert.Expect(LogType.Assert, new Regex("no horizontal component"));

            Vector3 nearlyDown = new Vector3(FLAT_JUST_BELOW_THRESHOLD, -1f, 0f);

            AssertDirection(Vector3.forward, CameraRelativeDirection.WorldMoveDir(0f, nearlyDown));
        }

        [Test]
        public void WorldMoveDir_FlatMagnitudeJustAboveThreshold_UsesCameraFacing()
        {
            Vector3 nearlyDownFacingEast = new Vector3(FLAT_JUST_ABOVE_THRESHOLD, -1f, 0f);

            AssertDirection(Vector3.right, CameraRelativeDirection.WorldMoveDir(0f, nearlyDownFacingEast));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WorldMoveDir_CameraStraightDownAndJoystickRight_IsEast()
        {
            LogAssert.Expect(LogType.Assert, new Regex("no horizontal component"));

            AssertDirection(Vector3.right, CameraRelativeDirection.WorldMoveDir(90f, Vector3.down));
        }
    }
}
