using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Range-check-and-substitute guards for wire-transmitted game-message enums (CR-NET-7.4/7.9),
    /// plus the range-check-and-reject guard for <see cref="StatID"/> (a different policy — see
    /// remarks). Never uses <see cref="System.Enum.IsDefined(System.Type, object)"/> — forbidden
    /// on IL2CPP (CR-NET-7.4); all checks are explicit numeric comparisons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two distinct policies live in this class — do not conflate them:</b>
    /// </para>
    /// <para>
    /// <b>Substitute-and-continue</b> (<see cref="DecodeDamageType"/>, <see cref="DecodeDisconnectReason"/>,
    /// <see cref="DecodeDisconnectType"/>): an out-of-range byte does <i>not</i> drop the message.
    /// It logs an anomaly and returns a documented fallback value, and the caller proceeds with
    /// that value as if it were legitimately received (CR-NET-7.9).
    /// </para>
    /// <para>
    /// <b>Reject</b> (<see cref="TryValidateStatID"/>): an out-of-range byte must cause the whole
    /// message to be treated as dropped — no stat mutation may be applied (AC-NC-18). This method
    /// returns <see langword="false"/> instead of substituting a usable value, giving the (future)
    /// caller an unambiguous "reject, do not apply" signal. There is no message dispatcher/handler
    /// in this codebase yet (Networking Core Stories 025-027 own generic dispatch; the
    /// <c>AllocateFreePointRequest</c> handler belongs to a future Leveling/CharacterStats epic) —
    /// this method proves the guard's contract at the serialization boundary, same pattern Story
    /// 003 used for AC-NC-03.
    /// </para>
    /// <para>
    /// <b>Range shapes differ per enum</b> — <see cref="DamageType"/> and <see cref="DisconnectType"/>
    /// have contiguous valid ranges (a plain <c>rawByte &gt; maxDeclaredValue</c> check suffices).
    /// <see cref="DisconnectReason"/>'s valid set is the non-contiguous <c>{0, 1, 2, 3, 255}</c> and
    /// is validated by an explicit switch, not a range comparison. <see cref="StatID"/>'s valid
    /// range is contiguous (<c>0..16</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// DamageType damageType = WireEnumCodec.DecodeDamageType(rawByte, messageTypeId: 0x0301);
    /// if (WireEnumCodec.TryValidateStatID(rawStatIdByte, messageTypeId: 0xE050, out StatID statId))
    /// {
    ///     // apply the free-point allocation to statId
    /// }
    /// // else: drop the message, no stat change applied (AC-NC-18)
    /// </code>
    /// </example>
    public static class WireEnumCodec
    {
        private const byte DamageTypeMaxValue = (byte)DamageType.True;
        private const byte DisconnectTypeMaxValue = (byte)DisconnectType.ZoneTransfer;
        private const byte StatIdMaxValue = (byte)StatID.MovementSpeed;

        // ---------------------------------------------------------------------------------------
        // DamageType — contiguous range, substitute Physical=0.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Range-checks <paramref name="rawByte"/> against the declared <see cref="DamageType"/>
        /// range (0–2) and casts. An out-of-range byte substitutes <see cref="DamageType.Physical"/>
        /// and logs an anomaly — the message is <i>not</i> dropped (CR-NET-7.9).
        /// </summary>
        /// <example>
        /// <code>
        /// DamageType damageType = WireEnumCodec.DecodeDamageType(rawByte: 100, messageTypeId: 0x0301);
        /// // damageType == DamageType.Physical; anomaly logged.
        /// </code>
        /// </example>
        public static DamageType DecodeDamageType(byte rawByte, ushort messageTypeId)
        {
            if (rawByte > DamageTypeMaxValue)
            {
                Debug.LogWarning($"[WireEnumCodec] DecodeDamageType: received out-of-range byte {rawByte} " +
                    $"for messageTypeId={messageTypeId} — substituting Physical (0) and continuing (CR-NET-7.9).");
                return DamageType.Physical;
            }

            return (DamageType)rawByte;
        }

        // ---------------------------------------------------------------------------------------
        // DisconnectType — contiguous range, substitute Timeout=1.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Range-checks <paramref name="rawByte"/> against the declared <see cref="DisconnectType"/>
        /// range (0–2) and casts. An out-of-range byte substitutes <see cref="DisconnectType.Timeout"/>
        /// and logs an anomaly — the entity is still despawned rather than the message being dropped
        /// (CR-NET-7.9).
        /// </summary>
        /// <example>
        /// <code>
        /// DisconnectType disconnectType = WireEnumCodec.DecodeDisconnectType(rawByte: 50, messageTypeId: 0x0520);
        /// // disconnectType == DisconnectType.Timeout; anomaly logged.
        /// </code>
        /// </example>
        public static DisconnectType DecodeDisconnectType(byte rawByte, ushort messageTypeId)
        {
            if (rawByte > DisconnectTypeMaxValue)
            {
                Debug.LogWarning($"[WireEnumCodec] DecodeDisconnectType: received out-of-range byte {rawByte} " +
                    $"for messageTypeId={messageTypeId} — substituting Timeout (1) and continuing (CR-NET-7.9).");
                return DisconnectType.Timeout;
            }

            return (DisconnectType)rawByte;
        }

        // ---------------------------------------------------------------------------------------
        // DisconnectReason — non-contiguous valid set {0,1,2,3,255}, substitute Other=255.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Validates <paramref name="rawByte"/> against the non-contiguous <see cref="DisconnectReason"/>
        /// valid set <c>{0, 1, 2, 3, 255}</c> via an explicit switch (never a range comparison —
        /// <c>255</c> is a valid outlier, not the top of a contiguous range) and casts. A byte
        /// outside this set substitutes <see cref="DisconnectReason.Other"/> and logs an anomaly —
        /// the message is <i>not</i> dropped; zone teardown still processes (CR-NET-7.9).
        /// </summary>
        /// <example>
        /// <code>
        /// DisconnectReason reason = WireEnumCodec.DecodeDisconnectReason(rawByte: 100, messageTypeId: 0x0530);
        /// // reason == DisconnectReason.Other; anomaly logged.
        /// </code>
        /// </example>
        public static DisconnectReason DecodeDisconnectReason(byte rawByte, ushort messageTypeId)
        {
            switch (rawByte)
            {
                case (byte)DisconnectReason.ZoneClosed:
                    return DisconnectReason.ZoneClosed;
                case (byte)DisconnectReason.ServerShutdown:
                    return DisconnectReason.ServerShutdown;
                case (byte)DisconnectReason.AdminKick:
                    return DisconnectReason.AdminKick;
                case (byte)DisconnectReason.GhostDeath:
                    return DisconnectReason.GhostDeath;
                case (byte)DisconnectReason.Other:
                    return DisconnectReason.Other;
                default:
                    Debug.LogWarning($"[WireEnumCodec] DecodeDisconnectReason: received out-of-range byte {rawByte} " +
                        $"for messageTypeId={messageTypeId} — substituting Other (255) and continuing (CR-NET-7.9).");
                    return DisconnectReason.Other;
            }
        }

        // ---------------------------------------------------------------------------------------
        // StatID — contiguous range 0..16, REJECT (not substitute) on out-of-range.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Range-checks <paramref name="rawByte"/> against the declared <see cref="StatID"/> range
        /// (0–16, <see cref="StatID.MovementSpeed"/> being the highest declared member). Unlike
        /// <see cref="DecodeDamageType"/>/<see cref="DecodeDisconnectReason"/>/<see cref="DecodeDisconnectType"/>,
        /// this method never substitutes a fallback value — an out-of-range byte logs an anomaly
        /// and returns <see langword="false"/>, signalling the caller to drop the whole message and
        /// apply no stat change (AC-NC-18).
        /// </summary>
        /// <example>
        /// <code>
        /// if (WireEnumCodec.TryValidateStatID(rawByte: 0xFF, messageTypeId: 0xE050, out StatID statId))
        /// {
        ///     // not reached — 0xFF is out of range
        /// }
        /// else
        /// {
        ///     // drop the message; no stat mutation applied (AC-NC-18)
        /// }
        /// </code>
        /// </example>
        public static bool TryValidateStatID(byte rawByte, ushort messageTypeId, out StatID validated)
        {
            if (rawByte <= StatIdMaxValue)
            {
                validated = (StatID)rawByte;
                return true;
            }

            Debug.LogWarning($"[WireEnumCodec] TryValidateStatID: received out-of-range byte {rawByte} " +
                $"for messageTypeId={messageTypeId} — rejecting the message; no stat change applied (AC-NC-18).");
            validated = default;
            return false;
        }
    }
}
