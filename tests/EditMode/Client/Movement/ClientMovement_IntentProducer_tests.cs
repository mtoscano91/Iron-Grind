using System;
using System.Text.RegularExpressions;
using IronGrind.Client.Movement;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;

namespace IronGrind.Tests.EditMode.Client.Movement
{
    /// <summary>
    /// EditMode unit tests for Movement Story 012 - the client intent producer and send cadence: AC-MOV-14,
    /// AC-MOV-1 (wire part), CR-MOV-8, CR-MOV-10, EC-MOV-15, EC-MOV-21.
    /// </summary>
    [TestFixture]
    internal sealed class ClientMovement_IntentProducer_Tests
    {
        private const int SINK_CAPACITY = 512;
        private const int AC_MOV_14_TICKS = 100;
        private const int IDLE_TICKS = 10;
        private const int HOLD_TICKS = 5;
        private const int ALLOC_ITERATIONS = 100;
        private const float ANGLE_RIGHT = 90f;
        private const float ANGLE_REAR = 180f;
        // 12 px is exactly the dead-zone edge at radius 80 / 0.15. The sweep uses a hair more because
        // 12 * sin / 12 * cos rounds to slightly under 12 at some angles, which would be dead by float rounding.
        private const float EDGE_DRAG_PX = 12.01f;
        private const float DEAD_ZONE_DRAG_PX = 11f;
        private const int SWEEP_ANGLES = 360;
        private const short DIR_FULL = MovementIntentMessageCodec.DIRECTION_SCALE;
        private const short FACING_RIGHT = 900;
        private const short FACING_REAR = 1800;
        private const uint FIRST_TICK = 1u;
        private const uint JUMP_TICK = 8u;
        private const uint REPEATED_TICK = 5u;
        private const uint OLDER_TICK = 4u;
        private const uint RESET_TICK = 3u;
        private const uint LATE_TICK = 10u;
        private const uint ARBITRARY_TICK = 4242u;
        private const float NON_UNIT_COMPONENT = 2f;
        private const float BUILD_FACING_DEG = 123.4f;
        private const short BUILD_FACING = 1234;
        private const short BUILD_DIR_X = 19660;
        private const short BUILD_DIR_Z = -26214;
        private const float BUILD_DIR_X_F = 0.6f;
        private const float BUILD_DIR_Z_F = -0.8f;
        private const uint BUILD_TICK = 1000u;
        // EncodeFacing is in 0.1-degree steps: 0.04 degrees rounds to the same short as 0.
        private const float SUB_STEP_YAW_DEG = 0.04f;

        private sealed class FakeProhibitedSource : IClientProhibitedSource
        {
            public bool Value;

            public bool IsProhibited => Value;
        }

        private sealed class FakeSink : IMovementIntentSink
        {
            public readonly MovementIntentMessage[] Messages = new MovementIntentMessage[SINK_CAPACITY];
            public int Count;

            public void Send(in MovementIntentMessage message)
            {
                if (Count < SINK_CAPACITY)
                {
                    Messages[Count] = message;
                }

                Count++;
            }
        }

        private MovementTuningValues _tuning;
        private FakeProhibitedSource _prohibited;
        private AutoRunState _state;
        private FakeSink _sink;
        private MovementIntentProducer _producer;

        [SetUp]
        public void SetUp()
        {
            _tuning = MovementTuningValues.Defaults;
            _prohibited = new FakeProhibitedSource();
            _state = new AutoRunState(_prohibited);
            _sink = new FakeSink();
            _producer = new MovementIntentProducer(_state, _sink);
        }

        private static JoystickState Held(float angleDeg) =>
            new JoystickState(true, Vector2.zero, Vector2.zero, false, angleDeg);

        private static void AssertMessage(MovementIntentMessage message, short dirX, short dirZ, short facing, uint tick)
        {
            Assert.AreEqual(dirX, message.DirX, "DirX");
            Assert.AreEqual(dirZ, message.DirZ, "DirZ");
            Assert.AreEqual(facing, message.FacingAngle, "FacingAngle");
            Assert.AreEqual(tick, message.TickNumber, "TickNumber");
        }

