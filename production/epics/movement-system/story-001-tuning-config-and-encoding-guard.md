# Story 001: Tuning Config and Position Encoding Guard

> **Epic**: Movement System
> **Status**: Complete — 2026-10-29 (`/story-done`: COMPLETE WITH NOTES); written 2026-10-29 (`/create-stories`, lean); `/story-readiness` 2026-10-29: NEEDS WORK (4 gaps) → fixed → Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — Tuning Knobs (TK-MOV-1–6), F-MOV-6, EC-MOV-20; AC-MOV-11
**Requirement**: `TR-mov-010`, `TR-mov-006` (encoding guard part)
*(Placeholder TR-IDs — not yet in `docs/architecture/tr-registry.yaml`; see the epic.)*

**ADR Governing Implementation**: ADR-012 (assembly placement)
**ADR Decision Summary**: server-only movement code in `IronGrind.ServerLogic`; `IronGrind.Client` must not reference ServerLogic, so anything both sides read lives in `IronGrind.Foundation`. The tuning asset holds client knobs (radius, dead zone, camera sensitivity) and server knobs (stale tolerance, stall ticks), so it goes in Foundation.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `ScriptableObject` with `[SerializeField]` on **fields only** (Unity 6.3: on properties it is a compile error — `VERSION.md`).

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven (coding standards), exposed through a ScriptableObject (GDD Tuning authority)
- Required: dependencies injected through constructor interfaces, not a singleton asset lookup
- Forbidden: `Resources.Load` for runtime data on the server path

---

## Acceptance Criteria

- [x] A `MovementTuning` ScriptableObject holds the six knobs with the GDD defaults: `JOYSTICK_RADIUS_PX` 80, `DEAD_ZONE_NORMALIZED` 0.15, `BASE_MOVE_SPEED` 5.0 (registry, revised 2026-10-29), `STALE_TICK_TOLERANCE` 3, `CAMERA_DRAG_SENSITIVITY` 0.25 °/px, `NAVMESH_STALL_RECOVERY_TICKS` 10.
- [x] A plain C# read model (`MovementTuningValues` or similar, a readonly struct) is what the systems take in their constructors, so tests build it without an asset. `MovementTuningValues.Defaults` equals the six defaults above.
- [x] Each knob has its GDD safe range in code as named constants (60–120, 0.05–0.30, 4.0–10.0, 2–5, 0.10–0.50, 5–30). **Out of range (readiness decision 2026-10-29): clamp into the range** when `MovementTuningValues` is built from the asset, and log one warning per clamped knob naming the knob, the asset value and the clamped value. Values inside the range log nothing. In the Editor, `OnValidate` logs the same warning so the designer sees it on edit. The server keeps starting with a bad asset.
- [x] **`BASE_MOVE_SPEED` is a reference value only (readiness decision 2026-10-29).** The live base speed is the Character Stats starting value (`CharacterStubDefaults.MOVEMENT_SPEED`, owned by Character Persistence), which Story 003 reads through `GetEffectiveStat`. The field's doc comment says so: changing it in the asset does not change movement speed. A test asserts `Defaults.BaseMoveSpeed == CharacterStubDefaults.MOVEMENT_SPEED` (5.0), so the 5.0/6.0 drift cannot come back silently. Making starting stats read this asset is a later change, not this story.
- [x] **AC-MOV-11**: encoding then decoding 1,000 positions through the existing `WireFixedPointCodec.EncodePosition` / `DecodePosition` (Foundation, ×100 shorts) gives a round-trip error ≤ 0.005 on every axis. The positions are a deterministic sweep, `−327.0 + i × 0.654` for i = 0..999 on each axis, plus the edge values −327.0, 0, 327.0 and values ending in .005. No random generator, seeded or not (testing standards).
- [x] F-MOV-6 guard: a static `IsEncodable(Vector3 candidate)` returns true only when `|x| ≤ WireFixedPointCodec.MaxPositionMeters` and `|z| ≤ MaxPositionMeters` (327.67; reuse the constant, no second one). 327.67 → true, 327.68 → false, −327.67 → true, NaN → false. Y is not checked. Story 007 calls it before commit, so a candidate never reaches the codec's own clamp-and-log path.

---

## Implementation Notes

