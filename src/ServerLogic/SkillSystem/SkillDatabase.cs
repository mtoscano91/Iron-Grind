using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Authored skill roster (skill-system.md SkillDefinition Schema; Skill System Story 001). Holds a
    /// <see cref="SkillDefinition"/> array and an id lookup built once on first use.
    /// </summary>
    /// <example>
    /// <code>
    /// if (database.TryGetDefinition(new SkillID(3), out SkillDefinition skill)) { /* use skill */ }
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "SkillDatabase", menuName = "IronGrind/Skill Database")]
    public sealed class SkillDatabase : ScriptableObject
    {
        [SerializeField] private SkillDefinition[] _skills;

        private Dictionary<uint, int> _indexById;

        /// <summary>
        /// Finds a skill by id. Returns false (and a default definition) for <see cref="SkillID.Invalid"/> and
        /// for an unknown id. With duplicate ids the first entry wins.
        /// </summary>
        /// <param name="id">The skill id.</param>
        /// <param name="definition">The definition when found.</param>
        /// <returns>True when found.</returns>
        public bool TryGetDefinition(SkillID id, out SkillDefinition definition)
        {
            definition = default;
            if (!id.IsValid)
            {
                return false;
            }

            if (_indexById == null)
            {
                BuildLookup();
            }

            if (_indexById.TryGetValue(id.Value, out int index))
            {
                definition = _skills[index];
                return true;
            }

            return false;
        }

        /// <summary>Test seam: replaces the skills and resets the lookup.</summary>
        internal void SetForTesting(SkillDefinition[] skills)
        {
            _skills = skills;
            _indexById = null;
        }

        private void OnEnable()
        {
            _indexById = null;
        }

#if UNITY_EDITOR
        // Editor feedback: rebuilds the lookup so a reserved or duplicate id is reported when the asset is edited,
        // not at the first runtime lookup.
        private void OnValidate()
        {
            BuildLookup();
        }
#endif

        // Fills a local dictionary and publishes it at the end, so a reader never sees a half-built lookup.
        private void BuildLookup()
        {
            var indexById = new Dictionary<uint, int>();
            if (_skills == null)
            {
                _indexById = indexById;
                return;
            }

            for (int i = 0; i < _skills.Length; i++)
            {
                uint id = _skills[i].ID;
                if (id == 0)
                {
                    Debug.LogError("[SkillDatabase] entry " + i.ToString(CultureInfo.InvariantCulture)
                        + " has reserved ID 0 (SkillID.Invalid); skipped");
                    continue;
                }

                if (indexById.ContainsKey(id))
                {
                    Debug.LogError("[SkillDatabase] duplicate skill ID " + id.ToString(CultureInfo.InvariantCulture)
                        + " at entry " + i.ToString(CultureInfo.InvariantCulture) + "; first entry wins");
                    continue;
                }

                indexById.Add(id, i);
            }

            _indexById = indexById;
        }
    }
}
