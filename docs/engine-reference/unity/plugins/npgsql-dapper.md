# Npgsql and Dapper under Unity 6.3 (server-only precompiled DLLs)

**Last verified:** 2026-10-16 — Character Persistence Story 012, Unity 6000.3.10f1, Editor in batch mode (not a headless build, not IL2CPP).
**Project record of the DLLs:** `docs/server-libraries.md` (versions, framework folders, SHA-256, NuGet URLs).

## Versions (NuGet API, 2026-10-16)

- **Npgsql 8.0.9 is the last version with .NET Standard builds** (`lib/netstandard2.0`, `lib/netstandard2.1`). Npgsql 9.0.x targets net6.0 / net8.0 only; 10.0.x net8.0+. Unity 6.3's API compatibility level is .NET Standard 2.1, so 8.0.9 is the newest usable version.
- **Dapper 2.1.66** has `lib/netstandard2.0`. Dapper 2.1.89 also has it but pulls `Microsoft.Bcl.AsyncInterfaces` 10.0.8.
- Source: `https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.nuspec` (dependency groups per target framework).

## The DLL set that compiles (confirmed in a run)

11 DLLs under `Assets/Plugins/Server/`: Npgsql, Dapper, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions, System.Collections.Immutable, System.Diagnostics.DiagnosticSource, System.Text.Json, System.Text.Encodings.Web, System.Threading.Channels, System.Runtime.CompilerServices.Unsafe, Microsoft.Bcl.AsyncInterfaces.

- **Not needed** (left out, the code compiled and the suite passed without them): System.Memory, System.Buffers, System.Numerics.Vectors, System.Threading.Tasks.Extensions — .NET Standard 2.1 has their types; System.Reflection.Emit.Lightweight — its `netstandard2.1` folder holds only the `_._` placeholder.
- **No clash** with Unity's own copies: no "multiple precompiled assemblies with the same name" error and no CS0433. The Burst package's copies of `System.Memory`, `System.Buffers`, `System.Collections.Immutable`, `Microsoft.Bcl.AsyncInterfaces` and `System.Runtime.CompilerServices.Unsafe` are under `.Runtime/`, which Unity does not import; the Collections package's test-only `System.Runtime.CompilerServices.Unsafe.dll` did not clash either.
- One `Microsoft.Bcl.AsyncInterfaces` (9.0.1) serves both Npgsql's chain (asks for 8.0.0) and Dapper's (9.0.1).
- `.NET Standard 2.0` builds load fine at the .NET Standard 2.1 level.

## Import settings (confirmed in a run)

- Wanted: Any platform, define constraint `UNITY_SERVER || UNITY_EDITOR`, `isExplicitlyReferenced: 1`, `validateReferences: 1`; the asmdef that uses them (`IronGrind.ServerLogic`) sets `overrideReferences: true` and lists the 11 file names in `precompiledReferences`.
- **Do not hand-write a PluginImporter `.meta`.** A hand-written one with `platformData` in the `- first: / second:` list form imported with no error or warning, yet the DLLs were invisible to the asmdef (`error CS0246: The type or namespace name 'Npgsql' could not be found`), also with the define constraint removed. The same DLLs with a guid-only `.meta` (Unity's defaults) compiled.
- What worked: an Editor script calling `PluginImporter.SetCompatibleWithAnyPlatform(true)`, `importer.DefineConstraints = new[] { "UNITY_SERVER || UNITY_EDITOR" }`, setting the serialized properties `m_IsExplicitlyReferenced` and `m_ValidateReferences` through a `SerializedObject`, then `SaveAndReimport()`. Unity 6.3 then writes `platformData` as a map — `Any: enabled: 1`, `Editor: enabled: 0` (with `DefaultValueInitialized: true`), `WindowsStoreApps: enabled: 0` — and the asmdef sees the DLLs. With `Any` enabled, `Editor: enabled: 0` does not exclude the Editor.
- `||` in a plugin's define constraint is accepted (the DLLs are available in the Editor with it).
- **Not verified:** that a client player build leaves the DLLs out (ADR-012's client-binary scan); behaviour on the headless Linux IL2CPP server build (ADR-006 Verification Required 1–3).

## IL2CPP risk: Dapper emits IL at runtime (not verified, flagged by Story 012's code review, 2026-10-19)

- **Dapper's default type mapping builds its readers and parameter binders with `System.Reflection.Emit` (`DynamicMethod` / `ILGenerator`).** IL2CPP compiles ahead of time and has no JIT, so emitted IL cannot run there; the expected failure is a `PlatformNotSupportedException` or `NotSupportedException` at the first query, not at build time. Belief from general .NET/IL2CPP knowledge, not run in this project — the reason the separate Dapper.AOT package (a source generator) exists.
- **`link.xml preserve="all"` does not help:** it stops the linker stripping code; it does not make emitted IL executable.
- Npgsql 8 and System.Text.Json also use reflection (JSONB / composite mapping), which IL2CPP handles only for code it can see ahead of time; plain `SELECT` round trips are lower risk than Dapper's mapping.
- **Conflict to settle before the first IL2CPP server build:** ADR-007 and ADR-012 plan an IL2CPP Linux headless server; ADR-006 assumes `link.xml` is enough and names Mono as the mitigation. Options: build the server with the **Mono** scripting backend; or prove Dapper works under IL2CPP (ADR-006 Verification 2); or move to Dapper.AOT or plain Npgsql commands. Carried by the Character Persistence epic; likely an ADR-006 amendment.

## API members present in Npgsql 8.0.9 (from the package's own `lib/netstandard2.1/Npgsql.xml`)

`T:Npgsql.NpgsqlDataSource`; `M:Npgsql.NpgsqlDataSource.Create(System.String)`; `M:Npgsql.NpgsqlDataSource.CreateCommand(System.String)`; `M:Npgsql.NpgsqlConnection.Wait(System.Int32)` (also `Wait()`, `Wait(TimeSpan)`, `WaitAsync(...)`); `E:Npgsql.NpgsqlConnection.Notification`; `P:Npgsql.NpgsqlNotificationEventArgs.Payload`. These are what ADR-006 and ADR-015 Decisions 2–3 use — the documentation half of ADR-015 Verification 0. Their behaviour (a blocking `Wait` on a background thread raising `Notification` on that thread) is not yet run; that is Networking Core Story 041.

## Known issue: synchronous `Wait(int)` under Unity Mono (2026-10-19)

Found by Networking Core Story 041, VR-1, first run, on a Windows Dedicated Server build with Mono. `NpgsqlConnection.Wait(int)` passes its timeout to `Socket.ReceiveTimeout` (8.0.9 `Npgsql.xml`). It recognises the expiry by the socket error code, and expects `WouldBlock` whenever the `Mono.Runtime` type exists (the `Mono.Runtime` string is present in the 8.0.9 DLL). Under Unity's Mono on Windows, every empty wait surfaced as `NpgsqlException: Exception while reading from stream` and broke the connection. **Use `WaitAsync(int timeout, CancellationToken)` instead**: it returns false on timeout and implements the timeout with a timer. Blocking on it from a dedicated thread is fine. On that path `Notification` is raised on the thread that completes the read, not on the caller. ADR-015 has the note; `NpgsqlRelayListenerConnection` has the code. The diagnosis is from the docs and the DLL strings plus the observed behaviour; the base socket error code is logged from the next run on.
