using Microsoft.Extensions.Configuration;

namespace IronGrind.Gateway.Config;

/// <summary>
/// Authentication knobs (GDD design/gdd/authentication.md Tuning Knobs, ADR-016). Bound from the
/// configuration root so each knob name is also the environment variable name.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Default lockout table (GDD <c>LOCKOUT_TABLE_SECONDS</c>).</summary>
    public static readonly IReadOnlyList<int> DEFAULT_LOCKOUT_TABLE_SECONDS = new[] { 30, 60, 120, 300, 600 };

    /// <summary>GDD knob <c>LOGIN_FAILURE_LIMIT</c>: consecutive failures before lockout. Safe range 3-10.</summary>
    [ConfigurationKeyName("LOGIN_FAILURE_LIMIT")]
    public int LoginFailureLimit { get; set; } = 5;

    /// <summary>
    /// GDD knob <c>LOCKOUT_TABLE_SECONDS</c> as bound from configuration. Null means "not configured"
    /// (so config array binding replaces rather than appends to a default); read
    /// <see cref="EffectiveLockoutTableSeconds"/> for the value in force.
    /// </summary>
    [ConfigurationKeyName("LOCKOUT_TABLE_SECONDS")]
    public int[]? LockoutTableSeconds { get; set; }

    /// <summary>The lockout table in force: the configured array, or the GDD default when none is configured.</summary>
    public IReadOnlyList<int> EffectiveLockoutTableSeconds => LockoutTableSeconds ?? DEFAULT_LOCKOUT_TABLE_SECONDS;

    /// <summary>GDD knob <c>ARGON2_ITERATIONS</c> (CR-AUTH-5): time cost. Safe range 2-4.</summary>
    [ConfigurationKeyName("ARGON2_ITERATIONS")]
    public int Argon2Iterations { get; set; } = 2;

    /// <summary>GDD knob <c>ARGON2_MEMORY_SIZE_KB</c> (CR-AUTH-5): memory cost in KiB. Safe range 19456-65536.</summary>
    [ConfigurationKeyName("ARGON2_MEMORY_SIZE_KB")]
    public int Argon2MemorySizeKb { get; set; } = 19456;

    /// <summary>GDD knob <c>ARGON2_PARALLELISM</c> (CR-AUTH-5): lanes. Safe range 1-2.</summary>
    [ConfigurationKeyName("ARGON2_PARALLELISM")]
    public int Argon2Parallelism { get; set; } = 1;

    /// <summary>GDD knob <c>USERNAME_MIN_LENGTH</c>. Safe range 2-6.</summary>
    [ConfigurationKeyName("USERNAME_MIN_LENGTH")]
    public int UsernameMinLength { get; set; } = 3;

    /// <summary>GDD knob <c>USERNAME_MAX_LENGTH</c>. Safe range 12-32.</summary>
    [ConfigurationKeyName("USERNAME_MAX_LENGTH")]
    public int UsernameMaxLength { get; set; } = 24;

    /// <summary>GDD knob <c>CHARACTER_NAME_MIN_LENGTH</c>. Safe range 2-4.</summary>
    [ConfigurationKeyName("CHARACTER_NAME_MIN_LENGTH")]
    public int CharacterNameMinLength { get; set; } = 3;

    /// <summary>GDD knob <c>CHARACTER_NAME_MAX_LENGTH</c>. Safe range 12-24.</summary>
    [ConfigurationKeyName("CHARACTER_NAME_MAX_LENGTH")]
    public int CharacterNameMaxLength { get; set; } = 16;

    /// <summary>GDD knob <c>PASSWORD_MIN_LENGTH</c>. Safe range 6-12.</summary>
    [ConfigurationKeyName("PASSWORD_MIN_LENGTH")]
    public int PasswordMinLength { get; set; } = 8;

    /// <summary>GDD knob <c>PASSWORD_MAX_LENGTH</c>. Safe range 72-128.</summary>
    [ConfigurationKeyName("PASSWORD_MAX_LENGTH")]
    public int PasswordMaxLength { get; set; } = 72;
}
