using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// One row of the unified MCR-2/CCR-3 routing table (Story 025) — the single source of truth
    /// every message-sending call site must consult for a given <c>MessageTypeID</c>'s pillar tag(s),
    /// delivery guarantee/channel, direction, and delivery context. See
    /// <see cref="MessageRoutingRegistry"/> for the concrete table and lookup/resolution API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Unified, not two tables:</b> <c>networking-message-criticality.md</c> (MCR-2: pillar +
    /// guarantee) and <c>networking-channel-contract.md</c> (CCR-3: direction + channel + context)
    /// describe the same set of messages from two angles. Story 025's Implementation Notes direct
    /// merging them into one authoritative structure — this struct is that merge. A row that exists
    /// in this registry therefore satisfies both AC-MCR-04 (every wire <c>MessageTypeID</c> has an
    /// MCR-2 row) and AC-CCR-09 (every MCR-2 message has a CCR-3 entry) simultaneously, by
    /// construction — there is no way to add a row here without also supplying its CCR-3 fields.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var row = new MessageRoutingEntry(
    ///     messageTypeId: GoldSyncEvent.MessageTypeId,
    ///     messageName: nameof(GoldSyncEvent),
    ///     pillars: DesignPillar.EarnedPower,
    ///     channel: NetworkChannel.ReliableUnordered,
    ///     direction: MessageDirection.ServerToOwningClient,
    ///     deliveryContext: MessageDeliveryContext.ReliableUnorderedBatch,
    ///     isMcr3Exception: true,
    ///     rationale: "Self-correcting; forced R-OD fallback after GOLD_MAX_CONSECUTIVE_DROP (MCR-4).");
    /// </code>
    /// </example>
    public readonly struct MessageRoutingEntry : IEquatable<MessageRoutingEntry>
    {
        /// <summary>The wire <c>MessageTypeID</c> this row describes.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>The message type's name (e.g. <c>"GoldSyncEvent"</c>), for diagnostics and anomaly logging.</summary>
        public readonly string MessageName;

        /// <summary>The MCR-1 design pillar(s) this message serves. May be more than one.</summary>
        public readonly DesignPillar Pillars;

        /// <summary>The resolved channel this message is sent on (post-MCR-3 resolution, if applicable).</summary>
        public readonly NetworkChannel Channel;

        /// <summary>The CCR-3 direction code.</summary>
        public readonly MessageDirection Direction;

        /// <summary>The CCR-3 delivery-context code.</summary>
        public readonly MessageDeliveryContext DeliveryContext;

        /// <summary>
        /// <see langword="true"/> if this row is one of the MCR-3 "self-correcting exception" rows
        /// (<c>GoldSyncEvent</c>, <c>LootBidUpdate</c>, <c>PartyMemberHealthUpdate</c>) — the channel
        /// above is <i>not</i> derived by taking the max guarantee across <see cref="Pillars"/>; it is
        /// the explicitly documented override channel instead. See
        /// <see cref="MessageRoutingRegistry.ResolveMultiPillarChannel"/>.
        /// </summary>
        /// <remarks>
        /// <b>GDD looseness, documented honestly:</b> AC-MCR-06 frames all three exceptions as
        /// "given any message tagged with multiple pillars," but <c>GoldSyncEvent</c>'s own MCR-2 row
        /// is single-pillar (Pillar 1 only) — MCR-3's own rule text (line 119 of
        /// <c>networking-message-criticality.md</c>) independently requires documenting the exception
        /// "even when tagged with a Pillar 1 classification" (no "multiple pillars" qualifier there).
        /// This flag follows AC-MCR-06's explicit enumeration of the three exception names verbatim
        /// rather than its looser "multi-pillar" framing — the resulting channel is correct either
        /// way (co-verified against the GDD by the technical-director).
        /// </remarks>
        public readonly bool IsMcr3Exception;

        /// <summary>Free-text rationale, mirroring the MCR-2 table's Rationale column. Documentation only — never consulted at runtime.</summary>
        public readonly string Rationale;

        /// <summary>Initializes a new <see cref="MessageRoutingEntry"/>.</summary>
        public MessageRoutingEntry(ushort messageTypeId, string messageName, DesignPillar pillars,
            NetworkChannel channel, MessageDirection direction, MessageDeliveryContext deliveryContext,
            bool isMcr3Exception, string rationale)
        {
            MessageTypeId = messageTypeId;
            MessageName = messageName ?? throw new ArgumentNullException(nameof(messageName));
            Pillars = pillars;
            Channel = channel;
            Direction = direction;
            DeliveryContext = deliveryContext;
            IsMcr3Exception = isMcr3Exception;
            Rationale = rationale ?? throw new ArgumentNullException(nameof(rationale));
        }

        /// <inheritdoc/>
        public bool Equals(MessageRoutingEntry other)
            => MessageTypeId == other.MessageTypeId
            && MessageName == other.MessageName
            && Pillars == other.Pillars
            && Channel == other.Channel
            && Direction == other.Direction
            && DeliveryContext == other.DeliveryContext
            && IsMcr3Exception == other.IsMcr3Exception
            && Rationale == other.Rationale;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MessageRoutingEntry other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = MessageTypeId.GetHashCode();
            h = (h * 397) ^ (MessageName?.GetHashCode() ?? 0);
            h = (h * 397) ^ Pillars.GetHashCode();
            h = (h * 397) ^ Channel.GetHashCode();
            h = (h * 397) ^ Direction.GetHashCode();
            h = (h * 397) ^ DeliveryContext.GetHashCode();
            h = (h * 397) ^ IsMcr3Exception.GetHashCode();
            h = (h * 397) ^ (Rationale?.GetHashCode() ?? 0);
            return h;
        }

        /// <summary>Returns true if both rows have identical field values.</summary>
        public static bool operator ==(MessageRoutingEntry left, MessageRoutingEntry right) => left.Equals(right);

        /// <summary>Returns true if the rows differ in any field.</summary>
        public static bool operator !=(MessageRoutingEntry left, MessageRoutingEntry right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"MessageRoutingEntry(Id=0x{MessageTypeId:X4}, Name={MessageName}, Pillars={Pillars}, Channel={Channel}, Direction={Direction}, Context={DeliveryContext}, Mcr3Exception={IsMcr3Exception})";
    }
}
