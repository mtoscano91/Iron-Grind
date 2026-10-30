using System;
using System.Collections.Generic;
using System.IO;
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
        private const string DEV_HARNESS = "IronGrind.DevHarness";
        // ServerLogic declares NGO types (ADR-012 Decision 1; Networking Core Story 038).
        private const string NETCODE_RUNTIME = "Unity.Netcode.Runtime";
        // Client and DevHarness read touches through EnhancedTouch (ADR-012 note 2026-10-29; Movement Story 013).
        private const string INPUT_SYSTEM = "Unity.InputSystem";
        private const string SERVER_CONSTRAINT = "UNITY_SERVER || UNITY_EDITOR";
        private const string CLIENT_CONSTRAINT = "!UNITY_SERVER || UNITY_EDITOR";
        private const BindingFlags ALL_DECLARED = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Server-only precompiled DLLs: exactly the precompiledReferences of IronGrind.ServerLogic
        // (ADR-012 Decision 1, ADR-006; Character Persistence Story 012; listed in docs/server-libraries.md).
        private static readonly string[] ServerOnlyDlls =
        {
            "Npgsql.dll",
            "Dapper.dll",
            "Microsoft.Bcl.AsyncInterfaces.dll",
            "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll",
            "System.Collections.Immutable.dll",
            "System.Diagnostics.DiagnosticSource.dll",
            "System.Runtime.CompilerServices.Unsafe.dll",
            "System.Text.Encodings.Web.dll",
            "System.Text.Json.dll",
            "System.Threading.Channels.dll",
        };
        private const string SERVER_PLUGIN_FOLDER = "Assets/Plugins/Server/";

        [Serializable]
        private sealed class AsmdefData
        {
            public string name;
            public string[] references;
            public string[] precompiledReferences;
            public bool overrideReferences;
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

        /// <summary>
        /// Reference lists of the three asmdefs match ADR-012 Decision 1: ServerLogic references Foundation and
        /// Unity.Netcode.Runtime; Client references Foundation and Unity.InputSystem (ADR-012 note 2026-10-29).
        /// </summary>
        [Test]
        public void test_reference_lists_match_adr012()
        {
            AsmdefData foundation = LoadAsmdef(FOUNDATION);
            AsmdefData server = LoadAsmdef(SERVER_LOGIC);
            AsmdefData client = LoadAsmdef(CLIENT);

            CollectionAssert.AreEqual(new[] { FOUNDATION, NETCODE_RUNTIME }, Safe(server.references), "ServerLogic references");
            // Story 012: replaces the old "each entry is on the allowed list" loop (the list was empty).
            Assert.IsTrue(server.overrideReferences, "ServerLogic overrideReferences");
            CollectionAssert.AreEquivalent(ServerOnlyDlls, Safe(server.precompiledReferences), "ServerLogic precompiled references");
            Assert.IsFalse(foundation.overrideReferences, "Foundation overrideReferences");
            Assert.IsFalse(client.overrideReferences, "Client overrideReferences");
            CollectionAssert.AreEqual(new[] { FOUNDATION, INPUT_SYSTEM }, Safe(client.references), "Client references");
            CollectionAssert.DoesNotContain(Safe(foundation.references), SERVER_LOGIC);
            CollectionAssert.DoesNotContain(Safe(foundation.references), CLIENT);
        }

        /// <summary>
        /// No project asmdef other than <c>IronGrind.ServerLogic</c> lists a server-only DLL (ADR-012 Decision 1;
        /// Character Persistence Story 012). Scans every asmdef under <c>Assets/</c> and the project's own
        /// <c>file:</c> packages (<c>src/</c>, <c>tests/</c>), so an asmdef added later is covered too.
        /// </summary>
        [Test]
        public void test_only_server_logic_lists_server_only_dlls()
        {
            List<AsmdefData> asmdefs = LoadProjectAsmdefs();
            List<string> names = asmdefs.Select(a => a.name).ToList();
            // Guard against a vacuous scan: the production assemblies and the EditMode test assembly are found.
            CollectionAssert.IsSubsetOf(
                new[] { FOUNDATION, SERVER_LOGIC, CLIENT, DEV_HARNESS, "IronGrind.Foundation.EditModeTests" }, names,
                "Asmdef scan did not find the known assemblies");

            foreach (AsmdefData asmdef in asmdefs)
            {
                if (asmdef.name == SERVER_LOGIC)
                {
                    continue;
                }
                string[] offending = Safe(asmdef.precompiledReferences).Intersect(ServerOnlyDlls).ToArray();
                CollectionAssert.IsEmpty(offending, asmdef.name + " lists a server-only DLL");
            }
        }

        /// <summary>
        /// Each server-only DLL's import settings keep it out of a client player: the define constraint
        /// <c>UNITY_SERVER || UNITY_EDITOR</c>, and <c>isExplicitlyReferenced: 1</c> so only an asmdef that
        /// lists it references it (ADR-012 Decision 1; Character Persistence Story 012).
        /// </summary>
        [Test]
        public void test_server_dll_import_settings_keep_them_out_of_client_builds()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            foreach (string dll in ServerOnlyDlls)
            {
                string path = Path.Combine(projectRoot, SERVER_PLUGIN_FOLDER + dll + ".meta");
                Assert.IsTrue(File.Exists(path), "Missing import settings file: " + path);
                string[] lines = File.ReadAllLines(path).Select(l => l.TrimEnd()).ToArray();
                CollectionAssert.Contains(lines, "  isExplicitlyReferenced: 1", dll + " isExplicitlyReferenced");
                CollectionAssert.Contains(lines, "  - " + SERVER_CONSTRAINT, dll + " define constraint");
                CollectionAssert.Contains(lines, "  validateReferences: 1", dll + " validateReferences");
            }
        }

        /// <summary>
        /// The manual harness assembly is Editor-only (ADR-012 Decision 7): define constraint exactly
        /// UNITY_EDITOR, not auto-referenced, references exactly the three production assemblies, and none
        /// of those references it.
        /// </summary>
        [Test]
        public void test_dev_harness_asmdef_is_editor_only_and_unreferenced()
        {
            AsmdefData harness = LoadAsmdef(DEV_HARNESS);

            Assert.AreEqual(DEV_HARNESS, harness.name);
            CollectionAssert.AreEqual(new[] { "UNITY_EDITOR" }, Safe(harness.defineConstraints), "DevHarness constraint");
            Assert.IsFalse(harness.autoReferenced, "DevHarness autoReferenced");
            CollectionAssert.AreEquivalent(new[] { FOUNDATION, SERVER_LOGIC, CLIENT, INPUT_SYSTEM }, Safe(harness.references), "DevHarness references");
            foreach (string name in new[] { FOUNDATION, SERVER_LOGIC, CLIENT })
            {
                CollectionAssert.DoesNotContain(Safe(LoadAsmdef(name).references), DEV_HARNESS, name + " must not reference DevHarness");
            }
        }

        /// <summary>Every top-level Foundation type is on the shared allow-list.</summary>
        [Test]
        public void test_every_foundation_type_is_listed()
        {
            Assembly foundation = typeof(IronGrind.CharacterStats.EntityID).Assembly;
            var listed = new HashSet<string>(AssemblyBoundaryLists.SharedAllowList);

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
                "Types in IronGrind.Foundation that are not on the shared allow-list (ADR-012 Decision 3: move them to ServerLogic/Client or add a shared entry with its client consumer):\n"
                + string.Join("\n", unlisted));
        }

        /// <summary>Every shared allow-list entry resolves to a type that is in Foundation.</summary>
        [Test]
        public void test_no_list_entry_is_stale()
        {
            Assembly foundation = typeof(IronGrind.CharacterStats.EntityID).Assembly;
            var stale = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string name in AssemblyBoundaryLists.SharedAllowList)
            {
                if (foundation.GetType(name) == null)
                {
                    stale.Add(name);
                }
            }

            Assert.IsEmpty(stale,
                "Shared allow-list entries that are not types of IronGrind.Foundation:\n"
                + string.Join("\n", stale));
        }

        /// <summary>No name appears twice on the shared allow-list.</summary>
        [Test]
        public void test_shared_allow_list_has_no_duplicate()
        {
            string[] duplicates = AssemblyBoundaryLists.SharedAllowList.GroupBy(n => n).Where(g => g.Count() > 1)
                .Select(g => g.Key).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.IsEmpty(duplicates, "Duplicated on the shared allow-list:\n" + string.Join("\n", duplicates));
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
        /// <c>CurrencySystem</c> is defined in ServerLogic — <c>currency-system.md</c> AC-CS-G-01, first
        /// sentence (ADR-012 Decision 6, check 1); Currency System Story 007. The second sentence of the
        /// criterion (no client build includes the class) needs the client-binary scan and is not covered here.
        /// </summary>
        [Test]
        public void test_currency_system_is_in_server_logic()
        {
            Assert.AreEqual(SERVER_LOGIC, typeof(IronGrind.Currency.CurrencySystem).Assembly.GetName().Name, typeof(IronGrind.Currency.CurrencySystem).FullName);
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

        // Every asmdef the project owns: under Assets/ and in its own file: packages (com.irongrind.*).
        // Unity's and third-party packages are left out.
        private static List<AsmdefData> LoadProjectAsmdefs()
        {
            var result = new List<AsmdefData>();
            foreach (string guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/com.irongrind."))
                {
                    continue;
                }
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                Assert.IsNotNull(asset, "Could not load the asmdef at " + path);
                result.Add(JsonUtility.FromJson<AsmdefData>(asset.text));
            }
            return result;
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
