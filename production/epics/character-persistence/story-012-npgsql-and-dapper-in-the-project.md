# Story 012: Npgsql and Dapper in the Project

> **Epic**: Character Persistence
> **Status**: Ready — written 2026-10-12; unblocked 2026-10-16 when the user closed open decisions 1–3; `/story-readiness` 2026-10-16: NEEDS WORK, fixed the same day at the user's request (versions pinned from the NuGet API, dependency set, compose file, manifest rules). Neither library is in the project yet.
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-16
> **Estimate**: 10 hours (8 + room for the dependency and import-setting trial and error)

## Context

**GDD**: `design/gdd/character-persistence.md` — no rule of its own; this story is the precondition of every database story.
**Requirement**: none in the epic's table (ADR-006 Verification Required 1–3)

**ADR Governing Implementation**: ADR-006 (Decision 2, the `link.xml` requirement, Verification Required 1–3); ADR-012 ("`IronGrind.ServerLogic` is the only assembly that may reference Npgsql and Dapper … added to the asmdef's precompiled references when ADR-006's code is written"; a client build contains neither DLL).
**ADR Decision Summary**: Npgsql and Dapper are server-only .NET libraries referenced by one assembly, kept out of client builds, and preserved from IL2CPP stripping.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: ADR-006 Verification Required — (1) Npgsql works on a headless Linux server build; (2) Dapper's `link.xml` needs under IL2CPP; (3) the connection pool under the headless process lifecycle. None is verified. **Versions checked against the NuGet API on 2026-10-16** (`api.nuget.org/v3-flatcontainer`): Npgsql **8.0.9** is the last version with `.NETStandard2.0` / `.NETStandard2.1` builds (9.0.x targets net6.0/net8.0 only, 10.0.x net8.0+); Dapper **2.1.66** has a `.NETStandard2.0` build. Unity 6.3's API profile is .NET Standard 2.1, which already contains `Span<T>`, `Memory<T>`, `ValueTask` and similar types, so some NuGet dependencies below may be redundant or clash (duplicate assembly name, or CS0433 duplicate type) — that is what the "In the project" criterion settles. The Burst package carries copies of `System.Memory`, `System.Buffers`, `System.Collections.Immutable`, `Microsoft.Bcl.AsyncInterfaces` and `System.Runtime.CompilerServices.Unsafe` under `.Runtime/` (a dot-folder Unity does not import); the Collections package has a test-only `System.Runtime.CompilerServices.Unsafe.dll` — check neither clashes.

**Control Manifest Rules (v2026-10-16)**:
- Required: `IronGrind.ServerLogic` is the only assembly that references Npgsql and Dapper; each DLL is itself absent from a client player under the same condition (`UNITY_SERVER || UNITY_EDITOR`) — ADR-012, ADR-006
- Required: IL2CPP `link.xml` preserves Npgsql and Dapper (`<assembly fullname="Npgsql" preserve="all" />`, `<assembly fullname="Dapper" preserve="all" />`); the `link.xml` must not be what brings the DLLs into a client build — ADR-006, ADR-012
- Forbidden: Entity Framework Core; SQLite — ADR-006
- Forbidden: a client player build that contains `Npgsql` or `Dapper` — ADR-012
- Note: ADR-015's relay (Networking Core Story 041) uses this story's `NpgsqlDataSource` and needs `NpgsqlConnection.Wait(int)` and `Notification` in the pinned version (ADR-015 Verification 0)

---

## Acceptance Criteria

