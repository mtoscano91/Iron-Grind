using System;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message carrying an authoritative, absolute gold balance for a character
    /// (CR-NET-7's Message Schemas section). <see cref="NewBalance"/> is always the character's
    /// full post-mutation balance — <b>never a delta</b> — a hard invariant checked by AC-NC-19
    /// and reiterated project-wide (ADR-010, Currency System stories).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation on the hot serialization path.
    /// Encoded/decoded via <see cref="BatchSubMessageCodec.WriteGoldSyncEvent"/> /
    /// <see cref="BatchSubMessageCodec.TryReadGoldSyncEvent"/>. <see cref="Version"/> pairs with
    /// <see cref="StaleDiscardComparer.IsNewerVersion"/> on the client to discard reordered
    /// deliveries (CR-NET-7.5).
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> same situation as <see cref="DamageEvent.MessageTypeId"/>
    /// — not formally registered in ADR-004. <c>0x0520</c> is adopted here because it matches the
    /// <c>characterId</c> field example's <c>messageTypeId</c> already used, unchanged, in
    /// <see cref="WireIdCodec.SerializeCharacterId"/>'s existing XML doc-comment example (Story
    /// 004), i.e. the same "already the de-facto convention, never previously named" situation.
    /// Treat as provisional until formally registered.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var goldSync = new GoldSyncEvent(
    ///     characterId: new CharacterID(7), newBalance: 250u, version: 3u, reason: GoldTransactionReason.MonsterDrop);
    /// Span&lt;byte&gt; buffer = stackalloc byte[GoldSyncEvent.BatchSize];
    /// int written = BatchSubMessageCodec.WriteGoldSyncEvent(buffer, in goldSync);
    /// </code>
    /// </example>
    public readonly struct GoldSyncEvent : IEquatable<GoldSyncEvent>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+4+4+1 = 13 bytes.</summary>
        public const int BodySize = 13;

        /// <summary>Total wire size as an R-U batch sub-message: <see cref="BatchSubMessageFraming.SubMessageHeaderSize"/> + <see cref="BodySize"/> = 17 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="GoldSyncEvent"/> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0x0520;

        /// <summary>The character whose balance changed. Must not be <see cref="CharacterID.Invalid"/> (CR-NET-7.3 zero-write guard).</summary>
        public readonly CharacterID CharacterId;

        /// <summary>The absolute post-mutation gold balance — never a delta (AC-NC-19 hard invariant).</summary>
        public readonly uint NewBalance;

        /// <summary>Monotonically increasing per-character mutation counter, used for stale-discard comparison (CR-NET-7.5) on the client.</summary>
        public readonly uint Version;

        /// <summary>Audit reason for this mutation.</summary>
        public readonly GoldTransactionReason Reason;

        /// <summary>Initializes a new <see cref="GoldSyncEvent"/>.</summary>
        /// <example>
        /// <code>
        /// var goldSync = new GoldSyncEvent(new CharacterID(7), 250u, 3u, GoldTransactionReason.MonsterDrop);
        /// </code>
        /// </example>
        public GoldSyncEvent(CharacterID characterId, uint newBalance, uint version, GoldTransactionReason reason)
        {
            CharacterId = characterId;
            NewBalance = newBalance;
            Version = version;
            Reason = reason;
        }

        /// <inheritdoc/>
        public bool Equals(GoldSyncEvent other)
            => CharacterId == other.CharacterId
            && NewBalance == other.NewBalance
            && Version == other.Version
            && Reason == other.Reason;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GoldSyncEvent other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = CharacterId.GetHashCode();
            h = (h * 397) ^ NewBalance.GetHashCode();
            h = (h * 397) ^ Version.GetHashCode();
            h = (h * 397) ^ Reason.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both events have identical field values.</summary>
        public static bool operator ==(GoldSyncEvent left, GoldSyncEvent right) => left.Equals(right);

        /// <summary>Returns true if the events differ in any field.</summary>
        public static bool operator !=(GoldSyncEvent left, GoldSyncEvent right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"GoldSyncEvent(CharacterId={CharacterId}, NewBalance={NewBalance}, Version={Version}, Reason={Reason})";
    }
}
