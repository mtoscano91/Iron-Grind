using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Result of <see cref="IAccountRepository.RegisterAsync"/> (ADR-016 Decision 5).
/// </summary>
public readonly struct RegistrationOutcome
{
    private RegistrationOutcome(bool success, AccountID accountID, CharacterID characterID, RegistrationError error)
    {
        Success = success;
        AccountID = accountID;
        CharacterID = characterID;
        Error = error;
    }

    /// <summary>True when the account and character rows were committed.</summary>
    public bool Success { get; }

    /// <summary>The new account's ID; meaningful only when <see cref="Success"/>.</summary>
    public AccountID AccountID { get; }

    /// <summary>The new character's ID; meaningful only when <see cref="Success"/>.</summary>
    public CharacterID CharacterID { get; }

    /// <summary>
    /// The failure code; ignored when <see cref="Success"/>. On success it holds <c>default</c>, which is
    /// <see cref="RegistrationError.UsernameTaken"/> (value 0) — always check <see cref="Success"/> first.
    /// </summary>
    public RegistrationError Error { get; }

    /// <summary>Creates a successful outcome.</summary>
    /// <param name="accountID">The committed account's ID.</param>
    /// <param name="characterID">The committed character's ID.</param>
    public static RegistrationOutcome Succeeded(AccountID accountID, CharacterID characterID) =>
        new(true, accountID, characterID, default);

    /// <summary>Creates a failed outcome.</summary>
    /// <param name="error">The failure code.</param>
    public static RegistrationOutcome Failed(RegistrationError error) =>
        new(false, AccountID.Invalid, CharacterID.Invalid, error);
}
