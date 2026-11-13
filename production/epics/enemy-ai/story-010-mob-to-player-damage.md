# Story 010: Mob → Player Damage

> **Epic**: Enemy AI
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Interactions (Hit Detection), AttackExecution row; `design/gdd/hit-detection.md` CR-HD-1, CR-HD-10; `design/gdd/damage-calculation.md`
**Requirement**: `TR-ai-004`, `TR-ai-005`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012 (server-only damage); ADR-010 (direct calls on injected interfaces)
**ADR Decision Summary**: Damage resolution runs only in `IronGrind.ServerLogic`; Enemy AI reaches Hit Detection, Damage Calculation and Character Stats through injected interfaces.

**User decision (2026-11-13, `/create-stories`)**: a mob hit goes through Damage Calculation — `baseDamage` = the mob's `AttackPower` (no weapon), `DamageCalculator.Calculate(baseDamage, mob, player, context)` (player Defense, min damage, crit rules as the formula defines), then Character Stats `ApplyDamage` on the player. One damage formula for every attacker. The GDD's "writes damage to target's HP" gets a dated note pointing here.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: dependency injection over singletons
- Forbidden: awarding XP or raising a death event from Enemy AI for a player kill (player death belongs to Death & Respawn, `OnEntityDied`)

---

## Acceptance Criteria

- [ ] `IMobAttackResolver` (Story 005 seam) implemented over `HitDetector.CheckHit` and `DamageCalculator`: miss (`MissOutOfRange` / `MissFacing`) → no damage; hit → `Calculate` with the mob's current `AttackPower` stat → `ApplyDamage(player, finalDamage)`.
- [ ] Hit result delivery (CR-HD-10): `HitCheckResult` (and the damage output the client needs) goes to the **defending player's** client, using the existing hit-result emitter / damage output path (`HitResultEmitter`, `AutoAttackDamageOutputEmitter` pattern); other zone clients get the broadcast damage event if Combat does so for player hits. Readiness: name the exact messages and delivery classes (check MessageTypeId vs delivery class, R-OD range rule).
- [ ] Enraged AttackPower is used when Enraged (stat read, not definition).
- [ ] Player HP reaching 0 lets Character Stats raise its death event; Enemy AI does nothing further (the target becomes invalid next tick via Story 006).
- [ ] Damage context: which `DamageContext` (physical / melee) a mob uses is recorded; `DamageCalculator` must not reject a mob attacker (it has no weapon / equipment) — test with real stats.
- [ ] GDD `enemy-ai.md` Interactions → Hit Detection gets the dated note (decision above).

---

## Implementation Notes

- `DamageCalculator` is in `src/ServerLogic/DamageCalculation/`; the adapters `DamageCalculatorAutoAttackAdapter` / `DamageCalculatorSkillAdapter` show the call shape. Check whether the calculator reads equipment / item database for the attacker — mobs have none; a null-weapon path must exist or be added here.
- EC-AI-9 (Auto-Attack must check liveness of a player killed this tick) is Combat's responsibility — do not gate here.

---

## Out of Scope

- Death & Respawn epic: what happens to the dead player

---

## QA Test Cases

*Lean review: written at `/story-readiness`.*

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_MobDamage_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 009
- Unlocks: Story 013
