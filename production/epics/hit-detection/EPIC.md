# Epic: Hit Detection

> **Layer**: Core
> **GDD**: design/gdd/hit-detection.md
> **Architecture Module**: Hit Detection — server-side hit validation, hit events (`architecture.md` module table)
> **Status**: In Progress
> **Stories**: 4 stories created 2026-10-30 (001–004); 3 Complete — see Stories below
> **Manifest Version**: 2026-10-26

## Overview

Hit Detection is the server-authoritative check that decides whether an attack connects before damage is calculated. A stateless `CheckHit(attackerPos, targetPos, attackRange, capturedForward)` returns `Hit`, `MissOutOfRange` or `MissFacing`. The range check uses XZ squared distance with an inclusive boundary, a point-blank epsilon guard skips the facing check for co-located entities, and the facing check compares `capturedForward` with the direction to the target against `ATTACK_FACING_TOLERANCE_DEG`. It lives in `IronGrind.ServerLogic` with no client equivalent. After every call the server sends a `HitCheckResult` to the player involved, which is the client's only hit/miss signal. Auto-Attack Combat and Enemy AI are the callers, and neither exists yet.

## Scope (user decision 2026-10-30, `/create-epics`)

**`CheckHit` and the message.** In this epic: `HitResult`, `CheckHit`, its constants, the `HitCheckResult` wire type and codec, and a seam that routes the message to the right player's client (attacker for player attacks, defender for monster attacks).

Moved to the caller epics, not yet created:

| Criterion | Owner |
|-----------|-------|
| AC-HD-12, AC-HD-13 (no damage on a miss) | Auto-Attack Combat |
| AC-HD-15 (`capturedForward` frozen at initiation) | Enemy AI |
| AC-HD-16 (600 ms wind-up) | Enemy AI |
| AC-HD-18, AC-HD-19 (facing updates and `AutoFaceEvent`, except anything already covered by Movement) | Auto-Attack Combat, Enemy AI |

AC-HD-20 (distinct `MissFacing` feedback) is a Polish item, outside MVP.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-012: Server/Client Assembly Boundary | `CheckHit` in `IronGrind.ServerLogic` (`UNITY_SERVER \|\| UNITY_EDITOR`); the wire type in `IronGrind.Foundation` | LOW for this module |
| ADR-004: Networking — NGO Substrate | `HitCheckResult` uses the project envelope on the R-U channel | LOW (consumer only) |
| ADR-014: Inbound Request Dispatch and Tick Order | `CheckHit` runs inside the callers' tick phases; `ServerTick` comes from the authoritative tick | LOW |
| ADR-010: Event and Messaging Architecture | Callers invoke `CheckHit` directly, no event | LOW |

## GDD Requirements

TR-IDs below are placeholders; `docs/architecture/tr-registry.yaml` has no Hit Detection entries. No requirement needs a new ADR.

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-hd-001 | `HitResult : byte` and a pure `CheckHit` with no `EntityState` or service access (CR-HD-1, AC-HD-17) | ADR-012 ✅ |
| TR-hd-002 | XZ range check, inclusive boundary, Y ignored (CR-HD-2, F-HD-1, AC-HD-1 to 4) | ADR-012 ✅ |
| TR-hd-003 | Point-blank guard before the facing check (CR-HD-3, AC-HD-9, AC-HD-10) | ADR-012 ✅ |
| TR-hd-004 | Facing check against `ATTACK_FACING_TOLERANCE_DEG` (CR-HD-4, F-HD-2, AC-HD-5 to 8, AC-HD-14) | ADR-012 ✅ |
| TR-hd-005 | `HitCheckResult` 13-byte payload, R-U low priority (CR-HD-10) | ADR-004 ✅ |
| TR-hd-006 | Message sent on every call to the player involved, `ServerTick` set to the current tick (AC-HD-11a, AC-HD-11b) | ADR-004, ADR-014 ✅ |

## Decisions (2026-10-30, `/create-stories`)

Applied to `hit-detection.md`, `enemy-ai.md`, `networking-wire-protocol.md`, `networking-channel-contract.md`, `networking-message-criticality.md` and `entities.yaml`.

1. **Facing convention:** Movement's is canonical (0° = +Z, clockwise), so no conversion from `eulerAngles.y`.
2. **`FacingAngle` storage:** `short` ×10, as Movement stores it.
3. **`ATTACK_FACING_TOLERANCE_DEG`:** default 60° → 45°. `MissFacing` is reachable up to R ≈ 2.1 m at `AttackRange` 3.0; beyond that a lateral dodge resolves as `MissOutOfRange` (EC-HD-3).
4. **`HitCheckResult`:** MessageTypeID `0x0306`, 17 bytes in batch, written after `SkillCooldownUpdate`, dropped after `LootBidUpdate`.
5. **R-U delivery:** `RUBatchWriter` has no production caller, so Story 003 builds the per-tick step that `DamageEvent` and `EntityHealthUpdate` will reuse.
6. **`CheckHit` signature:** CR-HD-1 is authoritative; `enemy-ai.md` updated. `MIN_WIND_UP_TICKS` is owned by Enemy AI.

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [CheckHit and constants](story-001-checkhit-and-constants.md) | Logic | Complete | ADR-012 |
| 002 | [HitCheckResult wire type and codec](story-002-hitcheckresult-wire-type-and-codec.md) | Logic | Complete | ADR-004 |
| 003 | [Per-tick R-U batch outbound step](story-003-per-tick-ru-batch-outbound-step.md) | Integration | Complete | ADR-014, ADR-004 |
| 004 | [Result emission and player routing](story-004-result-emission-and-player-routing.md) | Integration | Ready | ADR-010, ADR-014 |

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- AC-HD-1 to AC-HD-11b, AC-HD-14 and AC-HD-17 from `design/gdd/hit-detection.md` are verified
- The criteria listed under Scope are recorded in the Auto-Attack Combat and Enemy AI epics when those are created
- All Logic and Integration stories have passing test files in `tests/`

## Next Step

Run `/story-readiness production/epics/hit-detection/story-004-result-emission-and-player-routing.md`.
