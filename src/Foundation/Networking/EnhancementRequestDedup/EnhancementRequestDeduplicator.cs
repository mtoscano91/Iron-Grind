using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Result of one <see cref="EnhancementRequestDeduplicator.TryProcess{TOutcome}"/> call (EC-NET-9,
    /// Networking Core Story 015).
    /// </summary>
    public enum EnhancementRequestDedupResult : byte
    {
        /// <summary>
        /// The request's <c>requestId</c> was not a duplicate, the outcome was computed, the
        /// persistence write (of both the outcome and the new <c>LastEnhancementRequestID</c>)
        /// succeeded, and the broadcast fired.
        /// </summary>
        Committed = 0,

        /// <summary>
        /// The request's <c>requestId</c> matches the already-recorded <c>LastEnhancementRequestID</c>
        /// — rejected unconditionally, with no time window (EC-NET-9, AC-NC-27). No computation,
        /// persistence write, or broadcast occurred.
        /// </summary>
        RejectedDuplicate = 1,

        /// <summary>
        /// <c>persistOutcomeAndRequestId</c> returned <see langword="false"/> — either a genuine
        /// persistence failure, or (in a crash-recovery test) a simulated server-process crash
        /// occurring synchronously after the durable write but before this method could proceed to
        /// broadcast (see <see cref="EnhancementRequestDeduplicator"/> remarks). No broadcast occurred.
        /// This in-memory instance does not record <c>requestId</c> as processed — a fresh instance
        /// seeded from the persisted value (see the constructor) is what proves durability across a
        /// simulated crash, not this instance's own state.
        /// </summary>
        NotCommitted = 2,
    }

    /// <summary>
    /// Generic, reusable proof of the EC-NET-9 duplicate-enhancement-request dedup rule: a per-character
    /// <c>LastEnhancementRequestID</c> field rejects any resubmission of an already-processed
    /// <c>RequestID</c> unconditionally — with no time window — and that field is persisted atomically
    /// with the enhancement outcome so the rejection survives a server crash (Networking Core Story 015,
    /// AC-NC-16 / AC-NC-27 / AC-NC-34-CRASH). Like <see cref="CommitBeforeBroadcastSequencer"/> (Story
    /// 011), this class owns only the dedup-and-ordering mechanics — never any domain logic, which every
    /// caller supplies via delegates. No real Enhancement System exists yet in this codebase; this is a
    /// generic mock proof against a caller-supplied <c>TOutcome</c>, not real Enhancement System logic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateful instance, one per character — deliberate contrast with
    /// <see cref="CommitBeforeBroadcastSequencer"/>'s stateless-static shape:</b> EC-NET-9's dedup field
    /// is inherently a piece of per-character persisted state (<c>LastEnhancementRequestID</c>), not a
    /// pure function of its arguments, so this class is instantiable and keeps that one field in memory
    /// across calls — matching <see cref="ConnectionStateMachine"/>'s precedent for the same reason,
    /// just scoped to a single character rather than an account registry (no internal dictionary; a
    /// caller managing multiple characters owns one instance per character, exactly as it would own one
    /// persisted <c>LastEnhancementRequestID</c> column per character row — EC-NET-9's "dedup scope is
    /// per-character" rule).
    /// </para>
    /// <para>
    /// <b>No time window, ever (EC-NET-9's core guarantee, AC-NC-27):</b> the duplicate check compares
    /// only <c>requestId == LastEnhancementRequestID</c> — there is no tick or wall-clock parameter
    /// anywhere on <see cref="TryProcess{TOutcome}"/>'s signature. This is the entire AC-NC-27 guarantee
    /// expressed structurally: a caller cannot pass an "elapsed time" argument that would let a duplicate
    /// through, because no such argument exists.
    /// </para>
    /// <para>
    /// <b>Crash-durability proof pattern (AC-NC-16 / AC-NC-34-CRASH):</b> this class does not integrate
    /// with <see cref="IServerCrashInjector"/> directly — <c>persistOutcomeAndRequestId</c> (the
    /// caller's own delegate) is where a test simulates "the durable write completed, then the server
    /// process died before <see cref="TryProcess{TOutcome}"/> could reach <c>emitOutcomeBroadcast</c>":
    /// the delegate performs its (mock) persistence write — which durably records both the outcome and
    /// the new <c>requestId</c> in the test's own mock store, independent of this instance's in-memory
    /// field — then consults <see cref="IServerCrashInjector.RegisterCrashAt"/>'s registered-step signal
    /// and returns <see langword="false"/> if a crash was signaled. A subsequent "reconnect" is modeled
    /// by constructing a <i>fresh</i> <see cref="EnhancementRequestDeduplicator"/> seeded (via the
    /// constructor) from the test's own mock store's now-durable value, then resubmitting the same
    /// <c>requestId</c> against that fresh instance to prove the rejection survives the simulated crash —
    /// never by asserting on the original (crashed) instance's own state, which is not the point being
    /// proven.
    /// </para>
    /// <para>
    /// <b>Delegates validated unconditionally, before the dedup check</b> — matching
    /// <see cref="ConnectionStateMachine.RecordFailedReAuthAttempt"/>'s precedent of validating every
    /// delegate upfront regardless of which branch executes.
    /// </para>
    /// <para>
    /// <b>Zero allocation beyond delegate invocation:</b> no boxing of <c>TOutcome</c> (a genuine
    /// generic parameter, never <see langword="object"/>), no collection allocation.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var dedup = new EnhancementRequestDeduplicator(); // fresh character, no prior enhancement
    ///
    /// EnhancementRequestDedupResult result = dedup.TryProcess(
    ///     requestId: 42u,
    ///     computeOutcome: () =&gt; EnhancementSystem.ComputeOutcome(itemId),
    ///     persistOutcomeAndRequestId: (outcome, requestId) =&gt; CharacterPersistence.SaveEnhancementOutcome(outcome, requestId),
    ///     emitOutcomeBroadcast: outcome =&gt; transport.BroadcastEnhancementOutcome(outcome),
    ///     outcome: out var outcome1);
    /// // result == Committed
    ///
    /// EnhancementRequestDedupResult duplicateResult = dedup.TryProcess(
    ///     requestId: 42u, // same RequestID, resubmitted — rejected unconditionally, no time window
    ///     computeOutcome: () =&gt; throw new InvalidOperationException("must not be called for a duplicate"),
    ///     persistOutcomeAndRequestId: (outcome, requestId) =&gt; throw new InvalidOperationException("must not be called for a duplicate"),
    ///     emitOutcomeBroadcast: outcome =&gt; throw new InvalidOperationException("must not be called for a duplicate"),
    ///     outcome: out var outcome2);
    /// // duplicateResult == RejectedDuplicate
    /// </code>
    /// </example>
    public sealed class EnhancementRequestDeduplicator
    {
        private uint? _lastEnhancementRequestId;

        /// <summary>
        /// Creates a new dedup tracker for one character, optionally seeded with an already-persisted
        /// <c>LastEnhancementRequestID</c> — the "server restart" / "reconnect" case (AC-NC-16 /
        /// AC-NC-34-CRASH): construct a fresh instance from the persisted value to prove a resubmission
        /// is still rejected after a simulated crash.
        /// </summary>
        /// <param name="persistedLastEnhancementRequestId">
        /// The character's persisted <c>LastEnhancementRequestID</c>, if any (<see langword="null"/> for
        /// a character that has never successfully completed an enhancement).
        /// </param>
        /// <example>
        /// <code>
        /// // Simulating a reconnect after a crash: seed from what the mock persistence store recorded.
        /// var reconnectedDedup = new EnhancementRequestDeduplicator(persistedLastEnhancementRequestId: 42u);
        /// </code>
        /// </example>
        public EnhancementRequestDeduplicator(uint? persistedLastEnhancementRequestId = null)
        {
            _lastEnhancementRequestId = persistedLastEnhancementRequestId;
        }

        /// <summary>The character's current in-memory <c>LastEnhancementRequestID</c>, if any.</summary>
        public uint? LastEnhancementRequestId => _lastEnhancementRequestId;

        /// <summary>
        /// Processes one enhancement request through the EC-NET-9 dedup check and, if not a duplicate,
        /// the compute → persist → broadcast sequence (mirroring
        /// <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/>'s validate-before-compute
        /// discipline, scoped to this one concern).
        /// </summary>
        /// <typeparam name="TOutcome">The caller's own outcome type. Never boxed.</typeparam>
        /// <param name="requestId">The enhancement request's client-supplied <c>RequestID</c>.</param>
        /// <param name="computeOutcome">
        /// Called only if <paramref name="requestId"/> is not a duplicate (EC-NET-9: no computation for
        /// a duplicate). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="persistOutcomeAndRequestId">
        /// Called with (<c>outcome</c>, <paramref name="requestId"/>) to durably write both the outcome
        /// and the new <c>LastEnhancementRequestID</c> atomically. Returning <see langword="true"/> means
        /// the write is confirmed durable and this instance now reflects <paramref name="requestId"/> as
        /// processed; returning <see langword="false"/> means no broadcast occurs and this instance does
        /// not mirror <paramref name="requestId"/> (see class remarks' crash-durability proof pattern for
        /// why this can still be correct across a simulated crash). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="emitOutcomeBroadcast">
        /// Called only after <paramref name="persistOutcomeAndRequestId"/> returns <see langword="true"/>.
        /// Must not be <see langword="null"/>.
        /// </param>
        /// <param name="outcome">
        /// The computed outcome, if <paramref name="computeOutcome"/> ran; otherwise <see langword="default"/>.
        /// </param>
        /// <returns>
        /// <see cref="EnhancementRequestDedupResult.Committed"/>,
        /// <see cref="EnhancementRequestDedupResult.RejectedDuplicate"/>, or
        /// <see cref="EnhancementRequestDedupResult.NotCommitted"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Any of <paramref name="computeOutcome"/>, <paramref name="persistOutcomeAndRequestId"/>, or
        /// <paramref name="emitOutcomeBroadcast"/> is <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// var result = dedup.TryProcess(requestId: 42u,
        ///     computeOutcome: () =&gt; new MockOutcome(itemId: 99u, success: true),
        ///     persistOutcomeAndRequestId: (outcome, reqId) =&gt; mockStore.Save(outcome, reqId),
        ///     emitOutcomeBroadcast: outcome =&gt; broadcastFired = true,
        ///     outcome: out var computed);
        /// </code>
        /// </example>
        public EnhancementRequestDedupResult TryProcess<TOutcome>(
            uint requestId,
            Func<TOutcome> computeOutcome,
            Func<TOutcome, uint, bool> persistOutcomeAndRequestId,
            Action<TOutcome> emitOutcomeBroadcast,
            out TOutcome outcome)
        {
            if (computeOutcome == null)
            {
                throw new ArgumentNullException(nameof(computeOutcome));
            }

            if (persistOutcomeAndRequestId == null)
            {
                throw new ArgumentNullException(nameof(persistOutcomeAndRequestId));
            }

            if (emitOutcomeBroadcast == null)
            {
                throw new ArgumentNullException(nameof(emitOutcomeBroadcast));
            }

            // EC-NET-9: unconditional rejection, no time window — the only comparison this class ever
            // makes is requestId == LastEnhancementRequestID.
            if (_lastEnhancementRequestId.HasValue && requestId == _lastEnhancementRequestId.Value)
            {
                outcome = default;
                return EnhancementRequestDedupResult.RejectedDuplicate;
            }

            outcome = computeOutcome();

            bool committed = persistOutcomeAndRequestId(outcome, requestId);
            if (!committed)
            {
                return EnhancementRequestDedupResult.NotCommitted;
            }

            _lastEnhancementRequestId = requestId;
            emitOutcomeBroadcast(outcome);

            return EnhancementRequestDedupResult.Committed;
        }
    }
}
