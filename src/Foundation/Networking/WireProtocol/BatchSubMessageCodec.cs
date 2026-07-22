using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Allocation-free encode/decode for the concrete batch sub-message schemas defined so far:
    /// Story 007's four — <see cref="DamageEvent"/>, <see cref="GoldSyncEvent"/>,
    /// <see cref="CycleTimerBroadcast"/>, <see cref="EntityPositionUpdate"/> — plus Story 028's two
    /// relevance-filtered HP schemas, <see cref="EntityHealthUpdate"/> and
    /// <see cref="PartyMemberHealthUpdate"/>. Each pair of methods writes/reads the full sub-message
    /// including its 4-byte <see cref="BatchSubMessageFraming"/> header — callers do not write the
    /// header separately. Follows the same "concrete, non-generic method per type" discipline as
    /// <see cref="WireIdCodec"/> (never a reflection-based <c>Serialize&lt;T&gt;()</c> dispatch, IL2CPP
    /// AOT safety, CR-NET-7.8).
    /// </summary>
    /// <remarks>
    /// <c>Write*</c> methods assume a correctly-sized destination and let <see cref="BinaryPrimitives"/>/
    /// <see cref="Span{T}"/> slicing throw on an undersized buffer — a caller sizing bug, not a
    /// recoverable network condition (mirrors <see cref="MessageEnvelopeCodec"/>'s documented
    /// convention). <c>TryRead*</c> methods instead return <see langword="false"/> on a too-short
    /// source, since decode paths receive untrusted/possibly-truncated network data. Every
    /// <c>Write*</c> method returns the total number of bytes written (header + body) so callers —
    /// including <see cref="RUBatchWriter"/>, <see cref="CycleBroadcastPacketWriter"/>,
    /// <see cref="PositionPacketWriter"/>, and test fixtures summing serialized bytes per tick —
    /// can advance their own write cursor or accumulate a byte total without a separate size
    /// lookup.
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[DamageEvent.BatchSize];
    /// int written = BatchSubMessageCodec.WriteDamageEvent(buffer, in damageEvent);
    /// if (BatchSubMessageCodec.TryReadDamageEvent(buffer, out DamageEvent decoded, out int bytesRead))
    /// {
    ///     // decoded == damageEvent; bytesRead == written == DamageEvent.BatchSize
    /// }
    /// </code>
    /// </example>
    public static class BatchSubMessageCodec
    {
        // ---------------------------------------------------------------------------------------
        // DamageEvent
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a full <see cref="DamageEvent"/> sub-message (header + body) to <paramref name="destination"/> at offset 0. Returns <see cref="DamageEvent.BatchSize"/> (18).</summary>
        /// <example>
        /// <code>
        /// int written = BatchSubMessageCodec.WriteDamageEvent(buffer, in damageEvent);
        /// </code>
        /// </example>
        public static int WriteDamageEvent(Span<byte> destination, in DamageEvent damageEvent)
        {
            int offset = BatchSubMessageFraming.WriteHeader(destination, DamageEvent.BodySize, DamageEvent.MessageTypeId);
            WireIdCodec.SerializeEntityId(destination.Slice(offset, 4), damageEvent.AttackerEntityId, DamageEvent.MessageTypeId, "attackerEntityId");
            offset += 4;
            WireIdCodec.SerializeEntityId(destination.Slice(offset, 4), damageEvent.TargetEntityId, DamageEvent.MessageTypeId, "targetEntityId");
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), damageEvent.FinalDamage);
            offset += 4;
            destination[offset] = damageEvent.IsCrit ? (byte)1 : (byte)0;
            offset += 1;
            destination[offset] = (byte)damageEvent.DamageType;
            offset += 1;
            return offset;
        }

        /// <summary>
        /// Attempts to decode a <see cref="DamageEvent"/> sub-message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="DamageEvent.BatchSize"/>. <c>damageType</c> is decoded via
        /// <see cref="WireEnumCodec.DecodeDamageType"/> (substitute-and-continue on an out-of-range byte).
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = BatchSubMessageCodec.TryReadDamageEvent(buffer, out DamageEvent decoded, out int bytesRead);
        /// </code>
        /// </example>
        public static bool TryReadDamageEvent(ReadOnlySpan<byte> source, out DamageEvent damageEvent, out int bytesRead)
        {
            if (source.Length < DamageEvent.BatchSize || !BatchSubMessageFraming.TryReadHeader(source, out _, out ushort messageTypeId))
            {
                damageEvent = default;
                bytesRead = 0;
                return false;
            }

            int offset = BatchSubMessageFraming.SubMessageHeaderSize;
            EntityID attackerEntityId = WireIdCodec.DeserializeEntityId(source.Slice(offset, 4));
            offset += 4;
            EntityID targetEntityId = WireIdCodec.DeserializeEntityId(source.Slice(offset, 4));
            offset += 4;
            int finalDamage = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            bool isCrit = source[offset] != 0;
            offset += 1;
            DamageType damageType = WireEnumCodec.DecodeDamageType(source[offset], messageTypeId);
            offset += 1;

            damageEvent = new DamageEvent(attackerEntityId, targetEntityId, finalDamage, isCrit, damageType);
            bytesRead = offset;
            return true;
        }

        // ---------------------------------------------------------------------------------------
        // GoldSyncEvent
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a full <see cref="GoldSyncEvent"/> sub-message (header + body) to <paramref name="destination"/> at offset 0. Returns <see cref="GoldSyncEvent.BatchSize"/> (17).</summary>
        /// <example>
        /// <code>
        /// int written = BatchSubMessageCodec.WriteGoldSyncEvent(buffer, in goldSyncEvent);
        /// </code>
        /// </example>
        public static int WriteGoldSyncEvent(Span<byte> destination, in GoldSyncEvent goldSyncEvent)
        {
            int offset = BatchSubMessageFraming.WriteHeader(destination, GoldSyncEvent.BodySize, GoldSyncEvent.MessageTypeId);
            offset += WriteGoldSyncEventBody(destination.Slice(offset), in goldSyncEvent);
            return offset;
        }

        /// <summary>
        /// Writes only the <see cref="GoldSyncEvent.BodySize"/>-byte (13) <see cref="GoldSyncEvent"/>
        /// body — characterId, newBalance, version, reason — excluding the 4-byte sub-message header
        /// <see cref="WriteGoldSyncEvent"/> writes ahead of it. Factored out (Story 026) so the R-U
        /// batch path (<see cref="WriteGoldSyncEvent"/>) and the standalone R-OD forced-delivery path
        /// (<see cref="GoldSyncForcedDeliveryCodec.Write"/>, MCR-4) encode <c>newBalance</c> via the
        /// exact same code path — the absolute-value-never-a-delta invariant (AC-NC-19) is therefore
        /// structurally identical on both paths, not merely documented to match.
        /// </summary>
        /// <returns><see cref="GoldSyncEvent.BodySize"/> (13), always.</returns>
        internal static int WriteGoldSyncEventBody(Span<byte> destination, in GoldSyncEvent goldSyncEvent)
        {
            WireIdCodec.SerializeCharacterId(destination.Slice(0, 4), goldSyncEvent.CharacterId, GoldSyncEvent.MessageTypeId, "characterId");
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), goldSyncEvent.NewBalance);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), goldSyncEvent.Version);
            destination[12] = (byte)goldSyncEvent.Reason;
            return GoldSyncEvent.BodySize;
        }

        /// <summary>
        /// Attempts to decode a <see cref="GoldSyncEvent"/> sub-message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="GoldSyncEvent.BatchSize"/>. <c>reason</c> is decoded via
        /// <see cref="WireEnumCodec.DecodeGoldTransactionReason"/> (substitute-and-continue on an
        /// out-of-range byte, per <see cref="IronGrind.Currency.GoldTransactionReason"/>'s own
        /// documented receiver contract).
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = BatchSubMessageCodec.TryReadGoldSyncEvent(buffer, out GoldSyncEvent decoded, out int bytesRead);
        /// </code>
        /// </example>
        public static bool TryReadGoldSyncEvent(ReadOnlySpan<byte> source, out GoldSyncEvent goldSyncEvent, out int bytesRead)
        {
            if (source.Length < GoldSyncEvent.BatchSize || !BatchSubMessageFraming.TryReadHeader(source, out _, out ushort messageTypeId))
            {
                goldSyncEvent = default;
                bytesRead = 0;
                return false;
            }

            int offset = BatchSubMessageFraming.SubMessageHeaderSize;
            if (!TryReadGoldSyncEventBody(source.Slice(offset), messageTypeId, out goldSyncEvent))
            {
                bytesRead = 0;
                return false;
            }

            bytesRead = offset + GoldSyncEvent.BodySize;
            return true;
        }

        /// <summary>
        /// Reads only the <see cref="GoldSyncEvent.BodySize"/>-byte (13) <see cref="GoldSyncEvent"/>
        /// body from <paramref name="source"/> (offset 0 of the body, i.e. immediately after whatever
        /// framing/envelope the caller already consumed) — the read-side counterpart to
        /// <see cref="WriteGoldSyncEventBody"/>, shared by <see cref="TryReadGoldSyncEvent"/> (R-U
        /// batch path) and <see cref="GoldSyncForcedDeliveryCodec.TryRead"/> (Story 026's standalone
        /// R-OD forced-delivery path, MCR-4).
        /// </summary>
        /// <param name="messageTypeIdForReasonDecode">
        /// The <c>MessageTypeID</c> passed through to <see cref="WireEnumCodec.DecodeGoldTransactionReason"/>
        /// for its own logging/traceability — the R-U batch path passes <see cref="GoldSyncEvent.MessageTypeId"/>;
        /// the forced-delivery path passes <see cref="GoldSyncEventForcedDelivery.MessageTypeId"/> instead,
        /// so a decode-failure log line correctly names which wire path it came from.
        /// </param>
        /// <returns><see langword="false"/> if <paramref name="source"/> is shorter than <see cref="GoldSyncEvent.BodySize"/>.</returns>
        internal static bool TryReadGoldSyncEventBody(ReadOnlySpan<byte> source, ushort messageTypeIdForReasonDecode, out GoldSyncEvent goldSyncEvent)
        {
            if (source.Length < GoldSyncEvent.BodySize)
            {
                goldSyncEvent = default;
                return false;
            }

            IronGrind.Currency.CharacterID characterId = WireIdCodec.DeserializeCharacterId(source.Slice(0, 4));
            uint newBalance = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4));
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(8, 4));
            IronGrind.Currency.GoldTransactionReason reason = WireEnumCodec.DecodeGoldTransactionReason(source[12], messageTypeIdForReasonDecode);

            goldSyncEvent = new GoldSyncEvent(characterId, newBalance, version, reason);
            return true;
        }

        // ---------------------------------------------------------------------------------------
        // CycleTimerBroadcast
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a full <see cref="CycleTimerBroadcast"/> sub-message (header + body) to <paramref name="destination"/> at offset 0. Returns <see cref="CycleTimerBroadcast.BatchSize"/> (10).</summary>
        /// <example>
        /// <code>
        /// int written = BatchSubMessageCodec.WriteCycleTimerBroadcast(buffer, in cycleTimerBroadcast);
        /// </code>
        /// </example>
        public static int WriteCycleTimerBroadcast(Span<byte> destination, in CycleTimerBroadcast cycleTimerBroadcast)
        {
            int offset = BatchSubMessageFraming.WriteHeader(destination, CycleTimerBroadcast.BodySize, CycleTimerBroadcast.MessageTypeId);
            WireIdCodec.SerializeEntityId(destination.Slice(offset, 4), cycleTimerBroadcast.EntityId, CycleTimerBroadcast.MessageTypeId, "entityId");
            offset += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), cycleTimerBroadcast.CycleTimer);
            offset += 2;
            return offset;
        }

        /// <summary>
        /// Attempts to decode a <see cref="CycleTimerBroadcast"/> sub-message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="CycleTimerBroadcast.BatchSize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = BatchSubMessageCodec.TryReadCycleTimerBroadcast(buffer, out CycleTimerBroadcast decoded, out int bytesRead);
        /// </code>
        /// </example>
        public static bool TryReadCycleTimerBroadcast(ReadOnlySpan<byte> source, out CycleTimerBroadcast cycleTimerBroadcast, out int bytesRead)
        {
            if (source.Length < CycleTimerBroadcast.BatchSize || !BatchSubMessageFraming.TryReadHeader(source, out _, out _))
            {
                cycleTimerBroadcast = default;
                bytesRead = 0;
                return false;
            }

            int offset = BatchSubMessageFraming.SubMessageHeaderSize;
            EntityID entityId = WireIdCodec.DeserializeEntityId(source.Slice(offset, 4));
            offset += 4;
            ushort cycleTimer = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
            offset += 2;

            cycleTimerBroadcast = new CycleTimerBroadcast(entityId, cycleTimer);
            bytesRead = offset;
            return true;
        }

        // ---------------------------------------------------------------------------------------
        // EntityPositionUpdate
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a full <see cref="EntityPositionUpdate"/> sub-message (header + body) to <paramref name="destination"/> at offset 0. Returns <see cref="EntityPositionUpdate.BatchSize"/> (14).</summary>
        /// <example>
        /// <code>
        /// int written = BatchSubMessageCodec.WriteEntityPositionUpdate(buffer, in entityPositionUpdate);
        /// </code>
        /// </example>
        public static int WriteEntityPositionUpdate(Span<byte> destination, in EntityPositionUpdate entityPositionUpdate)
        {
            int offset = BatchSubMessageFraming.WriteHeader(destination, EntityPositionUpdate.BodySize, EntityPositionUpdate.MessageTypeId);
            WireIdCodec.SerializeEntityId(destination.Slice(offset, 4), entityPositionUpdate.EntityId, EntityPositionUpdate.MessageTypeId, "entityId");
            offset += 4;
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(offset, 2), entityPositionUpdate.PosX);
            offset += 2;
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(offset, 2), entityPositionUpdate.PosY);
            offset += 2;
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(offset, 2), entityPositionUpdate.PosZ);
            offset += 2;
            return offset;
        }

        /// <summary>
        /// Attempts to decode an <see cref="EntityPositionUpdate"/> sub-message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="EntityPositionUpdate.BatchSize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = BatchSubMessageCodec.TryReadEntityPositionUpdate(buffer, out EntityPositionUpdate decoded, out int bytesRead);
        /// </code>
        /// </example>
        public static bool TryReadEntityPositionUpdate(ReadOnlySpan<byte> source, out EntityPositionUpdate entityPositionUpdate, out int bytesRead)
        {
            if (source.Length < EntityPositionUpdate.BatchSize || !BatchSubMessageFraming.TryReadHeader(source, out _, out _))
            {
                entityPositionUpdate = default;
                bytesRead = 0;
                return false;
            }

            int offset = BatchSubMessageFraming.SubMessageHeaderSize;
            EntityID entityId = WireIdCodec.DeserializeEntityId(source.Slice(offset, 4));
            offset += 4;
            short posX = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(offset, 2));
            offset += 2;
            short posY = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(offset, 2));
            offset += 2;
            short posZ = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(offset, 2));
            offset += 2;

            entityPositionUpdate = new EntityPositionUpdate(entityId, posX, posY, posZ);
            bytesRead = offset;
            return true;
        }

        // ---------------------------------------------------------------------------------------
        // EntityHealthUpdate (Story 028)
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a full <see cref="EntityHealthUpdate"/> sub-message (header + body) to <paramref name="destination"/> at offset 0. Returns <see cref="EntityHealthUpdate.BatchSize"/> (16).</summary>
        /// <example>
        /// <code>
        /// int written = BatchSubMessageCodec.WriteEntityHealthUpdate(buffer, in entityHealthUpdate);
        /// </code>
        /// </example>
        public static int WriteEntityHealthUpdate(Span<byte> destination, in EntityHealthUpdate entityHealthUpdate)
        {
            int offset = BatchSubMessageFraming.WriteHeader(destination, EntityHealthUpdate.BodySize, EntityHealthUpdate.MessageTypeId);
            offset += WriteEntityHealthUpdateBody(destination.Slice(offset), in entityHealthUpdate);
            return offset;
        }

        /// <summary>
        /// Writes only the <see cref="EntityHealthUpdate.BodySize"/>-byte (12) <see cref="EntityHealthUpdate"/>
        /// body — entityId, currentHP, maxHP — excluding the 4-byte sub-message header
        /// <see cref="WriteEntityHealthUpdate"/> writes ahead of it. Factored out (mirroring
        /// <see cref="WriteGoldSyncEventBody"/>) so <see cref="RelevanceFilter"/> can write only the
        /// body bytes into a <see cref="PendingSubMessage"/>'s <see cref="PendingSubMessage.Payload"/>
        /// — <see cref="RUBatchWriter"/> writes the 4-byte header itself for every opaque
        /// <see cref="PendingSubMessage"/> at the point of batch insertion.
        /// </summary>
        /// <returns><see cref="EntityHealthUpdate.BodySize"/> (12), always.</returns>
        internal static int WriteEntityHealthUpdateBody(Span<byte> destination, in EntityHealthUpdate entityHealthUpdate)
        {
            WireIdCodec.SerializeEntityId(destination.Slice(0, 4), entityHealthUpdate.EntityId, EntityHealthUpdate.MessageTypeId, "entityId");
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), entityHealthUpdate.CurrentHP);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8, 4), entityHealthUpdate.MaxHP);
            return EntityHealthUpdate.BodySize;
        }

        /// <summary>
        /// Attempts to decode a <see cref="EntityHealthUpdate"/> sub-message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="EntityHealthUpdate.BatchSize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = BatchSubMessageCodec.TryReadEntityHealthUpdate(buffer, out EntityHealthUpdate decoded, out int bytesRead);
        /// </code>
        /// </example>
        public static bool TryReadEntityHealthUpdate(ReadOnlySpan<byte> source, out EntityHealthUpdate entityHealthUpdate, out int bytesRead)
        {
            if (source.Length < EntityHealthUpdate.BatchSize || !BatchSubMessageFraming.TryReadHeader(source, out _, out _))
            {
                entityHealthUpdate = default;
                bytesRead = 0;
                return false;
            }

            int offset = BatchSubMessageFraming.SubMessageHeaderSize;
            EntityID entityId = WireIdCodec.DeserializeEntityId(source.Slice(offset, 4));
            offset += 4;
            int currentHP = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            int maxHP = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;

            entityHealthUpdate = new EntityHealthUpdate(entityId, currentHP, maxHP);
            bytesRead = offset;
            return true;
        }

        // ---------------------------------------------------------------------------------------
        // PartyMemberHealthUpdate (Story 028)
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a full <see cref="PartyMemberHealthUpdate"/> sub-message (header + body) to <paramref name="destination"/> at offset 0. Returns <see cref="PartyMemberHealthUpdate.BatchSize"/> (24).</summary>
        /// <example>
        /// <code>
        /// int written = BatchSubMessageCodec.WritePartyMemberHealthUpdate(buffer, in partyMemberHealthUpdate);
        /// </code>
        /// </example>
        public static int WritePartyMemberHealthUpdate(Span<byte> destination, in PartyMemberHealthUpdate partyMemberHealthUpdate)
        {
            int offset = BatchSubMessageFraming.WriteHeader(destination, PartyMemberHealthUpdate.BodySize, PartyMemberHealthUpdate.MessageTypeId);
            offset += WritePartyMemberHealthUpdateBody(destination.Slice(offset), in partyMemberHealthUpdate);
            return offset;
        }

        /// <summary>
        /// Writes only the <see cref="PartyMemberHealthUpdate.BodySize"/>-byte (20)
        /// <see cref="PartyMemberHealthUpdate"/> body — entityId, currentHP, maxHP, currentMP, maxMP —
        /// excluding the 4-byte sub-message header <see cref="WritePartyMemberHealthUpdate"/> writes
        /// ahead of it. Factored out for the same reason as <see cref="WriteEntityHealthUpdateBody"/>
        /// — <see cref="RelevanceFilter"/> writes only body bytes into each
        /// <see cref="PendingSubMessage"/>'s <see cref="PendingSubMessage.Payload"/>.
        /// </summary>
        /// <returns><see cref="PartyMemberHealthUpdate.BodySize"/> (20), always.</returns>
        internal static int WritePartyMemberHealthUpdateBody(Span<byte> destination, in PartyMemberHealthUpdate partyMemberHealthUpdate)
        {
            WireIdCodec.SerializeEntityId(destination.Slice(0, 4), partyMemberHealthUpdate.EntityId, PartyMemberHealthUpdate.MessageTypeId, "entityId");
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), partyMemberHealthUpdate.CurrentHP);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8, 4), partyMemberHealthUpdate.MaxHP);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12, 4), partyMemberHealthUpdate.CurrentMP);
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(16, 4), partyMemberHealthUpdate.MaxMP);
            return PartyMemberHealthUpdate.BodySize;
        }

        /// <summary>
        /// Attempts to decode a <see cref="PartyMemberHealthUpdate"/> sub-message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is shorter than
        /// <see cref="PartyMemberHealthUpdate.BatchSize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = BatchSubMessageCodec.TryReadPartyMemberHealthUpdate(buffer, out PartyMemberHealthUpdate decoded, out int bytesRead);
        /// </code>
        /// </example>
        public static bool TryReadPartyMemberHealthUpdate(ReadOnlySpan<byte> source, out PartyMemberHealthUpdate partyMemberHealthUpdate, out int bytesRead)
        {
            if (source.Length < PartyMemberHealthUpdate.BatchSize || !BatchSubMessageFraming.TryReadHeader(source, out _, out _))
            {
                partyMemberHealthUpdate = default;
                bytesRead = 0;
                return false;
            }

            int offset = BatchSubMessageFraming.SubMessageHeaderSize;
            EntityID entityId = WireIdCodec.DeserializeEntityId(source.Slice(offset, 4));
            offset += 4;
            int currentHP = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            int maxHP = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            int currentMP = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            int maxMP = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;

            partyMemberHealthUpdate = new PartyMemberHealthUpdate(entityId, currentHP, maxHP, currentMP, maxMP);
            bytesRead = offset;
            return true;
        }
    }
}
