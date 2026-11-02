using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// <see cref="ZoneEntityIdView"/> and <see cref="TargetSlotTracker.Clear"/> (networking-relevance-filter.md RFR-3a;
    /// Auto-Attack Combat Story 006 code review).
    /// </summary>
    [TestFixture]
    internal sealed class TargetSlotTracker_Clear_Tests
    {
        private const uint PLAYER_RAW = 101u;
        private const uint MOB_RAW = 202u;
        private const uint UNKNOWN_RAW = 303u;
        private const uint LATE_RAW = 404u;
        private const uint CLIENT = 7u;
        private const uint OWN_RAW = 505u;

        [Test]
        public void test_zone_entity_id_view_contains_a_player_and_a_mob_but_not_an_unknown_id()
        {
            var view = new ZoneEntityIdView(new List<EntityID> { new EntityID(PLAYER_RAW) }, new List<EntityID> { new EntityID(MOB_RAW) });

            Assert.IsTrue(view.Contains(new EntityID(PLAYER_RAW)));
            Assert.IsTrue(view.Contains(new EntityID(MOB_RAW)));
            Assert.IsFalse(view.Contains(new EntityID(UNKNOWN_RAW)));
            Assert.AreEqual(2, view.Count);
        }

        [Test]
        public void test_zone_entity_id_view_reflects_a_later_allocation_in_the_live_lists()
        {
            var players = new List<EntityID>();
            var mobs = new List<EntityID>();
            var view = new ZoneEntityIdView(players, mobs);
            Assert.IsFalse(view.Contains(new EntityID(LATE_RAW)));

            mobs.Add(new EntityID(LATE_RAW));

            Assert.IsTrue(view.Contains(new EntityID(LATE_RAW)));
        }

        [Test]
        public void test_tracker_accepts_a_non_self_player_present_in_the_view()
        {
            var tracker = new TargetSlotTracker();
            var view = new ZoneEntityIdView(new List<EntityID> { new EntityID(PLAYER_RAW) }, new List<EntityID>());

            SetTargetOutcome outcome = tracker.ProcessSetTarget(CLIENT, new EntityID(OWN_RAW), new EntityID(PLAYER_RAW), view);

            Assert.AreEqual(SetTargetOutcome.Accepted, outcome);
            Assert.AreEqual(new EntityID(PLAYER_RAW), tracker.GetTarget(CLIENT));
        }

        [Test]
        public void test_clear_removes_the_clients_target_entry()
        {
            var tracker = new TargetSlotTracker();
            var view = new ZoneEntityIdView(new List<EntityID>(), new List<EntityID> { new EntityID(MOB_RAW) });
            tracker.ProcessSetTarget(CLIENT, new EntityID(OWN_RAW), new EntityID(MOB_RAW), view);

            tracker.Clear(CLIENT);

            Assert.AreEqual(EntityID.Invalid, tracker.GetTarget(CLIENT));
        }

        [Test]
        public void test_clear_for_an_unknown_client_is_a_no_op()
        {
            var tracker = new TargetSlotTracker();

            Assert.DoesNotThrow(() => tracker.Clear(CLIENT));
            Assert.AreEqual(EntityID.Invalid, tracker.GetTarget(CLIENT));
        }
    }
}
