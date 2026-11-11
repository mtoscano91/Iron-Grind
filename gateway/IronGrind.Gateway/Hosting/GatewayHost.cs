using System.Net;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Http;
using IronGrind.Gateway.Launch;
using IronGrind.Gateway.Routing;
using IronGrind.Gateway.ZoneLink;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IronGrind.Gateway.Hosting;

/// <summary>
/// Builds the gateway host (ADR-016 Decisions 1-2): service wiring, the two endpoints, the error middleware and the
/// loopback-only Kestrel binding.
/// </summary>
public static class GatewayHost
{
    /// <summary>Configuration key (environment variable) holding the gateway role's PostgreSQL connection string.</summary>
    public const string DATABASE_CONFIG_KEY = "IRONGRIND_GATEWAY_DB";

    /// <summary>
    /// Creates the web application. Options are validated at start; the database connection string is validated when
    /// the account repository is first built, which <see cref="GatewayStartupCheck"/> forces at host start.
    /// </summary>
    /// <param name="args">Command-line arguments (configuration overrides).</param>
    /// <returns>The built, not yet started, application.</returns>
    public static WebApplication CreateApp(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel((_, kestrel) =>
            ConfigureKestrel(kestrel, kestrel.ApplicationServices.GetRequiredService<IOptions<GatewayOptions>>().Value.HttpPort));
        AddGatewayServices(builder.Services, builder.Configuration);

        WebApplication app = builder.Build();
        app.UseMiddleware<InfrastructureErrorMiddleware>();
        app.MapAuthEndpoints();
        return app;
    }

    /// <summary>Binds Kestrel to <c>127.0.0.1:httpPort</c> only; Caddy fronts it (ADR-016 Decision 1).</summary>
    /// <param name="options">Kestrel options.</param>
    /// <param name="httpPort">The validated <c>GATEWAY_HTTP_PORT</c>.</param>
    public static void ConfigureKestrel(KestrelServerOptions options, int httpPort)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Listen(IPAddress.Loopback, httpPort);
    }

    /// <summary>Registers the authentication services and the gateway composition (Stories 004-011 and Gateway Story 009).</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddGatewayServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGatewayOptions(configuration);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<GatewayOptions>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthOptions>>().Value);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IAuthHashGate>(sp => new AuthHashGate(sp.GetRequiredService<GatewayOptions>().MaxAuthPending));
        services.AddSingleton<DummyHash>();

        // Built on first resolution, so a test host that replaces IAccountRepository never needs the variable.
        services.AddSingleton(sp => CreateDataSource(configuration[DATABASE_CONFIG_KEY]));
        services.AddSingleton<IRegistrationFaultHook>(NoRegistrationFaultHook.Instance);
        services.AddSingleton<LockoutPolicy>();
        services.AddSingleton<IAccountRepository, NpgsqlAccountRepository>();

        services.AddSingleton<RegistrationValidator>();
        services.AddSingleton<CredentialCheck>();
        services.AddSingleton<TokenPath>();
        AddZoneComposition(services);
        services.AddSingleton(sp => new SessionEstablishment(
            sp.GetRequiredService<IAccountRepository>(),
            sp.GetRequiredService<IAccountZoneMap>(),
            sp.GetRequiredService<IZoneRouter>(),
            sp.GetRequiredService<IZoneLink>(),
            sp.GetRequiredService<GatewayOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<GatewayBootWait>()));
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // Hosted services start in this order: the check first (a bad option or database string fails before the port binds), then the link listener, then the timer.
        services.AddHostedService<GatewayStartupCheck>();
        services.AddHostedService(sp => sp.GetRequiredService<ZoneLinkListener>());
        services.AddHostedService<GatewayMaintenanceTimer>();
        return services;
    }

    /// <summary>
    /// Registers the zone side (Gateway Story 009): each of the listener, link, map, routing table and warm-spare keeper is one
    /// singleton under its concrete type, and the interfaces resolve to that same instance. The two cycles (listener, events,
    /// link, listener and map, evict sender, link, map) are broken by <see cref="ZoneLinkEventFanOut"/>'s lazy handler list and by
    /// the evict sender's <c>Func&lt;IZoneLink&gt;</c>.
    /// </summary>
    private static void AddZoneComposition(IServiceCollection services)
    {
        // Not started here: the zone-link listener starts it once it has bound (the window opens when zones can reconnect).
        services.AddSingleton(sp => GatewayBootWait.NotStarted(sp.GetRequiredService<GatewayOptions>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<RoutingTable>();
        services.AddSingleton<FillFirstRouter>();
        services.AddSingleton<IZoneRouter>(sp => sp.GetRequiredService<FillFirstRouter>());
        services.AddSingleton<IZoneEvictSender>(sp => new ZoneLinkEvictSender(
            () => sp.GetRequiredService<IZoneLink>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(sp => new AccountZoneMap(
            sp.GetRequiredService<GatewayOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IZoneEvictSender>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<GatewayBootWait>()));
        services.AddSingleton<IAccountZoneMap>(sp => sp.GetRequiredService<AccountZoneMap>());
        services.AddSingleton<LinkZoneLink>();
        services.AddSingleton<IZoneLink>(sp => sp.GetRequiredService<LinkZoneLink>());
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IFileWriter, SystemFileWriter>();
        services.AddSingleton(sp => ZoneLauncherFactory.Create(
            sp.GetRequiredService<GatewayOptions>(),
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IFileWriter>(),
            sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IZoneIdAllocator, NpgsqlZoneIdAllocator>();
        services.AddSingleton<WarmSpareKeeper>();
        services.AddSingleton(sp => new ZoneLinkEventFanOut(
            () => new IZoneLinkEvents[]
            {
                sp.GetRequiredService<RoutingTable>(),
                sp.GetRequiredService<AccountZoneMap>(),
                sp.GetRequiredService<LinkZoneLink>(),
                sp.GetRequiredService<WarmSpareKeeper>(),
            },
            sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IZoneLinkEvents>(sp => sp.GetRequiredService<ZoneLinkEventFanOut>());
        services.AddSingleton(sp => new ZoneLinkListener(
            sp.GetRequiredService<IOptions<GatewayOptions>>(),
            sp.GetRequiredService<IZoneLinkEvents>(),
            sp.GetRequiredService<ILogger<ZoneLinkListener>>(),
            sp.GetRequiredService<GatewayBootWait>()));
        services.AddSingleton<IZoneLinkSender>(sp => sp.GetRequiredService<ZoneLinkListener>());
    }

    /// <summary>
    /// Builds the one pooled data source from the connection string.
    /// </summary>
    /// <param name="connectionString">Value of <c>IRONGRIND_GATEWAY_DB</c>.</param>
    /// <returns>The data source (no connection is opened).</returns>
    /// <exception cref="InvalidOperationException">The variable is missing or empty, or the string sets <c>Include Error Detail=true</c>
    /// (database errors would then log the failing row, password hash included).</exception>
    public static NpgsqlDataSource CreateDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{DATABASE_CONFIG_KEY} is not set; the gateway cannot start without its database.");
        }

        if (new NpgsqlConnectionStringBuilder(connectionString).IncludeErrorDetail)
        {
            throw new InvalidOperationException($"{DATABASE_CONFIG_KEY} must not set 'Include Error Detail=true' (database errors would log password hashes).");
        }

        return NpgsqlDataSource.Create(connectionString);
    }
}
