using System;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneInboundTransport"/> over <see cref="NgoInboundTransportShell"/>: forwards
    /// <see cref="Start"/> and <see cref="Stop"/> (startup S8, teardown step 6). Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// IZoneInboundTransport transport = new NgoZoneInboundTransport(new NgoInboundTransportShell(manager, intake, dispatcher, outbox));
    /// transport.Start(); // after NetworkManager.StartServer()
    /// transport.Stop();  // before NetworkManager.Shutdown()
    /// </code>
    /// </example>
    public sealed class NgoZoneInboundTransport : IZoneInboundTransport
    {
        private readonly NgoInboundTransportShell _shell;

        /// <summary>Wraps the shell.</summary>
        /// <param name="shell">The shell to forward to; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="shell"/> is null.</exception>
        public NgoZoneInboundTransport(NgoInboundTransportShell shell)
        {
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        }

        /// <summary>The wrapped shell (its adapter is the disconnect seam for the outbox).</summary>
        public NgoInboundTransportShell Shell => _shell;

        /// <inheritdoc/>
        public void Start()
        {
            _shell.Start();
        }

        /// <inheritdoc/>
        public void Stop()
        {
            _shell.Stop();
        }
    }
}
