using System;
using System.Security.Cryptography;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Creates the process-level loot PRNG (design/gdd/loot-table-system.md, CR-LT-1).
    /// </summary>
    /// <remarks>
    /// CR-LT-1 wants an entropy-seeded PRNG whose seed is written to the server log for
    /// auditability. A default-seeded <see cref="Random"/> has no readable seed, so this factory
    /// draws the seed itself, logs it, and seeds the instance explicitly. Create one instance per
    /// process (no per-zone or per-mob re-seeding) and inject it into callers; tests inject their
    /// own seeded <see cref="Random"/> instead of calling this.
    ///
    /// <para><see cref="Random"/> is not thread-safe: use the returned instance only from the
    /// game-logic thread (the server tick loop).</para>
    ///
    /// <para>Replaying a logged seed reproduces the sequence on the same runtime and build only;
    /// .NET does not guarantee the <see cref="Random"/> algorithm across runtime versions.</para>
    /// </remarks>
    public static class LootRandomFactory
    {
        private const int SEED_BYTE_COUNT = 4;

        /// <summary>
        /// Draws a seed from system entropy, logs it as <c>[LootTable] PRNG seed: {seed}</c>,
        /// and returns a <see cref="Random"/> seeded with it.
        /// </summary>
        /// <param name="seed">The seed that was used.</param>
        /// <returns>A new seeded PRNG.</returns>
        public static Random CreateSeededFromEntropy(out int seed)
        {
            var bytes = new byte[SEED_BYTE_COUNT];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }
            seed = BitConverter.ToInt32(bytes, 0);
            UnityEngine.Debug.Log("[LootTable] PRNG seed: " + seed);
            return new Random(seed);
        }
    }
}
