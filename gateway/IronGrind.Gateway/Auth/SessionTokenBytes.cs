using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// The one definition of a valid session token on the gateway (CR-TOK-1, ADR-013, ADR-016 Decision 4): 16 bytes from
/// <see cref="RandomNumberGenerator"/>, never all zeros (all zeros means "no token" on the wire).
/// </summary>
internal static class SessionTokenBytes
{
    /// <summary>Token length in bytes.</summary>
    public const int LENGTH = 16;

    /// <summary>True when <paramref name="token"/> is non-null, <see cref="LENGTH"/> bytes and not all zeros.</summary>
    /// <param name="token">The candidate token.</param>
    /// <returns>Whether the token is usable.</returns>
    public static bool IsValid([NotNullWhen(true)] byte[]? token)
    {
        return token is not null && token.Length == LENGTH && !Array.TrueForAll(token, b => b == 0);
    }

    /// <summary>A fresh random token; regenerated on the (practically impossible) all-zero result.</summary>
    /// <returns>A valid token.</returns>
    public static byte[] NewRandom()
    {
        byte[] token = RandomNumberGenerator.GetBytes(LENGTH);
        while (!IsValid(token))
        {
            token = RandomNumberGenerator.GetBytes(LENGTH);
        }

        return token;
    }
}
