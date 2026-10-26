using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// The request handler of <see cref="EquipRequest"/> (Equipment Story 008; equipment-system.md CR-EQS-13 and the
    /// <c>EquipResult</c> → wire mapping table; ADR-014 Decision 3: one descriptor and one handler per type). Decodes
    /// the body, calls <see cref="IEquipmentService.Equip"/> or <see cref="IEquipmentService.Unequip"/> on the tick
    /// thread and enqueues one <see cref="EquipResultMessage"/> to the requesting connection only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Routing</b>: <c>itemId = 0</c> → <c>Unequip(charId, gearSlot)</c> (<c>inventorySlot</c> ignored); otherwise
    /// <c>Equip(charId, gearSlot, itemId, inventorySlot)</c>. The handler does not pre-validate <c>gearSlot</c> or the
    /// bag slot: the service's own guards answer (Story 003). The only check here is the body length.
    /// </para>
    /// <para>
    /// <b>Slot echo</b>: the result's slot fields are read from the service <b>after</b> the call, so they show the new
    /// contents on success and the unchanged slot on <c>NoChange</c> and every failure. A <c>gearSlot</c> outside
    /// <c>[0, EquipmentService.GEAR_SLOT_COUNT)</c> is echoed as empty (item 0, level 0) without reading the service,
    /// which would log an error.
    /// </para>
    /// <para>
    /// <b>Retransmits</b>: per character, the last processed <c>requestId</c> and its encoded result are kept. A request
    /// whose id equals the stored one is a retransmit: the stored result is sent again and the service is not called
    /// (wire protocol <c>EquipRequest</c> note). Any other id, smaller or larger, is processed: the wire protocol does
    /// not say whether the client counter survives a reconnect, so a client that restarts its counter is still answered,
    /// and a replayed old request is answered by the service's own guards (a stale bag slot gives
    /// <c>ItemNotInInventory</c>). <see cref="ClearSession"/> forgets a character's entry at session end.
    /// </para>
    /// <para>Not thread-safe: tick thread only, like the dispatcher that calls it.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var handlers = new EquipmentClientRequestHandlers(equipment, outbox);
    /// EquipmentInboundRegistration.Register(dispatcher, handlers);
    /// // at session end:
    /// handlers.ClearSession(charId);
    /// </code>
    /// </example>
    public sealed class EquipmentClientRequestHandlers
    {
        private const string LOG_PREFIX = "[EquipmentClientRequestHandlers]";

        private readonly IEquipmentService _service;
        private readonly IClientMessageOutbox _outbox;

        // Last processed request per character (retransmit store). Tick thread only.
        private readonly Dictionary<CharacterID, ProcessedRequest> _lastProcessed = new Dictionary<CharacterID, ProcessedRequest>();

        private sealed class ProcessedRequest
        {
            public uint RequestId;
            public readonly byte[] EncodedResult = new byte[EquipResultMessage.BodySize];
        }

        /// <summary>Creates the handler over its collaborators.</summary>
        /// <param name="service">The Equipment System; not null.</param>
        /// <param name="outbox">Server-to-client send seam; not null.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public EquipmentClientRequestHandlers(IEquipmentService service, IClientMessageOutbox outbox)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <summary>Number of characters with a stored result (retransmit store).</summary>
        public int TrackedSessionCount => _lastProcessed.Count;

        /// <summary>
        /// Handles <see cref="EquipRequest"/>. A body that is not exactly <see cref="EquipRequest.BodySize"/> bytes is
        /// dropped with one <c>InboundMessageMalformed</c> warning, no service call and no reply (a longer body is
        /// already dropped by the dispatcher's bound). See the type remarks for routing, slot echo and retransmits.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 10-byte body.</param>
        public void HandleEquipRequest(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (!EquipRequestCodec.TryReadBody(body, out uint requestId, out byte gearSlot, out uint itemId, out byte inventorySlot))
            {
                Debug.LogWarning($"{LOG_PREFIX} InboundMessageMalformed: clientId={context.ClientId} " +
                    $"messageType=0x{context.MessageTypeId:X4} - body length {body.Length}, expected {EquipRequest.BodySize}.");
                return;
            }

            CharacterID charId = context.CharacterId;
            if (_lastProcessed.TryGetValue(charId, out ProcessedRequest last) && requestId == last.RequestId)
            {
                // Retransmit: answer from the store; Equip / Unequip is not called again.
                _outbox.Enqueue(context.ClientId, EquipResultMessage.MessageTypeId, last.EncodedResult, false);
                return;
            }

            var slot = (GearSlot)gearSlot;
            EquipResult result = itemId == EquipRequest.UnequipItemId
                ? _service.Unequip(charId, slot)
                : _service.Equip(charId, slot, new ItemID(itemId), inventorySlot);

            // Everything that can throw runs before the store is touched, so a throw leaves the previous entry intact.
            bool success = IsSuccess(result);
            EquipFailReason reason = ToWire(result);
            uint slotItemId = 0;
            byte slotLevel = 0;
            if (gearSlot < EquipmentService.GEAR_SLOT_COUNT)
            {
                slotItemId = _service.GetEquipmentSlotState(charId, slot).ItemId.RawValue;
                slotLevel = _service.GetSlotEnhancementLevel(charId, slot);
            }

            if (last == null)
            {
                last = new ProcessedRequest();
                _lastProcessed.Add(charId, last);
            }

            EquipResultMessageCodec.WriteBody(last.EncodedResult, requestId, success, reason, gearSlot, slotItemId, slotLevel);
            last.RequestId = requestId;
            _outbox.Enqueue(context.ClientId, EquipResultMessage.MessageTypeId, last.EncodedResult, false);
        }

        /// <summary>
        /// Forgets <paramref name="charId"/>'s stored request, so a new session's <c>requestId</c> counter is not taken
        /// for a retransmit. Called at session end (<c>SessionEndCoordinator</c> release). Unknown id: no-op.
        /// </summary>
        /// <param name="charId">The character whose session ended.</param>
        public void ClearSession(CharacterID charId)
        {
            _lastProcessed.Remove(charId);
        }

        /// <summary>
        /// <see langword="true"/> for <see cref="EquipResult.Success"/> and <see cref="EquipResult.NoChange"/>, the two
        /// values the wire reports as <c>success = true</c>.
        /// </summary>
        /// <param name="result">The service result.</param>
        public static bool IsSuccess(EquipResult result)
            => result == EquipResult.Success || result == EquipResult.NoChange;

        /// <summary>
        /// Maps a service <see cref="EquipResult"/> to its wire <see cref="EquipFailReason"/>, 1:1 by name
        /// (OQ-EQS-9 option a): <c>Success</c> and <c>NoChange</c> → <see cref="EquipFailReason.None"/>.
        /// </summary>
        /// <param name="result">The service result.</param>
        /// <returns>The wire reason.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="result"/> is not a defined value.</exception>
        public static EquipFailReason ToWire(EquipResult result)
        {
            switch (result)
            {
                case EquipResult.Success:
                case EquipResult.NoChange:
                    return EquipFailReason.None;
                case EquipResult.StatRequirementNotMet:
                    return EquipFailReason.StatRequirementNotMet;
                case EquipResult.InventoryFull:
                    return EquipFailReason.InventoryFull;
                case EquipResult.SlotMismatch:
                    return EquipFailReason.SlotMismatch;
                case EquipResult.ItemLocked:
                    return EquipFailReason.ItemLocked;
                case EquipResult.ItemNotInInventory:
                    return EquipFailReason.ItemNotInInventory;
                case EquipResult.InventoryError:
                    return EquipFailReason.InventoryError;
                case EquipResult.CriticalFailure:
                    return EquipFailReason.CriticalFailure;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result, "No wire mapping for this EquipResult.");
            }
        }
    }
}
