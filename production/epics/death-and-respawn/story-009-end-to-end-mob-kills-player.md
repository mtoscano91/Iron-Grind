# Story 009: End-to-End — A Mob Kills a Player, the Player Respawns

> **Epic**: Death & Respawn
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Estimate**: 3–4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/death-and-respawn.md` — full T-1 → T-3 path; `enemy-ai.md` (mob attack resolution)
**Requirement**: `TR-dr-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: The tick order from Story 008 is what this test runs.

**Engine**: Unity 6.3 LTS | **Risk**: LOW (real NavMesh in PlayMode; Navigation's HIGH risk is owned by that epic)
**Engine Notes**: PlayMode test assembly has `InternalsVisibleTo` on ServerLogic (ADR-012 Decision 5 amendment, Mob Spawning Story 008).

**Control Manifest Rules (this layer)**:
- Required: tests deterministic (seeded `IRandomProvider`, no wall-clock assertions)

---

## Acceptance Criteria

- [ ] **AC-1 Kill**: in a hand-built rig like `MobSpawning_EndToEnd_tests` (real nav on the Zone0 mesh, real Enemy AI, Character Stats, Status Effects, Auto-Attack Combat, the Death & Respawn service in the Story 008 tick order), a mob aggroes a player with low HP and its attacks bring HP to 0 → on that tick the player is Dead, combat-prohibited, `DeathStateEntered` and `EntityDied` are in the outbox.
- [ ] **AC-2 Mob lets go**: after the death the mob stops attacking the player (no further `ApplyDamage` against them) and leaves combat per its rules (re-scan / return).
- [ ] **AC-3 Respawn**: exactly `RespawnDelayTicks` after death the player is Alive at the town point (within 0.1), HP and MP full, combat allowed, `EntityRespawned` and `RespawnConfirmed` in the outbox.
- [ ] **AC-4 Back in play**: after respawn the player can auto-attack a mob again (combat state not Prohibited; a hit resolves).
- [ ] **AC-5 Clean run**: no errors logged across the run; the step's per-tick cost is logged (`[DeathAndRespawn009]`, max and p95).

---

## Implementation Notes

- Reuse the Mob Spawning Story 008 rig (stand-in damage calculator if the EditMode-only fakes are still unavailable in PlayMode; record any stand-in in Completion Notes).
- Town point: whatever Story 008 settled (authored Zone0 point or centroid fallback); the assertion uses the provider's value, not a literal.

---

## Out of Scope

- Party behaviour (Party System epic)
- Client presentation

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-17; refine at `/story-readiness`.*

- **AC-1/3**: Player HP 1, mob in range, seeded RNG → death tick D recorded; respawn on D + delay exactly; position, HP, MP asserted.
- **AC-2**: Count `ApplyDamage` calls against the player after D → 0 until respawn.
- **AC-4**: After respawn, set target on a mob, auto-attack on → at least one hit within the cycle length + 1 ticks.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/PlayMode/DeathAndRespawn/DeathAndRespawn_EndToEnd_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 006, 008
- Unlocks: None (last story of the epic)
