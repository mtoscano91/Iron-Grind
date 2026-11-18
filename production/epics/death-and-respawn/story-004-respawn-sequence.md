# Story 004: Respawn Sequence — T-2 and T-3

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-8 to CR-DR-12, CR-DR-14, T-2, T-3, "Single-Tick Respawning State Semantics", EC-DR-4, EC-DR-9
**Requirement**: `TR-dr-004`, `TR-dr-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Each phase 3 system exposes a step whose position the composition root lists explicitly. This story exposes the countdown step's `Tick`; Story 008 places it.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#, `Vector3` only).

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await` in tick code (ADR-011)
- Forbidden: `LoadCharacter` on respawn (CR-DR-9)
- Guardrail: the per-tick scan is O(max players), no allocation

---

## Acceptance Criteria

- [ ] **AC-1 Exact tick (AC-DR-6)**: with death at tick T and delay 140, `Tick(currentTick)` starts the respawn on tick T + 140 exactly and not on T + 139. Guard: `State == Dead && currentTick >= RespawnTick`; a default (Alive, zero) state never fires.
- [ ] **AC-2 Town point (CR-DR-8, CR-DR-14)**: the position comes from `ITownRespawnPointProvider.GetTownRespawnPoint(zone)`. If the provider reports no authored point (readiness: settle how "no valid position" is detected given the provider's own centroid / zero fallback), retry once per tick up to `RespawnAnchorRetryTicks`, then use the fallback position and log a critical error naming the zone. During the retry window the entity stays Dead (not Alive).
- [ ] **AC-3 HP / MP (AC-DR-7, CR-DR-9)**: `SetBaseStat(CurrentHP, GetEffectiveStat(MaxHP))` and the same for MP; afterwards current equals max for both. `LoadCharacter` is never called.
- [ ] **AC-4 Position (AC-DR-8, CR-DR-10)**: the player's authoritative Movement position equals the town point within 0.1 units. Adds a narrow `TrySetAuthoritativePosition(EntityID, Vector3)` (teleport: clears any pending intent and stall counters) to `MovementService` / `IMovementSystem`, with its own unit test in the Movement tests.
- [ ] **AC-5 Bans lifted (AC-DR-9, CR-DR-11)**: `ICombatProhibitionSink.SetCombatProhibited(id, false)` and `IMovementSystem.SetProhibited(id, false)` are each called once (fakes record the calls; the seam is declared in Story 002, real wiring verified in Story 008).
- [ ] **AC-6 Status Effects (AC-DR-5, CR-DR-11a)**: `StatusEffects.OnEntityRespawned(id)` called once; with the real `StatusEffectsService`, a player with 2 buffs at death has 0 active effects one tick after death, and `ApplyEffect` succeeds after T-3.
- [ ] **AC-7 Party (CR-DR-12)**: party sink called once with `Online`.
- [ ] **AC-8 Single tick (T-2 → T-3)**: Respawning is entered and left in the same `Tick`; at the end of the tick `DeathState` is Alive with ticks zeroed, and the slot state is Alive. An `OnEntityDied` raised during the T-3 steps (e.g. by a stat write) is dropped (CR-DR-2). An `OnEntityDied` on a later tick is a fresh death (EC-DR-4).
- [ ] **AC-9 Order**: the T-3 side effects run in GDD order: HP/MP → position → combat → Status Effects → party → (Story 005 messages).
- [ ] **AC-10 No allocation**: `Tick` allocates 0 bytes after warm-up, including a tick with a respawn.

---

## Implementation Notes

- The town point lookup is synchronous and same-tick; no spatial query.
- The movement teleport is a small addition outside this epic's folder: keep it to Movement's existing slot arrays, no new state machine.
- Message enqueueing (`EntityRespawned`, `RespawnConfirmed`) is Story 005; leave the call site.

---

## Out of Scope

- Story 005: wire messages
- Story 008: placing `Tick` in the phase list

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1**: Death at 1000; Tick(1139) → still Dead; Tick(1140) → Alive. Repeat with T = 2^32.
- **AC-2**: Provider reports no point for 3 ticks then a point → respawn on the 4th tick at that point. Provider never reports a point, budget 40 → fallback position on tick 40 of the retry, one critical log.
- **AC-3**: MaxHP 250 / MaxMP 80, HP 0 / MP 13 at death → 250 / 80 after T-3; persistence fake records no load.
- **AC-4**: Town point (12, 0, −7) → Movement position within 0.1.
- **AC-6**: Real Status Effects with 2 buffs → 0 after one tick; after T-3 `ApplyEffect` returns success.
- **AC-8**: A stat-write hook raising `OnEntityDied` mid-T-3 → ignored; `OnEntityDied` on tick +1 → Dead again with a new RespawnTick.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Respawn_tests.cs` (+ the Movement teleport test in the Movement test folder) — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002
- Unlocks: Stories 005, 007
