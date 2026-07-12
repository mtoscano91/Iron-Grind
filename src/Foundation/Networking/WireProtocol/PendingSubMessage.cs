using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// An opaque, already-serialized R-U batch sub-message supplied by the caller for one of the
    /// seven <see cref="RUBatchCategory"/> categories that have no concrete wire schema in this
    /// codebase yet (<see cref="RUBatchCategory.EntityHealthUpdate"/>,
    /// <see cref="RUBatchCategory.PartyMemberHealthUpdate"/>, <see cref="RUBatchCategory.SelfPositionUpdate"/>,
    /// <see cref="RUBatchCategory.SkillCastResult"/>, <see cref="RUBatchCategory.SkillCooldownUpdate"/>,
    /// <see cref="RUBatchCategory.LootBidUpdate"/>, <see cref="RUBatchCategory.ConnectionQualityUpdate"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why opaque payloads:</b> the systems that own these seven categories (Skill System,
    /// Party System, Loot Table System, Client-Side Prediction, relevance filtering) do not exist
    /// in this codebase yet — the same scoping discipline <see cref="PriorityPathQueue{T}"/>
    /// applies to its opaque <c>T</c> payload. Rather than inventing speculative schemas for
    /// systems this story does not own, <see cref="RUBatchWriter"/> accepts pre-serialized bytes
    /// for these categories and only needs to know which category (for canonical ordering and
    /// category-level overflow drop) and which <see cref="MessageTypeId"/> to stamp on the wire.
    /// Future stories that implement these systems' concrete schemas construct
    /// <see cref="PendingSubMessage"/> instances by serializing their own typed struct into a
    /// buffer and wrapping the result — <see cref="RUBatchWriter"/> itself never changes.
    /// </para>
    /// <para>
    /// <b>Do not use this type for <see cref="RUBatchCategory.DamageEvent"/> or
    /// <see cref="RUBatchCategory.GoldSyncEvent"/></b> — those two categories have concrete typed
    /// schemas (<see cref="DamageEvent"/>, <see cref="GoldSyncEvent"/>) owned by this GDD document
    /// itself and are passed to <see cref="RUBatchWriter.Write"/> via their own dedicated typed
    /// list parameters instead, so the writer can apply their category-specific overflow policies
    /// (DamageEvent's intra-class cap; GoldSyncEvent's <see cref="INetworkTestObserver.OnServerGoldSyncBatched"/>
    /// hook) precisely at the point of batch insertion. <see cref="RUBatchWriter.Write"/> throws
    /// <see cref="ArgumentException"/> if a <see cref="PendingSubMessage"/> tagged with either of
    /// those two categories is passed through this generic path.
    /// </para>
    /// <para>
    /// <see cref="Payload"/> excludes the 2-byte length prefix and 2-byte <see cref="MessageTypeId"/>
    /// — <see cref="RUBatchWriter"/> writes that 4-byte sub-message header itself
    /// (<see cref="BatchSubMessageFraming.WriteHeader"/>) immediately before copying
    /// <see cref="Payload"/> to the destination buffer.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// byte[] serializedSkillCastResult = new byte[SkillCastResultBodySize];
    /// // ... future Skill System story serializes its fields into serializedSkillCastResult ...
    /// var pending = new PendingSubMessage(RUBatchCategory.SkillCastResult, messageTypeId: 0x0A10, serializedSkillCastResult);
    /// </code>
    /// </example>
    public readonly struct PendingSubMessage
    {
        /// <summary>
        /// The category this sub-message belongs to. Determines canonical write order and
        /// category-level overflow eviction priority (see <see cref="RUBatchWriter"/> remarks).
        /// Must not be <see cref="RUBatchCategory.DamageEvent"/> or <see cref="RUBatchCategory.GoldSyncEvent"/>
        /// — those are supplied via dedicated typed parameters instead (see type-level remarks).
        /// </summary>
        public readonly RUBatchCategory Category;

        /// <summary>The wire <c>MessageTypeID</c> stamped on this sub-message's 4-byte header.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>
        /// The already-serialized sub-message body, excluding the 2-byte length prefix and 2-byte
        /// <see cref="MessageTypeId"/> that <see cref="RUBatchWriter"/> writes ahead of it.
        /// </summary>
        public readonly ReadOnlyMemory<byte> Payload;

        /// <summary>Initializes a new <see cref="PendingSubMessage"/>.</summary>
        /// <example>
        /// <code>
        /// var pending = new PendingSubMessage(RUBatchCategory.ConnectionQualityUpdate, messageTypeId: 0x0A20, payloadBytes);
        /// </code>
        /// </example>
        public PendingSubMessage(RUBatchCategory category, ushort messageTypeId, ReadOnlyMemory<byte> payload)
        {
            Category = category;
            MessageTypeId = messageTypeId;
            Payload = payload;
        }
    }
}
