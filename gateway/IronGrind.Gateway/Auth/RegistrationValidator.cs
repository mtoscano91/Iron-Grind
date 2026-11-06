using IronGrind.Authentication;
using IronGrind.Gateway.Config;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Registration input validation in CR-AUTH-8 step 2 order: username, password, character name, class.
/// The first failure wins. Database uniqueness checks are Story 006.
/// </summary>
public sealed class RegistrationValidator
{
    private readonly AuthOptions _options;

    /// <summary>Creates a validator reading its knobs from <paramref name="options"/>.</summary>
    /// <param name="options">Auth knobs.</param>
    public RegistrationValidator(AuthOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Validates a registration request.</summary>
    /// <param name="request">The decoded request.</param>
    /// <returns>The first failure, or null when valid.</returns>
    public RegistrationError? Validate(RegistrationRequest request)
    {
        string username = UsernameRules.Normalize(request.Username);

        RegistrationError? error = UsernameRules.Validate(username, _options);
        if (error is not null)
        {
            return error;
        }

        error = PasswordRules.Validate(request.Password, _options);
        if (error is not null)
        {
            return error;
        }

        error = CharacterNameRules.Validate(request.CharacterName, username, _options);
        if (error is not null)
        {
            return error;
        }

        return PlayableClasses.IsPlayable(request.ClassType) ? null : RegistrationError.ClassInvalid;
    }
}
