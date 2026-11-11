using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace IronGrind.Networking
{
    /// <summary>
    /// Forwards connection add / remove events to several <see cref="IClientConnectionListener"/>s in the order given
    /// (the transport accepts only one). A throwing listener does not stop the ones after it (the adapter would log
    /// and skip them otherwise); the first exception is rethrown after all ran.
    /// </summary>
    /// <example>
    /// <code>
    /// var listener = new ClientConnectionListenerFanOut(outbox, wiring.Admission, wiring.HandshakeIntake);
    /// network.CreateInboundTransport(dispatcher, dispatcher, listener);
    /// </code>
    /// </example>
    public sealed class ClientConnectionListenerFanOut : IClientConnectionListener
    {
        private readonly IClientConnectionListener[] _listeners;

        /// <summary>Creates the fan-out.</summary>
        /// <param name="listeners">The listeners, called in this order; none may be null.</param>
        /// <exception cref="ArgumentNullException">The array or an element is null.</exception>
        public ClientConnectionListenerFanOut(params IClientConnectionListener[] listeners)
        {
            if (listeners == null)
            {
                throw new ArgumentNullException(nameof(listeners));
            }

            _listeners = (IClientConnectionListener[])listeners.Clone();
            for (int i = 0; i < _listeners.Length; i++)
            {
                if (_listeners[i] == null)
                {
                    throw new ArgumentNullException(nameof(listeners), "A listener is null.");
                }
            }
        }

        /// <summary>The listeners, in call order (the composition passes the outbox first).</summary>
        /// <example><code>var outbox = (ClientMessageOutbox)fanOut.Listeners[0];</code></example>
        public IReadOnlyList<IClientConnectionListener> Listeners => _listeners;

        /// <inheritdoc/>
        public void AddClient(uint clientId)
        {
            Exception first = null;
            for (int i = 0; i < _listeners.Length; i++)
            {
                try
                {
                    _listeners[i].AddClient(clientId);
                }
                catch (Exception ex)
                {
                    first = first ?? ex;
                }
            }

            if (first != null)
            {
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }

        /// <inheritdoc/>
        public void RemoveClient(uint clientId)
        {
            Exception first = null;
            for (int i = 0; i < _listeners.Length; i++)
            {
                try
                {
                    _listeners[i].RemoveClient(clientId);
                }
                catch (Exception ex)
                {
                    first = first ?? ex;
                }
            }

            if (first != null)
            {
                ExceptionDispatchInfo.Capture(first).Throw();
            }
        }
    }
}
