using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// U-U CycleBroadcast-packet sub-message reporting one zone entity's auto-attack cycle
    /// position, normalized to a 0–10,000 fraction (CR-NET-7's Message Schemas section). One
    /// instance is sent per zone entity other than the receiving client, every tick, via
    /// <see cref="CycleBroadcastPacketWriter"/> (<c>TICK_BATCH_UU_CYCLE = 0x0103</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation on the hot serialization path.
    /// Encoded/decoded via <see cref="BatchSubMessageCodec.WriteCycleTimerBroadcast"/> /
    /// <see cref="BatchSubMessageCodec.TryReadCycleTimerBroadcast"/>.
    /// </para>
    /// <para>
    /// <b>Never dropped</b> — this packet drives the Rhythm Mastery core gameplay pillar (Pillar
    /// 2). <see cref="CycleBroadcastPacketWriter"/> throws rather than truncating if the caller
    /// supplies more entries than fit within <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/>
    /// (a <see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/> misconfiguration, not a runtime
    /// network condition).
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> the GDD's own wire-format diagram
    /// literally writes <c>[0x????]</c> for this sub-message's <c>MessageTypeID</c> (an
    /// unassigned placeholder in the source document itself, not just an omission in this
    /// summary), and no other file in this codebase establishes a de-facto convention for it
    /// (unlike <see cref="DamageEvent"/>/<see cref="GoldSyncEvent"/>, which reuse values already
    /// present in Story 004's doc-comment examples). <c>0x0302</c> is chosen here purely to sit
    /// adjacent to <see cref="DamageEvent.MessageTypeId"/> (<c>0x0301</c>) in the <c>0x0200–0xDFFF</c>
    /// application range and to avoid colliding with any other constant declared in this folder.
    /// This is a genuinely unassigned wire primitive — flag for formal registration in a future
    /// Networking ADR amendment or ID registry before cross-team/client implementations rely on
    /// this exact value.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var cycle = new CycleTimerBroadcast(entityId: new EntityID(5), cycleTimer: 4200);
    /// Span&lt;byte&gt; buffer = stackalloc byte[CycleTimerBroadcast.BatchSize];
    /// int written = BatchSubMessageCodec.WriteCycleTimerBroadcast(buffer, in cycle);
    /// </code>
    /// </example>
    public readonly struct CycleTimerBroadcast : IEquatable<CycleTimerBroadcast>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+2 = 6 bytes.</summary>
        public const int BodySize = 6;

        /// <summary>Total wire size as a batch sub-message: <see cref="BatchSubMessageFraming.SubMessageHeaderSize"/> + <see cref="BodySize"/> = 10 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="CycleTimerBroadcast"/> — see type-level remarks (genuinely unassigned in the source GDD).</summary>
        public const ushort MessageTypeId = 0x0302;

        /// <summary>The entity whose cycle timer this reports. Must not be <see cref="EntityID.Invalid"/> (CR-NET-7.3 zero-write guard).</summary>
        public readonly EntityID EntityId;

        /// <summary>Normalized auto-attack cycle position, in the range 0–10,000.</summary>
        public readonly ushort CycleTimer;

        /// <summary>Initializes a new <see cref="CycleTimerBroadcast"/>.</summary>
        /// <example>
        /// <code>
        /// var cycle = new CycleTimerBroadcast(new EntityID(5), 4200);
        /// </code>
        /// </example>
        public CycleTimerBroadcast(EntityID entityId, ushort cycleTimer)
        {
            EntityId = entityId;
            CycleTimer = cycleTimer;
        }

        /// <inheritdoc/>
        public bool Equals(CycleTimerBroadcast other) => EntityId == other.EntityId && CycleTimer == other.CycleTimer;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CycleTimerBroadcast other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ CycleTimer.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both broadcasts have identical field values.</summary>
        public static bool operator ==(CycleTimerBroadcast left, CycleTimerBroadcast right) => left.Equals(right);

        /// <summary>Returns true if the broadcasts differ in any field.</summary>
        public static bool operator !=(CycleTimerBroadcast left, CycleTimerBroadcast right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"CycleTimerBroadcast(EntityId={EntityId}, CycleTimer={CycleTimer})";
    }
}
