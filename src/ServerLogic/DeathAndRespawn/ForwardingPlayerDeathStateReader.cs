using System;
using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// <see cref="IPlayerDeathStateReader"/> that forwards to a late-bound target (Death and Respawn Story 008,
    /// Readiness Decision 2). The snapshot builder and zone close steps are built inside the zone instancing wiring,
    /// before the <see cref="DeathAndRespawnService"/> can exist (it needs Movement, Auto-Attack and Status Effects);
    /// the composition passes this forwarder then and calls <see cref="Bind"/> once the service is built. Unbound,
    /// nobody is dead. Main thread only; allocation-free.
    /// </summary>
    /// <example>
    /// <code>
    /// var reader = new ForwardingPlayerDeathStateReader();   // passed to the wiring options
    /// reader.Bind(service);                                  // after the service is built
    /// </code>
    /// </example>
    public sealed class ForwardingPlayerDeathStateReader : IPlayerDeathStateReader
    {
        private IPlayerDeathStateReader _target;

        /// <summary>True once <see cref="Bind"/> was called.</summary>
        public bool IsBound => _target != null;

        /// <summary>Binds the real reader. Allowed once.</summary>
        /// <param name="target">The reader to forward to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Already bound.</exception>
        public void Bind(IPlayerDeathStateReader target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (_target != null)
            {
                throw new InvalidOperationException("ForwardingPlayerDeathStateReader is already bound.");
            }

            _target = target;
        }

        /// <inheritdoc/>
        public bool IsDead(EntityID id)
        {
            return _target != null && _target.IsDead(id);
        }

        /// <inheritdoc/>
        public bool TryGetRespawnTick(EntityID id, out long respawnTick)
        {
            if (_target == null)
            {
                respawnTick = 0L;
                return false;
            }

            return _target.TryGetRespawnTick(id, out respawnTick);
        }
    }
}
