using System;
using System.Collections.Generic;
using System.Linq;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Client.Session;
using IronGrind.Currency;
using IronGrind.DevHarness.Session;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.DevHarness
{
    /// <summary>
    /// EditMode tests for the Story 018 login load driver (its numbers feed Story 017's AC-AUTH-05a evidence):
    /// slot usernames, slot reuse, no launches after D, completion and the median in the summary line.
    /// </summary>
    [TestFixture]
    internal sealed class LoginLoadDriver_Tests
    {
        private const string PREFIX = "load";
        private const string PASSWORD = "hunter22";
        private const double DURATION = 1.0;

        private static readonly byte[] Token = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        private sealed class FakeLogin : IZoneLoginService
        {
            public readonly List<string> Users = new List<string>();
            public readonly List<string> Passwords = new List<string>();
            private readonly Dictionary<string, Action<LoginResponse>> _pending = new Dictionary<string, Action<LoginResponse>>();

            public void Login(string username, string password, byte[] token, Action<LoginResponse> onDone)
            {
                Users.Add(username);
                Passwords.Add(password);
                _pending[username] = onDone;
            }

            public void Complete(string username, bool success)
            {
                Action<LoginResponse> callback = _pending[username];
                _pending.Remove(username);
                callback(success
                    ? LoginResponse.ForSuccess((byte[])Token.Clone(), new CharacterID(1u), new ZoneID(1u), 7777)
                    : LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials));
            }
        }

        private sealed class FakeClock : IClientSessionClock
        {
            public double Now { get; set; }
        }

        private FakeLogin _login;
        private FakeClock _clock;
        private List<string> _lines;

        [SetUp]
        public void SetUp()
        {
            _login = new FakeLogin();
            _clock = new FakeClock();
            _lines = new List<string>();
        }

        private LoginLoadDriver NewDriver(int concurrency)
        {
            return new LoginLoadDriver(_login, _clock, _lines.Add, PREFIX, PASSWORD, concurrency, DURATION);
        }

        private static string User(int slot)
        {
            return PREFIX + "_" + slot;
        }

        private string Summary()
        {
            return _lines.Single(l => l.Contains(" summary "));
        }

        [Test]
        public void test_load_driver_update_before_start_launches_nothing()
        {
            LoginLoadDriver driver = NewDriver(3);

            driver.Update();

            Assert.AreEqual(0, _login.Users.Count);
            Assert.IsFalse(driver.IsFinished);
        }

        [Test]
        public void test_load_driver_first_update_launches_one_login_per_slot_with_slot_usernames()
        {
            LoginLoadDriver driver = NewDriver(3);
            driver.Start();

            driver.Update();
            driver.Update();

            CollectionAssert.AreEqual(new[] { User(0), User(1), User(2) }, _login.Users);
            Assert.IsTrue(_login.Passwords.All(p => p == PASSWORD));
        }

        [Test]
        public void test_load_driver_finished_slot_is_reused_for_the_same_username()
        {
            LoginLoadDriver driver = NewDriver(3);
            driver.Start();
            driver.Update();

            _clock.Now = 0.25;
            _login.Complete(User(1), true);
            driver.Update();

            CollectionAssert.AreEqual(new[] { User(0), User(1), User(2), User(1) }, _login.Users);
            Assert.IsTrue(_lines.Contains("[LoginLoad] i=0 ms=250.0 ok=true"));
        }

        [Test]
        public void test_load_driver_launches_nothing_after_duration_and_finishes_when_all_return()
        {
            LoginLoadDriver driver = NewDriver(2);
            driver.Start();
            driver.Update();

            _clock.Now = DURATION;
            _login.Complete(User(0), true);
            driver.Update();
            Assert.AreEqual(2, _login.Users.Count);
            Assert.IsFalse(driver.IsFinished);

            _login.Complete(User(1), false);
            driver.Update();

            Assert.AreEqual(2, _login.Users.Count);
            Assert.IsTrue(driver.IsFinished);
            StringAssert.Contains("count=2 ok=1", Summary());
        }

        [Test]
        public void test_load_driver_summary_median_of_odd_count_is_the_middle_latency()
        {
            LoginLoadDriver driver = NewDriver(3);
            driver.Start();
            driver.Update();

            _clock.Now = 0.1;
            _login.Complete(User(0), true);
            _clock.Now = 0.2;
            _login.Complete(User(2), true);
            _clock.Now = 0.3;
            _login.Complete(User(1), true);
            _clock.Now = DURATION;
            driver.Update();

            Assert.IsTrue(driver.IsFinished);
            StringAssert.Contains("min=100.0 median=200.0 max=300.0 ms", Summary());
        }

        [Test]
        public void test_load_driver_summary_median_of_even_count_is_the_mean_of_the_two_middle_latencies()
        {
            LoginLoadDriver driver = NewDriver(4);
            driver.Start();
            driver.Update();

            for (int slot = 0; slot < 4; slot++)
            {
                _clock.Now = 0.1 * (slot + 1);
                _login.Complete(User(slot), true);
            }

            _clock.Now = DURATION;
            driver.Update();

            StringAssert.Contains("min=100.0 median=250.0 max=400.0 ms", Summary());
        }

        [Test]
        public void test_load_driver_rejects_non_positive_concurrency_and_duration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NewDriver(0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new LoginLoadDriver(_login, _clock, null, PREFIX, PASSWORD, 1, 0.0));
        }

        [Test]
        public void test_load_driver_never_logs_the_password()
        {
            LoginLoadDriver driver = NewDriver(1);
            driver.Start();
            driver.Update();
            _login.Complete(User(0), true);
            _clock.Now = DURATION;
            driver.Update();

            Assert.IsFalse(_lines.Any(l => l.Contains(PASSWORD)));
        }
    }
}
