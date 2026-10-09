namespace IronGrind.Randomness
{
    /// <summary>
    /// Source of gameplay random numbers on the server (ADR-013 Decision 1). Every server class that rolls
    /// takes one through its constructor. Drawn from the game-logic (tick) thread only; implementations
    /// are not required to be thread-safe.
    /// </summary>
    public interface IRandomProvider
    {
        /// <summary>Uniform in [0.0f, 1.0f); 0.0f is possible, 1.0f is never returned.</summary>
        /// <returns>The next float draw.</returns>
        float NextFloat();

        /// <summary>Uniform in [0.0, 1.0).</summary>
        /// <returns>The next double draw.</returns>
        double NextDouble();

        /// <summary>
        /// Uniform in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>). Returns
        /// <paramref name="minInclusive"/> when the two are equal.
        /// </summary>
        /// <param name="minInclusive">Smallest value that can be returned.</param>
        /// <param name="maxExclusive">One above the largest value that can be returned.</param>
        /// <returns>The next integer draw.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="minInclusive"/> is greater than <paramref name="maxExclusive"/>.
        /// </exception>
        int NextInt(int minInclusive, int maxExclusive);
    }
}
