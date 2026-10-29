# Story 001: Tuning Config and Position Encoding Guard

> **Epic**: Movement System
> **Status**: Ready — written 2026-10-29 (`/create-stories`, lean)
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/movement-system.md` — Tuning Knobs (TK-MOV-1–6), F-MOV-6, EC-MOV-20; AC-MOV-11
**Requirement**: `TR-mov-010`, `TR-mov-006` (encoding guard part)
*(Placeholder TR-IDs — not yet in `docs/architecture/tr-registry.yaml`; see the epic.)*

**ADR Governing Implementation**: ADR-012 (assembly placement)
**ADR Decision Summary**: server-only movement code in `IronGrind.ServerLogic`; values both sides read (the dead zone and joystick radius are client-only, `STALE_TICK_TOLERANCE` is server-only) stay in the assembly that uses them, or in `IronGrind.Foundation` when both do.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: `ScriptableObject` with `[SerializeField]` on **fields only** (Unity 6.3: on properties it is a compile error — `VERSION.md`).

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven (coding standards), exposed through a ScriptableObject (GDD Tuning authority)
- Required: dependencies injected through constructor interfaces, not a singleton asset lookup
- Forbidden: `Resources.Load` for runtime data on the server path

---

## Acceptance Criteria

- [ ] A `MovementTuning` ScriptableObject holds the six knobs with the GDD defaults: `JOYSTICK_RADIUS_PX` 80, `DEAD_ZONE_NORMALIZED` 0.15, `BASE_MOVE_SPEED` 5.0 (registry, revised 2026-10-29), `STALE_TICK_TOLERANCE` 3, `CAMERA_DRAG_SENSITIVITY` 0.25 °/px, `NAVMESH_STALL_RECOVERY_TICKS` 10.
- [ ] A plain C# read model (`MovementTuningValues` or similar, a readonly struct) is what the systems take in their constructors, so tests build it without an asset. `MovementTuningValues.Defaults` equals the six defaults above.
- [ ] Each knob has its GDD safe range in code (60–120, 0.05–0.30, 4.0–10.0, 2–5, 0.10–0.50, 5–30). **Out-of-range behaviour is decided at `/story-readiness`** (the GDD gives the ranges, not what happens outside them).
- [ ] `BASE_MOVE_SPEED` matches `CharacterStubDefaults.MOVEMENT_SPEED` (5.0); a test asserts the two are equal, so the 5.0/6.0 drift cannot come back silently.
- [ ] **AC-MOV-11**: encoding then decoding 1,000 positions in ±327.0 per axis with `RoundToInt(pos × 100)` / `÷ 100` gives a round-trip error ≤ 0.005 on every axis. The 1,000 positions are deterministic (a fixed list or a fixed-seed generator built in the test; no unseeded random — testing standards).
- [ ] F-MOV-6 guard: a static check `IsEncodable(Vector3 candidate)` returns true only when `|x| ≤ 327.67` and `|z| ≤ 327.67`; 327.67 → true, 327.68 → false, −327.67 → true. (Story 007 uses it before commit.)

---

## Implementation Notes

- Files: `src/ServerLogic/MovementSystem/` (namespace `IronGrind.MovementSystem`) for the server read model and guard; the ScriptableObject can live in Foundation if the client stories (010–012) read the same asset — decide in `/dev-story`, keep one asset.
- Reuse the existing position encoding (`EntityPositionUpdate` / `CR-NET-7.2` ×100 short) rather than a second encoder; the test may call that encoder directly.
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
- **AC-3 ranges**: each knob at its min and max → accepted; one step outside each end → the behaviour chosen at readiness (test both ends for all six).
- **AC-MOV-11**: Given 1,000 deterministic positions in [−327.0, 327.0] (include −327.0, 0, 327.0, values ending in .005) — When encoded/decoded — Then max |error| ≤ 0.005 per axis.
- **Guard**: 327.67, 327.68, −327.67, −327.68, NaN (→ false) on x and on z separately; y is not checked.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/MovementSystem/MovementSystem_Tuning_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 003, 004, 007, 010, 011
