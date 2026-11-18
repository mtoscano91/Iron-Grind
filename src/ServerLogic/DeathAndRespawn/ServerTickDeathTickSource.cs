using System;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Production <see cref="IDeathTickSource"/>: widens <see cref="IServerTickSource.ServerTickNumber"/> to <c>long</c>.
    /// The <c>uint</c> counter's own wrap is the tick loop's concern (ADR-014).
    /// </summary>
    public sealed class ServerTickDeathTickSource : IDeathTickSource
    {
        private readonly IServerTickSource _ticks;

        /// <summary>Wraps a tick source.</summary>
        /// <param name="ticks">The server tick source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="ticks"/> is null.</exception>
        public ServerTickDeathTickSource(IServerTickSource ticks)
        {
            _ticks = ticks ?? throw new ArgumentNullException(nameof(ticks));
        }

        /// <inheritdoc />
        public long CurrentTick => (long)_ticks.ServerTickNumber;
    }
}
