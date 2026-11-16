# Story 010: Mob → Player Damage

> **Epic**: Enemy AI
> **Status**: Complete
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/enemy-ai.md` — Interactions (Hit Detection), AttackExecution row; `design/gdd/hit-detection.md` CR-HD-1, CR-HD-10 (`HitCheckResult` to the defending player), CR-HD-11 (a miss applies no damage, sends no `DamageEvent`); `design/gdd/damage-calculation.md` (F-DC-1 physical mitigation, F-DC-4 floor clamp); `design/gdd/networking-core.md` (`DamageEvent` R-U broadcast)
**Requirement**: `TR-ai-004` (`capturedForward` passed unmodified to `CheckHit` on AttackExecution). No TR covers the damage path itself: it comes from the user decision below and the GDD Interactions section.
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-012 (server-only damage); ADR-010 (direct calls on injected interfaces)
**ADR Decision Summary**: Damage resolution runs only in `IronGrind.ServerLogic`; Enemy AI reaches Hit Detection, Damage Calculation and Character Stats through injected interfaces.

**User decision (2026-11-13, `/create-stories`)**: a mob hit goes through Damage Calculation — `baseDamage` = the mob's `AttackPower` (no weapon), Damage Calculation (player Defense, min damage, crit rules as the formula defines), then Character Stats `ApplyDamage` on the player. One damage formula for every attacker. The GDD's "writes damage to target's HP" gets a dated note pointing here.

**User decisions (2026-11-16, `/story-readiness`)**:
1. **Delivery: reuse both emitters.** `HitResultEmitter` sends `HitCheckResult` (0x0306, R-U) to the defending player (it routes to the target when the attacker holds no player slot). `AutoAttackDamageOutputEmitter` sends `DamageEvent` (0x0301, R-U) to every zone client, the defender included; no `SelfDamageEvent` (a mob has no client). No new message ids; no R-OD message.
2. **Damage context: `PhysicalAuto`.** A mob swing is a physical auto-attack; no enum or emitter change.
3. **Damage seam: `IAutoAttackDamageCalculator`** (Combat's seam, `Calculate(baseDamage, attacker, target)`). Tests use the real `DamageCalculator` through `DamageCalculatorAutoAttackAdapter`.
4. **Production calculator (Story 013):** a real `DamageCalculator` with a no-weapon `IEquippedWeaponQuery` (physical only) for mob attacks only; player autos keep their stand-in until Story 012 decides. TD entry at Story 013.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API beyond `Vector3` math.

**Control Manifest Rules (this layer)**:
- Required: dependency injection over singletons
- Forbidden: awarding XP or raising a death event from Enemy AI for a player kill (player death belongs to Death & Respawn, `OnEntityDied`)

---

## Acceptance Criteria

- [ ] **AC-1 Resolver.** `MobAttackResolver : IMobAttackResolver` (constructor: `HitDetector`, `IHitResultEmitter`, `IAutoAttackDamageCalculator`, `CharacterStats`, `IAutoAttackDamageOutput`; null → `ArgumentNullException`). `ResolveAttack(mob, target, mobPos, targetPos, attackRange, capturedForward)`:
  1. `CheckHit(mobPos, targetPos, attackRange, capturedForward)` with the captured forward unchanged; the result is emitted through `IHitResultEmitter.Emit(mob, target, result)` on **every** attack (mob attacks are discrete swings, so there is no miss-streak suppression).
  2. `MissOutOfRange` / `MissFacing` → no damage calculation, no `ApplyDamage`, no damage output.
  3. `Hit` → `Calculate(GetEffectiveStat(mob, StatID.AttackPower), mob, target)`; if `FinalDamage > 0`: `ApplyDamage(target, FinalDamage)`, then `IAutoAttackDamageOutput.Emit(mob, target, result)`. A rejected result (`FinalDamage` 0, e.g. AttackPower 0 or above `MaxBaseDamage`) applies nothing and sends nothing; the calculator logs its own error.
- [ ] **AC-2 Delivery.** With the real emitters over a `ZoneSlotPool` with 2 players (defender + bystander) and recording outbox / R-U collector: the defender's client gets one `HitCheckResult` (attacker = mob, target = defender) per attack, hit or miss; on a hit both players' clients get one `DamageEvent` (attacker = mob, `DamageType.Physical`, `FinalDamage`, `IsCrit`); no `SelfDamageEvent` is enqueued; a miss sends no `DamageEvent`.
- [ ] **AC-3 Enraged.** Damage uses the mob's `AttackPower` **stat** (which carries Story 007's Enraged base stat), not `MobDefinition.BaseAttackPower`: an Enraged mob deals more damage than a normal mob of the same type to the same player.
- [ ] **AC-4 Death.** A hit that takes the player's HP to 0 makes Character Stats raise `OnEntityDied` exactly once; the resolver ignores `IsKill` for players and does nothing further (no XP, no event). A second mob attacking the same player later in the same tick does not damage it (the controller skips `ResolveAttack` for a dead target, Story 006) — test with two controllers.
- [ ] **AC-5 Damage context.** Mob hits resolve with `DamageContext.PhysicalAuto` (through `DamageCalculatorAutoAttackAdapter`). Tested with a real `DamageCalculator` over real Character Stats and a no-weapon `IEquippedWeaponQuery` (returns `ItemID.Invalid`): `FinalDamage = max(1, floor(max(AttackPower × MinDamageFraction, AttackPower − Defense)))` with CritChance 0; the item database and enhancement provider are never called (fakes that throw).
- [ ] **AC-6 Zero allocation.** After one warm-up call, `ResolveAttack` (hit path with real calculator, applier and emitters) allocates 0 bytes.
- [ ] **AC-7 GDD note.** `design/gdd/enemy-ai.md` Interactions → Hit Detection gets a dated note (2026-11-16) pointing to this story: mob damage goes through Damage Calculation (PhysicalAuto, AttackPower stat, no weapon), then `ApplyDamage`, delivered by the existing emitters.

---

## Implementation Notes

- `MobAttackResolver` lives in `src/ServerLogic/EnemyAI/`. It reads `AttackPower` with `CharacterStats.GetEffectiveStat(mob, StatID.AttackPower)`.
- `DamageCalculator` reads the attacker's weapon through `IEquippedWeaponQuery`; `ItemID.Invalid` skips the item database and enhancement provider (physical only). Nothing implements the query in production yet — Story 013 adds the no-weapon query (decision 4).
- `CharacterStats.ApplyDamage` clamps to [0, MaxHP], fires `OnEntityDied` once when HP crosses 0, and is a no-op on a dead entity.
- `AutoAttackDamageOutputEmitter` sends the self message only to an attacker with a player slot and skips the attacker in the broadcast, so a mob attacker yields the broadcast to every zone player. It never throws.
- An exception from the resolver propagates to `EnemyAISystem.Run`'s per-controller catch (Story 009); the controller records the attack before calling the resolver (Story 005).
- EC-AI-9 (Auto-Attack must check liveness of a player killed this tick) is Combat's responsibility — do not gate here.

---

## Out of Scope

- Story 012: player → mob damage, Combat's stand-ins (`StandInAutoAttackDamageCalculator`, `StandInAutoAttackDamageApplier`)
- Story 013: building the production `DamageCalculator`, no-weapon query, resolver and emitters in the composition
- Elemental damage / weapon path for mobs
- Death & Respawn epic: what happens to the dead player

---

## QA Test Cases

*Lean review: written at `/story-readiness` 2026-11-16.*

1. **Hit applies damage** — real calculator, player Defense 3, mob AttackPower 10 → player HP drops by 7; one `HitCheckResult` Hit; one `DamageEvent` per zone player with `FinalDamage` 7.
2. **Min damage** — Defense above AttackPower → damage = `max(1, floor(AttackPower × MinDamageFraction))`.
3. **Miss out of range** — target beyond `attackRange` → `HitCheckResult` MissOutOfRange to the defender; HP unchanged; no `DamageEvent`; calculator not called (recording fake variant).
4. **Miss facing** — target behind `capturedForward` → MissFacing; same as 3.
5. **Captured forward unchanged** — a recording seam around `CheckHit` input (or a geometric case): the forward passed in is the one the resolver received.
6. **Every attack emits** — two consecutive misses → two `HitCheckResult` messages (no streak suppression).
7. **Rejected calculation** — AttackPower 0 → `HitCheckResult` Hit, no damage, no `DamageEvent`, the calculator's error log expected.
8. **Enraged stat** — same type, Enraged vs normal (seeded through the Story 007 path or `SetBaseStat`) → Enraged deals more.
9. **Death once** — player HP 5, damage 7 → HP 0, `OnEntityDied` once; a second `ResolveAttack` → no further event, HP stays 0.
10. **Two mobs same tick** — two controllers targeting one player at HP 1: the first kills, the second does not call the resolver (or deals no damage) that tick.
11. **No self message** — no `SelfDamageEvent` enqueued for any client; the defender gets the R-U `DamageEvent`.
12. **No weapon** — the item database and enhancement provider fakes throw if called; a hit still resolves.
13. **Null arguments** — each constructor argument null → `ArgumentNullException`.
14. **Zero allocation** — 0 bytes over 10 `ResolveAttack` hit calls after warm-up.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/EnemyAI/EnemyAI_MobDamage_tests.cs` — must exist and pass

