# Story 007: Client-Binary Scan

> **Epic**: Damage Calculation
> **Status**: Blocked — **no client build pipeline exists. ADR-012 Migration Plan step 3: the scan is written with the first client build pipeline.**
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-08
> **Estimate**: to be set when the client build pipeline exists

*Split from Story 006 on 2026-10-08: AC-DC-I-01 needs a real client build, which Story 006 (first move and boundary test) does not.*

## Context

**GDD**: `design/gdd/damage-calculation.md` — AC-DC-I-01 and AC-DC-I-01-MANUAL.
**Requirement**: `TR-dmg-001` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012 Server/Client Assembly Boundary — Decision 6, check 2.
**ADR Decision Summary**: a build-pipeline step under `tools/` lists the managed assemblies and type names that went into a client player and fails if `IronGrind.ServerLogic`, `Npgsql`, `Dapper` or a forbidden type name is present.

**Engine**: Unity 6.3 LTS | **Risk**: HIGH — the mechanism is not confirmed against the 6.3 manual (ADR-012 Verification Required 5): `IPostBuildPlayerScriptDLLs` and the output file locations (`ScriptingAssemblies.json`, `global-metadata.dat`) come from the engine specialist's memory of Unity 2021–6.0.
**Engine Notes**: confirm the callback and the file locations on a real client build before writing the scan around them.

**Control Manifest Rules (Foundation layer → Server/client assembly boundary)**:
- Required: the scan must be shown to fail on a deliberately misplaced type.
- Forbidden: `BuildReport.packedAssets`, `CompilationPipeline.GetAssemblies` or the `Library/Bee` folders as the source of the assembly list.

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story:*

- [ ] **AC-DC-I-01**: for a client player build, the automated scan finds no `IronGrind.ServerLogic`, `Npgsql` or `Dapper` assembly and no type named `DamageCalculator`, `DamageResult` or `DamageContext` (plus the names of every system moved by then).
- [ ] **The scan detects a violation**: with a forbidden type deliberately placed in `IronGrind.Foundation`, the client build fails at the scan step; the type is then removed.
- [ ] **Server build**: a Linux Dedicated Server IL2CPP build contains `IronGrind.ServerLogic`, does not contain `IronGrind.Client`, and starts (ADR-012 Validation Criteria).
- [ ] **AC-DC-I-01-MANUAL** is advisory only: a documented review with technical-director sign-off does not satisfy the gate.

---

## Implementation Notes

*To be written when the client build pipeline exists and Verification Required 5 has been checked on a real build. Do not start from this story as it stands.*

---

## Out of Scope

- The boundary test and the first move (Story 006).
- The content check (ADR-012 Decision 6, check 3).
- The client build pipeline itself.

---

## QA Test Cases

*To be written with the Implementation Notes.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: the scan step under `tools/` and a build log showing it pass on a clean client build and fail on the deliberate violation.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006; a client build pipeline (none exists); the Linux Dedicated Server module and Linux IL2CPP toolchain on the build machine for the server-build criterion.
- Unlocks: closing the epic (Definition of Done: all GDD acceptance criteria verified).
