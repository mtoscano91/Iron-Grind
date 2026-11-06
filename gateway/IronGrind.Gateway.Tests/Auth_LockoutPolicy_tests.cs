using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 005: F-AUTH-1 lockout lookup (AC-AUTH-18).</summary>
[TestFixture]
internal sealed class Auth_LockoutPolicy_tests
{
    private static readonly DateTime NOW = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(4, 0)]
    [TestCase(5, 30)]
    [TestCase(6, 60)]
    [TestCase(7, 120)]
    [TestCase(8, 300)]
    [TestCase(9, 600)]
    [TestCase(15, 600)]
    [TestCase(100, 600)]
    public void test_duration_default_table_returns_listed_seconds(int failures, int expected)
    {
        var policy = new LockoutPolicy(new AuthOptions());

        Assert.That(policy.Duration(failures), Is.EqualTo(expected));
    }

    [Test]
    public void test_lockout_until_seven_failures_adds_120_seconds()
    {
        var policy = new LockoutPolicy(new AuthOptions());

        Assert.That(policy.LockoutUntil(7, NOW), Is.EqualTo(NOW.AddSeconds(120)));
    }

    [Test]
    public void test_lockout_until_below_limit_returns_null()
    {
        var policy = new LockoutPolicy(new AuthOptions());

        Assert.That(policy.LockoutUntil(3, NOW), Is.Null);
    }

    [TestCase(2, 0)]
    [TestCase(3, 10)]
    [TestCase(4, 20)]
    [TestCase(50, 20)]
    public void test_duration_custom_options_reads_knobs(int failures, int expected)
    {
        var policy = new LockoutPolicy(new AuthOptions { LoginFailureLimit = 3, LockoutTableSeconds = new[] { 10, 20 } });

        Assert.That(policy.Duration(failures), Is.EqualTo(expected));
    }
}
