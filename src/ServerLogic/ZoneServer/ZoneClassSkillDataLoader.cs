using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.LevelingSystem;
using IronGrind.SkillSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The class and skill static data loaded at startup S2, read by S5 (Skill System Story 013). The composition
    /// looks for it on its <see cref="IZoneStaticDataLoader"/>; a loader without it leaves S5 on its fallbacks.
    /// </summary>
    public interface IZoneClassSkillData
    {
        /// <summary>The registry filled from the class asset; null until <c>LoadAndValidate</c> succeeded.</summary>
        ClassRegistry LoadedClassRegistry { get; }

        /// <summary>The validated skill roster; null until <c>LoadAndValidate</c> succeeded.</summary>
        SkillDatabase LoadedSkillDatabase { get; }
    }

    /// <summary>
    /// Startup S2 loader for the class and skill data (Skill System Story 013; ADR-009 Amendment 1 S2; class-system.md
    /// CD-10, AC-CS-18). Validates the two assets, fills a <see cref="ClassRegistry"/>, then runs the loader for the
    /// remaining static data (the item records, Character Persistence Story 019, then a stand-in), whose item database it
    /// forwards (<see cref="IZoneItemData"/>). A missing or invalid asset logs one
    /// <c>[ZoneServer] S2: &lt;reason&gt;</c> error and returns false (S-fail in every build). The reason names the
    /// asset, the class and the check, never an asset path.
    /// </summary>
    /// <remarks>
    /// Checks: both assets present; at least one class; per entry (on the entries, since a later entry replaces an
    /// earlier one in the registry): no duplicate <see cref="ClassType"/>, a defined <see cref="ClassType"/> member,
    /// each auto-allocation in [0, 2], the four auto-allocations plus free points equal 5; and every id in a class's
    /// skill list resolves in the skill database, with exactly 10 distinct skills per class (skill-system.md Skill Roster).
    /// The assets are owned by the scene; the loader never destroys them.
    /// </remarks>
    /// <example>
    /// <code>
    /// var loader = new ZoneClassSkillDataLoader(skillDatabase, classDatabase, new StandInZoneStaticDataLoader());
    /// if (!loader.LoadAndValidate()) { /* S-fail */ }
    /// </code>
    /// </example>
    public sealed class ZoneClassSkillDataLoader : IZoneStaticDataLoader, IZoneClassSkillData, IZoneItemData
    {
        /// <summary>CD-10: lowest auto-allocation per stat per level at MVP.</summary>
        public const int MIN_AUTO_ALLOC = 0;

        /// <summary>CD-10: highest auto-allocation per stat per level at MVP.</summary>
        public const int MAX_AUTO_ALLOC = 2;

        /// <summary>CD-10: auto-allocations plus free points per level at MVP.</summary>
        public const int POINTS_PER_LEVEL = 5;

        /// <summary>Skill Roster: skills per class at MVP.</summary>
        public const int SKILLS_PER_CLASS = 10;

        private const string LOG_PREFIX = "[ZoneServer] S2: ";

        private readonly SkillDatabase _skillDatabase;
        private readonly ClassDatabase _classDatabase;
        private readonly IZoneStaticDataLoader _rest;

        /// <summary>Creates the loader.</summary>
        /// <param name="skillDatabase">The skill roster asset; null fails S2.</param>
        /// <param name="classDatabase">The class asset; null fails S2.</param>
        /// <param name="rest">The loader for the other static data, run after this one succeeds; null for none.</param>
        public ZoneClassSkillDataLoader(SkillDatabase skillDatabase, ClassDatabase classDatabase, IZoneStaticDataLoader rest)
        {
            _skillDatabase = skillDatabase;
            _classDatabase = classDatabase;
            _rest = rest;
        }

        /// <inheritdoc/>
        public ClassRegistry LoadedClassRegistry { get; private set; }

        /// <inheritdoc/>
        public SkillDatabase LoadedSkillDatabase { get; private set; }

        /// <summary>
        /// The item database of the <c>rest</c> loader when it provides one (<see cref="ZoneItemDataLoader"/>, Character
        /// Persistence Story 019), forwarded so S5 finds it on this loader; null until <c>LoadAndValidate</c> succeeded or
        /// when the rest loader has none.
        /// </summary>
        public IronGrind.ItemDatabase.IItemDatabase LoadedItemDatabase { get; private set; }

        /// <inheritdoc/>
        public bool LoadAndValidate()
        {
            if (!TryValidate(_skillDatabase, _classDatabase, out string reason))
            {
                Debug.LogError(LOG_PREFIX + reason);
                return false;
            }

            var registry = new ClassRegistry();
            _classDatabase.RegisterAll(registry);
            if (_rest != null && !_rest.LoadAndValidate())
            {
                // S2 failed on the other static data: nothing is exposed, so the properties stay null.
                return false;
            }

            LoadedClassRegistry = registry;
            LoadedSkillDatabase = _skillDatabase;
            LoadedItemDatabase = (_rest as IZoneItemData)?.LoadedItemDatabase;
            return true;
        }

        /// <summary>Runs every S2 check on the two assets without logging.</summary>
        /// <param name="skillDatabase">The skill roster asset.</param>
        /// <param name="classDatabase">The class asset.</param>
        /// <param name="reason">The first failed check, or null.</param>
        /// <returns>True when every check passed.</returns>
        public static bool TryValidate(SkillDatabase skillDatabase, ClassDatabase classDatabase, out string reason)
        {
            if (skillDatabase == null)
            {
                reason = "SkillDatabase missing";
                return false;
            }

            if (classDatabase == null)
            {
                reason = "ClassDatabase missing";
                return false;
            }

            if (classDatabase.Count == 0)
            {
                reason = "ClassDatabase has no classes";
                return false;
            }

            var seen = new HashSet<ClassType>();
            for (int i = 0; i < classDatabase.Count; i++)
            {
                ClassDefinitionEntry entry = classDatabase.GetEntry(i);
                if (!TryValidateEntry(entry, skillDatabase, seen, out string check))
                {
                    reason = "ClassDatabase entry " + i.ToString(CultureInfo.InvariantCulture) + " ("
                        + ClassName(entry.ClassType) + "): " + check;
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static bool TryValidateEntry(ClassDefinitionEntry entry, SkillDatabase skills, HashSet<ClassType> seen,
            out string check)
        {
            if (!Enum.IsDefined(typeof(ClassType), entry.ClassType))
            {
                check = "undefined class type";
                return false;
            }

            if (!seen.Add(entry.ClassType))
            {
                check = "duplicate class type";
                return false;
            }

            if (!InRange(entry.StrengthAutoAlloc) || !InRange(entry.DexterityAutoAlloc)
                || !InRange(entry.VitalityAutoAlloc) || !InRange(entry.IntelligenceAutoAlloc))
            {
                check = "auto-allocation outside [" + MIN_AUTO_ALLOC.ToString(CultureInfo.InvariantCulture) + ", "
                    + MAX_AUTO_ALLOC.ToString(CultureInfo.InvariantCulture) + "]";
                return false;
            }

            int total = entry.StrengthAutoAlloc + entry.DexterityAutoAlloc + entry.VitalityAutoAlloc
                + entry.IntelligenceAutoAlloc + entry.FreePointsPerLevel;
            if (total != POINTS_PER_LEVEL)
            {
                check = "auto-allocation plus free points is not " + POINTS_PER_LEVEL.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            int count = entry.SkillList == null ? 0 : entry.SkillList.Length;
            if (count != SKILLS_PER_CLASS)
            {
                check = "skill list does not have " + SKILLS_PER_CLASS.ToString(CultureInfo.InvariantCulture) + " skills";
                return false;
            }

            var ids = new HashSet<uint>();
            for (int s = 0; s < count; s++)
            {
                if (!skills.TryGetDefinition(new SkillID(entry.SkillList[s]), out SkillDefinition _))
                {
                    check = "skill list slot " + s.ToString(CultureInfo.InvariantCulture) + " not in SkillDatabase";
                    return false;
                }

                // A repeated id would give the cooldown snapshot two slots for one skill.
                if (!ids.Add(entry.SkillList[s]))
                {
                    check = "skill list slot " + s.ToString(CultureInfo.InvariantCulture) + " repeats an earlier skill";
                    return false;
                }
            }

            check = null;
            return true;
        }

        private static bool InRange(int value) => value >= MIN_AUTO_ALLOC && value <= MAX_AUTO_ALLOC;

        // An undefined value is named by its number, so the line never carries free text from the asset.
        private static string ClassName(ClassType type)
            => Enum.IsDefined(typeof(ClassType), type) ? type.ToString() : ((byte)type).ToString(CultureInfo.InvariantCulture);
    }
}
