using IronGrind.Authentication;
using IronGrind.Gateway.Config;

namespace IronGrind.Gateway.Auth;

/// <summary>Password validation (GDD CR-AUTH-13; Authentication Story 005): length only, no charset rule.</summary>
public static class PasswordRules
{
    /// <summary>Validates the password length in Unicode code points.</summary>
    /// <param name="password">Raw password.</param>
    /// <param name="options">Length knobs.</param>
    /// <returns>The failure, or null when valid.</returns>
    public static RegistrationError? Validate(string password, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(options);

        int length = AuthText.CodePointCount(password);
        if (length < options.PasswordMinLength)
        {
            return RegistrationError.PasswordTooShort;
        }

        if (length > options.PasswordMaxLength)
        {
            return RegistrationError.PasswordTooLong;
        }

        return null;
    }
}
