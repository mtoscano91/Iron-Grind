using System;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Splits the commit-before-broadcast sequence across ticks (ADR-011 Decision 5): <c>Begin</c>
    /// runs on tick N and starts the write; the completion callback runs on tick N+k, delivers the
    /// outcome on success or runs the shared failure protocol otherwise. Never retries a write.
    /// </summary>
    /// <remarks>Tick thread only. The caller's delegates must tolerate a client that has disconnected meanwhile.</remarks>
    /// <example>
    /// <code>
    /// var result = coordinator.Begin&lt;Attempt, SaveResult&gt;(
    ///     clientId, charId,
    ///     validateRequest: () =&gt; CanEnhance(charId),
    ///     emitAcknowledgment: () =&gt; SendRequestReceived(clientId),
    ///     computeAndApplyOutcome: () =&gt; ApplyAttempt(charId),
    ///     startWrite: (attempt, token) =&gt; persistence.Save(attempt, token),
    ///     isSuccess: saved =&gt; saved == SaveResult.Ok,
    ///     deliverOutcome: attempt =&gt; Broadcast(attempt),
    ///     revertOnFailure: () =&gt; RollBack(charId),
    ///     disconnectClient: (id, reason) =&gt; transport.Disconnect(id, reason),
    ///     preserveSessionForTtl: (id, ttl) =&gt; sessions.Preserve(id, ttl));
    /// </code>
    /// </example>
    public interface IIrreversibleOutcomeCoordinator
    {
        /// <summary>
        /// Runs the tick-N half of an irreversible outcome: reject if a write is in flight for the
        /// character, validate, close the gate, acknowledge, compute and apply the outcome in memory,
        /// start the write and track it. See <see cref="IrreversibleOutcomeBeginResult"/> for the results.
        /// </summary>
        /// <typeparam name="TOutcome">The caller's outcome type; the same instance reaches the write and deliver delegates.</typeparam>
        /// <typeparam name="TResult">The write task's result type (for example a save result code).</typeparam>
        /// <param name="clientId">The connection this request belongs to.</param>
        /// <param name="charId">The character whose gate is closed for the duration of the write.</param>
        /// <param name="validateRequest">Returns false to reject the request.</param>
        /// <param name="emitAcknowledgment">Request acknowledgment; runs with the gate closed, before the outcome is computed.</param>
        /// <param name="computeAndApplyOutcome">Computes the outcome and applies it in memory.</param>
        /// <param name="startWrite">Starts the persistence write with the outcome and a token the watchdog cancels. Must return a started task.</param>
        /// <param name="isSuccess">Decides whether a completed write's value means success.</param>
        /// <param name="deliverOutcome">Runs on the completing tick on success, with the gate still closed.</param>
        /// <param name="revertOnFailure">First failure step: undo the in-memory outcome.</param>
        /// <param name="disconnectClient">Second failure step; called with <see cref="DisconnectReason.Other"/>.</param>
        /// <param name="preserveSessionForTtl">Last failure step; called with <see cref="CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS"/>.</param>
        /// <returns>What happened; see <see cref="IrreversibleOutcomeBeginResult"/>.</returns>
        /// <exception cref="ArgumentNullException">Any delegate is null; nothing runs.</exception>
        /// <exception cref="Exception">
        /// Exceptions from the caller's delegates propagate. From validation: the gate was never closed.
        /// From acknowledge or compute: the gate is open again and no write was started. From a failure
        /// step when the write did not start: the remaining steps are skipped and the gate is open again.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Called off the tick thread: the write had already started, so the failure protocol runs, the
        /// gate is open again and the queue's exception is rethrown.
        /// </exception>
        IrreversibleOutcomeBeginResult Begin<TOutcome, TResult>(
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
            Action<uint, int> preserveSessionForTtl);
    }
}
