# Story 004: Respawn Sequence — T-2 and T-3

> **Epic**: Death & Respawn
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 5 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-8 to CR-DR-12, CR-DR-14, T-2, T-3, "Single-Tick Respawning State Semantics", EC-DR-4, EC-DR-9
**Requirement**: `TR-dr-004`, `TR-dr-009`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: Each phase 3 system exposes a step whose position the composition root lists explicitly. This story exposes the countdown step's `Tick(long currentTick)`; Story 008 places it.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#, `Vector3` only).

**Control Manifest Rules (this layer)**:
- Forbidden: `async`/`await` in tick code (ADR-011)
- Forbidden: `LoadCharacter` on respawn (CR-DR-9)
- Guardrail: the per-tick scan is O(max players), no allocation

---

## New Seams (decided at readiness 2026-11-18)

`DeathAndRespawnService`'s constructor gains four parameters after the existing ones (null → `ArgumentNullException`, like the others):

| Seam | Members | Production implementation |
|------|---------|---------------------------|
| `ITownRespawnPointProvider` (existing, Zone Instancing) | `GetTownRespawnPoint(ZoneID)` | `TownRespawnPointCache` / `ZoneDefinitionSlotRespawnPointProvider`; the zone is `ZoneSlotPool.Zone` |
| `IRespawnVitalsWriter` (new, DR folder) | `void RestoreToFull(EntityID)` | new `CharacterStatsRespawnVitalsWriter` (DR folder): `SetCurrentHP(id, GetEffectiveStat(id, MaxHP))`, `SetCurrentMP(id, GetEffectiveStat(id, MaxMP))` — pattern of `CharacterStatsMobStatWriter` |
| `IMovementTeleport` (new, Movement folder) | `bool TrySetAuthoritativePosition(EntityID, Vector3)` | `MovementService` |
| `IStatusEffectsRespawnSink` (new, DR folder) | `void OnEntityRespawned(EntityID)` | `StatusEffectsService` (implements list only, its method already exists — same pattern as `AutoAttackCombatSystem : ICombatProhibitionSink`) |

`IMovementSystem` is **not** changed (it is implemented by `AutoFaceEmitter` and test fakes).

---

## Acceptance Criteria

