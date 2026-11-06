using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IronGrind.Gateway.Config;

/// <summary>Registers the gateway options (ADR-016 Tuning Knobs). Host wiring calls this in Story 012.</summary>
public static class GatewayConfiguration
{
    /// <summary>
    /// Binds <see cref="GatewayOptions"/> and <see cref="AuthOptions"/> from the configuration root
    /// (flat keys, so environment variables override directly), registers their validators and
    /// validates both at host start.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddGatewayOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayOptions>().Bind(configuration).ValidateOnStart();
        services.AddOptions<AuthOptions>().Bind(configuration).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GatewayOptions>, GatewayOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>());
        return services;
    }
}
