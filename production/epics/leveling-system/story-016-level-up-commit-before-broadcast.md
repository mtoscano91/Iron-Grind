# Story 016: Level-Up Behind the Irreversible-Outcome Coordinator

> **Epic**: Leveling System
> **Status**: Ready — written 2026-10-15 (follow-up handed off by Character Persistence Story 009; decisions taken at authoring, listed below)
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-09
> **Estimate**: 8 hours

## Context

**GDD**: `design/gdd/character-persistence.md` — CR-CP-4 (level-up trigger; consecutive level-ups in one tick give one write), CR-CP-5 (failure protocol: the caller reverts), AC-CP-13, AC-CP-14, AC-CP-17. `design/gdd/leveling-system.md` — CR-2 (the level-up sequence), CR-2.9 (consecutive level-ups), CR-2.10 (broadcast after the loop). `design/gdd/networking-core.md` — CR-NET-5 (commit before broadcast, no retry).
**Requirement**: `TR-cp-004`, `TR-cp-005`, `TR-cp-011`, `TR-lvl-005`

**ADR Governing Implementation**: ADR-011 Decision 5 (`IIrreversibleOutcomeCoordinator.Begin`: apply on tick N, deliver or revert on tick N + k) and Decision 1; ADR-010 (`OnLevelUp` stays a direct event, fired from the coordinator's deliver step).
**ADR Decision Summary**: a level-up is applied in memory, persisted with `SaveIrreversibleOutcome(LevelUp)` without awaiting, and broadcast only after the write succeeds; on any other result the caller reverts and the coordinator disconnects, alerts and preserves the session.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: tick code passes the task of a persistence call, in the same statement, to `ITickCompletionQueue.Track` (done inside the coordinator) — ADR-011
- Forbidden: `await`, `.Result`, `.Wait()`, `GetResult()`, `ContinueWith` in tick-driven code — ADR-011
- Forbidden: a central EventBus — ADR-010

**Performance**: no tick-time impact expected — the write runs off the tick thread; one snapshot of about 15 stat values and a few delegate allocations per level-up (rare event).

---

## Decisions (authoring, 2026-10-15)

1. **AC-CP-14 "pre-level-up Experience" = before the triggering XP gain.** The revert restores `Experience` to its value before the `AddExperience` call that crossed the threshold, so `Experience < threshold` holds after the revert. The kill's XP is lost (the client is disconnected anyway). `AddExperience` passes the pre-gain value to Leveling — the one Character Stats edit in this story.
2. **An XP gain while a level-up write is in flight**: the XP is added; `Begin` returns `RejectedWriteInFlight` and no level-up starts. The next XP gain after the gate opens re-triggers the level-up (`AddExperience` already checks `≥ threshold`). No new code path; the XP bar can sit past full until the next gain. If the in-flight write then fails, the revert of decision 1 also removes that XP.
3. **CurrentHP / CurrentMP on revert**: kept at their current values, clamped to the reverted `MaxHP` / `MaxMP`. Damage taken while the write was in flight is kept; only the excess above the old maxima goes.
4. **A level-up commit seam with an immediate default.** `LevelingService` takes an injected commit seam. The default commits immediately — today's behaviour, `OnLevelUp` inside the `AddExperience` call — and is what the existing tests (15 files) and the dev harness use, unchanged. The server composition injects the coordinator-backed implementation.
5. **Resolving the session.** `Begin` needs a `clientId` and a `CharacterID`; nothing maps an `EntityID` to them today. The coordinator-backed seam takes an injected resolver (`TryResolve(EntityID, out uint clientId, out CharacterID charId)`). If resolution fails, the level-up does not start: one error is logged, the XP is kept, and the next gain retries.

---

## Acceptance Criteria

*As in Character Persistence Story 009, the GDD's `ISavePersistence` / `IEventBus` / `ICriticalAlertService` mocks are restated against what exists: a controllable fake of `ICharacterPersistence` (or the in-memory store with a held operation), `OnLevelUp` subscribers, and a log assertion for the alert. These criteria use the real `LevelingService` and `CharacterStats`, with the coordinator-backed seam.*

- [ ] **AC-CP-13**: an `AddExperience` that crosses the threshold, with the write held open, applies the level-up in memory but fires no `OnLevelUp`; when the write is released as `Success`, `OnLevelUp` fires on the next drain, once per level gained and in ascending order.
- [ ] **AC-CP-14**: a level-up from Level 10 whose write returns `DatabaseError` fires no `OnLevelUp`. After the drain: Level 10; `Experience` equal to its value before the triggering `AddExperience`; Strength, Dexterity, Vitality and Intelligence, the held free points and the seven derived stats (MaxHP, MaxMP, AttackPower, Defense, MagicDefense, CritChance, AttackSpeedMultiplier) equal their pre-level-up values; CurrentHP and CurrentMP at most the reverted maxima. The client is disconnected with `DisconnectReason.Other`, one critical alert is logged, and the session goes to `preserveSessionForTtl`. Each happens exactly once.
- [ ] **The same revert** for a fault, a cancellation and a watchdog timeout of the write, and for `ConcurrencyConflict` and `CharacterNotFound`.
- [ ] **AC-CP-17**: one `AddExperience` that crosses two thresholds makes one `SaveIrreversibleOutcome(LevelUp)` call; the written record holds the final level; `OnLevelUp` fires twice after success.
- [ ] **59 → 60**: a failed write of the level-up to 60 also reverts the CR-2.2a Experience clamp (decision 1's value is restored).
- [ ] **In flight (decision 2)**: a second `AddExperience` crossing the next threshold while the write is in flight adds its XP and starts no second write; after the first write succeeds, the next `AddExperience` starts the next level-up.
- [ ] **HP / MP (decision 3)**: damage applied while the write is in flight is still applied after a revert, with CurrentHP clamped to the reverted MaxHP.
- [ ] **Unresolved session (decision 5)**: when the resolver fails, no level-up is applied, no write starts, one error is logged and the XP is kept.
- [ ] **Immediate default (decision 4)**: with no seam injected, `OnLevelUp` fires inside the `AddExperience` call as before; the existing Leveling, Character Stats and HUD tests pass unchanged.
- [ ] **After a failure**: the rolled-back state is what a later `SaveSession(SessionTTLExpiry)` writes (shown with the in-memory store).
- [ ] **No await**: a source search of the files this story adds or edits finds no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith`.

---

## Implementation Notes

- Call site: `CharacterStats.AddExperience` → `ILevelingService.NotifyExperienceCrossedThreshold` (`CharacterStats.cs:1010`). The pre-gain Experience is passed along (decision 1) — an added parameter or overload; the old signature's callers are updated in the same change.
- `NotifyExperienceCrossedThreshold` today runs the CR-2.9 loop under `_levelingUpInProgress`, then calls `FireOnLevelUp` per level. Behind the seam: `computeAndApplyOutcome` = capture the snapshot, then run the loop, returning the levels gained; `startWrite` / `isSuccess` = `IrreversibleOutcomePersistStep` with `IrreversibleOutcomeTrigger.LevelUp`; `deliverOutcome` = `FireOnLevelUp` per level; `revertOnFailure` = restore the snapshot (decisions 1 and 3). `validateRequest` returns true and `emitAcknowledgment` is empty — a level-up has no client request. When no level is gained (at the cap), no write starts.
- The snapshot lives with `LevelingService` (it owns `_heldFreePoints` and the stat list of CR-2.4 to CR-2.7); extending `LevelingStateSnapshot` is optional — it is the load snapshot and holds `HeldFreePoints` only.
- Revert writes go through `SetBaseStat` / `SetBaseStatFloat` / `SetCurrentHP` / `SetCurrentMP`, inside one stat transaction so subscribers see one batch.
- `IIrreversibleOutcomeCoordinator.Begin` returns `RejectedWriteInFlight` when the gate is held; the seam treats that as "no level-up now" and applies nothing (decision 2).
- The resolver's server implementation can be built on the persistence session entries (`CharacterID` → `EntityID`) and the connection state machine (`clientId`); a reverse index or a lookup by scan is a code-review choice.

---

## Out of Scope

- Character Persistence Story 009: the persist step (done)
- Enhancement Story 011 (the second coordinator caller)
- OQ-CP-1 (`DisconnectReason.PersistenceFailure`)
- Respec and free-point allocation during an in-flight level-up — inbound requests, held by the dispatcher's gate check
- A per-tick retry of a deferred level-up (decision 2 rejected it)

---

## QA Test Cases

- **Held then success** — L10, XP gain crosses → no `OnLevelUp`, Level 11 in memory; release `Success`, drain → `OnLevelUp(11)` once.
- **Failure** — L10, Experience 950 → gain 100 crosses → `DatabaseError` → Level 10, Experience 950, stats as captured, one disconnect, one alert, one preserve.
- **Double level-up** — gain crosses two thresholds → one write, record Level 12, `OnLevelUp(11)` then `OnLevelUp(12)`.
- **In flight** — second gain during the hold → XP added, write count 1; after success, a third gain → write count 2.
- **HP** — damage 30 during the hold, then failure → CurrentHP = min(current, reverted MaxHP).
- **Default seam** — no injection → `OnLevelUp` inside `AddExperience`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/LevelingSystem/LevelingSystem_LevelUpCommit_tests.cs` — must exist and pass; the full EditMode suite passes unchanged.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Leveling Stories 002, 003, 004 (Complete); Character Persistence Stories 008, 009, 010 (Complete)
- Unlocks: AC-CP-13 / 14 / 17 proven on the real level-up path (Character Persistence Story 009's handoff)