**Status**: [x] Created — 24/24 passing (2026-11-16)

---

## Dependencies

- Depends on: Story 009 (Complete)
- Unlocks: Story 013

---

## Completion Notes
**Completed**: 2026-11-16
**Criteria**: 7/7 passing (all covered by automated tests)
**Deviations** (advisory):
1. Beyond spec (code review, user decision 2026-11-16): a hit on a target whose current HP is not above 0 sends only the `HitCheckResult` — no damage calculation, no `ApplyDamage`, no `DamageEvent`. Without it, a direct call on a dead defender sent a `DamageEvent` for damage never applied (the calculator returns damage > 0 with its dead-entity error). `MobController` already skips dead targets.
2. QA 12 uses counting fakes (`CountingItemDatabase`, `RecordingBonusProvider`) asserting zero calls instead of throwing fakes — equivalent proof.
3. QA 5 is geometric (`HitDetector` is sealed): +Z forward vs a target on +X → MissFacing; +X forward → Hit.
4. Mobs never crit: no crit stats are seeded for mobs, so CritChance reads 0 (`test_mobWithoutCritStats_neverCrits`). A design change if the GDD ever wants mob crits.
**Tech debt**: none new; the mobs-physical-only TD entry is logged at Story 013 (decision 4).
**Test Evidence**: Integration — `tests/EditMode/EnemyAI/EnemyAI_MobDamage_tests.cs` 24/24; batch EditMode 7751 / 7622 passed / 0 failed / 129 skipped (2026-11-16).
**Code Review**: Complete — APPROVED WITH SUGGESTIONS, all suggestions applied plus the dead-target guard (2026-11-16). Lean mode: QL-TEST-COVERAGE and LP-CODE-REVIEW skipped.
