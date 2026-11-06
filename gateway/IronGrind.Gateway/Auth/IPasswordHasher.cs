namespace IronGrind.Gateway.Auth;

/// <summary>
/// Argon2id password hashing (ADR-016 Decision 1, GDD CR-AUTH-5). Synchronous CPU work; callers
/// (Stories 008/009) run it behind the MAX_AUTH_PENDING semaphore.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password into a PHC string with a fresh 16-byte random salt.</summary>
    /// <param name="password">Plain password; UTF-8 encoded, never trimmed or truncated.</param>
    /// <returns>PHC string <c>$argon2id$v=19$m=..,t=..,p=..$salt$hash</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="password"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="password"/> is empty (Argon2 needs a password).</exception>
    string Hash(string password);

    /// <summary>Verifies a password against a PHC string using the parameters stored in that string.</summary>
    /// <param name="password">Plain password.</param>
    /// <param name="phc">Stored PHC string; null or malformed yields false.</param>
    /// <returns>True when the password matches; false on mismatch, an empty password, or any malformed PHC input (never throws for those).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="password"/> is null.</exception>
    bool Verify(string password, string phc);
}
