using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Stand-in <see cref="IMovementIntentSink"/> that logs each message until client networking exists
    /// (Movement Story 013). Logging allocates; this is dev-only, not the real send path.
    /// </summary>
    public sealed class LoggingMovementIntentSink : IMovementIntentSink
    {
        /// <summary>Logs the message with <see cref="Debug.Log(object)"/>.</summary>
        /// <param name="message">The encoded intent for one tick.</param>
        public void Send(in MovementIntentMessage message)
        {
            Debug.Log("MovementIntent tick=" + message.TickNumber + " dir=(" + message.DirX + ", " + message.DirZ
                + ") facing=" + message.FacingAngle);
        }
    }
}
