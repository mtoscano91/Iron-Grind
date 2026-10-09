using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IronGrind.EnhancementSystem;
using IronGrind.Randomness;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Architecture
{
    /// <summary>
    /// <c>System.Random</c> type rule of ADR-013 Decision 3 and Decision 6 (Enhancement Story 014; ADR-013
    /// Migration Plan step 3). In <c>IronGrind.ServerLogic</c>, outside <c>IronGrind.Randomness</c>, no
    /// field, constructor parameter or method parameter has the type <c>System.Random</c> (by value or
    /// by reference); and no test double subclasses <c>System.Random</c>. Reflection only; no Unity API
    /// is used. Properties are not inspected directly: their backing fields and accessors are covered
    /// like any other field or method. Return types, arrays and generic arguments are outside the rule.
    /// </summary>
    [TestFixture]
    internal sealed class RandomTypeRule_Tests
    {
        private const string SERVER_LOGIC = "IronGrind.ServerLogic";
        private const string EDIT_MODE_TESTS = "IronGrind.Foundation.EditModeTests";
        private const string RANDOMNESS_NAMESPACE = "IronGrind.Randomness";
        private const BindingFlags ALL_DECLARED = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Private fixture for the probe test: one <c>System.Random</c> member of each kind the checker
        /// must report (static and instance field, constructor parameter, public and private method
        /// parameter, by-reference parameter). It does not derive from <c>System.Random</c>.
        /// </summary>
        private sealed class OffendingFixture
        {
            private static System.Random _staticRandom;
            private readonly System.Random _heldRandom;

            public OffendingFixture(System.Random constructorRandom)
            {
                _heldRandom = constructorRandom;
            }

            public int UseRandom(System.Random methodRandom)
            {
                return methodRandom == _heldRandom ? 1 : 0;
            }

            private static void HoldRandom(System.Random staticRandom)
            {
                _staticRandom = staticRandom;
            }

            private bool TryGetRandom(out System.Random outRandom)
            {
                outRandom = _staticRandom ?? _heldRandom;
                return outRandom != null;
            }
        }

        /// <summary>
        /// No type of the <c>IronGrind.ServerLogic</c> assembly outside <c>IronGrind.Randomness</c> (nested
        /// and compiler-generated types included) has a field, constructor parameter or method parameter
        /// of type <c>System.Random</c>. The failure message lists each offending <c>Type.Member</c>.
        /// Guards against a vacuous pass: the scan includes <see cref="EnhancementService"/>, and the
        /// exclusion is what keeps <see cref="SystemRandomProvider"/> out of the result.
        /// </summary>
        [Test]
        public void test_server_logic_has_no_system_random_member_outside_randomness()
        {
            Assembly serverLogic = typeof(EnhancementService).Assembly;
            Assert.AreEqual(SERVER_LOGIC, serverLogic.GetName().Name);

            var checkedTypes = new HashSet<Type>();
            var offenders = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Type type in GetTypesOrFail(serverLogic))
            {
                if (IsInNamespace(type, RANDOMNESS_NAMESPACE))
                {
                    continue;
                }
                checkedTypes.Add(type);
                foreach (string member in FindSystemRandomMembers(type))
                {
                    offenders.Add(member);
                }
            }

            Assert.IsTrue(checkedTypes.Contains(typeof(EnhancementService)), "EnhancementService must be among the checked types.");
            Assert.IsFalse(checkedTypes.Contains(typeof(SystemRandomProvider)), "SystemRandomProvider must be excluded by its namespace.");
            Assert.IsNotEmpty(FindSystemRandomMembers(typeof(SystemRandomProvider)),
                "The checker must report SystemRandomProvider when the exclusion is not applied.");
            Assert.IsEmpty(offenders,
                "System.Random used as a field, constructor parameter or method parameter type in IronGrind.ServerLogic outside IronGrind.Randomness (ADR-013 Decision 3; take IRandomProvider instead):\n"
                + string.Join("\n", offenders));
        }

        /// <summary>
        /// No type of the EditMode test assembly derives from <c>System.Random</c>, directly or
        /// indirectly (ADR-013 Decision 6: test doubles implement <c>IRandomProvider</c>). The failure
        /// message lists the types.
        /// </summary>
        [Test]
        public void test_test_assembly_has_no_system_random_subclass()
        {
            Assembly testAssembly = typeof(RandomTypeRule_Tests).Assembly;
            Assert.AreEqual(EDIT_MODE_TESTS, testAssembly.GetName().Name);

            Type[] testTypes = GetTypesOrFail(testAssembly);
            Assert.IsNotEmpty(testTypes, "The test assembly must have types to check.");

            string[] subclasses = testTypes
                .Where(t => typeof(System.Random).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.IsEmpty(subclasses,
                "Test types that derive from System.Random (ADR-013 Decision 6: implement IRandomProvider instead):\n"
                + string.Join("\n", subclasses));
        }

        /// <summary>
        /// Failure path probe: the checker used by the type rule reports exactly the six members of a
        /// private fixture (static and instance field, constructor parameter, public and private method
        /// parameter, <c>out</c> parameter).
        /// </summary>
        [Test]
        public void test_checker_reports_every_system_random_field_and_parameter_of_fixture()
        {
            Assert.IsFalse(typeof(System.Random).IsAssignableFrom(typeof(OffendingFixture)), "The fixture must not derive from System.Random");

            List<string> reported = FindSystemRandomMembers(typeof(OffendingFixture));

            string fixtureName = typeof(OffendingFixture).FullName;
            string[] expected =
            {
                fixtureName + "._staticRandom",
                fixtureName + "._heldRandom",
                fixtureName + ".ctor(constructorRandom)",
                fixtureName + ".UseRandom(methodRandom)",
                fixtureName + ".HoldRandom(staticRandom)",
                fixtureName + ".TryGetRandom(outRandom)",
            };
            CollectionAssert.AreEquivalent(expected, reported);
        }

        /// <summary>
        /// Names (<c>Type.Member</c>) of the declared fields, constructor parameters and method parameters
        /// of <paramref name="type"/> whose type is exactly <c>System.Random</c>. Public and non-public,
        /// instance and static; a <c>ref</c>, <c>in</c> or <c>out</c> parameter counts.
        /// </summary>
        private static List<string> FindSystemRandomMembers(Type type)
        {
            var found = new List<string>();
            foreach (FieldInfo field in type.GetFields(ALL_DECLARED))
            {
                if (field.FieldType == typeof(System.Random))
                {
                    found.Add(type.FullName + "." + field.Name);
                }
            }
            foreach (ConstructorInfo ctor in type.GetConstructors(ALL_DECLARED))
            {
                foreach (ParameterInfo parameter in ctor.GetParameters())
                {
                    if (IsSystemRandomParameter(parameter))
                    {
                        // ConstructorInfo.Name already starts with a dot (".ctor", ".cctor").
                        found.Add(type.FullName + ctor.Name + "(" + parameter.Name + ")");
                    }
                }
            }
            foreach (MethodInfo method in type.GetMethods(ALL_DECLARED))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (IsSystemRandomParameter(parameter))
                    {
                        found.Add(type.FullName + "." + method.Name + "(" + parameter.Name + ")");
                    }
                }
            }
            return found;
        }

        /// <summary>True for a <c>System.Random</c> parameter, by value or by reference (<c>System.Random&amp;</c>).</summary>
        private static bool IsSystemRandomParameter(ParameterInfo parameter)
        {
            Type parameterType = parameter.ParameterType;
            if (parameterType.IsByRef)
            {
                parameterType = parameterType.GetElementType();
            }
            return parameterType == typeof(System.Random);
        }

        /// <summary>True when the type is in <paramref name="rootNamespace"/> or in a namespace nested under it.</summary>
        private static bool IsInNamespace(Type type, string rootNamespace)
        {
            string typeNamespace = type.Namespace;
            return typeNamespace != null
                && (typeNamespace == rootNamespace
                    || typeNamespace.StartsWith(rootNamespace + ".", StringComparison.Ordinal));
        }

        private static Type[] GetTypesOrFail(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                string messages = string.Join("\n", ex.LoaderExceptions.Where(e => e != null).Select(e => e.Message));
                Assert.Fail("Could not load all types of " + assembly.GetName().Name + ":\n" + messages);
                return Array.Empty<Type>();
            }
        }
    }
}