- [x] **AC-1 Exact tick (AC-DR-6)**: with death at tick T and delay 140, `Tick(currentTick)` starts the respawn on tick T + 140 exactly and not on T + 139. Guard: `State == Dead && currentTick >= RespawnTick`; a default (Alive, zero) state never fires. Works for T ≥ 2^32 (`long`).
- [x] **AC-2 Town point (CR-DR-8, CR-DR-14)**: the position comes from `ITownRespawnPointProvider.GetTownRespawnPoint(pool.Zone)`. `Vector3.zero` means "not resolved" (the provider's contract): the player stays Dead and the lookup is retried on each following tick, up to `RespawnAnchorRetryTicks` attempts in all (a per-slot counter, no allocation). When the budget is used up, the respawn proceeds at `Vector3.zero` (the end of the provider's own fallback chain; no separate per-zone fallback exists) and one `Debug.LogError` critical alert names the zone and the entity. Accepted edge case: a town point authored exactly at the origin goes through the retries and logs a false alert.
- [x] **AC-3 HP / MP (AC-DR-7, CR-DR-9)**: `IRespawnVitalsWriter.RestoreToFull(id)` is called once. The production writer, tested against a real `CharacterStats`, leaves CurrentHP == effective MaxHP and CurrentMP == effective MaxMP. `LoadCharacter` is never called (the service has no persistence dependency).
- [x] **AC-4 Position (AC-DR-8, CR-DR-10)**: `IMovementTeleport.TrySetAuthoritativePosition(id, point)` is called once with the resolved point. `MovementService` implements it by reusing what `OnPlayerEntering` does to place a player (reset slot state, take and drop any pending intent, set the position; facing and occupancy unchanged); unknown id → false. If it returns false, the service logs an error and continues the respawn. Movement unit test: the position reads back within 0.1 units, a queued intent is dropped, unknown id returns false.
- [x] **AC-5 Bans lifted (AC-DR-9, CR-DR-11)**: `ICombatProhibitionSink.SetCombatProhibited(id, false)` and `IMovementSystem.SetProhibited(id, false)` are each called once (fakes record the calls; real wiring verified in Story 008).
- [x] **AC-6 Status Effects (AC-DR-5, CR-DR-11a)**: `IStatusEffectsRespawnSink.OnEntityRespawned(id)` is called once. With the real `StatusEffectsService` as the sink, `ApplyEffect` on the player succeeds after T-3 (the "buffs cleared at death" half is Status Effects' own death subscription, already built).
- [x] **AC-7 Party (CR-DR-12)**: party sink called once with `Online`.
- [x] **AC-8 Single tick (T-2 → T-3)**: Respawning is entered and left in the same `Tick`; at the end of the tick `DeathState` is Alive with `DeathTick` / `RespawnTick` and the retry counter zeroed. The slot state is set to `ZoneSessionState.Connected` only when it is `Dead` or `Respawning`; a `Disconnected_SessionActive` slot is left unchanged (Story 007 AC-2: the timer runs while disconnected). An `OnEntityDied` raised during the T-3 steps for a connected player is dropped by duplicate suppression (CR-DR-2); an `OnEntityDied` on a later tick is a fresh death (EC-DR-4).
- [x] **AC-9 Order**: the T-3 side effects run in GDD order: HP/MP → position → combat → movement ban lifted → Status Effects → party → (Story 005 messages).
- [x] **AC-10 No allocation**: `Tick` allocates 0 bytes after warm-up, including a tick with a respawn (measured with `GC.GetAllocatedBytesForCurrentThread`, as in `EnemyAI_DeathBridge_tests.cs`; the provider fake returns a non-zero point, since the zero path logs).

---

## Implementation Notes

- The town point lookup is synchronous and same-tick; no spatial query.
- Retry counting: attempt 1 is the respawn tick itself; with budget N, the fallback is used on attempt N (tick `RespawnTick + N − 1`). Store the counter beside `DeathState` (a field on it or a parallel array), reset at T-3.
- The movement teleport is a small addition outside this epic's folder: keep it to Movement's existing slot arrays, no new state machine.
- The test-only `SetStateForTests` hook: replace its use in the Story 002 Respawning-duplicate test with the real T-2 path if practical; otherwise keep it and leave its doc as is.
- Message enqueueing (`EntityRespawned`, `RespawnConfirmed`) is Story 005; leave the call site after the party step.
- GDD deviation (advisory): CR-DR-9 says `SetBaseStat(CurrentHP, …)`; current HP / MP are written through `SetCurrentHP` / `SetCurrentMP` in this codebase. Same outcome.
- A player who disconnected while dead (slot `Disconnected_SessionActive`) still respawns here; any `OnEntityDied` for them during T-3 would hit the Story 003 ghost check first. That case is Story 007's.

---

## Out of Scope

- Story 005: wire messages
- Story 007: disconnect / reconnect while dead, teardown, slot release reset
- Story 008: placing `Tick` in the phase list and building the production adapters into the composition

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-18.*

- **AC-1**: Death at 1000; Tick(1139) → still Dead; Tick(1140) → Alive. Repeat with T = 2^32. Tick on an Alive player → no calls.
- **AC-2**: Provider returns zero for 3 ticks then (12, 0, −7) → respawn on the 4th attempt at that point, no critical log. Provider always zero, budget 40 → still Dead after attempt 39; on attempt 40 respawn at zero with one critical log naming zone and entity.
- **AC-3**: Vitals fake called once with P. Writer test with real `CharacterStats`: MaxHP 250 / MaxMP 80, HP 0 / MP 13 → 250 / 80.
- **AC-4**: Teleport fake receives (P, (12, 0, −7)). Movement test: position within 0.1, pending intent dropped, unknown id → false. Teleport returns false → error logged, rest of T-3 still runs.
- **AC-5 / AC-7**: fakes record `SetCombatProhibited(P, false)`, `SetProhibited(P, false)`, `SetMemberStatus(P, Online)` once each.
- **AC-6**: sink fake called once; real `StatusEffectsService`: P dies, respawns, `ApplyEffect` succeeds.
- **AC-8**: slot Dead → Connected after T-3; slot `Disconnected_SessionActive` → unchanged. Vitals fake raises `OnEntityDied(P)` inside `RestoreToFull` → ignored (state ends Alive). `OnEntityDied` on tick +1 → Dead again with a new RespawnTick.
- **AC-9**: one shared call log across all fakes records the order.
- **AC-10**: warm-up death + respawn, then a second death + respawn measured → 0 bytes.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Respawn_tests.cs` (+ the Movement teleport test in the Movement test folder) — must exist and pass

**Status**: [x] Created — Respawn 26 tests + Movement teleport 5 tests, pass (2026-11-18)

---

## Dependencies

- Depends on: Stories 001, 002 (Complete), 003 (Complete — constructor shape)
- Unlocks: Stories 005, 007, 010

---

## Completion Notes
**Completed**: 2026-11-18
**Criteria**: 10/10 passing (all automated)
**Deviations**: Advisory — CR-DR-9 names `SetBaseStat`; HP / MP are written with `SetCurrentHP` / `SetCurrentMP` through `IRespawnVitalsWriter` (decided at readiness, same outcome). Each T-3 step is guarded by its own try/catch (log entity + step, continue, always end Alive), added at code review; not specified by the GDD, consistent with Story 003. Retry counter is a parallel `int[]`; the pool slot is not written Respawning. Story 007 notes: reset `_retryAttempts[slot]` on slot release.
**Test Evidence**: Logic — `tests/EditMode/DeathAndRespawn/DeathAndRespawn_Respawn_tests.cs` 26, `tests/EditMode/MovementSystem/MovementSystem_Teleport_tests.cs` 5; filter `DeathAndRespawn|MovementSystem` 419/419; full EditMode `-testCategory "!Database"` 7938/7938 on one run, 7937/7938 on two runs (the failure is the known TD-110 flaky Skill System composition test, unrelated)
**Code Review**: Complete (`/code-review` with unity-specialist + qa-tester; all required changes and suggestions applied)
