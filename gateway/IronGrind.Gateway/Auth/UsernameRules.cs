using IronGrind.Authentication;
using IronGrind.Gateway.Config;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Username normalization and validation (GDD design/gdd/authentication.md CR-AUTH-7, EC-AUTH-9;
/// Authentication Story 005; ADR-016 Decision 6).
/// </summary>
public static class UsernameRules
{
    /// <summary>Lowercases with the invariant culture (never culture-sensitive: Turkish-I).</summary>
    /// <param name="username">Raw username.</param>
    /// <returns>The normalized username.</returns>
    public static string Normalize(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        return username.ToLowerInvariant();
    }

    /// <summary>
    /// Validates an already normalized username: length in code points first, then ASCII [a-z0-9_].
    /// </summary>
    /// <param name="normalized">Username after <see cref="Normalize"/>.</param>
    /// <param name="options">Length knobs.</param>
    /// <returns>The first failure, or null when valid.</returns>
    public static RegistrationError? Validate(string normalized, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        ArgumentNullException.ThrowIfNull(options);

        int length = AuthText.CodePointCount(normalized);
        if (length < options.UsernameMinLength)
        {
            return RegistrationError.UsernameTooShort;
        }

        if (length > options.UsernameMaxLength)
        {
            return RegistrationError.UsernameTooLong;
        }

        foreach (char c in normalized)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok)
            {
                return RegistrationError.UsernameInvalidChars;
            }
        }

        return null;
    }
}
