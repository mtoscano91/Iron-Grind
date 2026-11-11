using System;
using IronGrind.Networking;

namespace IronGrind.Client.Session
{
    /// <summary>
    /// Configuration of <see cref="ClientZoneSession"/> (Authentication Story 018). A plain class so a harness or a test
    /// can build it in code; all values are data, none is a literal inside the session.
    /// </summary>
    public sealed class ClientZoneSessionConfig
    {
        /// <summary>HEARTBEAT_INTERVAL_SECONDS: seconds between heartbeats on an otherwise idle connection. Must be at least 1 and below <see cref="HeartbeatTimeoutSeconds"/> / 2.</summary>
        public int HeartbeatIntervalSeconds { get; set; } = HeartbeatActivityTracker.HEARTBEAT_INTERVAL_SECONDS;

        /// <summary>HEARTBEAT_TIMEOUT_SECONDS: the server's inactivity timeout (default 10). Used only to validate the interval.</summary>
        public int HeartbeatTimeoutSeconds { get; set; } = 10;

        /// <summary>FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS: seconds after the handshake send in which the whole snapshot must arrive (default 10).</summary>
        public int FragmentReassemblyTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// CONNECT_TIMEOUT_SECONDS: seconds after the transport start in which the connection must come up (default 10).
        /// On expiry the entry fails with <see cref="ZoneEntryFailureReason.ConnectTimeout"/>.
        /// </summary>
        public int ConnectTimeoutSeconds { get; set; } = 10;

        /// <summary>MAX_CLIENT_SNAPSHOT_FRAGMENTS: largest accepted <c>totalFragments</c> (default 32).</summary>
        public int MaxClientSnapshotFragments { get; set; } = 32;

        /// <summary>When non-empty, the zone host used instead of the host of <see cref="GatewayBaseUrl"/>.</summary>
        public string ZoneHostOverride { get; set; } = "";

        /// <summary>The gateway base URL given to <c>AuthClient</c>; its host is the zone host unless overridden.</summary>
        public string GatewayBaseUrl { get; set; } = "";

        /// <summary>Checks the configuration.</summary>
        /// <exception cref="InvalidOperationException">A value is out of range, or no zone host can be derived.</exception>
        public void Validate()
        {
            if (HeartbeatIntervalSeconds < 1 || HeartbeatIntervalSeconds * 2 >= HeartbeatTimeoutSeconds)
            {
                throw new InvalidOperationException(
                    "HeartbeatIntervalSeconds must be >= 1 and < HeartbeatTimeoutSeconds / 2 (was "
                    + HeartbeatIntervalSeconds + " with timeout " + HeartbeatTimeoutSeconds + ").");
            }

            if (HeartbeatTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("HeartbeatTimeoutSeconds must be positive.");
            }

            if (FragmentReassemblyTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("FragmentReassemblyTimeoutSeconds must be positive.");
            }

            if (ConnectTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("ConnectTimeoutSeconds must be positive.");
            }

            if (MaxClientSnapshotFragments < 1)
            {
                throw new InvalidOperationException("MaxClientSnapshotFragments must be at least 1.");
            }

            if (string.IsNullOrEmpty(ZoneHostOverride)
                && !Uri.TryCreate(GatewayBaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    "GatewayBaseUrl must be an absolute URL when ZoneHostOverride is empty (was '" + GatewayBaseUrl + "').");
            }
        }

        /// <summary>The host the zone client connects to: the override if set, else the gateway URL's host.</summary>
        /// <returns>The zone host.</returns>
        public string ResolveZoneHost()
        {
            return string.IsNullOrEmpty(ZoneHostOverride) ? new Uri(GatewayBaseUrl).Host : ZoneHostOverride;
        }
    }
}
