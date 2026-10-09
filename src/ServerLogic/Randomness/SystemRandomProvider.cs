using System;

namespace IronGrind.Randomness
{
    /// <summary>
    /// Production <see cref="IRandomProvider"/> over one <see cref="System.Random"/> handed to the
    /// constructor (ADR-013 Decision 2). Not thread-safe: draw from the game-logic thread only.
    /// </summary>
    /// <remarks>
    /// <see cref="NextFloat"/> takes the top 24 bits of one integer draw and scales them by 2^-24, so there
    /// is no rounding step and the result is never 1.0f. No allocation per draw.
    /// </remarks>
    public sealed class SystemRandomProvider : IRandomProvider
    {
        private const int DISCARDED_LOW_BITS = 7;
        private const float UNIT_SCALE = 1f / 16777216f; // 2^-24

        private readonly Random _random;

        /// <summary>Wraps <paramref name="random"/>.</summary>
        /// <param name="random">The generator to draw from; the provider does not seed or replace it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
        public SystemRandomProvider(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// Maps a sample in [0, int.MaxValue) to a float in [0.0f, 1.0f) using its top 24 bits
        /// (<c>(sample >> 7) * 2^-24</c>). Exact: every result is representable as a float.
        /// </summary>
        /// <param name="sample">A non-negative integer draw.</param>
        /// <returns>A float in [0.0f, 0.99999994f].</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sample"/> is negative.</exception>
        public static float ToUnitFloat(int sample)
        {
            if (sample < 0)
                throw new ArgumentOutOfRangeException(nameof(sample), sample, "Sample must not be negative.");

            return (sample >> DISCARDED_LOW_BITS) * UNIT_SCALE;
        }

        /// <inheritdoc/>
        public float NextFloat()
        {
            return ToUnitFloat(_random.Next());
        }

        /// <inheritdoc/>
        public double NextDouble()
        {
            return _random.NextDouble();
        }

        /// <inheritdoc/>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }
    }
}
