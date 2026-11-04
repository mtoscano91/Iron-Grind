# Story 009: Cast Output (SkillCastResult, SkillCooldownUpdate)

> **Epic**: Skill System
> **Status**: Complete
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

- [x] `SkillCastResult` codec: 28-byte body in the wire-protocol field order, `MessageTypeId = 0x0309`; round-trips; on rejection the damage, heal, crit, kill and cooldown fields are 0 or false.
- [x] `SkillCooldownUpdate` codec: 8-byte body `{ uint skillID, uint cooldownExpiryTick }`, `MessageTypeId = 0x030A`; round-trips.
- [x] **AC-SK-42**: the caster receives a `SkillCastResult` for every request that passes V-1 and V-2: 5 accepted and 5 rejected (V-3 to V-9) → exactly 10.
- [x] **CR-SK-9 rejected**: a rejected result goes to the caster only.
- [x] **CR-SK-9 accepted**: an accepted result goes to the caster and to every other zone client whose relevance set holds the caster or the target: party members of either, and clients whose current hostile target is the caster or the target. Each recipient gets it once, even if it matches more than one rule. Party membership comes from an injected `ISkillCastPartySource` (readiness 2026-11-04); tests use a fake with real party data. In production it adapts `IPartyZonePresence`, whose stand-in `NoPartyZonePresence` reports no parties, so in a real zone today only the caster and the target-slot rule match until the Party System lands.
- [x] **CR-SK-23 (1)**: an accepted cast also sends the caster `SkillCooldownUpdate` with the newly written `CooldownExpiryTick`, in the same R-U batch as its `SkillCastResult`.
- [x] **CR-SK-23 (2) / CR-SK-12 amendment**: an `OnCooldown` rejection also sends the caster `SkillCooldownUpdate` with the current `CooldownExpiryTick`, in the same batch.
- [x] No `SkillCooldownUpdate` is sent when a cooldown expires, or for any other rejection code.
- [x] `CooldownTicks == 0` (readiness 2026-11-04: send): the accepted cast still sends the caster one `SkillCooldownUpdate` with `cooldownExpiryTick` equal to the cast tick, so the client always gets a fresh value.
- [x] `newCooldownTicks` in `SkillCastResult` equals `CooldownTicks` on accept and 0 on rejection.

---

## Implementation Notes

