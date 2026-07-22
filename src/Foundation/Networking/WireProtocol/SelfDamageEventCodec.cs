using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// Encodes/decodes the Story 027 standalone R-OD <see cref="SelfDamageEvent"/> message (MCR-2):
    /// the 10-byte <see cref="ServerMessageEnvelope"/> immediately followed by the 14-byte
    /// <see cref="DamageEvent"/> body — no batch header, no sub-message framing
    /// (<see cref="BatchSubMessageFraming"/> is never used here; contrast with
    /// <see cref="BatchSubMessageCodec.WriteDamageEvent"/>'s R-U batch sub-message shape).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Field layout is duplicated from, not shared with, <see cref="BatchSubMessageCodec"/>'s
    /// <c>WriteDamageEvent</c>/<c>TryReadDamageEvent</c></b> — see <see cref="SelfDamageEvent"/>'s
    /// type-level remarks for why (Story 027's Out of Scope forbids modifying <c>DamageEvent</c>'s
    /// existing R-U serialization/dispatch). The field order and the shared low-level primitives
    /// (<see cref="WireIdCodec.SerializeEntityId"/>, <see cref="WireIdCodec.DeserializeEntityId"/>,
    /// <see cref="WireEnumCodec.DecodeDamageType"/>) are identical, so the wire layout matches
    /// <see cref="DamageEvent"/>'s body exactly: attackerEntityId[0-3], targetEntityId[4-7],
    /// finalDamage[8-11], isCrit[12], damageType[13].
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var selfDamage = new DamageEvent(attackerEntityId, targetEntityId, finalDamage: 42, isCrit: true, DamageType.Physical);
    /// Span&lt;byte&gt; buffer = stackalloc byte[SelfDamageEvent.WireSize];
    /// int written = SelfDamageEventCodec.Write(buffer, sequenceNumber: 42u, tickNumber: 1003u, in selfDamage);
    /// bool ok = SelfDamageEventCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out DamageEvent decoded);
    /// </code>
    /// </example>
    public static class SelfDamageEventCodec
    {
        /// <summary>
        /// Writes the standalone <see cref="SelfDamageEvent"/> message to <paramref name="destination"/>
        /// at offset 0. Fires <c>observer.OnServerSelfDamageEventSerialized</c> at the point of
        /// serialization (Story 002's pre-built hook for this exact call site) — analogous to
        /// <see cref="GoldSyncForcedDeliveryCodec.Write"/>'s observer call for the R-OD forced-delivery
        /// path.
        /// </summary>
        /// <param name="destination">The destination buffer — must be at least <see cref="SelfDamageEvent.WireSize"/> bytes.</param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this message.</param>
        /// <param name="tickNumber">The server tick this <see cref="SelfDamageEvent"/> was authored on.</param>
        /// <param name="selfDamageEvent">
        /// The attacker/target/damage/crit/type tuple (same fields as <see cref="DamageEvent"/>,
        /// CCR-3). Callers should route through <see cref="SelfDamageEventDispatcher"/> rather than
        /// calling this method directly, so the singleton-recipient invariant stays structural.
        /// </param>
        /// <returns><see cref="SelfDamageEvent.WireSize"/> (24), always.</returns>
        /// <example>
        /// <code>
        /// int written = SelfDamageEventCodec.Write(buffer, sequenceNumber: 42u, tickNumber: 1003u, in selfDamage);
        /// </code>
        /// </example>
        public static int Write(
            Span<byte> destination,
            uint sequenceNumber,
            uint tickNumber,
            in DamageEvent selfDamageEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            var envelope = new ServerMessageEnvelope(SelfDamageEvent.MessageTypeId, sequenceNumber, tickNumber);
            MessageEnvelopeCodec.Write(destination, in envelope);
            int offset = ServerMessageEnvelope.WireSize;

            WireIdCodec.SerializeEntityId(destination.Slice(offset, 4), selfDamageEvent.AttackerEntityId, SelfDamageEvent.MessageTypeId, "attackerEntityId");
            offset += 4;
            WireIdCodec.SerializeEntityId(destination.Slice(offset, 4), selfDamageEvent.TargetEntityId, SelfDamageEvent.MessageTypeId, "targetEntityId");
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), selfDamageEvent.FinalDamage);
            offset += 4;
            destination[offset] = selfDamageEvent.IsCrit ? (byte)1 : (byte)0;
            offset += 1;
            destination[offset] = (byte)selfDamageEvent.DamageType;
            offset += 1;

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnServerSelfDamageEventSerialized(
                selfDamageEvent.AttackerEntityId.RawValue, selfDamageEvent.TargetEntityId.RawValue, selfDamageEvent.FinalDamage);
#endif

            return offset;
        }

        /// <summary>
        /// Attempts to decode a standalone <see cref="SelfDamageEvent"/> message from
        /// <paramref name="source"/>. Returns <see langword="false"/> if <paramref name="source"/> is
        /// too short, or if the decoded envelope's <c>MessageTypeId</c> does not match
        /// <see cref="SelfDamageEvent.MessageTypeId"/> (wrong dispatch — a caller routing the wrong
        /// buffer here rather than a network condition, but returned rather than thrown since decode
        /// paths receive untrusted/possibly-truncated data, matching
        /// <see cref="MessageEnvelopeCodec.TryRead"/>'s own convention). Does not itself call any
        /// client-side observer hook — callers fire <c>observer.OnClientSelfDamageEventReceived</c>
        /// after decoding, matching <see cref="GoldSyncForcedDeliveryCodec.TryRead"/>'s precedent.
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = SelfDamageEventCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out DamageEvent decoded);
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ServerMessageEnvelope envelope, out DamageEvent selfDamageEvent)
        {
            if (!MessageEnvelopeCodec.TryRead(source, out envelope) || envelope.MessageTypeId != SelfDamageEvent.MessageTypeId)
            {
                envelope = default;
                selfDamageEvent = default;
                return false;
            }

            ReadOnlySpan<byte> body = source.Slice(ServerMessageEnvelope.WireSize);
            if (body.Length < DamageEvent.BodySize)
            {
                selfDamageEvent = default;
                return false;
            }

            int offset = 0;
            EntityID attackerEntityId = WireIdCodec.DeserializeEntityId(body.Slice(offset, 4));
            offset += 4;
            EntityID targetEntityId = WireIdCodec.DeserializeEntityId(body.Slice(offset, 4));
            offset += 4;
            int finalDamage = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(offset, 4));
            offset += 4;
            bool isCrit = body[offset] != 0;
            offset += 1;
            DamageType damageType = WireEnumCodec.DecodeDamageType(body[offset], SelfDamageEvent.MessageTypeId);

            selfDamageEvent = new DamageEvent(attackerEntityId, targetEntityId, finalDamage, isCrit, damageType);
            return true;
        }
    }
}
