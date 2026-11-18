# Story 010: Ghost Death Session End

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — CR-DR-Ghost, T-4; `networking-ghost-session.md` CR-GH-6, CR-GH-7; `networking-ghost-character-state.md` CGS-5, GD-CGS-2
**Requirement**: `TR-dr-003` (production half: what happens after the handler hands off)
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-011: Async Persistence and the Tick Loop (also ADR-010)
**ADR Decision Summary**: A session end is started from the tick, its save is tracked behind the mutation gate, and its completion (release, state transition) runs on the main thread when the save finishes. Nothing is awaited in the tick.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none (plain C#).

**Control Manifest Rules (this layer)**:
- Required: persistence writes go through the existing `SessionEndCoordinator` mutation gate (ADR-011)
- Forbidden: `async`/`await` in tick code; no second save path beside the coordinator's

---

## Acceptance Criteria

- [ ] **AC-1 Begin**: `SessionEndCoordinator.BeginGhostDeath(accountId)` starts a ghost-death end for an account that is `Disconnected_SessionActive`; returns false (and starts nothing) when an end is already in flight or the account is in any other state, logging one warning.
- [ ] **AC-2 Save (CGS-5, GD-CGS-2)**: the saved record holds the pre-disconnect snapshot HP (no HP penalty), the zone's town respawn position in place of the disconnect position, and `wasKilledWhileDisconnected = true`.
- [ ] **AC-3 Order (CR-GH-6)**: save confirmed → existing release chain (session, inventory, equipment, entity unregister — which frees the zone slot) → session `Disconnected_SessionActive → Disconnected_SessionExpired` with trigger "GhostDeath" → `GhostExpiredEvent(GhostDeath)` to zone clients → mob de-targeting for every mob targeting the ghost (CR-GH-7).
- [ ] **AC-4 Save failure**: a failed save logs the error, does not release or transition, and leaves the account `Disconnected_SessionActive` so the normal TTL end can still run (same policy as the existing TTL path).
- [ ] **AC-5 Reconnect race**: if the account is `Reconnecting` when the ghost dies, the existing `HandleGhostDeathWhileReconnecting` behaviour applies (`ZoneSessionEnded(GhostDeath)` to the reconnecting client); if a reconnect completes while the save is in flight, completion finds the state changed and logs instead of releasing (existing coordinator rule).
- [ ] **AC-6 Reconnect after ghost death**: a later reconnect for that account gets `ZoneSessionEnded(DisconnectReason.GhostDeath = 3)` and enters the respawn flow, not the reconnect flow (CR-GH-6 step 6).
- [ ] **AC-7 Sink adapter**: the production `IGhostDeathSink` maps the EntityID to the account through the zone slot and calls `BeginGhostDeath`; an EntityID with no player slot logs a warning and does nothing.

---

## Implementation Notes

- Readiness to confirm: where the town respawn position comes from (`ITownRespawnPointProvider`, Story 004) and how the save record carries `wasKilledWhileDisconnected` today (column or field may be missing).
- Readiness to confirm: how the existing `MobDeTargetingCoordinator` and `GhostExpiredEvent` broadcast are reached from the coordinator in production, or whether they need a seam.
- Mirror `EvaluateSessionTTL` / `BeginEviction` for in-flight tracking and completion; reuse `Release(...)`.

---

## Out of Scope

- Story 003: the handler branch and the seams
- Story 008: composing the sink into `ZoneServerComposition`
- Party XP forfeit for the ghost period (CR-GH-9.1) — Party System epic

---

## QA Test Cases

*Lean mode: written at `/story-readiness` of Story 003, 2026-11-18; refine at this story's readiness.*

- **AC-1**: Disconnected_SessionActive → true and one save started; Connected / end in flight → false, warning logged, no save.
- **AC-2**: save fake records snapshot HP, respawn position, `wasKilledWhileDisconnected == true`.
- **AC-3**: recorded call order across save, release, transition, broadcast, de-target fakes.
- **AC-4**: save fails → no release, state unchanged, error logged.
- **AC-5 / AC-6**: reconnect race and reconnect-after cases against the real state machine.
- **AC-7**: unknown EntityID → warning, no begin.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/DeathAndRespawn/DeathAndRespawn_GhostSessionEnd_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (seam), Story 004 (town respawn point provider)
- Unlocks: Story 008
