#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Test/dev-build-only structured value capture at the server serialization boundary and the
    /// client transport boundary. Enables deterministic assertion of server-computed values against
    /// client-received values without log scraping or rendering-layer inspection.
    /// </summary>
    /// <remarks>
    /// <para><b>Observation, not behavior:</b> this interface is a pure spy/recorder contract. Its
    /// methods never influence production behavior — they exist only so a test-only implementation
    /// can capture "what happened, in what order, with what arguments" for later assertion. Callers
    /// (production code, once each owning story wires its own emit call) invoke these methods
    /// exactly at the point named by each method's doc comment; they must never branch on the
    /// return value of any of these calls (there is none — every method but
    /// <see cref="GetOutboundMessageCount"/> returns <see langword="void"/>).</para>
    /// <para><b>Incremental wiring:</b> this story (Story 002) defines the interface shape and a
    /// concrete recorder implementation (<see cref="NetworkTestObserver"/>). It does not wire every
    /// callback into its real production trigger point — each owning story (session lifecycle,
    /// priority-path queue, OWL compensation, etc.) adds its own emit call as it implements that
    /// feature later in the Networking Core epic.</para>
    /// <para><b>Release-build stripping:</b> see <see cref="ITransportFaultInjector"/> remarks — the
    /// same guard and enforcement rules apply to this interface. <c>SessionState</c> and
    /// <c>ZoneState</c> (used by two of the callbacks below) are production enums declared
    /// unconditionally elsewhere in this namespace — see <c>SessionState.cs</c> / <c>ZoneState.cs</c>
    /// — because production session/zone lifecycle code also depends on them. Only
    /// <see cref="PersistenceWriteReason"/> below is test-only, since nothing outside this interface
    /// references it.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// INetworkTestObserver observer = NetworkingTestHarness.CreateNetworkTestObserver();
    /// // ... exercise a code path that serializes a DamageEvent, passing `observer` in ...
    /// observer.OnServerDamageEventSerialized(attackerEntityId: 1, targetEntityId: 2, serverComputedDamage: 42);
    /// // A concrete NetworkTestObserver records the call for later assertion:
    /// var recorder = (NetworkTestObserver)observer;
    /// Assert.AreEqual(1, recorder.ServerDamageEventSerializedCalls.Count);
    /// </code>
    /// </example>
    public interface INetworkTestObserver
    {
        // ---------------------------------------------------------------------
        // Server-side capture
        // ---------------------------------------------------------------------

        /// <summary>
        /// Called when the server serializes a <c>DamageEvent</c>, before it is placed in the R-U
        /// batch. Used by AC-NC-03.
        /// </summary>
        void OnServerDamageEventSerialized(uint attackerEntityId, uint targetEntityId, int serverComputedDamage);

        /// <summary>
        /// Called when the server serializes a <c>SelfDamageEvent</c> for the attacker's R-OD path
        /// (MCR-2) — a distinct message from <c>DamageEvent</c>, fired once per R-OD serialization.
        /// Used by wire-protocol AC-CCR-04 to verify the correct server-side routing decision.
        /// </summary>
        void OnServerSelfDamageEventSerialized(uint attackerEntityId, uint targetEntityId, int serverComputedDamage);

        /// <summary>
        /// Called when the server places a <c>GoldSyncEvent</c> sub-message into the R-U batch.
        /// Used by AC-NC-19.
        /// </summary>
        void OnServerGoldSyncBatched(uint characterId, uint newBalance, uint version);

        /// <summary>Called when the server emits a <c>KillEvent</c> on the priority path. Used by AC-NC-30.</summary>
        void OnServerKillEventEmitted(uint killerEntityId, uint targetEntityId, int finalDamage);

        /// <summary>
        /// Called when the server serializes a <c>CycleTimerBroadcast</c> for an entity (U-U path,
        /// <c>0x0103</c>). <paramref name="cyclePositionTicks"/> is the entity's current auto-attack
        /// cycle position in server ticks (0–<c>BEAT_TICKS</c>). Fires every tick per active entity.
        /// Verifies the Pillar 2 invariant that <c>CycleBroadcast</c> is never omitted under R-U or
        /// R-OD congestion (PA-P7-05 separate-packet fix).
        /// </summary>
        void OnServerCycleTimerBroadcastSerialized(uint entityId, ushort cyclePositionTicks, uint serverTickNumber);

        /// <summary>
        /// Called when the server serializes an <c>EnhancementOutcomeBroadcast</c> for zone-wide R-OD
        /// delivery. Provisional signature — full schema pending the Enhancement System GDD.
        /// </summary>
        void OnServerEnhancementOutcomeSerialized(uint characterId, uint itemId, bool success, byte newEnhancementLevel);

        /// <summary>
        /// Called after the R-U batch for a client is fully serialized each tick.
        /// <paramref name="deliveredEntityIds"/> lists every <c>EntityID</c> included in
        /// <c>EntityHealthUpdate</c> sub-messages for this client's batch (self-slot and target-slot,
        /// per the RFR-1 separation invariant). Used by AC-RFR-01, AC-RFR-03, AC-RFR-07
        /// (<c>networking-relevance-filter.md</c>).
        /// </summary>
        void OnRUBatchEntityHealthUpdates(uint clientId, IReadOnlyList<uint> deliveredEntityIds);

        // ---------------------------------------------------------------------
        // Client-side capture
        // ---------------------------------------------------------------------

        /// <summary>
        /// Called on the client when a <c>DamageEvent</c> is received at the transport boundary,
        /// before any rendering or game-logic processing. Used by AC-NC-03.
        /// </summary>
        void OnClientDamageEventReceived(uint attackerEntityId, uint targetEntityId, int finalDamage);

        /// <summary>
        /// Called on the attacker's client when a <c>SelfDamageEvent</c> is received at the transport
        /// boundary (R-OD). Fires only on the attacker's client — never on other zone clients. Used
        /// by wire-protocol AC-CCR-04.
        /// </summary>
        void OnClientSelfDamageEventReceived(uint attackerEntityId, uint targetEntityId, int finalDamage);

        /// <summary>
        /// Called on the client when a <c>GoldSyncEvent</c> sub-message is extracted from an R-U
        /// batch, before stale-discard comparison AND before any balance update is applied. Used by
        /// AC-NC-07, AC-NC-19.
        /// </summary>
        void OnClientGoldSyncReceived(uint characterId, uint newBalance, uint version);

        /// <summary>Called on the client when a <c>KillEvent</c> is received at the transport boundary. Used by AC-NC-30.</summary>
        void OnClientKillEventReceived(uint killerEntityId, uint targetEntityId, int finalDamage);

        /// <summary>Called on the client when a <c>CycleTimerBroadcast</c> is received (U-U path, <c>0x0103</c>).</summary>
        void OnClientCycleTimerBroadcastReceived(uint entityId, ushort cyclePositionTicks);

        /// <summary>
        /// Called on the client when an <c>EnhancementOutcomeBroadcast</c> is received at the
        /// transport boundary. Fires on ALL zone clients (both the enhancing player and all zone
        /// observers) — the Pillar 3 social signal. Provisional signature — full schema pending the
        /// Enhancement System GDD.
        /// </summary>
        void OnClientEnhancementOutcomeReceived(uint characterId, uint itemId, bool success, byte newEnhancementLevel);

        // ---------------------------------------------------------------------
        // Zone entry capture
        // ---------------------------------------------------------------------

        /// <summary>
        /// Called on the client when both dual-gate conditions are met simultaneously: (1)
        /// <c>SessionReady</c> has been received AND (2) <c>ZoneStateSnapshot</c> reassembly is
        /// complete. Fires exactly once per zone entry per client. Used by AC-ZI-6 to establish the
        /// gate-open instant — any outbound game RPCs (<c>MovementIntentMessage</c>,
        /// <c>SkillCastRequest</c>, <c>NotifySkillUsed</c>) captured before this callback fires
        /// violate the dual-gate invariant (<c>zone-instancing.md</c> CR-ZI-9).
        /// </summary>
        void OnZoneGateOpened(uint clientId);

        // ---------------------------------------------------------------------
        // Session lifecycle capture
        // ---------------------------------------------------------------------

        /// <summary>
        /// Called whenever a session transitions between <see cref="SessionState"/> values
        /// (ST-NET-1). <paramref name="trigger"/> is a short label matching the ST-NET-1 trigger
        /// column (e.g. <c>"HeartbeatTimeout"</c>, <c>"AuthSuccess"</c>, <c>"SessionSteal"</c>,
        /// <c>"ReauthLimitExceeded"</c>, <c>"ConnectingTimeout"</c>). Used by AC-NC-10, AC-NC-12,
        /// AC-NC-37, AC-NC-38, AC-NC-39.
        /// </summary>
        void OnSessionStateTransitioned(uint accountId, SessionState fromState, SessionState toState, string trigger);

        /// <summary>
        /// Called when a prior session is invalidated because a new authenticated connection for the
        /// same account arrived (session-stealing, B-NP-7). Fires before the new connection proceeds
        /// to <see cref="SessionState.Connecting"/>, confirming the ordering guarantee. Used by
        /// AC-NC-37.
        /// </summary>
        void OnSessionInvalidatedBySteal(uint accountId, uint priorCharacterId);

        /// <summary>
        /// Called on each failed re-authentication attempt while in
        /// <see cref="SessionState.Reconnecting"/> (EC-NET-7). <paramref name="attemptNumber"/> is
        /// 1-based; <paramref name="remainingAttempts"/> decrements toward 0 before session expiry.
        /// Used by AC-NC-38.
        /// </summary>
        void OnReAuthAttemptFailed(uint accountId, int attemptNumber, int remainingAttempts);

        /// <summary>
        /// Called when the server completes a character state persistence write (CR-NET-6.5 step 3).
        /// Fires before any session resource release, confirming commit-before-release ordering.
        /// Used by AC-NC-12, AC-NC-37, AC-NC-41, AC-NC-42.
        /// </summary>
        void OnPersistenceWriteCompleted(uint characterId, PersistenceWriteReason reason);

        /// <summary>
        /// Called when the ghost combat TTL expires for an entity (CR-GH-10,
        /// <c>networking-ghost-session.md</c>). Fires on the tick that <paramref name="expiryTick"/>
        /// is reached, before the mob retarget fires.
        /// </summary>
        void OnGhostCombatTTLExpired(uint entityId, uint disconnectTickNumber, uint expiryTick);

        /// <summary>
        /// Called when the server emits a <c>GhostPromotionEvent</c> to all zone clients (CR-GH-2).
        /// Fires once per disconnect that triggers ghost promotion, before ghost-period combat
        /// begins. Used by AC-GH-1.
        /// </summary>
        void OnGhostPromotionEventEmitted(uint characterId);

        /// <summary>
        /// Called on a zone client when a <c>GhostPromotionEvent</c> is received at the transport
        /// boundary. Used by AC-GH-1.
        /// </summary>
        void OnClientGhostPromotionEventReceived(uint characterId);

        /// <summary>Called when the zone instance's state machine transitions (ST-NET-2). Used by AC-NC-41.</summary>
        void OnZoneStateTransitioned(uint zoneInstanceId, ZoneState fromState, ZoneState toState);

        /// <summary>
        /// Called when the server emits a <c>SessionHandshake</c> to a connecting or reconnecting
        /// client. Enables AC assertions about delivered character state without wall-clock waits.
        /// <paramref name="goldVersion"/> is the <c>GoldSyncEvent.Version</c> seed delivered in the
        /// handshake — required to verify that the client initializes <c>cachedVersion</c> correctly
        /// before the first <c>GoldSyncEvent</c> (AC-NC-07 scenario B). Provisional signature pending
        /// the full <c>SessionHandshake</c> schema (OQ-NC-SER-2 / Character Persistence GDD). Used by
        /// AC-NC-11.
        /// </summary>
        void OnSessionHandshakeEmitted(uint characterId, bool wasKilledWhileDisconnected,
                                       int goldBalance, uint goldVersion, int level, int currentHp,
                                       int currentMp, int heldFreePoints, byte classType);

        /// <summary>
        /// Called each time the client emits a <c>ZoneSnapshotRequest</c> retransmit (B-NP-8).
        /// <paramref name="attemptNumber"/> is 1-based; <paramref name="maxAttempts"/> mirrors
        /// <c>MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS</c>. Used by AC-NC-40.
        /// </summary>
        void OnSnapshotRetransmitAttempt(uint characterId, int attemptNumber, int maxAttempts);

        // ---------------------------------------------------------------------
        // Priority path capture
        // ---------------------------------------------------------------------

        /// <summary>
        /// Called once per R-OD message flushed to the transport layer in a given server tick.
        /// <paramref name="messageTypeId"/> is the CR-NET-7.1 <c>MessageTypeID</c> of the transmitted
        /// message; <paramref name="tickNumber"/> is the <c>ServerTickNumber</c> at which the flush
        /// occurred. Used by AC-NC-43 to assert the 8-message-per-tick cap and verify that the
        /// enhancement-outcome exemption correctly places the exempted message at position 0 in
        /// tick T.
        /// </summary>
        void OnPriorityPathMessageFlushed(uint characterId, ushort messageTypeId, uint tickNumber);

        /// <summary>
        /// Called at the end of each server tick, after all tick-driven processing completes.
        /// <paramref name="tickNumber"/> is the <c>ServerTickNumber</c> that just completed. Used by
        /// AC-NC-04 (200 ticks in 10s), AC-NC-05 (Beat cadence — 20 ticks per Beat), and AC-NC-06
        /// (TTL expiry within one tick boundary).
        /// </summary>
        void OnTickCompleted(uint tickNumber);

        /// <summary>
        /// Called when the server evaluates the OWL grace window for a <c>NotifySkillUsed</c> RPC.
        /// <paramref name="adjustedTimer"/> is the final <c>adjustedCycleTimer</c> value after OWL
        /// compensation (seconds); <paramref name="graceTriggers"/> reports whether
        /// <c>adjustedTimer &gt; BaseGraceThreshold × CycleDuration</c>. Used by AC-OWL-05 and
        /// AC-NC-29.
        /// </summary>
        void OnSkillGraceWindowEvaluated(uint entityId, float adjustedTimer, bool graceTriggers);

        /// <summary>
        /// Called when the server emits a <c>StatSnapshotEvent</c> to any client. Used by AC-NC-44
        /// (a negative AC — verifies <c>StatSnapshotEvent</c> is event-driven, not emitted every
        /// tick: count must be 0 over an idle 60-second window).
        /// </summary>
        void OnStatSnapshotEmitted(uint entityId);

        /// <summary>
        /// Called when a <c>NotifySkillUsed</c> RPC is rejected due to rate limiting
        /// (<c>NOTIFY_SKILL_USED_RATE_LIMIT_MS</c> enforcement). Used by AC-NC-46.
        /// </summary>
        void OnSkillUsedRateLimitRejected(uint entityId);

        // ---------------------------------------------------------------------
        // Query methods
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns the cumulative count of outbound messages of <paramref name="messageTypeId"/>
        /// emitted by the server to <paramref name="clientId"/> since the last reset or test harness
        /// initialization. Counts are incremented at the server serialization boundary — before
        /// fault injection — so a fragment subsequently dropped by
        /// <see cref="ITransportFaultInjector"/> still counts. AC-ZI-8 usage: pass
        /// <c>ZoneStateSnapshotFragment.MessageTypeID</c> to assert the exact fragment count emitted
        /// for a joining client matches F-ZI-1 (expected range [17, 19] at max load).
        /// </summary>
        int GetOutboundMessageCount(uint clientId, ushort messageTypeId);
    }

    /// <summary>
    /// Test-only reason code for <see cref="INetworkTestObserver.OnPersistenceWriteCompleted"/>.
    /// Declared inside the test/dev-build guard (unlike <see cref="SessionState"/> and
    /// <see cref="ZoneState"/>) because nothing outside <see cref="INetworkTestObserver"/>
    /// references it.
    /// </summary>
    public enum PersistenceWriteReason : byte
    {
        /// <summary>The session's TTL expired while disconnected (CR-NET-6.5).</summary>
        SessionExpiry = 0,

        /// <summary>The client explicitly disconnected (clean logout).</summary>
        ExplicitDisconnect = 1,

        /// <summary>A new authenticated connection for the same account stole the session (B-NP-7).</summary>
        SessionSteal = 2,

        /// <summary>The owning zone instance closed (ST-NET-2 Draining → Closed).</summary>
        ZoneClose = 3,

        /// <summary>The ghost combat TTL expired; character state written per CR-GH-10.</summary>
        GhostCombatTTLExpiry = 4,

        /// <summary>The ghost entity's HP reached zero; pre-disconnect snapshot HP persisted per CGS-5.</summary>
        GhostDeath = 5,
    }
}
#endif
