# Story 001: Party Config and F-PS-1

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — Formulas (F-PS-1), Tuning Knobs, Edge Cases (formula boundary)
**Requirement**: `TR-party-005` (formula part)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012: Server/Client Assembly Boundary
**ADR Decision Summary**: Server gameplay code lives in `IronGrind.ServerLogic`; config is a ScriptableObject read into a plain values struct.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#; `ScriptableObject` only)
**Engine Notes**: `[SerializeField]` on fields only (Unity 6.3).

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven; doc comments on public APIs; no hardcoded tuning numbers in logic
- Guardrail: formula is pure and allocation-free

---

## Readiness Decisions (2026-11-18)

Found at `/story-readiness`: `Mathf.RoundToInt` is `(int)Math.Round(f)` (round half to even) and `1 − 0.10f` is 0.89999998, so the GDD's midpoint example (`XP_base` 5, N 2 → 5) gives 4; the GDD allows `MAX_PARTY_SIZE` up to 6 but `PartyStateUpdate` is fixed at 4 member slots; `InviteRetrySeconds` and `DisbandTimeoutSeconds` had no safe range. User decisions: keep float + `Mathf.RoundToInt` (as the GDD formula and Leveling) and correct the GDD edge case; limit `MaxPartySize` to [2, 4] (GDD tuning table and `entities.yaml` updated); retry [1, 10] s and below the invite timeout, disband timeout [5, 30] s.

---

## Acceptance Criteria

- [x] **AC-1 Config**: `PartySystemConfig` (ScriptableObject) + `PartySystemConfigValues` (readonly struct, `Defaults`, `FromAsset`, `Validate()`) hold every GDD knob with its safe range: `MaxPartySize` 4 [2, 4], `XpPartyDeductionRate` 0.10 [0.00, 0.20], `LevelRangeGate` 5 [0, 15], `InviteTimeoutSeconds` 60 [30, 120], `InviteRetrySeconds` 5 [1, 10], `ReconnectGraceSeconds` 5 [2, 30], `HpDeltaThresholdFraction` 0.05 [0.00, 0.20], `DisbandTimeoutSeconds` 10 [5, 30]; tick conversions `Mathf.RoundToInt(seconds × ServerTickLoop.TICK_RATE_HZ)` (20 Hz).
- [x] **AC-2 Validation**: `Validate()` returns false and logs one error per failing field for any value outside its safe range (a `MaxPartySize` above 4 names the fixed 4-slot wire schema in its message), for `InviteRetrySeconds ≥ InviteTimeoutSeconds`, and for a joint-constraint breach `XpPartyDeductionRate × (MaxPartySize − 1) ≥ 1.0`; `Defaults` validate.
- [x] **AC-3 F-PS-1**: `PartyXpFormula.Compute(xpBase, nEligible, rate)` returns `Mathf.RoundToInt(Mathf.Max(0f, xpBase × (1f − rate × (nEligible − 1))))` in float: 1000 at N 1 → 1000 (AC-PS-9), N 2 → 900 (AC-PS-10), N 4 → 700 (AC-PS-11/12); `XP_base` 200 at N 4 → 140; 2,000,000 at N 4 → 1,400,000; `XP_base` 5 at N 2 → **4** (float 4.4999998, banker's rounding — GDD edge case corrected 2026-11-18).
- [x] **AC-4 Guards**: `nEligible ≤ 0` returns 0 without evaluating the formula (EC: unguarded N 0 would give ×1.10); `xpBase ≤ 0` returns 0; a rate that would make the multiplier negative returns 0 (`Max(0, …)` floor).

---

## Implementation Notes

- Folder `src/ServerLogic/PartySystem/`, namespace `IronGrind.ServerLogic.PartySystem` (as `DeathAndRespawn`).
- Follow `DeathAndRespawnConfig` / `DeathAndRespawnConfigValues` (Story 001 of that epic): defaults + internal composition override seam, no shipped asset required.
- `PartyXpFormula` is static and pure; float maths and `Mathf.RoundToInt` (not a cast, not double) per GDD F-PS-1 notes and the readiness decision.
- With the [2, 4] size range and [0.00, 0.20] rate range the joint constraint always holds; keep the check (it guards future range changes) as an internal static helper so it is testable.

---

## Out of Scope

- Story 005: who is eligible and calling `AddExperience`
- Story 011: composition override seam wiring

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: `Defaults` equal the GDD values; tick conversions 60 s → 1200, 5 s → 100, 10 s → 200.
- **AC-2**: each field just outside its range → false + one error naming the field (size 5 → message mentions the 4-slot wire schema); retry 60 with timeout 60 → error; both ends of each range valid; the joint constraint cannot fail inside the ranges (max 0.20 × 3 = 0.6) — test it through an internal check helper with rate 0.5, size 3 (1.0 → error).
- **AC-3**: table-driven cases above, including the 5 → 4 midpoint.
- **AC-4**: N 0, N −1, xpBase 0, rate 0.6 with N 4 → 0.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_ConfigAndFormula_tests.cs` — must exist and pass

**Status**: [x] Created — 30 test methods, passing

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002, 005

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 4/4 passing (all automated)
**Deviations**: Advisory — `PartyXpFormula.Compute` returns `xpBase` unchanged when the multiplier is ≥ 1 (N 1, rate 0, or a negative rate the config refuses) and 0 for a NaN multiplier, instead of `RoundToInt(Max(0, …))` throughout. Same result for every valid config; avoids the `(float)int.MaxValue` = 2^31 int-cast overflow and never awards more than the base.
**Test Evidence**: Logic: `tests/EditMode/PartySystem/PartySystem_ConfigAndFormula_tests.cs` (30 methods); full EditMode `-testCategory "!Database"` 8133/8133 (2026-11-18 23:31Z)
**Code Review**: Complete — APPROVED WITH SUGGESTIONS; required fix and all suggestions applied
