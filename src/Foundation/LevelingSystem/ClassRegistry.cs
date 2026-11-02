using System;
using System.Collections.Generic;
using IronGrind.SkillSystem;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Caller-supplied in-memory <see cref="IClassRegistry"/> implementation. Mirrors the
    /// same "test-local/caller-supplied state for not-yet-built subsystems" idiom as
    /// <c>LevelingService.RegisterPlayerEntity</c> (this epic) and
    /// <c>PartyDisbandCoordinator</c> (Networking Core Story 020) — nothing here is the real
    /// Class System, it is a stand-in a caller populates with whatever class data it needs.
    /// </summary>
    public sealed class ClassRegistry : IClassRegistry
    {
        private readonly Dictionary<byte, ClassDefinition> _classes = new Dictionary<byte, ClassDefinition>();
        private readonly Dictionary<byte, SkillID[]> _skills = new Dictionary<byte, SkillID[]>();

        /// <summary>
        /// Registers or overwrites the <see cref="ClassDefinition"/> for <paramref name="classType"/>. The
        /// definition's <c>SkillList</c> is copied into a cached <see cref="SkillID"/> array, so later changes to
        /// the caller's array have no effect.
        /// </summary>
        public void RegisterClass(byte classType, ClassDefinition definition)
        {
            _classes[classType] = definition;

            uint[] raw = definition.SkillList;
            if (raw == null || raw.Length == 0)
            {
                _skills.Remove(classType);
                return;
            }

            var wrapped = new SkillID[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                wrapped[i] = new SkillID(raw[i]);
            }

            _skills[classType] = wrapped;
        }

        /// <inheritdoc/>
        public bool TryGetClass(byte classType, out ClassDefinition definition)
            => _classes.TryGetValue(classType, out definition);

        /// <inheritdoc/>
        public IReadOnlyList<SkillID> GetClassSkills(byte classType)
            => _skills.TryGetValue(classType, out SkillID[] skills) ? skills : Array.Empty<SkillID>();
    }
}
