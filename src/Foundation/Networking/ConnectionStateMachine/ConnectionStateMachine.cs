using System;
using System.Collections.Generic;
using IronGrind.Currency;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The ST-NET-1 player connection state machine (<c>networking-session.md</c>). Story 012 built
    /// the six core (non-reconnect) transitions — <c>— → Connecting</c>, <c>Connecting → Connected</c>,
    /// <c>Connecting → Disconnected_SessionExpired</c>, <c>Connected → Disconnected_SessionActive</c>
    /// (heartbeat timeout), and the two <c>Connected → Disconnected_SessionExpired</c> paths (explicit
    /// disconnect, session-stealing) — and the actual per-account <see cref="SessionState"/> registry,
    /// the first (and still only) one in this codebase. Story 013 extends the SAME class and registry
    /// with 4 more transitions covering <see cref="SessionState.Reconnecting"/> — see the class
    /// remarks' "Story 013 additions" paragraph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sealed, stateful class, matching <see cref="CrossCuttingRpcGuardChain"/>'s shape (Story
    /// 010), not <see cref="CommitBeforeBroadcastSequencer"/>'s (Story 011):</b> unlike the
    /// sequencer, this class genuinely tracks state per account across calls (the current
    /// <see cref="SessionState"/>, the associated <c>characterId</c> once known, and the tick of
    /// last relevant activity), so it is a constructible class with its own in-memory registry, not
    /// a stateless static helper.
    /// </para>
    /// <para>
    /// <b>Out of scope, owned by neighbouring stories:</b> the TTL expiry sequence detail and zone
    /// crash recovery (Story 015), and the zone-level state machine ST-NET-2 (Story 014). This class
    /// never itself expires a <see cref="SessionState.Disconnected_SessionActive"/> session purely
    /// due to elapsed TTL time — <see cref="EvaluateTimeouts"/> deliberately has no
    /// <see cref="SessionState.Reconnecting"/> case (see that method's remarks and the Story 013
    /// paragraph below). The only ways this class ends a <see cref="SessionState.Reconnecting"/>
    /// session are the two synchronous, caller-driven events it does own:
    /// <see cref="RecordFailedReAuthAttempt"/> exhausting <c>REAUTH_FAILURE_LIMIT</c>, and
    /// <see cref="HandleReconnectSessionSteal"/>.
    /// </para>
    /// <para>
    /// <b>Story 013 additions — <see cref="SessionState.Reconnecting"/> transitions and their explicit
    /// scope boundary:</b> Story 012 built this registry specifically so a later story could extend it
    /// rather than duplicating state tracking. This class now owns 4 of the ST-NET-1 rows adjacent to
    /// <see cref="SessionState.Reconnecting"/>: <see cref="EnterReconnecting"/>
    /// (<c>Disconnected_SessionActive → Reconnecting</c>), <see cref="CompleteReAuthSuccess"/>
    /// (<c>Reconnecting → Connected</c>, the full CR-NET-6.4 sequence), <see cref="RecordFailedReAuthAttempt"/>
    /// (<c>Reconnecting → Disconnected_SessionActive</c> on a single failure, or
    /// <c>Reconnecting → Disconnected_SessionExpired</c> on <c>REAUTH_FAILURE_LIMIT</c> exhaustion —
    /// EC-NET-7), and <see cref="HandleReconnectSessionSteal"/> (<c>Reconnecting →
    /// Disconnected_SessionExpired</c> via session-stealing during re-authentication). The GDD's own
    /// transition table bundles a 5th trigger into that same <c>Reconnecting → Disconnected_SessionExpired</c>
    /// row — "session TTL elapses while re-authentication is in progress" (NP-NEW-2) — which this
    /// story deliberately does NOT implement: it has no dedicated AC in this story, and implementing
    /// it correctly needs the same tick-driven CR-NET-6.5 expiry-sequence infrastructure that AC-NC-12
    /// (Story 015, not this story) exercises. <see cref="EvaluateTimeouts"/> is therefore left
    /// untouched — no <see cref="SessionState.Reconnecting"/> case was added to its switch — and a
    /// <see cref="SessionState.Reconnecting"/> session whose TTL elapses without a completed re-auth,
    /// a failed-limit exhaustion, or a session-steal simply has no code path in this class that ends
    /// it. A future Story 015 pass is expected to add that case.
    /// </para>
    /// <para>
    /// <b>AC-NC-37 resolution (judgment call, approved before implementation):</b> the story's own AC
    /// text for "AC-NC-37" is byte-for-byte the same <see cref="SessionState.Connected"/>-state
    /// session-stealing scenario Story 012 already covers as <c>AC-NC-39-SESSION</c>
    /// (<see cref="HandleSessionSteal"/>). The GDD's transition table separately has a distinct,
    /// unlabeled <see cref="SessionState.Reconnecting"/>-state session-stealing row that no AC-NC-##
    /// identifier in the GDD's own Acceptance Criteria section names. <see cref="HandleReconnectSessionSteal"/>
    /// implements that row instead — the reading that gives this story's AC-NC-37 citation an actual
    /// <see cref="SessionState.Reconnecting"/>-specific scenario to test, rather than reproducing a
    /// byte-for-byte duplicate of a test Story 012 already wrote.
    /// </para>
    /// <para>
    /// <b><c>ReauthFailureCount</c> survives the <see cref="SessionState.Reconnecting"/> ↔
    /// <see cref="SessionState.Disconnected_SessionActive"/> bounce cycle by construction, not by
    /// caller discipline (EC-NET-7):</b> per the GDD's literal transition table, a single failed
    /// re-auth attempt that does not exhaust <c>REAUTH_FAILURE_LIMIT</c> returns the account to
    /// <see cref="SessionState.Disconnected_SessionActive"/> — the client's next reconnect attempt is
    /// a fresh <see cref="EnterReconnecting"/> call. <see cref="EnterReconnecting"/> mutates the
    /// existing <see cref="AccountSessionRecord"/> instance already present in <c>_sessions</c> in
    /// place (<c>record.State = ...; record.SessionExpiryTick = ...;</c>) — it never replaces the
    /// dictionary entry with a new <see cref="AccountSessionRecord"/> object the way
    /// <see cref="EnterConnecting"/> does for a brand-new connection. This is what makes
    /// <c>ReauthFailureCount</c> (and, independently, <c>SessionExpiryTick</c>) survive every
    /// bounce-back cycle within one TTL window: <see cref="EnterReconnecting"/> never touches
    /// <c>ReauthFailureCount</c> at all, so the field keeps accumulating across repeated
    /// <see cref="EnterReconnecting"/> / <see cref="RecordFailedReAuthAttempt"/> call pairs until
    /// either re-auth succeeds or the limit is exhausted. See <see cref="EnterReconnecting"/>'s own
    /// doc comment for the explicit in-place-mutation guarantee.
    /// </para>
    /// <para>
    /// <b>Persistence, zone-removal, transport-close, gold reconciliation, respec reservation, and
    /// session-token invalidation are all delegate seams, not real infrastructure</b> (mirrors Story
    /// 010's <c>RegisterEntityOwnership</c>/<c>MarkSessionReady</c> and Story 011's
    /// <c>disconnectClient</c>/<c>preserveSessionForTtl</c> precedent for the same forward-dependency
    /// shape): no persistence layer, zone manager, transport abstraction, <c>PendingPurchase</c> store,
    /// <c>ItemReservation</c>, or session-token registry exists in this codebase yet, so
    /// <see cref="ProcessExplicitDisconnect"/>, <see cref="HandleSessionSteal"/>,
    /// <see cref="CompleteReAuthSuccess"/>, <see cref="RecordFailedReAuthAttempt"/>, and
    /// <see cref="HandleReconnectSessionSteal"/> all accept caller-supplied delegates for those effects
    /// instead of inventing one. The one deliberate exception is gold: <see cref="CompleteReAuthSuccess"/>
    /// calls <see cref="ICurrencyService.AddGold"/> and <see cref="ICurrencyService.GetBalance"/>
    /// directly, because the Currency System is real, already-implemented, already-thread-safe
    /// production code (Currency System Stories 001–006) — not a forward dependency. This class proves
    /// only the state machine's own transition logic and ordering guarantees.
    /// </para>
    /// <para>
    /// <b>Synthetic <c>fromState</c> for a brand-new connection (judgment call, approved before
    /// implementation):</b> <see cref="INetworkTestObserver.OnSessionStateTransitioned"/> takes a
    /// non-nullable <see cref="SessionState"/> <c>fromState</c>, but ST-NET-1's <c>— → Connecting</c>
    /// row has no real prior state. <see cref="SessionState"/> is a pinned, five-value enum (ST-NET-1,
    /// control-manifest — "do not renumber once referenced by wire-serialized or persisted data"), so
    /// adding a sixth <c>None</c> sentinel member was rejected. <see cref="EnterConnecting"/> instead
    /// reports <see cref="SessionState.Disconnected_SessionExpired"/> as the synthetic
    /// <c>fromState</c> for every <c>— → Connecting</c> transition — semantically accurate ("no
    /// active/recoverable session"), exactly correct for the session-steal case (the account was
    /// literally just transitioned there, moments earlier, by <see cref="HandleSessionSteal"/>), and
    /// requires no enum change. This call site is a test-observability signal only, never
    /// wire-serialized, so reusing the value creates no wire-format conflict.
    /// </para>
    /// <para>
    /// <b><c>"ExplicitDisconnect"</c> trigger string (judgment call, approved before
    /// implementation):</b> the story's own Implementation Notes enumerate the trigger strings used
    /// across the epic (<c>"HeartbeatTimeout"</c>, <c>"ConnectingTimeout"</c>, <c>"SessionSteal"</c>,
    /// <c>"NewConnection"</c>) but do not give one for the explicit-disconnect transition, and
    /// AC-NC-26 does not itself assert that <see cref="INetworkTestObserver.OnSessionStateTransitioned"/>
    /// fires at all. <see cref="ProcessExplicitDisconnect"/> fires it anyway — for consistency, every
    /// real ST-NET-1 transition in this class's scope emits the callback — using
    /// <c>"ExplicitDisconnect"</c>, mirroring the already-existing
    /// <see cref="PersistenceWriteReason.ExplicitDisconnect"/> member name.
    /// </para>
    /// <para>
    /// <b>Registry removal is how "session resources released" / "pending slot released" is
    /// modeled:</b> <see cref="FailConnecting"/>'s and <see cref="EvaluateTimeouts"/>'s
    /// <c>ConnectingTimeout</c> path, <see cref="ProcessExplicitDisconnect"/>, and
    /// <see cref="HandleSessionSteal"/> all remove the account's registry entry — after that call,
    /// <see cref="IsAccountRegistered"/> reports <see langword="false"/>. The heartbeat-timeout path
    /// inside <see cref="EvaluateTimeouts"/> deliberately does not: AC-NC-10 requires the entity to
    /// remain in the zone and no session resources to be released, so that path only mutates the
    /// existing registry entry's <see cref="SessionState"/> in place.
    /// </para>
    /// <para>
    /// <b><see cref="HandleSessionSteal"/> removes the prior entry before re-registering the new
    /// connection (explicit ordering, not implied by assignment semantics):</b> even though
    /// <see cref="EnterConnecting"/> unconditionally overwrites whatever entry (if any) already
    /// exists for <paramref name="accountId"/> via a plain dictionary assignment — so a stray
    /// <c>Remove</c> beforehand has no functional effect on the end state — this method still calls
    /// <see cref="Dictionary{TKey,TValue}.Remove(TKey)"/> on the prior entry immediately after the
    /// prior session's cleanup completes and strictly before calling <see cref="EnterConnecting"/>.
    /// This keeps the code's own sequencing legible and self-documenting about the "prior-session
    /// cleanup completes before the new connection's <c>Connecting</c> transition fires" ordering
    /// guarantee (AC-NC-39-SESSION) — a reader (or a future maintainer editing
    /// <see cref="EnterConnecting"/> to no longer unconditionally overwrite) should never need to
    /// infer removal from an overwrite's side effect.
    /// </para>
    /// <para>
    /// <b>Deferred-removal scratch buffer in <see cref="EvaluateTimeouts"/> (lesson from
    /// <see cref="ServerTickLoop"/>'s Story 009 code review):</b> mutating an <i>existing</i>
    /// dictionary key's value in place (the <c>Connected → Disconnected_SessionActive</c> case) does
    /// not invalidate a <c>foreach</c> enumeration of <see cref="Dictionary{TKey,TValue}"/> — only
    /// structural changes (<c>Add</c>/<c>Remove</c>/<c>Clear</c>) do. Removing an entry (the
    /// <c>Connecting → Disconnected_SessionExpired</c> timeout case) therefore cannot happen inside
    /// the same enumeration pass; candidate accountIds are collected into
    /// <see cref="_connectingTimeoutScratch"/> (cleared and reused every call — no steady-state
    /// allocation once warmed up) and removed only after the enumeration completes, the same
    /// collect-then-mutate discipline <see cref="ServerTickLoop.AdvanceTick"/> already applies to its
    /// own TTL timer list.
    /// </para>
    /// <para>
    /// <b>Caller-supplied timeout tick counts, never stored as constants here:</b>
    /// <see cref="EvaluateTimeouts"/> takes <c>heartbeatTimeoutTicks</c> and
    /// <c>connectingTimeoutTicks</c> as parameters, exactly matching
    /// <see cref="HeartbeatActivityTracker.IsHeartbeatDue"/>'s <c>intervalTicks</c> pattern.
    /// <c>HEARTBEAT_TIMEOUT_SECONDS</c>'s production default is OQ-NET-1, still BLOCKING/undetermined
    /// — this class must not hardcode one. <c>CONNECTING_TIMEOUT_SECONDS</c> has a GDD default (30)
    /// but this story's own tests use 10; declaring either as a compile-time constant on this class
    /// was judged unnecessary scope (unlike <see cref="CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS"/>,
    /// which that story's own AC text required as a concrete value passed to a delegate).
    /// </para>
    /// <para>
    /// <b>Story 018 addition — <see cref="HandleGhostDeathWhileReconnecting"/> (AC-CGS-3, CGS-6):</b>
    /// a fifth <see cref="SessionState.Reconnecting"/>-adjacent row, covering "a ghost entity's HP
    /// reaches zero while a reconnect ACK for the same account is queued the same tick." None of the
    /// four Story 013 methods above cover this case — it is a death racing an in-progress reconnect,
    /// not a session steal, a failed re-auth, or a successful one. See that method's own remarks for
    /// the full call order and for how CGS-6's single-tick priority rule is proven structurally (via
    /// <see cref="RequireState"/> rejecting a same-tick <see cref="CompleteReAuthSuccess"/> attempt
    /// once this method has already run), not via a new lock or timestamp comparison. The sibling
    /// CGS-4 (TTL expiry) and CGS-5 (ghost death, no reconnect race) write-ordering paths live in the
    /// separate, stateless <see cref="GhostCleanupSequencer"/> — not in this class — because those
    /// two paths do not mutate this class's own <see cref="SessionState"/> registry.
    /// </para>
    /// <para>
    /// <b>Story 019 addition — <see cref="CompleteGhostDeathFromDisconnected"/> (AC-GH-4):</b> the
    /// <see cref="SessionState.Disconnected_SessionActive"/>-state counterpart to
    /// <see cref="HandleGhostDeathWhileReconnecting"/> — both cover "a ghost entity's HP reaches
    /// zero," but this method is the common (non-racing) case: the account is
    /// <see cref="SessionState.Disconnected_SessionActive"/>, with no reconnect in progress, when
    /// death is processed. No prior story built this <c>Disconnected_SessionActive →
    /// Disconnected_SessionExpired</c> row via a <c>"GhostDeath"</c> trigger. The mob de-targeting
    /// half of ghost death (CR-GH-6, CR-GH-7, AC-GH-5) is a separate, stateless
    /// <see cref="MobDeTargetingCoordinator"/> — not in this class — since it never touches this
    /// class's own <see cref="SessionState"/> registry, mirroring <see cref="GhostCleanupSequencer"/>'s
    /// own separation rationale in the paragraph above.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var stateMachine = new ConnectionStateMachine();
    ///
    /// // A client initiates a transport connection:
    /// stateMachine.EnterConnecting(accountId: 7, currentTick: 100u, observer);
    ///
    /// // Authentication and zone assignment complete:
    /// stateMachine.CompleteAuthSuccess(accountId: 7, characterId: 555u, currentTick: 105u, observer);
    ///
    /// // Every inbound packet resets the heartbeat-silence window:
    /// stateMachine.RecordInboundActivity(accountId: 7, currentTick: 110u);
    ///
    /// // Once per server tick (a future integration story wires this via ServerTickLoop.RegisterTickDriven):
    /// stateMachine.EvaluateTimeouts(currentTick: tickLoop.ServerTickNumber,
    ///     heartbeatTimeoutTicks: 60u, connectingTimeoutTicks: 200u, observer);
    ///
    /// // An explicit client logout:
    /// stateMachine.ProcessExplicitDisconnect(accountId: 7,
    ///     persistFinalCharacterState: characterId => persistence.Save(characterId),
    ///     removeEntityFromZone: characterId => zone.RemoveEntity(characterId),
    ///     broadcastPlayerLeftZone: (characterId, type) => zone.BroadcastPlayerLeftZone(characterId, type),
    ///     observer);
    ///
    /// // (Story 013) The client reconnects within the TTL:
    /// stateMachine.EnterReconnecting(accountId: 7, sessionExpiryTick: 6100u, observer);
    /// stateMachine.CompleteReAuthSuccess(accountId: 7, currencySystem,
    ///     queryGoldDebitedPurchases: charId => pendingPurchaseStore.QueryGoldDebited(charId),
    ///     markPurchaseRefunded: record => pendingPurchaseStore.MarkRefundedAndDelete(record),
    ///     queryRespecReservationStatus: charId => respecReservations.QueryStatus(charId),
    ///     representRespecPhase2: charId => respecFlow.RepresentPhase2(charId),
    ///     releaseRespecReservationAndNotify: charId => respecReservations.ReleaseAndNotify(charId),
    ///     handshakeData: new SessionHandshakeData(false, goldVersion: 3u, level: 15, currentHp: 80,
    ///         currentMp: 40, heldFreePoints: 0, classType: 0),
    ///     observer);
    /// </code>
    /// </example>
    public sealed class ConnectionStateMachine
    {
        /// <summary>
        /// One account's in-memory session record. A mutable reference type (not a struct) so
        /// <see cref="EvaluateTimeouts"/> can update <see cref="State"/> in place during dictionary
        /// enumeration without any structural dictionary mutation (see class remarks).
        /// </summary>
        private sealed class AccountSessionRecord
        {
            /// <summary>The account's current <see cref="SessionState"/>.</summary>
            internal SessionState State;

            /// <summary>
            /// The character associated with this session. <c>0</c> (unset) while
            /// <see cref="State"/> is <see cref="SessionState.Connecting"/> — not known until
            /// <see cref="CompleteAuthSuccess"/>.
            /// </summary>
            internal uint CharacterId;

            /// <summary>
            /// The tick of last relevant activity. Meaning depends on <see cref="State"/>: while
            /// <see cref="SessionState.Connecting"/>, the tick the connection attempt began (the
            /// <c>CONNECTING_TIMEOUT_TICKS</c> baseline); while <see cref="SessionState.Connected"/>,
            /// the tick of the most recent inbound packet (the <c>HEARTBEAT_TIMEOUT_TICKS</c>
            /// baseline).
            /// </summary>
            internal uint LastActivityTick;

            /// <summary>
            /// (Story 013) The frozen absolute tick at which this session's TTL expires — set once by
            /// <see cref="EnterReconnecting"/> from a caller-supplied value (this class does not itself
            /// compute <c>disconnectTick + SESSION_TTL_TICKS</c> — see class remarks) and never
            /// recomputed on a subsequent <see cref="EnterReconnecting"/> re-entry within the same TTL
            /// window, so long as the caller keeps passing the same original value (EC-NET-7). Default
            /// <c>0</c> for any account that has never entered <see cref="SessionState.Reconnecting"/>.
            /// </summary>
            internal uint SessionExpiryTick;

            /// <summary>
            /// (Story 013) The count of consecutive failed re-authentication attempts recorded by
            /// <see cref="RecordFailedReAuthAttempt"/> since this session last became
            /// <see cref="SessionState.Disconnected_SessionActive"/> for the first time in its current
            /// TTL window. Deliberately never reset by <see cref="EnterReconnecting"/> — see class
            /// remarks' EC-NET-7 paragraph — so it correctly accumulates across repeated
            /// <see cref="SessionState.Reconnecting"/> ↔ <see cref="SessionState.Disconnected_SessionActive"/>
            /// bounce cycles within one TTL window.
            /// </summary>
            internal int ReauthFailureCount;
        }

        private readonly Dictionary<uint, AccountSessionRecord> _sessions = new();

        // Reused scratch buffer for EvaluateTimeouts' deferred-removal pass (see class remarks) —
        // cleared at the start of every call, so no steady-state allocation once warmed up.
        private readonly List<uint> _connectingTimeoutScratch = new();

        /// <summary>
        /// Registers <paramref name="accountId"/> as a brand-new pending connection (ST-NET-1 row 1,
        /// <c>— → Connecting</c>): "client initiates transport connection." Unconditionally
        /// overwrites any prior registry entry for <paramref name="accountId"/> — callers that need
        /// an explicit prior-session cleanup boundary (e.g. <see cref="HandleSessionSteal"/>) perform
        /// that cleanup themselves before calling this method.
        /// </summary>
        /// <param name="accountId">The account initiating the connection.</param>
        /// <param name="currentTick">
        /// The current server tick — recorded as the <c>CONNECTING_TIMEOUT_TICKS</c> baseline for
        /// <see cref="EvaluateTimeouts"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires
        /// <c>OnSessionStateTransitioned(accountId, Disconnected_SessionExpired, Connecting,
        /// "NewConnection")</c> — see class remarks for why <see cref="SessionState.Disconnected_SessionExpired"/>
        /// is the synthetic <c>fromState</c> for a connection with no real prior state.
        /// </param>
        /// <example>
        /// <code>stateMachine.EnterConnecting(accountId: 7, currentTick: 100u, observer);</code>
        /// </example>
        public void EnterConnecting(uint accountId, uint currentTick
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            _sessions[accountId] = new AccountSessionRecord
            {
                State = SessionState.Connecting,
                CharacterId = 0u,
                LastActivityTick = currentTick,
            };

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Disconnected_SessionExpired,
                SessionState.Connecting, "NewConnection");
#endif
        }

        /// <summary>
        /// Completes authentication and zone assignment (ST-NET-1 row 2, <c>Connecting →
        /// Connected</c>). Requires <paramref name="accountId"/> to currently be
        /// <see cref="SessionState.Connecting"/>.
        /// </summary>
        /// <param name="accountId">The account completing authentication.</param>
        /// <param name="characterId">The character now associated with this session.</param>
        /// <param name="currentTick">
        /// The current server tick — reseeds the <c>HEARTBEAT_TIMEOUT_TICKS</c> baseline (the
        /// account's silence window starts fresh at the moment it becomes <c>Connected</c>, not at
        /// whatever tick it originally entered <c>Connecting</c>).
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnSessionStateTransitioned(accountId,
        /// Connecting, Connected, "AuthSuccess")</c>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Connecting"/>.
        /// </exception>
        /// <example>
        /// <code>stateMachine.CompleteAuthSuccess(accountId: 7, characterId: 555u, currentTick: 105u, observer);</code>
        /// </example>
        public void CompleteAuthSuccess(uint accountId, uint characterId, uint currentTick
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            AccountSessionRecord record = RequireState(accountId, SessionState.Connecting, nameof(CompleteAuthSuccess));

            record.State = SessionState.Connected;
            record.CharacterId = characterId;
            record.LastActivityTick = currentTick;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Connecting, SessionState.Connected, "AuthSuccess");
#endif
        }

        /// <summary>
        /// Abandons a pending connection (ST-NET-1 row 3, <c>Connecting → Disconnected_SessionExpired</c>)
        /// for any of the non-timeout causes the GDD collapses into that same row — auth failure, zone
        /// full, or the client dropping mid-handshake. <see cref="EvaluateTimeouts"/>'s
        /// <c>ConnectingTimeout</c> path shares this exact release-and-transition logic via
        /// <see cref="TransitionConnectingToExpired"/>; this method is the caller-driven counterpart
        /// for the three non-timeout causes, none of which has a dedicated GDD trigger-string constant
        /// or a dedicated AC test in this story — the caller supplies whatever label fits.
        /// </summary>
        /// <param name="accountId">The account whose pending connection is being abandoned.</param>
        /// <param name="trigger">
        /// The <c>OnSessionStateTransitioned</c> trigger label (e.g. <c>"AuthFailed"</c>,
        /// <c>"ZoneFull"</c>, <c>"ClientDroppedDuringHandshake"</c> — none of these three exact
        /// strings are prescribed anywhere in the story or GDD text, unlike <c>"ConnectingTimeout"</c>).
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Connecting"/>.
        /// </exception>
        /// <example>
        /// <code>stateMachine.FailConnecting(accountId: 7, trigger: "AuthFailed", observer);</code>
        /// </example>
        public void FailConnecting(uint accountId, string trigger
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            RequireState(accountId, SessionState.Connecting, nameof(FailConnecting));

            TransitionConnectingToExpired(accountId, trigger
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , observer
#endif
                );
        }

        /// <summary>
        /// Records that an inbound packet of any type was received from <paramref name="accountId"/>
        /// at <paramref name="currentTick"/> — the "has this client gone silent" tracker AC-NC-10
        /// requires, deliberately distinct from <see cref="HeartbeatActivityTracker"/> (which tracks
        /// <i>outbound</i> packets for skip-on-activity heartbeat scheduling, an unrelated concern).
        /// A no-op if <paramref name="accountId"/> is not currently <see cref="SessionState.Connected"/>
        /// — a stray packet from an unregistered or already-disconnected account is not this class's
        /// concern to flag (that belongs to a guard/validation layer, not the state machine itself).
        /// </summary>
        /// <param name="accountId">The account the inbound packet was received from.</param>
        /// <param name="currentTick">The server tick the packet was received on.</param>
        /// <example>
        /// <code>stateMachine.RecordInboundActivity(accountId: 7, currentTick: 110u);</code>
        /// </example>
        public void RecordInboundActivity(uint accountId, uint currentTick)
        {
            if (_sessions.TryGetValue(accountId, out AccountSessionRecord record) && record.State == SessionState.Connected)
            {
                record.LastActivityTick = currentTick;
            }
        }

        /// <summary>
        /// The single per-tick sweep evaluating both tick-based timeouts this story owns (AC-NC-10,
        /// AC-NC-39-CONNECTING): <see cref="SessionState.Connecting"/> accounts silent for at least
        /// <paramref name="connectingTimeoutTicks"/> since <see cref="EnterConnecting"/> transition to
        /// <see cref="SessionState.Disconnected_SessionExpired"/> (<c>"ConnectingTimeout"</c>, slot
        /// released); <see cref="SessionState.Connected"/> accounts silent for at least
        /// <paramref name="heartbeatTimeoutTicks"/> since the last <see cref="RecordInboundActivity"/>
        /// call transition to <see cref="SessionState.Disconnected_SessionActive"/>
        /// (<c>"HeartbeatTimeout"</c>, registry entry retained — no resources released). Both
        /// thresholds are boundary-inclusive at equality, via <see cref="StaleDiscardComparer.IsTickExpired"/>.
        /// Intended to be called exactly once per server tick — the future integration point a later
        /// story wires via <see cref="ServerTickLoop.RegisterTickDriven"/>.
        /// </summary>
        /// <param name="currentTick">The current server tick.</param>
        /// <param name="heartbeatTimeoutTicks">
        /// <c>HEARTBEAT_TIMEOUT_SECONDS × TICK_RATE_HZ</c> — caller-supplied, never hardcoded here
        /// (OQ-NET-1 is still undetermined; see class remarks).
        /// </param>
        /// <param name="connectingTimeoutTicks"><c>CONNECTING_TIMEOUT_SECONDS × TICK_RATE_HZ</c> — caller-supplied.</param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <example>
        /// <code>
        /// stateMachine.EvaluateTimeouts(currentTick: tickLoop.ServerTickNumber,
        ///     heartbeatTimeoutTicks: 60u, connectingTimeoutTicks: 200u, observer);
        /// </code>
        /// </example>
        public void EvaluateTimeouts(uint currentTick, uint heartbeatTimeoutTicks, uint connectingTimeoutTicks
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            _connectingTimeoutScratch.Clear();

            foreach (KeyValuePair<uint, AccountSessionRecord> entry in _sessions)
            {
                AccountSessionRecord record = entry.Value;

                switch (record.State)
                {
                    case SessionState.Connecting:
                    {
                        uint expiryTick = record.LastActivityTick + connectingTimeoutTicks;
                        if (StaleDiscardComparer.IsTickExpired(currentTick, expiryTick))
                        {
                            // Removal is deferred until after this enumeration completes (see class remarks).
                            _connectingTimeoutScratch.Add(entry.Key);
                        }

                        break;
                    }

                    case SessionState.Connected:
                    {
                        uint expiryTick = record.LastActivityTick + heartbeatTimeoutTicks;
                        if (StaleDiscardComparer.IsTickExpired(currentTick, expiryTick))
                        {
                            // In-place mutation of an existing entry's value — safe mid-enumeration,
                            // no structural dictionary change (see class remarks).
                            record.State = SessionState.Disconnected_SessionActive;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                            observer?.OnSessionStateTransitioned(entry.Key, SessionState.Connected,
                                SessionState.Disconnected_SessionActive, "HeartbeatTimeout");
#endif
                        }

                        break;
                    }

                    default:
                        // Disconnected_SessionActive TTL expiry via elapsed tick time (Story 015) is
                        // out of scope for this class. Reconnecting is NOT out of scope as of Story
                        // 013 in general (see EnterReconnecting, CompleteReAuthSuccess,
                        // RecordFailedReAuthAttempt, HandleReconnectSessionSteal) — but this sweep
                        // specifically does not evaluate a Reconnecting session's TTL against
                        // currentTick (the NP-NEW-2 sub-case), a deliberate scope-out — see class
                        // remarks' "Story 013 additions" paragraph.
                        break;
                }
            }

            for (int i = 0; i < _connectingTimeoutScratch.Count; i++)
            {
                TransitionConnectingToExpired(_connectingTimeoutScratch[i], "ConnectingTimeout"
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                    , observer
#endif
                    );
            }
        }

        /// <summary>
        /// Processes an explicit client disconnect (ST-NET-1 row 5, <c>Connected →
        /// Disconnected_SessionExpired</c>, AC-NC-26). Requires <paramref name="accountId"/> to
        /// currently be <see cref="SessionState.Connected"/>. The 5-minute session TTL is skipped
        /// entirely — no <see cref="SessionState.Disconnected_SessionActive"/> state is ever entered.
        /// Call order: <paramref name="persistFinalCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, ExplicitDisconnect)</c> →
        /// <paramref name="removeEntityFromZone"/> → <paramref name="broadcastPlayerLeftZone"/>
        /// (with <see cref="DisconnectType.Graceful"/>) → registry release →
        /// <c>OnSessionStateTransitioned(accountId, Connected, Disconnected_SessionExpired,
        /// "ExplicitDisconnect")</c>.
        /// </summary>
        /// <param name="accountId">The disconnecting account.</param>
        /// <param name="persistFinalCharacterState">
        /// Writes the character's final state to persistence, called with the session's
        /// <c>characterId</c>. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="removeEntityFromZone">
        /// Removes the character's entity from its zone, called with <c>characterId</c>. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="broadcastPlayerLeftZone">
        /// Broadcasts <c>PlayerLeftZone</c> to other zone clients, called with (<c>characterId</c>,
        /// <see cref="DisconnectType.Graceful"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">
        /// Any of the three delegate parameters is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Connected"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.ProcessExplicitDisconnect(accountId: 7,
        ///     persistFinalCharacterState: characterId => persistence.Save(characterId),
        ///     removeEntityFromZone: characterId => zone.RemoveEntity(characterId),
        ///     broadcastPlayerLeftZone: (characterId, type) => zone.BroadcastPlayerLeftZone(characterId, type),
        ///     observer);
        /// </code>
        /// </example>
        public void ProcessExplicitDisconnect(
            uint accountId,
            Action<uint> persistFinalCharacterState,
            Action<uint> removeEntityFromZone,
            Action<uint, DisconnectType> broadcastPlayerLeftZone
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            if (removeEntityFromZone == null)
            {
                throw new ArgumentNullException(nameof(removeEntityFromZone));
            }

            if (broadcastPlayerLeftZone == null)
            {
                throw new ArgumentNullException(nameof(broadcastPlayerLeftZone));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Connected, nameof(ProcessExplicitDisconnect));
            uint characterId = record.CharacterId;

            // Skip TTL entirely; write final state to persistence immediately (AC-NC-26 a/b).
            persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.ExplicitDisconnect);
