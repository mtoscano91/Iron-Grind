using System;
using System.Buffers.Binary;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Allocation-free encode/decode for the wire ID types that carry a reserved-zero sentinel
    /// (<see cref="EntityID"/>, <see cref="ItemID"/>, <see cref="CharacterID"/> — CR-NET-7.3).
    /// Each type gets its own concrete, non-generic method pair — never a reflection-based
    /// <c>Serialize&lt;T&gt;()</c> dispatch (IL2CPP AOT safety, CR-NET-7.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero-write guard (CR-NET-7.3):</b> the <c>Invalid</c>/<c>None</c> sentinel (underlying
    /// value <c>0</c>) must never reach the wire in a valid message body. Every
    /// <c>Serialize*Id</c> method checks this <i>before</i> writing any byte; on a zero ID it
    /// logs an <c>InvalidIdZeroWrite</c> anomaly naming the message type and field, then throws
    /// <see cref="InvalidOperationException"/> — no partial write is possible because the check
    /// happens strictly before <see cref="BinaryPrimitives.WriteUInt32LittleEndian"/> is called.
    /// </para>
    /// <para>
    /// <see cref="InvalidOperationException"/> was chosen (over e.g. <see cref="ArgumentException"/>)
    /// because the raw <c>uint</c> value <c>0</c> is not itself a malformed argument — it is a
    /// well-formed sentinel meaning "no ID assigned". What is invalid is the <i>operation</i> of
    /// writing that sentinel into a message body that requires a real, assigned ID. There is no
    /// existing custom exception type in this codebase for this class of condition, so a standard
    /// BCL type is used per CR-NET-7.3's guidance.
    /// </para>
    /// <para>
    /// <b>Decode</b> methods perform no zero-guard — CR-NET-7.3's assert is specifically about
    /// the write side. A <c>0</c> arriving from the wire on decode is a receiver-side concern for
    /// a future story (out of scope here), same as <see cref="MessageEnvelopeCodec.Write"/>'s
    /// documented convention: decode assumes a correctly-sized source and lets
    /// <see cref="BinaryPrimitives"/> throw its own exception on an undersized buffer — decode is
    /// not a <c>Try*</c> method here because, unlike envelope framing, ID fields are fixed-size
    /// sub-fields of an already envelope-validated message body.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[WireIdCodec.EntityIdWireSize];
    /// WireIdCodec.SerializeEntityId(buffer, attackerEntityId, messageTypeId: 0x0301, fieldName: "attackerEntityId");
    /// EntityID decoded = WireIdCodec.DeserializeEntityId(buffer);
    /// </code>
    /// </example>
    public static class WireIdCodec
    {
        /// <summary>Wire size of an encoded <see cref="EntityID"/>, in bytes.</summary>
        public const int EntityIdWireSize = 4;

        /// <summary>Wire size of an encoded <see cref="ItemID"/>, in bytes.</summary>
        public const int ItemIdWireSize = 4;

        /// <summary>Wire size of an encoded <see cref="CharacterID"/>, in bytes.</summary>
        public const int CharacterIdWireSize = 4;

        // ---------------------------------------------------------------------------------------
        // EntityID
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Writes <paramref name="entityId"/> as a 4-byte little-endian <see cref="uint"/> into
        /// <paramref name="destination"/>. Throws <see cref="InvalidOperationException"/> without
        /// writing any byte if <paramref name="entityId"/> is <see cref="EntityID.Invalid"/> (see
        /// type-level remarks on the zero-write guard).
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireIdCodec.EntityIdWireSize];
        /// WireIdCodec.SerializeEntityId(buffer, attackerEntityId, messageTypeId: 0x0301, fieldName: "attackerEntityId");
        /// </code>
        /// </example>
        public static void SerializeEntityId(Span<byte> destination, EntityID entityId, ushort messageTypeId, string fieldName)
        {
            if (entityId == EntityID.Invalid)
            {
                Debug.LogWarning($"[WireIdCodec] SerializeEntityId: InvalidIdZeroWrite — messageTypeId={messageTypeId}, " +
                    $"field=\"{fieldName}\" — refusing to write a zero-valued EntityID to the wire (CR-NET-7.3).");
                throw new InvalidOperationException($"InvalidIdZeroWrite: EntityID field \"{fieldName}\" on message " +
                    $"type {messageTypeId} is Invalid (0) and must never be written to a valid message body (CR-NET-7.3).");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, entityId.RawValue);
        }

        /// <summary>Decodes an <see cref="EntityID"/> previously written by <see cref="SerializeEntityId"/>. No zero-guard — see type-level remarks.</summary>
        /// <example>
        /// <code>
        /// EntityID attackerEntityId = WireIdCodec.DeserializeEntityId(receivedBytes);
        /// </code>
        /// </example>
        public static EntityID DeserializeEntityId(ReadOnlySpan<byte> source)
            => new EntityID(BinaryPrimitives.ReadUInt32LittleEndian(source));

        // ---------------------------------------------------------------------------------------
        // ItemID
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Writes <paramref name="itemId"/> as a 4-byte little-endian <see cref="uint"/> into
        /// <paramref name="destination"/>. Throws <see cref="InvalidOperationException"/> without
        /// writing any byte if <paramref name="itemId"/> is <see cref="ItemID.Invalid"/>/<see cref="ItemID.None"/>
        /// (both alias <c>uint(0)</c>; see type-level remarks on the zero-write guard).
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireIdCodec.ItemIdWireSize];
        /// WireIdCodec.SerializeItemId(buffer, lootedItemId, messageTypeId: 0x0410, fieldName: "itemId");
        /// </code>
        /// </example>
        public static void SerializeItemId(Span<byte> destination, ItemID itemId, ushort messageTypeId, string fieldName)
        {
            if (itemId == ItemID.Invalid)
            {
                Debug.LogWarning($"[WireIdCodec] SerializeItemId: InvalidIdZeroWrite — messageTypeId={messageTypeId}, " +
                    $"field=\"{fieldName}\" — refusing to write a zero-valued ItemID to the wire (CR-NET-7.3).");
                throw new InvalidOperationException($"InvalidIdZeroWrite: ItemID field \"{fieldName}\" on message " +
                    $"type {messageTypeId} is Invalid/None (0) and must never be written to a valid message body (CR-NET-7.3).");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, itemId.RawValue);
        }

        /// <summary>Decodes an <see cref="ItemID"/> previously written by <see cref="SerializeItemId"/>. No zero-guard — see type-level remarks.</summary>
        /// <example>
        /// <code>
        /// ItemID lootedItemId = WireIdCodec.DeserializeItemId(receivedBytes);
        /// </code>
        /// </example>
        public static ItemID DeserializeItemId(ReadOnlySpan<byte> source)
            => new ItemID(BinaryPrimitives.ReadUInt32LittleEndian(source));

        // ---------------------------------------------------------------------------------------
        // CharacterID
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Writes <paramref name="characterId"/> as a 4-byte little-endian <see cref="uint"/> into
        /// <paramref name="destination"/>. Throws <see cref="InvalidOperationException"/> without
        /// writing any byte if <paramref name="characterId"/> is <see cref="CharacterID.Invalid"/>
        /// (see type-level remarks on the zero-write guard).
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireIdCodec.CharacterIdWireSize];
        /// WireIdCodec.SerializeCharacterId(buffer, ownerCharacterId, messageTypeId: 0x0520, fieldName: "characterId");
        /// </code>
        /// </example>
        public static void SerializeCharacterId(Span<byte> destination, CharacterID characterId, ushort messageTypeId, string fieldName)
        {
            if (characterId == CharacterID.Invalid)
            {
                Debug.LogWarning($"[WireIdCodec] SerializeCharacterId: InvalidIdZeroWrite — messageTypeId={messageTypeId}, " +
                    $"field=\"{fieldName}\" — refusing to write a zero-valued CharacterID to the wire (CR-NET-7.3).");
                throw new InvalidOperationException($"InvalidIdZeroWrite: CharacterID field \"{fieldName}\" on message " +
                    $"type {messageTypeId} is Invalid (0) and must never be written to a valid message body (CR-NET-7.3).");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, characterId.RawValue);
        }

        /// <summary>Decodes a <see cref="CharacterID"/> previously written by <see cref="SerializeCharacterId"/>. No zero-guard — see type-level remarks.</summary>
        /// <example>
        /// <code>
        /// CharacterID ownerCharacterId = WireIdCodec.DeserializeCharacterId(receivedBytes);
        /// </code>
        /// </example>
        public static CharacterID DeserializeCharacterId(ReadOnlySpan<byte> source)
            => new CharacterID(BinaryPrimitives.ReadUInt32LittleEndian(source));
    }
}
