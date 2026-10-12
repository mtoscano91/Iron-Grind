# Story 012: Npgsql and Dapper in the Project

> **Epic**: Character Persistence
> **Status**: Blocked — written 2026-10-12. **Neither library is in the project** (not in `Packages/manifest.json`, nothing under `Assets/`), and how they get there is not decided (open decision 1).
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/character-persistence.md` — no rule of its own; this story is the precondition of every database story.
**Requirement**: none in the epic's table (ADR-006 Verification Required 1–3)

**ADR Governing Implementation**: ADR-006 (Decision 2, the `link.xml` requirement, Verification Required 1–3); ADR-012 ("`IronGrind.ServerLogic` is the only assembly that may reference Npgsql and Dapper … added to the asmdef's precompiled references when ADR-006's code is written"; a client build contains neither DLL).
**ADR Decision Summary**: Npgsql and Dapper are server-only .NET libraries referenced by one assembly, kept out of client builds, and preserved from IL2CPP stripping.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM
**Engine Notes**: ADR-006 Verification Required — (1) Npgsql works on a headless Linux server build; (2) Dapper's `link.xml` needs under IL2CPP; (3) the connection pool under the headless process lifecycle. None is verified. Whether a current Npgsql build targets a .NET profile Unity 6.3 accepts (.NET Standard 2.1) and which of its dependencies must come with it are **not known** — check before choosing a version.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` is the only assembly that references Npgsql and Dapper — ADR-012
- Forbidden: Entity Framework Core; SQLite — ADR-006
- Forbidden: a client player build that contains `Npgsql` or `Dapper` — ADR-012

---

## Acceptance Criteria

- [ ] **In the project**: the Npgsql and Dapper assemblies and each dependency they need are in the project at pinned versions, recorded in a file under `docs/` with where each came from.
- [ ] **Referenced by one assembly**: `IronGrind.ServerLogic.asmdef` lists them as precompiled references; no other asmdef does. The assembly-boundary tests are updated to assert exactly that, and pass.
- [ ] **Server only**: the DLLs' import settings keep them out of a client player build (ADR-012's client-binary rule).
- [ ] **Compiles**: a type in `ServerLogic` that names `NpgsqlDataSource` and a Dapper extension method compiles in the Editor; the EditMode suite still passes.
- [ ] **`link.xml`**: the two `preserve="all"` entries of ADR-006 are present.
- [ ] **Verification, as far as it can be run** — recorded in `production/qa/evidence/character-persistence-012-npgsql-verification.md`, each item PASS / FAIL / NOT RUN with its observation: (a) in the Editor, a connection opens against a PostgreSQL instance and `SELECT 1` returns through Dapper; (b) ADR-006 item 1, a headless Linux server build; (c) item 2, IL2CPP; (d) item 3, the pool across start and shutdown.
- [ ] **Engine reference**: what was confirmed is added to `docs/engine-reference/unity/`.

---

## Implementation Notes

- This story writes no persistence behaviour; it ends when a query round-trips.
- ADR-012's Verification Required items on server and client builds overlap with (b) and (c): one build run can serve both.

**Open decisions for `/story-readiness`**

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
- **Round trip** — Setup: a reachable PostgreSQL instance. Verify: `SELECT 1` through Dapper. Pass: the value 1 returns and the connection goes back to the pool.
- **Build items** — Setup: a headless Linux server build with IL2CPP. Verify: the same round trip. Pass: it returns; otherwise the observation is recorded as FAIL with the error text.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `production/qa/evidence/character-persistence-012-npgsql-verification.md`; the updated assembly-boundary tests passing.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **the user's decision on open decisions 1 and 2 (blocking)**
- Unlocks: Stories 014, 015, 016
