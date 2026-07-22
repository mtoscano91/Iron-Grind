using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// EC-CCR-2's client-side wrong-recipient defense for <see cref="SelfDamageEvent"/> (Story 027):
    /// a sanity check layered on top of the server-side singleton-recipient guarantee
    /// (<see cref="SelfDamageEventDispatcher"/>), not a substitute for it. If a
    /// <see cref="SelfDamageEvent"/> is ever mistakenly delivered to a non-attacker client, this
    /// class stops it from displaying a phantom damage number.
    /// </summary>
    /// <remarks>
    /// On a mismatch this logs a <c>SelfDamageDirectionViolation</c> anomaly and fires
    /// <see cref="INetworkTestObserver.OnSelfDamageDirectionViolationLogged"/> — a new hook added by
    /// this story (no existing <c>INetworkTestObserver</c> callback named or shaped for this
    /// anomaly). Every other observer call this story needs already existed
    /// (<c>OnServerSelfDamageEventSerialized</c>, <c>OnClientSelfDamageEventReceived</c>,
    /// <c>OnServerCycleTimerBroadcastSerialized</c>, <c>OnClientCycleTimerBroadcastReceived</c>) — see
    /// <see cref="SelfDamageEventCodec"/> and <see cref="CycleTimerInterpolator"/> remarks.
    /// </remarks>
    /// <example>
    /// <code>
    /// bool shouldDisplay = SelfDamageRecipientGuard.ValidateRecipient(
    ///     attackerEntityId: decoded.AttackerEntityId.RawValue, localPlayerEntityId: localPlayerId, observer);
    /// if (!shouldDisplay)
    /// {
    ///     return; // suppressed — anomaly already logged
    /// }
    /// </code>
    /// </example>
    public static class SelfDamageRecipientGuard
    {
        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="attackerEntityId"/> equals
        /// <paramref name="localPlayerEntityId"/> — the display may proceed. On a mismatch, logs a
        /// <c>SelfDamageDirectionViolation</c> warning, fires
        /// <paramref name="observer"/>'s <c>OnSelfDamageDirectionViolationLogged</c> callback, and
        /// returns <see langword="false"/> (suppress display).
        /// </summary>
        /// <param name="attackerEntityId">The <c>attackerEntityId</c> field of the received <see cref="SelfDamageEvent"/>.</param>
        /// <param name="localPlayerEntityId">This client's own <c>EntityID</c>.</param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <example>
        /// <code>SelfDamageRecipientGuard.ValidateRecipient(attackerEntityId: 7u, localPlayerEntityId: 7u, observer); // true</code>
        /// </example>
        public static bool ValidateRecipient(
            uint attackerEntityId,
            uint localPlayerEntityId
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (attackerEntityId == localPlayerEntityId)
            {
                return true;
            }

            Debug.LogWarning($"[SelfDamageRecipientGuard] SelfDamageDirectionViolation: attackerEntityId={attackerEntityId}, " +
                $"localPlayerEntityId={localPlayerEntityId} — a SelfDamageEvent was received whose attackerEntityId does not " +
                "match this client's own EntityID; suppressing display (EC-CCR-2).");

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSelfDamageDirectionViolationLogged(attackerEntityId, localPlayerEntityId);
#endif

            return false;
        }
    }
}
