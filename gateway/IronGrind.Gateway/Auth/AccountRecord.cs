using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// An <c>accounts</c> row as the login flow needs it (Authentication Story 007, ADR-016 Decision 5).
/// </summary>
/// <param name="AccountID">The account's ID.</param>
/// <param name="PasswordHashPhc">Argon2id PHC string; never logged.</param>
/// <param name="IsBanned">True when the account is banned.</param>
/// <param name="LoginFailureCount">Consecutive failed logins.</param>
/// <param name="LockoutUntilUtc">End of the current lockout, or null; <see cref="DateTimeKind.Utc"/> when set.</param>
public sealed record AccountRecord(
    AccountID AccountID,
    string PasswordHashPhc,
    bool IsBanned,
    int LoginFailureCount,
    DateTime? LockoutUntilUtc);
