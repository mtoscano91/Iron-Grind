using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Routing;
using IronGrind.Gateway.ZoneLink;

namespace IronGrind.Gateway.Hosting;

/// <summary>
/// Resolves the account repository, the dummy hash and the zone composition when the host starts, so a missing or unsafe
/// database connection string or a bad option fails startup, the shared boot wait starts with the host, and the dummy hash
/// is computed before the first request (CR-AUTH-9 step 4). The composition part builds the event fan-out with all its
/// handlers, the link listener, the zone launcher and the keeper. Tests that replace <see cref="IAccountRepository"/>
/// and the hasher skip the database and the Argon2 cost.
/// </summary>
public sealed class GatewayStartupCheck : IHostedService
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the check.</summary>
    /// <param name="services">Root service provider.</param>
    public GatewayStartupCheck(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _services.GetRequiredService<IAccountRepository>();
        _ = _services.GetRequiredService<DummyHash>();
        _ = _services.GetRequiredService<GatewayBootWait>();
        _ = _services.GetRequiredService<ZoneLinkEventFanOut>().Handlers;
        _ = _services.GetRequiredService<ZoneLinkListener>();
        _ = _services.GetRequiredService<IZoneLauncher>();
        _ = _services.GetRequiredService<WarmSpareKeeper>();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
