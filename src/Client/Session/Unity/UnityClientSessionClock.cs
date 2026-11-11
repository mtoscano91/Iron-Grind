using UnityEngine;

namespace IronGrind.Client.Session
{
    /// <summary>
    /// <see cref="IClientSessionClock"/> over <see cref="Time.realtimeSinceStartupAsDouble"/>: monotonic, unaffected
    /// by <see cref="Time.timeScale"/>.
    /// </summary>
    public sealed class UnityClientSessionClock : IClientSessionClock
    {
        /// <inheritdoc/>
        public double Now => Time.realtimeSinceStartupAsDouble;
    }
}
