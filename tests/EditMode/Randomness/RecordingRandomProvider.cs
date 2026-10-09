using System;
using IronGrind.Randomness;

namespace IronGrind.Tests.EditMode.Randomness
{
    /// <summary>
    /// <see cref="IRandomProvider"/> test double that wraps another provider, forwards every call, and
    /// records the last value returned by each method plus draw counts (ADR-013 Decision 6). Used when a
    /// test needs real pseudo-random values and must see each one.
    /// </summary>
    internal sealed class RecordingRandomProvider : IRandomProvider
    {
        private readonly IRandomProvider _inner;

        /// <summary>Wraps <paramref name="inner"/>.</summary>
        /// <param name="inner">The provider every call is forwarded to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
        public RecordingRandomProvider(IRandomProvider inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Last value returned by <see cref="NextFloat"/>; 0 before the first draw.</summary>
        public float LastFloat { get; private set; }

        /// <summary>Last value returned by <see cref="NextDouble"/>; 0 before the first draw.</summary>
        public double LastDouble { get; private set; }

        /// <summary>Last value returned by <see cref="NextInt"/>; 0 before the first draw.</summary>
        public int LastInt { get; private set; }

        /// <summary>Total number of draws across the three methods.</summary>
        public int DrawCount => FloatDrawCount + DoubleDrawCount + IntDrawCount;

        /// <summary>Number of <see cref="NextFloat"/> draws.</summary>
        public int FloatDrawCount { get; private set; }

        /// <summary>Number of <see cref="NextDouble"/> draws.</summary>
        public int DoubleDrawCount { get; private set; }

        /// <summary>Number of <see cref="NextInt"/> draws.</summary>
        public int IntDrawCount { get; private set; }

        /// <inheritdoc/>
        public float NextFloat()
        {
            LastFloat = _inner.NextFloat();
            FloatDrawCount++;
            return LastFloat;
        }

        /// <inheritdoc/>
        public double NextDouble()
        {
            LastDouble = _inner.NextDouble();
            DoubleDrawCount++;
            return LastDouble;
        }

        /// <inheritdoc/>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            LastInt = _inner.NextInt(minInclusive, maxExclusive);
            IntDrawCount++;
            return LastInt;
        }
    }
}
