using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// The unified MCR-2/CCR-3 message-routing table (Story 025) — the single authoritative data
    /// structure every message-sending call site must consult for a <c>MessageTypeID</c>'s pillar
    /// tag(s), delivery guarantee/channel, direction, and delivery context. Do not hardcode a channel
    /// choice for any message type anywhere else in the codebase; add or look up a row here instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scope of the current table (Out of Scope per Story 025):</b> this registry contains one row
    /// per <c>MessageTypeID</c> that already exists as a concrete schema class in
    /// <c>src/Foundation/Networking/WireProtocol/</c> today — <see cref="HeartbeatMessage"/>,
    /// <see cref="DamageEvent"/>, <see cref="CycleTimerBroadcast"/>, <see cref="EntityPositionUpdate"/>,
    /// and <see cref="GoldSyncEvent"/>. Every other message named in <c>networking-message-criticality.md</c>
    /// / <c>networking-channel-contract.md</c> (e.g. <c>RttProbe</c>/<c>RttProbeEcho</c>,
    /// <c>SessionHandshake</c>, <c>KillEvent</c>, <c>LootBidUpdate</c>, ...) belongs to a system not yet
    /// implemented in this codebase — its owning story adds a row here when its schema class is
    /// written, per this document's own MCR-2 rule ("a new message requires a row here before its
    /// schema is accepted").
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// MessageRoutingResult result = MessageRoutingRegistry.ValidateAndRoute(
    ///     DamageEvent.MessageTypeId, isDevelopmentBuild: BuildConfiguration.IsDevelopmentBuild);
    /// // result.WasClassified == true, result.RoutedChannel == NetworkChannel.ReliableUnordered
    /// </code>
    /// </example>
    public static class MessageRoutingRegistry
    {
        private static readonly Dictionary<ushort, MessageRoutingEntry> _entries = BuildEntries();

        /// <summary>Every row currently in the registry, in no particular order.</summary>
        public static IReadOnlyCollection<MessageRoutingEntry> AllEntries => _entries.Values;

        /// <summary>Looks up the registry row for <paramref name="messageTypeId"/>, if one exists.</summary>
        public static bool TryGetEntry(ushort messageTypeId, out MessageRoutingEntry entry)
            => _entries.TryGetValue(messageTypeId, out entry);

        private static Dictionary<ushort, MessageRoutingEntry> BuildEntries()
        {
            var map = new Dictionary<ushort, MessageRoutingEntry>();

            void Add(MessageRoutingEntry entry) => map.Add(entry.MessageTypeId, entry);

            Add(new MessageRoutingEntry(
                messageTypeId: HeartbeatMessage.MessageTypeId,
                messageName: nameof(HeartbeatMessage),
                pillars: DesignPillar.Infrastructure,
                channel: NetworkChannel.Unreliable,
                direction: MessageDirection.ClientToServer,
                deliveryContext: MessageDeliveryContext.Standalone,
                isMcr3Exception: false,
                rationale: "Keepalive; dropped heartbeat self-corrects on the next one (MCR-2, CR-NET-7.10)."));

            Add(new MessageRoutingEntry(
                messageTypeId: DamageEvent.MessageTypeId,
                messageName: nameof(DamageEvent),
                pillars: DesignPillar.RhythmMastery,
                channel: NetworkChannel.ReliableUnordered,
                direction: MessageDirection.ServerToAllZoneClients,
                deliveryContext: MessageDeliveryContext.ReliableUnorderedBatch,
                isMcr3Exception: false,
                rationale: "Cosmetic for non-attacker clients; best-effort, high-frequency (MCR-2). Distinct from SelfDamageEvent (R-OD, not yet implemented)."));

            Add(new MessageRoutingEntry(
                messageTypeId: CycleTimerBroadcast.MessageTypeId,
                messageName: nameof(CycleTimerBroadcast),
                pillars: DesignPillar.RhythmMastery,
                channel: NetworkChannel.Unreliable,
                direction: MessageDirection.ServerToAllZoneClients,
                deliveryContext: MessageDeliveryContext.CycleBroadcastPacket,
                isMcr3Exception: false,
                rationale: "Fresh value every tick; client interpolates for up to 3 lost packets (MCR-2, MCR-5a)."));

            Add(new MessageRoutingEntry(
                messageTypeId: EntityPositionUpdate.MessageTypeId,
                messageName: nameof(EntityPositionUpdate),
                pillars: DesignPillar.SocialSignals,
                channel: NetworkChannel.Unreliable,
                direction: MessageDirection.ServerToAllZoneClients,
                deliveryContext: MessageDeliveryContext.PositionPacket,
                isMcr3Exception: false,
                rationale: "Position self-corrects every tick; clients interpolate stale values (MCR-2)."));

            Add(new MessageRoutingEntry(
                messageTypeId: GoldSyncEvent.MessageTypeId,
                messageName: nameof(GoldSyncEvent),
                pillars: DesignPillar.EarnedPower,
                channel: NetworkChannel.ReliableUnordered,
                direction: MessageDirection.ServerToOwningClient,
                deliveryContext: MessageDeliveryContext.ReliableUnorderedBatch,
                isMcr3Exception: true,
                rationale: "MCR-3 exception: version-based self-correction on next tick; the ultimate economic outcome is " +
                           "guaranteed by a forced standalone R-OD GoldSyncEvent after GOLD_MAX_CONSECUTIVE_DROP consecutive " +
                           "drops (MCR-4) — the R-U copy itself carries no unrecoverable economic authority."));

            Add(new MessageRoutingEntry(
                messageTypeId: EntityHealthUpdate.MessageTypeId,
                messageName: nameof(EntityHealthUpdate),
                pillars: DesignPillar.RhythmMastery | DesignPillar.SocialSignals,
                channel: NetworkChannel.ReliableUnordered,
                direction: MessageDirection.ServerToRelevantClients,
                deliveryContext: MessageDeliveryContext.ReliableUnorderedBatch,
                isMcr3Exception: false,
                rationale: "Tactical combat feedback (Pillar 2) + party-visible health bar data (Pillar 3); self-corrects " +
                           "on next tick. Both tagged pillars independently require R-U, so max_guarantee is R-U with no " +
                           "MCR-3 exception needed. Relevance-filtered per networking-relevance-filter.md (Story 028)."));

            Add(new MessageRoutingEntry(
                messageTypeId: PartyMemberHealthUpdate.MessageTypeId,
                messageName: nameof(PartyMemberHealthUpdate),
                pillars: DesignPillar.EarnedPower | DesignPillar.SocialSignals,
                channel: NetworkChannel.ReliableUnordered,
                direction: MessageDirection.ServerToOwningClient,
                deliveryContext: MessageDeliveryContext.ReliableUnorderedBatch,
                isMcr3Exception: true,
                rationale: "CCR-3 direction is literally 'S→C (per-client for party)', not S→PARTY — each client " +
                           "receives its own tailored 0-3 entries about its own party's other members " +
                           "(RelevanceFilter.BuildHealthUpdateSubMessages), never a single payload broadcast " +
                           "verbatim to every party member (that is MessageDirection.ServerToParty's semantic, " +
                           "used by genuinely identical-content party broadcasts like LootBidUpdate). " +
                           "MCR-3 exception: party HP bars are fellowship-critical (Pillar 3); XP-eligibility/death-state " +
                           "ordering nominally implies Pillar 1's R-OD, but the authoritative death signal is " +
                           "GhostPromotionEvent (R-OD) — HP=0 here is only a precursor indicator, and HP data itself " +
                           "self-corrects on next tick, so the R-U copy carries no unrecoverable ordering authority."));

            Add(new MessageRoutingEntry(
                messageTypeId: GoldSyncEventForcedDelivery.MessageTypeId,
                messageName: nameof(GoldSyncEventForcedDelivery),
                pillars: DesignPillar.EarnedPower,
                channel: NetworkChannel.ReliableOrdered,
                direction: MessageDirection.ServerToOwningClient,
                deliveryContext: MessageDeliveryContext.PriorityPath,
                isMcr3Exception: false,
                rationale: "MCR-4's forced-delivery escalation of GoldSyncEvent after GOLD_MAX_CONSECUTIVE_DROP " +
                           "consecutive R-U overflow-drops (Story 026) — the same logical message as GoldSyncEvent, " +
                           "delivered via the R-OD priority path instead of the R-U batch. Channel is R-OD by MCR-4's " +
                           "own rule, not resolved via MCR-3 pillar-conflict logic, so no exception flag applies."));

            Add(new MessageRoutingEntry(
                messageTypeId: SelfDamageEvent.MessageTypeId,
                messageName: nameof(SelfDamageEvent),
                pillars: DesignPillar.RhythmMastery,
                channel: NetworkChannel.ReliableOrdered,
                direction: MessageDirection.ServerToOwningClient,
                deliveryContext: MessageDeliveryContext.PriorityPath,
                isMcr3Exception: false,
                rationale: "Direct feedback for the attacker's own successful timing window (Pillar 2); must arrive " +
                           "once, in order, sent only to the attacker's own client (Story 027). Single-pillar R-OD — " +
                           "no MCR-3 conflict to resolve."));

            return map;
        }

        /// <summary>
        /// MCR-3's multi-pillar resolution rule as a pure function: the highest-guarantee channel
        /// among <paramref name="taggedPillarChannels"/> wins, unless <paramref name="isDocumentedException"/>
        /// is <see langword="true"/>, in which case <paramref name="documentedExceptionChannel"/> is
        /// returned verbatim (the three named MCR-3 exceptions — <c>GoldSyncEvent</c>,
        /// <c>LootBidUpdate</c>, <c>PartyMemberHealthUpdate</c> — stay on their self-correcting
        /// channel rather than escalating).
        /// </summary>
        /// <param name="taggedPillarChannels">
        /// The delivery-guarantee channel each pillar tag independently requires for this specific
        /// message (per MCR-1's per-pillar guarantee table). Must contain at least one element.
        /// </param>
        /// <param name="isDocumentedException">
        /// <see langword="true"/> if this message is a documented MCR-3 exception row (see
        /// <see cref="MessageRoutingEntry.IsMcr3Exception"/>).
        /// </param>
        /// <param name="documentedExceptionChannel">
        /// Required when <paramref name="isDocumentedException"/> is <see langword="true"/> — the
        /// explicitly documented override channel (e.g. <see cref="NetworkChannel.ReliableUnordered"/>
        /// for <c>GoldSyncEvent</c>).
        /// </param>
        /// <example>
        /// <code>
        /// // KillEvent-shape: Pillar 1 (R-OD) + Pillar 3 (R-OD), no exception -&gt; R-OD.
        /// NetworkChannel killEvent = MessageRoutingRegistry.ResolveMultiPillarChannel(
        ///     new[] { NetworkChannel.ReliableOrdered, NetworkChannel.ReliableOrdered }, isDocumentedException: false);
        ///
        /// // GoldSyncEvent: Pillar 1, documented exception -&gt; stays R-U.
        /// NetworkChannel goldSync = MessageRoutingRegistry.ResolveMultiPillarChannel(
        ///     new[] { NetworkChannel.ReliableUnordered }, isDocumentedException: true, NetworkChannel.ReliableUnordered);
        /// </code>
        /// </example>
        public static NetworkChannel ResolveMultiPillarChannel(
            IReadOnlyList<NetworkChannel> taggedPillarChannels,
            bool isDocumentedException,
            NetworkChannel? documentedExceptionChannel = null)
        {
            if (taggedPillarChannels == null)
            {
                throw new ArgumentNullException(nameof(taggedPillarChannels));
            }

            if (taggedPillarChannels.Count == 0)
            {
                throw new ArgumentException("At least one tagged pillar channel must be supplied.", nameof(taggedPillarChannels));
            }

            if (isDocumentedException)
            {
                if (!documentedExceptionChannel.HasValue)
                {
                    throw new ArgumentException(
                        "documentedExceptionChannel must be supplied when isDocumentedException is true.",
                        nameof(documentedExceptionChannel));
                }

                return documentedExceptionChannel.Value;
            }

            NetworkChannel max = taggedPillarChannels[0];
            for (int i = 1; i < taggedPillarChannels.Count; i++)
            {
                if (taggedPillarChannels[i] > max)
                {
                    max = taggedPillarChannels[i];
                }
            }

            return max;
        }

        /// <summary>
        /// EC-MCR-1 / AC-MCR-03: validates that <paramref name="messageTypeId"/> has a registry row
        /// before "registering a handler" for it. A registered ID returns its classified
        /// <see cref="MessageRoutingResult"/> unconditionally (no debug/release distinction applies
        /// to already-classified messages). An unregistered ID behaves per <paramref name="isDevelopmentBuild"/>:
        /// in a debug build, raises <see cref="PendingSchemaDispatchException"/>; in a release build,
        /// returns an R-U fallback result and (test/dev-build-only) notifies <paramref name="observer"/>'s
        /// <c>OnUnclassifiedMessageTypeLogged</c> callback. No crash occurs in either configuration —
        /// this method either returns normally or throws a specific, documented exception; it never
        /// lets an unregistered ID silently disappear.
        /// </summary>
        /// <param name="messageTypeId">The <c>MessageTypeID</c> being registered/dispatched.</param>
        /// <param name="isDevelopmentBuild">
        /// Whether the running build is a debug build (see <see cref="BuildConfiguration.IsDevelopmentBuild"/>
        /// remarks for why this is an explicit parameter rather than an internal <c>#if</c> branch).
        /// </param>
        /// <example>
        /// <code>
        /// MessageRoutingResult result = MessageRoutingRegistry.ValidateAndRoute(
        ///     messageTypeId: 0x9999, isDevelopmentBuild: BuildConfiguration.IsDevelopmentBuild);
        /// if (!result.WasClassified)
        /// {
        ///     // release build: routed to result.RoutedChannel (R-U), anomaly already logged.
        /// }
        /// </code>
        /// </example>
        public static MessageRoutingResult ValidateAndRoute(
            ushort messageTypeId,
            bool isDevelopmentBuild
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (TryGetEntry(messageTypeId, out MessageRoutingEntry entry))
            {
                return MessageRoutingResult.Classified(entry);
            }

            if (isDevelopmentBuild)
            {
                throw new PendingSchemaDispatchException(messageTypeId);
            }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnUnclassifiedMessageTypeLogged(messageTypeId);
#endif

            return MessageRoutingResult.UnclassifiedFallback(messageTypeId);
        }
    }
}
