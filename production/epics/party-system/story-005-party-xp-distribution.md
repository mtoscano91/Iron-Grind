# Story 005: Party XP Distribution

> **Epic**: Party System
> **Status**: Complete
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

- [x] **AC-1 One seam**: `PartyExperienceDistributor` implements `IAutoAttackExperienceSink` and wraps the inner sink (`CharacterStatsExperienceSink`). Both kill paths already take that interface (`AutoAttackCombatSystem.cs:571`, `SkillDamageExecutionStep.cs:42/126`), so neither combat file changes. Pass-through (one inner `AddExperience(killer, XP_base)`, unchanged behaviour) when: the killer has no CharacterID (`PartyRegistry.TryGetCharacter` false), or the killer's party is a solo party (eligibility not evaluated; a Dead solo killer still gets XP as today). Without a party service the wrapper is simply not composed (Story 011).
- [x] **AC-2 Split**: for a killer in a real party, the distributor counts `N_eligible` with `PartyXpEligibility.IsXpEligible` (Story 004) and calls inner `AddExperience(memberEntity, PartyXpFormula.Compute(XP_base, N_eligible, rate))` once per eligible member, in join order: 1000 with N 1 → 1000 (AC-PS-9), N 2 → 900 each (AC-PS-10), N 4 → 700 each (AC-PS-11/12). The killer is treated like any other member: an ineligible killer (Dead, or > 5 levels from the leader) gets 0 while the eligible members split.
- [x] **AC-3 Ineligible**: an OutOfZone member (AC-PS-14), a member > 5 levels from the leader (AC-PS-15), a Ghost / Disconnected member and a Dead member (AC-DR-13b, AC-DR-18) get no call and are not counted; N 0 → no `AddExperience` calls, F-PS-1 not evaluated, and exactly one `Debug.Log` `"[PartyXp] party {partyId}: 0 eligible, 0 XP awarded"` (AC-PS-13).
- [x] **AC-4 Capped member**: a level-60 member counts toward N (party of 4 → 700 each) and its `AddExperience` call is made (a no-op in Leveling) (AC-PS-30).
- [x] **AC-5 Ordering**: eligibility is read at the kill call: a member removed after the kill in the same tick has already received that kill's XP, and the removal does not change the awards already made; a party in `Disbanding` still awards normally (AC-PS-24, CR-PS-10). The GDD same-tick rule for a leave *request* (EC line 201) is Story 008 / 011's (see Readiness Decisions G3).

---

## Readiness Decisions (2026-11-19)

- **G1 One seam.** Both kill paths share `IAutoAttackExperienceSink`; the distributor is a decorator around the existing sink. No combat-system change.
- **G2 Killer cases.** Solo party → pass-through (keeps "solo unchanged"). Real party → every member, killer included, goes through eligibility.
- **G3 Same-tick leave vs ADR-014.** ADR-014 runs dispatch (step 2) before simulation (step 3), so a `PartyLeaveRequest` handled directly in dispatch would remove the leaver before that tick's kill, against the GDD edge case. Decision: keep the GDD rule; Story 008's leave / disband handlers queue the change and a party membership step applies it after phase 3; Story 011 places that step and records an ADR-014 note. This story asserts only the call-time snapshot.
- **G4 Logging.** Only the zero-award line (AC-3). No per-member award log on each kill (hot path); AC-PS-24's "log records per-member XP awards" is verified with a recording sink instead — record the deviation in Completion Notes.
- **G5 Disbanding in tests.** Nothing reaches `Disbanding` until Story 008; add `internal PartyRegistry.SetStateForTests(PartyID, PartyLifecycleState)` (calls `PartyState.SetState`) for AC-5.

---

## Implementation Notes

- Read `AutoAttackCombatSystem.cs:571`, `CharacterStatsExperienceSink.cs`, `SkillDamageExecutionStep.cs:126` first. Constructor: `(IAutoAttackExperienceSink inner, PartyRegistry registry, PartyXpEligibility eligibility, PartySystemConfigValues config)`, nulls throw.
- Leveling's `AddExperience` takes an EntityID; party members are stored by CharacterID with an EntityID per slot (`PartyState.GetEntityAtIndex`) — an OutOfZone member keeps its slot EntityID; it is never called because it is ineligible (corrected at /story-done; the slot entity is not cleared).
- No allocation per kill: iterate the party's slots directly (count pass, then award pass); no LINQ, no lists.
- Same-tick ordering: see Readiness Decisions G3 — the distributor reads current state at the call; no snapshot buffer is needed because the award is synchronous inside the kill.

---

## Out of Scope

- Gold split (F-PS-2, moved out of the epic)
- Story 011: wiring the distributor into the composed zone

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

*Refined at `/story-readiness` 2026-11-19.*

- **AC-1**: unknown killer EntityID → one inner call (killer, 1000); solo killer → one inner call, also when the solo killer is Dead; distributor assigned to both `AutoAttackCombatSystem` and `SkillDamageExecutionStep` constructors (type check, no combat code change).
- **AC-2**: table N 1/2/3/4 with a recording inner sink (entity, amount) → 1000 / 900 / 800 / 700, calls in join order; ineligible killer → 0 for the killer, others split.
- **AC-3**: OutOfZone, Ghost, Dead, level-gap 6 each → excluded and N reduced; boundary level-gap 5 → included; all ineligible → 0 calls + `LogAssert.Expect` the zero-award line, then `NoUnexpectedReceived`.
- **AC-4**: L60 member in a party of 4 → 4 calls, 700 each.
- **AC-5**: kill, then `RemoveMember` in the same test tick → recorded awards unchanged (removed member got 900 in a party of 2); party set to Disbanding via `SetStateForTests` → awards normal.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_XpDistribution_tests.cs` — must exist and pass

**Status**: [x] Created — 26 cases, passing

---

## Dependencies

- Depends on: Stories 001, 003, 004
- Unlocks: Story 011

---

## Completion Notes

**Completed**: 2026-11-19
**Criteria**: 5/5 passing (all automated)
**Deviations (advisory, decided at readiness / review)**:
- G4: no per-member award log on each kill (hot path); AC-PS-24's "log records per-member XP awards" is verified with a recording sink instead. Only the zero-eligible line is logged.
- `PartyMemberStatus` has no Disconnected value; Ghost stands in for "Ghost / Disconnected" in the tests (CR-PS-9 treats Disconnected as Ghost, Story 003 G5).
- Code review C1: eligibility is read once per kill into a bitmask (and slot entities captured before the first award), so an award that levels the leader up cannot change recipients or the amount.
- A Dead leader does not disable the party: the leader is ineligible, the others are still gated from its level (pinned by test).
- Implementation note corrected: an OutOfZone member keeps its slot EntityID (not 0); excluded by eligibility.
**Test Evidence**: Integration — `tests/EditMode/PartySystem/PartySystem_XpDistribution_tests.cs` (26 cases); PartySystem filter 265/265; full EditMode `-testCategory "!Database"` 8359/8359 (13:33:31Z), 0 `error CS`.
**Code Review**: Complete (`/code-review` 2026-11-19, all required changes and suggestions applied); QL-TEST-COVERAGE and LP-CODE-REVIEW skipped — Lean mode.
