using System;
using IronGrind.Networking;
using NUnit.Framework;
using Unity.Netcode;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 039 — the channel-to-delivery table and the constructor of
    /// <see cref="NgoClientMessageSender"/> (ADR-004 Decision 2 and Amendment 1). No <c>NetworkManager</c> is
    /// created: the table is a static method and the constructor is checked with null only. The send itself is
    /// run in PlayMode (<c>ClientMessageOutbox_ngo_run_tests.cs</c>).
    /// </summary>
    [TestFixture]
    internal sealed class NgoClientMessageSender_Tests
    {
        private const NetworkChannel UndefinedChannel = (NetworkChannel)99;

        [Test]
        public void test_mapping_reliable_ordered_is_reliable_sequenced()
        {
            Assert.AreEqual(NetworkDelivery.ReliableSequenced, NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.ReliableOrdered));
        }

        [Test]
        public void test_mapping_reliable_unordered_is_reliable()
        {
            Assert.AreEqual(NetworkDelivery.Reliable, NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.ReliableUnordered));
        }

        [Test]
        public void test_mapping_unreliable_is_unreliable()
        {
            Assert.AreEqual(NetworkDelivery.Unreliable, NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.Unreliable));
        }

        [Test]
        public void test_mapping_undefined_channel_throws_argument_out_of_range()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NgoClientMessageSender.ToNetworkDelivery(UndefinedChannel));
        }

        [Test]
        public void test_mapping_constructor_rejects_null_manager()
        {
            Assert.Throws<ArgumentNullException>(() => new NgoClientMessageSender(null));
        }
    }
}
