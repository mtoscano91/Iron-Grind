using IronGrind.Gateway.Auth;

namespace IronGrind.Gateway.Hosting;

/// <summary>
/// Resolves the account repository and the dummy hash when the host starts, so a missing or unsafe database connection
/// string fails startup and the dummy hash is computed before the first request (CR-AUTH-9 step 4). Tests that
/// replace <see cref="IAccountRepository"/> and the hasher skip the database and the Argon2 cost.
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
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
