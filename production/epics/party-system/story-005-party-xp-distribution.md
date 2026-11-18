# Story 005: Party XP Distribution

> **Epic**: Party System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-5, CR-PS-10, F-PS-1, Edge Cases (formula boundary, same-tick kill and leave, Disbanding); `leveling-system.md` CR-1.5; `death-and-respawn.md` CR-DR-19
**Requirement**: `TR-party-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: XP is a direct call, not an `OnMobDied` subscriber (ADR-010 note 2026-10-30): the kill path calls the XP sink with the attacker and amount.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: dependency injection; doc comments; deterministic tests
- Guardrail: no allocation per kill (member buffers sized `MaxPartySize`)

---

## Acceptance Criteria

- [ ] **AC-1 One seam**: both kill paths (`AutoAttackCombatSystem` via `IAutoAttackExperienceSink`, `SkillDamageExecutionStep` via its XP sink) reach one `PartyExperienceDistributor` that receives (killer EntityID, `XP_base`); without a party service the old direct `AddExperience(killer, XP_base)` behaviour is unchanged (solo).
- [ ] **AC-2 Split**: the distributor finds the killer's party, counts `N_eligible` (Story 004 eligibility), and calls `AddExperience(member, F-PS-1)` once per eligible member: 1000 with N 1 → 1000 (AC-PS-9), N 2 → 900 each (AC-PS-10), N 4 → 700 each (AC-PS-11/12).
- [ ] **AC-3 Ineligible**: an OutOfZone member (AC-PS-14), a member > 5 levels from the leader (AC-PS-15) and a Dead member (AC-DR-13b, AC-DR-18) get 0 and are not counted; N 0 → no `AddExperience` calls and one log line recording zero awards (AC-PS-13).
- [ ] **AC-4 Capped member**: a level-60 member counts toward N (party of 4 → ×0.70 for the others) and its `AddExperience` call is made (a no-op in Leveling) (AC-PS-30).
- [ ] **AC-5 Ordering**: eligibility is snapshotted at the kill: a member removed later in the same tick still receives that kill's XP (EC same-tick kill / leave); a party in `Disbanding` still awards (AC-PS-24, CR-PS-10).

---

## Implementation Notes

- Read `AutoAttackCombatSystem.cs:571`, `CharacterStatsExperienceSink.cs`, `SkillDamageExecutionStep.cs:126` first; prefer one adapter implementing both XP sink interfaces so neither combat system changes beyond construction.
- Leveling's `AddExperience` takes an EntityID; party members are stored by CharacterID with an EntityID per slot (Story 002) — an OutOfZone member has EntityID 0 and is never called.
- Same-tick ordering (AC-5): membership changes requested by inbound requests run in the dispatch phase (ADR-014), before kills in the simulation phase; confirm against the phase list at readiness and assert the documented order.

---

## Out of Scope

- Gold split (F-PS-2, moved out of the epic)
- Story 011: wiring the distributor into the composed zone

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: fake party service absent → one call with full XP; both kill-path sink interfaces route to the distributor.
- **AC-2**: table N 1/2/3/4 with recording XP sink (member, amount).
- **AC-3**: each ineligible kind + all-ineligible (0 calls, log line asserted).
- **AC-4**: L60 member in a party of 4 → 4 calls, 700 each.
- **AC-5**: kill then remove within one tick → removed member got XP; party Disbanding → awards normal.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_XpDistribution_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 003, 004
- Unlocks: Story 011