        // ---------------------------------------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Ctor_NullState_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new MovementIntentProducer(null, _sink));
        }

        [Test]
        public void Ctor_NullSink_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new MovementIntentProducer(_state, null));
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-14 cadence
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Cadence_HeldJoystick100Ticks_OneMessagePerTickInOrder()
        {
            for (uint tick = FIRST_TICK; tick <= AC_MOV_14_TICKS; tick++)
            {
                _producer.OnTick(tick, Held(0f), Vector3.forward);
            }

            Assert.AreEqual(AC_MOV_14_TICKS, _sink.Count);
            for (int i = 0; i < AC_MOV_14_TICKS; i++)
            {
                Assert.AreEqual((uint)(i + 1), _sink.Messages[i].TickNumber, "message " + i);
            }
        }

        [Test]
        public void Message_TickNumberIsArgumentAndFacingIsEncodedYaw()
        {
            _producer.OnTick(ARBITRARY_TICK, Held(ANGLE_RIGHT), Vector3.forward);

            Assert.AreEqual(1, _sink.Count);
            AssertMessage(_sink.Messages[0], DIR_FULL, 0, FACING_RIGHT, ARBITRARY_TICK);
            Assert.AreEqual(MovementIntentMessageCodec.EncodeFacing(_state.Facing.YawDeg), _sink.Messages[0].FacingAngle);
        }

        // ---------------------------------------------------------------------------------------
        // Suppression / first tick / release
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Suppression_FirstTickNoInput_SendsZeroDirectionFacingZero()
        {
            _producer.OnTick(FIRST_TICK, JoystickState.Inactive, Vector3.forward);

            Assert.AreEqual(1, _sink.Count);
            AssertMessage(_sink.Messages[0], 0, 0, 0, FIRST_TICK);
        }

        [Test]
        public void Suppression_IdleTicks_NoMessagesUntilCameraRotates()
        {
            for (uint tick = FIRST_TICK; tick <= IDLE_TICKS; tick++)
            {
                Vector3 camera = tick >= 5u ? Vector3.right : Vector3.forward;
                _producer.OnTick(tick, JoystickState.Inactive, camera);
            }

            Assert.AreEqual(2, _sink.Count);
            AssertMessage(_sink.Messages[0], 0, 0, 0, FIRST_TICK);
            AssertMessage(_sink.Messages[1], 0, 0, FACING_RIGHT, 5u);
        }

        [Test]
        public void Suppression_FacingChangeBelowEncodingStep_SendsNothing()
        {
            _producer.OnTick(FIRST_TICK, JoystickState.Inactive, Vector3.forward);
            Vector3 nudged = Quaternion.AngleAxis(SUB_STEP_YAW_DEG, Vector3.up) * Vector3.forward;

            _producer.OnTick(2u, JoystickState.Inactive, nudged);

            Assert.AreNotEqual(0f, _state.Facing.YawDeg);
            Assert.AreEqual(1, _sink.Count);
        }

        [Test]
        public void Release_HeldAtRightThenReleased_OneFacingMessageThenSilence()
        {
            for (uint tick = FIRST_TICK; tick <= HOLD_TICKS; tick++)
            {
                _producer.OnTick(tick, Held(ANGLE_RIGHT), Vector3.forward);
            }

            Assert.AreEqual(HOLD_TICKS, _sink.Count);
            for (int i = 0; i < HOLD_TICKS; i++)
            {
                AssertMessage(_sink.Messages[i], DIR_FULL, 0, FACING_RIGHT, (uint)(i + 1));
            }

            for (uint tick = HOLD_TICKS + 1u; tick <= IDLE_TICKS; tick++)
            {
                _producer.OnTick(tick, JoystickState.Inactive, Vector3.forward);
            }

            Assert.AreEqual(HOLD_TICKS + 1, _sink.Count);
            AssertMessage(_sink.Messages[HOLD_TICKS], 0, 0, 0, HOLD_TICKS + 1u);
        }

        [Test]
        public void Release_HeldAtForwardThenReleased_FacingUnchangedSendsNothing()
        {
            for (uint tick = FIRST_TICK; tick <= HOLD_TICKS; tick++)
            {
                _producer.OnTick(tick, Held(0f), Vector3.forward);
            }

            for (uint tick = HOLD_TICKS + 1u; tick <= IDLE_TICKS; tick++)
            {
                _producer.OnTick(tick, JoystickState.Inactive, Vector3.forward);
            }

            Assert.AreEqual(HOLD_TICKS, _sink.Count);
        }

        // ---------------------------------------------------------------------------------------
        // CR-MOV-8 Prohibited
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Prohibited_JoystickHeld_MessagesCarryZeroDirectionAndOnlyFacingChangesSend()
        {
            _prohibited.Value = true;

            _producer.OnTick(1u, Held(ANGLE_RIGHT), Vector3.forward);
            _producer.OnTick(2u, Held(ANGLE_RIGHT), Vector3.forward);
            _producer.OnTick(3u, Held(ANGLE_RIGHT), Vector3.forward);
            _producer.OnTick(4u, Held(ANGLE_REAR), Vector3.forward);
            _producer.OnTick(5u, Held(ANGLE_REAR), Vector3.forward);

            Assert.AreEqual(2, _sink.Count);
            AssertMessage(_sink.Messages[0], 0, 0, FACING_RIGHT, 1u);
            AssertMessage(_sink.Messages[1], 0, 0, FACING_REAR, 4u);
        }

        [Test]
        public void Prohibited_CameraRotatesAtRest_SendsZeroDirectionWithNewFacing()
        {
            _prohibited.Value = true;
            _producer.OnTick(1u, JoystickState.Inactive, Vector3.forward);

            _producer.OnTick(2u, JoystickState.Inactive, Vector3.right);
            _producer.OnTick(3u, JoystickState.Inactive, Vector3.right);

            Assert.AreEqual(2, _sink.Count);
            AssertMessage(_sink.Messages[1], 0, 0, FACING_RIGHT, 2u);
        }

        [Test]
        public void Prohibited_EnteredWhileMovingWithSameFacing_SendsNothing()
        {
            _producer.OnTick(1u, Held(0f), Vector3.forward);
            _producer.OnTick(2u, Held(0f), Vector3.forward);
            _prohibited.Value = true;

            _producer.OnTick(3u, Held(0f), Vector3.forward);
            _producer.OnTick(4u, Held(0f), Vector3.forward);

            Assert.AreEqual(2, _sink.Count);
        }

        // ---------------------------------------------------------------------------------------
        // Auto-run
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AutoRun_FlagOnNoTouch_OneMessagePerTickWithFacingDirection()
        {
            _state.PressButton();

            for (uint tick = FIRST_TICK; tick <= IDLE_TICKS; tick++)
            {
                _producer.OnTick(tick, JoystickState.Inactive, Vector3.right);
            }

            Assert.AreEqual((int)IDLE_TICKS, _sink.Count);
            for (int i = 0; i < IDLE_TICKS; i++)
            {
                AssertMessage(_sink.Messages[i], DIR_FULL, 0, FACING_RIGHT, (uint)(i + 1));
            }
        }

        [Test]
        public void AutoRun_CameraTurns_DirectionAndFacingFollowCamera()
        {
            _state.PressButton();

            _producer.OnTick(1u, JoystickState.Inactive, Vector3.forward);
            _producer.OnTick(2u, JoystickState.Inactive, Vector3.forward);
            _producer.OnTick(3u, JoystickState.Inactive, Vector3.right);

            Assert.AreEqual(3, _sink.Count);
            AssertMessage(_sink.Messages[0], 0, DIR_FULL, 0, 1u);
            AssertMessage(_sink.Messages[1], 0, DIR_FULL, 0, 2u);
            AssertMessage(_sink.Messages[2], DIR_FULL, 0, FACING_RIGHT, 3u);
        }

        // ---------------------------------------------------------------------------------------
        // AC-MOV-1 wire / EC-MOV-21
        // ---------------------------------------------------------------------------------------

        [Test]
        public void DeadZone_ElevenPixelDrag_SendsZeroDirection()
        {
            JoystickState dead = JoystickState.FromDrag(Vector2.zero, new Vector2(0f, DEAD_ZONE_DRAG_PX),
                _tuning.JoystickRadiusPx, _tuning.DeadZoneNormalized);
            Assert.IsTrue(dead.IsDead);

            _producer.OnTick(FIRST_TICK, dead, Vector3.forward);

            Assert.AreEqual(1, _sink.Count);
            AssertMessage(_sink.Messages[0], 0, 0, 0, FIRST_TICK);
        }

        [Test]
        public void DeadZoneEdge_SweepOf360Angles_EveryMessageHasNonZeroDirection()
        {
            for (int angle = 0; angle < SWEEP_ANGLES; angle++)
            {
                float rad = angle * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * EDGE_DRAG_PX;
                JoystickState joystick = JoystickState.FromDrag(Vector2.zero, offset,
                    _tuning.JoystickRadiusPx, _tuning.DeadZoneNormalized);
                Assert.IsFalse(joystick.IsDead, "dead at " + angle);
                var state = new AutoRunState(new FakeProhibitedSource());
                var sink = new FakeSink();
                var producer = new MovementIntentProducer(state, sink);

                producer.OnTick(FIRST_TICK, joystick, Vector3.forward);

                Assert.AreEqual(1, sink.Count, "count at " + angle);
                Assert.IsTrue(sink.Messages[0].DirX != 0 || sink.Messages[0].DirZ != 0, "zero direction at " + angle);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Repeated / older ticks
        // ---------------------------------------------------------------------------------------

        [Test]
        public void RepeatedTick_SameOlderIgnoredForwardJumpSends()
        {
            _producer.OnTick(REPEATED_TICK, Held(0f), Vector3.forward);

            // A repeated tick must not tick the state: the different joystick would change the direction.
            _producer.OnTick(REPEATED_TICK, Held(ANGLE_RIGHT), Vector3.forward);
            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual(Vector3.forward.z, _state.MoveDirection.z, 0.01f);

            _producer.OnTick(OLDER_TICK, Held(0f), Vector3.forward);
            Assert.AreEqual(1, _sink.Count);

            _producer.OnTick(JUMP_TICK, Held(0f), Vector3.forward);
            Assert.AreEqual(2, _sink.Count);
            Assert.AreEqual(JUMP_TICK, _sink.Messages[1].TickNumber);
        }

        [Test]
        public void TickZero_IsAcceptedAsFirstTick()
        {
            _producer.OnTick(0u, JoystickState.Inactive, Vector3.forward);

            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual(0u, _sink.Messages[0].TickNumber);
        }

        [Test]
        public void TickZero_Repeated_IsIgnored()
        {
            _producer.OnTick(0u, Held(0f), Vector3.forward);

            _producer.OnTick(0u, Held(0f), Vector3.forward);

            Assert.AreEqual(1, _sink.Count);
        }

        // ---------------------------------------------------------------------------------------
        // BuildMessage / non-unit
        // ---------------------------------------------------------------------------------------

        [Test]
        public void BuildMessage_UnitDirection_EncodesDirectionFacingAndTick()
        {
            MovementIntentMessage message = MovementIntentProducer.BuildMessage(
                new Vector3(BUILD_DIR_X_F, 0f, BUILD_DIR_Z_F), BUILD_FACING_DEG, BUILD_TICK);

            AssertMessage(message, BUILD_DIR_X, BUILD_DIR_Z, BUILD_FACING, BUILD_TICK);
        }

        [Test]
        public void BuildMessage_NonUnitDirection_SendsSubstituteAndLogsOneWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("non-unit"));

            MovementIntentMessage message = MovementIntentProducer.BuildMessage(
                new Vector3(NON_UNIT_COMPONENT, 0f, NON_UNIT_COMPONENT), 0f, FIRST_TICK);

            AssertMessage(message, DIR_FULL, 0, 0, FIRST_TICK);
        }

        // ---------------------------------------------------------------------------------------
        // EC-MOV-15 Reset
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Reset_AfterSends_NextTickSendsAgainEvenWithUnchangedFacing()
        {
            _producer.OnTick(FIRST_TICK, JoystickState.Inactive, Vector3.forward);
            _producer.OnTick(2u, JoystickState.Inactive, Vector3.forward);
            Assert.AreEqual(1, _sink.Count);

            _producer.Reset();
            _producer.OnTick(RESET_TICK, JoystickState.Inactive, Vector3.forward);

            Assert.AreEqual(2, _sink.Count);
            AssertMessage(_sink.Messages[1], 0, 0, 0, RESET_TICK);
        }

        [Test]
        public void Reset_NonZeroFacingUnchanged_NextTickSendsAgain()
        {
            // The last sent facing (900) equals the facing after Reset, so only clearing "has sent" makes tick 3 send.
            _producer.OnTick(FIRST_TICK, JoystickState.Inactive, Vector3.right);
            _producer.OnTick(2u, JoystickState.Inactive, Vector3.right);
            Assert.AreEqual(1, _sink.Count);

            _producer.Reset();
            _producer.OnTick(RESET_TICK, JoystickState.Inactive, Vector3.right);

            Assert.AreEqual(2, _sink.Count);
            AssertMessage(_sink.Messages[1], 0, 0, FACING_RIGHT, RESET_TICK);
        }

        [Test]
        public void Reset_ThenTickZero_IsAccepted()
        {
            _producer.OnTick(REPEATED_TICK, JoystickState.Inactive, Vector3.forward);

            _producer.Reset();
            _producer.OnTick(0u, JoystickState.Inactive, Vector3.forward);

            Assert.AreEqual(2, _sink.Count);
            Assert.AreEqual(0u, _sink.Messages[1].TickNumber);
        }

        [Test]
        public void Reset_AutoRunOn_FlagOffAndSmallerTickAccepted()
        {
            _state.PressButton();
            _producer.OnTick(LATE_TICK, JoystickState.Inactive, Vector3.forward);
            Assert.IsTrue(_state.IsActive);

            _producer.Reset();

            Assert.IsFalse(_state.IsActive);
            _producer.OnTick(RESET_TICK, JoystickState.Inactive, Vector3.forward);
            Assert.AreEqual(2, _sink.Count);
            AssertMessage(_sink.Messages[1], 0, 0, 0, RESET_TICK);
        }

        // ---------------------------------------------------------------------------------------
        // No allocation
        // ---------------------------------------------------------------------------------------

        [Test]
        public void OnTick_100Ticks_AllocateNothing()
        {
            JoystickState joystick = Held(ANGLE_RIGHT);
            Vector3 camera = Vector3.forward;
            uint tick = FIRST_TICK;

            // Warm-up call outside the measured region (JIT, first-use costs).
            _producer.OnTick(tick, joystick, camera);

            Assert.That(() =>
            {
                for (int i = 0; i < ALLOC_ITERATIONS; i++)
                {
                    tick++;
                    _producer.OnTick(tick, joystick, camera);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            Assert.AreEqual(ALLOC_ITERATIONS + 1, _sink.Count);
        }

        [Test]
        public void OnTick_100IdleTicks_SuppressedPathAllocatesNothing()
        {
            uint tick = FIRST_TICK;
            _producer.OnTick(tick, JoystickState.Inactive, Vector3.forward);

            Assert.That(() =>
            {
                for (int i = 0; i < ALLOC_ITERATIONS; i++)
                {
                    tick++;
                    _producer.OnTick(tick, JoystickState.Inactive, Vector3.forward);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());

            Assert.AreEqual(1, _sink.Count);
        }
    }
}
