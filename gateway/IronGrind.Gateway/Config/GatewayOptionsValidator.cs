using Microsoft.Extensions.Options;

namespace IronGrind.Gateway.Config;

/// <summary>Validates <see cref="GatewayOptions"/> (ADR-016 Tuning Knobs); collects every failure.</summary>
public sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        var failures = new List<string>();

        if (options.HttpPort < 1 || options.HttpPort > 65535)
        {
            failures.Add($"GATEWAY_HTTP_PORT must be between 1 and 65535 (was {options.HttpPort})");
        }

        if (options.ZoneLinkPort < 1 || options.ZoneLinkPort > 65535)
        {
            failures.Add($"GATEWAY_ZONE_LINK_PORT must be between 1 and 65535 (was {options.ZoneLinkPort})");
        }

        if (options.HttpPort == options.ZoneLinkPort)
        {
            failures.Add($"GATEWAY_HTTP_PORT and GATEWAY_ZONE_LINK_PORT must differ (both {options.HttpPort})");
        }

        if (options.ZoneRequestTimeoutMs <= 0)
        {
            failures.Add($"GATEWAY_ZONE_REQUEST_TIMEOUT_MS must be greater than 0 (was {options.ZoneRequestTimeoutMs})");
        }

        if (options.EvictTimeoutMs <= 0)
        {
            failures.Add($"GATEWAY_EVICT_TIMEOUT_MS must be greater than 0 (was {options.EvictTimeoutMs})");
        }

        if (options.EvictTimeoutMs <= options.ZoneRequestTimeoutMs)
        {
            failures.Add(
                $"GATEWAY_EVICT_TIMEOUT_MS must be greater than GATEWAY_ZONE_REQUEST_TIMEOUT_MS (was {options.EvictTimeoutMs} vs {options.ZoneRequestTimeoutMs})");
        }

        if (options.LinkRetrySeconds <= 0)
        {
            failures.Add($"GATEWAY_LINK_RETRY_SECONDS must be greater than 0 (was {options.LinkRetrySeconds})");
        }

        if (options.LinkMaxFrameBytes <= 0)
        {
            failures.Add($"GATEWAY_LINK_MAX_FRAME_BYTES must be greater than 0 (was {options.LinkMaxFrameBytes})");
        }

        if (options.MaxAuthPending <= 0)
        {
            failures.Add($"MAX_AUTH_PENDING must be greater than 0 (was {options.MaxAuthPending})");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
