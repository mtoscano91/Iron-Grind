# Server Libraries

Server-only .NET libraries committed under `Assets/Plugins/Server/` (Character Persistence Story 012; ADR-006,
ADR-012). Only `IronGrind.ServerLogic` references them (`overrideReferences: true` plus `precompiledReferences`).
Taken by hand from the NuGet packages, no NuGet-for-Unity tool. Versions checked against NuGet 2026-10-16.

## DLLs in the project

| DLL | Package | Version | Framework folder | SHA-256 | NuGet |
|-----|---------|---------|------------------|---------|-------|
| Npgsql.dll | Npgsql | 8.0.9 | lib/netstandard2.1 | c526923248a9160274911aa44b1ab511753aad66e296aa3070f2833294d3cc87 | https://www.nuget.org/packages/Npgsql/8.0.9 |
| Dapper.dll | Dapper | 2.1.66 | lib/netstandard2.0 | 9eb212a20a196ce42e063969398c98b55fc19959fdd0e7d086293b17b23cb30d | https://www.nuget.org/packages/Dapper/2.1.66 |
| Microsoft.Extensions.Logging.Abstractions.dll | Microsoft.Extensions.Logging.Abstractions | 8.0.0 | lib/netstandard2.0 | e899e37dfc2dd433807c205e764c32ea34bb95da083e189d7615947dd16c51e8 | https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/8.0.0 |
| Microsoft.Extensions.DependencyInjection.Abstractions.dll | Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.0 | lib/netstandard2.1 | 3a588921e88c7b989f805adc5721ed25e24d389e8a9bc968ebcff79f412a5288 | https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/8.0.0 |
| System.Collections.Immutable.dll | System.Collections.Immutable | 8.0.0 | lib/netstandard2.0 | 5b1b1c83ba3d135c2fdfe425842fbe9c7432878b7e468623acb554c69b4c130f | https://www.nuget.org/packages/System.Collections.Immutable/8.0.0 |
| System.Diagnostics.DiagnosticSource.dll | System.Diagnostics.DiagnosticSource | 8.0.0 | lib/netstandard2.0 | abc69603fc8ddab9e90eb5317170242ef1f57d575690f82da691a2b34fc46eac | https://www.nuget.org/packages/System.Diagnostics.DiagnosticSource/8.0.0 |
| System.Text.Json.dll | System.Text.Json | 8.0.5 | lib/netstandard2.0 | 42a3d66464aa3f52202a625fb8d719663ceae07678081253b54d6ed56a95ea0a | https://www.nuget.org/packages/System.Text.Json/8.0.5 |
| System.Text.Encodings.Web.dll | System.Text.Encodings.Web | 8.0.0 | lib/netstandard2.0 | 5ad1f348223f281c34b9b857445a1c3412a430026ef049aeac641c53a92408be | https://www.nuget.org/packages/System.Text.Encodings.Web/8.0.0 |
| System.Threading.Channels.dll | System.Threading.Channels | 8.0.0 | lib/netstandard2.1 | 31c7e3704c0477c53d9306362dc6abe741088efb7a7b4e46cded0169cf7bb0b2 | https://www.nuget.org/packages/System.Threading.Channels/8.0.0 |
| System.Runtime.CompilerServices.Unsafe.dll | System.Runtime.CompilerServices.Unsafe | 6.0.0 | lib/netstandard2.0 | 01748200f2400c742aa689f1f5101bd6298efdfd92c00c18f4fa473847235ba9 | https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.0.0 |
| Microsoft.Bcl.AsyncInterfaces.dll | Microsoft.Bcl.AsyncInterfaces | 9.0.1 | lib/netstandard2.1 | 444520a2437efcc55a0adcc59c7b79c15c14e5c32fe6496687e0776661ebc5d3 | https://www.nuget.org/packages/Microsoft.Bcl.AsyncInterfaces/9.0.1 |

