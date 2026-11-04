using UnityEngine;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Authored class data: per-class auto-allocation, free points and skill list (class-system.md CD-1, CD-2;
    /// Skill System Story 012). Fills a <see cref="ClassRegistry"/>; the zone composition loading it is Skill
    /// System Story 013.
    /// </summary>
    /// <example>
    /// <code>
    /// var registry = new ClassRegistry();
    /// classDatabase.RegisterAll(registry);
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "ClassDatabase", menuName = "IronGrind/Class Database")]
    public sealed class ClassDatabase : ScriptableObject
    {
        [SerializeField] private ClassDefinitionEntry[] _classes;

        /// <summary>Number of authored class entries.</summary>
        public int Count => _classes == null ? 0 : _classes.Length;

        /// <summary>The entry at an index, in authored order.</summary>
        /// <param name="index">Index in [0, <see cref="Count"/>).</param>
        /// <returns>A copy of the entry; its skill list array is the asset's, do not modify it.</returns>
        public ClassDefinitionEntry GetEntry(int index) => _classes[index];

        /// <summary>
        /// Registers every entry with <paramref name="registry"/> under its <see cref="ClassDefinitionEntry.ClassType"/>
        /// (a later entry for the same class replaces an earlier one, as <see cref="ClassRegistry.RegisterClass"/> does).
        /// </summary>
        /// <param name="registry">The registry to fill.</param>
        public void RegisterAll(ClassRegistry registry)
        {
            if (registry == null)
            {
                throw new System.ArgumentNullException(nameof(registry));
            }

            if (_classes == null)
            {
                return;
            }

            for (int i = 0; i < _classes.Length; i++)
            {
                registry.RegisterClass((byte)_classes[i].ClassType, _classes[i].ToClassDefinition());
            }
        }

        /// <summary>Test seam: replaces the entries.</summary>
        internal void SetForTesting(ClassDefinitionEntry[] classes)
        {
            _classes = classes;
        }
    }
}
