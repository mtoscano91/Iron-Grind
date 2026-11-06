using System.Diagnostics;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 004: Argon2id hasher (AC-AUTH-06, AC-AUTH-07), malformed PHC handling,
/// the dummy hash (CR-AUTH-9 step 4) and the hash-time benchmark (explicit).
/// </summary>
[TestFixture]
internal sealed class Auth_PasswordHasher_tests
{
    private const string PASSWORD = "correct horse";
    private const string DEFAULT_PREFIX = "$argon2id$v=19$m=19456,t=2,p=1$";
    private const int SALT_BYTES = 16;
    private const int HASH_COUNT_FOR_SALTS = 5;
    private const int BENCHMARK_RUNS = 20;

    private static Argon2idPasswordHasher MakeHasher(int iterations = 2)
    {
        return new Argon2idPasswordHasher(new AuthOptions { Argon2Iterations = iterations });
    }

    private static byte[] DecodeNoPad(string text)
    {
        return Convert.FromBase64String(text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '='));
    }

    [Test]
    public void test_hash_default_options_emits_phc_with_16_byte_distinct_salts()
    {
        var hasher = MakeHasher();
        var salts = new HashSet<string>();

        for (int i = 0; i < HASH_COUNT_FOR_SALTS; i++)
        {
            string phc = hasher.Hash(PASSWORD);

            Assert.That(phc, Does.StartWith(DEFAULT_PREFIX));
            string[] parts = phc.Split('$');
            Assert.That(parts, Has.Length.EqualTo(6));
            Assert.That(parts[4], Does.Not.Contain("="));
            Assert.That(parts[5], Does.Not.Contain("="));
            Assert.That(DecodeNoPad(parts[4]), Has.Length.EqualTo(SALT_BYTES));
            Assert.That(DecodeNoPad(parts[5]), Has.Length.EqualTo(32));
            salts.Add(parts[4]);
        }

        Assert.That(salts, Has.Count.EqualTo(HASH_COUNT_FOR_SALTS));
    }

    [Test]
    public void test_verify_round_trip_matches_correct_password_only()
    {
        var hasher = MakeHasher();
        string phc = hasher.Hash(PASSWORD);

        Assert.That(hasher.Verify(PASSWORD, phc), Is.True);
        Assert.That(hasher.Verify("correct hors", phc), Is.False);
    }

    [Test]
    public void test_verify_72_char_password_is_not_truncated()
    {
        var hasher = MakeHasher();
        string first = new string('a', 72);
        string second = first + "b";
        string phc = hasher.Hash(first);

        Assert.That(hasher.Verify(first, phc), Is.True);
        Assert.That(hasher.Verify(second, phc), Is.False);
    }

    [Test]
    public void test_verify_multibyte_password_round_trips()
    {
        var hasher = MakeHasher();
        string phc = hasher.Hash("пароль123");

        Assert.That(hasher.Verify("пароль123", phc), Is.True);
    }

    [Test]
    public void test_verify_older_parameter_hash_still_verifies_with_current_options()
    {
        string oldPhc = MakeHasher(iterations: 3).Hash(PASSWORD);

        Assert.That(oldPhc, Does.Contain("t=3"));
        Assert.That(MakeHasher().Verify(PASSWORD, oldPhc), Is.True);
    }

    [Test]
    public void test_verify_null_password_throws_argument_null()
    {
        var hasher = MakeHasher();

        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
        Assert.Throws<ArgumentNullException>(() => hasher.Verify(null!, DEFAULT_PREFIX));
    }

    [Test]
    public void test_verify_malformed_phc_returns_false_without_throwing()
    {
        var hasher = MakeHasher();
        string valid = hasher.Hash(PASSWORD);
        string[] p = valid.Split('$');
        string salt = p[4];
        string hash = p[5];

        string[] malformed =
        {
            "",
            "$argon2i$v=19$m=19456,t=2,p=1$" + salt + "$" + hash,
            "$argon2id$v=18$m=19456,t=2,p=1$" + salt + "$" + hash,
            "$argon2id$v=19$m=19456,t=2,p=1$" + salt + "$" + hash + "$extra",
            "$argon2id$v=19$m=19456,t=2,p=1$" + salt,
            "$argon2id$v=19$m=19456,t=2$" + salt + "$" + hash,
            "$argon2id$v=19$m=abc,t=2,p=1$" + salt + "$" + hash,
            "$argon2id$v=19$m=999999999,t=2,p=1$" + salt + "$" + hash,
            "$argon2id$v=19$m=65537,t=2,p=1$" + salt + "$" + hash,
            "$argon2id$v=19$m=19456,t=0,p=1$" + salt + "$" + hash,
            "$argon2id$v=19$m=19456,t=2,p=99$" + salt + "$" + hash,
            "$argon2id$v=19$m=19456,t=2,p=1$!!!notbase64!!!$" + hash,
            "$argon2id$v=19$m=19456,t=2,p=1$" + salt + "$@@@@",
            "$argon2id$v=19$m=19456,t=2,p=1$" + salt + "$",
            "$argon2id$v=19$m=19456,t=2,p=1$AAAA$" + hash,
            "not a phc string",
        };

        foreach (string phc in malformed)
        {
            bool result = true;
            Assert.DoesNotThrow(() => result = hasher.Verify(PASSWORD, phc), phc);
            Assert.That(result, Is.False, phc);
        }

        Assert.That(hasher.Verify(PASSWORD, null!), Is.False);
    }

    [Test]
    public void test_verify_older_memory_and_parallelism_hash_still_verifies_with_current_options()
    {
        var oldHasher = new Argon2idPasswordHasher(
            new AuthOptions { Argon2MemorySizeKb = 20480, Argon2Parallelism = 2 });
        string oldPhc = oldHasher.Hash(PASSWORD);

        Assert.That(oldPhc, Does.StartWith("$argon2id$v=19$m=20480,t=2,p=2$"));
        Assert.That(MakeHasher().Verify(PASSWORD, oldPhc), Is.True);
        Assert.That(MakeHasher().Verify("correct hors", oldPhc), Is.False);
    }

    [Test]
    public void test_verify_empty_password_returns_false_without_throwing()
    {
        var hasher = MakeHasher();
        string phc = hasher.Hash(PASSWORD);

        bool result = true;
        Assert.DoesNotThrow(() => result = hasher.Verify(string.Empty, phc));
        Assert.That(result, Is.False);
        Assert.Throws<ArgumentException>(() => hasher.Hash(string.Empty));
    }

    [Test]
    public void test_dummy_hash_uses_live_parameters_and_is_computed_once()
    {
        var hasher = new CountingHasher(MakeHasher(iterations: 3));
        var dummy = new DummyHash(hasher);

        string first = dummy.Phc;
        string second = dummy.Phc;

        Assert.That(hasher.HashCalls, Is.EqualTo(1));
        Assert.That(second, Is.SameAs(first));
        Assert.That(first, Does.Contain("t=3"));
        Assert.That(hasher.Verify("any password", first), Is.False);
    }

    /// <summary>Wraps a real hasher and counts <see cref="Hash"/> calls.</summary>
    private sealed class CountingHasher : IPasswordHasher
    {
        private readonly IPasswordHasher _inner;

        public CountingHasher(IPasswordHasher inner) => _inner = inner;

        public int HashCalls { get; private set; }

        public string Hash(string password)
        {
            HashCalls++;
            return _inner.Hash(password);
        }

        public bool Verify(string password, string phc) => _inner.Verify(password, phc);
    }

    [Test, Explicit, Category("Benchmark")]
    public void test_benchmark_hash_time_over_20_runs_prints_mean_and_p95()
    {
        var hasher = new Argon2idPasswordHasher(new AuthOptions());
        hasher.Hash("warm-up");

        var samples = new List<double>();
        for (int i = 0; i < BENCHMARK_RUNS; i++)
        {
            long start = Stopwatch.GetTimestamp();
            hasher.Hash(PASSWORD);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        samples.Sort();
        double mean = samples.Average();
        double p95 = samples[(int)Math.Ceiling(0.95 * samples.Count) - 1];
        TestContext.Out.WriteLine($"BENCHMARK Argon2id Hash x{BENCHMARK_RUNS}: mean={mean:F1} ms, p95={p95:F1} ms, min={samples[0]:F1} ms, max={samples[^1]:F1} ms");
    }
}
