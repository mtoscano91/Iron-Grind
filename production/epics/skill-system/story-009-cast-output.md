# Story 009: Cast Output (SkillCastResult, SkillCooldownUpdate)

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 4 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-9, CR-SK-12, CR-SK-23; `design/gdd/networking-wire-protocol.md` — `SkillCastResult`, `SkillCooldownUpdate`, R-U batch order and overflow; `design/gdd/networking-relevance-filter.md`
**Requirement**: `TR-sk-009`, `TR-sk-010`

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: the project owns the envelope, channels and batch framing. Both messages are R-U batch sub-messages; `RUBatchCategory.SkillCastResult` and `SkillCooldownUpdate` already exist with fixed write and drop order.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: wire schemas and codecs in `IronGrind.Foundation`
- Guardrail: no allocation per tick (pooled payload buffers, as the other R-U emitters do)

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] `SkillCastResult` codec: 28-byte body in the wire-protocol field order, `MessageTypeId = 0x0309`; round-trips; on rejection the damage, heal, crit, kill and cooldown fields are 0 or false.
- [ ] `SkillCooldownUpdate` codec: 8-byte body `{ uint skillID, uint cooldownExpiryTick }`, `MessageTypeId = 0x030A`; round-trips.
- [ ] **AC-SK-42**: the caster receives a `SkillCastResult` for every request that passes V-1 and V-2: 5 accepted and 5 rejected (V-3 to V-9) → exactly 10.
- [ ] **CR-SK-9 rejected**: a rejected result goes to the caster only.
- [ ] **CR-SK-9 accepted**: an accepted result goes to the caster and to every other zone client whose relevance set holds the caster or the target: party members of either, and clients whose current hostile target is the caster or the target. Each recipient gets it once, even if it matches more than one rule.
- [ ] **CR-SK-23 (1)**: an accepted cast also sends the caster `SkillCooldownUpdate` with the newly written `CooldownExpiryTick`, in the same R-U batch as its `SkillCastResult`.
- [ ] **CR-SK-23 (2) / CR-SK-12 amendment**: an `OnCooldown` rejection also sends the caster `SkillCooldownUpdate` with the current `CooldownExpiryTick`, in the same batch.
- [ ] No `SkillCooldownUpdate` is sent when a cooldown expires, or for any other rejection code.
- [ ] `CooldownTicks == 0`: the accepted cast still sends `SkillCooldownUpdate` (expiry = the cast tick), or none; pick one and document it (recommended: send it, so the client always gets a fresh value).
- [ ] `newCooldownTicks` in `SkillCastResult` equals `CooldownTicks` on accept and 0 on rejection.

---

## Implementation Notes

- **Seam**: implement the `ISkillCastOutput` declared in Story 005 (accepted and rejected entry points). Production class: `SkillCastOutputEmitter` in `src/ServerLogic/SkillSystem/`, following `AutoAttackDamageOutputEmitter` and `AutoFaceEmitter` (`src/ServerLogic/AutoAttackCombat/`): the zone pool for `EntityID` → client lookup, `IRUBatchCollector` / `RUBatchOutboundStep` for R-U, and `PendingSubMessage(RUBatchCategory.SkillCastResult, 0x0309, payload)`. `PendingSubMessage.cs` already shows this exact example with a placeholder id; update that doc comment to `0x0309`.
- **Relevance**: `RelevanceFilter` (`src/ServerLogic/Networking/WireProtocol/RelevanceFilter.cs`) is specific to health updates. Build the recipient set from the same sources: party membership (the party query `RelevanceFilter` uses) and `TargetSlotTracker` (each client's hostile target). Use a pre-allocated, de-duplicated recipient buffer. If no party query is available in the zone process, fall back to target-slot membership plus the caster, and note it.
- **Batch order and overflow** are already fixed in `RUBatchWriter` (`SkillCastResult` before `SkillCooldownUpdate` in write order, the reverse in drop order). Only the categories are set here.
- **Wire**: structs and codecs in `src/Foundation/Networking/WireProtocol/`. Record `0x0309` and `0x030A` in `networking-wire-protocol.md`. `SkillCastResult` fields: `int damageDealt`, `int healAmount`, `int newCooldownTicks`.
- **From Story 004 (code review 2026-11-02)**: AC-SK-43 was only shown at Story 004 as "a V-1 / V-2 drop stores nothing". Add the real test here: submit a mismatched-caster and a stale request through `SkillCastRequestHandler`, run the phase-3 step and this output, and assert zero `SkillCastResult` messages to any client.

---

## Out of Scope

- Story 010: `SkillCooldownSnapshot` on zone join
- Client rendering, VFX and audio from `SkillCastResult`: Combat UI, VFX, Audio

---

## QA Test Cases

- **Codecs**: round trip of every field; wrong lengths rejected; ids `0x0309`, `0x030A`; rejection layout has zeroed fields.
- **AC-SK-42**: 5 accepted + 5 rejected (one each of `InvalidSkill`, `SkillLocked`, `OnCooldown`, `InvalidTarget`, `InsufficientMana`) → the caster gets 10 results.
- **Rejected recipients**: three other clients, one of them targeting the caster → only the caster gets the rejection.
- **Accepted recipients**: client A in the caster's party, client B targeting the target, client C unrelated, client D both in the party and targeting → A, B, D (once each) and the caster; not C.
- **CR-SK-23 (1)**: accepted → one `SkillCooldownUpdate` to the caster, same batch, correct expiry.
- **CR-SK-23 (2)**: `OnCooldown` rejection → one update with the current expiry; `InsufficientMana` rejection → none.
- **No expiry message**: run ticks past an expiry → zero updates.
- **No allocation**: an accepted cast with three recipients inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_CastOutput_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006 (Story 005 declares the seam)
- Unlocks: Stories 010, 011
