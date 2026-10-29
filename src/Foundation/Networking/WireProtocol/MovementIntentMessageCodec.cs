using System;
using System.Buffers.Binary;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Encodes/decodes <see cref="MovementIntentMessage"/> (Movement System Story 002, CR-MOV-10) and
    /// provides the direction/facing quantisation helpers. Explicit little-endian, allocation-free,
    /// no logging (the caller logs when <c>wasNonUnit</c> is reported). Thread-safe: all members are
    /// pure static functions over their arguments.
    /// </summary>
    /// <example>
    /// <code>
    /// (short x, short z) = MovementIntentMessageCodec.EncodeDirection(new Vector2(0.6f, -0.8f), out bool wasNonUnit);
    /// var msg = new MovementIntentMessage(x, z, MovementIntentMessageCodec.EncodeFacing(123.4f), 1000u);
    /// Span&lt;byte&gt; buffer = stackalloc byte[MovementIntentMessage.BodySize];
    /// MovementIntentMessageCodec.WriteBody(buffer, in msg);
    /// bool ok = MovementIntentMessageCodec.TryReadBody(buffer, out MovementIntentMessage read);
    /// </code>
    /// </example>
    public static class MovementIntentMessageCodec
    {
        /// <summary>Direction quantisation scale: a unit component maps to ±32,767 (CR-NET-7.2).</summary>
        public const short DIRECTION_SCALE = 32767;

        /// <summary>Allowed deviation of the encoded vector length from <see cref="DIRECTION_SCALE"/> before the input is deemed non-unit.</summary>
        public const int NON_UNIT_TOLERANCE = 100;

        /// <summary>Number of facing steps in a full turn (degrees × 10).</summary>
        public const short FACING_STEPS = 3600;

        // Facing input is clamped to this many degrees before × 10 so the float-to-int conversion
        // never sees a value outside int range (that conversion differs between x64 and ARM64).
        private const float MAX_FACING_INPUT_DEGREES = 1e6f;

        // ── Full message (envelope + body) ─────────────────────────────────────────────────

        /// <summary>
        /// Writes the envelope followed by the body at offset 0 of <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">At least <see cref="MovementIntentMessage.WireSize"/> bytes.</param>
        /// <param name="sequenceNumber">Outbound envelope sequence number.</param>
        /// <param name="tickNumber">Envelope tick number.</param>
        /// <param name="senderEntityId">The sending client's own EntityID (envelope field).</param>
        /// <param name="message">The message body to write.</param>
        /// <returns><see cref="MovementIntentMessage.WireSize"/> (24).</returns>
        /// <example>
        /// <code>
        /// int n = MovementIntentMessageCodec.Write(buffer, 7u, 1000u, 501u, in msg);
        /// </code>
        /// </example>
        public static int Write(Span<byte> destination, uint sequenceNumber, uint tickNumber, uint senderEntityId, in MovementIntentMessage message)
        {
            var envelope = new ClientEntityMessageEnvelope(MovementIntentMessage.MessageTypeId, sequenceNumber, tickNumber, senderEntityId);
            MessageEnvelopeCodec.Write(destination, in envelope);
            WriteBody(destination.Slice(ClientEntityMessageEnvelope.WireSize), in message);
            return MovementIntentMessage.WireSize;
        }

        /// <summary>
        /// Attempts to decode a full message. Returns false if the envelope is unreadable, its
        /// <c>MessageTypeId</c> is not <see cref="MovementIntentMessage.MessageTypeId"/>, or the
        /// remaining bytes are not exactly <see cref="MovementIntentMessage.BodySize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = MovementIntentMessageCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out MovementIntentMessage msg);
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ClientEntityMessageEnvelope envelope, out MovementIntentMessage message)
        {
            if (!MessageEnvelopeCodec.TryRead(source, out envelope) || envelope.MessageTypeId != MovementIntentMessage.MessageTypeId)
            {
                envelope = default;
                message = default;
                return false;
            }

            return TryReadBody(source.Slice(ClientEntityMessageEnvelope.WireSize), out message);
        }

        // ── Body ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the 10-byte body (little-endian: dirX, dirZ, facingAngle, tickNumber).
        /// </summary>
        /// <param name="destination">At least <see cref="MovementIntentMessage.BodySize"/> bytes.</param>
        /// <param name="message">The message to write.</param>
        /// <returns><see cref="MovementIntentMessage.BodySize"/> (10).</returns>
        /// <example>
        /// <code>
        /// int written = MovementIntentMessageCodec.WriteBody(body, in msg);
        /// </code>
        /// </example>
        public static int WriteBody(Span<byte> destination, in MovementIntentMessage message)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(0, 2), message.DirX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), message.DirZ);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(4, 2), message.FacingAngle);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(6, 4), message.TickNumber);
            return MovementIntentMessage.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns false unless <paramref name="body"/> is exactly
        /// <see cref="MovementIntentMessage.BodySize"/> bytes. Does NOT range-check the facing angle;
        /// the intake calls <see cref="NormalizeFacing"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// if (MovementIntentMessageCodec.TryReadBody(body, out MovementIntentMessage msg)) { /* use msg */ }
        /// </code>
        /// </example>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out MovementIntentMessage message)
        {
            if (body.Length != MovementIntentMessage.BodySize)
            {
                message = default;
                return false;
            }

            message = new MovementIntentMessage(
                BinaryPrimitives.ReadInt16LittleEndian(body.Slice(0, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(body.Slice(2, 2)),
                BinaryPrimitives.ReadInt16LittleEndian(body.Slice(4, 2)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(6, 4)));
            return true;
        }

        // ── Direction ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Quantises a unit XZ direction (x to DirX, y to DirZ): each component is rounded to
        /// round(c × 32,767) and clamped to ±32,767. An exact zero vector yields (0, 0) with
        /// <paramref name="wasNonUnit"/> false. For a non-zero input, if the encoded vector's length
        /// differs from 32,767 by more than <see cref="NON_UNIT_TOLERANCE"/>, returns (32767, 0)
        /// and sets <paramref name="wasNonUnit"/> true. A NaN or infinite component is treated the
        /// same way: (32767, 0), <paramref name="wasNonUnit"/> true. Each scaled component is clamped
        /// before rounding, so an over-unit single-axis input such as (2, 0) encodes to (32767, 0)
        /// and is not flagged. This method does not log; the caller does.
        /// </summary>
        /// <example>
        /// <code>
        /// (short x, short z) = MovementIntentMessageCodec.EncodeDirection(new Vector2(0.6f, -0.8f), out bool wasNonUnit);
        /// </code>
        /// </example>
        public static (short x, short z) EncodeDirection(Vector2 unitXZ, out bool wasNonUnit)
        {
            // NaN/Infinity to int differs by platform (x64: int.MinValue; ARM64: saturates, NaN → 0)
            // and NaN fails the zero check below, so reject non-finite input before any conversion.
            if (!float.IsFinite(unitXZ.x) || !float.IsFinite(unitXZ.y))
            {
                wasNonUnit = true;
                return (DIRECTION_SCALE, 0);
            }

            wasNonUnit = false;
            if (unitXZ.x == 0f && unitXZ.y == 0f)
            {
                return (0, 0);
            }

            int x = Mathf.RoundToInt(Mathf.Clamp(unitXZ.x * DIRECTION_SCALE, -DIRECTION_SCALE, DIRECTION_SCALE));
            int z = Mathf.RoundToInt(Mathf.Clamp(unitXZ.y * DIRECTION_SCALE, -DIRECTION_SCALE, DIRECTION_SCALE));

            double length = Math.Sqrt((double)x * x + (double)z * z);
            if (Math.Abs(length - DIRECTION_SCALE) > NON_UNIT_TOLERANCE)
            {
                wasNonUnit = true;
                return (DIRECTION_SCALE, 0);
            }

            return ((short)x, (short)z);
        }

        /// <summary>Decodes wire direction shorts to a float vector (÷ 32,767; x to x, z to y).</summary>
        /// <example>
        /// <code>
        /// Vector2 dir = MovementIntentMessageCodec.DecodeDirection(msg.DirX, msg.DirZ);
        /// </code>
        /// </example>
        public static Vector2 DecodeDirection(short dirX, short dirZ)
            => new Vector2(dirX / (float)DIRECTION_SCALE, dirZ / (float)DIRECTION_SCALE);

        // ── Facing ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Encodes degrees as RoundToInt(degrees × 10) wrapped into 0–3599 (negatives and values
        /// at or above 360° wrap; 359.96° encodes to 0, −0.1° to 3599). A NaN or infinite input
        /// encodes to 0; finite input is clamped to ±1,000,000° first, so the result is always in
        /// 0–3599 on every platform.
        /// </summary>
        /// <example>
        /// <code>
        /// short facing = MovementIntentMessageCodec.EncodeFacing(123.4f); // 1234
        /// </code>
        /// </example>
        public static short EncodeFacing(float degrees)
        {
            if (!float.IsFinite(degrees))
            {
                return 0;
            }

            int steps = Mathf.RoundToInt(Mathf.Clamp(degrees, -MAX_FACING_INPUT_DEGREES, MAX_FACING_INPUT_DEGREES) * 10f);
            int wrapped = ((steps % FACING_STEPS) + FACING_STEPS) % FACING_STEPS;
            return (short)wrapped;
        }

        /// <summary>Decodes a facing value to degrees (÷ 10).</summary>
        /// <example>
        /// <code>
        /// float degrees = MovementIntentMessageCodec.DecodeFacing(1234); // 123.4
        /// </code>
        /// </example>
        public static float DecodeFacing(short facing) => facing / 10f;

        /// <summary>
        /// Wraps any received facing value into 0–3599: <c>((v % 3600) + 3600) % 3600</c> in int math.
        /// Out-of-range received values are wrapped, never dropped (AC-NC-32 precedent: substitute,
        /// never drop). Server intake (Story 004) calls this before storing.
        /// </summary>
        /// <example>
        /// <code>
        /// short safe = MovementIntentMessageCodec.NormalizeFacing(msg.FacingAngle); // -1 becomes 3599
        /// </code>
        /// </example>
        public static short NormalizeFacing(short value)
            => (short)(((value % FACING_STEPS) + FACING_STEPS) % FACING_STEPS);
    }
}
