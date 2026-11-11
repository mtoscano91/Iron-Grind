namespace IronGrind.Gateway.Config;

/// <summary>
/// The gateway's boot wait (ADR-016 Decision 3): from its start until <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> have passed,
/// zones that were running get time to reconnect their links. One shared instance so the account map, the warm-spare
/// keeper and the login path use one formula and one start time (Gateway Story 007). In the running gateway the wait
/// is created not started and the zone-link listener starts it once it is bound (Story 009), so the window opens when
/// zones can actually reconnect. Unit tests and fixed-clock callers use the constructor, which starts the wait at once.
/// </summary>
public sealed class GatewayBootWait
{
    private readonly TimeSpan _length;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private DateTimeOffset? _end;

    /// <summary>Creates the wait and starts it now.</summary>
    /// <param name="options">Source of <c>GATEWAY_LINK_RETRY_SECONDS</c>.</param>
    /// <param name="time">Clock.</param>
    public GatewayBootWait(GatewayOptions options, TimeProvider time)
        : this(options, time, startNow: true)
    {
    }

    private GatewayBootWait(GatewayOptions options, TimeProvider time, bool startNow)
    {
        ArgumentNullException.ThrowIfNull(options);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _length = TimeSpan.FromSeconds(2.0 * options.LinkRetrySeconds);
        if (startNow)
        {
            Start();
        }
    }

    /// <summary>Creates a wait that has not started: <see cref="IsOver"/> stays false until <see cref="Start"/> is called.</summary>
    /// <param name="options">Source of <c>GATEWAY_LINK_RETRY_SECONDS</c>.</param>
    /// <param name="time">Clock.</param>
    /// <returns>The not-started wait.</returns>
    public static GatewayBootWait NotStarted(GatewayOptions options, TimeProvider time) => new(options, time, startNow: false);

    /// <summary>True once the wait was started and <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> have passed since then (inclusive).</summary>
    public bool IsOver
    {
        get
        {
            lock (_lock)
            {
                return _end is { } end && _time.GetUtcNow() >= end;
            }
        }
    }

    /// <summary>True once <see cref="Start"/> has been called (or the wait was created started).</summary>
    public bool IsStarted
    {
        get
        {
            lock (_lock)
            {
                return _end is not null;
            }
        }
    }

    /// <summary>Starts the wait now. Idempotent: a second call does not move the end.</summary>
    public void Start()
    {
        lock (_lock)
        {
            _end ??= _time.GetUtcNow() + _length;
        }
    }
}
