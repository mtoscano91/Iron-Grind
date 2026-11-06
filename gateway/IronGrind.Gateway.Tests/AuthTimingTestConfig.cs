namespace IronGrind.Gateway.Tests;

/// <summary>Constants for the AC-AUTH-16 timing oracle test (Authentication Story 009).</summary>
internal static class AuthTimingTestConfig
{
    /// <summary>Every sample must take at least this long, proving real Argon2id ran on both paths.</summary>
    public const int AUTH_TIMING_FLOOR_MS = 150;

    /// <summary>The mean difference between the unknown-username and wrong-password paths must stay below this.</summary>
    public const int AUTH_TIMING_DELTA_THRESHOLD_MS = 20;

    /// <summary>Argon2 memory for the test hasher (inside the GDD safe range 19456-65536); slower than live parameters on purpose (TD-093).</summary>
    public const int ARGON2_MEMORY_SIZE_KB = 65536;

    /// <summary>Samples per path.</summary>
    public const int SAMPLE_COUNT = 20;
}
