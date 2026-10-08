using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The write-failure steps of CR-NET-5.5 / CR-CP-5, shared by
    /// <see cref="CommitBeforeBroadcastSequencer"/> and <see cref="IrreversibleOutcomeCoordinator"/>:
    /// revert, disconnect, critical alert, preserve the session. No retry, and no catch between the
    /// steps: a throwing step skips the rest.
    /// </summary>
    /// <example>
    /// <code>
    /// IrreversibleWriteFailureProtocol.Run(clientId, "write failed", "alert reason",
    ///     RollBack, transport.Disconnect, sessions.Preserve);
    /// </code>
    /// </example>
    public static class IrreversibleWriteFailureProtocol
    {
        /// <summary>
        /// Runs the failure steps in CR-CP-5's numbered order: <paramref name="revertOnFailure"/>,
        /// <paramref name="disconnectClient"/> with <see cref="DisconnectReason.Other"/>, a
        /// <see cref="Debug.LogError(object)"/> with <paramref name="logMessage"/> plus the test observer's
        /// alert hook, then <paramref name="preserveSessionForTtl"/> with
        /// <see cref="CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS"/>.
        /// </summary>
        /// <param name="clientId">The connection identity passed to the disconnect and preserve steps.</param>
        /// <param name="logMessage">The complete critical log line; the caller owns its wording.</param>
        /// <param name="alertReason">The reason text passed to the observer's alert hook (test and development builds only).</param>
        /// <param name="revertOnFailure">Undoes the caller's in-memory outcome.</param>
        /// <param name="disconnectClient">Disconnects the client.</param>
        /// <param name="preserveSessionForTtl">Preserves the rolled-back session for the given seconds.</param>
        /// <param name="observer">Optional test observer; the parameter exists only in test and development builds.</param>
        public static void Run(
            uint clientId,
            string logMessage,
            string alertReason,
            Action revertOnFailure,
            Action<uint, DisconnectReason> disconnectClient,
            Action<uint, int> preserveSessionForTtl
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            revertOnFailure();
            disconnectClient(clientId, DisconnectReason.Other);

            Debug.LogError(logMessage);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnCriticalInfrastructureAlertFired(clientId, alertReason);
#endif

            preserveSessionForTtl(clientId, CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS);
        }
    }
}
