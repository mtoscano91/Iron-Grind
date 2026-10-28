# Story 006: Expiry Clamp and Batched Stat Events

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28 NEEDS WORK (4 gaps) → fixed
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 6 hours (was 4; +1 Character Stats modifier-event deferral, +1 replacement bracket and TD-085 closure, `/story-readiness` 2026-10-28)

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-8 steps 2–3, CR-SE-17, EC-SE-4, EC-SE-5, OQ-SE-1; AC-SE-15, 29. `design/gdd/character-stats.md` — Transaction constraints (Status Effects is a permitted caller of `BeginStatTransaction` / `EndStatTransaction`)
**Requirement**: `TR-se-004` (active)

**ADR Governing Implementation**: ADR-010 (Event/Messaging) — `OnStatChanged` is deferred and deduplicated inside a stat transaction
**ADR Decision Summary**: `OnStatChanged` fires once per unique `(EntityID, StatID)` at `EndStatTransaction()`; Status Effects brackets its tick pass with the transaction.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#.

**Control Manifest Rules (this layer)**:
- Required: Character Stats event handlers must not write stats (`IsFiringAndAssert`) — ADR-010, Character Stats Rule 8

---

## Acceptance Criteria

- [ ] **OQ-SE-1 resolved (HP)**: `CharacterStats.ApplyDamage(entity, 0f)` clamps `CurrentHP` to the effective `MaxHP` and fires no event unless HP reaches 0 (verified in code 2026-10-28: `ApplyDamage` returns early at HP 0 and fires only `OnEntityDied` on reaching 0). A Character Stats test pins this: HP 580, MaxHP lowered to 400 by removing a buff modifier, `ApplyDamage(0f)` → HP 400, no `OnEntityDied`, no `OnStatChanged` for `CurrentHP`.
- [ ] **OQ-SE-1 resolved (MP) — new Character Stats API**: `CharacterStats.ClampCurrentMPToMax(EntityID)` sets `CurrentMP = min(CurrentMP, effective MaxMP)`; no event when unchanged; documented as the Status Effects expiry clamp. (`ApplyManaRegen(0f)` returns early and cannot be used.) Also `ClampCurrentHPToMax(EntityID)` is **not** added — `ApplyDamage(0f)` is used as the GDD specifies. Record the resolution in the GDD (OQ-SE-1 → Resolved) and in `character-stats.md` Interactions.
- [ ] **AC-SE-15**: +200 MaxHP buff, `CurrentHP` = MaxHP (600) → at expiry (1) `RemoveBuffModifier(entity, MaxHP, buffId)`; (2) `ApplyDamage(entity, 0f)` immediately after (recorded order); (3) `CurrentHP` = 400.
- [ ] **EC-SE-4 no-op**: `CurrentHP` 300 ≤ new MaxHP 400 → `ApplyDamage(0f)` still called; HP stays 300; no event.
- [ ] **EC-SE-5**: +100 MaxMP buff, `CurrentMP` = MaxMP → at expiry `RemoveBuffModifier(MaxMP)` then `ClampCurrentMPToMax`; `CurrentMP` = reduced MaxMP.
- [ ] **Character Stats — modifier events deferred in a transaction (CR-SE-17 prerequisite)**: today `AddBuffModifier` (both the new-entry and the same-BuffID overwrite branch) and `RemoveBuffModifier` call `FireOnStatChanged` directly, so a transaction batches nothing. When `_transactionOpen`, they call `AddToDeferredDedup(entityId, statId)` instead, as `SetBaseStat` does. `RollbackStatTransaction` discards those deferred events but does **not** revert buff modifiers (no snapshot) — documented on both methods and on `RollbackStatTransaction`. Character Stats tests: inside a transaction an add and a remove on the same stat fire nothing until `EndStatTransaction`, which fires once; outside a transaction behaviour is unchanged (one event per call).
- [ ] **CR-SE-17 — transaction bracket**: every `OnServerTick()` call is wrapped in `BeginStatTransaction()` / `EndStatTransaction()`, always (no "skip when nothing changed" branch). `EndStatTransaction()` runs in a `finally`, so the deferred events for changes that really happened fire even when an exception escapes the pass — Status Effects never calls `RollbackStatTransaction()` (its modifier writes are not reverted, so discarding their events would hide real changes). If `BeginStatTransaction()` throws (a transaction already open — non-nestable), the exception propagates and no row is processed. Record in `character-stats.md` Transaction constraints: Status Effects closes with `EndStatTransaction()` in a `finally`, not `RollbackStatTransaction()`.
- [ ] **TD-085 closed (tick path)**: with the bracket, an `OnStatChanged` subscriber can only throw at `EndStatTransaction()`, after every row of the tick is processed, freed and compacted. Test: a subscriber that throws on the first event of the expiry tick → `OnServerTick` throws; every row was processed exactly once (regen counts), the expired rows are gone, occupancy reduced, no transaction left open; the next tick runs normally and no row's regen fires twice.
- [ ] **Replacement clamp (CR-SE-4 exception)**: a replacement (Story 004 `ReplaceInPlace`) clamps once **after** the new adds, against the final effective MaxHP / MaxMP — only if MaxHP / MaxMP was among the old or the new modifiers. Upgrading a +200 MaxHP buff to +300 at full HP (600/600) leaves HP 600 of 700, not 400. On the add-failure path (old effect lost) the clamp runs after the rollback. `RemoveModifiers` itself does not clamp; expiry calls the clamp right after `RemoveModifiers` (AC-SE-15 order). Record in `status-effects.md` CR-SE-4.
- [ ] **Replacement bracketed outside the tick (TD-085 replacement path)**: a replacement made outside `OnServerTick` (no Status Effects transaction open) is wrapped in its own `BeginStatTransaction()` / `EndStatTransaction()` (`finally`); inside the tick it uses the tick's bracket (no nested Begin). Subscribers see one `OnStatChanged` per affected stat and never the transient lower value between the remove and the add; a subscriber throw surfaces at `EndStatTransaction()` after the row, occupancy and clamp are consistent. Test: AttackPower +10 → +20 replacement outside a tick → exactly one AttackPower event, and the handler reads base + 20; a throwing subscriber → `ApplyEffect` throws, row holds the new values, occupancy correct, no transaction left open.
- [ ] **AC-SE-29**: a 3-modifier buff (AttackPower, MaxHP, Defense) expires → a counter on `OnStatChanged` sees exactly 3 events on the expiry tick (one per stat). Two effects sharing AttackPower expire on the same tick → AttackPower fires exactly once that tick.
- [ ] New-buff applications and plain refreshes from outside the tick (Skill System calls between ticks) are **not** bracketed by Status Effects — `OnStatChanged` fires per add as today; documented. Only replacements are bracketed (criterion above).

