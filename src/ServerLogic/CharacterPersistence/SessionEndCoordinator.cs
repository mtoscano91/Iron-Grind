using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Starts the session-end saves of Character Persistence Story 010 (clean logout, CR-CP-4, and session TTL expiry)
    /// and, in the completion callback on the tick thread, releases the session (CR-CP-10 steps 4, 9, 10, CR-CP-11;
    /// ADR-011 Decisions 1 and 2).
    /// </summary>
    /// <remarks>
    /// <para>The save is started with <see cref="ICharacterPersistence.SaveSession(CharacterID, SessionEndReason, CancellationToken)"/>
    /// and its task is passed, in the same statement, to <see cref="ITickCompletionQueue.Track{T}"/>; nothing is awaited or
    /// blocked on. <see cref="ConnectionStateMachine.ProcessExplicitDisconnect"/> and
    /// <see cref="ConnectionStateMachine.CompleteSessionActiveTTLExpiry"/> are called only from the completion callback and with a
    /// no-op persist delegate, because the save has already happened (Networking Core is not changed).</para>
    /// <para><b>Release</b> (each step exactly once, in this order): the persistence session-table entry
    /// (<see cref="ICharacterSessionRelease.ReleaseSession"/>), <see cref="IInventoryService.UnregisterCharacter"/>,
    /// the <c>EquipRequest</c> retransmit-store clear and <see cref="IEquipmentService.UnregisterCharacter"/> (Equipment Story 008), the Leveling
    /// player-entity unregistration (skipped when the persistence entry did not exist, as the entity id is then unknown), then the
    /// state machine's method, which removes the entity from the zone, broadcasts <c>PlayerLeftZone</c> and removes the session.
    /// Character Stats and Currency have no per-character release (TD-066). A step that throws stops the release there with
    /// one error log; the callback still finishes, and a logout still reports Success because its save committed.</para>
    /// <para><b>Clean logout</b>: a failed save (any <see cref="CharacterSaveResult"/> other than Success, and a faulted, cancelled or
    /// timed-out task, all reported as <see cref="CharacterSaveResult.DatabaseError"/>) runs no release step; the session stays
    /// <see cref="SessionState.Connected"/> and <c>onResult</c> receives the failure once so the caller can retry (CR-CP-10 step 9).
    /// <b>TTL expiry</b>: the release runs whatever the result (step 10); the save itself logs the critical error, so this class logs none.</para>
    /// <para><b>One end per account (EC-CP-4)</b>: while an end is in flight, a second logout or TTL evaluation starts nothing. The
    /// in-flight entry is cleared before <c>onResult</c> runs, so a retry from the callback starts a new save.</para>
    /// <para><b>State re-check</b>: the completion callback calls the state machine only if the account is still in the state the
    /// end was started from (Connected for a logout, Disconnected_SessionActive for a TTL expiry). Nothing in Networking Core
    /// refuses a reconnect after the expiry tick today, so the state can differ by then (reconnect, session steal, ghost cleanup). If
    /// it differs, no release step runs at all (releasing the persistence entry of a live or already-released session would break
    /// the new owner of that state), one error is logged, and the in-flight entry is cleared; a logout still reports the save's own
    /// result. The TTL callback cannot see the drain tick, so it passes the expiry tick itself as the current tick to
    /// <see cref="ConnectionStateMachine.CompleteSessionActiveTTLExpiry"/> (an equal tick counts as expired).</para>
    /// <para><b>Waiting for the mutation gate (Story 017, ADR-011 Decision 4)</b>: a session-end save is started only while the
    /// character's <see cref="ICharacterMutationGate"/> is open. If an end is requested while the gate is held (an irreversible write
    /// is in flight), nothing is started, the account is marked in flight and the end waits for
    /// <see cref="ICharacterMutationGate.OnGateOpened"/>; the save then takes its snapshot and version after the outcome was delivered
    /// or rolled back, with no write ahead of it in the queue. At the open the account state is re-checked: a waiting logout whose
    /// account is no longer Connected (the failed write's failure protocol disconnected and preserved it) is dropped, with
    /// <c>onResult(DatabaseError)</c> and no release, and the TTL expiry saves the preserved session later; a waiting TTL end whose
    /// account is no longer Disconnected_SessionActive starts nothing and logs one warning if the player reconnected (Connected), one
    /// error otherwise.</para>
    /// <para><b>Lifetime and shutdown</b>: the constructor subscribes to the gate's <see cref="ICharacterMutationGate.OnGateOpened"/>
    /// and nothing unsubscribes, so the coordinator is meant to live as long as the gate (one per zone server). An end still waiting
    /// on a gate when the server shuts down is never started and nothing is logged for it; a save started from a gate opened during
    /// the shutdown drain is reported by <see cref="ITickCompletionQueue"/>'s shutdown alert with no callback (TD-071).</para>
    /// <para><b>Ghost-death end (Death and Respawn Story 010, <see cref="BeginGhostDeath"/>)</b>: a disconnected
    /// ghost that dies is saved with <see cref="SessionEndReason.GhostDeath"/> and the pre-disconnect or respawn HP,
    /// then released whatever the save result (like the TTL end), the state machine's ghost-death completion runs
    /// (Disconnected_SessionActive or Reconnecting at completion), the zone slot is freed, the account is marked
    /// and the optional delegates run.</para>
    /// <para>Not thread-safe: tick thread only, like <see cref="ITickCompletionQueue"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var ends = new SessionEndCoordinator(persistence, persistence, queue, gate, stateMachine, inventory,
    ///     equipment, equipHandlers.ClearSession, leveling.UnregisterPlayerEntity, zone.RemoveEntity, zone.BroadcastPlayerLeftZone);
    /// ends.BeginCleanLogout(accountId, result =&gt; { if (result != CharacterSaveResult.Success) NotifyClientToRetry(); });
    /// // each tick, for each Disconnected_SessionActive account:
    /// ends.EvaluateSessionTTL(accountId, tickLoop.ServerTickNumber, sessionExpiryTick);
    /// </code>
    /// </example>
    public sealed class SessionEndCoordinator
    {
        private const string LOG_PREFIX = "[SessionEndCoordinator]";

        private static readonly Action<uint> NoOpPersist = _ => { };

        private readonly ICharacterPersistence _persistence;
        private readonly ICharacterSessionRelease _sessionRelease;
        private readonly ITickCompletionQueue _queue;
        private readonly ConnectionStateMachine _stateMachine;
        private readonly IInventoryService _inventory;
        private readonly IEquipmentService _equipment;
        private readonly Action<CharacterID> _clearEquipRequestStore;
        private readonly Action<EntityID> _unregisterPlayerEntity;
        private readonly Action<uint> _removeEntityFromZone;
        private readonly Action<uint, DisconnectType> _broadcastPlayerLeftZone;

        private readonly ICharacterMutationGate _gate;

        // Accounts with a session end in flight (EC-CP-4), started or waiting on the gate. Tick thread only.
        private readonly HashSet<uint> _inFlight = new HashSet<uint>();

        // Session ends requested while the character's gate was held, keyed by character (one account <-> one character).
        private readonly Dictionary<CharacterID, PendingEnd> _pending = new Dictionary<CharacterID, PendingEnd>();

        // Gateway-forced ends (Authentication Story 014): accounts whose in-flight end is an eviction or a re-auth exhaustion,
        // and the onEnded callbacks waiting for the account's session end (EC-CP-4). Tick thread only.
        private readonly HashSet<uint> _forced = new HashSet<uint>();
        private readonly Dictionary<uint, List<Action>> _waiters = new Dictionary<uint, List<Action>>();
        private readonly Action<uint> _closeTransportIfOpen;

        // Ghost-death end seams (Story 010); all optional.
        private readonly IPreDisconnectHpSource _preDisconnectHp;
        private readonly Func<EntityID, float> _effectiveMaxHp;
        private readonly Action<uint, string> _broadcastGhostExpired;
        private readonly Action<EntityID> _deTargetMobsOf;
        private readonly Action<uint, DisconnectReason> _sendZoneSessionEnded;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        /// <summary>Test/dev-build observer passed to the state machine's eviction and exhaustion completions.</summary>
        internal INetworkTestObserver TestObserver { get; set; }
#endif

        private sealed class PendingEnd
        {
            public uint AccountId;
            public bool IsForced;
            public bool IsExhaustion;
            public SessionEndReason Reason;
            public bool IsLogout;
            public bool IsGhostDeath;
            public EntityID GhostEntity;
            public uint SessionExpiryTick;
            public Action<CharacterSaveResult> OnResult;
        }

        /// <summary>Creates the coordinator and subscribes once to <see cref="ICharacterMutationGate.OnGateOpened"/>.</summary>
        /// <param name="persistence">The persistence service (the session-end save).</param>
        /// <param name="sessionRelease">The persistence session-table release (normally the same service).</param>
        /// <param name="queue">The tick completion queue that hands each save result back on the tick thread.</param>
        /// <param name="gate">The per-character mutation gate; a session-end save waits while the character's gate is held.</param>
        /// <param name="stateMachine">The connection state machine that owns the account sessions.</param>
        /// <param name="inventory">The Inventory service (<c>UnregisterCharacter</c>).</param>
        /// <param name="equipment">The Equipment System (<c>UnregisterCharacter</c>, Equipment Story 008).</param>
        /// <param name="clearEquipRequestStore">
        /// Forgets the character's stored <c>EquipRequest</c> result (<c>EquipmentClientRequestHandlers.ClearSession</c>),
        /// so the next session's <c>requestId</c> counter is not taken for a retransmit.
        /// </param>
        /// <param name="unregisterPlayerEntity">Leveling's <c>UnregisterPlayerEntity</c> (not on <c>ILevelingService</c>, so a seam).</param>
        /// <param name="removeEntityFromZone">Passed to the state machine; removes the character's entity from its zone.</param>
        /// <param name="broadcastPlayerLeftZone">Passed to the state machine; broadcasts <c>PlayerLeftZone</c>.</param>
        /// <param name="closeTransportIfOpen">
        /// Optional (Story 014): closes the account's transport connection when a gateway eviction or re-auth exhaustion ends
        /// its session; called with the account id. Null: nothing is closed.
        /// </param>
        /// <param name="preDisconnectHp">
        /// Optional (Death and Respawn Story 010): the pre-disconnect HP snapshot written by a ghost-death save.
        /// Null: none.
        /// </param>
        /// <param name="effectiveMaxHp">
        /// Optional: the entity's effective MaxHP, written when there is no snapshot. Null and no snapshot: the save
        /// uses the no-override overload (the stored session state decides the HP).
        /// </param>
        /// <param name="broadcastGhostExpired">
        /// Optional: called with (raw character id, "GhostDeath") after a ghost-death end.
        /// </param>
        /// <param name="deTargetMobsOf">
        /// Optional: called with the dead ghost's entity so mobs drop it (CR-GH-7).
        /// </param>
        /// <param name="sendZoneSessionEnded">
        /// Optional: sends <c>ZoneSessionEnded(GhostDeath)</c> when the account is Reconnecting at completion.
        /// Null: nothing.
        /// </param>
        /// <exception cref="ArgumentNullException">Any required argument is null.</exception>
        public SessionEndCoordinator(
            ICharacterPersistence persistence,
            ICharacterSessionRelease sessionRelease,
            ITickCompletionQueue queue,
            ICharacterMutationGate gate,
            ConnectionStateMachine stateMachine,
            IInventoryService inventory,
            IEquipmentService equipment,
            Action<CharacterID> clearEquipRequestStore,
            Action<EntityID> unregisterPlayerEntity,
            Action<uint> removeEntityFromZone,
            Action<uint, DisconnectType> broadcastPlayerLeftZone,
            Action<uint> closeTransportIfOpen = null,
            IPreDisconnectHpSource preDisconnectHp = null,
            Func<EntityID, float> effectiveMaxHp = null,
            Action<uint, string> broadcastGhostExpired = null,
            Action<EntityID> deTargetMobsOf = null,
            Action<uint, DisconnectReason> sendZoneSessionEnded = null)
        {
            _preDisconnectHp = preDisconnectHp;
            _effectiveMaxHp = effectiveMaxHp;
            _broadcastGhostExpired = broadcastGhostExpired;
            _deTargetMobsOf = deTargetMobsOf;
            _sendZoneSessionEnded = sendZoneSessionEnded;
            _closeTransportIfOpen = closeTransportIfOpen ?? (_ => { });
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _sessionRelease = sessionRelease ?? throw new ArgumentNullException(nameof(sessionRelease));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _clearEquipRequestStore = clearEquipRequestStore ?? throw new ArgumentNullException(nameof(clearEquipRequestStore));
            _unregisterPlayerEntity = unregisterPlayerEntity ?? throw new ArgumentNullException(nameof(unregisterPlayerEntity));
            _removeEntityFromZone = removeEntityFromZone ?? throw new ArgumentNullException(nameof(removeEntityFromZone));
            _broadcastPlayerLeftZone = broadcastPlayerLeftZone ?? throw new ArgumentNullException(nameof(broadcastPlayerLeftZone));
            _gate.OnGateOpened += OnGateOpened;
        }

        /// <summary>Number of session ends started and not yet handled by their completion callback, or waiting on the mutation gate.</summary>
        public int InFlightCount => _inFlight.Count;

        /// <summary>
        /// Whether a session end (logout, TTL expiry, eviction or re-auth exhaustion) is started or waiting on the mutation gate
        /// for <paramref name="accountId"/>. The gateway <c>Reconnect</c> handler rejects while this is true, so it never moves a
        /// session that an end in flight is about to release (Authentication Story 014).
        /// </summary>
        /// <param name="accountId">The account to check.</param>
        /// <returns><see langword="true"/> while an end for the account has not yet been handled by its completion callback.</returns>
        public bool IsEndInFlight(uint accountId) => _inFlight.Contains(accountId);

        /// <summary>
        /// Starts a clean logout save for a <see cref="SessionState.Connected"/> account. Nothing is released before the save is handled.
        /// </summary>
        /// <param name="accountId">The logging-out account.</param>
        /// <param name="onResult">
        /// Called once from <c>Drain</c> with <see cref="CharacterSaveResult.Success"/> after the release, or with the failure
        /// (<see cref="CharacterSaveResult.DatabaseError"/> for a fault, cancellation or watchdog timeout) with the session still
        /// <see cref="SessionState.Connected"/>. A release step that throws is logged and still gives Success, since the save
        /// committed. A logout that waited on the mutation gate and finds the session no longer Connected when it opens is dropped:
        /// <see cref="CharacterSaveResult.DatabaseError"/>, called from the gate's <c>Open</c>, with no release. Not called when this
        /// method returns <see langword="false"/>. An exception it throws is caught and logged by the tick completion queue (or by the
        /// gate, for a dropped logout); the session end is already finished by then.
        /// </param>
        /// <returns><see langword="true"/> if a save was started, or is waiting for the character's mutation gate to open; <see langword="false"/> if a session end is already in flight for the account (nothing started, <paramref name="onResult"/> never called).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="onResult"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The account is not currently <see cref="SessionState.Connected"/>.</exception>
        /// <example>
        /// <code>bool started = ends.BeginCleanLogout(accountId: 7, result =&gt; Reply(result));</code>
        /// </example>
        public bool BeginCleanLogout(uint accountId, Action<CharacterSaveResult> onResult)
        {
            if (onResult == null)
            {
                throw new ArgumentNullException(nameof(onResult));
            }

            if (_inFlight.Contains(accountId))
            {
                return false;
            }

            if (!_stateMachine.TryGetSessionState(accountId, out SessionState state) || state != SessionState.Connected)
            {
                throw new InvalidOperationException(
                    $"{LOG_PREFIX} BeginCleanLogout: accountId={accountId} is not currently registered as {SessionState.Connected}.");
            }

            CharacterID charId = CharacterOf(accountId);
            if (_gate.IsHeld(charId))
            {
                // An irreversible write is in flight: wait for the gate (Story 017).
                _pending[charId] = new PendingEnd { AccountId = accountId, IsLogout = true, OnResult = onResult };
                _inFlight.Add(accountId);
                return true;
            }

            StartLogoutSave(accountId, charId, onResult);
            return true;
        }

        private void StartLogoutSave(uint accountId, CharacterID charId, Action<CharacterSaveResult> onResult)
        {
            var cts = new CancellationTokenSource();
            try
            {
                _queue.Track(
                    _persistence.SaveSession(charId, SessionEndReason.CleanLogout, cts.Token),
                    cts,
                    result => OnLogoutSaved(accountId, charId, cts, onResult, result));
            }
            catch
            {
                cts.Dispose(); // the save did not start or was not tracked; nothing is in flight
                throw;
            }

            _inFlight.Add(accountId);
        }

        /// <summary>
        /// Per-tick check of one account's session TTL. Starts the TTL save on the first call at or after
        /// <paramref name="sessionExpiryTick"/> for an account that is <see cref="SessionState.Disconnected_SessionActive"/> on that call;
        /// otherwise does nothing. The expiry tick is supplied by the caller on every call (the state machine never stores it).
        /// </summary>
        /// <param name="accountId">The account to check.</param>
        /// <param name="currentTick">The current server tick.</param>
        /// <param name="sessionExpiryTick">The absolute tick at which the session TTL expires.</param>
        /// <returns><see langword="true"/> only on the call that started the save.</returns>
        /// <example>
        /// <code>ends.EvaluateSessionTTL(accountId: 7, currentTick: 6100u, sessionExpiryTick: 6100u);</code>
        /// </example>
        public bool EvaluateSessionTTL(uint accountId, uint currentTick, uint sessionExpiryTick)
        {
            if (_inFlight.Contains(accountId)
                || !_stateMachine.TryGetSessionState(accountId, out SessionState state)
                || state != SessionState.Disconnected_SessionActive
                || !StaleDiscardComparer.IsTickExpired(currentTick, sessionExpiryTick))
            {
                return false;
            }

            CharacterID charId = CharacterOf(accountId);
            if (_gate.IsHeld(charId))
            {
                // An irreversible write is in flight: wait for the gate (Story 017).
                _pending[charId] = new PendingEnd { AccountId = accountId, IsLogout = false, SessionExpiryTick = sessionExpiryTick };
                _inFlight.Add(accountId);
                return true;
            }

            StartTtlSave(accountId, charId, sessionExpiryTick);
            return true;
        }

        private void StartTtlSave(uint accountId, CharacterID charId, uint sessionExpiryTick)
        {
            var cts = new CancellationTokenSource();
            try
            {
                _queue.Track(
                    _persistence.SaveSession(charId, SessionEndReason.SessionTTLExpiry, cts.Token),
                    cts,
                    result => OnTtlSaved(accountId, charId, sessionExpiryTick, cts, result));
            }
            catch
            {
                cts.Dispose(); // the save did not start or was not tracked; nothing is in flight
                throw;
            }

            _inFlight.Add(accountId);
        }

        // Runs inside the gate's Open. IrreversibleOutcomeCoordinator.OnCompleted runs deliver-or-failure-protocol (revert,
        // disconnect, preserve), then token disposal, then gate.Open, so at this point a delivered write left the account
        // Connected and a failed one left it Disconnected_SessionActive (Story 017 decision 1 relies on this order).
        // Runs inside a Drain callback, so a failure to start the save is logged here rather than rethrown.
        private void OnGateOpened(CharacterID charId)
        {
            if (!_pending.TryGetValue(charId, out PendingEnd end))
            {
                return;
            }

            if (_gate.IsHeld(charId))
            {
                return; // an earlier subscriber closed the gate again; keep waiting for the next open
            }

            _pending.Remove(charId);
            bool stateFound = _stateMachine.TryGetSessionState(end.AccountId, out SessionState state);
            if (end.IsGhostDeath)
            {
                ResumeGhostDeath(end, charId, stateFound, state);
            }
            else if (end.IsForced)
            {
                ResumeForced(end, charId, stateFound, state);
            }
            else if (end.IsLogout)
            {
                ResumeLogout(end, charId, stateFound, state);
            }
            else
            {
                ResumeTtl(end, charId, stateFound, state);
            }
        }

        // The in-flight mark is cleared before onResult runs, so a throwing onResult (logged by the gate) leaves no stale state.
        private void ResumeLogout(PendingEnd end, CharacterID charId, bool stateFound, SessionState state)
        {
            if (!stateFound || state != SessionState.Connected)
            {
                _inFlight.Remove(end.AccountId);
                Debug.LogWarning($"{LOG_PREFIX} Clean logout for {charId} was dropped: the session is no longer {SessionState.Connected} when the mutation gate opened (the irreversible write it waited on failed, or the session ended); a preserved session is saved by its TTL expiry.");
                AfterNonForcedEnd(end.AccountId);
                end.OnResult(CharacterSaveResult.DatabaseError);
                return;
            }

            try
            {
                StartLogoutSave(end.AccountId, charId, end.OnResult);
            }
            catch (Exception ex)
            {
                _inFlight.Remove(end.AccountId);
                Debug.LogError($"{LOG_PREFIX} Starting the clean logout save for {charId} after the mutation gate opened threw: {ex}");
                AfterNonForcedEnd(end.AccountId);
                end.OnResult(CharacterSaveResult.DatabaseError);
            }
        }

        // A reconnect during the wait (state Connected) is a legitimate race and logs a warning; any other state is an error.
        private void ResumeTtl(PendingEnd end, CharacterID charId, bool stateFound, SessionState state)
        {
            if (!stateFound || state != SessionState.Disconnected_SessionActive)
            {
                _inFlight.Remove(end.AccountId);
                string message = $"{LOG_PREFIX} Session TTL end for account {end.AccountId} / {charId} was waiting on the mutation gate but the session is no longer {SessionState.Disconnected_SessionActive}; nothing was started.";
                if (stateFound && state == SessionState.Connected)
                {
                    Debug.LogWarning(message);
                }
                else
                {
                    Debug.LogError(message);
                }

                AfterNonForcedEnd(end.AccountId);
                return;
            }

            try
            {
                StartTtlSave(end.AccountId, charId, end.SessionExpiryTick);
            }
            catch (Exception ex)
            {
                _inFlight.Remove(end.AccountId);
                Debug.LogError($"{LOG_PREFIX} Starting the TTL expiry save for {charId} after the mutation gate opened threw: {ex}");
                AfterNonForcedEnd(end.AccountId);
            }
        }

        // The account's state was checked just before, so it is registered and has a character.
        private CharacterID CharacterOf(uint accountId)
        {
            bool found = _stateMachine.TryGetCharacterId(accountId, out uint rawCharacterId);
            Debug.Assert(found, $"{LOG_PREFIX} accountId={accountId} has a session state but no character id.");
            return new CharacterID(rawCharacterId);
        }

        // A timed-out save is still running and still holds the token (the queue has cancelled it), so its source is left
        // to the garbage collector rather than disposed under it.
        private static void DisposeUnlessTimedOut(CancellationTokenSource cts, TickTaskStatus status)
        {
            if (status != TickTaskStatus.TimedOut)
            {
                cts.Dispose();
            }
        }

        private void OnLogoutSaved(
            uint accountId,
            CharacterID charId,
            CancellationTokenSource cts,
            Action<CharacterSaveResult> onResult,
            TickTaskResult<CharacterSaveResult> result)
        {
            _inFlight.Remove(accountId);
            DisposeUnlessTimedOut(cts, result.Status);

            CharacterSaveResult outcome = result.Status == TickTaskStatus.Completed ? result.Value : CharacterSaveResult.DatabaseError;
            if (outcome != CharacterSaveResult.Success)
            {
                Debug.LogWarning($"{LOG_PREFIX} Clean logout save for {charId} did not succeed ({result.Status}, {outcome}); the session stays Connected for a retry.");
                AfterNonForcedEnd(accountId); // a joined eviction starts its own save now (EC-CP-4)
                onResult(outcome);
                return;
            }

            if (HasState(accountId, SessionState.Connected, "clean logout", charId))
            {
                Release(charId, "clean logout", () => _stateMachine.ProcessExplicitDisconnect(accountId, NoOpPersist, _removeEntityFromZone, _broadcastPlayerLeftZone));
            }

            AfterNonForcedEnd(accountId);
            onResult(CharacterSaveResult.Success);
        }

        private void OnTtlSaved(
            uint accountId,
            CharacterID charId,
            uint sessionExpiryTick,
            CancellationTokenSource cts,
            TickTaskResult<CharacterSaveResult> result)
        {
            _inFlight.Remove(accountId);
            DisposeUnlessTimedOut(cts, result.Status);

            // Step 10: released whatever the result; the save already logged its critical error.
            if (HasState(accountId, SessionState.Disconnected_SessionActive, "TTL expiry", charId))
            {
                Release(charId, "TTL expiry", () => _stateMachine.CompleteSessionActiveTTLExpiry(
                    accountId, sessionExpiryTick, sessionExpiryTick, NoOpPersist, _removeEntityFromZone, _broadcastPlayerLeftZone));
            }

            AfterNonForcedEnd(accountId);
        }

        // ----- Ghost-death end (Death and Respawn Story 010) -----------------------------------------------

        /// <summary>
        /// Ends the session of a disconnected ghost that died (Death and Respawn Story 010, CR-GH-6, CR-GH-7):
        /// saves with <see cref="SessionEndReason.GhostDeath"/> and the pre-disconnect HP (or the effective MaxHP),
        /// waiting on the
        /// mutation gate if held, then releases whatever the save result and completes the state machine. Accepts
        /// <see cref="SessionState.Disconnected_SessionActive"/> and <see cref="SessionState.Reconnecting"/>.
        /// </summary>
        /// <param name="accountId">The ghost's account.</param>
        /// <param name="entity">The ghost's EntityID.</param>
        /// <returns>
        /// <see langword="true"/> if the save was started or is waiting on the gate; <see langword="false"/>
        /// (one warning, nothing started) if an end is already in flight or the account is in another state or unknown.
        /// </returns>
        /// <example><code>ends.BeginGhostDeath(accountId: 7, entity);</code></example>
        public bool BeginGhostDeath(uint accountId, EntityID entity)
        {
            if (_inFlight.Contains(accountId))
            {
                Debug.LogWarning(
                    $"{LOG_PREFIX} Ghost death for account {accountId} ignored: a session end is already in flight.");
                return false;
            }

            if (!IsGhostDeathState(accountId, out bool found, out SessionState state))
            {
                Debug.LogWarning(
                    $"{LOG_PREFIX} Ghost death for account {accountId} ignored: the session is not "
                    + "Disconnected_SessionActive or Reconnecting "
                    + $"(state: {(found ? state.ToString() : "no session")}).");
                return false;
            }

            CharacterID charId = CharacterOf(accountId);
            try
            {
                if (_gate.IsHeld(charId))
                {
                    _pending[charId] = new PendingEnd
                    {
                        AccountId = accountId,
                        IsGhostDeath = true,
                        GhostEntity = entity,
                    };
                    _inFlight.Add(accountId);
                    return true;
                }

                StartGhostDeathSave(accountId, charId, entity);
            }
            catch (Exception ex)
            {
                _inFlight.Remove(accountId);
                Debug.LogError($"{LOG_PREFIX} Starting the ghost-death save for {charId} threw: {ex}");
                return false;
            }

            return true;
        }

        // found is false when the account has no session (state is then meaningless).
        private bool IsGhostDeathState(uint accountId, out bool found, out SessionState state)
        {
            found = _stateMachine.TryGetSessionState(accountId, out state);
            if (!found)
            {
                return false;
            }

            return state == SessionState.Disconnected_SessionActive || state == SessionState.Reconnecting;
        }

        private void StartGhostDeathSave(uint accountId, CharacterID charId, EntityID entity)
        {
            var cts = new CancellationTokenSource();
            try
            {
                // The HP is computed here, at save start (AC-2), also when a gate wait delayed the start.
                Task<CharacterSaveResult> save;
                if (_preDisconnectHp != null && _preDisconnectHp.TryGetHp(entity, out float snapshotHp))
                {
                    save = _persistence.SaveSession(charId, SessionEndReason.GhostDeath, snapshotHp, cts.Token);
                }
                else if (_effectiveMaxHp != null)
                {
                    save = _persistence.SaveSession(
                        charId, SessionEndReason.GhostDeath, _effectiveMaxHp(entity), cts.Token);
                }
                else
                {
                    save = _persistence.SaveSession(charId, SessionEndReason.GhostDeath, cts.Token);
                }

                _queue.Track(save, cts, result => OnGhostDeathSaved(accountId, charId, entity, cts, result));
            }
            catch
            {
                cts.Dispose();
                throw;
            }

            _inFlight.Add(accountId);
        }

        private void ResumeGhostDeath(PendingEnd end, CharacterID charId, bool stateFound, SessionState state)
        {
            if (!stateFound || (state != SessionState.Disconnected_SessionActive && state != SessionState.Reconnecting))
            {
                _inFlight.Remove(end.AccountId);
                string prefix = $"{LOG_PREFIX} Ghost-death end for account {end.AccountId} / {charId} was waiting on "
                    + "the mutation gate but ";
                if (stateFound && state == SessionState.Connected)
                {
                    // A reconnect during the wait is a legitimate race (like ResumeTtl).
                    Debug.LogWarning(prefix + "the player reconnected (Connected); nothing was started.");
                }
                else
                {
                    Debug.LogError(prefix + "the session is no longer Disconnected_SessionActive or Reconnecting; "
                        + "nothing was started.");
                }

                AfterNonForcedEnd(end.AccountId);
                return;
            }

            try
            {
                StartGhostDeathSave(end.AccountId, charId, end.GhostEntity);
            }
            catch (Exception ex)
            {
                _inFlight.Remove(end.AccountId);
                Debug.LogError(
                    $"{LOG_PREFIX} Starting the ghost-death save for {charId} "
                    + $"after the mutation gate opened threw: {ex}");
                AfterNonForcedEnd(end.AccountId);
            }
        }

        private void OnGhostDeathSaved(
            uint accountId,
            CharacterID charId,
            EntityID entity,
            CancellationTokenSource cts,
            TickTaskResult<CharacterSaveResult> result)
        {
            _inFlight.Remove(accountId);
            DisposeUnlessTimedOut(cts, result.Status);

            if (result.Status != TickTaskStatus.Completed || result.Value != CharacterSaveResult.Success)
            {
                // Decision 4: like the TTL end, release and transition anyway.
                Debug.LogError(
                    $"{LOG_PREFIX} Ghost-death save for {charId} did not succeed ({result.Status}); "
                    + "the session is released anyway.");
            }

            uint rawChar = charId.RawValue;
            bool found = _stateMachine.TryGetSessionState(accountId, out SessionState state);
            if (found && state == SessionState.Disconnected_SessionActive)
            {
                Release(charId, "ghost death", () =>
                {
                    _stateMachine.CompleteGhostDeathFromDisconnected(accountId, NoOpPersist
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                        , TestObserver
#endif
                        );
                    _removeEntityFromZone(rawChar);
                    // Closest existing DisconnectType (the enum has Graceful, Timeout, ZoneTransfer).
                    _broadcastPlayerLeftZone(rawChar, DisconnectType.Timeout);
                });
                FinishGhostDeath(accountId, rawChar, entity);
            }
            else if (found && state == SessionState.Reconnecting)
            {
                Release(charId, "ghost death", () =>
                {
                    _stateMachine.HandleGhostDeathWhileReconnecting(
                        accountId,
                        NoOpPersist,
                        _sendZoneSessionEnded ?? ((_, __) => { })
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                        , TestObserver
#endif
                        );
                    _removeEntityFromZone(rawChar);
                    // Closest existing DisconnectType (the enum has Graceful, Timeout, ZoneTransfer).
                    _broadcastPlayerLeftZone(rawChar, DisconnectType.Timeout);
                });
                FinishGhostDeath(accountId, rawChar, entity);
            }
            else if (found && state == SessionState.Connected)
            {
                Debug.LogWarning(
                    $"{LOG_PREFIX} Ghost-death end for account {accountId} / {charId} finished but the player "
                    + "reconnected (Connected) during the save; nothing was released.");
            }
            else
            {
                Debug.LogError(
                    $"{LOG_PREFIX} Ghost-death end for account {accountId} / {charId} finished but the session is "
                    + "not Disconnected_SessionActive or Reconnecting "
                    + $"(state: {(found ? state.ToString() : "no session")}); no release step was run.");
            }

            AfterNonForcedEnd(accountId);
        }

        private void FinishGhostDeath(uint accountId, uint rawChar, EntityID entity)
        {
            _stateMachine.MarkGhostDeath(accountId);

            // Each optional hook is isolated: a throwing hook must not skip the other hook or AfterNonForcedEnd.
            try
            {
                _broadcastGhostExpired?.Invoke(rawChar, "GhostDeath");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} The ghost-expired broadcast for account {accountId} threw: {ex}");
            }

            try
            {
                _deTargetMobsOf?.Invoke(entity);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} De-targeting mobs of the dead ghost of account {accountId} threw: {ex}");
            }
        }

        // ----- Gateway-forced ends (Authentication Story 014) ---------------------------------------------

        /// <summary>
        /// Ends the account's session on a gateway <c>Evict</c> (Authentication Story 014, ADR-016 Decision 3): saves with
        /// <see cref="SessionEndReason.Evicted"/> exactly like a logout or TTL end (started and tracked in the same statement,
        /// waiting on the mutation gate if held), then releases whatever the save result and completes the state machine
        /// through <see cref="ConnectionStateMachine.CompleteEviction"/>. Accepts <see cref="SessionState.Connected"/>,
        /// <see cref="SessionState.Disconnected_SessionActive"/> and <see cref="SessionState.Reconnecting"/>.
        /// </summary>
        /// <remarks>
        /// Joins an end already in flight (EC-CP-4): no second save starts and every waiting <paramref name="onEnded"/> fires
        /// when that end completes. If the in-flight end was a clean logout whose save failed (or whose wait on the gate was
        /// dropped) and the session still exists, the eviction save starts then. A failed save is logged critically by the
        /// save itself; this class adds no second log.
        /// </remarks>
        /// <param name="accountId">The evicted account.</param>
        /// <param name="onEnded">Called once on the tick when the account's session end finished, whatever the save result, or at once when the account has no session in an accepted state.</param>
        /// <exception cref="ArgumentNullException"><paramref name="onEnded"/> is null.</exception>
        /// <example><code>ends.BeginEviction(accountId: 7, () =&gt; outbox.EnqueueEvictResult(requestId));</code></example>
        public void BeginEviction(uint accountId, Action onEnded)
        {
            if (onEnded == null)
            {
                throw new ArgumentNullException(nameof(onEnded));
            }

            if (!_waiters.TryGetValue(accountId, out List<Action> list))
            {
                list = new List<Action>(1);
                _waiters[accountId] = list;
            }

            list.Add(onEnded);
            if (_inFlight.Contains(accountId))
            {
                return; // joined: the end in flight drains the waiters
            }

            if (!IsEvictable(accountId))
            {
                FireWaiters(accountId);
                return;
            }

            StartForcedEndOrFire(accountId, SessionEndReason.Evicted, false);
        }

        /// <summary>
        /// Ends a <see cref="SessionState.Reconnecting"/> session whose re-authentication failures reached
        /// <c>REAUTH_FAILURE_LIMIT</c> (Authentication Story 014): async save (<see cref="SessionEndReason.SessionTTLExpiry"/>),
        /// release whatever the result, then <see cref="ConnectionStateMachine.CompleteReauthExhaustion"/>. Never uses a
        /// synchronous persist delegate.
        /// </summary>
        /// <param name="accountId">The account whose attempts are exhausted.</param>
        /// <returns><see langword="true"/> if the end was started or is waiting on the gate; <see langword="false"/> if an end is already in flight or the session is not <see cref="SessionState.Reconnecting"/>.</returns>
        /// <example><code>ends.BeginReauthExhaustion(accountId: 7);</code></example>
        public bool BeginReauthExhaustion(uint accountId)
        {
            if (_inFlight.Contains(accountId)
                || !_stateMachine.TryGetSessionState(accountId, out SessionState state)
                || state != SessionState.Reconnecting)
            {
                return false;
            }

            StartForcedEndOrFire(accountId, SessionEndReason.SessionTTLExpiry, true);
            return true;
        }

        private bool IsEvictable(uint accountId)
        {
            return _stateMachine.TryGetSessionState(accountId, out SessionState state)
                && (state == SessionState.Connected || state == SessionState.Disconnected_SessionActive || state == SessionState.Reconnecting);
        }

        // Starts (or queues behind the gate) a forced end; a start that throws is logged and the waiters are released.
        private void StartForcedEndOrFire(uint accountId, SessionEndReason reason, bool exhaustion)
        {
            CharacterID charId = CharacterOf(accountId);
            try
            {
                if (_gate.IsHeld(charId))
                {
                    _pending[charId] = new PendingEnd { AccountId = accountId, IsForced = true, IsExhaustion = exhaustion, Reason = reason };
                    _inFlight.Add(accountId);
                    _forced.Add(accountId);
                    return;
                }

                StartForcedSave(accountId, charId, reason, exhaustion);
            }
            catch (Exception ex)
            {
                _inFlight.Remove(accountId);
                _forced.Remove(accountId);
                Debug.LogError($"{LOG_PREFIX} Starting the forced session-end save for {charId} threw: {ex}");
                FireWaiters(accountId);
            }
        }

        private void StartForcedSave(uint accountId, CharacterID charId, SessionEndReason reason, bool exhaustion)
        {
            var cts = new CancellationTokenSource();
            try
            {
                _queue.Track(
                    _persistence.SaveSession(charId, reason, cts.Token),
                    cts,
                    result => OnForcedSaved(accountId, charId, exhaustion, cts, result));
            }
            catch
            {
                cts.Dispose();
                throw;
            }

            _inFlight.Add(accountId);
            _forced.Add(accountId);
        }

        private void ResumeForced(PendingEnd end, CharacterID charId, bool stateFound, SessionState state)
        {
            bool ok = stateFound && (end.IsExhaustion
                ? state == SessionState.Reconnecting
                : IsEvictable(end.AccountId));
            if (!ok)
            {
                _inFlight.Remove(end.AccountId);
                _forced.Remove(end.AccountId);
                Debug.LogError($"{LOG_PREFIX} Forced session end for account {end.AccountId} / {charId} was waiting on the mutation gate but the session is no longer in an accepted state; nothing was started.");
                FireWaiters(end.AccountId);
                return;
            }

            try
            {
                StartForcedSave(end.AccountId, charId, end.Reason, end.IsExhaustion);
            }
            catch (Exception ex)
            {
                _inFlight.Remove(end.AccountId);
                _forced.Remove(end.AccountId);
                Debug.LogError($"{LOG_PREFIX} Starting the forced session-end save for {charId} after the mutation gate opened threw: {ex}");
                FireWaiters(end.AccountId);
            }
        }

        private void OnForcedSaved(uint accountId, CharacterID charId, bool exhaustion, CancellationTokenSource cts,
            TickTaskResult<CharacterSaveResult> result)
        {
            _inFlight.Remove(accountId);
            _forced.Remove(accountId);
            DisposeUnlessTimedOut(cts, result.Status);

            // Released whatever the result; the save already logged its critical error (no second log here).
            bool accepted = exhaustion
                ? HasState(accountId, SessionState.Reconnecting, "re-auth exhaustion", charId)
                : (IsEvictable(accountId) || LogNotEvictable(accountId, charId));
            if (accepted)
            {
                Release(charId, exhaustion ? "re-auth exhaustion" : "eviction", () =>
                {
                    if (exhaustion)
                    {
                        _stateMachine.CompleteReauthExhaustion(accountId, _closeTransportIfOpen, _removeEntityFromZone, _broadcastPlayerLeftZone
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                            , TestObserver
#endif
                            );
                    }
                    else
                    {
                        _stateMachine.CompleteEviction(accountId, _closeTransportIfOpen, _removeEntityFromZone, _broadcastPlayerLeftZone
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                            , TestObserver
#endif
                            );
                    }
                });
            }

            FireWaiters(accountId);
        }

        private bool LogNotEvictable(uint accountId, CharacterID charId)
        {
            Debug.LogError($"{LOG_PREFIX} Session end (eviction) for account {accountId} / {charId} finished but the session is no longer Connected, Disconnected_SessionActive or Reconnecting; no release step was run.");
            return false;
        }

        // A logout or TTL end finished. A joined eviction starts its own save if the session still exists, else is answered.
        private void AfterNonForcedEnd(uint accountId)
        {
            if (!_waiters.ContainsKey(accountId) || _inFlight.Contains(accountId))
            {
                return;
            }

            if (IsEvictable(accountId))
            {
                StartForcedEndOrFire(accountId, SessionEndReason.Evicted, false);
            }
            else
            {
                FireWaiters(accountId);
            }
        }

        private void FireWaiters(uint accountId)
        {
            if (!_waiters.TryGetValue(accountId, out List<Action> list))
            {
                return;
            }

            _waiters.Remove(accountId);
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    list[i]();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} An eviction callback for account {accountId} threw: {ex}");
                }
            }
        }

        private bool HasState(uint accountId, SessionState expected, string what, CharacterID charId)
        {
            if (_stateMachine.TryGetSessionState(accountId, out SessionState actual) && actual == expected)
            {
                return true;
            }

            Debug.LogError($"{LOG_PREFIX} Session end ({what}) for account {accountId} / {charId} finished but the session is no longer {expected}; no release step was run.");
            return false;
        }

        // A step that throws stops the release there and is logged once; the steps after it do not run. The callback still
        // finishes (a logout still reports its result), so no caller is left waiting.
        private void Release(CharacterID charId, string what, Action releaseInStateMachine)
        {
            try
            {
                bool hadEntry = _sessionRelease.ReleaseSession(charId, out EntityID entityId);
                _inventory.UnregisterCharacter(charId);
                _clearEquipRequestStore(charId); // in-memory only; first, so a throwing unregister cannot leave a stale entry
                _equipment.UnregisterCharacter(charId);
                if (hadEntry)
                {
                    _unregisterPlayerEntity(entityId);
                }

                releaseInStateMachine();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} Release after {what} for {charId} threw; the release is incomplete: {ex}");
            }
        }
    }
}
