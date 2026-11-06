using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IronGrind.Gateway.Config;
using Konscious.Security.Cryptography;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Argon2id hasher (ADR-016 Decision 1, GDD CR-AUTH-5) built on Konscious. Parameters for new hashes
/// come from <see cref="AuthOptions"/> (ARGON2_ITERATIONS, ARGON2_MEMORY_SIZE_KB, ARGON2_PARALLELISM);
/// verification reads them from the stored PHC string. Salt comes from <see cref="RandomNumberGenerator"/>
/// (ADR-013). Never logs the password, PHC string or derived hash.
/// </summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int SALT_BYTES = 16;
    private const int HASH_BYTES = 32;
    private const int ARGON2_VERSION = 19;

    private const int MIN_MEMORY_KB = 8;
    // Upper bound of the GDD ARGON2_MEMORY_SIZE_KB safe range: no hash we ever wrote is larger, and a
    // corrupted row cannot make Verify allocate more than this (x MAX_AUTH_PENDING concurrent calls).
    private const int MAX_MEMORY_KB = 65536;
    private const int MIN_ITERATIONS = 1;
    private const int MAX_ITERATIONS = 16;
    private const int MIN_PARALLELISM = 1;
    private const int MAX_PARALLELISM = 16;
    private const int MIN_SALT_BYTES = 8;
    private const int MAX_SALT_BYTES = 64;
    private const int MIN_HASH_BYTES = 16;
    private const int MAX_HASH_BYTES = 64;

    private readonly int _iterations;
    private readonly int _memoryKb;
    private readonly int _parallelism;

    /// <summary>Creates a hasher using the (already validated) Argon2 knobs.</summary>
    /// <param name="options">Validated authentication options.</param>
    public Argon2idPasswordHasher(AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _iterations = options.Argon2Iterations;
        _memoryKb = options.Argon2MemorySizeKb;
        _parallelism = options.Argon2Parallelism;
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        byte[] salt = RandomNumberGenerator.GetBytes(SALT_BYTES);
        byte[] hash = Derive(password, salt, _iterations, _memoryKb, _parallelism, HASH_BYTES);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v={ARGON2_VERSION}$m={_memoryKb},t={_iterations},p={_parallelism}${ToBase64NoPad(salt)}${ToBase64NoPad(hash)}");
    }

    /// <inheritdoc />
    public bool Verify(string password, string phc)
    {
        ArgumentNullException.ThrowIfNull(password);

        // Konscious rejects an empty password. No stored hash is of an empty password
        // (PASSWORD_MIN_LENGTH >= 6), and this exit depends only on the input, not the account.
        if (password.Length == 0)
        {
            return false;
        }

        if (!TryParse(phc, out int memoryKb, out int iterations, out int parallelism, out byte[] salt, out byte[] expected))
        {
            return false;
        }

        byte[] actual = Derive(password, salt, iterations, memoryKb, parallelism, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int memoryKb, int parallelism, int length)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = iterations,
            MemorySize = memoryKb,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(length);
    }

    private static string ToBase64NoPad(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryParse(string? phc, out int memoryKb, out int iterations, out int parallelism, out byte[] salt, out byte[] hash)
    {
        memoryKb = iterations = parallelism = 0;
        salt = hash = Array.Empty<byte>();

        if (string.IsNullOrEmpty(phc))
        {
            return false;
        }

        string[] parts = phc.Split('$');
        // "$argon2id$v=19$m=..,t=..,p=..$salt$hash" splits into 6 parts, the first empty.
        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id")
        {
            return false;
        }

        if (!parts[2].StartsWith("v=", StringComparison.Ordinal)
            || !TryParseInt(parts[2].AsSpan(2), out int version)
            || version != ARGON2_VERSION)
        {
            return false;
        }

        string[] kv = parts[3].Split(',');
        if (kv.Length != 3
            || !TryParseNamed(kv[0], "m=", MIN_MEMORY_KB, MAX_MEMORY_KB, out memoryKb)
            || !TryParseNamed(kv[1], "t=", MIN_ITERATIONS, MAX_ITERATIONS, out iterations)
            || !TryParseNamed(kv[2], "p=", MIN_PARALLELISM, MAX_PARALLELISM, out parallelism))
        {
            return false;
        }

        if (!TryDecode(parts[4], MIN_SALT_BYTES, MAX_SALT_BYTES, out salt)
            || !TryDecode(parts[5], MIN_HASH_BYTES, MAX_HASH_BYTES, out hash))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseInt(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryParseNamed(string field, string prefix, int min, int max, out int value)
    {
        value = 0;
        return field.StartsWith(prefix, StringComparison.Ordinal)
            && TryParseInt(field.AsSpan(prefix.Length), out value)
            && value >= min
            && value <= max;
    }

    private static bool TryDecode(string text, int minBytes, int maxBytes, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        // Unpadded standard base64 only; length % 4 == 1 can never be valid.
        if (text.Length == 0 || text.Contains('=') || text.Length % 4 == 1)
        {
            return false;
        }

        string padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        var buffer = new byte[padded.Length / 4 * 3];
        if (!Convert.TryFromBase64String(padded, buffer, out int written)
            || written < minBytes
            || written > maxBytes)
        {
            return false;
        }

        bytes = buffer.AsSpan(0, written).ToArray();
        return true;
    }
}
