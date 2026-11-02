using System.Collections.Generic;
using IronGrind.SkillSystem;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Read-only lookup from a class type byte to its <see cref="ClassDefinition"/>.
    /// Forward-dependency stand-in for the real Class System epic — see
    /// <see cref="ClassDefinition"/> remarks and <see cref="ClassRegistry"/> for the
    /// caller-supplied mock implementation this codebase's tests use today.
    /// </summary>
    public interface IClassRegistry
    {
        /// <summary>
        /// Returns <see langword="true"/> and writes <paramref name="definition"/> when
        /// <paramref name="classType"/> is a known class. Returns <see langword="false"/>
        /// (and a default <see cref="ClassDefinition"/>) otherwise.
        /// </summary>
        bool TryGetClass(byte classType, out ClassDefinition definition);

        /// <summary>
        /// The class's skills in authored order (Class System CD-4; Skill System AC-SK-35). Never null: empty for
        /// an unknown class, a null list or an empty list (EC-CS-8). Callers cast <see cref="ClassType"/> to byte.
        /// The list is the registry's cache: read it only, and iterate with an index loop over <c>Count</c>
        /// (a <c>foreach</c> over the interface allocates an enumerator).
        /// </summary>
        /// <param name="classType">The class type byte.</param>
        /// <returns>The skill ids; never null.</returns>
        IReadOnlyList<SkillID> GetClassSkills(byte classType);
    }
}
