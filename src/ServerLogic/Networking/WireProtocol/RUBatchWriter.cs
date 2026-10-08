using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Serializes one client's per-tick Path 2a Reliable-Unordered (R-U) batch packet
    /// (CR-NET-7.7, <c>TICK_BATCH_RU = 0x0101</c>): the 12-byte batch header followed by every
    /// included sub-message, in <see cref="RUBatchCategory"/> canonical write order, subject to
    /// the <see cref="MAX_MESSAGE_BODY_BYTES"/> budget (CR-NET-7.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two overflow policies apply, in this order:</b>
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <b>DamageEvent intra-class overflow</b> (applied first, before anything else): at most
    /// <c>⌊(MAX_MESSAGE_BODY_BYTES − 12) / DamageEvent.BatchSize⌋</c> = 27 <see cref="DamageEvent"/>
    /// entries are held; excess is dropped oldest-first. This assumes <c>damageEvents</c> is
    /// supplied in chronological (oldest-first) order — the same FIFO-by-caller-order convention
    /// <see cref="PriorityPathQueue{T}"/> uses for its own enqueue order — since this class has no
    /// independent tick-sequence field to sort by (the GDD's own "lowest tick-sequence number
    /// dropped first" language is honored by treating list order as sequence order). Logs a
    /// <c>DamageEventIntraclassOverflow</c> anomaly (<see cref="Debug.LogWarning"/>) with the drop
    /// count and tick number when triggered. <see cref="RUBatchCategory.DamageEvent"/> is never
    /// evicted as a whole category by policy 2 below — this intra-class cap is what protects it.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Category-level overflow eviction</b> (applied second, only if the batch — including the
    /// already-bounded DamageEvent set — still exceeds <see cref="MAX_MESSAGE_BODY_BYTES"/>):
    /// whole categories are evicted, lowest priority first, in this order until the batch fits:
    /// <see cref="RUBatchCategory.LootBidUpdate"/> → <see cref="RUBatchCategory.SkillCooldownUpdate"/>
    /// → <see cref="RUBatchCategory.GoldSyncEvent"/> → <see cref="RUBatchCategory.SkillCastResult"/>
    /// → <see cref="RUBatchCategory.SelfPositionUpdate"/> → <see cref="RUBatchCategory.EntityHealthUpdate"/>
    /// → <see cref="RUBatchCategory.PartyMemberHealthUpdate"/> → <see cref="RUBatchCategory.ConnectionQualityUpdate"/>.
    /// This drop order is deliberately <i>not</i> the reverse of the write order (e.g.
    /// <see cref="RUBatchCategory.ConnectionQualityUpdate"/> is written last but is also dropped
    /// last) — the GDD specifies the two orderings independently, not derived from one another.
    /// <b>Eviction is atomic per category</b>: the GDD numbers the drop order as 8 discrete steps
    /// ("1. LootBidUpdate — dropped first", etc.), each removing an entire category's
    /// sub-messages as one group — not a partial/interleaved per-message drop across categories.
    /// This implementation adopts that atomic-category reading (the same kind of judgment call
    /// <see cref="PriorityPathQueue{T}"/> documented for its own ambiguous GDD passage). The
    /// forced-R-OD-delivery escalation after <c>GOLD_MAX_CONSECUTIVE_DROP</c> consecutive
    /// <see cref="RUBatchCategory.GoldSyncEvent"/> drops (Story 026) is explicitly out of scope —
    /// this class drops <see cref="RUBatchCategory.GoldSyncEvent"/> exactly like any other
    /// category. A single <c>BatchOverflow</c> anomaly (<see cref="Debug.LogWarning"/>) is logged
    /// per <see cref="Write"/> call if any category is dropped — no cross-tick streak tracking
    /// (the GDD's "3 consecutive ticks" escalation threshold is a display/alerting nuance out of
    /// scope for this story's blocking ACs).
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>Concrete vs. opaque categories:</b> <see cref="RUBatchCategory.DamageEvent"/> and
    /// <see cref="RUBatchCategory.GoldSyncEvent"/> have concrete typed schemas in this codebase
    /// (<see cref="DamageEvent"/>, <see cref="GoldSyncEvent"/>) and are supplied via their own
    /// dedicated typed list parameters — <i>not</i> via <paramref name="otherSubMessages"/> below.
    /// <see cref="Write"/> throws <see cref="ArgumentException"/> if a <see cref="PendingSubMessage"/>
    /// tagged with either of those two categories is found in <c>otherSubMessages</c>. The
    /// remaining seven categories flow through <see cref="PendingSubMessage"/> as opaque,
    /// already-serialized payloads (see <see cref="PendingSubMessage"/> remarks) — no concrete
    /// schema is invented for systems not yet implemented in this codebase (Skill System, Party
    /// System, Loot Table System, Client-Side Prediction, relevance filtering).
    /// </para>
    /// <para>
    /// <b>The test/dev-build observer's <c>OnServerGoldSyncBatched</c> hook is invoked at the point
    /// each <see cref="GoldSyncEvent"/> is actually written into the destination buffer</b> — i.e.
    /// after the category-level eviction decision, only for entries that survive (if
    /// <see cref="RUBatchCategory.GoldSyncEvent"/> is evicted this tick, the hook does not fire
    /// for the dropped entries). <paramref name="observer"/> is optional/nullable — production
    /// call sites that have no observer wired pass <see langword="null"/>, matching this
    /// codebase's established nullable-observer convention. (Not naming the observer's interface
    /// type directly in this comment is deliberate — see the release-stripping remarks on
    /// <see cref="Write"/> itself.)
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = new byte[600];
    /// int bytesWritten = RUBatchWriter.Write(
    ///     buffer, sequenceNumber: 42u, tickNumber: 1000u,
    ///     damageEvents: myDamageEvents, goldSyncEvents: myGoldSyncEvents, otherSubMessages: myOtherMessages,
    ///     clientIdForLogging: 7u, observer: null);
    /// </code>
    /// </example>
    public static class RUBatchWriter
    {
        /// <summary>
        /// Maximum message body budget, in bytes, INCLUDING the 12-byte batch header
        /// (CR-NET-7.6). A compile-time structural constant, not read from external config,
        /// matching this folder's other wire-protocol constants (e.g.
        /// <see cref="PriorityPathQueue{T}.PRIORITY_PATH_CAP"/>).
        /// </summary>
        public const int MAX_MESSAGE_BODY_BYTES = 512;

        /// <summary>Batch-header <c>MessageTypeID</c> for the per-tick R-U batch packet (Path 2a, CR-NET-7.7).</summary>
        public const ushort TICK_BATCH_RU = 0x0101;

        /// <summary>
        /// The maximum number of <see cref="DamageEvent"/> entries held in a single batch:
        /// <c>⌊(MAX_MESSAGE_BODY_BYTES − BatchHeaderCodec.HeaderSize) / DamageEvent.BatchSize⌋</c>
        /// = ⌊500/18⌋ = 27 (CR-NET-7.6's DamageEvent intra-class overflow policy).
        /// </summary>
        public const int DamageEventIntraclassCap = (MAX_MESSAGE_BODY_BYTES - BatchHeaderCodec.HeaderSize) / DamageEvent.BatchSize;

        /// <summary>
        /// A single opaque sub-message's total wire size (<see cref="BatchSubMessageFraming.SubMessageHeaderSize"/>
        /// + payload) must never exceed this many bytes, guaranteeing it always fits in a fresh,
        /// empty batch (<c>MAX_MESSAGE_BODY_BYTES − BatchHeaderCodec.HeaderSize − SubMessageHeaderSize</c>
        /// = 512 − 12 − 4 = 496; equivalently the GDD's "<c>MessageTypeID + payload</c> must never
        /// exceed <c>MAX_MESSAGE_BODY_BYTES − 14</c>" = 498, minus the 2-byte length prefix already
        /// counted separately here). <see cref="Write"/> enforces this as a caller sizing bug
        /// (throws), not a network condition — mirrors the zero-write-guard precedent in
        /// <see cref="WireIdCodec"/>.
        /// </summary>
        public const int MaxSingleOpaqueSubMessagePayloadBytes = MAX_MESSAGE_BODY_BYTES - BatchHeaderCodec.HeaderSize - BatchSubMessageFraming.SubMessageHeaderSize;

        private static readonly RUBatchCategory[] WriteOrder =
        {
            RUBatchCategory.DamageEvent,
            RUBatchCategory.EntityHealthUpdate,
            RUBatchCategory.PartyMemberHealthUpdate,
            RUBatchCategory.SelfPositionUpdate,
            RUBatchCategory.SkillCastResult,
            RUBatchCategory.GoldSyncEvent,
            RUBatchCategory.SkillCooldownUpdate,
            RUBatchCategory.LootBidUpdate,
            RUBatchCategory.ConnectionQualityUpdate,
        };

        private static readonly RUBatchCategory[] DropOrder =
        {
            RUBatchCategory.LootBidUpdate,
            RUBatchCategory.SkillCooldownUpdate,
            RUBatchCategory.GoldSyncEvent,
            RUBatchCategory.SkillCastResult,
            RUBatchCategory.SelfPositionUpdate,
            RUBatchCategory.EntityHealthUpdate,
            RUBatchCategory.PartyMemberHealthUpdate,
            RUBatchCategory.ConnectionQualityUpdate,
        };

        /// <summary>
        /// Serializes one client's R-U batch for a single tick into <paramref name="destination"/>
        /// at offset 0. See class remarks for the full overflow-handling algorithm.
        /// </summary>
        /// <param name="destination">
        /// The destination buffer. Assumed correctly sized (e.g. a <see cref="ClientBufferSet.RUBatchBuffer"/>
        /// slot) — this method never writes more than <see cref="MAX_MESSAGE_BODY_BYTES"/> bytes,
        /// but does not itself validate <paramref name="destination"/>'s length beyond what
        /// <see cref="Span{T}"/> slicing enforces.
        /// </param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this packet.</param>
        /// <param name="tickNumber">The server tick this batch was authored on.</param>
        /// <param name="damageEvents">
        /// This tick's <see cref="DamageEvent"/> entries, in chronological (oldest-first) order.
        /// May be <see langword="null"/> (treated as empty).
        /// </param>
        /// <param name="goldSyncEvents">
        /// This tick's <see cref="GoldSyncEvent"/> entries, in emission order. May be
        /// <see langword="null"/> (treated as empty).
        /// </param>
        /// <param name="otherSubMessages">
        /// This tick's opaque sub-messages for the remaining seven categories (see class
        /// remarks) — must not contain any entry tagged <see cref="RUBatchCategory.DamageEvent"/>
        /// or <see cref="RUBatchCategory.GoldSyncEvent"/>. May be <see langword="null"/> (treated
        /// as empty).
        /// </param>
        /// <param name="clientIdForLogging">The destination client's ID, included in anomaly log messages for traceability.</param>
        /// <param name="observer">
        /// Optional test/dev-build observer. When non-null, its <c>OnServerGoldSyncBatched</c> hook
        /// fires once per <see cref="GoldSyncEvent"/> actually written (AC-NC-19). This parameter
        /// only exists inside <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> — see remarks on
        /// why the observer's interface type is never referenced in an unguarded production context
        /// (Story 002 release-stripping contract, AC-TC-02; fixed alongside Story 010's code review,
        /// which surfaced this same defect in a new file and traced it back here).
        /// </param>
        /// <returns>The total number of bytes written to <paramref name="destination"/> (never exceeds <see cref="MAX_MESSAGE_BODY_BYTES"/>).</returns>
        /// <example>
        /// <code>
        /// int bytesWritten = RUBatchWriter.Write(buffer, 42u, 1000u, damageEvents, goldSyncEvents, otherSubMessages, clientIdForLogging: 7u, observer);
        /// </code>
        /// </example>
        public static int Write(
            Span<byte> destination,
            uint sequenceNumber,
            uint tickNumber,
            IReadOnlyList<DamageEvent> damageEvents,
            IReadOnlyList<GoldSyncEvent> goldSyncEvents,
            IReadOnlyList<PendingSubMessage> otherSubMessages,
            uint clientIdForLogging
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            damageEvents ??= Array.Empty<DamageEvent>();
            goldSyncEvents ??= Array.Empty<GoldSyncEvent>();
            otherSubMessages ??= Array.Empty<PendingSubMessage>();

            // Step 1 — DamageEvent intra-class overflow (oldest-first drop, bounded to DamageEventIntraclassCap).
            int damageEventCount = damageEvents.Count;
            int damageEventStartIndex = 0;
            if (damageEventCount > DamageEventIntraclassCap)
            {
                int droppedCount = damageEventCount - DamageEventIntraclassCap;
                damageEventStartIndex = droppedCount;
                damageEventCount = DamageEventIntraclassCap;
                Debug.LogWarning($"[RUBatchWriter] DamageEventIntraclassOverflow: clientId={clientIdForLogging}, tick={tickNumber}, " +
                    $"droppedCount={droppedCount} — holding at most {DamageEventIntraclassCap} DamageEvent entries per CR-NET-7.6, oldest dropped first.");
            }

            // Step 2 — bucket the seven opaque categories, preserving each category's relative input order.
            var buckets = new List<PendingSubMessage>[9];
            for (int i = 0; i < otherSubMessages.Count; i++)
            {
                PendingSubMessage msg = otherSubMessages[i];
                if (msg.Category == RUBatchCategory.DamageEvent || msg.Category == RUBatchCategory.GoldSyncEvent)
                {
                    throw new ArgumentException(
                        $"PendingSubMessage with Category={msg.Category} must be supplied via the typed " +
                        $"damageEvents/goldSyncEvents parameters, not otherSubMessages.", nameof(otherSubMessages));
                }

                int singleMessageBytes = BatchSubMessageFraming.SubMessageHeaderSize + msg.Payload.Length;
                if (msg.Payload.Length > MaxSingleOpaqueSubMessagePayloadBytes)
                {
                    Debug.LogWarning($"[RUBatchWriter] OversizedSubMessage: clientId={clientIdForLogging}, tick={tickNumber}, " +
                        $"category={msg.Category}, messageTypeId={msg.MessageTypeId} — payload of {msg.Payload.Length} bytes " +
                        $"exceeds the {MaxSingleOpaqueSubMessagePayloadBytes}-byte single-sub-message limit (CR-NET-7.7); this can never fit, even in a fresh batch.");
                    throw new InvalidOperationException(
                        $"OversizedSubMessage: category={msg.Category}, messageTypeId={msg.MessageTypeId} payload " +
                        $"({msg.Payload.Length} bytes) exceeds MaxSingleOpaqueSubMessagePayloadBytes ({MaxSingleOpaqueSubMessagePayloadBytes}).");
                }

                int idx = (int)msg.Category;
                (buckets[idx] ??= new List<PendingSubMessage>()).Add(msg);
                _ = singleMessageBytes; // computed for the guard above; not otherwise needed here.
            }

            // Step 3 — per-category byte totals, in write order.
            var categoryBytes = new int[9];
            categoryBytes[(int)RUBatchCategory.DamageEvent] = damageEventCount * DamageEvent.BatchSize;
            categoryBytes[(int)RUBatchCategory.GoldSyncEvent] = goldSyncEvents.Count * GoldSyncEvent.BatchSize;
            for (int c = 1; c <= 8; c++)
            {
                if (c == (int)RUBatchCategory.GoldSyncEvent)
                {
                    continue;
                }

                List<PendingSubMessage> bucket = buckets[c];
                int sum = 0;
                if (bucket != null)
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        sum += BatchSubMessageFraming.SubMessageHeaderSize + bucket[i].Payload.Length;
                    }
                }

                categoryBytes[c] = sum;
            }

            int totalBytes = BatchHeaderCodec.HeaderSize;
            for (int c = 0; c <= 8; c++)
            {
                totalBytes += categoryBytes[c];
            }

            // Step 4 — category-level eviction (atomic per category), lowest priority first, until it fits.
            var evicted = new bool[9];
            bool anyCategoryDropped = false;
            for (int d = 0; d < DropOrder.Length && totalBytes > MAX_MESSAGE_BODY_BYTES; d++)
            {
                int idx = (int)DropOrder[d];
                if (categoryBytes[idx] == 0)
                {
                    continue;
                }

                totalBytes -= categoryBytes[idx];
                evicted[idx] = true;
                anyCategoryDropped = true;
            }

            if (anyCategoryDropped)
            {
                Debug.LogWarning($"[RUBatchWriter] BatchOverflow: clientId={clientIdForLogging}, tick={tickNumber} — " +
                    "one or more R-U batch categories were dropped in their entirety to fit within " +
                    $"MAX_MESSAGE_BODY_BYTES ({MAX_MESSAGE_BODY_BYTES}).");
            }

            // Step 5 — sub-message count for the 12-byte header.
            int subMessageCount = 0;
            if (!evicted[(int)RUBatchCategory.DamageEvent])
            {
                subMessageCount += damageEventCount;
            }

            if (!evicted[(int)RUBatchCategory.GoldSyncEvent])
            {
                subMessageCount += goldSyncEvents.Count;
            }

            for (int c = 1; c <= 8; c++)
            {
                if (c == (int)RUBatchCategory.GoldSyncEvent || evicted[c] || buckets[c] == null)
                {
                    continue;
                }

                subMessageCount += buckets[c].Count;
            }

            // Step 6 — write the header, then each surviving category in canonical WRITE order.
            var envelope = new ServerMessageEnvelope(TICK_BATCH_RU, sequenceNumber, tickNumber);
            BatchHeaderCodec.Write(destination, in envelope, (ushort)subMessageCount);
            int offset = BatchHeaderCodec.HeaderSize;

            for (int w = 0; w < WriteOrder.Length; w++)
            {
                RUBatchCategory category = WriteOrder[w];
                int idx = (int)category;
                if (evicted[idx])
                {
                    continue;
                }

                if (category == RUBatchCategory.DamageEvent)
                {
                    for (int i = 0; i < damageEventCount; i++)
                    {
                        DamageEvent damageEvent = damageEvents[damageEventStartIndex + i];
                        offset += BatchSubMessageCodec.WriteDamageEvent(destination.Slice(offset), in damageEvent);
                    }
                }
                else if (category == RUBatchCategory.GoldSyncEvent)
                {
                    for (int i = 0; i < goldSyncEvents.Count; i++)
                    {
                        GoldSyncEvent goldSyncEvent = goldSyncEvents[i];
                        offset += BatchSubMessageCodec.WriteGoldSyncEvent(destination.Slice(offset), in goldSyncEvent);
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                        observer?.OnServerGoldSyncBatched(goldSyncEvent.CharacterId.RawValue, goldSyncEvent.NewBalance, goldSyncEvent.Version);
#endif
                    }
                }
                else
                {
                    List<PendingSubMessage> bucket = buckets[idx];
                    if (bucket == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        PendingSubMessage msg = bucket[i];
                        offset += BatchSubMessageFraming.WriteHeader(destination.Slice(offset), (ushort)msg.Payload.Length, msg.MessageTypeId);
                        msg.Payload.Span.CopyTo(destination.Slice(offset, msg.Payload.Length));
                        offset += msg.Payload.Length;
                    }
                }
            }

            return offset;
        }
    }
}
