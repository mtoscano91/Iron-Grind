# Story 001: Party Config and F-PS-1

> **Epic**: Party System
> **Status**: Ready
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

## Acceptance Criteria

- [ ] **AC-1 Config**: `PartySystemConfig` (ScriptableObject) + `PartySystemConfigValues` (readonly struct, `Defaults`, `FromAsset`, `Validate()`) hold every GDD knob: `MaxPartySize` 4 [2, 6], `XpPartyDeductionRate` 0.10 [0.00, 0.20], `LevelRangeGate` 5 [0, 15], `InviteTimeoutSeconds` 60 [30, 120], `InviteRetrySeconds` 5, `ReconnectGraceSeconds` 5 [2, 30], `HpDeltaThresholdFraction` 0.05 [0.00, 0.20], `DisbandTimeoutSeconds` 10; tick conversions via `TICK_RATE_HZ = 20` (`RoundToInt(seconds × 20)`).
- [ ] **AC-2 Validation**: `Validate()` returns false and logs one error per failing field for any value outside its safe range, and for a joint-constraint breach `XpPartyDeductionRate × (MaxPartySize − 1) ≥ 1.0`; `Defaults` validate.
- [ ] **AC-3 F-PS-1**: `PartyXpFormula.Compute(xpBase, nEligible, rate)` returns `Mathf.RoundToInt(Mathf.Max(0f, xpBase × (1 − rate × (nEligible − 1))))`: 1000 at N 1 → 1000 (AC-PS-9), N 2 → 900 (AC-PS-10), N 4 → 700 (AC-PS-11/12); `XP_base` 200 at N 4 → 140; midpoint 5 at N 2 → 5 (round half away from zero).
- [ ] **AC-4 Guards**: `nEligible ≤ 0` returns 0 without evaluating the formula (EC: unguarded N 0 would give ×1.10); `xpBase ≤ 0` returns 0; a rate that would make the multiplier negative returns 0 (`Max(0, …)` floor).

---

## Implementation Notes

- Folder `src/ServerLogic/PartySystem/`, namespace `IronGrind.ServerLogic.PartySystem` (as `DeathAndRespawn`).
- Follow `DeathAndRespawnConfig` / `DeathAndRespawnConfigValues` (Story 001 of that epic): defaults + internal composition override seam, no shipped asset required.
- `PartyXpFormula` is static and pure; `Mathf.RoundToInt` (not a cast) per GDD F-PS-1 notes.

---

## Out of Scope

- Story 005: who is eligible and calling `AddExperience`
- Story 011: composition override seam wiring

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: `Defaults` equal the GDD values; tick conversions 60 s → 1200, 5 s → 100, 10 s → 200.
- **AC-2**: each field just outside its range → false + one error naming the field; both in range but joint ≥ 1.0 (rate 0.20 with size 6: 0.20 × 5 = 1.0) → joint-constraint error; rate 0.20 with size 5 (0.8) → valid.
- **AC-3**: table-driven cases above plus 2,000,000 at N 4 → 1,400,000.
- **AC-4**: N 0, N −1, xpBase 0, rate 0.6 with N 4 → 0.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_ConfigAndFormula_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 002, 005
