# Story 005: GetTownRespawnPoint (CR-ZI-11)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22; readiness fixes 2026-10-23
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-19
> **Estimate**: 3 hours (confirmed at `/story-readiness` 2026-10-23: cache class, interface, log-once and allocation tests)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-11, AC-ZI-9, AC-ZI-10, AC-ZI-18
**Requirement**: `TR-zi-011` *(epic placeholder — not yet in `docs/architecture/tr-registry.yaml`; registered by the next `/architecture-review`, same as Stories 003 and 004)*

**ADR Governing Implementation**: ADR-009: Scene / Zone-Load Management (`ZoneDefinition` holds the respawn point)
**ADR Decision Summary**: static zone data comes from the `ZoneDefinition` asset loaded at startup.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**:
- `GC.GetAllocatedBytesForCurrentThread()` is the allocation measure (AC-ZI-9) — not `GC.GetTotalMemory`.
- `ZoneTemplateID` is a `byte` enum. If the AC-ZI-9 test shows the enum-keyed `Dictionary` lookup allocating under Mono (default comparer boxing), give the dictionary a small `IEqualityComparer<ZoneTemplateID>` that compares the `byte` values; do not change the key type.

**Control Manifest Rules (this layer)**:
- Required: `IronGrind.ServerLogic` — ADR-012
- Guardrail: O(1), synchronous, zero allocation per call (on the success path)

---

## Acceptance Criteria

- [ ] `ITownRespawnPointProvider.GetTownRespawnPoint(ZoneID) : Vector3` is implemented by `TownRespawnPointCache`, constructed once from the process's own `ZoneID` and the loaded `ZoneDefinition`. The constructor resolves the point (authored or fallback), stores it in a `Dictionary<ZoneTemplateID, Vector3>` keyed on `definition.TemplateId`, and logs any fallback. A null definition throws `ArgumentNullException`.
- [ ] **AC-ZI-9**: with an authored point (`TownRespawnPointAuthored == true`), 1,000 consecutive calls with the own `ZoneID` return the same `Vector3` and the `GC.GetAllocatedBytesForCurrentThread()` delta across them is 0 (no `GC.Collect()` during the measurement).
- [ ] **AC-ZI-18**: no authored point but valid bounds → returns `(boundsMin + boundsMax) * 0.5f`; logs one `TownRespawnPointNotAuthored` **warning**; never `Vector3.zero` for that case; no exception.
- [ ] **AC-ZI-10**: no authored point and no valid bounds → returns `Vector3.zero`; logs one `TownRespawnPointMissing` critical alert; no exception. "No valid bounds" means `ZoneDefinitionBoundsValidator.AreBoundsValid(min, max, out _)` returns false (Story 004's rule — non-finite, beyond ±250 m, or min > max). Zero-size bounds (min == max) are valid under that rule and take the AC-ZI-18 path.
- [ ] The fallback is decided once in the constructor: the warning or alert is logged once, and later calls log nothing.
- [ ] A `ZoneID` other than the process's own returns `Vector3.zero` and logs a `TownRespawnPointMissing` critical alert naming that `ZoneID`, on every such call; no exception. (This is an error path: allocation and per-call logging are allowed there.)

---

## Implementation Notes

- The cache is keyed on template, so a closing zone never invalidates it (CR-ZI-11): the cache does not observe the lifecycle and keeps answering after the zone reaches Closed.
- The GDD's `ZoneID → ZoneTemplateID` "routing table" does not exist in a zone process (one process = one zone). The cache holds the own `ZoneID → template` mapping it was given; any other `ZoneID` is a caller error (see the last criterion).
- In a running server the AC-ZI-10 path cannot reach Active: S6 closes a zone whose bounds fail validation. The path is defensive; tests exercise it directly.
- Log format, matching the existing `EntityIDCounterExhausted` alert in `ZoneSlotPool`:
  - warning: `Debug.LogWarning("[ZoneInstancing] TownRespawnPointNotAuthored: …")` with the template and the centroid;
  - critical alert: `Debug.LogError("[ZoneInstancing] TownRespawnPointMissing: …")` with the template or the foreign `ZoneID`.
  Tests check them with `LogAssert.Expect` and `LogAssert.NoUnexpectedReceived`.
- Tests build `ZoneDefinition` instances with `ScriptableObject.CreateInstance` and the internal `Initialize` (Story 004), and destroy them in teardown.

**Files**
- `src/ServerLogic/ZoneInstancing/ITownRespawnPointProvider.cs` — new interface (the Death & Respawn seam)
- `src/ServerLogic/ZoneInstancing/TownRespawnPointCache.cs` — new
- `tests/EditMode/ZoneInstancing/ZoneInstancing_TownRespawnPoint_tests.cs` — new

---

## Out of Scope

- Death & Respawn (the caller, CR-DR-8)
- Story 004: `ZoneDefinition` itself
- Constructing the cache in `ZoneServerComposition` / `ZoneServerHost` (after S6 validation, from `ZoneDefinitionSlot.Current` and the startup `ZoneID`) — Story 014, which wires Stories 001–013's systems into the composition
- A multi-template routing table (a gateway concern; not in a zone process)

---

## QA Test Cases

- **AC-ZI-9** — Given authored (10, 0, −5); When 1,000 calls with the own `ZoneID`; Then all equal (10, 0, −5) and allocation delta 0; no log.
- **AC-ZI-18** — Given bounds (−100, 0, −50)..(100, 10, 50), no point; Then (0, 5, 0), one warning.
- **AC-ZI-18 edge** — Given zero-size bounds (3, 0, 4)..(3, 0, 4), no point; Then (3, 0, 4), one warning (not the alert).
- **AC-ZI-18 ignores an unauthored value** — Given `TownRespawnPointAuthored == false` but `TownRespawnPoint` = (7, 7, 7) and valid bounds; Then the centroid, not (7, 7, 7).
- **AC-ZI-10** — Given no point and bounds that fail `AreBoundsValid` (one case each: min.x > max.x; max.z = 251; min.y = NaN); Then `Vector3.zero`, one critical alert, no throw.
- **Log once** — Given the AC-ZI-18 definition; When 3 calls; Then exactly one warning (logged at construction) and none from the calls.
- **Closed zone** — Given an authored point; When the call is made after the caller's lifecycle reached Closed (the cache holds no lifecycle reference); Then the same point.
- **Foreign ZoneID** — Given own `ZoneID(1)`; When called with `ZoneID(2)` twice; Then `Vector3.zero` both times, two critical alerts naming `ZoneID(2)`, no throw; a following call with `ZoneID(1)` still returns the point.
- **Null definition** — constructor throws `ArgumentNullException`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/ZoneInstancing/ZoneInstancing_TownRespawnPoint_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 004 (Complete 2026-10-23 — `ZoneDefinition`, `TownRespawnPoint`, `TownRespawnPointAuthored`, `ZoneDefinitionBoundsValidator.AreBoundsValid`)
- Unlocks: Story 014 (wires the cache into the composition), the Death & Respawn epic
