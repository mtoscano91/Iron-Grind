using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Shared test host plumbing for the Authentication Story 012 HTTP tests (fakes, log capture, completion signal).</summary>
internal static class GwHttp
{
    public const string OCTET = "application/octet-stream";
    public const string CORRECT_PASSWORD = "Correct!pass1";
    public const string WRONG_PASSWORD = "Wr0ng!pass";
    public static readonly TimeSpan HANG_GUARD = TimeSpan.FromSeconds(30);

    public static ByteArrayContent Body(byte[] bytes, string contentType = OCTET)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    public static byte[] LoginBody(string username, string password) =>
        AuthBodyCodec.Encode(new ConnectionRequest(username, password, new byte[AuthBodyCodec.TOKEN_LENGTH]));

    public static byte[] RegisterBody(string username, string password, string character) =>
        AuthBodyCodec.Encode(new RegistrationRequest(username, password, character, 0));

    public static async Task<LoginResponse> PostLoginAsync(HttpClient client, string username, string password)
    {
        using HttpResponseMessage response = await client.PostAsync("/v1/login", Body(LoginBody(username, password)));
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(OCTET));
        Assert.That(AuthBodyCodec.TryDecode(await response.Content.ReadAsByteArrayAsync(), out LoginResponse decoded), Is.True);
        return decoded;
    }

    public static async Task<RegistrationResult> PostRegisterAsync(HttpClient client, string username, string password, string character)
    {
        using HttpResponseMessage response = await client.PostAsync("/v1/register", Body(RegisterBody(username, password, character)));
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(AuthBodyCodec.TryDecode(await response.Content.ReadAsByteArrayAsync(), out RegistrationResult decoded), Is.True);
        return decoded;
    }

    public static async Task<T> Within<T>(Task<T> task)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(HANG_GUARD));
        if (finished != task)
        {
            Assert.Fail("Task did not complete within the hang guard.");
        }

        return await task;
    }

    public static async Task ExpectCanceledAsync(Task<HttpResponseMessage> request)
    {
        Task finished = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.That(finished, Is.SameAs(request), "the client request did not end after the server finished");
        if (request.IsCompletedSuccessfully)
        {
            // TestServer ends a client call only when the server handler ends; an aborted request must get no body.
            using HttpResponseMessage response = request.Result;
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty, "an aborted request must get no body");
        }
        else
        {
            Assert.That(request.Exception?.GetBaseException(), Is.InstanceOf<OperationCanceledException>().Or.Null);
        }
    }

    public static async Task WaitAsync(SemaphoreSlim signal, string what)
    {
        if (!await signal.WaitAsync(HANG_GUARD))
        {
            Assert.Fail($"Timed out waiting for {what}.");
        }
    }
}

/// <summary>Account repository fake holding one account; counts every write.</summary>
internal sealed class GwFakeRepo : IAccountRepository
{
    public static readonly AccountID ACCOUNT = new(1u);

    public string Username = "alice";
    public string Phc = "$argon2id$fake$" + GwHttp.CORRECT_PASSWORD;
    public int FailureCount = 2;
    public DateTime? LockoutUntilUtc { get; set; }
    public int RecordFailureCalls;
    public int ResetCalls;
    public int RegisterCalls;
    public string? LastRegisteredPhc;

    public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct)
    {
        Interlocked.Increment(ref RegisterCalls);
        Username = usernameLower;
        Phc = passwordHashPhc;
        LastRegisteredPhc = passwordHashPhc;
        return Task.FromResult(RegistrationOutcome.Succeeded(ACCOUNT, new CharacterID(77u)));
    }

    public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct) =>
        Task.FromResult(usernameLower == Username ? new AccountRecord(ACCOUNT, Phc, false, FailureCount, LockoutUntilUtc) : null);

    public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct)
    {
        Interlocked.Increment(ref RecordFailureCalls);
        FailureCount++;
        return Task.FromResult(new LoginFailureState(FailureCount, null));
    }

    public Task ResetFailuresAsync(AccountID account, CancellationToken ct)
    {
        Interlocked.Increment(ref ResetCalls);
        FailureCount = 0;
        return Task.CompletedTask;
    }

    public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct) => Task.FromResult(new CharacterID(77u));

    public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => Task.FromResult(ZoneID.Invalid);
}

