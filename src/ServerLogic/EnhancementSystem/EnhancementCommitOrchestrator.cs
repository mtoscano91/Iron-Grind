using System;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Commit orchestration of an enhancement attempt (Enhancement Story 011; design/gdd/enhancement-system.md
    /// CR-ENH-11, CR-ENH-15 step 6b and Rollback, EC-ENH-1, EC-ENH-2, EC-ENH-6, AC-ENH-13, AC-ENH-23, AC-ENH-34,
    /// AC-ENH-35; character-persistence.md CR-CP-5, CR-CP-6, CR-CP-7; networking-core.md CR-NET-5; ADR-011 Decision 5).
    /// It stands between <see cref="EnhancementService.BeginAttempt"/> and
    /// <see cref="EnhancementService.CompleteAttempt"/> / <see cref="EnhancementService.RollBackAttempt"/>: the
    /// attempt is applied in memory, persisted with one <c>SaveIrreversibleOutcome(EnhancementResult)</c> write
    /// without awaiting, and completed (events, result) only after the write succeeds. On any other result the
    /// attempt is rolled back, the client disconnected and the session preserved (the coordinator's failure protocol).
    /// </summary>
    /// <remarks>
    /// <para>Decision D1: <c>BeginAttempt</c> runs as the coordinator's request validation, because it can still
    /// reject (including at the scroll-consumption step). A rejection gives
    /// <see cref="IrreversibleOutcomeBeginResult.RejectedInvalidRequest"/> with the rejection code, no acknowledgment,
    /// no write and no gate change.</para>
    /// <para>All per-attempt state lives in a private per-call object, never in the orchestrator's fields, so two
    /// characters' attempts cannot see each other's data.</para>
    /// <para>If the delivery callback throws after a successful write, the commit stands: no rollback, no disconnect. The
    /// exception is logged by the tick completion queue, the gate is reopened by the coordinator, and the client gets no
    /// result; the callback must therefore be exception-safe.</para>
    /// <para>Tick thread only. Nothing here awaits or blocks. The production starter (acknowledgment message, result
    /// messages, deduplication) is <see cref="EnhancementAttemptStarter"/> (Enhancement Story 015), built on this class.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var orchestrator = new EnhancementCommitOrchestrator(coordinator, service, persistence, disconnect, preserve);
    /// IrreversibleOutcomeBeginResult result = orchestrator.Begin(
    ///     clientId, charId, itemSlot, scrollSlot,
    ///     emitAcknowledgment: () =&gt; SendRequestReceived(clientId),
    ///     deliverResult: attempt =&gt; SendResult(clientId, attempt),
    ///     out EnhancementResultCode rejection);
    /// if (result == IrreversibleOutcomeBeginResult.RejectedInvalidRequest) SendRejection(clientId, rejection);
    /// </code>
    /// </example>
    public sealed class EnhancementCommitOrchestrator
    {
        private readonly IIrreversibleOutcomeCoordinator _coordinator;
        private readonly EnhancementService _service;
        private readonly ICharacterPersistence _persistence;
        private readonly Action<uint, DisconnectReason> _disconnectClient;
        private readonly Action<uint, int> _preserveSessionForTtl;

        /// <summary>Creates the orchestrator over a coordinator, the service, the persistence service and the two failure steps.</summary>
        /// <param name="coordinator">The irreversible-outcome coordinator.</param>
        /// <param name="service">The Enhancement service whose attempts are committed.</param>
        /// <param name="persistence">The persistence service the attempt write goes to.</param>
        /// <param name="disconnectClient">Failure step: disconnects the client.</param>
        /// <param name="preserveSessionForTtl">Failure step: keeps the session for the TTL.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public EnhancementCommitOrchestrator(
            IIrreversibleOutcomeCoordinator coordinator,
            EnhancementService service,
            ICharacterPersistence persistence,
            Action<uint, DisconnectReason> disconnectClient,
            Action<uint, int> preserveSessionForTtl)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _disconnectClient = disconnectClient ?? throw new ArgumentNullException(nameof(disconnectClient));
            _preserveSessionForTtl = preserveSessionForTtl ?? throw new ArgumentNullException(nameof(preserveSessionForTtl));
        }

        /// <summary>
        /// Runs the tick-N half of one enhancement attempt through the coordinator: begins the attempt (validation
        /// and application in memory), acknowledges, starts the single <c>SaveIrreversibleOutcome</c> write and
        /// tracks it. On the completing tick the attempt is completed and handed to <paramref name="deliverResult"/>
        /// (success), or rolled back with the client disconnected and the session preserved (any other result).
        /// </summary>
        /// <param name="clientId">The requesting connection.</param>
        /// <param name="charId">The requesting character.</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance.</param>
        /// <param name="scrollSlotIndex">Bag slot of the scroll.</param>
        /// <param name="emitAcknowledgment">Request acknowledgment; runs after the attempt is accepted and the gate closed, never for a rejection.</param>
        /// <param name="deliverResult">Receives the final result, once, after the write succeeded and <see cref="EnhancementService.CompleteAttempt"/> ran.
        /// If <see cref="EnhancementService.CompleteAttempt"/> throws, the exception is logged and the result is built from the pending
        /// attempt (its outcome and new level), so the callback still runs.
        /// If it throws, the commit stands: there is no rollback and no disconnect (the write succeeded). The exception goes to the
        /// tick completion queue's logger, the coordinator's finally reopens the gate, and the client receives no result, so the
        /// callback must be exception-safe.</param>
        /// <param name="rejectionCode">
        /// The reason the attempt was rejected when the result is <see cref="IrreversibleOutcomeBeginResult.RejectedInvalidRequest"/>;
        /// <see cref="EnhancementResultCode.Success"/> (meaning "not rejected by the attempt") for every other result.
        /// </param>
        /// <returns>
        /// <see cref="IrreversibleOutcomeBeginResult.Started"/>; <see cref="IrreversibleOutcomeBeginResult.RejectedInvalidRequest"/>
        /// (the attempt was rejected: nothing changed, no acknowledgment, no draw, gate untouched);
        /// <see cref="IrreversibleOutcomeBeginResult.RejectedWriteInFlight"/> (a write is already in flight for the character:
        /// nothing ran); or <see cref="IrreversibleOutcomeBeginResult.PersistenceFailed"/> (the write could not start: the
        /// attempt was rolled back and the client disconnected).
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="emitAcknowledgment"/> or <paramref name="deliverResult"/> is null.</exception>
        /// <exception cref="Exception">
        /// <list type="bullet">
        /// <item>An exception from <see cref="EnhancementService.BeginAttempt"/> itself leaves no pending attempt (the service
        /// unlocks the slot), so there is nothing to roll back here; the gate was never closed.</item>
        /// <item>An exception before the write started while the attempt is pending (for example the acknowledgment callback)
        /// is rolled back here, then rethrown; no write was started and the gate is open.</item>
        /// <item>An <see cref="InvalidOperationException"/> from calling off the tick thread means the write did start: the
        /// coordinator already ran its failure protocol (rollback, disconnect, preserve) and the gate is open; nothing is
        /// rolled back a second time.</item>
        /// </list>
        /// </exception>
        public IrreversibleOutcomeBeginResult Begin(
            uint clientId,
            CharacterID charId,
            int itemSlotIndex,
            int scrollSlotIndex,
            Action emitAcknowledgment,
            Action<EnhancementAttemptResult> deliverResult,
            out EnhancementResultCode rejectionCode)
        {
            return Begin(clientId, charId, itemSlotIndex, scrollSlotIndex, emitAcknowledgment, deliverResult, () => { }, out rejectionCode);
        }

        /// <summary>
        /// Same as the seven-argument <see cref="Begin(uint, CharacterID, int, int, Action, Action{EnhancementAttemptResult}, out EnhancementResultCode)"/>,
        /// plus a failure hook (Enhancement Story 015, decision D2): <paramref name="onCommitFailed"/> runs once, inside the
        /// attempt's revert step, right after <see cref="EnhancementService.RollBackAttempt"/>, in the coordinator's failure
        /// protocol (CR-NET-5.5). It therefore fires for a failed or faulted write, a cancelled or timed-out write, and for
        /// <see cref="IrreversibleOutcomeBeginResult.PersistenceFailed"/> (the write could not start). It does not run on
        /// success, on <see cref="IrreversibleOutcomeBeginResult.RejectedInvalidRequest"/>, on
        /// <see cref="IrreversibleOutcomeBeginResult.RejectedWriteInFlight"/>, or on the path where an exception before the write
        /// started rolls the pending attempt back (that exception is rethrown to the caller instead).
        /// </summary>
        /// <param name="clientId">The requesting connection.</param>
        /// <param name="charId">The requesting character.</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance.</param>
        /// <param name="scrollSlotIndex">Bag slot of the scroll.</param>
        /// <param name="emitAcknowledgment">See the seven-argument overload.</param>
        /// <param name="deliverResult">See the seven-argument overload.</param>
        /// <param name="onCommitFailed">Runs once after the rollback of a failed commit, for example to discard the owner-sync hold. It runs
        /// even if <see cref="EnhancementService.RollBackAttempt"/> throws (that exception then continues to the completion queue's logger).
        /// It must not throw; an exception it throws is logged and swallowed so the coordinator's failure protocol (disconnect,
        /// session preservation) still completes.</param>
        /// <param name="rejectionCode">See the seven-argument overload.</param>
        /// <returns>See the seven-argument overload.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="emitAcknowledgment"/>, <paramref name="deliverResult"/> or <paramref name="onCommitFailed"/> is null.</exception>
        /// <exception cref="Exception">As the seven-argument overload.</exception>
        public IrreversibleOutcomeBeginResult Begin(
            uint clientId,
            CharacterID charId,
            int itemSlotIndex,
            int scrollSlotIndex,
            Action emitAcknowledgment,
            Action<EnhancementAttemptResult> deliverResult,
            Action onCommitFailed,
            out EnhancementResultCode rejectionCode)
        {
            if (emitAcknowledgment == null)
            {
                throw new ArgumentNullException(nameof(emitAcknowledgment));
            }

            if (deliverResult == null)
            {
                throw new ArgumentNullException(nameof(deliverResult));
            }

            if (onCommitFailed == null)
            {
                throw new ArgumentNullException(nameof(onCommitFailed));
            }

            rejectionCode = EnhancementResultCode.Success;
            var attempt = new Attempt(_service, _persistence, charId, itemSlotIndex, scrollSlotIndex, deliverResult, onCommitFailed);

            IrreversibleOutcomeBeginResult result;
            try
            {
                result = _coordinator.Begin<EnhancementAttemptStart, CharacterSaveResult>(
                    clientId,
                    charId,
                    attempt.Validate,
                    emitAcknowledgment,
                    attempt.ComputeOutcome,
                    attempt.StartWrite,
                    IrreversibleOutcomePersistStep.IsSuccess,
                    attempt.Deliver,
                    attempt.Revert,
                    _disconnectClient,
                    _preserveSessionForTtl);
            }
            catch (Exception)
            {
                attempt.RollBackIfNothingElseWill();
                throw;
            }

            if (result == IrreversibleOutcomeBeginResult.RejectedInvalidRequest)
            {
                rejectionCode = attempt.Rejection;
            }

            return result;
        }

        // The state of ONE attempt and the delegates handed to the coordinator for it. A new instance per Begin call,
        // so nothing about an attempt is kept in the orchestrator's fields.
        private sealed class Attempt
        {
            private readonly EnhancementService _service;
            private readonly Func<EnhancementAttemptStart, CancellationToken, Task<CharacterSaveResult>> _persistStart;
            private readonly CharacterID _charId;
            private readonly int _itemSlotIndex;
            private readonly int _scrollSlotIndex;
            private readonly Action<EnhancementAttemptResult> _deliverResult;
            private readonly Action _onCommitFailed;
            private EnhancementAttemptStart _start;
            private bool _pending;

            // True once the coordinator invoked the write delegate. From here the coordinator owns the rollback
            // (its failure protocol calls Revert); rolling back here as well would be a second rollback.
            private bool _startWriteInvoked;

            public Attempt(
                EnhancementService service,
                ICharacterPersistence persistence,
                CharacterID charId,
                int itemSlotIndex,
                int scrollSlotIndex,
                Action<EnhancementAttemptResult> deliverResult,
                Action onCommitFailed)
            {
                _service = service;
                _onCommitFailed = onCommitFailed;
                _persistStart = IrreversibleOutcomePersistStep.StartWrite<EnhancementAttemptStart>(
                    persistence, charId, IrreversibleOutcomeTrigger.EnhancementResult);
                _charId = charId;
                _itemSlotIndex = itemSlotIndex;
                _scrollSlotIndex = scrollSlotIndex;
                _deliverResult = deliverResult;
            }

            /// <summary>The rejection reason; meaningful when validation returned false.</summary>
            public EnhancementResultCode Rejection { get; private set; } = EnhancementResultCode.Success;

            public bool Validate()
            {
                _start = _service.BeginAttempt(_charId, _itemSlotIndex, _scrollSlotIndex);
                _pending = _start.IsPending;
                if (!_pending)
                {
                    Rejection = _start.RejectionCode;
                }

                return _pending;
            }

            public EnhancementAttemptStart ComputeOutcome() => _start;

            public Task<CharacterSaveResult> StartWrite(EnhancementAttemptStart outcome, CancellationToken token)
            {
                _startWriteInvoked = true;
                return _persistStart(outcome, token);
            }

            public void Deliver(EnhancementAttemptStart outcome)
            {
                EnhancementAttemptResult result;
                try
                {
                    result = _service.CompleteAttempt(_charId);
                }
                catch (Exception exception)
                {
                    // The write succeeded, so the commit stands and the result is the pending one. Without this the
                    // caller would never get its result (and a caller holding something until then would hold it for good).
                    Debug.LogException(exception);
                    result = new EnhancementAttemptResult(_start.Outcome, _start.NewLevel,
                        _start.Outcome == EnhancementOutcome.Success ? EnhancementResultCode.Success : EnhancementResultCode.Destruction);
                }

                _deliverResult(result);
            }

            public void Revert()
            {
                try
                {
                    _service.RollBackAttempt(_charId);
                }
                finally
                {
                    try
                    {
                        _onCommitFailed();
                    }
                    catch (Exception exception)
                    {
                        // The hook must not throw; if it does, the coordinator's disconnect and preserve steps still have to run.
                        Debug.LogException(exception);
                    }
                }
            }

            // An exception escaped the coordinator. If this attempt is pending and the write was never invoked (for
            // example the acknowledgment threw), nothing else would end it: roll it back. If BeginAttempt itself threw,
            // the service already unlocked the slot and left nothing pending, so there is nothing to do.
            public void RollBackIfNothingElseWill()
            {
                if (!_pending || _startWriteInvoked || !_service.IsAttemptInProgress(_charId))
                {
                    return;
                }

                try
                {
                    _service.RollBackAttempt(_charId);
                }
                catch (Exception exception)
                {
                    // The service does not log an exception thrown out of a restore step; this is its only log.
                    // The caller's original exception is the one that must surface.
                    Debug.LogException(exception);
                }
            }
        }
    }
}
