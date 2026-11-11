using Microsoft.Extensions.Configuration;

namespace IronGrind.Gateway.Config;

/// <summary>
/// Gateway process knobs (ADR-016 Tuning Knobs). Bound from the configuration root so each
/// knob name is also the environment variable name. Defaults equal the ADR-016 defaults.
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>ADR-016 knob <c>GATEWAY_HTTP_PORT</c>: HTTP listen port. Valid 1-65535.</summary>
    [ConfigurationKeyName("GATEWAY_HTTP_PORT")]
    public int HttpPort { get; set; } = 8080;

    /// <summary>ADR-016 knob <c>GATEWAY_ZONE_LINK_PORT</c>: zone-link listen port. Valid 1-65535, differs from the HTTP port.</summary>
    [ConfigurationKeyName("GATEWAY_ZONE_LINK_PORT")]
    public int ZoneLinkPort { get; set; } = 7900;

    /// <summary>ADR-016 knob <c>GATEWAY_ZONE_REQUEST_TIMEOUT_MS</c>: per-request zone timeout in ms. Must be &gt; 0.</summary>
    [ConfigurationKeyName("GATEWAY_ZONE_REQUEST_TIMEOUT_MS")]
    public int ZoneRequestTimeoutMs { get; set; } = 2000;

    /// <summary>ADR-016 knob <c>GATEWAY_EVICT_TIMEOUT_MS</c>: eviction timeout in ms. Must be greater than the zone request timeout.</summary>
    [ConfigurationKeyName("GATEWAY_EVICT_TIMEOUT_MS")]
    public int EvictTimeoutMs { get; set; } = 11000;

    /// <summary>ADR-016 knob <c>GATEWAY_LINK_RETRY_SECONDS</c>: zone-link reconnect delay in seconds. Must be &gt; 0.</summary>
    [ConfigurationKeyName("GATEWAY_LINK_RETRY_SECONDS")]
    public int LinkRetrySeconds { get; set; } = 2;

    /// <summary>ADR-016 knob <c>GATEWAY_LINK_MAX_FRAME_BYTES</c>: largest accepted zone-link frame length field in bytes. Must be &gt; 0.</summary>
    [ConfigurationKeyName("GATEWAY_LINK_MAX_FRAME_BYTES")]
    public int LinkMaxFrameBytes { get; set; } = 65536;

    /// <summary>ADR-016 knob <c>MAX_AUTH_PENDING</c>: max concurrent hash operations. Must be &gt; 0.</summary>
    [ConfigurationKeyName("MAX_AUTH_PENDING")]
    public int MaxAuthPending { get; set; } = 10;

    /// <summary>Registry knob <c>ZONE_OPEN_THRESHOLD</c> (CR-ZI-6 rule 3): below this player count a zone is an "open" fill-first target. Valid 20-48 and at most <c>MAX_PLAYERS_PER_ZONE</c> - 2.</summary>
    [ConfigurationKeyName("ZONE_OPEN_THRESHOLD")]
    public int ZoneOpenThreshold { get; set; } = 35;

    /// <summary>Registry knob <c>MAX_PLAYERS_PER_ZONE</c>: sanity bound on a zone's hard cap (the hard cap is the lower of this and the zone's registered capacity). Valid 10-100; because <c>ZONE_OPEN_THRESHOLD</c> is at least 20 and at most this - 2, the lowest cap that validates is 22.</summary>
    [ConfigurationKeyName("MAX_PLAYERS_PER_ZONE")]
    public int MaxPlayersPerZone { get; set; } = 50;

    /// <summary>Registry knob <c>ZONE_UDP_PORT_FIRST</c> (zone-instancing.md CR-ZI-6, Gateway Story 007): first UDP game port the warm-spare keeper hands to a zone it starts. Valid 1024-65535.</summary>
    [ConfigurationKeyName("ZONE_UDP_PORT_FIRST")]
    public int ZoneUdpPortFirst { get; set; } = 7777;

    /// <summary>Registry knob <c>ZONE_UDP_PORT_COUNT</c> (Gateway Story 007): number of ports in the zone port range. Valid 1-256; the range must end at 65535 or lower and contain neither the HTTP nor the zone-link port.</summary>
    [ConfigurationKeyName("ZONE_UDP_PORT_COUNT")]
    public int ZoneUdpPortCount { get; set; } = 16;

    /// <summary>Registry knob <c>ZONE_LOAD_TIMEOUT_SECONDS</c> (zone-instancing.md Tuning Knobs, Gateway Story 007): how long a started zone may take to register, and the minimum spacing between start attempts. Valid 10-120.</summary>
    [ConfigurationKeyName("ZONE_LOAD_TIMEOUT_SECONDS")]
    public int ZoneLoadTimeoutSeconds { get; set; } = 30;
}
