using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>
    /// EditMode unit tests for Status Effects Story 001 - data model, constants and duration
    /// conversion (CR-SE-1, F-SE-1, AC-SE-26).
    /// </summary>
    [TestFixture]
    internal sealed class StatusEffects_DataModel_Tests
    {
        private const uint BUFF_RAW_ID = 5u;
        private const uint ENTITY_RAW_ID = 11u;
        private const uint CASTER_RAW_ID = 12u;
        private const uint DURATION_TICKS = 60u;
        private const float HP_REGEN = 1.5f;
        private const float MP_REGEN = 0.5f;
        private const int USED_MODIFIERS = 3;
        private const int SLOT_COUNT = 8;
        private const int TOO_MANY_MODIFIERS = 9;
        private const string IS_READ_ONLY_ATTRIBUTE = "System.Runtime.CompilerServices.IsReadOnlyAttribute";

        private static readonly BuffID Buff = (BuffID)BUFF_RAW_ID;
        private static readonly EntityID Entity = new EntityID(ENTITY_RAW_ID);
        private static readonly EntityID Caster = new EntityID(CASTER_RAW_ID);

        private static StatModifier ModifierFor(int i)
        {
            // Distinct non-default values per slot so a swapped slot is detected.
            return new StatModifier((StatID)(i + 1), 10f * (i + 1), 0.01f * (i + 1));
        }

        private static StatModifier[] Modifiers(int count)
        {
            var result = new StatModifier[count];
            for (int i = 0; i < count; i++)
                result[i] = ModifierFor(i);
            return result;
        }

        private static BuffDefinition DefinitionWith(int count)
        {
            return new BuffDefinition(Buff, Modifiers(count), DURATION_TICKS, HP_REGEN, MP_REGEN);
        }

        private static bool IsMarkedReadOnly(Type type)
        {
            return type.GetCustomAttributes(false).Any(a => a.GetType().FullName == IS_READ_ONLY_ATTRIBUTE);
        }

        private static void AssertAllFieldsAreValueTypes(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotEmpty(fields, type.Name + " has no fields");
            foreach (FieldInfo field in fields)
            {
                Assert.IsTrue(field.FieldType.IsValueType, type.Name + "." + field.Name + " is a reference type");
                if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum
                    && field.FieldType.Namespace != "System")
                {
                    foreach (FieldInfo nested in field.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        Assert.IsTrue(nested.FieldType.IsValueType,
                            type.Name + "." + field.Name + "." + nested.Name + " is a reference type");
                }
            }
        }

        // ---- Value types (CR-SE-1) ----

        [Test]
        public void StatusEffects_DataModel_StatModifier_AllFieldsValueTypesAndNoReferences()
        {
            AssertAllFieldsAreValueTypes(typeof(StatModifier));
            Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<StatModifier>());
        }

        [Test]
        public void StatusEffects_DataModel_BuffDefinition_AllFieldsValueTypesAndNoReferences()
        {
            AssertAllFieldsAreValueTypes(typeof(BuffDefinition));
            Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<BuffDefinition>());
        }

        [Test]
        public void StatusEffects_DataModel_ActiveEffect_AllFieldsValueTypesAndNoReferences()
        {
            AssertAllFieldsAreValueTypes(typeof(ActiveEffect));
            Assert.IsFalse(RuntimeHelpers.IsReferenceOrContainsReferences<ActiveEffect>());
        }

        [Test]
        public void StatusEffects_DataModel_Mutability_ActiveEffectMutableOthersReadonly()
        {
            Assert.IsFalse(IsMarkedReadOnly(typeof(ActiveEffect)), "ActiveEffect must not be readonly");
            Assert.IsTrue(IsMarkedReadOnly(typeof(StatModifier)), "StatModifier must be readonly");
            Assert.IsTrue(IsMarkedReadOnly(typeof(BuffDefinition)), "BuffDefinition must be readonly");

            var effect = new ActiveEffect(DefinitionWith(USED_MODIFIERS), Entity, Caster);
            effect.TicksRemaining--;
            Assert.AreEqual(DURATION_TICKS - 1u, effect.TicksRemaining);
        }

        // ---- Indexed access ----

        [Test]
        public void StatusEffects_DataModel_BuffDefinitionGetModifier_UsedSlotsReturnedUnusedDefault()
        {
            BuffDefinition def = DefinitionWith(USED_MODIFIERS);

            Assert.AreEqual(USED_MODIFIERS, def.ModifierCount);
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                StatModifier expected = i < USED_MODIFIERS ? ModifierFor(i) : default;
                StatModifier actual = def.GetModifier(i);
                Assert.AreEqual(expected.StatId, actual.StatId, "slot " + i);
                Assert.AreEqual(expected.FlatBonus, actual.FlatBonus, "slot " + i);
                Assert.AreEqual(expected.PctBonus, actual.PctBonus, "slot " + i);
            }
        }

        [Test]
        public void StatusEffects_DataModel_BuffDefinitionGetModifier_OutOfRangeThrows()
        {
            BuffDefinition def = DefinitionWith(USED_MODIFIERS);

            Assert.Throws<ArgumentOutOfRangeException>(() => def.GetModifier(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => def.GetModifier(SLOT_COUNT));
        }

        [Test]
        public void StatusEffects_DataModel_ActiveEffectGetModifier_UsedSlotsReturnedUnusedDefault()
        {
            var effect = new ActiveEffect(DefinitionWith(USED_MODIFIERS), Entity, Caster);

            Assert.AreEqual(USED_MODIFIERS, effect.ModifierCount);
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                StatModifier expected = i < USED_MODIFIERS ? ModifierFor(i) : default;
                StatModifier actual = effect.GetModifier(i);
                Assert.AreEqual(expected.StatId, actual.StatId, "slot " + i);
                Assert.AreEqual(expected.FlatBonus, actual.FlatBonus, "slot " + i);
                Assert.AreEqual(expected.PctBonus, actual.PctBonus, "slot " + i);
            }
        }

        [Test]
        public void StatusEffects_DataModel_ActiveEffectGetModifier_OutOfRangeThrows()
        {
            var effect = new ActiveEffect(DefinitionWith(USED_MODIFIERS), Entity, Caster);

            Assert.Throws<ArgumentOutOfRangeException>(() => effect.GetModifier(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => effect.GetModifier(SLOT_COUNT));
        }

        [Test]
        public void StatusEffects_DataModel_ActiveEffectConstructor_CopiesDefinitionAndIdentities()
        {
            BuffDefinition def = DefinitionWith(SLOT_COUNT);

            var effect = new ActiveEffect(def, Entity, Caster);

            Assert.AreEqual(Buff, effect.BuffId);
            Assert.AreEqual(Entity, effect.EntityId);
            Assert.AreEqual(Caster, effect.CasterEntityId);
            Assert.AreEqual(def.DurationTicks, effect.TicksRemaining);
            Assert.AreEqual(def.HpRegenPerTick, effect.HpRegenPerTick);
            Assert.AreEqual(def.MpRegenPerTick, effect.MpRegenPerTick);
            Assert.AreEqual(def.ModifierCount, effect.ModifierCount);
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                StatModifier expected = ModifierFor(i);
                StatModifier actual = effect.GetModifier(i);
                Assert.AreEqual(expected.StatId, actual.StatId, "slot " + i);
                Assert.AreEqual(expected.FlatBonus, actual.FlatBonus, "slot " + i);
                Assert.AreEqual(expected.PctBonus, actual.PctBonus, "slot " + i);
            }
        }

        [Test]
        public void StatusEffects_DataModel_ActiveEffectConstructor_DefaultDefinitionGivesEmptyEffect()
        {
            var effect = new ActiveEffect(default(BuffDefinition), Entity, Caster);

            Assert.AreEqual(0, effect.ModifierCount);
            Assert.AreEqual(0u, effect.TicksRemaining);
            for (int i = 0; i < SLOT_COUNT; i++)
                Assert.AreEqual(default(StatModifier), effect.GetModifier(i), "slot " + i);
        }

        // ---- Constructor bound ----

        [Test]
        public void StatusEffects_DataModel_BuffDefinitionConstructor_NineModifiersThrowsArgumentException()
        {
            StatModifier[] nine = Modifiers(TOO_MANY_MODIFIERS);

            Assert.Throws<ArgumentException>(() => new BuffDefinition(Buff, nine, DURATION_TICKS, 0f, 0f));
        }

        [Test]
        public void StatusEffects_DataModel_BuffDefinitionConstructor_EightModifiersAccepted()
        {
            BuffDefinition def = DefinitionWith(SLOT_COUNT);

            Assert.AreEqual(SLOT_COUNT, def.ModifierCount);
        }

        [Test]
        public void StatusEffects_DataModel_BuffDefinitionConstructor_EmptyModifiersAllSlotsDefault()
        {
            BuffDefinition def = DefinitionWith(0);

            Assert.AreEqual(0, def.ModifierCount);
            for (int i = 0; i < SLOT_COUNT; i++)
                Assert.AreEqual(default(StatModifier), def.GetModifier(i), "slot " + i);
        }

        // ---- Constants ----

        [Test]
        public void StatusEffects_DataModel_Constants_HaveSpecifiedValues()
        {
            Assert.AreEqual(8, StatusEffectsConstants.MAX_STATS_PER_BUFF);
            Assert.AreEqual(8, StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY);
            Assert.AreEqual(32, StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY);
            Assert.AreEqual(6000u, StatusEffectsConstants.MAX_DURATION_TICKS);
            Assert.AreEqual(200, StatusEffectsConstants.MAX_ENTITY_SLOTS);
        }

        [Test]
        public void StatusEffects_DataModel_ConstantGuard_AcSe26RulesHold() // AC-SE-26
        {
            int buffs = StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY;
            int capacity = StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY;
            int stats = StatusEffectsConstants.MAX_STATS_PER_BUFF;

            Assert.GreaterOrEqual(buffs, capacity / stats,
                "AC-SE-26: MAX_ACTIVE_BUFFS_PER_ENTITY must be >= MODIFIER_CAPACITY_PER_ENTITY / MAX_STATS_PER_BUFF");
            Assert.LessOrEqual(stats, capacity,
                "AC-SE-26: MAX_STATS_PER_BUFF must be <= MODIFIER_CAPACITY_PER_ENTITY");
        }

        // ---- Duration conversion (F-SE-1) ----

        [TestCase(0.5f, 10u)]
        [TestCase(3.0f, 60u)]
        [TestCase(300.0f, 6000u)]
        [TestCase(0.0f, 0u)]
        [TestCase(-1.0f, 0u)]
        [TestCase(float.NaN, 0u)]
        [TestCase(float.NegativeInfinity, 0u)]
        [TestCase(0.09f, 1u)]   // 1.8 ticks floors to 1 (round would give 2)
        [TestCase(0.04f, 0u)]   // 0.8 ticks floors to 0 (round would give 1)
        [TestCase(float.PositiveInfinity, uint.MaxValue)]
        [TestCase(1e10f, uint.MaxValue)]
        public void StatusEffects_DataModel_SecondsToTicks_FloorsSecondsTimesTickRate(float seconds, uint expected)
        {
            Assert.AreEqual(expected, StatusEffectsMath.SecondsToTicks(seconds));
        }

        [TestCase(1u, true)]
        [TestCase(6000u, true)]
        [TestCase(0u, false)]
        [TestCase(72000u, false)]
        public void StatusEffects_DataModel_IsWithinAuthoringCeiling_OnlyOneToMaxDuration(uint ticks, bool expected)
        {
            Assert.AreEqual(expected, StatusEffectsMath.IsWithinAuthoringCeiling(ticks));
        }

        // ---- ApplyResult ----

        [Test]
        public void StatusEffects_DataModel_ApplyResult_HasExactlyTheEightNamedMembers()
        {
            string[] expected =
            {
                "Success", "CapacityExceeded", "EffectSlotsFull", "InvalidDuration",
                "InvalidStat", "InvalidTarget", "InvalidAmount", "NoOpDefinition",
            };

            CollectionAssert.AreEquivalent(expected, Enum.GetNames(typeof(ApplyResult)));
        }
    }
}