#endif

            removeEntityFromZone(characterId); // AC-NC-26 c
            broadcastPlayerLeftZone(characterId, DisconnectType.Graceful); // AC-NC-26 d

            _sessions.Remove(accountId); // release entity and session — no Disconnected_SessionActive ever entered

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Connected,
                SessionState.Disconnected_SessionExpired, "ExplicitDisconnect");
#endif
        }

        /// <summary>
        /// Processes session-stealing (ST-NET-1 row 6, AC-NC-39-SESSION / GDD AC-NC-37): a new
        /// authenticated connection arrives for the same account while it is
        /// <see cref="SessionState.Connected"/>. Invalidates and fully releases the prior session,
        /// then registers the new connection at <see cref="SessionState.Connecting"/> — structurally
        /// guaranteeing "prior-session cleanup completes before the new connection's <c>Connecting</c>
        /// transition fires," since both halves run synchronously inside this one method (the same
        /// structural-ordering technique <see cref="CommitBeforeBroadcastSequencer"/> uses for its own
        /// ordering guarantee). Call order: <c>OnSessionInvalidatedBySteal</c> →
        /// <paramref name="persistFinalCharacterState"/> → <c>OnPersistenceWriteCompleted(characterId,
        /// SessionSteal)</c> → <c>OnSessionStateTransitioned(accountId, Connected,
        /// Disconnected_SessionExpired, "SessionSteal")</c> → <paramref name="closePriorTransportConnection"/>
        /// → registry release → <see cref="EnterConnecting"/> (which itself fires
        /// <c>OnSessionStateTransitioned(accountId, Disconnected_SessionExpired, Connecting,
        /// "NewConnection")</c>).
        /// </summary>
        /// <param name="accountId">The account being stolen.</param>
        /// <param name="newConnectionCurrentTick">
        /// The current server tick, seeding the new connection's <c>CONNECTING_TIMEOUT_TICKS</c>
        /// baseline via the internal <see cref="EnterConnecting"/> call.
        /// </param>
        /// <param name="persistFinalCharacterState">
        /// Writes the prior session's character state to persistence, called with the prior session's
        /// <c>characterId</c>. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="closePriorTransportConnection">
        /// Closes the prior transport connection, called with <paramref name="accountId"/>. Must not
        /// be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">
        /// Either delegate parameter is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Connected"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.HandleSessionSteal(accountId: 7, newConnectionCurrentTick: 500u,
        ///     persistFinalCharacterState: characterId => persistence.Save(characterId),
        ///     closePriorTransportConnection: accountId => transport.Close(accountId),
        ///     observer);
        /// </code>
        /// </example>
        public void HandleSessionSteal(
            uint accountId,
            uint newConnectionCurrentTick,
            Action<uint> persistFinalCharacterState,
            Action<uint> closePriorTransportConnection
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            if (closePriorTransportConnection == null)
            {
                throw new ArgumentNullException(nameof(closePriorTransportConnection));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Connected, nameof(HandleSessionSteal));
            uint priorCharacterId = record.CharacterId;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionInvalidatedBySteal(accountId, priorCharacterId); // (a)
#endif

            persistFinalCharacterState(priorCharacterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(priorCharacterId, PersistenceWriteReason.SessionSteal); // (b)
#endif

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Connected,
                SessionState.Disconnected_SessionExpired, "SessionSteal"); // (c)
#endif

            closePriorTransportConnection(accountId); // (d)

            // Explicit removal before EnterConnecting re-registers the account (see class remarks —
            // EnterConnecting's own overwrite-assignment would make this functionally redundant, but
            // this keeps the "prior-session cleanup completes before the new Connecting transition"
            // ordering guarantee legible in the code itself, not just implied by assignment semantics).
            _sessions.Remove(accountId);

            EnterConnecting(accountId, newConnectionCurrentTick
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , observer
#endif
                ); // (e) — strictly after (a)-(d) above, by construction
        }

        // =====================================================================================
        // Story 013 — SessionState.Reconnecting transitions (ST-NET-1 rows adjacent to
        // Disconnected_SessionActive/Reconnecting; see class remarks' "Story 013 additions"
        // paragraph for full scope, including the AC-NC-37 resolution and the NP-NEW-2 scope-out).
        // =====================================================================================

        /// <summary>
        /// Begins re-authentication for a client that re-established its transport connection within
        /// the session TTL (ST-NET-1, <c>Disconnected_SessionActive → Reconnecting</c>). Requires
        /// <paramref name="accountId"/> to currently be <see cref="SessionState.Disconnected_SessionActive"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>In-place mutation, not replacement — load-bearing for EC-NET-7 (confirmed before
        /// implementation):</b> this method mutates the <em>existing</em> <see cref="AccountSessionRecord"/>
        /// instance already present in <c>_sessions</c> (<c>record.State = ...; record.SessionExpiryTick
        /// = ...;</c>) via <see cref="RequireState"/>'s dictionary lookup. It never does
        /// <c>_sessions[accountId] = new AccountSessionRecord {...}</c> the way <see cref="EnterConnecting"/>
        /// does for a brand-new connection. This is exactly what makes <c>ReauthFailureCount</c> (not
        /// touched by this method at all) survive every <see cref="SessionState.Reconnecting"/> ↔
        /// <see cref="SessionState.Disconnected_SessionActive"/> bounce cycle within one TTL window —
        /// if this method ever replaced the record object instead, <c>ReauthFailureCount</c> would
        /// silently reset to <c>0</c> on every re-entry, defeating <see cref="RecordFailedReAuthAttempt"/>'s
        /// whole EC-NET-7 guarantee. Do not change this method to a replacing assignment.
        /// </para>
        /// <para>
        /// <b><paramref name="sessionExpiryTick"/> is caller-supplied and re-asserted, not recomputed
        /// or defended against a changing value across repeated calls:</b> this class does not itself
        /// compute <c>disconnectTick + SESSION_TTL_TICKS</c> (that formula, and the
        /// <c>Connected → Disconnected_SessionActive</c> transition that would seed it, are Story
        /// 015's territory — see class remarks). Every call to this method (including a bounce-back
        /// re-entry after <see cref="RecordFailedReAuthAttempt"/> returns the account to
        /// <see cref="SessionState.Disconnected_SessionActive"/>) writes <paramref name="sessionExpiryTick"/>
        /// into the record again. EC-NET-7 compliance for this specific parameter is therefore the
        /// caller's responsibility — the caller must pass the same original value on every re-entry
        /// within one TTL window — exactly like <see cref="EnterConnecting"/>'s <c>currentTick</c> is
        /// trusted without cross-call validation.
        /// </para>
        /// <para>
        /// Does not validate <c>IsTickExpired(currentTick, sessionExpiryTick)</c> internally — the
        /// caller is expected to have already verified the reconnect attempt is within the TTL window
        /// (AC-NC-11's own scenario) before calling. A reconnect attempt arriving after TTL expiry is
        /// the NP-NEW-2 scope-out (see class remarks); this method assumes it is never called for an
        /// already-expired session.
        /// </para>
        /// </remarks>
        /// <param name="accountId">The account re-establishing its transport connection.</param>
        /// <param name="sessionExpiryTick">
        /// The absolute tick at which this session's TTL expires — computed by the caller from the
        /// original disconnect tick, not by this class. Stored verbatim on the record.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnSessionStateTransitioned(accountId,
        /// Disconnected_SessionActive, Reconnecting, "ReconnectAttempt")</c>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Disconnected_SessionActive"/>.
        /// </exception>
        /// <example>
        /// <code>stateMachine.EnterReconnecting(accountId: 7, sessionExpiryTick: 6100u, observer);</code>
        /// </example>
        public void EnterReconnecting(uint accountId, uint sessionExpiryTick
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            AccountSessionRecord record = RequireState(accountId, SessionState.Disconnected_SessionActive, nameof(EnterReconnecting));

            // In-place mutation of the existing record instance — see this method's own remarks and
            // the class remarks' EC-NET-7 paragraph for why this must never become a replacing
            // assignment (ReauthFailureCount must survive this call untouched).
            record.State = SessionState.Reconnecting;
            record.SessionExpiryTick = sessionExpiryTick;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Disconnected_SessionActive,
                SessionState.Reconnecting, "ReconnectAttempt");
