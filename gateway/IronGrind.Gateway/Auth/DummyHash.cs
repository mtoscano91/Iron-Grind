using System.Security.Cryptography;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// A PHC hash of a random secret, computed once with the live Argon2 parameters, used for the
/// constant-time unknown-username path (GDD CR-AUTH-9 step 4). Registered as a singleton by
/// Story 012 and resolved at startup so the cost is paid before the first request.
/// </summary>
public sealed class DummyHash
{
    private const int SECRET_BYTES = 32;

    /// <summary>Computes the dummy hash once.</summary>
    /// <param name="hasher">Hasher configured with the live parameters.</param>
    public DummyHash(IPasswordHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SECRET_BYTES));
        Phc = hasher.Hash(secret);
    }

    /// <summary>The dummy PHC string (same instance on every read).</summary>
    public string Phc { get; }
}
