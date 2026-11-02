# Story 004: Cast Intake (SkillCastRequest, V-1, V-2)

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-1, CR-SK-2 (V-1, V-2), EC-SK-11; `design/gdd/networking-wire-protocol.md` — `SkillCastRequest`
**Requirement**: `TR-sk-004`

**ADR Governing Implementation**: ADR-014: Inbound Request Dispatch and Tick Order
**ADR Decision Summary**: one `InboundRequestDispatcher` per zone is the only path from a client message to a game system. `SkillCastRequest` has a routing row: U-U, not held, `RpcTypeTag.NotifySkillUsed`, 16-byte body, stale-dropped per type by `SequenceNumber`. Handlers run in the dispatch step, before phase 3.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: register the handler in the server composition root before `Seal`
- Required: a malformed body is logged and dropped, never thrown
- Guardrail: the handler allocates nothing

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] `SkillCastRequest` codec: 16-byte body `{ uint casterEntityID, uint skillID, uint targetEntityID, uint clientTickNumber }`, `MessageTypeId = 0x0308`, round-trips; a wrong length is rejected.
- [ ] **V-1**: a request whose `casterEntityID` is not the connection's `SenderEntityId` is dropped silently (no `SkillCastResult`) and logs a security event.
- [ ] **AC-SK-04 / V-2**: a request with `clientTickNumber = serverTick − (CAST_STALE_TICK_TOLERANCE + 1)` is dropped with no `SkillCastResult` and a server-side security log entry; `serverTick − CAST_STALE_TICK_TOLERANCE` is accepted (boundary).
- [ ] **AC-SK-43**: V-1 and V-2 failures produce no `SkillCastResult` to any client.
- [ ] **EC-SK-11 / CR-SK-1**: of several requests from one entity in one tick, the first received is kept and the rest are discarded without notification; discarded requests are not carried into the next tick.
- [ ] A kept request is visible to the phase-3 step on the same tick (Decision 3) and is cleared once that step has run.
- [ ] A `clientTickNumber` ahead of the server tick is not stale (the check is `serverTick − clientTickNumber ≤ tolerance`, unsigned-safe).

---

## Implementation Notes

- **Handler**: `SkillCastRequestHandler` in `src/ServerLogic/Networking/InboundDispatch/` (the `SetAutoAttackRequestHandler.cs` precedent: decode with the codec, log `InboundMessageMalformed` and return on failure). It runs V-1 and V-2 and then calls `ISkillCastIntake.Submit(...)` on the Skill System, which stores the request in a per-entity slot (one per player slot, pre-allocated). The slot is first-wins until the phase-3 step clears it.
- **Registration**: add a `RegisterSkillInput` method (or extend `RegisterCombatInput`) in `NetworkingCoreInboundRegistration.cs` with `new InboundRequestDescriptor(SkillCastRequest.MessageTypeId, RpcTypeTag.NotifySkillUsed, heldDuringIrreversibleWrite: false, maxBodyBytes: 16)`. `RpcTypeTag.NotifySkillUsed = 1` already exists and is rate-limited in `CrossCuttingRpcGuardChain` (`NotifySkillUsedRateLimitTicks`). Check that the rate limit is not stricter than one cast per tick; if it is, raise it at readiness. Update the `RpcTypeTag` doc comment, which says the Skill System epic has not started.
- **V-1**: compare the body's `casterEntityID` with `context.SenderEntityId`. The security log is a `Debug.LogWarning` with a fixed prefix (the guard-chain style), throttled like the other guard logs.
- **V-2**: the server tick comes from `IServerTickSource` (ADR-014). `CAST_STALE_TICK_TOLERANCE` is read from `SkillSystemConfig`. Compute in `long` or with an explicit `clientTick > serverTick` branch, so a client tick ahead of the server does not wrap.
- V-2 is separate from ADR-014's `SequenceNumber` stale drop, which happens before the handler. Both can drop a message.
- **Wire**: `SkillCastRequest` struct and codec in `src/Foundation/Networking/WireProtocol/`. Record `0x0308` in `networking-wire-protocol.md`.

---

## Out of Scope

- Story 005: V-0 and V-3 to V-8 (run in phase 3)
- Story 011: registration in `ZoneServerComposition`

---

## QA Test Cases

- **Codec**: round trip of all four fields; bodies of 15 and 17 bytes rejected; `MessageTypeId == 0x0308`.
- **V-1**: mismatched caster → nothing stored, zero results, one warning (`LogAssert.Expect`).
- **V-2 boundary**: `[TestCase]` at tolerance (accepted), tolerance + 1 (dropped + log), client tick = server tick + 3 (accepted), server tick 2 with client tick 4,294,967,295 (no wrap-around accept).
- **First wins**: three requests from one entity in one tick → only the first is visible to the phase-3 consumer.
- **Clear**: after the consumer runs, the slot is empty; a request on the next tick is stored again.
- **Two entities**: one request each in the same tick → both stored.
- **Malformed**: a short body → warning, nothing stored, no throw.
- **No allocation**: `Submit` and the handler path inside `Is.Not.AllocatingGCMemory()`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_CastIntake_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 001
- Unlocks: Story 005
