using System.Collections.Generic;
using System.Globalization;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Authored skill roster (skill-system.md SkillDefinition Schema; Skill System Story 001). Holds a
    /// <see cref="SkillDefinition"/> array and an id lookup built once on first use.
    /// </summary>
    /// <remarks>
    /// Main thread only: the lookup is built lazily on first use (and the buff definitions with it) without locking.
    /// The zone simulation reads it from its single tick thread.
    /// </remarks>
    /// <example>
    /// <code>
    /// if (database.TryGetDefinition(new SkillID(3), out SkillDefinition skill)) { /* use skill */ }
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "SkillDatabase", menuName = "IronGrind/Skill Database")]
    public sealed class SkillDatabase : ScriptableObject, ISkillDefinitionSource
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

        /// <summary>Number of authored entries, including any skipped by the lookup (reserved or duplicate id).</summary>
        public int Count => _skills == null ? 0 : _skills.Length;

        /// <summary>Test seam: the entry at an index, with its buff definition built (Story 012 roster test).</summary>
        internal SkillDefinition GetEntryForTesting(int index)
        {
            if (_indexById == null)
            {
                BuildLookup();
            }

            return _skills[index];
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
                BuildBuffDefinition(i);
            }

            _indexById = indexById;
        }

        // Story 012: builds the runtime BuffDefinition from the authored BuffData. An entry without BuffData keeps
        // the BuffDefinition it was given (in-memory definitions in tests). Invalid data is reported and leaves the
        // definition empty, which Status Effects rejects at cast time (NoOpDefinition / InvalidDuration).
        private void BuildBuffDefinition(int index)
        {
            BuffDefinitionData data = _skills[index].BuffData;
            // Editor note: clearing BuffData in the Inspector leaves the previously built BuffDefinition until the next
            // domain reload; it cannot be reset here without breaking in-memory definitions (tests) that set it directly.
            if (!data.IsAuthored)
            {
                return;
            }

            uint id = _skills[index].ID;
            string error;
            if (_skills[index].BuffID == BuffID.Invalid)
            {
                error = "has buff data but BuffID.Invalid";
            }
            else if (data.TryValidate(out error))
            {
                _skills[index].BuffDefinition = data.ToBuffDefinition(_skills[index].BuffID);
                return;
            }

            _skills[index].BuffDefinition = default;
            Debug.LogError("[SkillDatabase] skill " + id.ToString(CultureInfo.InvariantCulture) + " buff data invalid: "
                + error + "; buff not built");
        }
    }
}
