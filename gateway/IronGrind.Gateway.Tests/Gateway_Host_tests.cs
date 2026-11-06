using System.Net;
using System.Net.Sockets;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 012 AC-7: host wiring, startup validation, no-zone stand-ins and loopback binding.
/// Non-parallelizable: these tests set and restore the process environment variable <c>IRONGRIND_GATEWAY_DB</c>,
/// which the Database tests also read.
/// </summary>
[TestFixture]
[NonParallelizable]
internal sealed class Gateway_Host_tests
{
    private const string DB_VAR = GatewayHost.DATABASE_CONFIG_KEY;
    private const string VALID_CONNECTION = "Host=127.0.0.1;Database=irongrind_auth_test;Username=gateway;Password=unused";

    private GwTestHost _host = null!;
    private HttpClient? _client;
    private string? _savedDbVar;

    [SetUp]
    public void SetUp()
    {
        _host = new GwTestHost();
        _savedDbVar = Environment.GetEnvironmentVariable(DB_VAR);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(DB_VAR, _savedDbVar);
        _client?.Dispose();
        _host.Dispose();
    }

    private static TException? FindInChain<TException>(Exception? ex) where TException : Exception
    {
        while (ex is not null)
        {
            if (ex is TException match)
            {
                return match;
            }

            ex = ex.InnerException;
        }

        return null;
    }

    // Wiring with the no-zone stand-ins

    [Test]
    public async Task CorrectLogin_WithNoZoneStandIns_Is200ServerStarting()
    {
        _client = _host.CreateClient();

        LoginResponse response = await GwHttp.PostLoginAsync(_client, "alice", GwHttp.CORRECT_PASSWORD);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        Assert.That(_host.Repo.ResetCalls, Is.EqualTo(1), "the credential check passed before the router found no zone");
    }

    [Test]
    public void StartupCheck_ComputesTheDummyHashBeforeTheFirstRequest()
    {
        _client = _host.CreateClient();

        Assert.That(_host.HashCallsAtStartup, Is.EqualTo(1), "the dummy hash is computed once, at host start");
        Assert.That(_host.Services().GetRequiredService<DummyHash>().Phc, Does.StartWith("$argon2id$"));
    }

    [Test]
    public void NoZoneStandIns_BehaveAsDocumented()
    {
        var map = new EmptyAccountZoneMap();
        var router = new NoZoneRouter();
        var link = new UnavailableZoneLink();

        Assert.That(map.TryGetZone(new AccountID(1u), out ZoneID mapped), Is.False);
        Assert.That(mapped, Is.EqualTo(ZoneID.Invalid));
        Assert.That(map.IsLinkUp(new ZoneID(5u)), Is.False);
        Assert.That(router.TryPick(ZoneID.Invalid, ZoneID.Invalid, out _, out _), Is.False);
        Assert.That(router.TryGetPort(new ZoneID(5u), out _), Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            link.AdmitAsync(new ZoneID(5u), new AccountID(1u), new CharacterID(1u), new byte[16], CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(() => link.EvictAsync(new ZoneID(5u), new AccountID(1u), CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            link.ReconnectAsync(new ZoneID(5u), new AccountID(1u), new byte[16], CancellationToken.None));
    }

    // Startup validation

    [Test]
    public void Startup_HttpPortZero_ThrowsOptionsValidationException()
    {
        _host.Settings["GATEWAY_HTTP_PORT"] = "0";

        Exception? thrown = Assert.Catch(() => _client = _host.CreateClient());

        Assert.That(FindInChain<OptionsValidationException>(thrown), Is.Not.Null, thrown?.ToString());
    }

    [Test]
    public void Startup_DatabaseVariableMissing_Throws()
    {
        Environment.SetEnvironmentVariable(DB_VAR, null);
        _host.ReplaceRepository = false;

        Exception? thrown = Assert.Catch(() => _client = _host.CreateClient());

        Assert.That(FindInChain<InvalidOperationException>(thrown)?.Message, Does.Contain(DB_VAR), thrown?.ToString());
    }

    [Test]
    public void Startup_DatabaseVariableEmpty_Throws()
    {
        Environment.SetEnvironmentVariable(DB_VAR, string.Empty);
        _host.ReplaceRepository = false;

        Exception? thrown = Assert.Catch(() => _client = _host.CreateClient());

        Assert.That(FindInChain<InvalidOperationException>(thrown)?.Message, Does.Contain(DB_VAR), thrown?.ToString());
    }

    [TestCase(";Include Error Detail=true")]
    [TestCase(";IncludeErrorDetail=True")]
    public void Startup_IncludeErrorDetailTrue_Throws(string suffix)
    {
        Environment.SetEnvironmentVariable(DB_VAR, VALID_CONNECTION + suffix);
        _host.ReplaceRepository = false;

        Exception? thrown = Assert.Catch(() => _client = _host.CreateClient());

        Assert.That(FindInChain<InvalidOperationException>(thrown)?.Message, Does.Contain("Include Error Detail"), thrown?.ToString());
    }

    [Test]
    public void Startup_ValidConnectionString_BuildsTheRealRepositoryWithoutConnecting()
    {
        Environment.SetEnvironmentVariable(DB_VAR, VALID_CONNECTION);
        _host.ReplaceRepository = false;

        Assert.DoesNotThrow(() => _client = _host.CreateClient());
        Assert.That(_host.Services().GetRequiredService<IAccountRepository>(), Is.TypeOf<NpgsqlAccountRepository>());
    }

    [Test]
    public void Startup_FakeRepository_DoesNotNeedTheDatabaseVariable()
    {
        Environment.SetEnvironmentVariable(DB_VAR, null);

        Assert.DoesNotThrow(() => _client = _host.CreateClient());
    }

    // Loopback binding on real Kestrel

    [Test]
    public async Task Kestrel_OnFreePort_BindsOnlyTo127001()
    {
        int port = FreeTcpPort();
        Environment.SetEnvironmentVariable(DB_VAR, VALID_CONNECTION);
        WebApplication app = GatewayHost.CreateApp(new[] { $"--GATEWAY_HTTP_PORT={port}" });
        try
        {
            await app.StartAsync();

            ICollection<string> addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            Assert.That(addresses, Is.Not.Empty);
            foreach (string address in addresses)
            {
                var uri = new Uri(address);
                Assert.That(uri.Host, Is.EqualTo("127.0.0.1"), address);
                Assert.That(uri.Port, Is.EqualTo(port), address);
            }

            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            using HttpResponseMessage response = await http.PostAsync("/v1/login", GwHttp.Body(new byte[1], "text/plain"));
            Assert.That((int)response.StatusCode, Is.EqualTo(415));
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Test]
    public void ConfigureKestrel_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GatewayHost.ConfigureKestrel(null!, 8080));
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
