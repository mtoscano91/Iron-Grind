using IronGrind.Gateway.Launch;
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

        if (options.MaxPlayersPerZone < 10 || options.MaxPlayersPerZone > 100)
        {
            failures.Add($"MAX_PLAYERS_PER_ZONE must be between 10 and 100 (was {options.MaxPlayersPerZone})");
        }

        if (options.ZoneOpenThreshold < 20 || options.ZoneOpenThreshold > 48)
        {
            failures.Add($"ZONE_OPEN_THRESHOLD must be between 20 and 48 (was {options.ZoneOpenThreshold})");
        }

        if (options.ZoneOpenThreshold > options.MaxPlayersPerZone - 2)
        {
            failures.Add(
                $"ZONE_OPEN_THRESHOLD must be at most MAX_PLAYERS_PER_ZONE - 2 (was {options.ZoneOpenThreshold} vs {options.MaxPlayersPerZone})");
        }

        if (options.ZoneUdpPortFirst < 1024 || options.ZoneUdpPortFirst > 65535)
        {
            failures.Add($"ZONE_UDP_PORT_FIRST must be between 1024 and 65535 (was {options.ZoneUdpPortFirst})");
        }

        if (options.ZoneUdpPortCount < 1 || options.ZoneUdpPortCount > 256)
        {
            failures.Add($"ZONE_UDP_PORT_COUNT must be between 1 and 256 (was {options.ZoneUdpPortCount})");
        }

        long rangeEnd = (long)options.ZoneUdpPortFirst + options.ZoneUdpPortCount - 1;
        if (rangeEnd > 65535)
        {
            failures.Add(
                $"ZONE_UDP_PORT_FIRST + ZONE_UDP_PORT_COUNT - 1 must be at most 65535 (was {options.ZoneUdpPortFirst} + {options.ZoneUdpPortCount} - 1)");
        }

        if (options.ZoneUdpPortCount >= 1 && InRange(options.HttpPort, options.ZoneUdpPortFirst, rangeEnd))
        {
            failures.Add($"The zone port range (ZONE_UDP_PORT_FIRST, ZONE_UDP_PORT_COUNT) must not contain GATEWAY_HTTP_PORT ({options.HttpPort})");
        }

        if (options.ZoneUdpPortCount >= 1 && InRange(options.ZoneLinkPort, options.ZoneUdpPortFirst, rangeEnd))
        {
            failures.Add($"The zone port range (ZONE_UDP_PORT_FIRST, ZONE_UDP_PORT_COUNT) must not contain GATEWAY_ZONE_LINK_PORT ({options.ZoneLinkPort})");
        }

        if (options.ZoneLoadTimeoutSeconds < 10 || options.ZoneLoadTimeoutSeconds > 120)
        {
            failures.Add($"ZONE_LOAD_TIMEOUT_SECONDS must be between 10 and 120 (was {options.ZoneLoadTimeoutSeconds})");
        }

        if (!ZoneLauncherFactory.Names.Contains(options.ZoneLauncher, StringComparer.Ordinal))
        {
            failures.Add($"ZONE_LAUNCHER must be one of {string.Join(", ", ZoneLauncherFactory.Names)} (was \"{options.ZoneLauncher}\")");
        }

        if (options.ZoneLauncher == ZoneLauncherFactory.PROCESS)
        {
            if (string.IsNullOrWhiteSpace(options.ZoneProcessPath))
            {
                failures.Add("ZONE_PROCESS_PATH must be set when ZONE_LAUNCHER is process");
            }

            if (string.IsNullOrWhiteSpace(options.ZoneServerConfig))
            {
                failures.Add("ZONE_SERVER_CONFIG must be set when ZONE_LAUNCHER is process");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool InRange(int port, int first, long last) => port >= first && port <= last;
}
