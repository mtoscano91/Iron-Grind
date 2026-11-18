# Story 012: End-to-End — Two Players Party Up and Share a Kill

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-4, CR-PS-5, F-PS-1, CR-PS-9
**Requirement**: `TR-party-012` (end-to-end)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: The tick order from Story 011 is what this test runs.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: If PlayMode: test assembly has `InternalsVisibleTo` on ServerLogic (ADR-012 Decision 5 amendment).

**Control Manifest Rules (this layer)**:
- Required: deterministic tests (seeded `IRandomProvider`, no wall-clock assertions)

---

## Acceptance Criteria

- [ ] **AC-1 Form**: in a composed zone with two admitted players (levels within ±5), player A sends `PartyInviteRequest` for B, B sends `PartyInviteResponse(accepted)` → both receive a `PartyStateUpdate` (decoded) listing [A, B], leader A, `IsSoloParty` false.
- [ ] **AC-2 Share**: A kills a mob worth `XP_base` → A and B each gain exactly F-PS-1 for N 2 (`RoundToInt(0.9 × XP_base)`) in Character Stats / Leveling; no other XP is awarded for that kill.
- [ ] **AC-3 Leave**: B sends `PartyLeaveRequest` → B gets a new solo PartyID; the next kill by A awards A the full `XP_base`.
- [ ] **AC-4 Clean run**: no errors logged; the party steps' per-tick cost is logged (`[PartySystem012]`, max and p95).

---

## Implementation Notes

- EditMode composition (Death & Respawn Story 008 rig: real admission, handshake, seeded record store) is preferred if a mob kill can be driven there; otherwise a PlayMode rig as Death & Respawn Story 009 — decide at readiness.
- `XP_base` from the killed mob's definition / variant, not a literal.

---

## Out of Scope

- Loot round-robin end to end (real loot not composed — moved out of the epic)
- Client presentation

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: inbound invite / response through the dispatcher; recording send seam decodes both updates.
- **AC-2**: XP before / after for A and B; recording sink sees exactly two awards.
- **AC-3**: leave → solo id differs from the party id; next kill → one award of `XP_base`.
- **AC-4**: problem capture (errors) empty; perf line present.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_EndToEnd_tests.cs` (or `tests/PlayMode/PartySystem/` if decided at readiness) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 011
- Unlocks: None (last story of the epic)
