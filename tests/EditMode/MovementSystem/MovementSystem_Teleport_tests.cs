using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode tests for the authoritative position set (Death and Respawn Story 004 AC-4, CR-DR-10):
    /// <see cref="IMovementTeleport.TrySetAuthoritativePosition"/> on <see cref="MovementService"/>.
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_Teleport_Tests
    {
        private const float TOLERANCE = 0.1f;
        private const int SLOT = 3;
        private const uint PLAYER_RAW = 11u;
        private const uint UNKNOWN_RAW = 99u;
        private const short FACING = 450;
        private const short FULL = 32767;

        private static readonly Vector3 Start = new Vector3(1f, 0f, 2f);
        private static readonly Vector3 Target = new Vector3(12f, 0f, -7f);

        private sealed class FakeIntents : IMovementIntentSource
        {
            public bool Has;
            public bool Sticky;

            public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
            {
                intent = Has ? new MovementIntentMessage(FULL, 0, 0, 0u) : default;
                bool had = Has;
                if (!Sticky)
                    Has = false;
                return had;
            }
        }

        private sealed class FakeSpeeds : IMovementSpeedSource
        {
            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                speed = 5f;
                return true;
            }
        }

        private sealed class FakeSampler : INavMeshSampler
        {
            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
            {
                hit = candidate;
                return true;
            }
        }

        private sealed class FakeIndex : IEntitySlotIndex
        {
            private readonly Dictionary<uint, int> _map = new Dictionary<uint, int>();

            public void Map(EntityID id, int slot) { _map[id.RawValue] = slot; }

            public bool TryGetSlotIndex(EntityID id, out int index) { return _map.TryGetValue(id.RawValue, out index); }
        }

        private FakeIntents _intents;
        private FakeIndex _index;
        private MovementService _service;
        private EntityID _player;

        [SetUp]
        public void SetUp()
        {
            _intents = new FakeIntents();
            _index = new FakeIndex();
            _service = new MovementService(_intents, new FakeSpeeds(), new FakeSampler(), _index);
            _player = new EntityID(PLAYER_RAW);
            _index.Map(_player, SLOT);
            _service.SeedForTesting(SLOT, _player, Start, FACING);
        }

        [Test]
        public void test_teleport_registered_player_position_reads_back_and_returns_true()
        {
            bool result = _service.TrySetAuthoritativePosition(_player, Target);

            Assert.IsTrue(result);
            Assert.IsTrue(_service.TryGetPosition(_player, out Vector3 position));
            Assert.Less(Vector3.Distance(Target, position), TOLERANCE);
        }

        [Test]
        public void test_teleport_pending_intent_is_dropped()
        {
            _intents.Has = true;

            _service.TrySetAuthoritativePosition(_player, Target);

            Assert.IsFalse(_intents.Has);
        }

        [Test]
        public void test_teleport_leaves_facing_and_occupancy_unchanged()
        {
            _service.TrySetAuthoritativePosition(_player, Target);

            Assert.IsTrue(_service.TryGetFacing(_player, out short facing));
            Assert.AreEqual(FACING, facing);
            Assert.IsTrue(_service.TryGetPosition(_player, out _));
        }

        [Test]
        public void test_teleport_unknown_id_returns_false_and_changes_nothing()
        {
            var unknown = new EntityID(UNKNOWN_RAW);

            bool result = _service.TrySetAuthoritativePosition(unknown, Target);

            Assert.IsFalse(result);
            Assert.IsTrue(_service.TryGetPosition(_player, out Vector3 position));
            Assert.AreEqual(Start, position);
        }

        [Test]
        public void test_teleport_clears_prohibited_so_movement_resumes()
        {
            Assert.IsTrue(_service.SetProhibited(_player, true));
            _service.TrySetAuthoritativePosition(_player, Target);
            _intents.Sticky = true;
            _intents.Has = true;

            _service.Run(1u, ServerTickLoop.FIXED_DELTA_TIME);

            Assert.IsTrue(_service.TryGetPosition(_player, out Vector3 position));
            Assert.Greater(position.x, Target.x + TOLERANCE);
        }
    }
}