- **Seam**: implement the `ISkillCastOutput` declared in Story 005 (accepted and rejected entry points). Production class: `SkillCastOutputEmitter` in `src/ServerLogic/SkillSystem/`, following `AutoAttackDamageOutputEmitter` and `AutoFaceEmitter` (`src/ServerLogic/AutoAttackCombat/`): the zone pool for `EntityID` → client lookup, `IRUBatchCollector` / `RUBatchOutboundStep` for R-U, and `PendingSubMessage(RUBatchCategory.SkillCastResult, 0x0309, payload)`. `PendingSubMessage.cs` already shows this exact example with a placeholder id; update that doc comment to `0x0309`.
- **Relevance**: `RelevanceFilter` (`src/ServerLogic/Networking/WireProtocol/RelevanceFilter.cs`) is specific to health updates. Build the recipient set from party membership and `TargetSlotTracker` (each client's hostile target). Use a pre-allocated, de-duplicated recipient buffer.
- **Party seam (readiness 2026-11-04)**: no real Party System exists (`RelevanceFilter` takes a mock party list). Declare `ISkillCastPartySource` in `src/ServerLogic/SkillSystem/`, e.g. `bool AreInSameParty(EntityID a, EntityID b)`, allocation-free. Production adapter: map `EntityID` → `CharacterID` through the zone pool and compare `IPartyZonePresence.TryGetFormalParty` results (`src/ServerLogic/ZoneInstancing/`; a solo pseudo-party is "no party"). The zone wires `NoPartyZonePresence` today, so the party rule matches nobody in production until the Party System replaces it; Story 011 wires the adapter. Tests drive the party rule through a fake source. Same pattern as Story 005's V-7 party handling.
- **Batch order and overflow** are already fixed in `RUBatchWriter` (`SkillCastResult` before `SkillCooldownUpdate` in write order, the reverse in drop order). Only the categories are set here.
- **Wire**: structs and codecs in `src/Foundation/Networking/WireProtocol/`. Record `0x0309` and `0x030A` in `networking-wire-protocol.md`. `SkillCastResult` fields: `int damageDealt`, `int healAmount`, `int newCooldownTicks`.
- **Message ids (readiness 2026-11-04)**: keep `SkillCastResult = 0x0309` and `SkillCooldownUpdate = 0x030A`. 0x02xx/0x03xx is the R-U batch / U-U range, next to the R-U sub-messages `HitCheckResult` 0x0306 and `AutoFaceEvent` 0x0307, and no other message uses these ids. The doc comments at `SkillCastRequest.cs:26` and `SkillUnlockNotification.cs:14` wrongly call 0x0308-0x030A a "U-U skill block"; reword both to "skill block 0x0308-0x030A (request U-U; result and cooldown update R-U)".
- **Target id 0 (code review 2026-11-04)**: the emitter has no resolved target, so `targetID` is the submitted `request.TargetEntityId`. A Self or target-0 cast reaches only the caster, the caster's party and clients targeting the caster, not bystanders who merely see the caster: RFR-1 has no proximity set. A player target always receives an accepted result through its own self slot.
- **"Same batch" (CR-SK-23, code review 2026-11-04)**: this story shows it as both messages enqueued for the caster in the same `SendAccepted` / `SendRejection` call; placement in one R-U batch and write/drop order belong to `RUBatchWriter` (its own tests) and the Story 011 composition.
- **From Story 008 (readiness 2026-11-04)**: `SendAccepted` gains `in SkillExecutionOutcome outcome` (`DamageDealt`, `IsCrit`, `IsKill`, `HealAmount`). Fill `SkillCastResult`'s damage, heal, crit and kill fields from it; do not re-derive them. `HealAmount` is the HP delivered (code review 2026-11-04): 0 when Status Effects rejected the heal or this cast killed the target.
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
- **Accepted recipients**: client A in the caster's party, client B targeting the target, client C unrelated, client D both in the party and targeting → A, B, D (once each) and the caster; not C. Party data from a fake `ISkillCastPartySource`; a client in the target's party (not the caster's) also receives it.
- **Party adapter**: over `NoPartyZonePresence` → `AreInSameParty` false for every pair; over a fake `IPartyZonePresence` with two characters in one formal party → true.
- **Zero cooldown**: a `CooldownTicks == 0` cast → one `SkillCooldownUpdate` with expiry = the cast tick.
- **CR-SK-23 (1)**: accepted → one `SkillCooldownUpdate` to the caster, same batch, correct expiry.
- **CR-SK-23 (2)**: `OnCooldown` rejection → one update with the current expiry; `InsufficientMana` rejection → none.
- **No expiry message**: run ticks past an expiry → zero updates.
- **No allocation**: an accepted cast with three recipients inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_CastOutput_tests.cs` — must exist and pass

**Status**: [x] Created 2026-11-04 (`SkillSystem_CastOutput_tests.cs` 35 cases + `tests/EditMode/Networking/SkillCastResult_Codec_tests.cs` 20; after code-review fixes EditMode 6337 / 6208 / 0 failed / 129 skipped)

---

## Dependencies

- Depends on: Story 006 (Story 005 declares the seam)
- Unlocks: Stories 010, 011

---

## Completion Notes
**Completed**: 2026-11-04
**Criteria**: 10/10 passing, none deferred
**Deviations** (advisory, no tech debt):
- `targetID` is the submitted `request.TargetEntityId` (the seam carries no resolved target); Self / target-0 casts reach only the caster, its party and clients targeting the caster (RFR-1 has no proximity set). A player target always receives an accepted result through its self slot (RFR-1; fixed during `/dev-story`).
- CR-SK-23 "same batch" is shown as both messages enqueued for the caster in one `SendAccepted` / `SendRejection` call; single-batch placement and write/drop order belong to `RUBatchWriter` tests and Story 011.
- Party recipients are inert in production until the Party System exists (`NoPartyZonePresence`); Story 011 wires `SkillCastOutputEmitter` and `ZoneSkillCastPartySource`.
- Codecs are standalone (`SkillCastResultCodec`, `SkillCooldownUpdateCodec`, like `SkillCastRequestCodec`), not in `BatchSubMessageCodec`; compatible because opaque R-U categories are enqueued as pre-encoded bytes.
- Extra files, required by existing gates: `MessageRoutingRegistry.cs` (rows for 0x0309 ServerToRelevantClients and 0x030A ServerToOwningClient, R-U batch) and `AssemblyBoundaryLists.cs` (ADR-012 allow-list); `SkillSystemService.TryGetCooldownExpiry` (read-only lookup).
**Test Evidence**: Logic: `tests/EditMode/SkillSystem/SkillSystem_CastOutput_tests.cs` (35 cases) + `tests/EditMode/Networking/SkillCastResult_Codec_tests.cs` (20); full EditMode 6337 / 6208 / 0 failed / 129 skipped, started 14:39:01Z after the last code edit.
**Code Review**: Complete (`/code-review` 2026-11-04: CHANGES REQUIRED → "fix all" applied: caster cooldown update before the roster loop, encode once, AC-SK-43 control request, `TryGetCooldownExpiry` tests, `LogAssert.NoUnexpectedReceived` + log-once test, recipient edge cases; QL-TEST-COVERAGE and LP-CODE-REVIEW skipped, lean)
