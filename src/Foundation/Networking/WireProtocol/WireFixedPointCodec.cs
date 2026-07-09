using System;
using System.Buffers.Binary;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Allocation-free fixed-point encode/decode for the CR-NET-7.2 primitive mappings — display-only
    /// float fields (<c>critChance</c>, <c>attackSpeedMultiplier</c>, <c>cycleTimer</c>), spatial data
    /// (position, rotation, unit direction), and the plain <see cref="int"/>/<see cref="uint"/> writers
    /// used for already-integer-domain authoritative fields (<c>finalDamage</c>, and HP/gold/XP/stat
    /// totals after <c>Mathf.FloorToInt</c> has been applied by the caller).
    /// </summary>
    /// <remarks>
    /// <para>
    /// No raw <see cref="float"/>, <see cref="Vector3"/>, or <see cref="Quaternion"/> value may reach
    /// a wire buffer without passing through one of the encoders below (CR-NET-7.2). Every encoder
    /// writes directly into a caller-supplied <see cref="Span{T}"/> at offset 0 — structs/values in,
    /// bytes out, zero heap allocation.
    /// </para>
    /// <para>
    /// Uses <see cref="BinaryPrimitives"/> little-endian writers/readers exclusively — never
    /// <see cref="BitConverter"/> (endianness is runtime-dependent) and never manual bit-shifting.
    /// </para>
    /// <para>
    /// <b>Guard scope (per Story 003):</b> pre-encode guards (clamp + <see cref="Debug.LogWarning"/>
    /// anomaly) are implemented for <c>cycleTimer</c> (including the <c>CycleDuration</c> caller
    /// contract), position, rotation, and unit direction — exactly the fields CR-NET-7.2 documents a
    /// guard for. <c>critChance</c> and <c>attackSpeedMultiplier</c> are intentionally implemented as
    /// a plain round + cast with no clamp/log: CR-NET-7.2 documents a valid range for these two fields
    /// but does not specify a runtime guard, and out-of-range input is currently an unguarded
    /// <see cref="ushort"/> wraparound risk — tracked as a known gap, not fixed by this story.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.PositionWireSize];
    /// WireFixedPointCodec.EncodePosition(buffer, new Vector3(12.75f, 0f, -85.23f));
    /// Vector3 decoded = WireFixedPointCodec.DecodePosition(buffer);
    /// </code>
    /// </example>
    public static class WireFixedPointCodec
    {
        // ---------------------------------------------------------------------------------------
        // Wire sizes (bytes) — public: callers need these to size/slice message body buffers.
        // ---------------------------------------------------------------------------------------

        /// <summary>Wire size of an encoded <c>critChance</c> value, in bytes.</summary>
        public const int CritChanceWireSize = 2;

        /// <summary>Wire size of an encoded <c>attackSpeedMultiplier</c> value, in bytes.</summary>
        public const int AttackSpeedMultiplierWireSize = 2;

        /// <summary>Wire size of an encoded <c>cycleTimer</c> value, in bytes.</summary>
        public const int CycleTimerWireSize = 2;

        /// <summary>Wire size of an encoded position (3 × <see cref="short"/>), in bytes.</summary>
        public const int PositionWireSize = 6;

        /// <summary>Wire size of an encoded rotation (4 × <see cref="short"/>), in bytes.</summary>
        public const int RotationWireSize = 8;

        /// <summary>Wire size of an encoded unit direction (3 × <see cref="short"/>), in bytes.</summary>
        public const int DirectionWireSize = 6;

        /// <summary>Wire size of a plain <see cref="int"/> passthrough field, in bytes.</summary>
        public const int Int32WireSize = 4;

        /// <summary>Wire size of a plain <see cref="uint"/> passthrough field, in bytes.</summary>
        public const int UInt32WireSize = 4;

        /// <summary>
        /// Maximum per-axis position magnitude (metres) representable at centimetre precision within
        /// a <see cref="short"/> (CR-NET-7.2). Referenced by Zone Instancing to validate walkable
        /// zone bounds fit within this range.
        /// </summary>
        public const float MaxPositionMeters = 327.67f;

        // ---------------------------------------------------------------------------------------
        // Fixed-point scale factors (CR-NET-7.2) — implementation detail.
        // ---------------------------------------------------------------------------------------

        private const int CritChanceScale = 10000;
        private const int AttackSpeedMultiplierScale = 1000;
        private const int CycleTimerScale = 10000;
        private const float PositionScale = 100f;
        private const float RotationScale = 32767f;
        private const float DirectionScale = 32767f;

        /// <summary>
        /// Below this magnitude, a quaternion or direction vector is treated as degenerate and
        /// substituted with a safe default (CR-NET-7.2 zero-guard).
        /// </summary>
        private const float ZeroMagnitudeEpsilon = 1e-5f;

        // ---------------------------------------------------------------------------------------
        // critChance
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Encodes <paramref name="critChance"/> (fractional domain, e.g. 0.05 = 5%; documented range
        /// 0.0–0.75) as a <see cref="ushort"/> (×10,000) into <paramref name="destination"/>.
        /// No clamp/guard — see the type-level remarks on guard scope.
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.CritChanceWireSize];
        /// WireFixedPointCodec.EncodeCritChance(buffer, 0.05f); // 5% crit chance
        /// </code>
        /// </example>
        public static void EncodeCritChance(Span<byte> destination, float critChance)
        {
            ushort encoded = (ushort)Mathf.RoundToInt(critChance * CritChanceScale);
            BinaryPrimitives.WriteUInt16LittleEndian(destination, encoded);
        }

        /// <summary>Decodes a <c>critChance</c> value previously written by <see cref="EncodeCritChance"/>.</summary>
        /// <example>
        /// <code>
        /// float critChance = WireFixedPointCodec.DecodeCritChance(receivedBytes);
        /// </code>
        /// </example>
        public static float DecodeCritChance(ReadOnlySpan<byte> source)
        {
            ushort encoded = BinaryPrimitives.ReadUInt16LittleEndian(source);
            return encoded / (float)CritChanceScale;
        }

        // ---------------------------------------------------------------------------------------
        // attackSpeedMultiplier
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Encodes <paramref name="attackSpeedMultiplier"/> (documented range 0.5–2.0) as a
        /// <see cref="ushort"/> (×1,000) into <paramref name="destination"/>. No clamp/guard — see
        /// the type-level remarks on guard scope.
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.AttackSpeedMultiplierWireSize];
        /// WireFixedPointCodec.EncodeAttackSpeedMultiplier(buffer, 1.25f);
        /// </code>
        /// </example>
        public static void EncodeAttackSpeedMultiplier(Span<byte> destination, float attackSpeedMultiplier)
        {
            ushort encoded = (ushort)Mathf.RoundToInt(attackSpeedMultiplier * AttackSpeedMultiplierScale);
            BinaryPrimitives.WriteUInt16LittleEndian(destination, encoded);
        }

        /// <summary>Decodes an <c>attackSpeedMultiplier</c> value previously written by <see cref="EncodeAttackSpeedMultiplier"/>.</summary>
        /// <example>
        /// <code>
        /// float attackSpeedMultiplier = WireFixedPointCodec.DecodeAttackSpeedMultiplier(receivedBytes);
        /// </code>
        /// </example>
        public static float DecodeAttackSpeedMultiplier(ReadOnlySpan<byte> source)
        {
            ushort encoded = BinaryPrimitives.ReadUInt16LittleEndian(source);
            return encoded / (float)AttackSpeedMultiplierScale;
        }

        // ---------------------------------------------------------------------------------------
        // cycleTimer
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Encodes <paramref name="cycleTimer"/> as a normalized <see cref="ushort"/> fraction of
        /// <paramref name="cycleDuration"/> (0–10,000) into <paramref name="destination"/>.
        /// <c>CycleDuration</c> is never transmitted — normalization is server-side only.
        /// </summary>
        /// <remarks>
        /// Pre-encode guards (CR-NET-7.2): (1) if <paramref name="cycleDuration"/> is not positive,
        /// logs an anomaly and encodes <c>0</c> as a safe fallback — rejecting/skipping the owning
        /// entity entirely is the caller's (zone-load) responsibility; this is a defensive backstop,
        /// not the primary enforcement point. (2) <paramref name="cycleTimer"/> is clamped to
        /// <c>[0, cycleDuration]</c> before normalizing; if it was out of range, the clamp is applied
        /// and an anomaly is logged — the encoded value ends up <c>10,000</c> when the input exceeded
        /// <paramref name="cycleDuration"/>.
        /// </remarks>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.CycleTimerWireSize];
        /// WireFixedPointCodec.EncodeCycleTimer(buffer, cycleTimer: 1.85f, cycleDuration: 5.0f); // 37%
        /// </code>
        /// </example>
        public static void EncodeCycleTimer(Span<byte> destination, float cycleTimer, float cycleDuration)
        {
            if (cycleDuration <= 0f)
            {
                Debug.LogWarning($"[WireFixedPointCodec] EncodeCycleTimer: cycleDuration ({cycleDuration}) must be > 0 — " +
                    "the caller (zone load) must reject/skip entities with non-positive CycleDuration before reaching " +
                    "the wire encoder. Encoding 0 as a safe fallback.");
                BinaryPrimitives.WriteUInt16LittleEndian(destination, 0);
                return;
            }

            float clamped = Mathf.Clamp(cycleTimer, 0f, cycleDuration);
            if (clamped != cycleTimer)
            {
                Debug.LogWarning($"[WireFixedPointCodec] EncodeCycleTimer: cycleTimer ({cycleTimer}) out of range " +
                    $"[0, {cycleDuration}] — clamped to {clamped} before encoding.");
            }

            float normalized = clamped / cycleDuration * CycleTimerScale;
            int roundedNormalized = Mathf.RoundToInt(normalized);
            // Defensive-only, silent: clamped <= cycleDuration guarantees normalized <= CycleTimerScale
            // mathematically; this guards against float rounding drift, not a documented anomaly.
            ushort encoded = (ushort)Mathf.Clamp(roundedNormalized, 0, CycleTimerScale);
            BinaryPrimitives.WriteUInt16LittleEndian(destination, encoded);
        }

        /// <summary>
        /// Decodes a <c>cycleTimer</c> value previously written by <see cref="EncodeCycleTimer"/> as
        /// a normalized fraction (0.0–1.0) of the original <c>CycleDuration</c>. The client multiplies
        /// this fraction by its own known cycle duration (if needed) or uses it directly as a charge
        /// bar fill amount.
        /// </summary>
        /// <example>
        /// <code>
        /// float fraction = WireFixedPointCodec.DecodeCycleTimerFraction(receivedBytes); // 0.0-1.0
        /// </code>
        /// </example>
        public static float DecodeCycleTimerFraction(ReadOnlySpan<byte> source)
        {
            ushort encoded = BinaryPrimitives.ReadUInt16LittleEndian(source);
            return encoded / (float)CycleTimerScale;
        }

        // ---------------------------------------------------------------------------------------
        // Vector3 position
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Encodes <paramref name="position"/> as 3 × <see cref="short"/> (×100, centimetre
        /// precision) into <paramref name="destination"/>, in <c>posX, posY, posZ</c> field order.
        /// </summary>
        /// <remarks>
        /// Pre-encode guard (CR-NET-7.2): each axis is clamped to
        /// <c>[-<see cref="MaxPositionMeters"/>, <see cref="MaxPositionMeters"/>]</c> before encoding,
        /// rounding to the nearest centimetre (not truncating). If any axis was out of range, a
        /// single aggregated anomaly is logged naming the clamped axis/axes (not one log per axis).
        /// The boundary value <c>(±327.67, 0, 0)</c> encodes exactly to <see cref="short.MaxValue"/>/
        /// <c>-32767</c> without overflow.
        /// </remarks>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.PositionWireSize];
        /// WireFixedPointCodec.EncodePosition(buffer, new Vector3(12.75f, 0f, -85.23f));
        /// </code>
        /// </example>
        public static void EncodePosition(Span<byte> destination, Vector3 position)
        {
            float x = ClampPositionAxis(position.x, out bool clampedX);
            float y = ClampPositionAxis(position.y, out bool clampedY);
            float z = ClampPositionAxis(position.z, out bool clampedZ);

            if (clampedX || clampedY || clampedZ)
            {
                string axes = (clampedX ? "X " : string.Empty) + (clampedY ? "Y " : string.Empty) + (clampedZ ? "Z " : string.Empty);
                Debug.LogWarning($"[WireFixedPointCodec] EncodePosition: position {position} exceeded " +
                    $"±{MaxPositionMeters}m on axis/axes {axes.Trim()} — clamped before encoding.");
            }

            short posX = (short)Mathf.RoundToInt(x * PositionScale);
            short posY = (short)Mathf.RoundToInt(y * PositionScale);
            short posZ = (short)Mathf.RoundToInt(z * PositionScale);

            BinaryPrimitives.WriteInt16LittleEndian(destination, posX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), posY);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(4, 2), posZ);
        }

        private static float ClampPositionAxis(float value, out bool wasClamped)
        {
            float clamped = Mathf.Clamp(value, -MaxPositionMeters, MaxPositionMeters);
            wasClamped = clamped != value;
            return clamped;
        }

        /// <summary>Decodes a position previously written by <see cref="EncodePosition"/>.</summary>
        /// <example>
        /// <code>
        /// Vector3 position = WireFixedPointCodec.DecodePosition(receivedBytes);
        /// </code>
        /// </example>
        public static Vector3 DecodePosition(ReadOnlySpan<byte> source)
        {
            short posX = BinaryPrimitives.ReadInt16LittleEndian(source);
            short posY = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(2, 2));
            short posZ = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(4, 2));
            return new Vector3(posX / PositionScale, posY / PositionScale, posZ / PositionScale);
        }

        // ---------------------------------------------------------------------------------------
        // Quaternion rotation
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Encodes <paramref name="rotation"/> as 4 × <see cref="short"/> (×32,767) into
        /// <paramref name="destination"/>, in <c>rotX, rotY, rotZ, rotW</c> field order.
        /// </summary>
        /// <remarks>
        /// Zero-quaternion guard (CR-NET-7.2): if <paramref name="rotation"/>'s magnitude is below
        /// the degenerate threshold, it is substituted with identity <c>(0, 0, 0, 1)</c> and an
        /// anomaly is logged <i>before</i> normalization would otherwise divide by ~zero. Otherwise
        /// the quaternion is normalized before encoding; each component is then clamped to
        /// <c>[-1, 1]</c> silently (a mathematically-guaranteed-safe float rounding backstop, not a
        /// documented anomaly).
        /// </remarks>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.RotationWireSize];
        /// WireFixedPointCodec.EncodeRotation(buffer, transform.rotation);
        /// </code>
        /// </example>
        public static void EncodeRotation(Span<byte> destination, Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);

            float nx, ny, nz, nw;
            if (magnitude < ZeroMagnitudeEpsilon)
            {
                Debug.LogWarning($"[WireFixedPointCodec] EncodeRotation: near-zero quaternion magnitude " +
                    $"({magnitude:R}) — substituting identity (0,0,0,1) before encoding.");
                nx = 0f;
                ny = 0f;
                nz = 0f;
                nw = 1f;
            }
            else
            {
                float inv = 1f / magnitude;
                nx = rotation.x * inv;
                ny = rotation.y * inv;
                nz = rotation.z * inv;
                nw = rotation.w * inv;
            }

            // Silent, defensive-only clamp — |component| <= 1 is mathematically guaranteed after
            // normalization; this only guards against float rounding drift, not a real anomaly.
            nx = Mathf.Clamp(nx, -1f, 1f);
            ny = Mathf.Clamp(ny, -1f, 1f);
            nz = Mathf.Clamp(nz, -1f, 1f);
            nw = Mathf.Clamp(nw, -1f, 1f);

            short rotX = (short)Mathf.RoundToInt(nx * RotationScale);
            short rotY = (short)Mathf.RoundToInt(ny * RotationScale);
            short rotZ = (short)Mathf.RoundToInt(nz * RotationScale);
            short rotW = (short)Mathf.RoundToInt(nw * RotationScale);

            BinaryPrimitives.WriteInt16LittleEndian(destination, rotX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), rotY);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(4, 2), rotZ);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(6, 2), rotW);
        }

        /// <summary>
        /// Decodes a rotation previously written by <see cref="EncodeRotation"/> and renormalizes it
        /// (per CR-NET-7.2, the client divides by 32,767 and renormalizes; normalization error &lt; 0.003%).
        /// </summary>
        /// <example>
        /// <code>
        /// Quaternion rotation = WireFixedPointCodec.DecodeRotation(receivedBytes);
        /// </code>
        /// </example>
        public static Quaternion DecodeRotation(ReadOnlySpan<byte> source)
        {
            short rotX = BinaryPrimitives.ReadInt16LittleEndian(source);
            short rotY = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(2, 2));
            short rotZ = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(4, 2));
            short rotW = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(6, 2));

            var raw = new Quaternion(rotX / RotationScale, rotY / RotationScale, rotZ / RotationScale, rotW / RotationScale);
            return raw.normalized;
        }

        // ---------------------------------------------------------------------------------------
        // Unit direction vector
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Encodes <paramref name="direction"/> as 3 × <see cref="short"/> (×32,767) into
        /// <paramref name="destination"/>, in <c>dirX, dirY, dirZ</c> field order.
        /// </summary>
        /// <remarks>
        /// Guard (CR-NET-7.2): <paramref name="direction"/> is normalized before encoding; if its
        /// magnitude is below the degenerate threshold, it is substituted with <c>(1, 0, 0)</c> and
        /// an anomaly is logged. Each component is then clamped to <c>[-1, 1]</c> silently (a
        /// mathematically-guaranteed-safe float rounding backstop).
        /// </remarks>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.DirectionWireSize];
        /// WireFixedPointCodec.EncodeDirection(buffer, facingDirection);
        /// </code>
        /// </example>
        public static void EncodeDirection(Span<byte> destination, Vector3 direction)
        {
            float magnitude = Mathf.Sqrt(direction.x * direction.x + direction.y * direction.y + direction.z * direction.z);

            float nx, ny, nz;
            if (magnitude < ZeroMagnitudeEpsilon)
            {
                Debug.LogWarning($"[WireFixedPointCodec] EncodeDirection: near-zero direction magnitude " +
                    $"({magnitude:R}) — substituting (1,0,0) before encoding.");
                nx = 1f;
                ny = 0f;
                nz = 0f;
            }
            else
            {
                float inv = 1f / magnitude;
                nx = direction.x * inv;
                ny = direction.y * inv;
                nz = direction.z * inv;
            }

            // Silent, defensive-only clamp — same rationale as EncodeRotation.
            nx = Mathf.Clamp(nx, -1f, 1f);
            ny = Mathf.Clamp(ny, -1f, 1f);
            nz = Mathf.Clamp(nz, -1f, 1f);

            short dirX = (short)Mathf.RoundToInt(nx * DirectionScale);
            short dirY = (short)Mathf.RoundToInt(ny * DirectionScale);
            short dirZ = (short)Mathf.RoundToInt(nz * DirectionScale);

            BinaryPrimitives.WriteInt16LittleEndian(destination, dirX);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(2, 2), dirY);
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(4, 2), dirZ);
        }

        /// <summary>Decodes a unit direction vector previously written by <see cref="EncodeDirection"/>.</summary>
        /// <example>
        /// <code>
        /// Vector3 direction = WireFixedPointCodec.DecodeDirection(receivedBytes);
        /// </code>
        /// </example>
        public static Vector3 DecodeDirection(ReadOnlySpan<byte> source)
        {
            short dirX = BinaryPrimitives.ReadInt16LittleEndian(source);
            short dirY = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(2, 2));
            short dirZ = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(4, 2));
            return new Vector3(dirX / DirectionScale, dirY / DirectionScale, dirZ / DirectionScale);
        }

        // ---------------------------------------------------------------------------------------
        // Plain int/uint passthrough writers — finalDamage, and HP/gold/XP/stat totals after the
        // caller has already applied Mathf.FloorToInt. NOT a fixed-point encoding — these are
        // already-integer-domain values crossing the wire boundary as-is (CR-NET-7.2).
        // ---------------------------------------------------------------------------------------

        /// <summary>Writes a plain <see cref="int"/> (little-endian) into <paramref name="destination"/>. No encoding — already-integer-domain passthrough (e.g. <c>finalDamage</c>).</summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.Int32WireSize];
        /// WireFixedPointCodec.WriteInt32(buffer, damageResult.finalDamage);
        /// </code>
        /// </example>
        public static void WriteInt32(Span<byte> destination, int value) => BinaryPrimitives.WriteInt32LittleEndian(destination, value);

        /// <summary>Reads a plain <see cref="int"/> (little-endian) previously written by <see cref="WriteInt32"/>.</summary>
        /// <example>
        /// <code>
        /// int finalDamage = WireFixedPointCodec.ReadInt32(receivedBytes);
        /// </code>
        /// </example>
        public static int ReadInt32(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadInt32LittleEndian(source);

        /// <summary>Writes a plain <see cref="uint"/> (little-endian) into <paramref name="destination"/>. No encoding — already-integer-domain passthrough (e.g. gold balance).</summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[WireFixedPointCodec.UInt32WireSize];
        /// WireFixedPointCodec.WriteUInt32(buffer, newGoldBalance);
        /// </code>
        /// </example>
        public static void WriteUInt32(Span<byte> destination, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(destination, value);

        /// <summary>Reads a plain <see cref="uint"/> (little-endian) previously written by <see cref="WriteUInt32"/>.</summary>
        /// <example>
        /// <code>
        /// uint goldBalance = WireFixedPointCodec.ReadUInt32(receivedBytes);
        /// </code>
        /// </example>
        public static uint ReadUInt32(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadUInt32LittleEndian(source);
    }
}