#endif
        }

        /// <summary>
        /// Completes a successful reconnect (ST-NET-1, <c>Reconnecting → Connected</c>): runs the full
        /// CR-NET-6.4 reconnect sequence steps 2–4 (step 1, re-authentication, is assumed to have
        /// already succeeded by the time this is called — the caller-driven-external-event pattern
        /// <see cref="CompleteAuthSuccess"/> already establishes for the initial-connect case; step 5,
        /// the client seeding its own <c>cachedVersion</c>, is a client-side concern outside this
        /// class). Requires <paramref name="accountId"/> to currently be <see cref="SessionState.Reconnecting"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Call order (CR-NET-6.4, structural — not just documented, matching
        /// <see cref="CommitBeforeBroadcastSequencer"/>'s "structural, not timing-dependent" ordering
        /// discipline):</b> transition to <see cref="SessionState.Connected"/> → for each
        /// <c>GoldDebited</c> record from <paramref name="queryGoldDebitedPurchases"/>: <see cref="ICurrencyService.AddGold"/>
        /// (<see cref="GoldTransactionReason.CompensatingRefund"/>) → <paramref name="markPurchaseRefunded"/>
        /// → reconciliation log line (ADR-001 Decision 4 step 4) — this entire loop runs to completion
        /// strictly before → <c>OnSessionHandshakeEmitted</c> (gold balance read fresh from
        /// <see cref="ICurrencyService.GetBalance"/>, post-reconciliation) → respec reservation check
        /// (<paramref name="queryRespecReservationStatus"/>, then <paramref name="representRespecPhase2"/>
        /// or <paramref name="releaseRespecReservationAndNotify"/> or neither). Because every step runs
        /// synchronously inside this one method body, "reconciliation completes before handshake
        /// emission, and before any <c>BuyRequest</c>/<c>SellRequest</c> is accepted" (ADR-001 Decision
        /// 4 / AC-NC-CR64-RECONCILE) is guaranteed by construction — a caller cannot observe the
        /// handshake without every reconciliation call having already returned.
        /// </para>
        /// <para>
        /// <b>Gold balance is real, not a stub:</b> unlike <c>wasKilledWhileDisconnected</c>/<c>level</c>/
        /// <c>currentHp</c>/<c>currentMp</c>/<c>heldFreePoints</c>/<c>classType</c> (bundled into
        /// <paramref name="handshakeData"/> because this class does not own Character Stats/Leveling/
        /// Class System state), the <c>goldBalance</c> value passed to <c>OnSessionHandshakeEmitted</c>
        /// is read fresh from <paramref name="currencyService"/>.<see cref="ICurrencyService.GetBalance"/>
        /// after the reconciliation loop completes — genuinely post-reconciliation, per CR-NET-6.4 step
        /// 3's "current gold balance (post-reconciliation)" requirement, not a caller-supplied
        /// placeholder.
        /// </para>
        /// <para>
        /// <b>No failure path for <see cref="ICurrencyService.AddGold"/> during reconciliation:</b>
        /// ADR-001 Decision 4's reconciliation algorithm does not describe one (unlike Decision 3's
        /// original-purchase-time refund, which does: "if <c>AddGold</c> itself fails ... leave for
        /// manual reconciliation"). This method calls <see cref="ICurrencyService.AddGold"/> and always
        /// proceeds to <paramref name="markPurchaseRefunded"/> regardless of the returned
        /// <c>GoldMutationResult.Success</c> — matching the story's own scope note that this class
        /// "only proves the reconnect-sequence ORDERING," not exhaustive reconciliation failure
        /// handling (out of scope; a future story can harden this if a real <c>PendingPurchase</c>
        /// store is built).
        /// </para>
        /// </remarks>
        /// <param name="accountId">The account completing re-authentication.</param>
        /// <param name="currencyService">
        /// The real, already-implemented Currency System (Currency System Stories 001–006) — called
        /// directly, not through a delegate seam (see class remarks). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="queryGoldDebitedPurchases">
        /// Returns every <c>PendingPurchase</c> record in the <c>GoldDebited</c> state for the given
        /// <c>characterId</c> (ADR-001 Decision 4 step 1). Delegate seam — no real <c>PendingPurchase</c>
        /// store exists yet (see <see cref="PendingPurchaseRecord"/> remarks). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="markPurchaseRefunded">
        /// Marks a reconciled record <c>Refunded</c> and deletes it (ADR-001 Decision 4 step 3). Called
        /// once per record returned by <paramref name="queryGoldDebitedPurchases"/>, immediately after
        /// the corresponding <see cref="ICurrencyService.AddGold"/> call. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="queryRespecReservationStatus">
        /// Returns whether <c>characterId</c> has an active respec Phase 1 reservation and, if so,
        /// whether its own 30-second TTL is still valid (CR-NET-6.4 step 4, AC-NC-13). Delegate seam —
        /// no real <c>ItemReservation</c> type exists yet. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="representRespecPhase2">
        /// Called with <c>characterId</c> when <paramref name="queryRespecReservationStatus"/> returns
        /// <see cref="RespecReservationStatus.TtlValid"/> (AC-NC-13a). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="releaseRespecReservationAndNotify">
        /// Called with <c>characterId</c> when <paramref name="queryRespecReservationStatus"/> returns
        /// <see cref="RespecReservationStatus.TtlExpired"/> (AC-NC-13b) — the caller's implementation
        /// is responsible for both releasing the reservation and emitting the "Respec scroll returned
        /// to inventory" notification; this class constructs neither. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="handshakeData">
        /// The caller-supplied character-state fields this class does not own — see
        /// <see cref="SessionHandshakeData"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Fires <c>OnSessionStateTransitioned(accountId,
        /// Reconnecting, Connected, "ReAuthSuccess")</c>, then <c>OnSessionHandshakeEmitted</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="currencyService"/>, <paramref name="queryGoldDebitedPurchases"/>,
        /// <paramref name="markPurchaseRefunded"/>, <paramref name="queryRespecReservationStatus"/>,
        /// <paramref name="representRespecPhase2"/>, or <paramref name="releaseRespecReservationAndNotify"/>
        /// is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Reconnecting"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.CompleteReAuthSuccess(accountId: 7, currencySystem,
        ///     queryGoldDebitedPurchases: charId =&gt; pendingPurchaseStore.QueryGoldDebited(charId),
        ///     markPurchaseRefunded: record =&gt; pendingPurchaseStore.MarkRefundedAndDelete(record),
        ///     queryRespecReservationStatus: charId =&gt; respecReservations.QueryStatus(charId),
        ///     representRespecPhase2: charId =&gt; respecFlow.RepresentPhase2(charId),
        ///     releaseRespecReservationAndNotify: charId =&gt; respecReservations.ReleaseAndNotify(charId),
        ///     handshakeData: new SessionHandshakeData(false, goldVersion: 3u, level: 15, currentHp: 80,
        ///         currentMp: 40, heldFreePoints: 0, classType: (byte)ClassType.Warrior),
        ///     observer);
        /// </code>
        /// </example>
        public void CompleteReAuthSuccess(
            uint accountId,
            ICurrencyService currencyService,
            Func<uint, IReadOnlyList<PendingPurchaseRecord>> queryGoldDebitedPurchases,
            Action<PendingPurchaseRecord> markPurchaseRefunded,
            Func<uint, RespecReservationStatus> queryRespecReservationStatus,
            Action<uint> representRespecPhase2,
            Action<uint> releaseRespecReservationAndNotify,
            SessionHandshakeData handshakeData
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (currencyService == null)
            {
                throw new ArgumentNullException(nameof(currencyService));
            }

            if (queryGoldDebitedPurchases == null)
            {
                throw new ArgumentNullException(nameof(queryGoldDebitedPurchases));
            }

            if (markPurchaseRefunded == null)
            {
                throw new ArgumentNullException(nameof(markPurchaseRefunded));
            }

            if (queryRespecReservationStatus == null)
            {
                throw new ArgumentNullException(nameof(queryRespecReservationStatus));
            }

            if (representRespecPhase2 == null)
            {
                throw new ArgumentNullException(nameof(representRespecPhase2));
            }

            if (releaseRespecReservationAndNotify == null)
            {
                throw new ArgumentNullException(nameof(releaseRespecReservationAndNotify));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Reconnecting, nameof(CompleteReAuthSuccess));
            uint characterId = record.CharacterId;

            record.State = SessionState.Connected;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Reconnecting, SessionState.Connected, "ReAuthSuccess");
