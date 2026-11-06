using System.Diagnostics;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 009, AC-AUTH-16: unknown-username and wrong-password logins run Argon2id at equal cost.
/// Uses the real hasher at <see cref="AuthTimingTestConfig.ARGON2_MEMORY_SIZE_KB"/> (the live parameters are below the
/// floor on a fast machine, TD-093; the live-parameter floor is Story 017).
/// </summary>
[TestFixture]
[Category("Timing")]
internal sealed class Auth_TimingOracle_tests
{
    private const string PASSWORD = "correct horse";
    private const string WRONG_PASSWORD = "battery staple";

    [Test]
    public async Task RunAsync_UnknownAndWrongPassword_BothTakeAtLeastFloorAndMeansDifferByLessThanThreshold()
    {
        AuthOptions options = new() { Argon2MemorySizeKb = AuthTimingTestConfig.ARGON2_MEMORY_SIZE_KB };
        Argon2idPasswordHasher hasher = new(options);
        DummyHash dummy = new(hasher);
        using AuthHashGate gate = new(10);
        FixedAccountRepository repo = new(new AccountRecord(new AccountID(1u), hasher.Hash(PASSWORD), false, 0, null));
        CredentialCheck check = new(repo, hasher, gate, dummy, TimeProvider.System, NullLoggerFactory.Instance);

        // Untimed warm-up of both paths (JIT, allocator, thread pool).
        await check.RunAsync("alice", WRONG_PASSWORD, CancellationToken.None);
        await check.RunAsync("nobody", WRONG_PASSWORD, CancellationToken.None);

        double[] known = new double[AuthTimingTestConfig.SAMPLE_COUNT];
        double[] unknown = new double[AuthTimingTestConfig.SAMPLE_COUNT];
        for (int i = 0; i < AuthTimingTestConfig.SAMPLE_COUNT; i++)
        {
            known[i] = await TimeOne(check, "alice");
            unknown[i] = await TimeOne(check, "nobody");
        }

        double knownMean = known.Average();
        double unknownMean = unknown.Average();
        TestContext.Out.WriteLine(
            $"wrong-password mean={knownMean:F1} ms min={known.Min():F1}; unknown-username mean={unknownMean:F1} ms min={unknown.Min():F1}; delta={Math.Abs(knownMean - unknownMean):F1} ms");

        Assert.That(known.Min(), Is.GreaterThanOrEqualTo(AuthTimingTestConfig.AUTH_TIMING_FLOOR_MS), "wrong-password path below floor");
        Assert.That(unknown.Min(), Is.GreaterThanOrEqualTo(AuthTimingTestConfig.AUTH_TIMING_FLOOR_MS), "unknown-username path below floor");
        Assert.That(Math.Abs(knownMean - unknownMean), Is.LessThan(AuthTimingTestConfig.AUTH_TIMING_DELTA_THRESHOLD_MS));
    }

    private static async Task<double> TimeOne(CredentialCheck check, string username)
    {
        Stopwatch watch = Stopwatch.StartNew();
        CredentialOutcome outcome = await check.RunAsync(username, WRONG_PASSWORD, CancellationToken.None);
        watch.Stop();
        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.InvalidCredentials));
        return watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>Repository with one account that ignores writes, so repeated failures never trigger the locked fast path.</summary>
    private sealed class FixedAccountRepository : IAccountRepository
    {
        private readonly AccountRecord _account;

        public FixedAccountRepository(AccountRecord account)
        {
            _account = account;
        }

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct)
        {
            return Task.FromResult(usernameLower == "alice" ? _account : null);
        }

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct)
        {
            return Task.FromResult(new LoginFailureState(0, null));
        }

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct) => Task.CompletedTask;

        public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct) => throw new NotSupportedException();

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();
    }
}