/// <summary>Hasher fake whose Verify can be held until a test releases it. Its PHC is the prefix plus the password.</summary>
internal sealed class GwHasher : IPasswordHasher
{
    private const string PREFIX = "$argon2id$fake$";

    public readonly SemaphoreSlim Entered = new(0);
    public volatile TaskCompletionSource? Hold;
    public int VerifyCalls;
    public int HashCalls;

    public string Hash(string password)
    {
        Interlocked.Increment(ref HashCalls);
        return PREFIX + password;
    }

    public bool Verify(string password, string phc)
    {
        Interlocked.Increment(ref VerifyCalls);
        Entered.Release();
        Hold?.Task.GetAwaiter().GetResult();
        return phc == PREFIX + password;
    }

    public void ResetCounts()
    {
        HashCalls = 0;
        VerifyCalls = 0;
    }
}

/// <summary>Zone fakes for a routable zone 5 on port 7777; the link counts every call.</summary>
internal sealed class GwFakeZone : IZoneRouter, IZoneLink, IAccountZoneMap
{
    public int AdmitCalls;
    public int EvictCalls;

    public bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port)
    {
        zone = new ZoneID(5u);
        port = 7777;
        return true;
    }

    public bool TryGetPort(ZoneID zone, out ushort port)
    {
        port = 7777;
        return true;
    }

    public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct)
    {
        Interlocked.Increment(ref AdmitCalls);
        SetSession(account, zone);
        return Task.FromResult(true);
    }

    public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct)
    {
        Interlocked.Increment(ref EvictCalls);
        return Task.CompletedTask;
    }

    public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct) =>
        Task.FromResult(new ReconnectOutcome(false, null));

    public bool TryGetZone(AccountID account, out ZoneID zone)
    {
        zone = ZoneID.Invalid;
        return false;
    }

    public bool IsLinkUp(ZoneID zone) => true;

    public void SetSession(AccountID account, ZoneID zone)
    {
    }

    public bool Remove(AccountID account, ZoneID fromZone) => false;
}

/// <summary>Authentication service fake returning fixed results, or throwing.</summary>
internal sealed class GwFakeAuthService : IAuthenticationService
{
    public LoginResponse Login = LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials);
    public RegistrationResult Register = new(true, default);
    public Exception? Throw;
    public int Calls;

    public Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Throw is null ? Task.FromResult(Register) : Task.FromException<RegistrationResult>(Throw);
    }

    public Task<LoginResponse> AuthenticateAsync(ConnectionRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return Throw is null ? Task.FromResult(Login) : Task.FromException<LoginResponse>(Throw);
    }
}

/// <summary>One captured log entry.</summary>
internal sealed record GwLogEntry(string Category, LogLevel Level, string Message, string? Exception, IReadOnlyList<string> Scopes);

/// <summary>In-memory logger provider that records messages, exceptions and active scopes (GAP-6).</summary>
internal sealed class GwLogProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly object _lock = new();
    private readonly List<GwLogEntry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<GwLogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void Add(GwLogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    private sealed class CategoryLogger : ILogger
    {
        private readonly string _category;
        private readonly GwLogProvider _owner;

        public CategoryLogger(string category, GwLogProvider owner)
        {
            _category = category;
            _owner = owner;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            _owner._scopes.ForEachScope(
                (scope, list) =>
                {
                    list.Add(scope?.ToString() ?? string.Empty);
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        list.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
                    }
                },
                scopes);
            _owner.Add(new GwLogEntry(_category, logLevel, formatter(state, exception), exception?.ToString(), scopes));
        }
    }
}

/// <summary>
/// Test-only outermost middleware added through an <see cref="IStartupFilter"/>: measures the handler time and signals a
/// server-side completion after the whole request pipeline returned (no sleeps in tests, no timer in production code).
/// </summary>
internal sealed class GwCompletionFilter : IStartupFilter
{
    private readonly object _lock = new();
    private readonly List<double> _elapsedMs = new();

    public SemaphoreSlim Completed { get; } = new(0);

    /// <summary>Released when the server observes the client's abort (RequestAborted fired).</summary>
    public SemaphoreSlim Aborted { get; } = new(0);