---

## Implementation Notes

- The clamp goes through the seam (`IStatResourceSink` gains `ClampHPToMax` → `ApplyDamage(id, 0f)` and `ClampMPToMax` → `ClampCurrentMPToMax(id)`), so the expiry tests can record order.
- Read `CharacterStats.BeginStatTransaction` / `EndStatTransaction` / `RollbackStatTransaction` doc comments for nesting rules before bracketing; Leveling also uses them (never in the same tick pass).
- `character-stats.md` uses `DefencePower` in AC-SE-29's text; the code's `StatID` is `Defense` — use the code name.
- Bracket seam: `IBuffModifierLayer` gains `BeginStatBatch()` / `EndStatBatch()` → `CharacterStats.BeginStatTransaction()` / `EndStatTransaction()` in `CharacterStatsBuffModifierLayer`, so the recording fake logs Begin/End in order with the adds, removes and clamps. Status Effects tracks its own open bracket with a private bool (the tick sets it; a replacement opens one only when it is false).
- The tick bracket also covers applies injected during the tick (EC-SE-8): their adds are deferred to the tick's `EndStatTransaction()`.
- Story 003's `CharacterStatsBuffModifierLayer` detects an add rejection by comparing `EntityBuffCount`; deferral does not change the count, so it keeps working — keep its tests green.
- Edge (no new rule): a −MaxHP debuff can leave effective MaxHP ≤ 0 after a +MaxHP buff expires; `ApplyDamage(0f)` then sets HP 0 and fires `OnEntityDied` during the tick (it is not deferred). Note it in the completion notes if a test shows it; Story 007 owns death cleanup.
- TD-085 (`docs/tech-debt-register.md`) is closed by this story: mark it resolved at `/story-done` if both TD-085 criteria pass.

---

## Out of Scope

- Death cleanup (Story 007) and zone departure (Story 008) reuse this expiry procedure; their own tests cover those paths.

---

## QA Test Cases

- **AC-SE-15 / EC-SE-4 / EC-SE-5** — as listed, real `CharacterStats`, recorded seam order.
- **AC-SE-29** — event counter subscribed to `OnStatChanged`; 3 events for one 3-stat expiry; 1 event for a stat shared by two expiring effects.
- **Bracket on exception** — a fake layer that throws inside the pass → `EndStatTransaction` still runs (no transaction left open; next tick works).
- **ClampCurrentMPToMax** — unit tests in the Character Stats suite: above max → clamped; at or below → unchanged, no event.
- **Deferral** — Character Stats suite: add + remove of buff modifiers inside a transaction → 0 events until End, then 1 per (entity, stat); same-BuffID overwrite inside a transaction deferred; Rollback discards the events and leaves the modifiers in place; outside a transaction one event per call (unchanged).
- **Bracket order** — recording fake: `BeginStatBatch` first and `EndStatBatch` last in every tick, including a tick with no rows and a tick whose pass throws.
- **TD-085** — throwing subscriber on the expiry tick (rows processed once, state consistent, next tick normal, no double regen); throwing subscriber on a replacement outside the tick.
- **Replacement clamp** — +200 → +300 MaxHP at full HP → 600/700; +300 → +100 MaxHP at 700/700 → 500/500; MaxMP equivalent; add-failure path clamps after the rollback; replacement inside the tick uses the tick's bracket (one Begin/End pair in the fake log).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_ExpiryClamp_tests.cs` — must exist and pass; Character Stats tests for `ClampCurrentMPToMax` and the `ApplyDamage(0f)` clamp in the existing Character Stats test folder

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 005
- Unlocks: Stories 007, 008
