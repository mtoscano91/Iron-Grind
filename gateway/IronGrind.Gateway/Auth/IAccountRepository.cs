using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Account persistence for the gateway (ADR-016 Decision 5): registration (Story 006) and login state (Story 007).
/// </summary>
public interface IAccountRepository
{
    /// <summary>
    /// Creates an account and its first character in one transaction. Uniqueness is enforced by the
    /// database (no read-then-write check).
    /// </summary>
    /// <param name="usernameLower">Normalized (lower-case) username.</param>
    /// <param name="passwordHashPhc">Argon2id PHC string; never logged.</param>
    /// <param name="characterName">Character name exactly as sent (case kept).</param>
    /// <param name="classType">Playable class value.</param>
    /// <param name="ct">
    /// Cancellation token; cancellation propagates as an exception. A cancellation during the commit leaves
    /// the outcome unknown (the rows may be committed), so callers past the request's abort check pass
    /// <see cref="CancellationToken.None"/> (Story 008/012).
    /// </param>
    /// <returns>Success with the new IDs, or UsernameTaken, CharacterNameTaken or ServerError.</returns>
    Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct);

    /// <summary>Reads an account by normalized username. Database errors propagate.</summary>
    /// <param name="usernameLower">Normalized (lower-case) username.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account, or null when no row exists.</returns>
    Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct);

    /// <summary>
    /// Counts one failed login: in one transaction, locks the account row, computes the new count and the F-AUTH-1
    /// lockout in C# (<see cref="LockoutPolicy"/>), and writes both. An existing lockout is always replaced.
    /// </summary>
    /// <param name="account">The account that failed to log in; its row must exist.</param>
    /// <param name="utcNow">Current time, <see cref="DateTimeKind.Utc"/> (injected clock).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The values just written.</returns>
    /// <exception cref="ArgumentException"><paramref name="utcNow"/> is not <see cref="DateTimeKind.Utc"/>.</exception>
    /// <exception cref="InvalidOperationException">No row exists for <paramref name="account"/>.</exception>
    Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct);

    /// <summary>Sets the failure count to 0 and clears the lockout (successful login).</summary>
    /// <param name="account">The account; its row must exist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No row exists for <paramref name="account"/>.</exception>
    Task ResetFailuresAsync(AccountID account, CancellationToken ct);

    /// <summary>
    /// Reads the account's character ID (<c>character_records.account_id</c>). Assumes at most one character row
    /// per account (MVP); two rows make the query throw <see cref="InvalidOperationException"/> — a data fault.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The character ID, or <see cref="CharacterID.Invalid"/> when the account has no character row.</returns>
    Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct);

    /// <summary>
    /// Reads <c>character_records.last_zone_id</c>. Not in ADR-016's interface listing; it follows Decision 4 step 4
    /// ("Rule 2 reads <c>character_records.last_zone_id</c>") and the gateway role's column grant, and is used by the
    /// gateway epic's router. Assumes at most one character row per account, as <see cref="GetCharacterIDAsync"/>.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The zone, or <see cref="ZoneID.Invalid"/> when the stored value is 0 (first login) or no character row exists.</returns>
    Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct);
}
