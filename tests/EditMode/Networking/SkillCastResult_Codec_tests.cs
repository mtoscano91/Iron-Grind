using System;
using IronGrind.Networking;
using IronGrind.SkillSystem;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode tests for Skill System Story 009 part A: the <see cref="SkillCastResult"/> and
    /// <see cref="SkillCooldownUpdate"/> wire types and their body codecs.
    /// </summary>
    [TestFixture]
    internal sealed class SkillCastResult_Codec_Tests
    {
        private static SkillCastResult SampleAccepted()
            => SkillCastResult.Accepted(7u, 1001u, 2001u, damageDealt: -120, healAmount: 45, isCrit: true, isKill: false, newCooldownTicks: 40);

        [Test]
        public void test_skill_cast_result_ids_and_sizes_match_the_wire_spec()
        {
            Assert.AreEqual(0x0309, SkillCastResult.MessageTypeId);
            Assert.AreEqual(28, SkillCastResult.BodySize);
            Assert.AreEqual(32, SkillCastResult.BatchSize);
            Assert.AreEqual(0x030A, SkillCooldownUpdate.MessageTypeId);
            Assert.AreEqual(8, SkillCooldownUpdate.BodySize);
            Assert.AreEqual(12, SkillCooldownUpdate.BatchSize);
        }

        [Test]
        public void test_skill_cast_result_accepted_round_trips_every_field()
        {
            // Arrange
            var original = new SkillCastResult(uint.MaxValue, 0x01020304u, 0xA0B0C0D0u, true, 0, int.MinValue, int.MaxValue, true, true, -5);
            byte[] body = new byte[SkillCastResult.BodySize];

            // Act
            int written = SkillCastResultCodec.WriteBody(body, in original);
            bool ok = SkillCastResultCodec.TryReadBody(body, out SkillCastResult decoded);

            // Assert
            Assert.AreEqual(SkillCastResult.BodySize, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(uint.MaxValue, decoded.SkillId);
            Assert.AreEqual(0x01020304u, decoded.CasterId);
            Assert.AreEqual(0xA0B0C0D0u, decoded.TargetId);
            Assert.IsTrue(decoded.CastAccepted);
            Assert.AreEqual(0, decoded.RejectionCode);
            Assert.AreEqual(int.MinValue, decoded.DamageDealt);
            Assert.AreEqual(int.MaxValue, decoded.HealAmount);
            Assert.IsTrue(decoded.IsCrit);
            Assert.IsTrue(decoded.IsKill);
            Assert.AreEqual(-5, decoded.NewCooldownTicks);
        }

        [Test]
        public void test_skill_cast_result_rejected_round_trips_with_the_code()
        {
            // Arrange
            SkillCastResult original = SkillCastResult.Rejected(7u, 1001u, 2001u, SkillCastRejectionCode.OnCooldown);
            byte[] body = new byte[SkillCastResult.BodySize];

            // Act
            SkillCastResultCodec.WriteBody(body, in original);
            bool ok = SkillCastResultCodec.TryReadBody(body, out SkillCastResult decoded);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(original, decoded);
            Assert.IsFalse(decoded.CastAccepted);
            Assert.AreEqual((byte)SkillCastRejectionCode.OnCooldown, decoded.RejectionCode);
        }

        [Test]
        public void test_skill_cast_result_write_produces_the_expected_little_endian_layout()
        {
            // Arrange
            SkillCastResult value = SkillCastResult.Accepted(7u, 1001u, 2001u, damageDealt: 300, healAmount: -2, isCrit: true, isKill: false, newCooldownTicks: 40);
            byte[] expected =
            {
                0x07, 0x00, 0x00, 0x00, // skillID 7 (offset 0)
                0xE9, 0x03, 0x00, 0x00, // casterID 1001 (4)
                0xD1, 0x07, 0x00, 0x00, // targetID 2001 (8)
                0x01,                   // castAccepted (12)
                0x00,                   // rejectionCode (13)
                0x2C, 0x01, 0x00, 0x00, // damageDealt 300 (14)
                0xFE, 0xFF, 0xFF, 0xFF, // healAmount -2 (18)
                0x01,                   // isCrit (22)
                0x00,                   // isKill (23)
                0x28, 0x00, 0x00, 0x00, // newCooldownTicks 40 (24)
            };
            byte[] actual = new byte[SkillCastResult.BodySize];

            // Act
            SkillCastResultCodec.WriteBody(actual, in value);

            // Assert
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void test_skill_cast_result_rejected_factory_zeroes_damage_heal_crit_kill_and_cooldown()
        {
            // Arrange / Act
            SkillCastResult r = SkillCastResult.Rejected(7u, 1001u, 2001u, (byte)6);

            // Assert
            Assert.IsFalse(r.CastAccepted);
            Assert.AreEqual(6, r.RejectionCode);
            Assert.AreEqual(0, r.DamageDealt);
            Assert.AreEqual(0, r.HealAmount);
            Assert.IsFalse(r.IsCrit);
            Assert.IsFalse(r.IsKill);
            Assert.AreEqual(0, r.NewCooldownTicks);
            Assert.AreEqual(7u, r.SkillId);
            Assert.AreEqual(1001u, r.CasterId);
            Assert.AreEqual(2001u, r.TargetId);
        }

        [Test]
        public void test_skill_cast_result_accepted_factory_sets_accepted_with_code_zero()
        {
            // Arrange / Act
            SkillCastResult r = SampleAccepted();

            // Assert
            Assert.IsTrue(r.CastAccepted);
            Assert.AreEqual(0, r.RejectionCode);
            Assert.AreEqual(-120, r.DamageDealt);
            Assert.AreEqual(45, r.HealAmount);
            Assert.IsTrue(r.IsCrit);
            Assert.IsFalse(r.IsKill);
            Assert.AreEqual(40, r.NewCooldownTicks);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(27)]
        [TestCase(29)]
        [TestCase(32)]
        public void test_skill_cast_result_wrong_body_length_returns_false(int length)
        {
            // Arrange
            byte[] body = new byte[length];

            // Act
            bool ok = SkillCastResultCodec.TryReadBody(body, out SkillCastResult decoded);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(default(SkillCastResult), decoded);
        }

        [Test]
        public void test_skill_cast_result_write_into_short_buffer_throws()
        {
            // Arrange
            SkillCastResult value = SampleAccepted();
            byte[] shortBuffer = new byte[SkillCastResult.BodySize - 1];

            // Act / Assert
            Assert.Throws<ArgumentException>(() => SkillCastResultCodec.WriteBody(shortBuffer, in value));
        }

        [Test]
        public void test_skill_cast_result_equality_distinguishes_each_field()
        {
            // Arrange
            SkillCastResult a = SampleAccepted();
            SkillCastResult sameAsA = SampleAccepted();
            SkillCastResult differentKill = SkillCastResult.Accepted(7u, 1001u, 2001u, -120, 45, true, true, 40);

            // Assert
            Assert.IsTrue(a == sameAsA);
            Assert.AreEqual(a.GetHashCode(), sameAsA.GetHashCode());
            Assert.IsTrue(a != differentKill);
        }

        [Test]
        public void test_skill_cooldown_update_round_trips_every_field()
        {
            // Arrange
            var original = new SkillCooldownUpdate(0xDEADBEEFu, uint.MaxValue);
            byte[] body = new byte[SkillCooldownUpdate.BodySize];

            // Act
            int written = SkillCooldownUpdateCodec.WriteBody(body, in original);
            bool ok = SkillCooldownUpdateCodec.TryReadBody(body, out SkillCooldownUpdate decoded);

            // Assert
            Assert.AreEqual(SkillCooldownUpdate.BodySize, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(0xDEADBEEFu, decoded.SkillId);
            Assert.AreEqual(uint.MaxValue, decoded.CooldownExpiryTick);
        }

        [Test]
        public void test_skill_cooldown_update_write_produces_the_expected_little_endian_layout()
        {
            // Arrange
            var value = new SkillCooldownUpdate(7u, 1000u);
            byte[] expected = { 0x07, 0x00, 0x00, 0x00, 0xE8, 0x03, 0x00, 0x00 };
            byte[] actual = new byte[SkillCooldownUpdate.BodySize];

            // Act
            SkillCooldownUpdateCodec.WriteBody(actual, in value);

            // Assert
            CollectionAssert.AreEqual(expected, actual);
        }

        [TestCase(0)]
        [TestCase(7)]
        [TestCase(9)]
        [TestCase(12)]
        public void test_skill_cooldown_update_wrong_body_length_returns_false(int length)
        {
            // Arrange
            byte[] body = new byte[length];

            // Act
            bool ok = SkillCooldownUpdateCodec.TryReadBody(body, out SkillCooldownUpdate decoded);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(default(SkillCooldownUpdate), decoded);
        }

        [Test]
        public void test_skill_cooldown_update_write_into_short_buffer_throws()
        {
            // Arrange
            var value = new SkillCooldownUpdate(1u, 2u);
            byte[] shortBuffer = new byte[SkillCooldownUpdate.BodySize - 1];

            // Act / Assert
            Assert.Throws<ArgumentException>(() => SkillCooldownUpdateCodec.WriteBody(shortBuffer, in value));
        }
    }
}
