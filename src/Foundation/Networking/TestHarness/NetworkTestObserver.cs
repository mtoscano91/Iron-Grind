#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// In-memory recorder implementation of <see cref="INetworkTestObserver"/>. Every callback
    /// simply appends the arguments it received, in call order, to a dedicated public list — this
    /// is a spy/recording pattern, not a behavior-driving mock. Tests read the recorded lists (or
    /// use <see cref="GetOutboundMessageCount"/>) to assert what fired, in what order, and with what
    /// arguments.
    /// </summary>
    /// <remarks>
    /// <para>Each capture list is named <c>{CallbackNameWithoutOn}Calls</c> (e.g.
    /// <see cref="OnServerDamageEventSerialized"/> records into
    /// <see cref="ServerDamageEventSerializedCalls"/>), holding one tuple entry per invocation in
    /// call order. This keeps the 25-callback surface of <see cref="INetworkTestObserver"/>
    /// mechanically simple: no hand-written record types, no shared "capture log" indirection to
    /// unwrap in test assertions.</para>
    /// <para><see cref="GetOutboundMessageCount"/> has no corresponding "On" callback in this
    /// interface that supplies both a <c>clientId</c> and a <c>messageTypeId</c> generically — the
    /// count is instead fed by the internal <see cref="RecordOutboundMessage"/> seam, which the
    /// future story owning the server serialization boundary calls once per outbound message
    /// alongside whichever specific <c>On*</c> callback also fires for that message.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// NetworkTestObserver observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
    /// observer.OnServerDamageEventSerialized(attackerEntityId: 1, targetEntityId: 2, serverComputedDamage: 42);
    /// Assert.AreEqual(1, observer.ServerDamageEventSerializedCalls.Count);
    /// Assert.AreEqual((1u, 2u, 42), observer.ServerDamageEventSerializedCalls[0]);
    /// </code>
    /// </example>
    public sealed class NetworkTestObserver : INetworkTestObserver
    {
        // ---------------------------------------------------------------------
        // Server-side capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnServerDamageEventSerialized"/> call, in call order.</summary>
        public List<(uint attackerEntityId, uint targetEntityId, int serverComputedDamage)> ServerDamageEventSerializedCalls { get; } = new();

        /// <summary>Every <see cref="OnServerSelfDamageEventSerialized"/> call, in call order.</summary>
        public List<(uint attackerEntityId, uint targetEntityId, int serverComputedDamage)> ServerSelfDamageEventSerializedCalls { get; } = new();

        /// <summary>Every <see cref="OnServerGoldSyncBatched"/> call, in call order.</summary>
        public List<(uint characterId, uint newBalance, uint version)> ServerGoldSyncBatchedCalls { get; } = new();

        /// <summary>Every <see cref="OnServerKillEventEmitted"/> call, in call order.</summary>
        public List<(uint killerEntityId, uint targetEntityId, int finalDamage)> ServerKillEventEmittedCalls { get; } = new();

        /// <summary>Every <see cref="OnServerCycleTimerBroadcastSerialized"/> call, in call order.</summary>
        public List<(uint entityId, ushort cyclePositionTicks, uint serverTickNumber)> ServerCycleTimerBroadcastSerializedCalls { get; } = new();

        /// <summary>Every <see cref="OnServerEnhancementOutcomeSerialized"/> call, in call order.</summary>
        public List<(uint characterId, uint itemId, bool success, byte newEnhancementLevel)> ServerEnhancementOutcomeSerializedCalls { get; } = new();

        /// <summary>
        /// Every <see cref="OnRUBatchEntityHealthUpdates"/> call, in call order. Each
        /// <c>deliveredEntityIds</c> entry is a defensive copy — later caller-side mutation of the
        /// original list passed to the callback cannot retroactively alter what was recorded.
        /// </summary>
        public List<(uint clientId, IReadOnlyList<uint> deliveredEntityIds)> RUBatchEntityHealthUpdatesCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Client-side capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnClientDamageEventReceived"/> call, in call order.</summary>
        public List<(uint attackerEntityId, uint targetEntityId, int finalDamage)> ClientDamageEventReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientSelfDamageEventReceived"/> call, in call order.</summary>
        public List<(uint attackerEntityId, uint targetEntityId, int finalDamage)> ClientSelfDamageEventReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientGoldSyncReceived"/> call, in call order.</summary>
        public List<(uint characterId, uint newBalance, uint version)> ClientGoldSyncReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientKillEventReceived"/> call, in call order.</summary>
        public List<(uint killerEntityId, uint targetEntityId, int finalDamage)> ClientKillEventReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientCycleTimerBroadcastReceived"/> call, in call order.</summary>
        public List<(uint entityId, ushort cyclePositionTicks)> ClientCycleTimerBroadcastReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientEnhancementOutcomeReceived"/> call, in call order.</summary>
        public List<(uint characterId, uint itemId, bool success, byte newEnhancementLevel)> ClientEnhancementOutcomeReceivedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Zone entry capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnZoneGateOpened"/> call, in call order.</summary>
        public List<uint> ZoneGateOpenedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Session lifecycle capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnSessionStateTransitioned"/> call, in call order.</summary>
        public List<(uint accountId, SessionState fromState, SessionState toState, string trigger)> SessionStateTransitionedCalls { get; } = new();

        /// <summary>Every <see cref="OnSessionInvalidatedBySteal"/> call, in call order.</summary>
        public List<(uint accountId, uint priorCharacterId)> SessionInvalidatedByStealCalls { get; } = new();

        /// <summary>Every <see cref="OnReAuthAttemptFailed"/> call, in call order.</summary>
        public List<(uint accountId, int attemptNumber, int remainingAttempts)> ReAuthAttemptFailedCalls { get; } = new();

        /// <summary>Every <see cref="OnPersistenceWriteCompleted"/> call, in call order.</summary>
        public List<(uint characterId, PersistenceWriteReason reason)> PersistenceWriteCompletedCalls { get; } = new();

        /// <summary>Every <see cref="OnGhostCombatTTLExpired"/> call, in call order.</summary>
        public List<(uint entityId, uint disconnectTickNumber, uint expiryTick)> GhostCombatTTLExpiredCalls { get; } = new();

        /// <summary>Every <see cref="OnGhostPromotionEventEmitted"/> call, in call order.</summary>
        public List<uint> GhostPromotionEventEmittedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientGhostPromotionEventReceived"/> call, in call order.</summary>
        public List<uint> ClientGhostPromotionEventReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnZoneStateTransitioned"/> call, in call order.</summary>
        public List<(uint zoneInstanceId, ZoneState fromState, ZoneState toState)> ZoneStateTransitionedCalls { get; } = new();

        /// <summary>Every <see cref="OnSessionHandshakeEmitted"/> call, in call order.</summary>
        public List<(uint characterId, bool wasKilledWhileDisconnected, int goldBalance, uint goldVersion,
            int level, int currentHp, int currentMp, int heldFreePoints, byte classType)> SessionHandshakeEmittedCalls { get; } = new();

        /// <summary>Every <see cref="OnSnapshotRetransmitAttempt"/> call, in call order.</summary>
        public List<(uint characterId, int attemptNumber, int maxAttempts)> SnapshotRetransmitAttemptCalls { get; } = new();

        /// <summary>Every <see cref="OnPartyDisbanded"/> call, in call order.</summary>
        public List<(uint partyId, uint tickNumber)> PartyDisbandedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Priority path capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnPriorityPathMessageFlushed"/> call, in call order.</summary>
        public List<(uint characterId, ushort messageTypeId, uint tickNumber)> PriorityPathMessageFlushedCalls { get; } = new();

        /// <summary>Every <see cref="OnTickCompleted"/> call, in call order.</summary>
        public List<uint> TickCompletedCalls { get; } = new();

        /// <summary>Every <see cref="OnSkillGraceWindowEvaluated"/> call, in call order.</summary>
        public List<(uint entityId, float adjustedTimer, bool graceTriggers)> SkillGraceWindowEvaluatedCalls { get; } = new();

        /// <summary>Every <see cref="OnStatSnapshotEmitted"/> call, in call order.</summary>
        public List<uint> StatSnapshotEmittedCalls { get; } = new();

        /// <summary>Every <see cref="OnSkillUsedRateLimitRejected"/> call, in call order.</summary>
        public List<uint> SkillUsedRateLimitRejectedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // OWL compensation capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnConnectionQualityUpdateEmitted"/> call, in call order.</summary>
        public List<(uint entityId, bool rhythmCompensationActive)> ConnectionQualityUpdateEmittedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Commit-before-broadcast capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnCriticalInfrastructureAlertFired"/> call, in call order.</summary>
        public List<(uint clientId, string reason)> CriticalInfrastructureAlertFiredCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Message routing capture
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnUnclassifiedMessageTypeLogged"/> call, in call order.</summary>
        public List<ushort> UnclassifiedMessageTypeLoggedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Gold sync forced delivery capture (Story 026)
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnServerGoldSyncForcedDeliveryEmitted"/> call, in call order.</summary>
        public List<(uint characterId, uint newBalance, uint version, uint tickNumber)> ServerGoldSyncForcedDeliveryEmittedCalls { get; } = new();

        /// <summary>Every <see cref="OnClientGoldSyncForcedDeliveryReceived"/> call, in call order.</summary>
        public List<(uint characterId, uint newBalance, uint version)> ClientGoldSyncForcedDeliveryReceivedCalls { get; } = new();

        /// <summary>Every <see cref="OnGoldSyncForcedDeliveryAnomalyLogged"/> call, in call order.</summary>
        public List<(uint characterId, int consecutiveTicksWithoutNormalDelivery)> GoldSyncForcedDeliveryAnomalyLoggedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Self-damage recipient defense capture (Story 027)
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnSelfDamageDirectionViolationLogged"/> call, in call order.</summary>
        public List<(uint attackerEntityId, uint localPlayerEntityId)> SelfDamageDirectionViolationLoggedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // Target slot capture (Story 029)
        // ---------------------------------------------------------------------

        /// <summary>Every <see cref="OnSelfTargetAttemptLogged"/> call, in call order.</summary>
        public List<uint> SelfTargetAttemptLoggedCalls { get; } = new();

        /// <summary>Every <see cref="OnInvalidTargetEntityIdLogged"/> call, in call order.</summary>
        public List<(uint clientId, uint invalidTargetEntityId)> InvalidTargetEntityIdLoggedCalls { get; } = new();

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — server-side capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnServerDamageEventSerialized(uint attackerEntityId, uint targetEntityId, int serverComputedDamage)
            => ServerDamageEventSerializedCalls.Add((attackerEntityId, targetEntityId, serverComputedDamage));

        /// <inheritdoc/>
        public void OnServerSelfDamageEventSerialized(uint attackerEntityId, uint targetEntityId, int serverComputedDamage)
            => ServerSelfDamageEventSerializedCalls.Add((attackerEntityId, targetEntityId, serverComputedDamage));

        /// <inheritdoc/>
        public void OnServerGoldSyncBatched(uint characterId, uint newBalance, uint version)
            => ServerGoldSyncBatchedCalls.Add((characterId, newBalance, version));

        /// <inheritdoc/>
        public void OnServerKillEventEmitted(uint killerEntityId, uint targetEntityId, int finalDamage)
            => ServerKillEventEmittedCalls.Add((killerEntityId, targetEntityId, finalDamage));

        /// <inheritdoc/>
        public void OnServerCycleTimerBroadcastSerialized(uint entityId, ushort cyclePositionTicks, uint serverTickNumber)
            => ServerCycleTimerBroadcastSerializedCalls.Add((entityId, cyclePositionTicks, serverTickNumber));

        /// <inheritdoc/>
        public void OnServerEnhancementOutcomeSerialized(uint characterId, uint itemId, bool success, byte newEnhancementLevel)
            => ServerEnhancementOutcomeSerializedCalls.Add((characterId, itemId, success, newEnhancementLevel));

        /// <inheritdoc/>
        public void OnRUBatchEntityHealthUpdates(uint clientId, IReadOnlyList<uint> deliveredEntityIds)
            => RUBatchEntityHealthUpdatesCalls.Add((clientId, new List<uint>(deliveredEntityIds)));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — client-side capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnClientDamageEventReceived(uint attackerEntityId, uint targetEntityId, int finalDamage)
            => ClientDamageEventReceivedCalls.Add((attackerEntityId, targetEntityId, finalDamage));

        /// <inheritdoc/>
        public void OnClientSelfDamageEventReceived(uint attackerEntityId, uint targetEntityId, int finalDamage)
            => ClientSelfDamageEventReceivedCalls.Add((attackerEntityId, targetEntityId, finalDamage));

        /// <inheritdoc/>
        public void OnClientGoldSyncReceived(uint characterId, uint newBalance, uint version)
            => ClientGoldSyncReceivedCalls.Add((characterId, newBalance, version));

        /// <inheritdoc/>
        public void OnClientKillEventReceived(uint killerEntityId, uint targetEntityId, int finalDamage)
            => ClientKillEventReceivedCalls.Add((killerEntityId, targetEntityId, finalDamage));

        /// <inheritdoc/>
        public void OnClientCycleTimerBroadcastReceived(uint entityId, ushort cyclePositionTicks)
            => ClientCycleTimerBroadcastReceivedCalls.Add((entityId, cyclePositionTicks));

        /// <inheritdoc/>
        public void OnClientEnhancementOutcomeReceived(uint characterId, uint itemId, bool success, byte newEnhancementLevel)
            => ClientEnhancementOutcomeReceivedCalls.Add((characterId, itemId, success, newEnhancementLevel));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — zone entry capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnZoneGateOpened(uint clientId)
            => ZoneGateOpenedCalls.Add(clientId);

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — session lifecycle capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnSessionStateTransitioned(uint accountId, SessionState fromState, SessionState toState, string trigger)
            => SessionStateTransitionedCalls.Add((accountId, fromState, toState, trigger));

        /// <inheritdoc/>
        public void OnSessionInvalidatedBySteal(uint accountId, uint priorCharacterId)
            => SessionInvalidatedByStealCalls.Add((accountId, priorCharacterId));

        /// <inheritdoc/>
        public void OnReAuthAttemptFailed(uint accountId, int attemptNumber, int remainingAttempts)
            => ReAuthAttemptFailedCalls.Add((accountId, attemptNumber, remainingAttempts));

        /// <inheritdoc/>
        public void OnPersistenceWriteCompleted(uint characterId, PersistenceWriteReason reason)
            => PersistenceWriteCompletedCalls.Add((characterId, reason));

        /// <inheritdoc/>
        public void OnGhostCombatTTLExpired(uint entityId, uint disconnectTickNumber, uint expiryTick)
            => GhostCombatTTLExpiredCalls.Add((entityId, disconnectTickNumber, expiryTick));

        /// <inheritdoc/>
        public void OnGhostPromotionEventEmitted(uint characterId)
            => GhostPromotionEventEmittedCalls.Add(characterId);

        /// <inheritdoc/>
        public void OnClientGhostPromotionEventReceived(uint characterId)
            => ClientGhostPromotionEventReceivedCalls.Add(characterId);

        /// <inheritdoc/>
        public void OnZoneStateTransitioned(uint zoneInstanceId, ZoneState fromState, ZoneState toState)
            => ZoneStateTransitionedCalls.Add((zoneInstanceId, fromState, toState));

        /// <inheritdoc/>
        public void OnSessionHandshakeEmitted(uint characterId, bool wasKilledWhileDisconnected,
                                               int goldBalance, uint goldVersion, int level, int currentHp,
                                               int currentMp, int heldFreePoints, byte classType)
            => SessionHandshakeEmittedCalls.Add((characterId, wasKilledWhileDisconnected, goldBalance, goldVersion,
                level, currentHp, currentMp, heldFreePoints, classType));

        /// <inheritdoc/>
        public void OnSnapshotRetransmitAttempt(uint characterId, int attemptNumber, int maxAttempts)
            => SnapshotRetransmitAttemptCalls.Add((characterId, attemptNumber, maxAttempts));

        /// <inheritdoc/>
        public void OnPartyDisbanded(uint partyId, uint tickNumber)
            => PartyDisbandedCalls.Add((partyId, tickNumber));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — priority path capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnPriorityPathMessageFlushed(uint characterId, ushort messageTypeId, uint tickNumber)
            => PriorityPathMessageFlushedCalls.Add((characterId, messageTypeId, tickNumber));

        /// <inheritdoc/>
        public void OnTickCompleted(uint tickNumber)
            => TickCompletedCalls.Add(tickNumber);

        /// <inheritdoc/>
        public void OnSkillGraceWindowEvaluated(uint entityId, float adjustedTimer, bool graceTriggers)
            => SkillGraceWindowEvaluatedCalls.Add((entityId, adjustedTimer, graceTriggers));

        /// <inheritdoc/>
        public void OnStatSnapshotEmitted(uint entityId)
            => StatSnapshotEmittedCalls.Add(entityId);

        /// <inheritdoc/>
        public void OnSkillUsedRateLimitRejected(uint entityId)
            => SkillUsedRateLimitRejectedCalls.Add(entityId);

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — OWL compensation capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnConnectionQualityUpdateEmitted(uint entityId, bool rhythmCompensationActive)
            => ConnectionQualityUpdateEmittedCalls.Add((entityId, rhythmCompensationActive));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — commit-before-broadcast capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnCriticalInfrastructureAlertFired(uint clientId, string reason)
            => CriticalInfrastructureAlertFiredCalls.Add((clientId, reason));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — message routing capture
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnUnclassifiedMessageTypeLogged(ushort messageTypeId)
            => UnclassifiedMessageTypeLoggedCalls.Add(messageTypeId);

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — gold sync forced delivery capture (Story 026)
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnServerGoldSyncForcedDeliveryEmitted(uint characterId, uint newBalance, uint version, uint tickNumber)
            => ServerGoldSyncForcedDeliveryEmittedCalls.Add((characterId, newBalance, version, tickNumber));

        /// <inheritdoc/>
        public void OnClientGoldSyncForcedDeliveryReceived(uint characterId, uint newBalance, uint version)
            => ClientGoldSyncForcedDeliveryReceivedCalls.Add((characterId, newBalance, version));

        /// <inheritdoc/>
        public void OnGoldSyncForcedDeliveryAnomalyLogged(uint characterId, int consecutiveTicksWithoutNormalDelivery)
            => GoldSyncForcedDeliveryAnomalyLoggedCalls.Add((characterId, consecutiveTicksWithoutNormalDelivery));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — self-damage recipient defense capture (Story 027)
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnSelfDamageDirectionViolationLogged(uint attackerEntityId, uint localPlayerEntityId)
            => SelfDamageDirectionViolationLoggedCalls.Add((attackerEntityId, localPlayerEntityId));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — target slot capture (Story 029)
        // ---------------------------------------------------------------------

        /// <inheritdoc/>
        public void OnSelfTargetAttemptLogged(uint entityId)
            => SelfTargetAttemptLoggedCalls.Add(entityId);

        /// <inheritdoc/>
        public void OnInvalidTargetEntityIdLogged(uint clientId, uint invalidTargetEntityId)
            => InvalidTargetEntityIdLoggedCalls.Add((clientId, invalidTargetEntityId));

        // ---------------------------------------------------------------------
        // INetworkTestObserver implementation — query methods
        // ---------------------------------------------------------------------

        private readonly Dictionary<(uint clientId, ushort messageTypeId), int> _outboundMessageCounts = new();

        /// <inheritdoc/>
        public int GetOutboundMessageCount(uint clientId, ushort messageTypeId)
            => _outboundMessageCounts.TryGetValue((clientId, messageTypeId), out int count) ? count : 0;

        // ---------------------------------------------------------------------
        // Internal seam: increments the GetOutboundMessageCount counter. Not part of
        // INetworkTestObserver — the interface only exposes the read side
        // (GetOutboundMessageCount). The future story owning the server serialization
        // boundary calls this once per outbound message, alongside whichever specific
        // On* callback also fires for that same message. Visible to
        // IronGrind.Foundation.EditModeTests via InternalsVisibleTo
        // (src/Foundation/AssemblyInfo.cs).
        // ---------------------------------------------------------------------

        /// <summary>
        /// Increments the cumulative outbound message count for
        /// (<paramref name="clientId"/>, <paramref name="messageTypeId"/>), read back via
        /// <see cref="GetOutboundMessageCount"/>.
        /// </summary>
        internal void RecordOutboundMessage(uint clientId, ushort messageTypeId)
        {
            _outboundMessageCounts.TryGetValue((clientId, messageTypeId), out int existing);
            _outboundMessageCounts[(clientId, messageTypeId)] = existing + 1;
        }

        /// <summary>
        /// Test-only convenience: clears every recorded call list and the outbound message count
        /// table, restoring a clean slate between test cases. Not part of
        /// <see cref="INetworkTestObserver"/> — a recorder-specific addition, analogous to
        /// <c>Reset()</c> on the other three test-harness implementations.
        /// </summary>
        public void Reset()
        {
            ServerDamageEventSerializedCalls.Clear();
            ServerSelfDamageEventSerializedCalls.Clear();
            ServerGoldSyncBatchedCalls.Clear();
            ServerKillEventEmittedCalls.Clear();
            ServerCycleTimerBroadcastSerializedCalls.Clear();
            ServerEnhancementOutcomeSerializedCalls.Clear();
            RUBatchEntityHealthUpdatesCalls.Clear();

            ClientDamageEventReceivedCalls.Clear();
            ClientSelfDamageEventReceivedCalls.Clear();
            ClientGoldSyncReceivedCalls.Clear();
            ClientKillEventReceivedCalls.Clear();
            ClientCycleTimerBroadcastReceivedCalls.Clear();
            ClientEnhancementOutcomeReceivedCalls.Clear();

            ZoneGateOpenedCalls.Clear();

            SessionStateTransitionedCalls.Clear();
            SessionInvalidatedByStealCalls.Clear();
            ReAuthAttemptFailedCalls.Clear();
            PersistenceWriteCompletedCalls.Clear();
            GhostCombatTTLExpiredCalls.Clear();
            GhostPromotionEventEmittedCalls.Clear();
            ClientGhostPromotionEventReceivedCalls.Clear();
            ZoneStateTransitionedCalls.Clear();
            SessionHandshakeEmittedCalls.Clear();
            SnapshotRetransmitAttemptCalls.Clear();
            PartyDisbandedCalls.Clear();

            PriorityPathMessageFlushedCalls.Clear();
            TickCompletedCalls.Clear();
            SkillGraceWindowEvaluatedCalls.Clear();
            StatSnapshotEmittedCalls.Clear();
            SkillUsedRateLimitRejectedCalls.Clear();

            ConnectionQualityUpdateEmittedCalls.Clear();

            CriticalInfrastructureAlertFiredCalls.Clear();

            UnclassifiedMessageTypeLoggedCalls.Clear();

            ServerGoldSyncForcedDeliveryEmittedCalls.Clear();
            ClientGoldSyncForcedDeliveryReceivedCalls.Clear();
            GoldSyncForcedDeliveryAnomalyLoggedCalls.Clear();

            SelfDamageDirectionViolationLoggedCalls.Clear();

            SelfTargetAttemptLoggedCalls.Clear();
            InvalidTargetEntityIdLoggedCalls.Clear();

            _outboundMessageCounts.Clear();
        }
    }
}
#endif
