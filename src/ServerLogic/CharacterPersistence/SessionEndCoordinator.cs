using System;
using System.Collections.Generic;
using System.Threading;
using IronGrind.CharacterStats;
using IronGrind.Currency;
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
    /// (<see cref="ICharacterSessionRelease.ReleaseSession"/>), <see cref="IInventoryService.UnregisterCharacter"/>, the Leveling
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
    /// <para>Not thread-safe: tick thread only, like <see cref="ITickCompletionQueue"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var ends = new SessionEndCoordinator(persistence, persistence, queue, gate, stateMachine, inventory,
    ///     leveling.UnregisterPlayerEntity, zone.RemoveEntity, zone.BroadcastPlayerLeftZone);
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
        private readonly Action<EntityID> _unregisterPlayerEntity;
        private readonly Action<uint> _removeEntityFromZone;
        private readonly Action<uint, DisconnectType> _broadcastPlayerLeftZone;

        private readonly ICharacterMutationGate _gate;

        // Accounts with a session end in flight (EC-CP-4), started or waiting on the gate. Tick thread only.
        private readonly HashSet<uint> _inFlight = new HashSet<uint>();

        // Session ends requested while the character's gate was held, keyed by character (one account <-> one character).
        private readonly Dictionary<CharacterID, PendingEnd> _pending = new Dictionary<CharacterID, PendingEnd>();

        private sealed class PendingEnd
        {
            public uint AccountId;
            public bool IsLogout;
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
        /// <param name="unregisterPlayerEntity">Leveling's <c>UnregisterPlayerEntity</c> (not on <c>ILevelingService</c>, so a seam).</param>
        /// <param name="removeEntityFromZone">Passed to the state machine; removes the character's entity from its zone.</param>
        /// <param name="broadcastPlayerLeftZone">Passed to the state machine; broadcasts <c>PlayerLeftZone</c>.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public SessionEndCoordinator(
            ICharacterPersistence persistence,
            ICharacterSessionRelease sessionRelease,
            ITickCompletionQueue queue,
            ICharacterMutationGate gate,
            ConnectionStateMachine stateMachine,
            IInventoryService inventory,
            Action<EntityID> unregisterPlayerEntity,
            Action<uint> removeEntityFromZone,
            Action<uint, DisconnectType> broadcastPlayerLeftZone)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _sessionRelease = sessionRelease ?? throw new ArgumentNullException(nameof(sessionRelease));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _unregisterPlayerEntity = unregisterPlayerEntity ?? throw new ArgumentNullException(nameof(unregisterPlayerEntity));
            _removeEntityFromZone = removeEntityFromZone ?? throw new ArgumentNullException(nameof(removeEntityFromZone));
            _broadcastPlayerLeftZone = broadcastPlayerLeftZone ?? throw new ArgumentNullException(nameof(broadcastPlayerLeftZone));
            _gate.OnGateOpened += OnGateOpened;
        }

        /// <summary>Number of session ends started and not yet handled by their completion callback, or waiting on the mutation gate.</summary>
        public int InFlightCount => _inFlight.Count;

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
            if (end.IsLogout)
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
                onResult(outcome);
                return;
            }

            if (HasState(accountId, SessionState.Connected, "clean logout", charId))
            {
                Release(charId, "clean logout", () => _stateMachine.ProcessExplicitDisconnect(accountId, NoOpPersist, _removeEntityFromZone, _broadcastPlayerLeftZone));
            }

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