    public IReadOnlyList<double> ElapsedMs
    {
        get
        {
            lock (_lock)
            {
                return _elapsedMs.ToList();
            }
        }
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, inner) =>
        {
            long start = Stopwatch.GetTimestamp();
            context.RequestAborted.Register(() => Aborted.Release());
            try
            {
                await inner(context);
            }
            finally
            {
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                lock (_lock)
                {
                    _elapsedMs.Add(ms);
                }

                Completed.Release();
            }
        });
        next(app);
    };
}

/// <summary>The fakes of one test host and the factory that wires them over the real <c>Program</c>.</summary>
internal sealed class GwTestHost : IDisposable
{
    public GwFakeRepo Repo { get; } = new();

    public GwHasher Hasher { get; } = new();

    public GwFakeZone Zone { get; } = new();

    public GwCompletionFilter Filter { get; } = new();

    public GwLogProvider Logs { get; } = new();

    /// <summary>True replaces the no-zone stand-ins with the routable fake zone.</summary>
    public bool Routable { get; set; }

    /// <summary>False keeps the real Argon2id hasher.</summary>
    public bool FakeHasher { get; set; } = true;

    /// <summary>Replaces <see cref="IAuthenticationService"/> when set.</summary>
    public IAuthenticationService? Service { get; set; }

    /// <summary>Hasher Hash calls made while the host started (the eager dummy hash).</summary>
    public int HashCallsAtStartup { get; private set; }

    /// <summary>False keeps the real Npgsql repository and its data source (startup validation tests).</summary>
    public bool ReplaceRepository { get; set; } = true;

    /// <summary>Settings; the zone-link port defaults to a free port so hosts never share the default 7900.</summary>
    public Dictionary<string, string?> Settings { get; } = new() { ["GATEWAY_ZONE_LINK_PORT"] = GwPorts.Free().ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private WebApplicationFactory<Program>? _factory;

    public HttpClient CreateClient()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (KeyValuePair<string, string?> setting in Settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(Logs);
            });
            builder.ConfigureTestServices(Replace);
        });
        HttpClient client = _factory.CreateClient();
        HashCallsAtStartup = Hasher.HashCalls;
        Hasher.ResetCounts();
        return client;
    }

    private void Replace(IServiceCollection services)
    {
        if (ReplaceRepository)
        {
            services.RemoveAll<IAccountRepository>();
            services.AddSingleton<IAccountRepository>(Repo);
        }

        services.RemoveAll<IZoneIdAllocator>();
        services.AddSingleton<IZoneIdAllocator>(new GwFakeAllocator());

        if (FakeHasher)
        {
            services.RemoveAll<IPasswordHasher>();
            services.AddSingleton<IPasswordHasher>(Hasher);
        }

        if (Routable)
        {
            services.RemoveAll<IZoneRouter>();
            services.RemoveAll<IZoneLink>();
            services.RemoveAll<IAccountZoneMap>();
            services.AddSingleton<IZoneRouter>(Zone);
            services.AddSingleton<IZoneLink>(Zone);
            services.AddSingleton<IAccountZoneMap>(Zone);

            // The fake zone is routable at once, so these hosts run without the boot wait (Gateway Story 009 covers it).
            services.RemoveAll<SessionEstablishment>();
            services.AddSingleton(sp => new SessionEstablishment(
                sp.GetRequiredService<IAccountRepository>(),
                sp.GetRequiredService<IAccountZoneMap>(),
                sp.GetRequiredService<IZoneRouter>(),
                sp.GetRequiredService<IZoneLink>(),
                sp.GetRequiredService<GatewayOptions>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>()));
        }

        if (Service is not null)
        {
            services.RemoveAll<IAuthenticationService>();
            services.AddSingleton(Service);
        }

        services.AddSingleton<IStartupFilter>(Filter);
    }

    /// <summary>The started host's root service provider.</summary>
    public IServiceProvider Services() => _factory!.Services;

    public void Dispose() => _factory?.Dispose();
}

/// <summary>Free loopback TCP ports for tests that start a real zone-link listener.</summary>
internal static class GwPorts
{
    public static int Free()
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

/// <summary>Zone id allocator that never touches a database: hands out 100, 101, ...</summary>
internal sealed class GwFakeAllocator : IZoneIdAllocator
{
    private int _next = 99;

    public int Calls => Volatile.Read(ref _next) - 99;

    public Task<ZoneID> AllocateAsync(CancellationToken ct) => Task.FromResult(new ZoneID((uint)Interlocked.Increment(ref _next)));
}
