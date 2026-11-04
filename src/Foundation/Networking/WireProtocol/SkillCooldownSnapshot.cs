using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// One positional entry of a <see cref="SkillCooldownSnapshot"/>: a skill id and the absolute server tick at which
    /// its cooldown ends. A padding entry (class with fewer than 10 skills) is <c>(0, 0)</c>.
    /// </summary>
    public readonly struct SkillCooldownEntry : IEquatable<SkillCooldownEntry>
    {
        /// <summary>The raw skill id; 0 for a padding entry.</summary>
        public readonly uint SkillId;

        /// <summary>The absolute server tick at which the cooldown ends; 0 when locked, never cast or padding.</summary>
        public readonly uint CooldownExpiryTick;

        /// <summary>Initializes a new <see cref="SkillCooldownEntry"/>.</summary>
        /// <param name="skillId">The raw skill id.</param>
        /// <param name="cooldownExpiryTick">The absolute server tick at which the cooldown ends.</param>
        public SkillCooldownEntry(uint skillId, uint cooldownExpiryTick)
        {
            SkillId = skillId;
            CooldownExpiryTick = cooldownExpiryTick;
        }

        /// <inheritdoc/>
        public bool Equals(SkillCooldownEntry other)
            => SkillId == other.SkillId && CooldownExpiryTick == other.CooldownExpiryTick;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SkillCooldownEntry other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (SkillId.GetHashCode() * 397) ^ CooldownExpiryTick.GetHashCode();

        /// <summary>Returns true if both entries have identical field values.</summary>
        public static bool operator ==(SkillCooldownEntry left, SkillCooldownEntry right) => left.Equals(right);

        /// <summary>Returns true if the entries differ in any field.</summary>
        public static bool operator !=(SkillCooldownEntry left, SkillCooldownEntry right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"SkillCooldownEntry(Skill={SkillId}, ExpiryTick={CooldownExpiryTick})";
    }

    /// <summary>
    /// R-OD message sent to the joining client after <c>SessionReady</c>, carrying the cooldown state of all 10 skill
    /// slots (skill-system.md CR-SK-12, CR-SK-23; Skill System Story 010).
    /// </summary>
    /// <remarks>
    /// Wire layout (little-endian, 80-byte body): 10 entries of <c>uint skillID</c>, <c>uint cooldownExpiryTick</c>,
    /// positionally aligned with <c>IClassRegistry.GetClassSkills</c>. Always exactly 10 entries. The entries are held
    /// in fixed fields, so a snapshot allocates nothing. Mutable by design (filled with <see cref="SetEntry"/>).
    /// <see cref="MessageTypeId"/> <c>0xE150</c>. Codec: <see cref="SkillCooldownSnapshotCodec"/>.
    /// </remarks>
    public struct SkillCooldownSnapshot
    {
        /// <summary>Number of entries on the wire: 10.</summary>
        public const int EntryCount = 10;

        /// <summary>Wire size of one entry: 8 bytes.</summary>
        public const int EntrySize = 8;

        /// <summary>Wire size of the body: <see cref="EntryCount"/> x <see cref="EntrySize"/> = 80 bytes.</summary>
        public const int BodySize = EntryCount * EntrySize;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="SkillCooldownSnapshot"/> (R-OD priority block).</summary>
        public const ushort MessageTypeId = 0xE150;

        private SkillCooldownEntry _e0;
        private SkillCooldownEntry _e1;
        private SkillCooldownEntry _e2;
        private SkillCooldownEntry _e3;
        private SkillCooldownEntry _e4;
        private SkillCooldownEntry _e5;
        private SkillCooldownEntry _e6;
        private SkillCooldownEntry _e7;
        private SkillCooldownEntry _e8;
        private SkillCooldownEntry _e9;

        /// <summary>Reads entry <paramref name="index"/>.</summary>
        /// <param name="index">The slot index in [0, <see cref="EntryCount"/>).</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the range.</exception>
        public SkillCooldownEntry GetEntry(int index)
        {
            switch (index)
            {
                case 0: return _e0;
                case 1: return _e1;
                case 2: return _e2;
                case 3: return _e3;
                case 4: return _e4;
                case 5: return _e5;
                case 6: return _e6;
                case 7: return _e7;
                case 8: return _e8;
                case 9: return _e9;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "must be in [0, 10)");
            }
        }

        /// <summary>Writes entry <paramref name="index"/>.</summary>
        /// <param name="index">The slot index in [0, <see cref="EntryCount"/>).</param>
        /// <param name="entry">The entry to store.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the range.</exception>
        public void SetEntry(int index, in SkillCooldownEntry entry)
        {
            switch (index)
            {
                case 0: _e0 = entry; break;
                case 1: _e1 = entry; break;
                case 2: _e2 = entry; break;
                case 3: _e3 = entry; break;
                case 4: _e4 = entry; break;
                case 5: _e5 = entry; break;
                case 6: _e6 = entry; break;
                case 7: _e7 = entry; break;
                case 8: _e8 = entry; break;
                case 9: _e9 = entry; break;
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "must be in [0, 10)");
            }
        }
    }
}
