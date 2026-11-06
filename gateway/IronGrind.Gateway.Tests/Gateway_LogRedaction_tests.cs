using IronGrind.Authentication;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 012: AC-AUTH-09 (no password in logs, PHC stored) and AC-AUTH-19 (a) locked fast path end to end.</summary>
[TestFixture]
internal sealed class Gateway_LogRedaction_tests
{
    private const string REGISTER_PASSWORD = "S3cret!pass";
    private const string WRONG_PASSWORD = "Wr0ng!pass";
    private const double LOCKED_HANDLER_LIMIT_MS = 10;

    private GwTestHost _host = null!;
    private HttpClient? _client;

    [SetUp]
    public void SetUp() => _host = new GwTestHost { FakeHasher = false };

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _host.Dispose();
    }

    // AC-5 (AC-AUTH-09 b)

    [Test]
    public async Task RegisterAndLogins_AtTrace_NoLogMessageOrScopeContainsAnyPassword()
    {
        _client = _host.CreateClient();

        RegistrationResult registered = await GwHttp.PostRegisterAsync(_client, "alice", REGISTER_PASSWORD, "Aldric");
        LoginResponse right = await GwHttp.PostLoginAsync(_client, "alice", REGISTER_PASSWORD);
        LoginResponse wrong = await GwHttp.PostLoginAsync(_client, "alice", WRONG_PASSWORD);

        Assert.That(registered.Success, Is.True);
        Assert.That(right.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting), "credentials passed; the no-zone router found no zone");
        Assert.That(wrong.ErrorCode, Is.EqualTo(AuthFailureReason.InvalidCredentials));
        Assert.That(_host.Logs.Entries, Is.Not.Empty, "the capture must actually see log output");
        foreach (GwLogEntry entry in _host.Logs.Entries)
        {
            string text = entry.Message + "\n" + entry.Exception + "\n" + string.Join("\n", entry.Scopes);
            Assert.That(text, Does.Not.Contain(REGISTER_PASSWORD), entry.Category);
            Assert.That(text, Does.Not.Contain(WRONG_PASSWORD), entry.Category);
        }
    }

    [Test]
    public async Task Register_StoresAnArgon2idPhcNotThePassword_FakeRepository()
    {
        _client = _host.CreateClient();

        await GwHttp.PostRegisterAsync(_client, "alice", REGISTER_PASSWORD, "Aldric");

        Assert.That(_host.Repo.LastRegisteredPhc, Does.StartWith("$argon2id$"));
        Assert.That(_host.Repo.LastRegisteredPhc, Does.Not.Contain(REGISTER_PASSWORD));
    }

    // AC-5 (AC-AUTH-09 a), test database

    [Test]
    [Category("Database")]
    public async Task Register_StoredPasswordHashPhc_StartsWithArgon2id()
    {
        await using AuthTestDatabase db = await AuthTestDatabase.CreateAsync();
        _host.ReplaceRepository = false;
        _client = _host.CreateClient();

        RegistrationResult registered = await GwHttp.PostRegisterAsync(_client, "alice", REGISTER_PASSWORD, "Aldric");

        Assert.That(registered.Success, Is.True);
        object? phc = await db.AdminScalarAsync("SELECT password_hash_phc FROM accounts WHERE username_lower = @u", ("u", "alice"));
        Assert.That(phc as string, Does.StartWith("$argon2id$"));
    }

    // AC-6 (AC-AUTH-19 a), test database

    [Test]
    [Category("Database")]
    public async Task LockedAccount_LoginHandlerTime_IsUnderTenMilliseconds()
    {
        await using AuthTestDatabase db = await AuthTestDatabase.CreateAsync();
        _host.ReplaceRepository = false;
        _client = _host.CreateClient();
        await GwHttp.PostRegisterAsync(_client, "alice", REGISTER_PASSWORD, "Aldric");
        await db.AdminExecuteAsync(
            "UPDATE accounts SET login_failure_count = 5, lockout_until_utc = now() + interval '1 hour' WHERE username_lower = @u",
            ("u", "alice"));

        // The first locked login warms the pool and JIT; the second is the measured one.
        await GwHttp.PostLoginAsync(_client, "alice", REGISTER_PASSWORD);
        LoginResponse response = await GwHttp.PostLoginAsync(_client, "alice", REGISTER_PASSWORD);

        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.InvalidCredentials));
        Assert.That(_host.Filter.ElapsedMs[^1], Is.LessThan(LOCKED_HANDLER_LIMIT_MS));
    }
}
