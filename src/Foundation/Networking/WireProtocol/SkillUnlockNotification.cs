using System;
using IronGrind.SkillSystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-OD message telling the owning client that a skill unlocked on level-up (skill-system.md CR-SK-21;
    /// networking-wire-protocol.md SkillUnlockNotification; Skill System Story 003). Drives the unlock animation and
    /// the "NEW" badge in the HUD skill bar.
    /// </summary>
    /// <remarks>
    /// Wire layout (little-endian): <c>uint skillID</c> = 4-byte body, 14 bytes standalone. The Skill System epic
    /// reserves the R-OD priority block 0xE150-0xE15F: 0xE150 SkillCooldownSnapshot, 0xE151 this message (the R-U and
    /// U-U skill messages use 0x0308-0x030A). Codec: <see cref="SkillUnlockNotificationCodec"/>.
    /// </remarks>
    public readonly struct SkillUnlockNotification : IEquatable<SkillUnlockNotification>
    {
        /// <summary>Wire size of the body: 4 bytes.</summary>
        public const int BodySize = 4;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="SkillUnlockNotification"/>.</summary>
        public const ushort MessageTypeId = 0xE151;

        /// <summary>The skill that unlocked.</summary>
        public readonly SkillID SkillId;

        /// <summary>Initializes a new <see cref="SkillUnlockNotification"/>.</summary>
        /// <param name="skillId">The skill that unlocked.</param>
        public SkillUnlockNotification(SkillID skillId)
        {
            SkillId = skillId;
        }

        /// <inheritdoc/>
        public bool Equals(SkillUnlockNotification other) => SkillId == other.SkillId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SkillUnlockNotification other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => SkillId.GetHashCode();

        /// <summary>Returns true if both carry the same skill id.</summary>
        public static bool operator ==(SkillUnlockNotification left, SkillUnlockNotification right) => left.Equals(right);

        /// <summary>Returns true if the skill ids differ.</summary>
        public static bool operator !=(SkillUnlockNotification left, SkillUnlockNotification right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => "SkillUnlockNotification(" + SkillId + ")";
    }
}
