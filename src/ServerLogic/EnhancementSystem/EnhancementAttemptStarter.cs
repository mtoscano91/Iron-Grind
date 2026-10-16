using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// The production <see cref="IEnhancementAttemptStarter"/> (Enhancement Story 015; design/gdd/enhancement-system.md
    /// CR-ENH-11, CR-ENH-15 steps 3, 4, 6b and 9, EC-ENH-1, AC-ENH-13; networking-core.md CR-NET-5.5; networking-wire-protocol.md
    /// CR-NET-7.7, AC-NC-35, AC-NC-43; networking-session.md EC-NET-9). It starts an accepted attempt through
    /// <see cref="EnhancementCommitOrchestrator"/> and owns what the client receives because of it: the
    /// <see cref="EnhancementRequestReceived"/> acknowledgment, the <see cref="EnhancementAttemptResultMessage"/> after the
    /// commit, the rejection of a step 4 failure, the owner-sync hold of CR-ENH-11, and the in-memory
    /// <c>LastEnhancementRequestID</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Hold.</b> <see cref="IOwnerInventorySyncHold.OpenHold"/> runs before <c>Begin</c>, because the attempt's
    /// validation (CR-ENH-15 steps 2 to 6a, including the step 4 scroll consumption) runs inside the coordinator's request
    /// validation and raises the inventory events the hold must keep from the client. The hold ends with
    /// <see cref="IOwnerInventorySyncHold.ReleaseHold"/> on a rejection (step 4 failure) and after a successful commit, and
    /// with <see cref="IOwnerInventorySyncHold.DiscardHold"/> on a failed commit, where the client is disconnected
    /// (CR-NET-5.5) and receives an <c>InventoryFullSync</c> on its next zone entry.</para>
    /// <para><b>Delivery callback.</b> It never throws. A throw after a successful write would leave the client without a
    /// result (Story 011), so the id recording, the result enqueue and the release are guarded separately and each
    /// exception is logged. The hold is released even if an earlier step threw.</para>
    /// <para><b>Result destination.</b> The result goes to the <c>clientId</c> given at start. After a reconnect the new
    /// connection receives no <c>EnhancementAttemptResult</c> (EC-ENH-1, no replay; OQ-ENH-8); it receives an
    /// <c>InventoryFullSync</c> of the committed bag from the hold (AC-ENH-13).</para>
    /// <para><b>RejectedWriteInFlight.</b> <c>EnhancementAttemptRequest</c> is not held by the dispatcher (ADR-014 Decision 3):
    /// while another system's irreversible write is in flight (for example a respec), the coordinator returns
    /// <see cref="IrreversibleOutcomeBeginResult.RejectedWriteInFlight"/> and this starter sends the rejection, with code
    /// <see cref="EnhancementResultCode.RejectedConcurrentAttempt"/>. A second attempt of the same character is rejected
    /// earlier, by the handler's step 2 validation.</para>
    /// <para><b>Hold ownership.</b> The starter opens a character's hold only if none of its own attempts holds it already, and
    /// an attempt ends only a hold it opened. A start that reaches the starter while the same character's attempt is in
    /// flight (a caller that skips the handler's validation) therefore leaves that attempt's hold alone.</para>
    /// <para><b>Order on a failed commit.</b> The hold is discarded inside the coordinator's failure protocol, after the
    /// rollback and before the client is disconnected.</para>
    /// <para>Tick thread only. Per-attempt state lives in a per-call object; the only state in its own fields is the set of
    /// characters whose hold it opened. Nothing here awaits or blocks, and it does not read the mutation gate.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var starter = new EnhancementAttemptStarter(orchestrator, outbox, ownerSyncSender, dedupLookup);
    /// var handlers = new EnhancementClientRequestHandlers(service, outbox, dedupLookup, starter);
    /// </code>
    /// </example>
    public sealed class EnhancementAttemptStarter : IEnhancementAttemptStarter
    {
        private const string LOG_PREFIX = "[EnhancementAttemptStarter]";

        private readonly EnhancementCommitOrchestrator _orchestrator;
        private readonly IClientMessageOutbox _outbox;
        private readonly IOwnerInventorySyncHold _hold;
        private readonly IEnhancementRequestDedupLookup _dedupLookup;

        // Characters whose owner-sync hold one of this starter's attempts opened and has not ended yet.
        private readonly HashSet<CharacterID> _holdOwners = new HashSet<CharacterID>();

        /// <summary>Creates the starter over its collaborators.</summary>
        /// <param name="orchestrator">Runs the attempt and its commit; not null.</param>
        /// <param name="outbox">Server-to-client send seam; not null.</param>
        /// <param name="hold">The owner inventory sync hold (CR-ENH-11); not null.</param>
        /// <param name="dedupLookup">Finds a character's deduplicator (EC-NET-9); not null.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public EnhancementAttemptStarter(
            EnhancementCommitOrchestrator orchestrator,
            IClientMessageOutbox outbox,
            IOwnerInventorySyncHold hold,
            IEnhancementRequestDedupLookup dedupLookup)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _hold = hold ?? throw new ArgumentNullException(nameof(hold));
            _dedupLookup = dedupLookup ?? throw new ArgumentNullException(nameof(dedupLookup));
        }

        /// <summary>
        /// Starts the attempt (CR-ENH-15 steps 3 and 4 onward): opens the owner-sync hold, then calls
        /// <see cref="EnhancementCommitOrchestrator.Begin(uint, CharacterID, int, int, Action, Action{EnhancementAttemptResult}, Action, out EnhancementResultCode)"/>.
        /// Accepted: one cap-exempt <see cref="EnhancementRequestReceived"/> now, one cap-exempt
        /// <see cref="EnhancementAttemptResultMessage"/> after the commit (and nothing on a failed commit). Rejected at step 4
        /// or by re-validation: the hold is released and one cap-exempt rejection result is sent, with no acknowledgment.
        /// Another write in flight: one cap-exempt <see cref="EnhancementResultCode.RejectedConcurrentAttempt"/> result.
        /// </summary>
        /// <param name="clientId">The requesting connection; receives the acknowledgment and the result.</param>
        /// <param name="characterId">The requesting character.</param>
        /// <param name="requestId">The client's <c>requestId</c> (EC-NET-9); recorded after a committed attempt.</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance.</param>
        /// <param name="scrollSlotIndex">Bag slot of the scroll.</param>
        /// <exception cref="Exception">An exception from the orchestrator (see its documentation) is rethrown after the hold
        /// has been released, if it was still open.</exception>
        public void StartAttempt(uint clientId, CharacterID characterId, uint requestId, int itemSlotIndex, int scrollSlotIndex)
        {
            // An attempt of this character already holding the hold keeps it; this call then never touches it.
            bool ownsHold = _holdOwners.Add(characterId);
            var call = new Call(this, clientId, characterId, requestId, itemSlotIndex, ownsHold);
            if (ownsHold)
            {
                try
                {
                    _hold.OpenHold(characterId);
                }
                catch
                {
                    _holdOwners.Remove(characterId);
                    throw;
                }
            }

            IrreversibleOutcomeBeginResult result;
            EnhancementResultCode rejectionCode;
            try
            {
                result = _orchestrator.Begin(
                    clientId,
                    characterId,
                    itemSlotIndex,
                    scrollSlotIndex,
                    call.EmitAcknowledgment,
                    call.DeliverResult,
                    call.OnCommitFailed,
                    out rejectionCode);
            }
            catch (Exception)
            {
                call.EndHoldIfOpen(discard: false);
                throw;
            }

            switch (result)
            {
                case IrreversibleOutcomeBeginResult.RejectedInvalidRequest:
                    call.EndHoldIfOpen(discard: false);
                    call.SendResultMessage(rejectionCode, 0);
                    break;

                case IrreversibleOutcomeBeginResult.RejectedWriteInFlight:
                    // Another irreversible write is in flight; the request is rejected, never queued (ADR-014 Decision 3).
                    call.EndHoldIfOpen(discard: false);
                    call.SendResultMessage(EnhancementResultCode.RejectedConcurrentAttempt, 0);
                    break;

                case IrreversibleOutcomeBeginResult.PersistenceFailed:
                    // onCommitFailed already discarded the hold; the client is disconnected (CR-NET-5.5).
                    break;

                default:
                    // Started: the acknowledgment is out; the result follows from the completion queue.
                    break;
            }
        }

        // The state of ONE attempt and the callbacks handed to the orchestrator for it.
        private sealed class Call
        {
            private readonly EnhancementAttemptStarter _owner;
            private readonly uint _clientId;
            private readonly CharacterID _characterId;
            private readonly uint _requestId;
            private readonly int _itemSlotIndex;
            private readonly bool _ownsHold;
            private bool _holdEnded;

            public Call(EnhancementAttemptStarter owner, uint clientId, CharacterID characterId, uint requestId, int itemSlotIndex, bool ownsHold)
            {
                _owner = owner;
                _clientId = clientId;
                _characterId = characterId;
                _requestId = requestId;
                _itemSlotIndex = itemSlotIndex;
                _ownsHold = ownsHold;
            }

            // CR-ENH-15 step 4 done: acknowledge. Cap-exempt (CR-NET-7.7, AC-NC-35).
            public void EmitAcknowledgment()
            {
                Span<byte> buffer = stackalloc byte[EnhancementRequestReceived.BodySize];
                int written = EnhancementRequestReceivedCodec.WriteBody(buffer, _requestId, (byte)_itemSlotIndex);
                _owner._outbox.Enqueue(_clientId, EnhancementRequestReceived.MessageTypeId, buffer.Slice(0, written), isCapExempt: true);
            }

            // The write succeeded and CompleteAttempt ran. Never throws (see the class remarks).
            public void DeliverResult(EnhancementAttemptResult result)
            {
                try
                {
                    RecordCommittedRequestId();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }

                try
                {
                    SendResultMessage(result.ResultCode, result.NewLevel);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    // The hold ends even if an enqueue threw. EndHoldIfOpen logs instead of throwing.
                    EndHoldIfOpen(discard: false);
                }
            }

            // The write failed or could not start; the attempt was rolled back. CR-NET-5.5: the client is disconnected and
            // gets an InventoryFullSync on its next zone entry, so nothing is sent and the held updates are dropped.
            public void OnCommitFailed()
            {
                EndHoldIfOpen(discard: true);
            }

            public void SendResultMessage(EnhancementResultCode resultCode, byte newLevel)
            {
                Span<byte> buffer = stackalloc byte[EnhancementAttemptResultMessage.BodySize];
                int written = EnhancementAttemptResultMessageCodec.WriteBody(buffer, _requestId, resultCode, newLevel);
                _owner._outbox.Enqueue(_clientId, EnhancementAttemptResultMessage.MessageTypeId, buffer.Slice(0, written), isCapExempt: true);
            }

            // Releases (or discards) the hold once, and only a hold this call opened. Never throws: an exception from the hold is logged.
            public void EndHoldIfOpen(bool discard)
            {
                if (!_ownsHold || _holdEnded)
                {
                    return;
                }

                _holdEnded = true;
                _owner._holdOwners.Remove(_characterId);
                try
                {
                    if (discard)
                    {
                        _owner._hold.DiscardHold(_characterId);
                    }
                    else
                    {
                        _owner._hold.ReleaseHold(_characterId);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            private void RecordCommittedRequestId()
            {
                if (_owner._dedupLookup.TryGetDeduplicator(_characterId, out EnhancementRequestDeduplicator deduplicator)
                    && deduplicator != null)
                {
                    deduplicator.RecordCommitted(_requestId);
                    return;
                }

                Debug.LogWarning($"{LOG_PREFIX} NoDeduplicator: clientId={_clientId} character={_characterId} " +
                    $"requestId={_requestId} - LastEnhancementRequestID not recorded; the result is still sent.");
            }
        }
    }
}
