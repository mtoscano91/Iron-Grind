using Microsoft.Extensions.Options;

namespace IronGrind.Gateway.Config;

/// <summary>
/// Validates <see cref="AuthOptions"/> against the GDD Tuning Knobs safe ranges and joint
/// constraints (design/gdd/authentication.md); collects every failure.
/// </summary>
public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        Range(failures, "LOGIN_FAILURE_LIMIT", options.LoginFailureLimit, 3, 10);
        ValidateLockoutTable(failures, options.EffectiveLockoutTableSeconds);
        Range(failures, "ARGON2_ITERATIONS", options.Argon2Iterations, 2, 4);
        Range(failures, "ARGON2_MEMORY_SIZE_KB", options.Argon2MemorySizeKb, 19456, 65536);
        Range(failures, "ARGON2_PARALLELISM", options.Argon2Parallelism, 1, 2);
        Range(failures, "USERNAME_MIN_LENGTH", options.UsernameMinLength, 2, 6);
        Range(failures, "USERNAME_MAX_LENGTH", options.UsernameMaxLength, 12, 32);
        Range(failures, "CHARACTER_NAME_MIN_LENGTH", options.CharacterNameMinLength, 2, 4);
        Range(failures, "CHARACTER_NAME_MAX_LENGTH", options.CharacterNameMaxLength, 12, 24);
        Range(failures, "PASSWORD_MIN_LENGTH", options.PasswordMinLength, 6, 12);
        Range(failures, "PASSWORD_MAX_LENGTH", options.PasswordMaxLength, 72, 128);

        MinBelowMax(failures, "USERNAME_MIN_LENGTH", options.UsernameMinLength, "USERNAME_MAX_LENGTH", options.UsernameMaxLength);
        MinBelowMax(failures, "CHARACTER_NAME_MIN_LENGTH", options.CharacterNameMinLength, "CHARACTER_NAME_MAX_LENGTH", options.CharacterNameMaxLength);
        MinBelowMax(failures, "PASSWORD_MIN_LENGTH", options.PasswordMinLength, "PASSWORD_MAX_LENGTH", options.PasswordMaxLength);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Range(List<string> failures, string key, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            failures.Add($"{key} must be between {min} and {max} (was {value})");
        }
    }

    private static void MinBelowMax(List<string> failures, string minKey, int min, string maxKey, int max)
    {
        if (min >= max)
        {
            failures.Add($"{minKey} must be less than {maxKey} (was {min} vs {max})");
        }
    }

    private static void ValidateLockoutTable(List<string> failures, IReadOnlyList<int> table)
    {
        const string KEY = "LOCKOUT_TABLE_SECONDS";
        if (table.Count == 0)
        {
            failures.Add($"{KEY} must not be empty");
            return;
        }

        if (table[0] < 15 || table[0] > 120)
        {
            failures.Add($"{KEY} first entry must be between 15 and 120 (was {table[0]})");
        }

        int last = table[table.Count - 1];
        if (last < 300 || last > 3600)
        {
            failures.Add($"{KEY} last entry must be between 300 and 3600 (was {last})");
        }

        for (int i = 1; i < table.Count; i++)
        {
            if (table[i] < table[i - 1])
            {
                failures.Add($"{KEY} must be non-decreasing (entry {i} is {table[i]} after {table[i - 1]})");
                break;
            }
        }
    }
}
