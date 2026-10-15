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
    /// <para>Not thread-safe: tick thread only, like <see cref="ITickCompletionQueue"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var ends = new SessionEndCoordinator(persistence, persistence, queue, stateMachine, inventory,
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

        // Accounts with a session end in flight (EC-CP-4). Tick thread only.
        private readonly HashSet<uint> _inFlight = new HashSet<uint>();

        /// <summary>Creates the coordinator.</summary>
        /// <param name="persistence">The persistence service (the session-end save).</param>
        /// <param name="sessionRelease">The persistence session-table release (normally the same service).</param>
        /// <param name="queue">The tick completion queue that hands each save result back on the tick thread.</param>
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
            ConnectionStateMachine stateMachine,
            IInventoryService inventory,
            Action<EntityID> unregisterPlayerEntity,
            Action<uint> removeEntityFromZone,
            Action<uint, DisconnectType> broadcastPlayerLeftZone)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _sessionRelease = sessionRelease ?? throw new ArgumentNullException(nameof(sessionRelease));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _unregisterPlayerEntity = unregisterPlayerEntity ?? throw new ArgumentNullException(nameof(unregisterPlayerEntity));
            _removeEntityFromZone = removeEntityFromZone ?? throw new ArgumentNullException(nameof(removeEntityFromZone));
            _broadcastPlayerLeftZone = broadcastPlayerLeftZone ?? throw new ArgumentNullException(nameof(broadcastPlayerLeftZone));
        }

        /// <summary>Number of session ends started and not yet handled by their completion callback.</summary>
        public int InFlightCount => _inFlight.Count;

        /// <summary>
        /// Starts a clean logout save for a <see cref="SessionState.Connected"/> account. Nothing is released before the save is handled.
        /// </summary>
        /// <param name="accountId">The logging-out account.</param>
        /// <param name="onResult">
        /// Called once from <c>Drain</c> with <see cref="CharacterSaveResult.Success"/> after the release, or with the failure
        /// (<see cref="CharacterSaveResult.DatabaseError"/> for a fault, cancellation or watchdog timeout) with the session still
        /// <see cref="SessionState.Connected"/>. A release step that throws is logged and still gives Success, since the save
        /// committed. Not called when this method returns <see langword="false"/>. An exception it throws is caught and logged by
        /// the tick completion queue; the session end is already finished by then.
        /// </param>
        /// <returns><see langword="true"/> if a save was started; <see langword="false"/> if a session end is already in flight for the account (nothing started, <paramref name="onResult"/> never called).</returns>
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
            return true;
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
            return true;
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