#endif

            // CR-NET-6.4 step 2 / ADR-001 Decision 4 — must complete in full before step 3 (handshake
            // emission) below. Structural ordering: this loop is synchronous code that runs to
            // completion before the OnSessionHandshakeEmitted call further down in this same method.
            IReadOnlyList<PendingPurchaseRecord> pendingPurchases = queryGoldDebitedPurchases(characterId);
            for (int i = 0; i < pendingPurchases.Count; i++)
            {
                PendingPurchaseRecord purchase = pendingPurchases[i];

                currencyService.AddGold(purchase.CharId, purchase.TotalCost, GoldTransactionReason.CompensatingRefund);
                markPurchaseRefunded(purchase);

                Debug.Log($"[ConnectionStateMachine] PendingPurchaseReconciled: charId={purchase.CharId}, " +
                    $"itemId={purchase.ItemId}, quantity={purchase.Quantity}, totalCost={purchase.TotalCost}, " +
                    $"requestId={purchase.RequestId}");
            }

            // CR-NET-6.4 step 3 — gold balance read fresh, post-reconciliation.
            uint goldBalance = currencyService.GetBalance(new CharacterID(characterId));

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionHandshakeEmitted(characterId, handshakeData.WasKilledWhileDisconnected,
                (int)goldBalance, handshakeData.GoldVersion, handshakeData.Level, handshakeData.CurrentHp,
                handshakeData.CurrentMp, handshakeData.HeldFreePoints, handshakeData.ClassType);