- [ ] **In the project**: Npgsql 8.0.9 (`lib/netstandard2.1`) and Dapper 2.1.66 (`lib/netstandard2.0`) are under `Assets/Plugins/Server/`, with the dependency DLLs they need, each from the `netstandard2.1` folder of its package when it has one, else `netstandard2.0`. Start from this set (NuGet's dependency graph for those two, 2026-10-16):
  - Npgsql 8.0.9 → Microsoft.Extensions.Logging.Abstractions 8.0.0 (→ Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0), System.Collections.Immutable 8.0.0, System.Diagnostics.DiagnosticSource 8.0.0, System.Text.Json 8.0.5 (→ System.Text.Encodings.Web 8.0.0), System.Threading.Channels 8.0.0, System.Runtime.CompilerServices.Unsafe 6.0.0;
  - Dapper 2.1.66 → Microsoft.Bcl.AsyncInterfaces 9.0.1 (one copy also serves Npgsql's chain, which asks for 8.0.0), System.Reflection.Emit.Lightweight 4.7.0;
  - leave out at first, add only on a missing-type compile error: System.Memory 4.5.5, System.Buffers 4.5.1, System.Numerics.Vectors 4.4.0, System.Threading.Tasks.Extensions 4.5.4 (already in .NET Standard 2.1).

  `docs/server-libraries.md` lists every DLL: package, version, framework folder, SHA-256 of the DLL, NuGet URL, and for each one added or left out, why. **No compile error** of the kind "multiple precompiled assemblies with the same name" or CS0433 (duplicate type) in the Editor.
- [ ] **Referenced by one assembly**: `IronGrind.ServerLogic.asmdef` lists them as precompiled references; no other asmdef does. The assembly-boundary tests are updated to assert exactly that, and pass.
- [ ] **Server only**: the DLLs' import settings keep them out of a client player build (ADR-012's client-binary rule).
- [ ] **Compiles**: a type in `ServerLogic` that names `NpgsqlDataSource` and a Dapper extension method compiles in the Editor; the EditMode suite still passes.
- [ ] **`link.xml`**: the two `preserve="all"` entries of ADR-006 are present.
- [ ] **Verification, as far as it can be run** — recorded in `production/qa/evidence/character-persistence-012-npgsql-verification.md`, each item PASS / FAIL / NOT RUN with its observation: (a) in the Editor, a connection opens against a PostgreSQL instance and `SELECT 1` returns through Dapper; (b) ADR-006 item 1, a headless Linux server build; (c) item 2, IL2CPP; (d) item 3, the pool across start and shutdown.
- [ ] **Development PostgreSQL**: `tools/postgres/docker-compose.yml` starts `postgres:16` bound to `127.0.0.1:5432` with a named volume; credentials come from `tools/postgres/.env`, which is git-ignored (`tools/postgres/.env.example` is committed); `docker compose up -d` then the round trip (a) runs against it. A short `tools/postgres/README.md` gives the two commands.
- [ ] **Engine reference**: what was confirmed is added to `docs/engine-reference/unity/` — the DLL set that compiles under Unity 6.3, which dependencies were redundant, the import settings used; and that `NpgsqlDataSource`, `NpgsqlConnection.Wait(int)` and the `Notification` event exist in Npgsql 8.0.9, citing that version's API documentation (the documentation half of ADR-015 Verification 0, for Networking Core Story 041).

---

## Implementation Notes

- This story writes no persistence behaviour; it ends when a query round-trips.
- ADR-012's Verification Required items on server and client builds overlap with (b) and (c): one build run can serve both.

**User decisions 2026-10-16** (close open decisions 1–3 below):
1. **Manual DLLs, pinned.** The .NET Standard DLLs are taken from the NuGet packages and committed under `Assets/Plugins/Server/`, versions and sources recorded in `docs/server-libraries.md`. No NuGet-for-Unity tool. Both are in the Allowed Libraries of `.claude/docs/technical-preferences.md`. **Versions (user decision, `/story-readiness` 2026-10-16): Npgsql 8.0.9 and Dapper 2.1.66.** *(Corrected the same day: this line first said "Npgsql 7.0.x, Npgsql 8 dropped .NET Standard" — wrong; the NuGet API shows 8.0.9 has .NET Standard builds and 9.0 is the version that dropped them.)*
2. **PostgreSQL for development: a local Docker container** (ADR-006's validation), started from `tools/postgres/docker-compose.yml` (`postgres:16`; neither ADR pins a server version, Npgsql 8 supports 12 and later). Docker 20.10 and Compose v2.3 are installed on the development machine. CI later uses a service container (not this story).
3. **Items (b)–(d) do not block `/story-done`.** The story closes on the Editor round trip (a) and the boundary tests; (b)–(d) are recorded NOT RUN and carried by the epic's headless-build gate, as Networking Core's are.

**Open decisions for `/story-readiness`** *(closed 2026-10-16, see above)*

1. **How the libraries get in**: DLLs taken from NuGet packages and placed under `Assets/Plugins/` (which versions, which dependencies), or a NuGet-for-Unity tool. This is the user's call — it adds third-party binaries to the repository, and "Allowed Libraries" in the technical preferences is empty.
2. **Where PostgreSQL runs for development and CI**: a local install, a container, or a hosted instance; ADR-006's validation names "a local PostgreSQL Docker container". Nothing is set up.
3. **Which verification items block `/story-done`.** Items (b)–(d) need a headless Linux build, which the project has not made (the same open gate as Networking Core's). Decide whether NOT RUN on them blocks this story or is carried by the epic.

---

## Out of Scope

- Story 013: the schema
- Story 014: the store implementation
- The hosting topology (ADR-007)

---

## QA Test Cases

- **Boundary** — Given the asmdefs, Then only `IronGrind.ServerLogic` names the two libraries (automated, in the existing boundary test file).
- **Compile** — Given the reference type, Then no `error CS` and the EditMode total is unchanged plus this story's tests.
- **DLL set** — Given the DLLs under `Assets/Plugins/Server/`, Then the Editor log has no "multiple precompiled assemblies" error and no CS0433; every DLL is in `docs/server-libraries.md` with its hash, and each left-out dependency is listed with its reason.
- **Compose** — `docker compose -f tools/postgres/docker-compose.yml up -d` → the container is healthy on 127.0.0.1:5432; `tools/postgres/.env` is ignored by git (`git check-ignore`).
- **Round trip** — Setup: the PostgreSQL container above. Verify: `SELECT 1` through Dapper. Pass: the value 1 returns and the connection goes back to the pool.
- **Build items** — Setup: a headless Linux server build with IL2CPP. Verify: the same round trip. Pass: it returns; otherwise the observation is recorded as FAIL with the error text.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `production/qa/evidence/character-persistence-012-npgsql-verification.md`; the updated assembly-boundary tests passing.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: the user's decisions on open decisions 1–3 — made 2026-10-16 (see Implementation Notes)
- Unlocks: Stories 014, 015, 016; Networking Core Story 041 (the ADR-015 Npgsql relay transport)