Why each dependency is there: Npgsql 8.0.9 depends on Logging.Abstractions (which pulls DependencyInjection.Abstractions),
Collections.Immutable, DiagnosticSource, Text.Json (which pulls Encodings.Web), Threading.Channels and
CompilerServices.Unsafe. Dapper 2.1.66 depends on Bcl.AsyncInterfaces; the one 9.0.1 copy also serves Npgsql's chain, which asks for 8.0.0.
Npgsql 8.0.9 is the last version with .NET Standard builds (9.0 targets net6.0/net8.0 only).

## Left out

| Package | Version | Reason |
|---------|---------|--------|
| System.Memory | 4.5.5 | .NET Standard 2.1 (Unity 6.3's API profile) already has `Span<T>`/`Memory<T>`; a copy risks CS0433. |
| System.Buffers | 4.5.1 | Already in .NET Standard 2.1. |
| System.Numerics.Vectors | 4.4.0 | Already in .NET Standard 2.1. |
| System.Threading.Tasks.Extensions | 4.5.4 | Already in .NET Standard 2.1 (`ValueTask`). |
| System.Reflection.Emit.Lightweight | 4.7.0 | Its `netstandard2.1` folder is the `_._` placeholder: the types are built in. |

Add one of these only on a missing-type compile error, and record it here with the reason.

## Import settings

Every DLL's `.meta` is a PluginImporter with: `defineConstraints: UNITY_SERVER || UNITY_EDITOR` (absent from a client
player, the same condition as `IronGrind.ServerLogic`), `isExplicitlyReferenced: 1` (only an asmdef that lists the DLL
references it), `isPreloaded: 0`, `isOverridable: 0`, `validateReferences: 1`, `Any` platform enabled. The assembly-boundary tests
(`AssemblyBoundary_tests.cs`) assert the asmdef lists and these `.meta` values. `Assets/link.xml` preserves `Npgsql`
and `Dapper` under IL2CPP stripping; it does not bring them into a client build. `.gitignore` ignores `*.dll`, with
`!Assets/Plugins/Server/*.dll` so these are committed.

**Set these in Unity, never by editing the `.meta` by hand** (found 2026-10-16, Story 012). A hand-written PluginImporter `.meta` whose `platformData` used the `- first: / second:` list form imported with no error or warning, but left every DLL unavailable to `IronGrind.ServerLogic` (`error CS0246` on `using Npgsql;`), even with the define constraint removed. Unity 6.3 writes `platformData` as a map (`Any:`, `Editor:`, `WindowsStoreApps:` keys). Set the values in the Inspector, or through `PluginImporter` (`SetCompatibleWithAnyPlatform(true)`, `DefineConstraints`, and the serialized `m_IsExplicitlyReferenced` / `m_ValidateReferences`) followed by `SaveAndReimport()` — that is how these 11 were written.

## IL2CPP warning

Dapper's default mapping emits IL at runtime (`System.Reflection.Emit`), which IL2CPP cannot run; `link.xml` only
prevents stripping. The server build planned in ADR-007 / ADR-012 is IL2CPP, so before the first IL2CPP server build
either the server uses the Mono scripting backend, or Dapper is shown to work under IL2CPP (ADR-006 Verification 2),
or the code moves to Dapper.AOT or plain Npgsql commands. Not verified (Editor runs only, which are Mono); details in
`docs/engine-reference/unity/plugins/npgsql-dapper.md`.

## Updating a library

1. Download the `.nupkg` of the new version (a `.nupkg` is a zip) and take the DLL from the same framework folder (`netstandard2.1` when it has one, else `netstandard2.0`). Check the dependency list of the package.
2. Replace the DLL; keep its `.meta` (same guid and settings).
3. Re-hash (`sha256sum`) and update the table above, including version and NuGet URL.
4. Add or remove dependency DLLs if the package's dependency set changed, and update `precompiledReferences` in `src/ServerLogic/IronGrind.ServerLogic.asmdef` and `ServerOnlyDlls` in the boundary test.
5. Re-run the EditMode suite and the `Database` category (`tools/postgres/README.md`); check the Editor log for "multiple precompiled assemblies" and CS0433.
