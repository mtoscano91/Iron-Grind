using System;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Default <see cref="IIrreversibleOutcomeCoordinator"/> (ADR-011 Decision 5, Networking Core
    /// Story 031). Uses <see cref="ITickCompletionQueue"/> to get the write's result back on the tick
    /// thread and <see cref="ICharacterMutationGate"/> to record the write in flight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds no record of in-flight writes: the gate records them and each call's state object rides
    /// on the queue entry. The state's callback is a one-shot, exempt from the ADR-010 lambda rule.
    /// </para>
    /// <para>
    /// The gate opens in a <c>finally</c> after the result has been handled. Exceptions from
    /// <c>isSuccess</c>, deliver or a failure step are not caught here; the queue logs them.
    /// </para>
    /// <para>
    /// Not wired in: nothing in production constructs this class yet.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var coordinator = new IrreversibleOutcomeCoordinator(queue, gate);
    /// </code>
    /// </example>
    public sealed class IrreversibleOutcomeCoordinator : IIrreversibleOutcomeCoordinator
    {
        private const string LOG_PREFIX = "[IrreversibleOutcomeCoordinator] PersistenceWriteFailed";
        private const string ALERT_REASON = "Irreversible write failure (CR-NET-5.5 / CR-CP-5).";

        private readonly ITickCompletionQueue _queue;
        private readonly ICharacterMutationGate _gate;
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
        private readonly INetworkTestObserver _observer;
#endif

        /// <summary>Creates a coordinator over a queue and a gate.</summary>
        /// <param name="queue">The tick completion queue that tracks the writes.</param>
        /// <param name="gate">The per-character mutation gate.</param>
        /// <param name="observer">Optional test observer; the parameter exists only in test and development builds.</param>
        /// <exception cref="ArgumentNullException"><paramref name="queue"/> or <paramref name="gate"/> is null.</exception>
        public IrreversibleOutcomeCoordinator(
            ITickCompletionQueue queue,
            ICharacterMutationGate gate
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            _observer = observer;
#endif
        }

        /// <inheritdoc/>
        public IrreversibleOutcomeBeginResult Begin<TOutcome, TResult>(
            uint clientId,
            CharacterID charId,
            Func<bool> validateRequest,
            Action emitAcknowledgment,
            Func<TOutcome> computeAndApplyOutcome,
            Func<TOutcome, CancellationToken, Task<TResult>> startWrite,
            Func<TResult, bool> isSuccess,
            Action<TOutcome> deliverOutcome,
            Action revertOnFailure,
            Action<uint, DisconnectReason> disconnectClient,
            Action<uint, int> preserveSessionForTtl)
        {
            if (validateRequest == null) { throw new ArgumentNullException(nameof(validateRequest)); }
            if (emitAcknowledgment == null) { throw new ArgumentNullException(nameof(emitAcknowledgment)); }
            if (computeAndApplyOutcome == null) { throw new ArgumentNullException(nameof(computeAndApplyOutcome)); }
            if (startWrite == null) { throw new ArgumentNullException(nameof(startWrite)); }
            if (isSuccess == null) { throw new ArgumentNullException(nameof(isSuccess)); }
            if (deliverOutcome == null) { throw new ArgumentNullException(nameof(deliverOutcome)); }
            if (revertOnFailure == null) { throw new ArgumentNullException(nameof(revertOnFailure)); }
            if (disconnectClient == null) { throw new ArgumentNullException(nameof(disconnectClient)); }
            if (preserveSessionForTtl == null) { throw new ArgumentNullException(nameof(preserveSessionForTtl)); }

            // One write in flight per character (CR-CP-7); checked before validation so no delegate runs.
            if (_gate.IsHeld(charId))
            {
                return IrreversibleOutcomeBeginResult.RejectedWriteInFlight;
            }

            if (!validateRequest())
            {
                return IrreversibleOutcomeBeginResult.RejectedInvalidRequest;
            }

            var state = new WriteState<TOutcome, TResult>(
                this, clientId, charId, isSuccess, deliverOutcome, revertOnFailure, disconnectClient, preserveSessionForTtl);

            _gate.Close(charId);
            bool tracked = false;
            try
            {
                emitAcknowledgment();
                state.Outcome = computeAndApplyOutcome();
                tracked = StartAndTrack(state, startWrite);
                return tracked ? IrreversibleOutcomeBeginResult.Started : IrreversibleOutcomeBeginResult.PersistenceFailed;
            }
            finally
            {
                if (!tracked)
                {
                    _gate.Open(charId);
                }
            }
        }

        // Starts the write and hands its task to the queue. Returns false when the write did not
        // start; the failure protocol has then run. The start and the Track call are separate
        // statements only for the null-task check: nothing yields or reads the task in between.
        private bool StartAndTrack<TOutcome, TResult>(
            WriteState<TOutcome, TResult> state,
            Func<TOutcome, CancellationToken, Task<TResult>> startWrite)
        {
            var cancellation = new CancellationTokenSource();
            state.Cancellation = cancellation;

            Task<TResult> task = null;
            Exception startError = null;
            try
            {
                task = startWrite(state.Outcome, cancellation.Token);
            }
            catch (Exception ex)
            {
                startError = ex;
            }

            if (task == null)
            {
                try
                {
                    state.RunFailureProtocol(startError != null
                        ? $"write did not start (start delegate threw: {startError})"
                        : "write did not start (start delegate returned no task)");
                }
                finally
                {
                    cancellation.Dispose();
                }

                return false;
            }

            try
            {
                _queue.Track(task, cancellation, state.OnCompleted);
            }
            catch
            {
                // The write is running but nothing will hand its result back (Track throws off the
                // tick thread). Same treatment as a failed write; the source is left to the
                // collector, as after a watchdog timeout, because the write still holds its token.
                state.RunFailureProtocol("write started but could not be tracked, the outcome of the write is unknown");
                throw;
            }

            return true;
        }

        private void RunFailureProtocol(
            uint clientId,
            CharacterID charId,
            string cause,
            Action revertOnFailure,
            Action<uint, DisconnectReason> disconnectClient,
            Action<uint, int> preserveSessionForTtl)
        {
            string message = $"{LOG_PREFIX}: clientId={clientId} charId={charId} — {cause}. No outcome message emitted, " +
                "no retry attempted (CR-NET-5.5). Caller rollback and client disconnect completed; critical infrastructure alert firing.";
            IrreversibleWriteFailureProtocol.Run(clientId, message, ALERT_REASON, revertOnFailure, disconnectClient, preserveSessionForTtl
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                , _observer
#endif
                );
        }

        private static string DescribeCause<TResult>(TickTaskResult<TResult> result)
        {
            switch (result.Status)
            {
                case TickTaskStatus.Completed:
                    return "write returned a non-success result";
                case TickTaskStatus.Faulted:
                    return $"write faulted ({result.Error?.GetType().Name}: {result.Error?.Message})";
                case TickTaskStatus.Canceled:
                    return "write was cancelled";
                default:
                    return "watchdog timeout, the outcome of the write is unknown";
            }
        }

        // Per-call state: the delegates, the outcome, the ids and the token source for one write.
        private sealed class WriteState<TOutcome, TResult>
        {
            private readonly IrreversibleOutcomeCoordinator _owner;
            private readonly uint _clientId;
            private readonly CharacterID _charId;
            private readonly Func<TResult, bool> _isSuccess;
            private readonly Action<TOutcome> _deliverOutcome;
            private readonly Action _revertOnFailure;
            private readonly Action<uint, DisconnectReason> _disconnectClient;
            private readonly Action<uint, int> _preserveSessionForTtl;

            internal TOutcome Outcome;
            internal CancellationTokenSource Cancellation;

            internal WriteState(
                IrreversibleOutcomeCoordinator owner,
                uint clientId,
                CharacterID charId,
                Func<TResult, bool> isSuccess,
                Action<TOutcome> deliverOutcome,
                Action revertOnFailure,
                Action<uint, DisconnectReason> disconnectClient,
                Action<uint, int> preserveSessionForTtl)
            {
                _owner = owner;
                _clientId = clientId;
                _charId = charId;
                _isSuccess = isSuccess;
                _deliverOutcome = deliverOutcome;
                _revertOnFailure = revertOnFailure;
                _disconnectClient = disconnectClient;
                _preserveSessionForTtl = preserveSessionForTtl;
            }

            internal void RunFailureProtocol(string cause) =>
                _owner.RunFailureProtocol(_clientId, _charId, cause, _revertOnFailure, _disconnectClient, _preserveSessionForTtl);

            internal void OnCompleted(TickTaskResult<TResult> result)
            {
                try
                {
                    if (result.Status == TickTaskStatus.Completed && _isSuccess(result.Value))
                    {
                        _deliverOutcome(Outcome);
                    }
                    else
                    {
                        RunFailureProtocol(DescribeCause(result));
                    }
                }
                finally
                {
                    try
                    {
                        // A timed-out write may still be running with this token; leave the source to the collector.
                        if (result.Status != TickTaskStatus.TimedOut)
                        {
                            Cancellation.Dispose();
                        }
                    }
                    finally
                    {
                        _owner._gate.Open(_charId);
                    }
                }
            }
        }
    }
}