#endif

            // CR-NET-6.4 step 4 — respec reservation check.
            RespecReservationStatus respecStatus = queryRespecReservationStatus(characterId);
            switch (respecStatus)
            {
                case RespecReservationStatus.TtlValid:
                    representRespecPhase2(characterId);
                    break;

                case RespecReservationStatus.TtlExpired:
                    releaseRespecReservationAndNotify(characterId);
                    break;

                case RespecReservationStatus.NoActiveReservation:
                default:
                    break; // nothing to do — no active reservation to resolve
            }
        }

        /// <summary>
        /// Records one failed re-authentication attempt while <paramref name="accountId"/> is
        /// <see cref="SessionState.Reconnecting"/> (EC-NET-7). Requires <paramref name="accountId"/>
        /// to currently be <see cref="SessionState.Reconnecting"/> — per the GDD's own transition
        /// table, a single non-exhausting failure returns the account to
        /// <see cref="SessionState.Disconnected_SessionActive"/>, so the caller must call
        /// <see cref="EnterReconnecting"/> again before the next reconnect attempt's failure can be
        /// recorded here.
        /// </summary>
        /// <remarks>
        /// <b><c>sessionExpiryTick</c> is never written by this method, on either branch:</b> this is
        /// the entire EC-NET-7 guarantee ("no TTL extension on failure") expressed structurally — a
        /// reader can confirm by inspection that this method's body contains no assignment to
        /// <c>record.SessionExpiryTick</c> anywhere.
        /// </remarks>
        /// <param name="accountId">The account whose re-authentication attempt failed.</param>
        /// <param name="reauthFailureLimit">
        /// <c>REAUTH_FAILURE_LIMIT</c> — caller-supplied, never hardcoded here (GDD default 3, tuning
        /// range [1,10]; matches this class's existing "caller-supplied timeout tick counts, never
        /// stored as constants here" precedent from <see cref="EvaluateTimeouts"/> — see class
        /// remarks).
        /// </param>
        /// <param name="persistFinalCharacterState">
        /// Writes the character's final state to persistence, called with the session's
        /// <c>characterId</c> — only on the limit-exhausted branch. Validated non-null unconditionally
        /// regardless of which branch is taken, matching <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/>'s
        /// precedent of validating every delegate upfront. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. Always fires <c>OnReAuthAttemptFailed(accountId,
        /// attemptNumber, remainingAttempts)</c> first. On the limit-exhausted branch, immediately
        /// followed by <c>OnPersistenceWriteCompleted(characterId, SessionExpiry)</c> then
        /// <c>OnSessionStateTransitioned(accountId, Reconnecting, Disconnected_SessionExpired,
        /// "ReauthLimitExceeded")</c>. On the non-exhausted branch, immediately followed by
        /// <c>OnSessionStateTransitioned(accountId, Reconnecting, Disconnected_SessionActive,
        /// "ReAuthFailed")</c>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="persistFinalCharacterState"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Reconnecting"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.RecordFailedReAuthAttempt(accountId: 7, reauthFailureLimit: 3,
        ///     persistFinalCharacterState: characterId =&gt; persistence.Save(characterId),
        ///     observer);
        /// </code>
        /// </example>
        public void RecordFailedReAuthAttempt(
            uint accountId,
            int reauthFailureLimit,
            Action<uint> persistFinalCharacterState
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Reconnecting, nameof(RecordFailedReAuthAttempt));

            record.ReauthFailureCount += 1; // never resets SessionExpiryTick — see remarks
            int attemptNumber = record.ReauthFailureCount;
            int remainingAttempts = reauthFailureLimit - attemptNumber;
            if (remainingAttempts < 0)
            {
                remainingAttempts = 0; // defensive floor — callers are expected to stop calling once exhausted
            }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnReAuthAttemptFailed(accountId, attemptNumber, remainingAttempts);
