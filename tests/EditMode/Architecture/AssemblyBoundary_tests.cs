using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace IronGrind.Tests.EditMode.Architecture
{
    /// <summary>
    /// Server/client assembly boundary test (ADR-012 Decision 6, check 1; Damage Calculation Story 006).
    /// Reads the three <c>.asmdef</c> files (located through <see cref="CompilationPipeline"/>, never a
    /// hard-coded path) and inspects the loaded assemblies by reflection. Runs with the EditMode suite;
    /// no build is needed. The lists it checks live in <see cref="AssemblyBoundaryLists"/>.
    /// </summary>
    [TestFixture]
    internal sealed class AssemblyBoundary_Tests
    {
        private const string FOUNDATION = "IronGrind.Foundation";
        private const string SERVER_LOGIC = "IronGrind.ServerLogic";
        private const string CLIENT = "IronGrind.Client";
        private const string SERVER_CONSTRAINT = "UNITY_SERVER || UNITY_EDITOR";
        private const string CLIENT_CONSTRAINT = "!UNITY_SERVER || UNITY_EDITOR";
        private const BindingFlags ALL_DECLARED = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Server-only precompiled DLLs allowed on IronGrind.ServerLogic (ADR-012 Decision 1; empty today).
        private static readonly string[] ServerOnlyDlls = { "Npgsql.dll", "Dapper.dll" };

        [Serializable]
        private sealed class AsmdefData
        {
            public string name;
            public string[] references;
            public string[] precompiledReferences;
            public bool autoReferenced;
            public string[] defineConstraints;
            public string[] includePlatforms;
            public string[] excludePlatforms;
        }

        /// <summary>
        /// Shape of the three asmdefs: names, define constraints, autoReferenced (Decision 6 check 1), and
        /// no platform include/exclude list (Decision 2: platform lists are not used for this boundary).
        /// </summary>
        [Test]
        public void test_asmdef_shape_matches_adr012()
        {
            AsmdefData foundation = LoadAsmdef(FOUNDATION);
            AsmdefData server = LoadAsmdef(SERVER_LOGIC);
            AsmdefData client = LoadAsmdef(CLIENT);

            Assert.AreEqual(FOUNDATION, foundation.name);
            Assert.AreEqual(SERVER_LOGIC, server.name);
            Assert.AreEqual(CLIENT, client.name);
            CollectionAssert.AreEqual(new[] { SERVER_CONSTRAINT }, Safe(server.defineConstraints), "ServerLogic constraint");
            CollectionAssert.AreEqual(new[] { CLIENT_CONSTRAINT }, Safe(client.defineConstraints), "Client constraint");
            CollectionAssert.IsEmpty(Safe(foundation.defineConstraints), "Foundation has no define constraints");
            Assert.IsFalse(foundation.autoReferenced, "Foundation autoReferenced");
            Assert.IsFalse(server.autoReferenced, "ServerLogic autoReferenced");
            Assert.IsFalse(client.autoReferenced, "Client autoReferenced");
            foreach (AsmdefData asmdef in new[] { foundation, server, client })
            {
                CollectionAssert.IsEmpty(Safe(asmdef.includePlatforms), asmdef.name + " includePlatforms");
                CollectionAssert.IsEmpty(Safe(asmdef.excludePlatforms), asmdef.name + " excludePlatforms");
            }
        }

        /// <summary>Reference lists of the three asmdefs match ADR-012 Decision 1.</summary>
        [Test]
        public void test_reference_lists_match_adr012()
        {
            AsmdefData foundation = LoadAsmdef(FOUNDATION);
            AsmdefData server = LoadAsmdef(SERVER_LOGIC);
            AsmdefData client = LoadAsmdef(CLIENT);

            CollectionAssert.AreEqual(new[] { FOUNDATION }, Safe(server.references), "ServerLogic references");
            foreach (string dll in Safe(server.precompiledReferences))
            {
                CollectionAssert.Contains(ServerOnlyDlls, dll, "ServerLogic precompiled reference outside the named list");
            }
            CollectionAssert.AreEqual(new[] { FOUNDATION }, Safe(client.references), "Client references");
            CollectionAssert.IsEmpty(Safe(client.precompiledReferences), "Client precompiled references");
            CollectionAssert.DoesNotContain(Safe(foundation.references), SERVER_LOGIC);
            CollectionAssert.DoesNotContain(Safe(foundation.references), CLIENT);
        }

        /// <summary>Every top-level Foundation type is on the shared allow-list or the not-yet-moved list.</summary>
        [Test]
        public void test_every_foundation_type_is_listed()
        {
            Assembly foundation = typeof(IronGrind.CharacterStats.EntityID).Assembly;
            var listed = new HashSet<string>(AssemblyBoundaryLists.SharedAllowList);
            listed.UnionWith(AssemblyBoundaryLists.NotYetMovedList);

            var unlisted = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Type type in GetTypesOrFail(foundation))
            {
                Type outermost = type;
                while (outermost.DeclaringType != null)
                {
                    outermost = outermost.DeclaringType;
                }
                if (IsCompilerInfrastructure(outermost))
                {
                    continue;
                }
                if (!listed.Contains(outermost.FullName))
                {
                    unlisted.Add(outermost.FullName);
                }
            }

            Assert.IsEmpty(unlisted,
                "Types in IronGrind.Foundation that are on neither list (ADR-012 Decision 3: move them to ServerLogic/Client or add a shared entry with its client consumer):\n"
                + string.Join("\n", unlisted));
        }

        /// <summary>Every list entry resolves to a type that is still in Foundation (no stale entry).</summary>
        [Test]
        public void test_no_list_entry_is_stale()
        {
            Assembly foundation = typeof(IronGrind.CharacterStats.EntityID).Assembly;
            var stale = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string name in AssemblyBoundaryLists.SharedAllowList.Concat(AssemblyBoundaryLists.NotYetMovedList))
            {
                if (foundation.GetType(name) == null)
                {
                    stale.Add(name);
                }
            }

            Assert.IsEmpty(stale,
                "List entries that are not types of IronGrind.Foundation (delete them when a system moves):\n"
                + string.Join("\n", stale));
        }

        /// <summary>No type is on both lists and neither list holds a duplicate.</summary>
        [Test]
        public void test_no_type_is_on_both_lists_or_duplicated()
        {
            string[] shared = AssemblyBoundaryLists.SharedAllowList;
            string[] notMoved = AssemblyBoundaryLists.NotYetMovedList;

            string[] both = shared.Intersect(notMoved).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] duplicates = shared.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)
                .Concat(notMoved.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key))
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.IsEmpty(both, "On both lists:\n" + string.Join("\n", both));
            Assert.IsEmpty(duplicates, "Duplicated within a list:\n" + string.Join("\n", duplicates));
        }

        /// <summary>
        /// The five Damage Calculation types the GDD and AC-DC-I-01 name are defined in ServerLogic
        /// (Damage Calculation Story 006 acceptance criterion).
        /// </summary>
        [Test]
        public void test_damage_calculation_types_are_in_server_logic()
        {
            Type[] damageTypes =
            {
                typeof(IronGrind.DamageCalculation.DamageCalculator),
                typeof(IronGrind.DamageCalculation.DamageResult),
                typeof(IronGrind.DamageCalculation.DamageContext),
                typeof(IronGrind.DamageCalculation.DamageCalculationConfig),
                typeof(IronGrind.DamageCalculation.IEquippedWeaponQuery),
            };
            foreach (Type type in damageTypes)
            {
                Assert.AreEqual(SERVER_LOGIC, type.Assembly.GetName().Name, type.FullName);
            }
        }

        /// <summary>
        /// No type of a namespace on <see cref="AssemblyBoundaryLists.ServerOnlyNamespaces"/> is defined in
        /// Foundation or Client, and each listed namespace has at least one type in ServerLogic, so a
        /// misspelt or emptied entry fails instead of passing vacuously (Loot Table Story 014).
        /// </summary>
        [Test]
        public void test_server_only_namespaces_have_no_type_outside_server_logic()
        {
            Assembly serverLogic = typeof(IronGrind.DamageCalculation.DamageCalculator).Assembly;
            Assembly foundation = typeof(IronGrind.CharacterStats.EntityID).Assembly;
            Assembly client = typeof(IronGrind.UI.LevelingSystem.LevelingHudController).Assembly;
            Type[] serverTypes = GetTypesOrFail(serverLogic);

            var problems = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string serverOnlyNamespace in AssemblyBoundaryLists.ServerOnlyNamespaces)
            {
                foreach (Assembly assembly in new[] { foundation, client })
                {
                    foreach (Type leaked in GetTypesOrFail(assembly).Where(t => IsInNamespace(t, serverOnlyNamespace)))
                    {
                        problems.Add(leaked.FullName + " is defined in " + assembly.GetName().Name);
                    }
                }
                if (!serverTypes.Any(t => IsInNamespace(t, serverOnlyNamespace)))
                {
                    problems.Add(serverOnlyNamespace + " has no type in " + SERVER_LOGIC);
                }
            }

            Assert.IsEmpty(problems, "Server-only namespace violations:\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// No Foundation or Client type has a ServerLogic type as base type, interface, field, property,
        /// method or constructor parameter or return type (cheap guard; the compiler already enforces it).
        /// </summary>
        [Test]
        public void test_no_server_logic_type_in_foundation_or_client_signature()
        {
            Assembly serverLogic = typeof(IronGrind.DamageCalculation.DamageCalculator).Assembly;
            Assembly foundation = typeof(IronGrind.CharacterStats.EntityID).Assembly;
            Assembly client = typeof(IronGrind.UI.LevelingSystem.LevelingHudController).Assembly;

            var violations = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Assembly assembly in new[] { foundation, client })
            {
                foreach (Type type in GetTypesOrFail(assembly))
                {
                    foreach (KeyValuePair<string, Type> use in EnumerateSignatureTypes(type))
                    {
                        if (ReferencesAssembly(use.Value, serverLogic))
                        {
                            violations.Add(type.FullName + " -> " + use.Key + " : " + use.Value);
                        }
                    }
                }
            }

            Assert.IsEmpty(violations, "ServerLogic types in Foundation/Client signatures:\n" + string.Join("\n", violations));
        }

        /// <summary>True when the type is in <paramref name="rootNamespace"/> or in a namespace nested under it.</summary>
        private static bool IsInNamespace(Type type, string rootNamespace)
        {
            string typeNamespace = type.Namespace;
            return typeNamespace != null
                && (typeNamespace == rootNamespace
                    || typeNamespace.StartsWith(rootNamespace + ".", StringComparison.Ordinal));
        }

        private static string[] Safe(string[] values)
        {
            return values ?? Array.Empty<string>();
        }

        private static AsmdefData LoadAsmdef(string assemblyName)
        {
            string path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            Assert.IsFalse(string.IsNullOrEmpty(path), "No asmdef located for " + assemblyName);

            // The path is a virtual package path (a file: package has no real Packages/ folder on disk),
            // so the file is read through the AssetDatabase, never the file system.
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.IsNotNull(asset, "Could not load the asmdef at " + path);
            return JsonUtility.FromJson<AsmdefData>(asset.text);
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

        private static bool IsCompilerInfrastructure(Type outermost)
        {
            string fullName = outermost.FullName ?? string.Empty;
            if (fullName.StartsWith("<", StringComparison.Ordinal))
            {
                return true;
            }
            // Types the compiler embeds (EmbeddedAttribute, IsReadOnlyAttribute, NullableAttribute, ...) and
            // Unity's generated MonoScript table all carry [CompilerGenerated]. A hand-written type does not,
            // whatever namespace it is placed in, so it cannot use this exemption to bypass the lists.
            bool compilerNamespace = string.IsNullOrEmpty(outermost.Namespace)
                || outermost.Namespace == "System.Runtime.CompilerServices"
                || outermost.Namespace == "Microsoft.CodeAnalysis";
            return compilerNamespace && outermost.GetCustomAttribute<CompilerGeneratedAttribute>() != null;
        }

        private static IEnumerable<KeyValuePair<string, Type>> EnumerateSignatureTypes(Type type)
        {
            if (type.BaseType != null)
            {
                yield return new KeyValuePair<string, Type>("base type", type.BaseType);
            }
            foreach (Type iface in type.GetInterfaces())
            {
                yield return new KeyValuePair<string, Type>("interface", iface);
            }
            foreach (FieldInfo field in type.GetFields(ALL_DECLARED))
            {
                yield return new KeyValuePair<string, Type>("field " + field.Name, field.FieldType);
            }
            foreach (PropertyInfo property in type.GetProperties(ALL_DECLARED))
            {
                yield return new KeyValuePair<string, Type>("property " + property.Name, property.PropertyType);
            }
            foreach (EventInfo evt in type.GetEvents(ALL_DECLARED))
            {
                if (evt.EventHandlerType != null)
                {
                    yield return new KeyValuePair<string, Type>("event " + evt.Name, evt.EventHandlerType);
                }
            }
            foreach (MethodInfo method in type.GetMethods(ALL_DECLARED))
            {
                yield return new KeyValuePair<string, Type>("return of " + method.Name, method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return new KeyValuePair<string, Type>("parameter " + parameter.Name + " of " + method.Name, parameter.ParameterType);
                }
            }
            foreach (ConstructorInfo ctor in type.GetConstructors(ALL_DECLARED))
            {
                foreach (ParameterInfo parameter in ctor.GetParameters())
                {
                    yield return new KeyValuePair<string, Type>("constructor parameter " + parameter.Name, parameter.ParameterType);
                }
            }
        }

        /// <summary>True when the type, or any element or generic argument of it, is defined in <paramref name="assembly"/>.</summary>
        private static bool ReferencesAssembly(Type type, Assembly assembly)
        {
            if (type == null || type.IsGenericParameter)
            {
                return false;
            }
            if (type.HasElementType)
            {
                return ReferencesAssembly(type.GetElementType(), assembly);
            }
            if (type.Assembly == assembly)
            {
                return true;
            }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    if (ReferencesAssembly(argument, assembly))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
