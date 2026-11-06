using IronGrind.Gateway.Config;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// F-AUTH-1 lockout lookup (GDD design/gdd/authentication.md; Authentication Story 005). A table lookup, not a formula:
/// index <c>failures - LoginFailureLimit</c>, clamped to the last entry. The clock is injected by the caller.
/// </summary>
public sealed class LockoutPolicy
{
    private readonly AuthOptions _options;

    /// <summary>Creates a policy reading its knobs from <paramref name="options"/>.</summary>
    /// <param name="options">Auth knobs.</param>
    public LockoutPolicy(AuthOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Lockout duration in seconds for a consecutive failure count (0 = not locked).</summary>
    /// <param name="failures">Consecutive failed logins.</param>
    public int Duration(int failures)
    {
        int limit = _options.LoginFailureLimit;
        IReadOnlyList<int> table = _options.EffectiveLockoutTableSeconds;
        if (failures < limit || table.Count == 0)
        {
            return 0;
        }

        int index = Math.Min(failures - limit, table.Count - 1);
        return table[index];
    }

    /// <summary>The UTC instant the lockout ends, or null when no lockout applies.</summary>
    /// <param name="failures">Consecutive failed logins.</param>
    /// <param name="utcNow">Current time as <see cref="DateTimeKind.Utc"/> (injected; Story 007 stores the result in a <c>timestamptz</c> column).</param>
    public DateTime? LockoutUntil(int failures, DateTime utcNow)
    {
        int seconds = Duration(failures);
        return seconds == 0 ? null : utcNow.AddSeconds(seconds);
    }
}
