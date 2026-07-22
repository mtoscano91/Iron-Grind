using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Generic, reusable ordering guarantee for any irreversible outcome (CR-NET-5.1: item
    /// enhancement destruction, item consumption, level-up stat writes, gold mutation): a broadcast
    /// describing the outcome is never transmitted to any client before the outcome has been
    /// confirmed durable in persistence (Networking Core Story 011). This class owns only the
    /// sequencing — validate → acknowledge → compute → persist → confirm → broadcast, and the
    /// CR-NET-5.5/CR-CP-5 write-failure protocol — never any domain logic, which every caller
    /// supplies via delegates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class, not an instantiable one (deliberate contrast with
    /// <see cref="CrossCuttingRpcGuardChain"/>):</b> Story 010's guard chain owns persistent
    /// in-memory registries (entity ownership, session-ready flags, rate-limit history) across
    /// calls, so it is a constructible class. This sequencer has no state of its own between calls —
    /// every call is fully described by its arguments — matching the established
    /// <see cref="RUBatchWriter"/> "pure stateless static function" precedent in this folder rather
    /// than the guard-chain precedent.
    /// </para>
    /// <para>
    /// <b>Why the ordering guarantee needs no injectable clock (AC-NC-09, AC-NC-15):</b> this method
    /// is fully synchronous — <paramref name="persistOutcome"/> (via <see cref="Execute{TOutcome}"/>'s
    /// generic parameter list) is called and awaited to completion (as an ordinary blocking
    /// delegate call) strictly before <paramref name="broadcastOutcome"/> is ever invoked. There is
    /// no concurrent/async path where a broadcast could race ahead of a slow persistence write. This
    /// makes the "commit before broadcast" guarantee structural, not timing-dependent: a caller
    /// whose <paramref name="persistOutcome"/> delegate takes 200ms or 500ms to return cannot cause
    /// <paramref name="broadcastOutcome"/> to fire any earlier, by construction. Tests prove this by
    /// giving the mock <c>persistOutcome</c> delegate a real (short) blocking delay and comparing
    /// <see cref="System.Diagnostics.Stopwatch"/> timestamps — no injectable time source is needed
    /// because the invariant does not depend on wall-clock trickery, only on call ordering.
    /// </para>
    /// <para>
    /// <b>No <see cref="IServerCrashInjector"/> here — a deliberate deviation from the
    /// story's own Implementation Notes, called out explicitly:</b> the story's Implementation Notes
    /// suggest testing this pattern via <c>IServerCrashInjector</c>'s "delay/crash-at-step variants."
    /// <c>IServerCrashInjector</c>'s actual (already-implemented) contract is narrower and semantically
    /// different: it has no delay variant at all, and its one operation
    /// (<c>RegisterCrashAt(CrashStep)</c>) simulates the server <i>process dying</i> synchronously at
    /// a named step — used for restart/duplicate-rejection testing (AC-NC-16, AC-NC-34), a
    /// fundamentally different scenario from CR-NET-5.5's write-failure protocol, which requires a
    /// <i>live, continuing</i> server to invoke a revert callback, disconnect a client, and preserve
    /// session memory — none of which a crashed process can do. AC-NC-09/AC-NC-15's 200ms/500ms
    /// delays are therefore simulated with a real blocking delay inside the test's own mock
    /// <c>persistOutcome</c> delegate (not via any test-harness interface, since no delay facility
    /// exists anywhere in this codebase), and AC-CBB-1's write failure is simulated by
    /// <c>persistOutcome</c> returning <see langword="false"/> directly — the exact mechanism this
    /// method's own <paramref name="persistOutcome"/> signature already exists to express.
    /// </para>
    /// <para>
    /// <b>Write-failure protocol step order — CR-CP-5 numbered list is authoritative over CR-NET-5.5's
    /// prose summary:</b> CR-NET-5.5 (<c>networking-core.md</c>) describes the failure protocol in
    /// prose as "revert, disconnect, preserve session, alert" and explicitly defers to
    /// <c>character-persistence.md</c> CR-CP-5 as "the complete failure protocol." CR-CP-5's own
    /// numbered list (the actual protocol definition, not a summary) is: 2. revert, 3. disconnect,
    /// 4. fire alert, 5. preserve session — alert strictly before session preservation. This
    /// implementation follows CR-CP-5's numbered order: <paramref name="revertOnFailure"/> →
    /// <paramref name="disconnectClient"/> → critical alert → <paramref name="preserveSessionForTtl"/>.
    /// (A second inconsistency was found and fixed during this story: CR-CP-11's comparison table
    /// previously stated the order with alert last, contradicting CR-CP-5's own numbered list; that
    /// table row was corrected to match CR-CP-5, not this class.)
    /// </para>
    /// <para>
    /// <b>Acknowledgment vs. outcome are structurally distinct (AC-CBB-2):</b>
    /// <paramref name="emitAcknowledgment"/> is a bare <see cref="Action"/> — it cannot carry a
    /// <typeparamref name="TOutcome"/> payload, because it fires before
    /// <paramref name="computeOutcome"/> even runs (CR-NET-5.3 step 3: "a request acknowledgment,
    /// not an outcome"). This is enforced by the delegate's shape, not just by convention.
    /// </para>
    /// <para>
    /// <b>Disconnect and session-preservation are delegate seams, not a real connection/session
    /// manager</b> (mirrors Story 010's <c>RegisterEntityOwnership</c>/<c>MarkSessionReady</c>
    /// precedent for the same forward-dependency shape): no session/connection registry exists yet
    /// in this codebase, so <paramref name="disconnectClient"/> and
    /// <paramref name="preserveSessionForTtl"/> are minimal caller-supplied delegates rather than
    /// invented infrastructure. A future session-lifecycle story is expected to supply real
    /// implementations backed by an actual connection registry.
    /// </para>
    /// <para>
    /// <b><see cref="SESSION_TTL_SECONDS"/> is declared here, provisionally:</b> it does not exist
    /// anywhere else in <c>src/</c> yet, even though it is referenced across multiple GDDs (default
    /// 300, tuning range [60,600], "owned by Networking Core CR-NET-2" per
    /// <c>character-persistence.md</c>). This story is the first to need it in code. Follows the
    /// <see cref="ServerTickLoop.TICK_RATE_HZ"/> precedent — a compile-time <see langword="public"/>
    /// <see langword="const"/>, not external config — pending a real Networking Core
    /// session-lifecycle story that owns its full lifecycle properly.
    /// </para>
    /// <para>
    /// <b>Zero allocation beyond delegate invocation:</b> this method allocates nothing itself — no
    /// boxing of <typeparamref name="TOutcome"/> (a genuine generic parameter, never <c>object</c>),
    /// no collection allocation, no string formatting except on the (rare, already-critical)
    /// persistence-failure path.
    /// </para>
    /// <para>
    /// <b>Exception safety on <paramref name="persistOutcome"/> (code-review hardening, not
    /// literally required by either GDD source):</b> neither CR-NET-5.5 nor CR-CP-5 discusses what
    /// happens if the persistence call throws instead of returning <see langword="false"/> — both
    /// only describe a "non-Success" return value. Since a real persistence layer composing with
    /// this sequencer later may legitimately throw (a database timeout, a connection fault) rather
    /// than catching every failure into a bool, this method wraps the <paramref name="persistOutcome"/>
    /// call in a <see langword="try"/>/<see langword="catch"/> and routes any thrown exception into
    /// the exact same write-failure protocol as a <see langword="false"/> return — revert,
    /// disconnect, critical alert, session preservation, in that order. Without this, a throwing
    /// persistence call would let the exception propagate straight past this sequencer, silently
    /// skipping the entire CR-NET-5.5 protocol this story exists to guarantee.
    /// </para>
    /// <para>
    /// <b>Why loose delegate parameters instead of a single descriptor struct (contrast with
    /// <see cref="CrossCuttingRpcGuardChain.Evaluate"/>'s <see cref="InboundRpcDescriptor"/>):</b>
    /// <see cref="InboundRpcDescriptor"/> bundles passive data fields read by every guard in a fixed
    /// order. <see cref="Execute{TOutcome}"/>'s parameters are behavior seams, not data — each one
    /// is a distinct callback the caller supplies for a distinct step of the sequence, with its own
    /// timing contract (before/after which other step). Bundling them into one struct would not
    /// reduce call-site complexity; it would only hide which callback fires when behind field names,
    /// which is the opposite of what this class's ordering guarantee needs to be legible about.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute(
    ///     clientId: 7,
    ///     validateRequest: () =&gt; itemExists &amp;&amp; materialsPresent,
    ///     emitAcknowledgment: () =&gt; SendEnhancementRequestReceived(entityId, itemId),
    ///     computeOutcome: () =&gt; EnhancementSystem.ComputeOutcome(itemId),
    ///     persistOutcome: outcome =&gt; CharacterPersistence.SaveIrreversibleOutcome(outcome),
    ///     broadcastOutcome: outcome =&gt; SendEnhancementOutcomeBroadcast(outcome),
    ///     revertOnFailure: () =&gt; EnhancementSystem.RevertInMemoryMutation(itemId),
    ///     disconnectClient: (id, reason) =&gt; connectionManager.Disconnect(id, reason),
    ///     preserveSessionForTtl: (id, ttlSeconds) =&gt; sessionRegistry.PreserveForTtl(id, ttlSeconds));
    /// </code>
    /// </example>
    public static class CommitBeforeBroadcastSequencer
    {
        /// <summary>
        /// How long, in seconds, the server preserves a disconnected session's rolled-back in-memory
        /// state after a <see cref="CommitBeforeBroadcastResult.PersistenceFailed"/> outcome, so a
        /// later <c>SaveSession(SessionTTLExpiry)</c> can write the rolled-back state (CR-NET-5.5).
        /// Default 300, tuning range [60,600] (character-persistence.md, "owned by Networking Core
        /// CR-NET-2"). Provisional: declared here because no other <c>src/</c> code owns it yet — see
        /// class remarks.
        /// </summary>
        public const int SESSION_TTL_SECONDS = 300;

        /// <summary>
        /// Runs one irreversible-outcome request through the full commit-before-broadcast sequence
        /// (CR-NET-5.3): validate → acknowledge → compute → persist → confirm → broadcast, with the
        /// CR-NET-5.5/CR-CP-5 write-failure protocol on a persistence failure. Never broadcasts
        /// before persistence confirms durable (CR-NET-5.1).
        /// </summary>
        /// <typeparam name="TOutcome">
        /// The caller's own outcome type (e.g. a mock outcome in tests, or a real domain outcome
        /// once an owning system exists). Never boxed — a genuine generic parameter, not
        /// <see langword="object"/>.
        /// </typeparam>
        /// <param name="clientId">
        /// The connection identity this request belongs to. Passed to
        /// <paramref name="disconnectClient"/> and <paramref name="preserveSessionForTtl"/> on the
        /// failure path.
        /// </param>
        /// <param name="validateRequest">
        /// Called first (CR-NET-5.3 step 2). Returning <see langword="false"/> rejects the request
        /// immediately — no acknowledgment, computation, persistence, or broadcast occurs, and
        /// <see cref="CommitBeforeBroadcastResult.RejectedInvalidRequest"/> is returned. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="emitAcknowledgment">
        /// Called immediately after a successful validation, before <paramref name="computeOutcome"/>
        /// runs (CR-NET-5.3 step 3, AC-CBB-2). A bare <see cref="Action"/> — structurally incapable
        /// of carrying an outcome payload, since none exists yet at this point in the sequence. Must
        /// not be <see langword="null"/>.
        /// </param>
        /// <param name="computeOutcome">
        /// Called after the acknowledgment fires (CR-NET-5.3 step 4). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="persistOutcome">
        /// Called with the computed outcome (CR-NET-5.3 step 5). Returning <see langword="true"/>
        /// means the write is confirmed durable; returning <see langword="false"/> triggers the
        /// CR-NET-5.5/CR-CP-5 write-failure protocol and <see cref="CommitBeforeBroadcastResult.PersistenceFailed"/>
        /// is returned. No retries are attempted on <see langword="false"/> — irreversible outcome
        /// writes are not idempotent; a retry risks a duplicate commit. Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="broadcastOutcome">
        /// Called only after <paramref name="persistOutcome"/> returns <see langword="true"/>
        /// (CR-NET-5.3 step 6) — never before. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="revertOnFailure">
        /// Called first on the write-failure path (CR-CP-5 step 2, caller-owns-rollback): this
        /// sequencer does not know how to undo caller-specific in-memory state, so the caller
        /// supplies its own revert logic. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="disconnectClient">
        /// Called second on the write-failure path (CR-CP-5 step 3) with
        /// (<paramref name="clientId"/>, <see cref="DisconnectReason.Other"/>). Must not be
        /// <see langword="null"/>.
        /// </param>
        /// <param name="preserveSessionForTtl">
        /// Called last on the write-failure path (CR-CP-5 step 5) with
        /// (<paramref name="clientId"/>, <see cref="SESSION_TTL_SECONDS"/>), after the critical alert
        /// fires (CR-CP-5 step 4). Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">
        /// Optional test/dev-build observer. On a write failure, its
        /// <c>OnCriticalInfrastructureAlertFired</c> hook fires between
        /// <paramref name="disconnectClient"/> and <paramref name="preserveSessionForTtl"/> (CR-CP-5
        /// step 4), matching this call's own critical-alert log. This parameter only exists inside
        /// <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> — see remarks elsewhere in this folder
        /// (Story 002 release-stripping contract, AC-TC-02) on why the observer's interface type is
        /// never referenced in an unguarded production context.
        /// </param>
        /// <returns>
        /// <see cref="CommitBeforeBroadcastResult.Committed"/>,
        /// <see cref="CommitBeforeBroadcastResult.RejectedInvalidRequest"/>, or
        /// <see cref="CommitBeforeBroadcastResult.PersistenceFailed"/>.
        /// </returns>
        /// <example>
        /// <code>
        /// CommitBeforeBroadcastResult result = CommitBeforeBroadcastSequencer.Execute&lt;MockOutcome&gt;(
        ///     clientId: 7,
        ///     validateRequest: () =&gt; true,
        ///     emitAcknowledgment: () =&gt; ackFired = true,
        ///     computeOutcome: () =&gt; new MockOutcome(42),
        ///     persistOutcome: outcome =&gt; persistence.Save(outcome),
        ///     broadcastOutcome: outcome =&gt; transport.Send(outcome),
        ///     revertOnFailure: () =&gt; caller.Revert(),
        ///     disconnectClient: (id, reason) =&gt; transport.Disconnect(id, reason),
        ///     preserveSessionForTtl: (id, ttl) =&gt; sessions.Preserve(id, ttl));
        /// </code>
        /// </example>
        public static CommitBeforeBroadcastResult Execute<TOutcome>(
            uint clientId,
            Func<bool> validateRequest,
            Action emitAcknowledgment,
            Func<TOutcome> computeOutcome,
            Func<TOutcome, bool> persistOutcome,
            Action<TOutcome> broadcastOutcome,
            Action revertOnFailure,
            Action<uint, DisconnectReason> disconnectClient,
            Action<uint, int> preserveSessionForTtl
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (validateRequest == null)
            {
                throw new ArgumentNullException(nameof(validateRequest));
            }

            if (emitAcknowledgment == null)
            {
                throw new ArgumentNullException(nameof(emitAcknowledgment));
            }

            if (computeOutcome == null)
            {
                throw new ArgumentNullException(nameof(computeOutcome));
            }

            if (persistOutcome == null)
            {
                throw new ArgumentNullException(nameof(persistOutcome));
            }

            if (broadcastOutcome == null)
            {
                throw new ArgumentNullException(nameof(broadcastOutcome));
            }

            if (revertOnFailure == null)
            {
                throw new ArgumentNullException(nameof(revertOnFailure));
            }

            if (disconnectClient == null)
            {
                throw new ArgumentNullException(nameof(disconnectClient));
            }

            if (preserveSessionForTtl == null)
            {
                throw new ArgumentNullException(nameof(preserveSessionForTtl));
            }

            // Step 1 (CR-NET-5.3 step 2): validate, reject immediately if invalid. Nothing else in
            // the sequence runs — no acknowledgment, no computation, no persistence, no broadcast.
            if (!validateRequest())
            {
                return CommitBeforeBroadcastResult.RejectedInvalidRequest;
            }

            // Step 2 (CR-NET-5.3 step 3, AC-CBB-2): acknowledge BEFORE computing the outcome. This
            // is a processing signal, not a result — structurally distinct from broadcastOutcome
            // (see class remarks).
            emitAcknowledgment();

            // Step 3 (CR-NET-5.3 step 4): compute the outcome.
            TOutcome outcome = computeOutcome();

            // Step 4 (CR-NET-5.3 step 5): write atomically to persistence. persistOutcome may return
            // false OR throw — both are treated identically as a write failure (deliberate hardening
            // beyond CR-NET-5.5/CR-CP-5's literal prose, see class remarks).
            bool persisted;
            Exception persistException = null;
            try
            {
                persisted = persistOutcome(outcome);
            }
            catch (Exception ex)
            {
                persisted = false;
                persistException = ex;
            }

            if (!persisted)
            {
                // CR-NET-5.5 / CR-CP-5 write-failure protocol, in CR-CP-5's numbered order (see
                // class remarks for why CR-CP-5's numbered list is authoritative over CR-NET-5.5's
                // prose summary): revert -> disconnect -> critical alert -> preserve session. No
                // retries are attempted — irreversible outcome writes are not idempotent. Runs
                // identically whether persistOutcome returned false or threw.
                revertOnFailure();
                disconnectClient(clientId, DisconnectReason.Other);

                if (persistException != null)
                {
                    Debug.LogError($"[CommitBeforeBroadcastSequencer] PersistenceWriteFailed: clientId={clientId} — " +
                        $"persistOutcome threw {persistException.GetType().Name}: {persistException.Message}. No " +
                        "outcome message emitted, no retry attempted (CR-NET-5.5). Caller rollback and client " +
                        "disconnect completed; critical infrastructure alert firing.");
                }
                else
                {
                    Debug.LogError($"[CommitBeforeBroadcastSequencer] PersistenceWriteFailed: clientId={clientId} — " +
                        "SaveIrreversibleOutcome failed. No outcome message emitted, no retry attempted (CR-NET-5.5). " +
                        "Caller rollback and client disconnect completed; critical infrastructure alert firing.");
                }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                observer?.OnCriticalInfrastructureAlertFired(clientId,
                    "SaveIrreversibleOutcome write failure (CR-NET-5.5 / CR-CP-5).");
#endif

                preserveSessionForTtl(clientId, SESSION_TTL_SECONDS);

                return CommitBeforeBroadcastResult.PersistenceFailed;
            }

            // Step 5 (CR-NET-5.3 step 6): only after the write is confirmed durable does the outcome
            // message get broadcast. CR-NET-5.1's core invariant — never broadcast before commit.
            broadcastOutcome(outcome);

            return CommitBeforeBroadcastResult.Committed;
        }
    }
}
