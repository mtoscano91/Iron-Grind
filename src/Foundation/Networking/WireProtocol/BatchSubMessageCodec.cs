using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Allocation-free encode/decode for the four concrete batch sub-message schemas this story
    /// defines: <see cref="DamageEvent"/>, <see cref="GoldSyncEvent"/>, <see cref="CycleTimerBroadcast"/>,
    /// <see cref="EntityPositionUpdate"/>. Each pair of methods writes/reads the full sub-message
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
            WireIdCodec.SerializeCharacterId(destination.Slice(offset, 4), goldSyncEvent.CharacterId, GoldSyncEvent.MessageTypeId, "characterId");
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), goldSyncEvent.NewBalance);
            offset += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), goldSyncEvent.Version);
            offset += 4;
            destination[offset] = (byte)goldSyncEvent.Reason;
            offset += 1;
            return offset;
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
            IronGrind.Currency.CharacterID characterId = WireIdCodec.DeserializeCharacterId(source.Slice(offset, 4));
            offset += 4;
            uint newBalance = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
            offset += 4;
            IronGrind.Currency.GoldTransactionReason reason = WireEnumCodec.DecodeGoldTransactionReason(source[offset], messageTypeId);
            offset += 1;

            goldSyncEvent = new GoldSyncEvent(characterId, newBalance, version, reason);
            bytesRead = offset;
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
    }
}