- Files (readiness decision 2026-10-29): `src/Foundation/MovementSystem/MovementTuning.cs` (ScriptableObject, private `[SerializeField]` fields with PascalCase read-only properties, as `ItemDefinition`) and `MovementTuningValues.cs` (readonly struct, `Defaults`, the range constants, a `FromAsset(MovementTuning)` that clamps and warns). Namespace `IronGrind.MovementSystem`. The guard goes in `src/ServerLogic/MovementSystem/MovementPositionGuard.cs`. One asset serves server and client.
- Reuse `WireFixedPointCodec.EncodePosition` / `DecodePosition` / `MaxPositionMeters` (CR-NET-7.2). `EntityPositionUpdate` is only the struct that carries the encoded shorts.
- The clamp warning is built only on the clamp path, so a valid asset allocates nothing beyond the struct.
- No Unity asset is created in this story beyond the ScriptableObject class; the asset instance comes with Story 009's composition wiring.

---

## Out of Scope

- Story 003: using `BASE_MOVE_SPEED` (speed comes from `GetEffectiveStat`, not this asset)
- Story 007: applying the encoding guard in the tick
- Stories 010–012: the client knobs' behaviour

---

## QA Test Cases

- **AC-1 defaults**: Given `MovementTuningValues.Defaults` — Then each of the six fields equals the GDD default. Edge: a freshly created `MovementTuning` asset (`ScriptableObject.CreateInstance`) maps to the same values.
- **AC-2 speed parity**: Given `CharacterStubDefaults.MOVEMENT_SPEED` — Then it equals `Defaults.BaseMoveSpeed` (5.0).
- **AC-3 ranges**: each knob at its min and at its max → kept, no log. One step below the min and one above the max for all six knobs → clamped to the bound, with exactly one warning naming the knob (`LogAssert.Expect`). An asset with two bad knobs → two warnings. Example: radius 50 → 60; dead zone 0.40 → 0.30; stale tolerance 1 → 2.
- **AC-MOV-11**: Given the sweep `−327.0 + i × 0.654` (i = 0..999) on x, y and z, plus −327.0, 0, 327.0, 1.005, −1.005 — When put through `EncodePosition` then `DecodePosition` — Then max |error| ≤ 0.005 per axis, and no warning is logged (all inside 327.67).
- **Guard**: 327.67, 327.68, −327.67, −327.68, NaN (→ false) on x and on z separately; y is not checked.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_Tuning_tests.cs` — must exist and pass. The test asmdef already references Foundation, ServerLogic and Client.

**Status**: [x] Created — `tests/EditMode/MovementSystem/MovementSystem_Tuning_tests.cs`, 17 methods / 59 cases, all passing (EditMode 4961 / 4832 passed / 0 failed / 129 skipped, 2026-10-29)

---

## Dependencies

- Depends on: None
- Unlocks: Stories 003, 004, 007, 010, 011

---

## Completion Notes

**Completed**: 2026-10-29
**Criteria**: 6/6 passing (all covered by automated tests; none deferred)
**Deviations** (advisory, no tech debt — user decision):
1. AC-MOV-11 is asserted as error ≤ 0.005 + 1e-4: a float sample such as 1.005f is stored a few ULPs off the decimal value. A truncating codec (error up to 0.01) still fails; the sweep also asserts error > 0 and a known-sample test checks rounding to the nearest centimetre.
2. `MovementTuning.OnValidate` is compiled only under `UNITY_EDITOR` (Unity never calls it in a player build).
3. TR-mov-001–010 are placeholders, not yet in `docs/architecture/tr-registry.yaml` (same as the other epics; the next `/architecture-review` registers them).
**Scope**: `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` gained the two Foundation entries the ADR-012 boundary test requires (consumer planned: Stories 010–012).
**Files**: `src/Foundation/MovementSystem/MovementTuning.cs`, `MovementTuningValues.cs`; `src/ServerLogic/MovementSystem/MovementPositionGuard.cs`; test file above; Unity-generated `.meta` files.
**Test Evidence**: Logic — `tests/EditMode/MovementSystem/MovementSystem_Tuning_tests.cs`
**Code Review**: Complete — `/code-review` 2026-10-29: APPROVED WITH SUGGESTIONS, "fix all" applied (`IEquatable` left out — no comparison caller). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
