using System;
using IronGrind.Client.Movement;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace IronGrind.Tests.EditMode.Client.Movement
{
    /// <summary>
    /// EditMode unit tests for Movement Story 013 - the EnhancedTouch to TouchInput mapping, the UI Toolkit hit
    /// test seams and the client/server tick-rate pin.
    /// </summary>
    [TestFixture]
    internal sealed class ClientMovement_TouchSource_Tests
    {
        private const int ID_A = 7;
        private const int ID_B = 8;
        private const int SMALL_BUFFER = 2;
        private const int MORE_THAN_BUFFER = 5;
        private static readonly Vector2 Position = new Vector2(123.5f, 456.25f);

        [TestCase(InputTouchPhase.Began, TouchInputPhase.Began)]
        [TestCase(InputTouchPhase.Moved, TouchInputPhase.Moved)]
        [TestCase(InputTouchPhase.Stationary, TouchInputPhase.Moved)]
        [TestCase(InputTouchPhase.Ended, TouchInputPhase.Ended)]
        [TestCase(InputTouchPhase.Canceled, TouchInputPhase.Canceled)]
        public void test_touch_source_phase_maps_to_expected_phase(InputTouchPhase raw, TouchInputPhase expected)
        {
            bool mapped = EnhancedTouchSource.TryMap(ID_A, raw, Position, out TouchInput result);

            Assert.IsTrue(mapped);
            Assert.AreEqual(expected, result.Phase);
        }

        [Test]
        public void test_touch_source_none_phase_returns_false()
        {
            bool mapped = EnhancedTouchSource.TryMap(ID_A, InputTouchPhase.None, Position, out _);

            Assert.IsFalse(mapped);
        }

        [Test]
        public void test_touch_source_id_comes_from_touch_id()
        {
            var buffer = new TouchInput[SMALL_BUFFER];
            var raw = new[]
            {
                new RawTouch(ID_A, InputTouchPhase.Began, Position),
                new RawTouch(ID_B, InputTouchPhase.Began, Position),
            };

            int count = EnhancedTouchSource.Fill(raw, buffer);

            Assert.AreEqual(SMALL_BUFFER, count);
            Assert.AreEqual(ID_A, buffer[0].Id);
            Assert.AreEqual(ID_B, buffer[1].Id);
        }

        [Test]
        public void test_touch_source_position_passes_through_unchanged()
        {
            bool mapped = EnhancedTouchSource.TryMap(ID_A, InputTouchPhase.Moved, Position, out TouchInput result);

            Assert.IsTrue(mapped);
            Assert.AreEqual(Position, result.Position);
        }

        [Test]
        public void test_touch_source_fill_more_touches_than_buffer_writes_buffer_length()
        {
            var buffer = new TouchInput[SMALL_BUFFER];
            var raw = new RawTouch[MORE_THAN_BUFFER];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = new RawTouch(i, InputTouchPhase.Moved, Position);
            }

            int count = 0;
            Assert.DoesNotThrow(() => count = EnhancedTouchSource.Fill(raw, buffer));

            Assert.AreEqual(buffer.Length, count);
        }

        [Test]
        public void test_touch_source_fill_skips_none_entries_without_counting_them()
        {
            var buffer = new TouchInput[SMALL_BUFFER];
            var raw = new[]
            {
                new RawTouch(ID_A, InputTouchPhase.None, Position),
                new RawTouch(ID_B, InputTouchPhase.Began, Position),
            };

            int count = EnhancedTouchSource.Fill(raw, buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(ID_B, buffer[0].Id);
        }

        [Test]
        public void test_client_movement_tick_rate_equals_server_tick_rate()
        {
            Assert.AreEqual(ServerTickLoop.TICK_RATE_HZ, ClientMovementController.CLIENT_TICK_RATE_HZ);
        }

        [Test]
        public void test_ui_toolkit_hit_test_without_panel_returns_false()
        {
            var hitTest = new UiToolkitHitTest(new VisualElement());

            Assert.IsFalse(hitTest.IsOnSkillButton(Position));
        }

        [Test]
        public void test_ui_toolkit_hit_test_element_with_blocker_class_is_blocker()
        {
            var element = new VisualElement();
            element.AddToClassList(UiToolkitHitTest.TOUCH_BLOCKER_CLASS);

            Assert.IsTrue(UiToolkitHitTest.HasBlockerInAncestry(element));
        }

        [Test]
        public void test_ui_toolkit_hit_test_child_of_blocker_is_blocker()
        {
            var parent = new VisualElement();
            parent.AddToClassList(UiToolkitHitTest.TOUCH_BLOCKER_CLASS);
            var child = new VisualElement();
            parent.Add(child);

            Assert.IsTrue(UiToolkitHitTest.HasBlockerInAncestry(child));
        }

        [Test]
        public void test_ui_toolkit_hit_test_no_blocker_in_ancestry_returns_false()
        {
            var parent = new VisualElement();
            var child = new VisualElement();
            parent.Add(child);

            Assert.IsFalse(UiToolkitHitTest.HasBlockerInAncestry(child));
            Assert.IsFalse(UiToolkitHitTest.HasBlockerInAncestry(null));
        }
    }
}
