# Story 005: Per-Tick Processing and Expiry

> **Epic**: Status Effects / Buffs
> **Status**: Ready — written 2026-10-28 (`/create-stories`, lean); `/story-readiness` 2026-10-28 NEEDS WORK (2 gaps) → fixed
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/status-effects.md` — CR-SE-6, CR-SE-7, CR-SE-8 steps 1 and 4, CR-SE-9, CR-SE-16, F-SE-5, EC-SE-1, EC-SE-7, EC-SE-8, EC-SE-10; AC-SE-02, 13, 14, 18, 19, 28
**Requirement**: `TR-se-004` (placeholder — registry empty)

**ADR Governing Implementation**: ADR-010 (expiry notifies through Character Stats' `OnStatChanged`); ADR-014 (a ticking core system runs in step 3 of `ZoneTickPipeline` — wired in Story 009)
**ADR Decision Summary**: no new event; subscribers see expiry through `OnStatChanged` fired by `RemoveBuffModifier`. The tick method is called by the pipeline, not self-scheduled.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: plain C#; the tick runs on the main thread.

**Control Manifest Rules (this layer)**:
- Required: game systems that tick run in phase 3 of `ZoneTickPipeline` step 3, in the order the composition root lists — ADR-014
- Guardrail: no `Dictionary`, LINQ or `new` in the per-entity per-tick path (CR-SE-15; measured in Story 009)

---

## Acceptance Criteria

- [ ] **`OnServerTick()` order per row (CR-SE-6, EC-SE-1)**: (1) decrement `TicksRemaining`; (2) `HpRegenPerTick > 0` → `IStatResourceSink.ApplyRegen(entity, rate)`; (3) `MpRegenPerTick > 0` → `ApplyManaRegen(entity, rate)`; (4) if `TicksRemaining == 0` → expiry: `RemoveBuffModifier(entity, stat, buffId)` once per populated modifier, then the row is freed and occupancy reduced (CR-SE-8 steps 1 and 4; steps 2–3 are Story 006).
- [ ] **AC-SE-13**: `DurationTicks 5` → after ticks 1–4 `TicksRemaining` 4, 3, 2, 1; on tick 5 expiry runs and the row is gone.
- [ ] **AC-SE-02**: a 3-modifier buff → after `DurationTicks` ticks exactly 3 `RemoveBuffModifier` calls (one per stat), row freed, effective values back to base for all 3.
- [ ] **AC-SE-14**: regen-only, `HpRegenPerTick 5`, `DurationTicks 3` → `ApplyRegen(entity, 5f)` exactly 3 times (including the expiry tick); zero adds.
- [ ] **AC-SE-18**: AttackPower flat −15 → effective = base − 15; after expiry = base. Same path as a buff (CR-SE-9).
- [ ] **AC-SE-28**: Buff 1 (AttackPower +10, 10 ticks) and Buff 2 (AttackPower +15, 20 ticks) → after 10 ticks: one `RemoveBuffModifier(…, BuffID 1)`, none for 2, effective = base + 15; after 10 more: effective = base.
- [ ] **AC-SE-19 — application during the tick (EC-SE-8)**: an `internal` test seam `InjectApplyEffectDuringTick(entity, def)` (the GDD's `[TestingOnly]` — no such attribute exists in `src/`, so `internal` + `InternalsVisibleTo` is the mechanism) makes `OnServerTick` call `ApplyEffect` from inside its row loop; at the end of that tick the new row's `TicksRemaining` = `DurationTicks`; after the next full tick = `DurationTicks − 1`. This holds whether the injected target is the entity being processed, an entity already processed this tick, or an entity whose slot comes later in the same tick.
- [ ] **Skip mechanism (EC-SE-8)**: at the start of `OnServerTick`, before any row is processed, every slot's `_rowCounts` value is copied into a preallocated `_tickStartRowCounts` array (allocated once in the constructor, `int[MAX_ENTITY_SLOTS]`). Each slot's loop processes only rows `0.._tickStartRowCounts[slot]-1`; `ApplyEffect` appends at `_rowCounts[slot]`, which is always ≥ that end, so a row created during the tick is never processed by it. When a row expires, the end for that slot is lowered by 1 together with `_rowCounts[slot]`.
- [ ] **Compaction (no holes)**: expiry closes the gap by shifting the later rows of that entity down one index (buff-bar order preserved), then `default`s the vacated last row and subtracts the expired row's `ModifierCount` from `_occupiedSlots`. After a removal the loop stays on the same index (the shifted-in row has not been processed yet). Rows `0..rowCount-1` are live at every point another method can observe them.
- [ ] **Dead entities**: slots with `_dead[slot] == true` are skipped entirely by Story 005's tick (no decrement, no regen, no removal); Story 007 adds the `PendingRemoval` cleanup that clears them.
- [ ] **EC-SE-10 / F-SE-5**: regen is called even at full HP — the service never reads CurrentHP/MaxHP before calling.
- [ ] Rows are processed per entity slot in index order; an empty slot range is skipped by the row count (no scan of 8 rows for an entity with 0 effects).

---

## Implementation Notes

- From Story 002 code review (2026-10-28): rows `0..rowCount-1` of an entity are always live (`ApplyEffect` writes at `rowCount`), so expiry must not leave holes. Decided at `/story-readiness` 2026-10-28: shift the later rows down (keeps the buff-bar order; at most 7 struct copies per expiry); swap-last was rejected because it reorders the buff bar. See the Compaction criterion.
- `IStatResourceSink` (or extend `IBuffModifierLayer`) wraps `CharacterStats.ApplyRegen` / `ApplyManaRegen`; Story 007's instant heal uses the same seam.
- Keep the row loop allocation-free: index arithmetic over the 1D array, no `foreach` over collections, no closures.
- `ActiveEffect` is a mutable struct (Story 001): change rows through the array element (`table[i].TicksRemaining--` or `ref var e = ref table[i]`), never through `foreach` or a `var e = table[i]` local — those decrement a copy (Story 001 code review 2026-10-28).
- The skip rule uses the whole-tick snapshot, not a per-row stamp: no `ActiveEffect` field change (Story 001's layout stays) and no tick counter. A snapshot taken per entity at the start of its own turn is not enough — it lets an effect injected into a later slot be processed in the same tick.
- Regen seam: pick `IStatResourceSink` vs extending `IBuffModifierLayer` in the `/dev-story` brief (advisory, not a readiness gap).

---

## Out of Scope

- MaxHP/MaxMP clamp and transaction batching on expiry (Story 006); death `PendingRemoval` processing (Story 007); zone-pipeline wiring and the allocation test (Story 009).

---

## QA Test Cases

- **AC-SE-13/02/14/18/28/19** — as listed, against the real `CharacterStats` with a recording decorator for call counts.
- **EC-SE-1** — `DurationTicks 1` with regen: one regen pulse, modifier added and removed by the end of tick 1.
- **Regen order** — HP regen before MP regen within one row, both before the expiry removes (recorded call order).
- **Edges** — two entities, one with 0 effects (no calls for it); an effect expiring and a new one injected on the same tick.
- **Cross-entity injection** — entities A (lower slot) and B (higher slot) each with one effect; during A's turn inject an effect into B → at the end of the tick B's new row has `TicksRemaining == DurationTicks` and B's existing row was decremented once. Repeat injecting into A during B's turn (already-processed slot) → same result for A's new row.
- **Compaction order** — three effects with durations 5, 2, 5 → after tick 2 the middle row is gone, `GetActiveEffects` returns the other two in their original order, both with `TicksRemaining == 3`, and `GetOccupiedSlots` drops by the expired row's modifier count.
- **Dead slot** — `MarkEntityDead` on an entity with a regen effect → the next tick makes no `ApplyRegen` call and does not decrement its row.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/StatusEffects/StatusEffects_Tick_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003
- Unlocks: Stories 004, 006, 007, 008, 009
