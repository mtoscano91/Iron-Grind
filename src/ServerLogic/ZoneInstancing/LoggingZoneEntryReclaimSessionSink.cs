using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Composition default for <see cref="IZoneEntryReclaimSessionSink"/> until the Networking Core follow-up exists
    /// (TD-077): logs one warning per instance and changes no session state.
    /// </summary>
    public sealed class LoggingZoneEntryReclaimSessionSink : IZoneEntryReclaimSessionSink
    {
        /// <summary>The warning logged on the first call.</summary>
        public const string NOT_IMPLEMENTED_MESSAGE =
            "[LoggingZoneEntryReclaimSessionSink] ExpireEnteringSession is a logging stand-in (TD-077); the session is not expired and no token is deleted.";

        private bool _warned;

        /// <inheritdoc/>
        public void ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning(NOT_IMPLEMENTED_MESSAGE);
            }
        }
    }
}
