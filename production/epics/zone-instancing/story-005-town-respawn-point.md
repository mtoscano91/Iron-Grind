# Story 005: GetTownRespawnPoint (CR-ZI-11)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 2 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-11, AC-ZI-9, AC-ZI-10, AC-ZI-18
**Requirement**: `TR-zi-011` *(epic placeholder)*

**ADR Governing Implementation**: ADR-009: Scene / Zone-Load Management (`ZoneDefinition` holds the respawn point)
**ADR Decision Summary**: static zone data comes from the `ZoneDefinition` asset loaded at startup.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `GC.GetAllocatedBytesForCurrentThread()` is the allocation measure (AC-ZI-9) — not `GC.GetTotalMemory`.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` — ADR-012
- Guardrail: O(1), synchronous, zero allocation per call

---

## Acceptance Criteria

- [ ] `GetTownRespawnPoint(ZoneID) : Vector3` backed by a `Dictionary<ZoneTemplateID, Vector3>` built once at startup from the loaded `ZoneDefinition`(s); the zone process maps its own `ZoneID` to its template.
- [ ] **AC-ZI-9**: with an authored point, 1,000 consecutive calls return the same `Vector3` and the `GC.GetAllocatedBytesForCurrentThread()` delta across them is 0 (no `GC.Collect()` during the measurement).
- [ ] **AC-ZI-18**: no authored point but valid bounds → returns `(boundsMin + boundsMax) * 0.5f`; logs a `TownRespawnPointNotAuthored` **warning**; never `Vector3.zero` for that case; no exception.
- [ ] **AC-ZI-10**: no authored point and no valid bounds → returns `Vector3.zero`; logs a `TownRespawnPointMissing` critical alert; no exception.
- [ ] The fallback is decided once at cache build (the log fires once, not per call).

---

## Implementation Notes

- The cache is keyed on template, so a closing zone never invalidates it (CR-ZI-11).

**Open decisions for `/story-readiness`**

1. "No valid walkable bounds" (AC-ZI-10) needs a definition: unset bounds (min == max), or bounds that fail Story 004's rule. Pick one and state it in the test.
2. A `ZoneID` that is not this process's zone: return the process template's point, or `Vector3.zero` + `TownRespawnPointMissing`. The GDD assumes a routing table the zone process does not have.

---

## Out of Scope

- Death & Respawn (the caller, CR-DR-8)
- Story 004: `ZoneDefinition` itself

---

## QA Test Cases

- **AC-ZI-9** — Given authored (10, 0, −5); When 1,000 calls; Then all equal and allocation delta 0.
- **AC-ZI-18** — Given bounds (−100, 0, −50)..(100, 10, 50), no point; Then (0, 5, 0), one warning.
- **AC-ZI-10** — Given no point, no valid bounds; Then `Vector3.zero`, one critical alert, no throw.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_TownRespawnPoint_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004
- Unlocks: the Death & Respawn epic
