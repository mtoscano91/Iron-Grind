# Story 006: Server Assembly Isolation Scan

> **Epic**: Damage Calculation
> **Status**: Blocked — **the ADR specifying the server/client assembly boundary (`ServerLogic.asmdef`) does not exist. GDD Rule 1: it "must be authored before implementation begins". Run `/architecture-decision`.**
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-07
> **Estimate**: to be set once the ADR is Accepted

## Context

**GDD**: `design/gdd/damage-calculation.md` — Core Rule 1 (Enforcement), AC-DC-I-01 and AC-DC-I-01-MANUAL.
**Requirement**: `TR-dmg-001` (placeholder — `docs/architecture/tr-registry.yaml` is empty)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: none yet — **required**. The decision is wider than this epic: every server-only system currently lives in `IronGrind.Foundation` (`src/Foundation/IronGrind.Foundation.asmdef`), and the Currency epic scoped out its own Group G (Server Assembly Isolation) for the same reason.
**What the ADR has to decide**: which assemblies exist and what each may reference; how the server-only assembly is excluded from the client build (platform constraints or define constraints — the GDD says the `[Server]` attribute alone is insufficient); where shared types (`EntityID`, `ItemID`, wire types) live; how the EditMode test assembly references the server-only assembly; and how the client binary is scanned.

**Engine**: Unity 6.3 LTS | **Risk**: unclassified until the ADR — build configuration and IL2CPP stripping behaviour are engine-specific; check `docs/engine-reference/unity/` and verify on a real client build.
**Engine Notes**: none until the ADR.

**Control Manifest Rules**: none yet for the assembly boundary; the manifest gains them when the ADR is Accepted (`/create-control-manifest update`). Update this story's Manifest Version then.

---

## Acceptance Criteria

*From GDD `design/gdd/damage-calculation.md`, scoped to this story:*

- [ ] **AC-DC-I-01**: given a server build and a client build, an automated inspection of the client assembly (namespace scan of client IL or assembly manifests) finds no type matching `DamageCalculation`, `DamageResult` or `DamageContext` from the server-only assembly. *(In code the resolver class is `DamageCalculator` — Story 001; the scan must cover the names actually used.)*
- [ ] **Types moved**: the Damage Calculation types of Stories 001–004 reside in the server-only assembly the ADR defines, and the full EditMode suite still passes.
- [ ] **AC-DC-I-01-MANUAL** is advisory only: a documented review in `production/qa/evidence/` with technical-director sign-off does not satisfy the gate; the automated scan must exist before the Integration milestone.

---

## Implementation Notes

*To be written from the ADR's Implementation Guidelines once it is Accepted. Do not start from this story as it stands.*

- Stories 001–005 keep the Damage Calculation files free of client, UI and NGO references so that the move is an assembly change.

---

## Out of Scope

- The ADR itself (`/architecture-decision`).
- Moving other server-only systems (Currency, Enhancement, Loot Table, Networking server code) — each needs its own story once the ADR exists.
- The formula, crit and kill-detection behaviour (Stories 001–005).

---

## QA Test Cases

*To be written with the Implementation Notes. The shape the GDD fixes:*

- **AC-DC-I-01** — Setup: a client build produced by the project's build pipeline. Verify: the automated scan lists the types of the client assemblies and searches for the Damage Calculation type names. Pass condition: zero matches, and the scan fails when a Damage Calculation type is deliberately placed in a client assembly (the scan is proven able to detect a violation).

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: an automated assembly scan (location defined by the ADR) — must exist and pass. A manual evidence document alone is not sufficient (AC-DC-I-01-MANUAL).

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: **an Accepted ADR for the server/client assembly boundary**; Stories 001–004 (the types to move).
- Unlocks: closing the epic (Definition of Done: all GDD acceptance criteria verified).
