# Story 007: Damage Output

> **Epic**: Auto-Attack Combat
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/networking-channel-contract.md` — `DamageEvent`, `SelfDamageEvent`, AC-CCR-04; `design/gdd/networking-message-criticality.md` — AC-MCR-02
**Requirement**: `TR-aa-009` (placeholder)

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: project messages use the project envelope and channels. `DamageEvent` is R-U, written first in the batch; `SelfDamageEvent` is R-OD.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: consumer only.

**Control Manifest Rules (this layer)**:
- Required: typed `DamageEvent` intake on the R-U batch step; `Enqueue` keeps refusing `RUBatchCategory.DamageEvent`
- Forbidden: allocation per emitted event
- Guardrail: a `SelfDamageEvent` recipient is the attacker alone, never derived from a broadcast list

---

## Acceptance Criteria

*From the channel contract and criticality GDDs, scoped to this story:*

- [ ] `IRUBatchCollector` gains typed `DamageEvent` intake, and `RUBatchOutboundStep` writes those events first in the batch.
- [ ] A hit sends `SelfDamageEvent` to exactly the attacker's client over the R-OD priority path.
- [ ] A hit sends `DamageEvent` to all zone clients except the attacker; the attacker gets none for the same triple (AC-CCR-04).
- [ ] `FinalDamage` on both messages equals `DamageResult.FinalDamage` (AC-MCR-02).
- [ ] Both carry `isCrit` and the damage type derived from `DamageContext.PhysicalAuto`.
- [ ] A miss sends neither (AC-HD-12, AC-HD-13).
- [ ] A kill sends the same two messages; `KillEvent` is not sent (Enemy AI epic).
- [ ] A ghost client gets nothing and nothing throws.

---

## Implementation Notes

- `SelfDamageEventDispatcher.SerializeForAttacker` exists and rejects a wrong recipient. Use it. It is not wired yet.
- `DamageEvent` and its codec exist (`BatchSubMessageCodec.WriteDamageEvent`). `DamageEvent` is bounded separately from the opaque categories; read `RUBatchWriter` before choosing storage.
- `DamageResult.FinalDamage` is checked against the wire's `int finalDamage`; do not round twice.
- `networking-core.md:275` still says `Server → All clients`; align it with the channel contract while here.
- The `MessageRoutingRegistry` remark at line 78 says `SelfDamageEvent` is not yet implemented; update it.
- This is the damage-applied seam from Story 003: a new emitter class fed by Combat, in composition after `RUBatchOutboundStep`.

---

## Out of Scope

- `KillEvent`: Enemy AI epic
- `EntityHealthUpdate` flow
- Client damage numbers, AC-08: Combat UI

---

## QA Test Cases

Use the real `RUBatchOutboundStep`, outbox and three clients (attacker, bystander, ghost).

- **AC-CCR-04**: one hit → attacker receives one `SelfDamageEvent` and no `DamageEvent`; bystander receives one `DamageEvent` and no `SelfDamageEvent`.
- **AC-MCR-02**: decoded `finalDamage` equals the fake `DamageResult`.
- **Crit**: `isCrit` true round-trips on both.
- **Batch order**: a `DamageEvent` and a `HitCheckResult` on one tick → `DamageEvent` is first in the decoded batch.
- **Miss**: no output.
- **Kill**: both messages, no `KillEvent`.
- **Solo**: attacker alone in the zone → only `SelfDamageEvent`.
- **Ghost**: ghost recipient → no packet, no throw.
- **`Enqueue` refusal**: the opaque path still refuses `RUBatchCategory.DamageEvent`.
- **Overflow**: more `DamageEvent` than the bound → follows `RUBatchWriter`'s rule, no throw.
- **Allocation**: emit inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/AutoAttackCombat/AutoAttackCombat_DamageOutput_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 006; Hit Detection Story 003 (Complete)
- Unlocks: Combat UI / HUD damage numbers
