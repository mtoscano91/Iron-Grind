using System;
using System.Globalization;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Identifies a skill. A wire type (the client receives it), so it lives in <c>IronGrind.Foundation</c> (ADR-012).
    /// <c>SkillID(0)</c> is <see cref="Invalid"/>, reserved (skill-system.md SkillDefinition Schema, AC-SK-33).
    /// </summary>
    public readonly struct SkillID : IEquatable<SkillID>
    {
        /// <summary>The raw id. 0 is reserved for <see cref="Invalid"/>.</summary>
        public readonly uint Value;

        /// <summary>Creates a skill id from a raw value.</summary>
        /// <param name="value">The raw id; 0 is <see cref="Invalid"/>.</param>
        public SkillID(uint value)
        {
            Value = value;
        }

        /// <summary>The reserved invalid id (0).</summary>
        public static readonly SkillID Invalid = new SkillID(0);

        /// <summary>True when <see cref="Value"/> is not 0.</summary>
        public bool IsValid => Value != 0;

        /// <inheritdoc/>
        public bool Equals(SkillID other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SkillID other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Value equality.</summary>
        public static bool operator ==(SkillID left, SkillID right) => left.Value == right.Value;

        /// <summary>Value inequality.</summary>
        public static bool operator !=(SkillID left, SkillID right) => left.Value != right.Value;

        /// <inheritdoc/>
        public override string ToString() => "SkillID(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
