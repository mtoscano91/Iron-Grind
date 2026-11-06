using System.Net;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 012 AC-1 and AC-8 (unhandled exception): the HTTP endpoint contract.</summary>
[TestFixture]
internal sealed class Gateway_Endpoints_tests
{
    private GwTestHost _host = null!;
    private GwFakeAuthService _fake = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new GwTestHost();
        _fake = new GwFakeAuthService();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _host.Dispose();
    }

    private void StartWithFakeService()
    {
        _host.Service = _fake;
        _client = _host.CreateClient();
    }

    private void StartWithRealService()
    {
        _host.Routable = true;
        _client = _host.CreateClient();
    }

    // AC-1: every outcome is HTTP 200 with the result in the body

    [TestCase(AuthFailureReason.InvalidCredentials)]
    [TestCase(AuthFailureReason.ServerError)]
    [TestCase(AuthFailureReason.ServerStarting)]
    public async Task Login_Failure_Is200WithReasonInBody(AuthFailureReason reason)
    {
        _fake.Login = LoginResponse.ForFailure(reason);
        StartWithFakeService();

        LoginResponse response = await GwHttp.PostLoginAsync(_client, "alice", GwHttp.CORRECT_PASSWORD);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(reason));
    }

    [Test]
    public async Task Login_Success_Is200WithTokenCharacterZoneAndPort()
    {
        byte[] token = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        _fake.Login = LoginResponse.ForSuccess(token, new CharacterID(77u), new ZoneID(5u), 7777);
        StartWithFakeService();

        LoginResponse response = await GwHttp.PostLoginAsync(_client, "alice", GwHttp.CORRECT_PASSWORD);

        Assert.That(response.Success, Is.True);
        Assert.That(response.SessionToken, Is.EqualTo(token));
        Assert.That(response.CharacterId, Is.EqualTo(new CharacterID(77u)));
        Assert.That(response.ZoneId, Is.EqualTo(new ZoneID(5u)));
        Assert.That(response.ZoneServerPort, Is.EqualTo((ushort)7777));
    }

    [Test]
    public async Task Register_EveryResult_Is200WithResultInBody([Values] RegistrationError error)
    {
        _fake.Register = new RegistrationResult(false, error);
        StartWithFakeService();

        RegistrationResult result = await GwHttp.PostRegisterAsync(_client, "alice", GwHttp.CORRECT_PASSWORD, "Aldric");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(error));
    }

    [TestCase("/v1/login")]
    [TestCase("/v1/register")]
    public async Task Response200_HasCacheControlNoStore(string path)
    {
        StartWithFakeService();
        byte[] body = path == "/v1/login"
            ? GwHttp.LoginBody("alice", GwHttp.CORRECT_PASSWORD)
            : GwHttp.RegisterBody("alice", GwHttp.CORRECT_PASSWORD, "Aldric");

        using HttpResponseMessage response = await _client.PostAsync(path, GwHttp.Body(body));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task Register_Success_Is200WithSuccess()
    {
        StartWithFakeService();

        RegistrationResult result = await GwHttp.PostRegisterAsync(_client, "alice", GwHttp.CORRECT_PASSWORD, "Aldric");

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task Login_RealServices_CorrectPasswordWithRoutableZone_ReturnsSuccess()
    {
        StartWithRealService();

        LoginResponse response = await GwHttp.PostLoginAsync(_client, "alice", GwHttp.CORRECT_PASSWORD);

        Assert.That(response.Success, Is.True);
        Assert.That(response.ZoneId, Is.EqualTo(new ZoneID(5u)));
        Assert.That(_host.Zone.AdmitCalls, Is.EqualTo(1));
    }

    // AC-1 edge cases: none of these reach the hasher

    [Test]
    public async Task Get_Login_Is405()
    {
        StartWithFakeService();

        using HttpResponseMessage response = await _client.GetAsync("/v1/login");

        Assert.That((int)response.StatusCode, Is.EqualTo(405));
        Assert.That(_fake.Calls, Is.EqualTo(0));
    }

    [TestCase("/v1/login")]
    [TestCase("/v1/register")]
    public async Task TruncatedBody_Is400AndNeverHashes(string path)
    {
        StartWithRealService();
        byte[] truncated = GwHttp.LoginBody("alice", GwHttp.CORRECT_PASSWORD)[..5];

        using HttpResponseMessage response = await _client.PostAsync(path, GwHttp.Body(truncated));

        Assert.That((int)response.StatusCode, Is.EqualTo(400));
        Assert.That(_host.Hasher.VerifyCalls + _host.Hasher.HashCalls, Is.EqualTo(0));
        Assert.That(_host.Repo.RegisterCalls, Is.EqualTo(0));
    }

    [TestCase("/v1/login")]
    [TestCase("/v1/register")]
    public async Task WrongContentType_Is415AndNeverHashes(string path)
    {
        StartWithRealService();

        using HttpResponseMessage response = await _client.PostAsync(
            path, GwHttp.Body(GwHttp.LoginBody("alice", GwHttp.CORRECT_PASSWORD), "text/plain"));

        Assert.That((int)response.StatusCode, Is.EqualTo(415));
        Assert.That(_host.Hasher.VerifyCalls + _host.Hasher.HashCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task MissingContentType_Is415()
    {
        StartWithRealService();

        using HttpResponseMessage response = await _client.PostAsync(
            "/v1/login", new ByteArrayContent(GwHttp.LoginBody("alice", GwHttp.CORRECT_PASSWORD)));

        Assert.That((int)response.StatusCode, Is.EqualTo(415));
        Assert.That(_host.Hasher.VerifyCalls, Is.EqualTo(0));
    }

    [TestCase(AuthEndpoints.MAX_REQUEST_BODY_BYTES, 400)]
    [TestCase(AuthEndpoints.MAX_REQUEST_BODY_BYTES + 1, 413)]
    [TestCase(2048, 413)]
    public async Task OversizedBody_Is413AndNeverHashes(int length, int expectedStatus)
    {
        StartWithRealService();

        using HttpResponseMessage response = await _client.PostAsync("/v1/login", GwHttp.Body(new byte[length]));

        Assert.That((int)response.StatusCode, Is.EqualTo(expectedStatus));
        Assert.That(_host.Hasher.VerifyCalls + _host.Hasher.HashCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task OversizedChunkedBody_Is413()
    {
        StartWithRealService();
        using var content = new StreamContent(new MemoryStream(new byte[2048]));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GwHttp.OCTET);

        using HttpResponseMessage response = await _client.PostAsync("/v1/login", content);

        Assert.That((int)response.StatusCode, Is.EqualTo(413));
        Assert.That(_host.Hasher.VerifyCalls, Is.EqualTo(0));
    }

    // AC-8: an unhandled exception is a 500 with an empty body and one Critical entry

    [TestCase("/v1/login")]
    [TestCase("/v1/register")]
    public async Task UnhandledException_Is500EmptyBodyAndOneInfrastructureCritical(string path)
    {
        _fake.Throw = new InvalidOperationException("boom");
        StartWithFakeService();
        byte[] body = path == "/v1/login"
            ? GwHttp.LoginBody("alice", GwHttp.CORRECT_PASSWORD)
            : GwHttp.RegisterBody("alice", GwHttp.CORRECT_PASSWORD, "Aldric");

        using HttpResponseMessage response = await _client.PostAsync(path, GwHttp.Body(body));

        Assert.That((int)response.StatusCode, Is.EqualTo(500));
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty);
        var criticals = _host.Logs.Entries.Where(e => e.Level == LogLevel.Critical).ToList();
        Assert.That(criticals, Has.Count.EqualTo(1));
        Assert.That(criticals[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
    }
}
