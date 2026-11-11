namespace IronGrind.Gateway.Config;

/// <summary>
/// The gateway's boot wait (ADR-016 Decision 3): from construction until <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> have passed,
/// zones that were running get time to reconnect their links. One shared instance so the account map and the warm-spare
/// keeper use one formula and one start time (Gateway Story 007).
/// </summary>
public sealed class GatewayBootWait
{
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _end;

    /// <summary>Starts the boot wait now.</summary>
    /// <param name="options">Source of <c>GATEWAY_LINK_RETRY_SECONDS</c>.</param>
    /// <param name="time">Clock.</param>
    public GatewayBootWait(GatewayOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _end = _time.GetUtcNow() + TimeSpan.FromSeconds(2.0 * options.LinkRetrySeconds);
    }

    /// <summary>True once <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> have passed since construction (inclusive).</summary>
    public bool IsOver => _time.GetUtcNow() >= _end;
}