#endif

            if (attemptNumber >= reauthFailureLimit)
            {
                // EC-NET-7 limit exhausted: execute the expiry outcome. record.SessionExpiryTick is
                // untouched by this entire method — the TTL never extended across any of the failed
                // attempts that led here.
                uint characterId = record.CharacterId;

                persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.SessionExpiry);
#endif

                _sessions.Remove(accountId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnSessionStateTransitioned(accountId, SessionState.Reconnecting,
                    SessionState.Disconnected_SessionExpired, "ReauthLimitExceeded");
#endif
            }
            else
            {
                record.State = SessionState.Disconnected_SessionActive;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnSessionStateTransitioned(accountId, SessionState.Reconnecting,
                    SessionState.Disconnected_SessionActive, "ReAuthFailed");
#endif
            }
        }

        /// <summary>
        /// Processes session-stealing that arrives while <paramref name="accountId"/> is
        /// <see cref="SessionState.Reconnecting"/> — the GDD transition-table row distinct from
        /// <see cref="HandleSessionSteal"/>'s <see cref="SessionState.Connected"/>-state row (see class
        /// remarks' "AC-NC-37 resolution" paragraph for why this method, not a duplicate of
        /// <see cref="HandleSessionSteal"/>, is what this story's AC-NC-37 actually exercises).
        /// Requires <paramref name="accountId"/> to currently be <see cref="SessionState.Reconnecting"/>.
        /// </summary>
        /// <remarks>
        /// Call order, mirroring <see cref="HandleSessionSteal"/>'s structural-ordering technique:
        /// <c>OnSessionInvalidatedBySteal</c> → <paramref name="invalidateSessionToken"/> (CR-TOK-6) →
        /// <paramref name="persistFinalCharacterState"/> → <c>OnPersistenceWriteCompleted(characterId,
        /// SessionSteal)</c> → <c>OnSessionStateTransitioned(accountId, Reconnecting,
        /// Disconnected_SessionExpired, "SessionStealDuringReconnect")</c> →
        /// <paramref name="closePriorTransportConnection"/> → registry release →
        /// <see cref="EnterConnecting"/> (which itself fires the new connection's own <c>NewConnection</c>
        /// transition). All of steps (a)-(f) run synchronously inside this one method body, strictly
        /// before (g) — the same ordering guarantee <see cref="HandleSessionSteal"/> already
        /// establishes for the <see cref="SessionState.Connected"/>-state row.
        /// </remarks>
        /// <param name="accountId">The account being stolen mid-reconnect.</param>
        /// <param name="newConnectionCurrentTick">
        /// The current server tick, seeding the new connection's <c>CONNECTING_TIMEOUT_TICKS</c>
        /// baseline via the internal <see cref="EnterConnecting"/> call.
        /// </param>
        /// <param name="invalidateSessionToken">
        /// Invalidates the prior session's token per CR-TOK-6 (<c>networking-session-token.md</c>),
        /// called with <paramref name="accountId"/>. Delegate seam — Story 016 (session tokens) is not
        /// built yet. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="persistFinalCharacterState">
        /// Writes the prior session's character state to persistence, called with the prior session's
        /// <c>characterId</c>. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="closePriorTransportConnection">
        /// Closes the prior (in-progress reconnect) transport connection, called with
        /// <paramref name="accountId"/>. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">Any of the three delegate parameters is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Reconnecting"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.HandleReconnectSessionSteal(accountId: 7, newConnectionCurrentTick: 900u,
        ///     invalidateSessionToken: accountId =&gt; tokenStore.Invalidate(accountId),
        ///     persistFinalCharacterState: characterId =&gt; persistence.Save(characterId),
        ///     closePriorTransportConnection: accountId =&gt; transport.Close(accountId),
        ///     observer);
        /// </code>
        /// </example>
        public void HandleReconnectSessionSteal(
            uint accountId,
            uint newConnectionCurrentTick,
            Action<uint> invalidateSessionToken,
            Action<uint> persistFinalCharacterState,
            Action<uint> closePriorTransportConnection
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (invalidateSessionToken == null)
            {
                throw new ArgumentNullException(nameof(invalidateSessionToken));
            }

            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            if (closePriorTransportConnection == null)
            {
                throw new ArgumentNullException(nameof(closePriorTransportConnection));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Reconnecting, nameof(HandleReconnectSessionSteal));
            uint priorCharacterId = record.CharacterId;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionInvalidatedBySteal(accountId, priorCharacterId); // (a)
#endif

            invalidateSessionToken(accountId); // (b) — CR-TOK-6

            persistFinalCharacterState(priorCharacterId); // (c)

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(priorCharacterId, PersistenceWriteReason.SessionSteal); // (d)
#endif

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Reconnecting,
                SessionState.Disconnected_SessionExpired, "SessionStealDuringReconnect"); // (e)
