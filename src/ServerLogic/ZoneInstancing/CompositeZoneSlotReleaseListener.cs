using System;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Fans one <see cref="ZoneSlotPool"/> release out to several <see cref="IZoneSlotReleaseListener"/>s in a fixed
    /// order (Movement Story 008; the pool accepts a single listener). Composition wiring is Movement Story 009.
    /// </summary>
    /// <remarks>An exception from one listener is caught and logged as one error; the next listener still runs.</remarks>
    public sealed class CompositeZoneSlotReleaseListener : IZoneSlotReleaseListener
    {
        private const string LOG_PREFIX = "[CompositeZoneSlotReleaseListener]";

        private readonly IZoneSlotReleaseListener[] _listeners;

        /// <summary>Creates the composite. The array is copied.</summary>
        /// <param name="listeners">Listeners in call order.</param>
        /// <exception cref="ArgumentNullException">The array or an element is null.</exception>
        public CompositeZoneSlotReleaseListener(params IZoneSlotReleaseListener[] listeners)
        {
            if (listeners == null)
                throw new ArgumentNullException(nameof(listeners));

            _listeners = new IZoneSlotReleaseListener[listeners.Length];
            for (int i = 0; i < listeners.Length; i++)
            {
                _listeners[i] = listeners[i] ?? throw new ArgumentNullException(nameof(listeners), $"element {i} is null");
            }
        }

        /// <inheritdoc/>
        public void BeforeRelease(EntityID id)
        {
            for (int i = 0; i < _listeners.Length; i++)
            {
                try
                {
                    _listeners[i].BeforeRelease(id);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} listener {i} threw for entity {id.RawValue}; continuing.\n{ex}");
                }
            }
        }
    }
}
