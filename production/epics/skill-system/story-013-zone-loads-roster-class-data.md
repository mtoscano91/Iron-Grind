# Story 013: Zone Loads the Roster and Class Data

> **Epic**: Skill System
> **Status**: Draft — written 2026-11-04 at Story 012 `/story-readiness` (user decision: assets in 012, zone wiring here); run `/story-readiness` before implementing
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours (provisional)

## Context

**GDD**: `design/gdd/skill-system.md` — Skill Roster, CR-SK-12 (snapshot of the caster's 10 skills); `design/gdd/class-system.md` — LC-1 (Leveling reads auto-allocation from `IClassRegistry`), CD-10 (startup validation)
**Requirement**: TBD at readiness (TR-sk-011 covers the data; check `docs/architecture/tr-registry.yaml` for a composition requirement)

**ADR Governing Implementation**: ADR-014 (composition root), ADR-012 (Server/Client Assembly Boundary)
**ADR Decision Summary**: TBD at readiness.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM — how a server build gets an asset reference (serialized field on the bootstrap, `Resources`, or Addressables) must be checked against Unity 6.3 and the dedicated-server build.
**Engine Notes**: TBD at readiness.

---

## Problem

Story 012 authors `Assets/Data/Skills/SkillDatabase.asset` and `Assets/Data/Classes/ClassDatabase.asset`, but `ZoneServerComposition` still builds an empty `ClassRegistry` (`BuildStatusEffects`, ~line 1631) and an empty in-code `SkillDatabase` (`BuildSkillSystem`, ~line 1756) when no test override is set. In a real zone, `GetClassSkills` is empty and Leveling has no auto-allocation, so players spawn with no skills and level-ups allocate nothing.

---

## Acceptance Criteria (draft)

- [ ] The zone composition receives the `SkillDatabase` and `ClassDatabase` assets from a production source (decided at readiness) and uses them instead of the empty fallbacks; test overrides still win.
- [ ] The `ClassRegistry` shared by Leveling and the Skill System holds Warrior and Healer from `ClassDatabase`; `TryGetClass` and `GetClassSkills` return them.
- [ ] A missing or invalid asset at zone startup is reported (log or startup failure, decided at readiness), not silently replaced by an empty roster.
- [ ] With Character Persistence Story 018 (TD-090) done, a player admitted through the composed zone gets a `SkillCooldownSnapshot` with their class's 10 skill ids. Until then, shown through the composition test path that registers the class type directly.

---

## Open Questions (settle at readiness)

- Asset source for the server: a serialized reference on the zone bootstrap MonoBehaviour, `Resources.Load`, or Addressables?
- Does the existing "composition tests fail on any unexpected log" rule need the fallbacks kept for tests that pass no assets?
- Order with Character Persistence Story 018: independent, but the end-to-end criterion needs both.

---

## Out of Scope

- Authoring the data (Story 012)
- The Persistence applier (Character Persistence Story 018, TD-090)

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/SkillSystem/SkillSystem_ZoneRosterData_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 012; Story 011 (composition)
- Related: Character Persistence Story 018 (end-to-end criterion)
- Unlocks: None within the epic
