using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// Death and Respawn Story 008: <see cref="ForwardingPlayerDeathStateReader"/> (the late-bound reader the zone
    /// instancing wiring is built with) and the <see cref="DeathAndRespawnPhaseStep"/> constructor contract.
    /// </summary>
    [TestFixture]
    internal sealed class DeathAndRespawn_ForwardingReader_Tests
    {
        private const uint DEAD_ENTITY = 7u;
        private const uint OTHER_ENTITY = 8u;
        private const long RESPAWN_TICK = 1234L;

        private sealed class StubReader : IPlayerDeathStateReader
        {
            public readonly EntityID DeadId;
            public readonly long RespawnTick;

            public StubReader(EntityID deadId, long respawnTick)
            {
                DeadId = deadId;
                RespawnTick = respawnTick;
            }

            public bool IsDead(EntityID id) => id == DeadId;

            public bool TryGetRespawnTick(EntityID id, out long respawnTick)
            {
                respawnTick = id == DeadId ? RespawnTick : 0L;
                return id == DeadId;
            }
        }

        [Test]
        public void test_forwarding_reader_unbound_reports_nobody_dead_and_no_respawn_tick()
        {
            // Arrange
            var reader = new ForwardingPlayerDeathStateReader();

            // Act
            bool dead = reader.IsDead(new EntityID(DEAD_ENTITY));
            bool hasTick = reader.TryGetRespawnTick(new EntityID(DEAD_ENTITY), out long tick);

            // Assert
            Assert.IsFalse(reader.IsBound);
            Assert.IsFalse(dead);
            Assert.IsFalse(hasTick);
            Assert.AreEqual(0L, tick);
        }

        [Test]
        public void test_forwarding_reader_bound_forwards_is_dead_and_the_respawn_tick()
        {
            // Arrange
            var reader = new ForwardingPlayerDeathStateReader();
            reader.Bind(new StubReader(new EntityID(DEAD_ENTITY), RESPAWN_TICK));

            // Act
            bool deadIsDead = reader.IsDead(new EntityID(DEAD_ENTITY));
            bool otherIsDead = reader.IsDead(new EntityID(OTHER_ENTITY));
            bool hasTick = reader.TryGetRespawnTick(new EntityID(DEAD_ENTITY), out long tick);
            bool otherHasTick = reader.TryGetRespawnTick(new EntityID(OTHER_ENTITY), out long otherTick);

            // Assert
            Assert.IsTrue(reader.IsBound);
            Assert.IsTrue(deadIsDead);
            Assert.IsFalse(otherIsDead);
            Assert.IsTrue(hasTick);
            Assert.AreEqual(RESPAWN_TICK, tick);
            Assert.IsFalse(otherHasTick);
            Assert.AreEqual(0L, otherTick);
        }

        [Test]
        public void test_forwarding_reader_bind_null_throws_argument_null_and_stays_unbound()
        {
            // Arrange
            var reader = new ForwardingPlayerDeathStateReader();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => reader.Bind(null));
            Assert.IsFalse(reader.IsBound);
        }

        [Test]
        public void test_forwarding_reader_bind_twice_throws_invalid_operation_and_keeps_the_first_target()
        {
            // Arrange
            var reader = new ForwardingPlayerDeathStateReader();
            reader.Bind(new StubReader(new EntityID(DEAD_ENTITY), RESPAWN_TICK));

            // Act
            Assert.Throws<InvalidOperationException>(
                () => reader.Bind(new StubReader(new EntityID(OTHER_ENTITY), RESPAWN_TICK)));

            // Assert
            Assert.IsTrue(reader.IsDead(new EntityID(DEAD_ENTITY)), "the first target is kept");
            Assert.IsFalse(reader.IsDead(new EntityID(OTHER_ENTITY)), "the second target was not bound");
        }

        [Test]
        public void test_death_phase_step_null_service_throws_argument_null()
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentNullException>(() => new DeathAndRespawnPhaseStep(null));
        }
    }
}
