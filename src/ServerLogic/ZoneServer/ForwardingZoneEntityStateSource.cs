using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneEntityStateSource"/> that forwards to a late-bound target (Zone Instancing Story 015). The
    /// snapshot builder takes its state source at construction, before Auto-Attack Combat exists; the composition passes
    /// this forwarder then and calls <see cref="Bind"/> afterwards. Returns false until bound. Main thread only.
    /// </summary>
    public sealed class ForwardingZoneEntityStateSource : IZoneEntityStateSource
    {
        private IZoneEntityStateSource _target;

        /// <summary>True once <see cref="Bind"/> was called.</summary>
        public bool IsBound => _target != null;

        /// <summary>Binds the real source. Allowed once.</summary>
        /// <param name="target">The source to forward to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Already bound.</exception>
        public void Bind(IZoneEntityStateSource target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (_target != null)
            {
                throw new InvalidOperationException("ForwardingZoneEntityStateSource is already bound.");
            }

            _target = target;
        }

        /// <inheritdoc/>
        public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
        {
            if (_target == null)
            {
                view = default;
                return false;
            }

            return _target.TryGetPlayerState(id, out view);
        }

        /// <inheritdoc/>
        public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
        {
            if (_target == null)
            {
                view = default;
                return false;
            }

            return _target.TryGetMobState(id, out view);
        }
    }
}
