#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.LevelingSystem;
using IronGrind.SkillSystem;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.SkillSystem
{
    /// <summary>
    /// Skill System Story 012: automated Config/Data smoke check of the authored MVP roster
    /// (<c>Assets/Data/Skills/SkillDatabase.asset</c>) and class data (<c>Assets/Data/Classes/ClassDatabase.asset</c>)
    /// against skill-system.md Skill Roster, CR-SK-14, CR-SK-15, CR-SK-22, AC-SK-36, AC-SK-38 and the buff table
    /// approved at the story's readiness check (2026-11-04), plus the buff authoring-form validation.
    /// The expected values below are the GDD table: the exact numbers are the point of the test.
    /// </summary>
    [TestFixture]
    [Category("AssetIntegrity")]
    internal sealed class SkillSystem_Roster_Tests
    {
        private const string SKILL_DATABASE_PATH = "Assets/data/Skills/SkillDatabase.asset";
        private const string CLASS_DATABASE_PATH = "Assets/data/Classes/ClassDatabase.asset";
        private const int SKILLS_PER_CLASS = 10;
        private const float WARRIOR_RANGE = 3.0f;
        private const float HEALER_RANGE = 12.0f;

        private static readonly uint[] WarriorIds = { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110 };
        private static readonly uint[] HealerIds = { 201, 202, 203, 204, 205, 206, 207, 208, 209, 210 };

        private const SkillEffectType PHYS = SkillEffectType.PhysicalDamage;
        private const SkillEffectType MAG = SkillEffectType.MagicalDamage;
        private const SkillEffectType HEAL = SkillEffectType.InstantHeal;
        private const SkillEffectType BUFF = SkillEffectType.ApplyBuff;
        private const SkillEffectType DEBUFF = SkillEffectType.ApplyDebuff;
        private const TargetConstraint HOSTILE = TargetConstraint.SingleHostile;
        private const TargetConstraint FRIENDLY = TargetConstraint.SingleFriendly;
        private const TargetConstraint SELF = TargetConstraint.Self;

        // id, class, unlock, effect, target, mana, cooldown, damage multiplier, base stat, heal multiplier, buff id.
        // Minor Heal unlocks at L5 (user decision 2026-11-04, CR-SK-22 second skill by L5; GDD table was L8).
        private static readonly object[] RosterRows =
        {
            new object[] { 101u, ClassType.Warrior, 1, PHYS, HOSTILE, 50, 0, 0.45f, DamageStat.AttackPower, 0f, BuffID.Invalid },
            new object[] { 102u, ClassType.Warrior, 5, PHYS, HOSTILE, 60, 40, 0.75f, DamageStat.AttackPower, 0f, BuffID.Invalid },
            new object[] { 103u, ClassType.Warrior, 15, DEBUFF, HOSTILE, 70, 100, 0f, DamageStat.AttackPower, 0f, BuffID.Rend },
            new object[] { 104u, ClassType.Warrior, 20, BUFF, SELF, 50, 400, 0f, DamageStat.AttackPower, 0f, BuffID.Rush },
            new object[] { 105u, ClassType.Warrior, 25, DEBUFF, HOSTILE, 80, 80, 0f, DamageStat.AttackPower, 0f, BuffID.ShieldBash },
            new object[] { 106u, ClassType.Warrior, 35, PHYS, HOSTILE, 100, 60, 0.90f, DamageStat.AttackPower, 0f, BuffID.Invalid },
            new object[] { 107u, ClassType.Warrior, 38, BUFF, SELF, 70, 600, 0f, DamageStat.AttackPower, 0f, BuffID.WarCry },
            new object[] { 108u, ClassType.Warrior, 42, PHYS, HOSTILE, 140, 200, 1.30f, DamageStat.AttackPower, 0f, BuffID.Invalid },
            new object[] { 109u, ClassType.Warrior, 50, PHYS | DEBUFF, HOSTILE, 110, 140, 0.85f, DamageStat.AttackPower, 0f, BuffID.SavageBlow },
            new object[] { 110u, ClassType.Warrior, 58, PHYS | DEBUFF, HOSTILE, 150, 400, 1.60f, DamageStat.AttackPower, 0f, BuffID.MortalWound },
            new object[] { 201u, ClassType.Healer, 1, MAG, HOSTILE, 90, 40, 1.5f, DamageStat.Intelligence, 0f, BuffID.Invalid },
            new object[] { 202u, ClassType.Healer, 5, HEAL, FRIENDLY, 80, 40, 0f, DamageStat.AttackPower, 1.5f, BuffID.Invalid },
            new object[] { 203u, ClassType.Healer, 12, BUFF, FRIENDLY, 100, 200, 0f, DamageStat.AttackPower, 0f, BuffID.Prayer },
            new object[] { 204u, ClassType.Healer, 18, BUFF, FRIENDLY, 120, 600, 0f, DamageStat.AttackPower, 0f, BuffID.Bless },
            new object[] { 205u, ClassType.Healer, 25, MAG, HOSTILE, 130, 80, 2.0f, DamageStat.Intelligence, 0f, BuffID.Invalid },
            new object[] { 206u, ClassType.Healer, 32, PHYS, HOSTILE, 120, 60, 1.2f, DamageStat.Strength, 0f, BuffID.Invalid },
            new object[] { 207u, ClassType.Healer, 38, HEAL, FRIENDLY, 160, 80, 0f, DamageStat.AttackPower, 2.5f, BuffID.Invalid },
            new object[] { 208u, ClassType.Healer, 45, PHYS | DEBUFF, HOSTILE, 140, 120, 1.5f, DamageStat.Strength, 0f, BuffID.SmiteArmor },
            new object[] { 209u, ClassType.Healer, 52, BUFF, SELF, 180, 600, 0f, DamageStat.AttackPower, 0f, BuffID.HolyShield },
            new object[] { 210u, ClassType.Healer, 58, PHYS, HOSTILE, 200, 200, 2.2f, DamageStat.Strength, 0f, BuffID.Invalid },
        };

        // buff id, duration ticks, hp regen per tick, then (stat, pct) pairs. All flat bonuses and MP regen are 0.
        private static readonly object[] BuffRows =
        {
            new object[] { BuffID.Rend, 100u, 0f, new[] { StatID.Defense }, new[] { -0.15f } },
            new object[] { BuffID.Rush, 60u, 0f, new[] { StatID.MovementSpeed }, new[] { 0.40f } },
            new object[] { BuffID.ShieldBash, 60u, 0f, new[] { StatID.MovementSpeed }, new[] { -0.30f } },
            new object[] { BuffID.WarCry, 200u, 0f, new[] { StatID.AttackPower }, new[] { 0.20f } },
            new object[] { BuffID.SavageBlow, 100u, 0f, new[] { StatID.Defense }, new[] { -0.20f } },
            new object[] { BuffID.MortalWound, 200u, 0f, new[] { StatID.AttackPower }, new[] { -0.15f } },
            new object[] { BuffID.Prayer, 100u, 2.0f, new StatID[0], new float[0] },
            new object[] { BuffID.Bless, 600u, 0f, new[] { StatID.AttackPower, StatID.Defense }, new[] { 0.10f, 0.10f } },
            new object[] { BuffID.SmiteArmor, 120u, 0f, new[] { StatID.Defense }, new[] { -0.15f } },
            new object[] { BuffID.HolyShield, 200u, 0f, new[] { StatID.Defense, StatID.MagicDefense }, new[] { 0.25f, 0.25f } },
        };

        private SkillDatabase _skills;
        private ClassDatabase _classes;
        private ClassRegistry _registry;
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            _skills = AssetDatabase.LoadAssetAtPath<SkillDatabase>(SKILL_DATABASE_PATH);
            _classes = AssetDatabase.LoadAssetAtPath<ClassDatabase>(CLASS_DATABASE_PATH);
            Assert.IsNotNull(_skills, "SkillDatabase asset missing at " + SKILL_DATABASE_PATH);
            Assert.IsNotNull(_classes, "ClassDatabase asset missing at " + CLASS_DATABASE_PATH);
            _registry = new ClassRegistry();
            _classes.RegisterAll(_registry);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject so in _created)
            {
                UnityEngine.Object.DestroyImmediate(so);
            }

            _created.Clear();
        }

        // ---- Count, ids, class lists -----------------------------------------------------------------------

        [Test]
        public void test_skill_database_has_20_entries_with_unique_nonzero_ids()
        {
            Assert.AreEqual(2 * SKILLS_PER_CLASS, _skills.Count);
            var seen = new HashSet<uint>();
            for (int i = 0; i < _skills.Count; i++)
            {
                uint id = _skills.GetEntryForTesting(i).ID;
                Assert.AreNotEqual(0u, id, "entry " + i);
                Assert.IsTrue(seen.Add(id), "duplicate id " + id);
            }
        }

        [Test]
        public void test_class_skill_lists_match_roster_order()
        {
            CollectionAssert.AreEqual(WarriorIds, ToRaw(_registry.GetClassSkills((byte)ClassType.Warrior)));
            CollectionAssert.AreEqual(HealerIds, ToRaw(_registry.GetClassSkills((byte)ClassType.Healer)));
        }

        [Test]
        public void test_every_class_skill_resolves_and_belongs_to_its_class()
        {
            AssertClassSkillsResolve(ClassType.Warrior);
            AssertClassSkillsResolve(ClassType.Healer);
        }

        // ---- Per row -------------------------------------------------------------------------------------

        [TestCaseSource(nameof(RosterRows))]
        public void test_roster_row_matches_gdd_table(uint id, ClassType classType, int unlock, SkillEffectType effect,
            TargetConstraint target, int mana, int cooldown, float damageMultiplier, DamageStat baseStat,
            float healMultiplier, BuffID buffId)
        {
            SkillDefinition s = Get(id);
            Assert.AreEqual(classType, s.ClassType, "class");
            Assert.AreEqual(unlock, s.UnlockLevel, "unlock");
            Assert.AreEqual(effect, s.SkillEffectType, "effect flags");
            Assert.AreEqual(target, s.TargetConstraint, "target");
            Assert.AreEqual(mana, s.ManaCost, "mana");
            Assert.AreEqual(cooldown, s.CooldownTicks, "cooldown");
            Assert.AreEqual(damageMultiplier, s.DamageMultiplier, 1e-5f, "damage multiplier");
            Assert.AreEqual(healMultiplier, s.HealMultiplier, 1e-5f, "heal multiplier");
            Assert.AreEqual(buffId, s.BuffID, "buff id");
            if (HasDamage(effect))
            {
                Assert.AreEqual(baseStat, s.DamageBaseStat, "base stat");
            }

            float expectedRange = target == TargetConstraint.Self ? 0f
                : classType == ClassType.Warrior ? WARRIOR_RANGE : HEALER_RANGE;
            Assert.AreEqual(expectedRange, s.RangeUnits, 1e-5f, "range");
            Assert.IsFalse(string.IsNullOrEmpty(s.DisplayName), "display name");
        }

        [Test]
        public void test_damage_context_agrees_with_effect_flags()
        {
            // Story 007 carry: the cast path derives the context from the flags (CR-SK-5); the authored field must agree.
            for (int i = 0; i < _skills.Count; i++)
            {
                SkillDefinition s = _skills.GetEntryForTesting(i);
                if ((s.SkillEffectType & SkillEffectType.PhysicalDamage) != 0)
                {
                    Assert.AreEqual(DamageContext.PhysicalSkill, s.DamageContext, s.DisplayName);
                }
                else if ((s.SkillEffectType & SkillEffectType.MagicalDamage) != 0)
                {
                    Assert.AreEqual(DamageContext.MagicalSkill, s.DamageContext, s.DisplayName);
                }
            }
        }

        // ---- AC-SK-36 / AC-SK-38 -----------------------------------------------------------------------------

        [Test]
        public void test_ac_sk_36_healer_has_exactly_one_level_1_magical_damage_skill()
        {
            int count = 0;
            foreach (SkillDefinition s in ClassSkills(ClassType.Healer))
            {
                if (s.UnlockLevel == 1 && (s.SkillEffectType & SkillEffectType.MagicalDamage) != 0)
                {
                    count++;
                    Assert.AreEqual(201u, s.ID, "Holy Smite");
                }
            }

            Assert.AreEqual(1, count);
        }

        [Test]
        public void test_ac_sk_38_warrior_has_a_level_1_physical_damage_skill()
        {
            bool found = false;
            foreach (SkillDefinition s in ClassSkills(ClassType.Warrior))
            {
                found |= s.UnlockLevel == 1 && (s.SkillEffectType & SkillEffectType.PhysicalDamage) != 0;
            }

            Assert.IsTrue(found);
        }

        // ---- CR-SK-14 / CR-SK-15 / CR-SK-22 / schema ranges ------------------------------------------------------

        [TestCase(ClassType.Warrior, 50, 150)]
        [TestCase(ClassType.Healer, 80, 200)]
        public void test_cr_sk_14_15_mana_costs_within_class_range(ClassType classType, int min, int max)
        {
            foreach (SkillDefinition s in ClassSkills(classType))
            {
                Assert.That(s.ManaCost, Is.InRange(min, max), s.DisplayName);
            }
        }

        [TestCase(ClassType.Warrior)]
        [TestCase(ClassType.Healer)]
        public void test_cr_sk_22_unlock_distribution(ClassType classType)
        {
            var levels = new List<int>();
            int early = 0, mid = 0, late = 0;
            foreach (SkillDefinition s in ClassSkills(classType))
            {
                levels.Add(s.UnlockLevel);
                if (s.UnlockLevel <= 10) early++;
                else if (s.UnlockLevel <= 30) mid++;
                else late++;
            }

            levels.Sort();
            Assert.That(early, Is.GreaterThanOrEqualTo(2), "L1-10");
            Assert.That(mid, Is.GreaterThanOrEqualTo(3), "L11-30");
            Assert.That(late, Is.GreaterThanOrEqualTo(5), "L31-60");
            Assert.AreEqual(1, levels[0], "first skill at L1");
            Assert.That(levels[1], Is.LessThanOrEqualTo(5), "second skill by L5");
        }

        [Test]
        public void test_every_value_is_within_its_schema_range()
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                SkillDefinition s = _skills.GetEntryForTesting(i);
                Assert.That(s.UnlockLevel, Is.InRange(1, 60), s.DisplayName + " unlock");
                Assert.That(s.CooldownTicks, Is.InRange(0, 6000), s.DisplayName + " cooldown");
                Assert.That(s.RangeUnits, Is.InRange(0f, 20f), s.DisplayName + " range");
                Assert.That(s.DamageMultiplier, Is.InRange(0f, 5f), s.DisplayName + " damage multiplier");
                Assert.That(s.HealMultiplier, Is.InRange(0f, 5f), s.DisplayName + " heal multiplier");
                if (HasDamage(s.SkillEffectType))
                {
                    Assert.That(s.DamageMultiplier, Is.GreaterThan(0f), s.DisplayName + " damaging skill multiplier");
                }

                if ((s.SkillEffectType & SkillEffectType.InstantHeal) != 0)
                {
                    Assert.That(s.HealMultiplier, Is.GreaterThan(0f), s.DisplayName + " heal multiplier");
                }
            }
        }

        // ---- BuffIDs and buff definitions ----------------------------------------------------------------------

        [Test]
        public void test_buff_skills_have_unique_defined_buff_ids_and_others_have_invalid()
        {
            var seen = new HashSet<BuffID>();
            int buffSkills = 0;
            for (int i = 0; i < _skills.Count; i++)
            {
                SkillDefinition s = _skills.GetEntryForTesting(i);
                bool isBuff = (s.SkillEffectType & (SkillEffectType.ApplyBuff | SkillEffectType.ApplyDebuff)) != 0;
                if (isBuff)
                {
                    buffSkills++;
                    Assert.AreNotEqual(BuffID.Invalid, s.BuffID, s.DisplayName);
                    Assert.IsTrue(Enum.IsDefined(typeof(BuffID), s.BuffID), s.DisplayName + " id is a BuffID member");
                    Assert.IsTrue(seen.Add(s.BuffID), "duplicate BuffID " + s.BuffID);
                    Assert.AreEqual(s.BuffID, s.BuffDefinition.BuffId, s.DisplayName + " built definition id");
                }
                else
                {
                    Assert.AreEqual(BuffID.Invalid, s.BuffID, s.DisplayName);
                    Assert.IsFalse(s.BuffData.IsAuthored, s.DisplayName + " has no buff data");
                }
            }

            Assert.AreEqual(10, buffSkills);
        }

        [TestCaseSource(nameof(BuffRows))]
        public void test_buff_definition_matches_approved_table(BuffID buffId, uint duration, float hpRegen,
            StatID[] stats, float[] pcts)
        {
            SkillDefinition s = FindByBuff(buffId);
            BuffDefinition d = s.BuffDefinition;
            Assert.AreEqual(buffId, d.BuffId);
            Assert.AreEqual(duration, d.DurationTicks, "duration");
            Assert.AreEqual(hpRegen, d.HpRegenPerTick, 1e-5f, "hp regen");
            Assert.AreEqual(0f, d.MpRegenPerTick, "mp regen");
            Assert.AreEqual(stats.Length, d.ModifierCount, "modifier count");
            for (int i = 0; i < stats.Length; i++)
            {
                StatModifier m = d.GetModifier(i);
                Assert.AreEqual(stats[i], m.StatId, "stat " + i);
                Assert.AreEqual(pcts[i], m.PctBonus, 1e-5f, "pct " + i);
                Assert.AreEqual(0f, m.FlatBonus, "flat " + i);
            }

            Assert.IsTrue(s.BuffData.TryValidate(out string error), error);
        }

        // ---- Class data ----------------------------------------------------------------------------------------

        [TestCase(ClassType.Warrior, 2, 1, 1, 0, 1)]
        [TestCase(ClassType.Healer, 0, 0, 1, 2, 2)]
        public void test_class_definition_matches_cd_2(ClassType classType, int str, int dex, int vit, int intel, int free)
        {
            Assert.IsTrue(_registry.TryGetClass((byte)classType, out ClassDefinition d));
            Assert.AreEqual(str, d.StrengthAutoAlloc, "Str");
            Assert.AreEqual(dex, d.DexterityAutoAlloc, "Dex");
            Assert.AreEqual(vit, d.VitalityAutoAlloc, "Vit");
            Assert.AreEqual(intel, d.IntelligenceAutoAlloc, "Int");
            Assert.AreEqual(free, d.FreePointsPerLevel, "free");
        }

        [Test]
        public void test_class_database_duplicate_class_entry_later_one_wins()
        {
            // Arrange
            var db = ScriptableObject.CreateInstance<ClassDatabase>();
            _created.Add(db);
            db.SetForTesting(new[]
            {
                new ClassDefinitionEntry { ClassType = ClassType.Warrior, FreePointsPerLevel = 1, SkillList = new uint[] { 1 } },
                new ClassDefinitionEntry { ClassType = ClassType.Warrior, FreePointsPerLevel = 3, SkillList = new uint[] { 2 } },
            });
            var registry = new ClassRegistry();

            // Act
            db.RegisterAll(registry);

            // Assert
            Assert.IsTrue(registry.TryGetClass((byte)ClassType.Warrior, out ClassDefinition d));
            Assert.AreEqual(3, d.FreePointsPerLevel);
            CollectionAssert.AreEqual(new uint[] { 2 }, ToRaw(registry.GetClassSkills((byte)ClassType.Warrior)));
        }

        [Test]
        public void test_class_database_holds_two_classes_meeting_the_cd_10_invariants()
        {
            Assert.AreEqual(2, _classes.Count);
            for (int i = 0; i < _classes.Count; i++)
            {
                ClassDefinitionEntry e = _classes.GetEntry(i);
                foreach (int v in new[] { e.StrengthAutoAlloc, e.DexterityAutoAlloc, e.VitalityAutoAlloc, e.IntelligenceAutoAlloc })
                {
                    Assert.That(v, Is.InRange(0, 2), e.ClassType + " auto-alloc field");
                }

                int total = e.StrengthAutoAlloc + e.DexterityAutoAlloc + e.VitalityAutoAlloc + e.IntelligenceAutoAlloc
                    + e.FreePointsPerLevel;
                Assert.AreEqual(5, total, e.ClassType + " auto + free");
                Assert.AreEqual(SKILLS_PER_CLASS, e.SkillList.Length, e.ClassType + " skill count");
            }
        }

        [Test]
        public void test_class_entry_conversion_copies_the_skill_list()
        {
            var entry = new ClassDefinitionEntry { SkillList = new uint[] { 1, 2 } };
            ClassDefinition d = entry.ToClassDefinition();
            entry.SkillList[0] = 99;
            Assert.AreEqual(1u, d.SkillList[0]);
        }

        [Test]
        public void test_class_database_register_all_rejects_null_registry()
        {
            Assert.Throws<ArgumentNullException>(() => _classes.RegisterAll(null));
        }

        // ---- Authoring-form validation (in-memory database) ---------------------------------------------------------

        [Test]
        public void test_valid_buff_data_builds_the_runtime_definition()
        {
            SkillDatabase db = Database(BuffSkill(1, BuffID.Rend, Data(100, 0f, Mod(StatID.Defense, -0.15f))));
            Assert.IsTrue(db.TryGetDefinition(new SkillID(1), out SkillDefinition s));
            Assert.AreEqual(BuffID.Rend, s.BuffDefinition.BuffId);
            Assert.AreEqual(100u, s.BuffDefinition.DurationTicks);
            Assert.AreEqual(1, s.BuffDefinition.ModifierCount);
        }

        [Test]
        public void test_unauthored_buff_data_keeps_an_in_memory_definition()
        {
            SkillDefinition skill = BuffSkill(1, BuffID.Rush, default);
            skill.BuffDefinition = new BuffDefinition(BuffID.Rush, default, 7u, 1f, 0f);
            SkillDatabase db = Database(skill);
            Assert.IsTrue(db.TryGetDefinition(new SkillID(1), out SkillDefinition s));
            Assert.AreEqual(7u, s.BuffDefinition.DurationTicks);
        }

        [Test]
        public void test_duplicate_stat_in_buff_data_is_reported_and_not_built()
        {
            AssertRejected(BuffSkill(1, BuffID.Bless, Data(100, 0f, Mod(StatID.Defense, 0.1f), Mod(StatID.Defense, 0.2f))),
                "appears twice");
        }

        [Test]
        public void test_zero_duration_buff_data_is_reported_and_not_built()
        {
            AssertRejected(BuffSkill(1, BuffID.Bless, Data(0, 0f, Mod(StatID.Defense, 0.1f))), "duration 0");
        }

        [Test]
        public void test_regen_only_buff_data_with_zero_duration_is_reported_and_not_built()
        {
            // Regen alone counts as authored (code review 2026-11-04), so the missing duration reaches the log.
            AssertRejected(BuffSkill(1, BuffID.Prayer, Data(0, 2f)), "duration 0");
        }

        [Test]
        public void test_buff_data_with_invalid_buff_id_is_reported_and_not_built()
        {
            AssertRejected(BuffSkill(1, BuffID.Invalid, Data(100, 0f, Mod(StatID.Defense, 0.1f))), "BuffID.Invalid");
        }

        [Test]
        public void test_buff_data_with_no_modifier_and_no_regen_is_invalid()
        {
            BuffDefinitionData data = Data(100, 0f);
            Assert.IsFalse(data.TryValidate(out string error));
            StringAssert.Contains("no modifier", error);
        }

        [Test]
        public void test_buff_data_with_more_than_max_modifiers_is_invalid()
        {
            var mods = new BuffModifierData[StatusEffectsConstants.MAX_STATS_PER_BUFF + 1];
            for (int i = 0; i < mods.Length; i++)
            {
                mods[i] = Mod((StatID)i, 0.1f);
            }

            BuffDefinitionData data = Data(100, 0f, mods);
            Assert.IsFalse(data.TryValidate(out string error));
            StringAssert.Contains("modifiers", error);
        }

        [Test]
        public void test_duration_above_6000_is_invalid_and_6000_is_valid()
        {
            Assert.IsFalse(Data(6001, 1f).TryValidate(out _));
            Assert.IsTrue(Data(6000, 1f).TryValidate(out string error), error);
        }

        // ---- Helpers -----------------------------------------------------------------------------------------

        private SkillDefinition Get(uint id)
        {
            Assert.IsTrue(_skills.TryGetDefinition(new SkillID(id), out SkillDefinition s), "skill " + id);
            return s;
        }

        private SkillDefinition FindByBuff(BuffID buffId)
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                SkillDefinition s = _skills.GetEntryForTesting(i);
                if (s.BuffID == buffId)
                {
                    return s;
                }
            }

            Assert.Fail("no skill with " + buffId);
            return default;
        }

        private IEnumerable<SkillDefinition> ClassSkills(ClassType classType)
        {
            IReadOnlyList<SkillID> ids = _registry.GetClassSkills((byte)classType);
            Assert.AreEqual(SKILLS_PER_CLASS, ids.Count, classType + " skill count");
            for (int i = 0; i < ids.Count; i++)
            {
                yield return Get(ids[i].Value);
            }
        }

        private void AssertClassSkillsResolve(ClassType classType)
        {
            foreach (SkillDefinition s in ClassSkills(classType))
            {
                Assert.AreEqual(classType, s.ClassType, s.DisplayName);
            }
        }

        private static uint[] ToRaw(IReadOnlyList<SkillID> ids)
        {
            var raw = new uint[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                raw[i] = ids[i].Value;
            }

            return raw;
        }

        private static bool HasDamage(SkillEffectType effect)
            => (effect & (SkillEffectType.PhysicalDamage | SkillEffectType.MagicalDamage)) != 0;

        private SkillDatabase Database(params SkillDefinition[] skills)
        {
            var db = ScriptableObject.CreateInstance<SkillDatabase>();
            _created.Add(db);
            db.SetForTesting(skills);
            return db;
        }

        private void AssertRejected(SkillDefinition skill, string reason)
        {
            SkillDatabase db = Database(skill);
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(reason)));
            Assert.IsTrue(db.TryGetDefinition(new SkillID(skill.ID), out SkillDefinition s));
            Assert.AreEqual(0u, s.BuffDefinition.DurationTicks, "definition not built");
            Assert.AreEqual(0, s.BuffDefinition.ModifierCount, "definition not built");
        }

        private static SkillDefinition BuffSkill(uint id, BuffID buffId, BuffDefinitionData data) => new SkillDefinition
        {
            ID = id,
            DisplayName = "test",
            UnlockLevel = 1,
            TargetConstraint = TargetConstraint.Self,
            SkillEffectType = SkillEffectType.ApplyBuff,
            BuffID = buffId,
            BuffData = data,
        };

        private static BuffDefinitionData Data(uint duration, float hpRegen, params BuffModifierData[] mods)
            => new BuffDefinitionData { DurationTicks = duration, HpRegenPerTick = hpRegen, Modifiers = mods };

        private static BuffModifierData Mod(StatID stat, float pct) => new BuffModifierData(stat, 0f, pct);
    }
}
#endif
