using IronGrind.Authentication;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 012: abandoned auth over HTTP (AC-AUTH-26, AC-AUTH-32 b/c, AC-AUTH-36 login half, AC-8 abort).
/// The hash is held by the test hasher; the server-side completion signal replaces any sleep.
/// </summary>
[TestFixture]
internal sealed class Gateway_AbandonedAuth_tests
{
    private GwTestHost _host = null!;
    private HttpClient _client = null!;
    private TaskCompletionSource _hold = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new GwTestHost { Routable = true };
        _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Hasher.Hold = _hold;
    }

    [TearDown]
    public void TearDown()
    {
        _hold.TrySetResult();
        _client?.Dispose();
        _host.Dispose();
    }

    private Task<HttpResponseMessage> StartLoginAsync(string password, CancellationToken ct) =>
        _client.PostAsync("/v1/login", GwHttp.Body(GwHttp.LoginBody("alice", password)), ct);

    private async Task ReleaseAndAwaitServerCompletionAsync()
    {
        _hold.SetResult();
        await GwHttp.WaitAsync(_host.Filter.Completed, "the server-side request to complete");
    }

    private void AssertNothingWritten()
    {
        Assert.That(_host.Repo.FailureCount, Is.EqualTo(2), "login_failure_count");
        Assert.That(_host.Repo.LockoutUntilUtc, Is.Null, "lockout_until_utc");
        Assert.That(_host.Repo.RecordFailureCalls, Is.EqualTo(0));
        Assert.That(_host.Repo.ResetCalls, Is.EqualTo(0));
        Assert.That(_host.Zone.AdmitCalls, Is.EqualTo(0));
        Assert.That(_host.Zone.EvictCalls, Is.EqualTo(0));
        Assert.That(_host.Hasher.VerifyCalls, Is.EqualTo(1), "the hash still ran to completion");
        Assert.That(_host.Logs.Entries.Where(e => e.Level == LogLevel.Critical), Is.Empty, "an aborted request is not a Critical");
    }

    // AC-2 (AC-AUTH-26)

    [TestCase(GwHttp.WRONG_PASSWORD)]
    [TestCase(GwHttp.CORRECT_PASSWORD)]
    public async Task ClientAbortsDuringHash_ResultIsDiscardedBeforeAnyWrite(string password)
    {
        _client = _host.CreateClient();
        using var cts = new CancellationTokenSource();
        Task<HttpResponseMessage> request = StartLoginAsync(password, cts.Token);
        await GwHttp.WaitAsync(_host.Hasher.Entered, "the hash to start");

        cts.Cancel();
        await GwHttp.WaitAsync(_host.Filter.Aborted, "the server to see the abort");
        await ReleaseAndAwaitServerCompletionAsync();
        await GwHttp.ExpectCanceledAsync(request);

        AssertNothingWritten();
    }

    // AC-3 (AC-AUTH-32 b, c): the client's own timeout is the same discard path

    [TestCase(GwHttp.WRONG_PASSWORD)]
    [TestCase(GwHttp.CORRECT_PASSWORD)]
    public async Task ClientTimeoutFiresBeforeHashEnds_ResultIsDiscardedBeforeAnyWrite(string password)
    {
        _client = _host.CreateClient();
        _client.Timeout = TimeSpan.FromMilliseconds(200);
        Task<HttpResponseMessage> request = StartLoginAsync(password, CancellationToken.None);
        await GwHttp.WaitAsync(_host.Hasher.Entered, "the hash to start");

        await GwHttp.WaitAsync(_host.Filter.Aborted, "the server to see the timeout abort");
        await ReleaseAndAwaitServerCompletionAsync();
        await GwHttp.ExpectCanceledAsync(request);

        AssertNothingWritten();
    }

    // Control: the same held hash without an abort completes the login normally

    [Test]
    public async Task ClientStaysConnected_CorrectPasswordCompletesTheLogin()
    {
        _client = _host.CreateClient();
        Task<HttpResponseMessage> request = StartLoginAsync(GwHttp.CORRECT_PASSWORD, CancellationToken.None);
        await GwHttp.WaitAsync(_host.Hasher.Entered, "the hash to start");

        _hold.SetResult();
        using HttpResponseMessage response = await GwHttp.Within(request);

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(_host.Repo.ResetCalls, Is.EqualTo(1));
        Assert.That(_host.Zone.AdmitCalls, Is.EqualTo(1));
    }

    // AC-4 (AC-AUTH-36, login half)

    [Test]
    public async Task GateFull_ThirdCorrectLogin_IsServerErrorWithoutHashOrWrites()
    {
        _host.Settings["MAX_AUTH_PENDING"] = "2";
        _client = _host.CreateClient();
        Task<HttpResponseMessage> first = StartLoginAsync(GwHttp.CORRECT_PASSWORD, CancellationToken.None);
        Task<HttpResponseMessage> second = StartLoginAsync(GwHttp.CORRECT_PASSWORD, CancellationToken.None);
        await GwHttp.WaitAsync(_host.Hasher.Entered, "the first hash to start");
        await GwHttp.WaitAsync(_host.Hasher.Entered, "the second hash to start");

        LoginResponse third = await GwHttp.Within(GwHttp.PostLoginAsync(_client, "alice", GwHttp.CORRECT_PASSWORD));

        Assert.That(third.Success, Is.False);
        Assert.That(third.ErrorCode, Is.EqualTo(AuthFailureReason.ServerError));
        Assert.That(_host.Hasher.VerifyCalls, Is.EqualTo(2), "the full gate must not start a third hash");
        Assert.That(_host.Repo.FailureCount, Is.EqualTo(2));
        Assert.That(_host.Repo.LockoutUntilUtc, Is.Null);
        Assert.That(_host.Repo.RecordFailureCalls, Is.EqualTo(0));
        Assert.That(_host.Zone.AdmitCalls, Is.EqualTo(0));

        _hold.SetResult();
        using HttpResponseMessage firstResponse = await GwHttp.Within(first);
        using HttpResponseMessage secondResponse = await GwHttp.Within(second);
        Assert.That((int)firstResponse.StatusCode, Is.EqualTo(200));
        Assert.That((int)secondResponse.StatusCode, Is.EqualTo(200));
    }
}
