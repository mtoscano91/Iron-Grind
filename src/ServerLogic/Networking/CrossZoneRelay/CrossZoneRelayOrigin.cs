using System;
using System.Globalization;
using System.Security.Cryptography;

namespace IronGrind.Networking
{
    /// <summary>
    /// The process's relay origin id (ADR-015 Decision 5): a random 64-bit value drawn once at construction from
    /// <see cref="RandomNumberGenerator"/>, never from the game's seeded random provider (ADR-013), so it consumes
    /// no game draw. It identifies the sender across restarts and zone ids; a receiver drops an envelope that
    /// carries its own id. The composition root creates one per process.
    /// </summary>
    /// <remarks>Immutable after construction; safe to read from any thread. The constructor allocates once.</remarks>
    /// <example>
    /// <code>
    /// var origin = new CrossZoneRelayOrigin();
    /// string hex = origin.Hex;   // 16 lowercase hex digits
    /// </code>
    /// </example>
    public sealed class CrossZoneRelayOrigin
    {
        /// <summary>Number of hex digits in the text form.</summary>
        public const int HEX_DIGITS = 16;

        /// <summary>The 64-bit id.</summary>
        public ulong Value { get; }

        /// <summary>The id as 16 lowercase hex digits.</summary>
        public string Hex { get; }

        /// <summary>Draws 8 random bytes from the operating system's cryptographic generator.</summary>
        public CrossZoneRelayOrigin()
        {
            var bytes = new byte[sizeof(ulong)];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            Value = BitConverter.ToUInt64(bytes, 0);
            Hex = Value.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>Uses a given id. For tests that need a known value; production uses the parameterless constructor.</summary>
        /// <param name="value">The id.</param>
        public CrossZoneRelayOrigin(ulong value)
        {
            Value = value;
            Hex = value.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