#endif

            closePriorTransportConnection(accountId); // (f)

            // Explicit removal before EnterConnecting re-registers the account — mirrors
            // HandleSessionSteal's own precedent (see that method's remarks) for keeping the ordering
            // guarantee legible in the code itself.
            _sessions.Remove(accountId);

            EnterConnecting(accountId, newConnectionCurrentTick
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , observer
#endif
                ); // (g) — strictly after (a)-(f) above, by construction
        }

        /// <summary>Returns the current <see cref="SessionState"/> for <paramref name="accountId"/>, if registered.</summary>
        /// <param name="accountId">The account to query.</param>
        /// <param name="state">The account's current state, if registered; otherwise <see langword="default"/>.</param>
        /// <returns><see langword="true"/> if <paramref name="accountId"/> has a registry entry.</returns>
        /// <example>
        /// <code>
        /// if (stateMachine.TryGetSessionState(accountId: 7, out SessionState state))
        /// {
        ///     // ...
        /// }
        /// </code>
        /// </example>
        public bool TryGetSessionState(uint accountId, out SessionState state)
        {
            if (_sessions.TryGetValue(accountId, out AccountSessionRecord record))
            {
                state = record.State;
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>
        /// (Story 013) Returns the frozen <c>sessionExpiryTick</c> value stored on
        /// <paramref name="accountId"/>'s registry entry, if registered — <c>0</c> for any account
        /// that has never entered <see cref="SessionState.Reconnecting"/> via <see cref="EnterReconnecting"/>.
        /// Exists so a caller (or a test verifying EC-NET-7) can confirm the TTL deadline is provably
        /// unchanged across repeated <see cref="RecordFailedReAuthAttempt"/> calls, rather than merely
        /// inferring it from having passed the same literal into every <see cref="EnterReconnecting"/>
        /// call.
        /// </summary>
        /// <param name="accountId">The account to query.</param>
        /// <param name="sessionExpiryTick">The account's stored TTL deadline, if registered; otherwise <see langword="default"/>.</param>
        /// <returns><see langword="true"/> if <paramref name="accountId"/> has a registry entry.</returns>
        /// <example>
        /// <code>
        /// if (stateMachine.TryGetSessionExpiryTick(accountId: 7, out uint expiryTick))
        /// {
        ///     // ...
        /// }
        /// </code>
        /// </example>
        public bool TryGetSessionExpiryTick(uint accountId, out uint sessionExpiryTick)
        {
            if (_sessions.TryGetValue(accountId, out AccountSessionRecord record))
            {
                sessionExpiryTick = record.SessionExpiryTick;
                return true;
            }

            sessionExpiryTick = default;
            return false;
        }

        /// <summary>
        /// Returns whether <paramref name="accountId"/> currently has any registry entry — the
        /// "session resources released" / "pending slot released" query (see class remarks).
        /// </summary>
        /// <param name="accountId">The account to query.</param>
        /// <example>
        /// <code>bool stillHeld = stateMachine.IsAccountRegistered(accountId: 7);</code>
        /// </example>
        public bool IsAccountRegistered(uint accountId) => _sessions.ContainsKey(accountId);

        // =====================================================================================
        // Story 015 — Disconnected_SessionActive TTL expiry (AC-NC-12, CR-NET-6.5).
        // =====================================================================================

        /// <summary>
        /// Completes the CR-NET-6.5 TTL-expiry sequence for a <see cref="SessionState.Disconnected_SessionActive"/>
        /// account once its session TTL has elapsed (AC-NC-12): <c>Disconnected_SessionActive →
        /// Disconnected_SessionExpired</c>. A no-op if <paramref name="sessionExpiryTick"/> has not yet
        /// been reached — this method is designed to be called on any cadence a caller chooses (e.g.
        /// once per tick) for every account currently <see cref="SessionState.Disconnected_SessionActive"/>,
        /// similar in spirit to <see cref="EvaluateTimeouts"/>'s own per-tick sweep, just scoped to one
        /// account and caller-driven rather than a full-registry sweep (this class never itself computes
        /// or stores a <c>Disconnected_SessionActive</c> expiry tick; the caller supplies it every call,
        /// mirroring <see cref="EnterReconnecting"/>'s <c>sessionExpiryTick</c> parameter — see class
        /// remarks' "caller-supplied timeout tick counts, never stored as constants here" precedent).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Call order (CR-NET-6.5 steps 3–6, structural — matching <see cref="ProcessExplicitDisconnect"/>'s
        /// exact call shape):</b> <paramref name="persistFinalCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, SessionExpiry)</c> →
        /// <paramref name="removeEntityFromZone"/> → <paramref name="broadcastPlayerLeftZone"/> (with
        /// <see cref="DisconnectType.Timeout"/>, not <see cref="DisconnectType.Graceful"/> — this is a
        /// TTL expiry, not an explicit logout) → registry release → <c>OnSessionStateTransitioned(accountId,
        /// Disconnected_SessionActive, Disconnected_SessionExpired, "TTLExpired")</c>. CR-NET-6.5 steps 1
        /// (complete the current Beat boundary) and 2 (process death if <c>CurrentHP==0</c>) are out of
        /// scope for this class — no Beat/cycle-timer system and no Character Stats system exist yet in
        /// this codebase (forward-dependency gap, matching this class's established delegate-seam
        /// precedent for persistence/zone/broadcast effects it does not own).
        /// </para>
        /// <para>
        /// <b>Delegates validated unconditionally, before the tick-expiry check:</b> matching
        /// <see cref="RecordFailedReAuthAttempt"/>'s precedent of validating every delegate upfront
        /// regardless of which branch executes, so a caller passing a <see langword="null"/> delegate
        /// fails loudly even on a call that ultimately turns out to be a no-op.
        /// </para>
        /// </remarks>
        /// <param name="accountId">The account whose <see cref="SessionState.Disconnected_SessionActive"/> session may have expired.</param>
        /// <param name="currentTick">The current server tick.</param>
        /// <param name="sessionExpiryTick">
        /// The absolute tick at which this session's TTL expires — caller-supplied, never computed or
        /// stored by this class.
        /// </param>
        /// <param name="persistFinalCharacterState">
        /// Writes the character's final state to persistence, called with the session's <c>characterId</c>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="removeEntityFromZone">
        /// Removes the character's entity from its zone, called with <c>characterId</c>. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="broadcastPlayerLeftZone">
        /// Broadcasts <c>PlayerLeftZone</c> to other zone clients, called with (<c>characterId</c>,
        /// <see cref="DisconnectType.Timeout"/>). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">Any of the three delegate parameters is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Disconnected_SessionActive"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.CompleteSessionActiveTTLExpiry(accountId: 7, currentTick: 6100u, sessionExpiryTick: 6100u,
        ///     persistFinalCharacterState: characterId =&gt; persistence.Save(characterId),
        ///     removeEntityFromZone: characterId =&gt; zone.RemoveEntity(characterId),
        ///     broadcastPlayerLeftZone: (characterId, type) =&gt; zone.BroadcastPlayerLeftZone(characterId, type),
        ///     observer);
        /// </code>
        /// </example>
        public void CompleteSessionActiveTTLExpiry(
            uint accountId,
            uint currentTick,
            uint sessionExpiryTick,
            Action<uint> persistFinalCharacterState,
            Action<uint> removeEntityFromZone,
            Action<uint, DisconnectType> broadcastPlayerLeftZone
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            if (removeEntityFromZone == null)
            {
                throw new ArgumentNullException(nameof(removeEntityFromZone));
            }

            if (broadcastPlayerLeftZone == null)
            {
                throw new ArgumentNullException(nameof(broadcastPlayerLeftZone));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Disconnected_SessionActive, nameof(CompleteSessionActiveTTLExpiry));

            if (!StaleDiscardComparer.IsTickExpired(currentTick, sessionExpiryTick))
            {
                return; // not yet expired — no-op; caller is expected to invoke this again on a later tick
            }

            uint characterId = record.CharacterId;

            persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.SessionExpiry);
#endif

            removeEntityFromZone(characterId);
            broadcastPlayerLeftZone(characterId, DisconnectType.Timeout);

            _sessions.Remove(accountId); // release session slot, ghost memory record, zone entity slot (CR-NET-6.5 step 5)

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Disconnected_SessionActive,
                SessionState.Disconnected_SessionExpired, "TTLExpired");
#endif
        }

        // =====================================================================================
        // Story 018 — Ghost Session cluster: CGS-6 single-tick death-vs-reconnect priority
        // (AC-CGS-3). See class remarks' "Story 018 addition" paragraph, and GhostCleanupSequencer
        // for the sibling CGS-4/CGS-5 write-ordering paths that do not touch this class's registry.
        // =====================================================================================

        /// <summary>
        /// Completes ghost-death cleanup for an account whose reconnect was already in progress when
        /// the killing blow was processed (AC-CGS-3, CGS-6's single-tick death-vs-reconnect priority
        /// rule): <c>Reconnecting → Disconnected_SessionExpired</c>. Requires
        /// <paramref name="accountId"/> to currently be <see cref="SessionState.Reconnecting"/> — a
        /// death racing a reconnect ACK means the account was mid-reconnect (re-authenticating) when
        /// death was detected the same tick.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A genuinely new ST-NET-1-adjacent transition row, not a re-use of an existing method
        /// (confirmed before implementation):</b> none of this class's existing
        /// <see cref="SessionState.Reconnecting"/>-adjacent methods — <see cref="EnterReconnecting"/>,
        /// <see cref="CompleteReAuthSuccess"/>, <see cref="RecordFailedReAuthAttempt"/>,
        /// <see cref="HandleReconnectSessionSteal"/> — represent "death arrives while reconnecting."
        /// This method is the fifth <see cref="SessionState.Reconnecting"/>-adjacent row this class
        /// owns.
        /// </para>
        /// <para>
        /// <b>Call order (AC-CGS-3, CGS-6):</b> <paramref name="persistFinalCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, GhostDeath)</c> →
        /// <paramref name="sendZoneSessionEnded"/> (with <see cref="DisconnectReason.GhostDeath"/>) →
        /// registry release → <c>OnSessionStateTransitioned(accountId, Reconnecting,
        /// Disconnected_SessionExpired, "GhostDeath")</c> — note the <c>fromState</c> is
        /// <see cref="SessionState.Reconnecting"/>, NOT <see cref="SessionState.Disconnected_SessionActive"/>
        /// (AC-CGS-3's own restored pass-condition text calls this out specifically — this scenario is
        /// specifically a death racing a reconnect already in progress).
        /// </para>
        /// <para>
        /// <b>CGS-6's single-tick priority is proven by call order, not new synchronization
        /// (structural, no lock needed — matching this class's and <see cref="GhostCleanupSequencer"/>'s
        /// established "ordering guarantee via synchronous call order" discipline):</b> the tick
        /// loop's single-threaded dispatch is the serialization point. A caller that processes the
        /// death queue before the reconnect queue within the same tick — by invoking this method
        /// before attempting <see cref="CompleteReAuthSuccess"/> for the same account — structurally
        /// guarantees death wins: once this method returns, <paramref name="accountId"/> is no longer
        /// registered as <see cref="SessionState.Reconnecting"/>, so a subsequent same-tick
        /// <see cref="CompleteReAuthSuccess"/> attempt against the same account throws
        /// <see cref="InvalidOperationException"/> via <see cref="RequireState"/> — the reconnect ACK
        /// is correctly rejected once death has already been processed first. No new locking or
        /// timestamp comparison is introduced; this class's existing <see cref="RequireState"/>
        /// precondition check is what enforces the priority rule.
        /// </para>
        /// <para>
        /// <b><paramref name="sendZoneSessionEnded"/> is a delegate seam, not a real transport
        /// message</b> (mirrors this class's established precedent for <c>persistFinalCharacterState</c>/
        /// <c>removeEntityFromZone</c>/<c>broadcastPlayerLeftZone</c> elsewhere in this class): no
        /// <c>ZoneSessionEnded</c> message serializer exists yet in this codebase, and no
        /// <see cref="INetworkTestObserver"/> hook exists for it either (confirmed before
        /// implementation — the delegate seam is the resolution, not a missing observer callback).
        /// </para>
        /// </remarks>
        /// <param name="accountId">The account whose reconnect is being superseded by ghost death.</param>
        /// <param name="persistFinalCharacterState">
        /// Writes the character's final state (pre-disconnect snapshot HP, respawn position,
        /// <c>wasKilledWhileDisconnected = true</c>) to persistence, called with the session's
        /// <c>characterId</c> (read from the account's own registry entry, not caller-supplied — see
        /// remarks). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="sendZoneSessionEnded">
        /// Sends <c>ZoneSessionEnded</c> to the reconnecting client, called with
        /// (<paramref name="accountId"/>, <see cref="DisconnectReason.GhostDeath"/>). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">
        /// Either delegate parameter is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as <see cref="SessionState.Reconnecting"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.HandleGhostDeathWhileReconnecting(accountId: 7,
        ///     persistFinalCharacterState: characterId =&gt; persistence.SaveGhostDeath(characterId),
        ///     sendZoneSessionEnded: (accountId, reason) =&gt; transport.SendZoneSessionEnded(accountId, reason),
        ///     observer);
        /// </code>
        /// </example>
        public void HandleGhostDeathWhileReconnecting(
            uint accountId,
            Action<uint> persistFinalCharacterState,
            Action<uint, DisconnectReason> sendZoneSessionEnded
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            if (sendZoneSessionEnded == null)
            {
                throw new ArgumentNullException(nameof(sendZoneSessionEnded));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Reconnecting, nameof(HandleGhostDeathWhileReconnecting));
            uint characterId = record.CharacterId;

            persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath);
