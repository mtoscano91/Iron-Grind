using IronGrind.Authentication;
using IronGrind.Gateway.Config;

namespace IronGrind.Gateway.Auth;

/// <summary>Character name validation (GDD CR-AUTH-13; Authentication Story 005).</summary>
public static class CharacterNameRules
{
    /// <summary>
    /// Validates in order: code-point length, ASCII [A-Za-z0-9_], then not equal to the username (case-insensitive).
    /// </summary>
    /// <param name="characterName">Requested character name.</param>
    /// <param name="normalizedUsername">The normalized account username.</param>
    /// <param name="options">Length knobs.</param>
    /// <returns>The first failure, or null when valid.</returns>
    public static RegistrationError? Validate(string characterName, string normalizedUsername, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(characterName);
        ArgumentNullException.ThrowIfNull(normalizedUsername);
        ArgumentNullException.ThrowIfNull(options);

        int length = AuthText.CodePointCount(characterName);
        if (length < options.CharacterNameMinLength)
        {
            return RegistrationError.CharacterNameTooShort;
        }

        if (length > options.CharacterNameMaxLength)
        {
            return RegistrationError.CharacterNameTooLong;
        }

        foreach (char c in characterName)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok)
            {
                return RegistrationError.CharacterNameInvalidChars;
            }
        }

        if (string.Equals(characterName, normalizedUsername, StringComparison.OrdinalIgnoreCase))
        {
            return RegistrationError.CharacterNameMatchesUsername;
        }

        return null;
    }
}
