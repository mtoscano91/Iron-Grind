using System;
using System.Collections.Generic;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// Gateway Story 012: <see cref="ClientConnectionListenerFanOut"/> calls every listener in order, runs the ones after
    /// a throwing listener, and rethrows the first exception after all ran.
    /// </summary>
    [TestFixture]
    internal sealed class ClientConnectionListenerFanOut_Tests
    {
        private const uint CLIENT = 77u;

        private sealed class OrderedListener : IClientConnectionListener
        {
            private readonly string _name;
            private readonly List<string> _calls;
            private readonly Exception _toThrow;

            public OrderedListener(string name, List<string> calls, Exception toThrow = null)
            {
                _name = name;
                _calls = calls;
                _toThrow = toThrow;
            }

            public void AddClient(uint clientId)
            {
                _calls.Add(_name + ":add:" + clientId);
                if (_toThrow != null)
                {
                    throw _toThrow;
                }
            }

            public void RemoveClient(uint clientId)
            {
                _calls.Add(_name + ":remove:" + clientId);
                if (_toThrow != null)
                {
                    throw _toThrow;
                }
            }
        }

        [Test]
        public void test_fan_out_calls_listeners_in_order_on_add_and_on_remove()
        {
            var calls = new List<string>();
            var fanOut = new ClientConnectionListenerFanOut(
                new OrderedListener("a", calls), new OrderedListener("b", calls), new OrderedListener("c", calls));

            fanOut.AddClient(CLIENT);
            fanOut.RemoveClient(CLIENT);

            CollectionAssert.AreEqual(
                new[] { "a:add:77", "b:add:77", "c:add:77", "a:remove:77", "b:remove:77", "c:remove:77" }, calls);
        }

        [Test]
        public void test_fan_out_add_runs_every_listener_when_some_throw_and_rethrows_the_first_exception()
        {
            var calls = new List<string>();
            var errorA = new InvalidOperationException("A");
            var errorB = new InvalidOperationException("B");
            var fanOut = new ClientConnectionListenerFanOut(
                new OrderedListener("ok1", calls), new OrderedListener("a", calls, errorA),
                new OrderedListener("ok2", calls), new OrderedListener("b", calls, errorB));

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => fanOut.AddClient(CLIENT));

            Assert.AreEqual("A", thrown.Message);
            CollectionAssert.AreEqual(new[] { "ok1:add:77", "a:add:77", "ok2:add:77", "b:add:77" }, calls);
        }

        [Test]
        public void test_fan_out_remove_runs_every_listener_when_some_throw_and_rethrows_the_first_exception()
        {
            var calls = new List<string>();
            var errorA = new InvalidOperationException("A");
            var errorB = new InvalidOperationException("B");
            var fanOut = new ClientConnectionListenerFanOut(
                new OrderedListener("ok1", calls), new OrderedListener("a", calls, errorA),
                new OrderedListener("ok2", calls), new OrderedListener("b", calls, errorB));

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => fanOut.RemoveClient(CLIENT));

            Assert.AreEqual("A", thrown.Message);
            CollectionAssert.AreEqual(new[] { "ok1:remove:77", "a:remove:77", "ok2:remove:77", "b:remove:77" }, calls);
        }

        [Test]
        public void test_fan_out_rejects_a_null_array_and_a_null_element()
        {
            var calls = new List<string>();

            Assert.Throws<ArgumentNullException>(() => new ClientConnectionListenerFanOut((IClientConnectionListener[])null));
            Assert.Throws<ArgumentNullException>(() => new ClientConnectionListenerFanOut(
                new OrderedListener("a", calls), null));
        }

        [Test]
        public void test_fan_out_ignores_a_change_to_the_source_array_after_construction()
        {
            var calls = new List<string>();
            var source = new IClientConnectionListener[] { new OrderedListener("a", calls), new OrderedListener("b", calls) };
            var fanOut = new ClientConnectionListenerFanOut(source);

            source[0] = new OrderedListener("x", calls);
            fanOut.AddClient(CLIENT);

            CollectionAssert.AreEqual(new[] { "a:add:77", "b:add:77" }, calls);
        }

        [Test]
        public void test_fan_out_listeners_returns_the_listeners_in_call_order()
        {
            var calls = new List<string>();
            var first = new OrderedListener("a", calls);
            var second = new OrderedListener("b", calls);
            var fanOut = new ClientConnectionListenerFanOut(first, second);

            Assert.AreEqual(2, fanOut.Listeners.Count);
            Assert.AreSame(first, fanOut.Listeners[0]);
            Assert.AreSame(second, fanOut.Listeners[1]);
        }
    }
}
