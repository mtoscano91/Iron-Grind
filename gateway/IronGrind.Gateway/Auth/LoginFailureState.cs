namespace IronGrind.Gateway.Auth;

/// <summary>
/// The failure counter values just written by <see cref="IAccountRepository.RecordFailureAsync"/>.
/// </summary>
/// <param name="LoginFailureCount">The new consecutive failure count.</param>
/// <param name="LockoutUntilUtc">The new lockout end (<see cref="DateTimeKind.Utc"/>), or null when no lockout applies.</param>
public readonly record struct LoginFailureState(int LoginFailureCount, DateTime? LockoutUntilUtc);
