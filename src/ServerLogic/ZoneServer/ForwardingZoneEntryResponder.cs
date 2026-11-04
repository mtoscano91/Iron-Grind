using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneEntryResponder"/> that forwards to a late-bound <see cref="Target"/> (Skill System Story 011).
    /// <c>ZoneEntryCompletion</c> takes its responder once at construction, before the Skill System exists; the
    /// composition passes this forwarder then and sets <see cref="Target"/> afterwards (the <c>_disconnector.Target</c>
    /// precedent). Tick thread only.
    /// </summary>
    public sealed class ForwardingZoneEntryResponder : IZoneEntryResponder
    {
        private IZoneEntryResponder _target;

        /// <summary>Creates the forwarder with a <see cref="LoggingZoneEntryResponder"/> as the initial target.</summary>
        public ForwardingZoneEntryResponder()
            : this(new LoggingZoneEntryResponder())
        {
        }

        /// <summary>Creates the forwarder with a given initial target (tests inject a recording default).</summary>
        /// <param name="initialTarget">The initial target.</param>
        /// <exception cref="ArgumentNullException"><paramref name="initialTarget"/> is null.</exception>
        internal ForwardingZoneEntryResponder(IZoneEntryResponder initialTarget)
        {
            _target = initialTarget ?? throw new ArgumentNullException(nameof(initialTarget));
        }

        /// <summary>The responder calls go to. Defaults to a <see cref="LoggingZoneEntryResponder"/>.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IZoneEntryResponder Target
        {
            get => _target;
            set => _target = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <inheritdoc/>
        public void SendSessionReady(uint clientId)
        {
            _target.SendSessionReady(clientId);
        }

        /// <inheritdoc/>
        public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId)
        {
            _target.SendSkillCooldownSnapshot(clientId, entityId);
        }
    }

    /// <summary>
    /// <see cref="IZonePlayerEntryListener"/> that calls several listeners in order (Skill System Story 011: Movement,
    /// then Skill). An exception in one listener is logged and the rest still run.
    /// </summary>
    public sealed class CompositeZonePlayerEntryListener : IZonePlayerEntryListener
    {
        private readonly IZonePlayerEntryListener[] _listeners;

        /// <summary>Creates the composite.</summary>
        /// <param name="listeners">The listeners, in call order; none may be null.</param>
        /// <exception cref="ArgumentNullException">The array or an element is null.</exception>
        public CompositeZonePlayerEntryListener(params IZonePlayerEntryListener[] listeners)
        {
            if (listeners == null)
            {
                throw new ArgumentNullException(nameof(listeners));
            }

            _listeners = new IZonePlayerEntryListener[listeners.Length];
            for (int i = 0; i < listeners.Length; i++)
            {
                _listeners[i] = listeners[i] ?? throw new ArgumentNullException(nameof(listeners), "element " + i + " is null");
            }
        }

        /// <inheritdoc/>
        public void OnPlayerEntering(byte slot, EntityID id)
        {
            for (int i = 0; i < _listeners.Length; i++)
            {
                try
                {
                    _listeners[i].OnPlayerEntering(slot, id);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError("[CompositeZonePlayerEntryListener] listener " + i + " threw; continuing.\n" + ex);
                }
            }
        }
    }
}