#endif

            sendZoneSessionEnded(accountId, DisconnectReason.GhostDeath);

            _sessions.Remove(accountId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Reconnecting,
                SessionState.Disconnected_SessionExpired, "GhostDeath");
#endif
        }

        // =====================================================================================
        // Story 019 — Ghost Session cluster: normal-path (non-racing) ghost-death session
        // transition (AC-GH-4). See class remarks' "Story 019 addition" paragraph, and
        // MobDeTargetingCoordinator for the sibling CR-GH-6/CR-GH-7 mob de-targeting sequence that
        // does not touch this class's registry.
        // =====================================================================================

        /// <summary>
        /// Completes ghost-death cleanup for an account that is currently
        /// <see cref="SessionState.Disconnected_SessionActive"/> (the normal, non-racing ghost-death
        /// path, AC-GH-4): <c>Disconnected_SessionActive → Disconnected_SessionExpired</c>, trigger
        /// <c>"GhostDeath"</c>. Requires <paramref name="accountId"/> to currently be
        /// <see cref="SessionState.Disconnected_SessionActive"/> — distinct from
        /// <see cref="HandleGhostDeathWhileReconnecting"/> (Story 018), which handles the
        /// <see cref="SessionState.Reconnecting"/>-race variant of the same underlying event (a
        /// ghost's HP reaching zero). No prior story built this specific
        /// <c>Disconnected_SessionActive → Disconnected_SessionExpired</c> row via a
        /// <c>"GhostDeath"</c> trigger.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A genuinely new ST-NET-1-adjacent transition row, not a re-use of an existing method
        /// (confirmed before implementation):</b> none of this class's existing methods — including
        /// <see cref="HandleGhostDeathWhileReconnecting"/> — cover "a ghost entity's HP reaches zero
        /// while the account is <see cref="SessionState.Disconnected_SessionActive"/>, with no
        /// reconnect in progress." This method is the sixth <see cref="SessionState"/>-adjacent row
        /// this class owns beyond the original six ST-NET-1 rows.
        /// </para>
        /// <para>
        /// <b><paramref name="accountId"/>'s <c>characterId</c> is derived from the registry entry,
        /// not an independent caller parameter</b> (matching
        /// <see cref="HandleGhostDeathWhileReconnecting"/>'s own code-review-fixed convention): a
        /// caller-supplied <c>characterId</c> could disagree with the account's actual registered
        /// character, an inconsistency this method's own registry lookup makes structurally
        /// impossible.
        /// </para>
        /// <para>
        /// <b>Call order (AC-GH-4):</b> <paramref name="persistFinalCharacterState"/> →
        /// <c>OnPersistenceWriteCompleted(characterId, GhostDeath)</c> → registry release →
        /// <c>OnSessionStateTransitioned(accountId, Disconnected_SessionActive,
        /// Disconnected_SessionExpired, "GhostDeath")</c>.
        /// </para>
        /// <para>
        /// <b>No <c>sendZoneSessionEnded</c>/<see cref="DisconnectReason"/> delegate parameter,
        /// unlike <see cref="HandleGhostDeathWhileReconnecting"/>:</b> AC-GH-4's own restored
        /// pass-condition text does not assert a <c>ZoneSessionEnded</c> send for this state — the
        /// reconnecting-client <c>ZoneSessionEnded(reason: GhostDeath)</c> delivery Story 018 already
        /// built only applies when a reconnect was actually in flight (the racing case this method
        /// does NOT cover). A normal-path ghost death (this method) has no in-flight reconnect client
        /// to notify via that specific message.
        /// </para>
        /// <para>
        /// <b>Does not call into <see cref="GhostCleanupSequencer"/>, mirroring
        /// <see cref="HandleGhostDeathWhileReconnecting"/>'s own precedent:</b> this method owns its
        /// own persist-then-transition sequence directly, exactly as
        /// <see cref="HandleGhostDeathWhileReconnecting"/> does for the <see cref="SessionState.Reconnecting"/>
        /// row — neither method delegates to <see cref="GhostCleanupSequencer"/>, which itself never
        /// performs a session-state transition (see that class's own remarks).
        /// </para>
        /// </remarks>
        /// <param name="accountId">The account whose ghost died while disconnected.</param>
        /// <param name="persistFinalCharacterState">
        /// Writes the character's final state (pre-disconnect snapshot HP, respawn position,
        /// <c>wasKilledWhileDisconnected = true</c>) to persistence, called with the session's
        /// <c>characterId</c> (read from the account's own registry entry, not caller-supplied — see
        /// remarks). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="persistFinalCharacterState"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="accountId"/> is not currently registered as
        /// <see cref="SessionState.Disconnected_SessionActive"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// stateMachine.CompleteGhostDeathFromDisconnected(accountId: 7,
        ///     persistFinalCharacterState: characterId =&gt; persistence.SaveGhostDeath(characterId),
        ///     observer);
        /// </code>
        /// </example>
        public void CompleteGhostDeathFromDisconnected(
            uint accountId,
            Action<uint> persistFinalCharacterState
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (persistFinalCharacterState == null)
            {
                throw new ArgumentNullException(nameof(persistFinalCharacterState));
            }

            AccountSessionRecord record = RequireState(accountId, SessionState.Disconnected_SessionActive,
                nameof(CompleteGhostDeathFromDisconnected));
            uint characterId = record.CharacterId;

            persistFinalCharacterState(characterId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath);
#endif

            _sessions.Remove(accountId);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Disconnected_SessionActive,
                SessionState.Disconnected_SessionExpired, "GhostDeath");
#endif
        }

        /// <summary>
        /// Shared release-and-transition logic for the <c>Connecting → Disconnected_SessionExpired</c>
        /// row, used by both <see cref="EvaluateTimeouts"/>'s <c>ConnectingTimeout</c> path and
        /// <see cref="FailConnecting"/>'s caller-driven non-timeout paths. Does not re-validate prior
        /// state — callers are responsible for only invoking this for an account already confirmed to
        /// be <see cref="SessionState.Connecting"/>.
        /// </summary>
        private void TransitionConnectingToExpired(uint accountId, string trigger
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer
#endif
            )
        {
            _sessions.Remove(accountId); // release the pending session slot; no persistence write — no session was ever established

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnSessionStateTransitioned(accountId, SessionState.Connecting,
                SessionState.Disconnected_SessionExpired, trigger);
#endif
        }

        /// <summary>
        /// Looks up <paramref name="accountId"/>'s registry entry and throws
        /// <see cref="InvalidOperationException"/> if it is missing or not currently
        /// <paramref name="requiredState"/>. Centralizes the prior-state precondition every
        /// caller-driven transition method in this class enforces.
        /// </summary>
        private AccountSessionRecord RequireState(uint accountId, SessionState requiredState, string callerName)
        {
            if (!_sessions.TryGetValue(accountId, out AccountSessionRecord record) || record.State != requiredState)
            {
                throw new InvalidOperationException(
                    $"[ConnectionStateMachine] {callerName}: accountId={accountId} is not currently registered as {requiredState}.");
            }

            return record;
        }
    }
}
