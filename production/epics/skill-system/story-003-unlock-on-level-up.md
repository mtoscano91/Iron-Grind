# Story 003: Unlock on Level-Up and SkillUnlockNotification

> **Epic**: Skill System
> **Status**: Complete
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-10-26
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-21, States and Transitions; `design/gdd/leveling-system.md` — CR-2.10; `design/gdd/networking-wire-protocol.md` — `SkillUnlockNotification`
**Requirement**: `TR-sk-003`

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture; ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: ADR-010: the Skill System subscribes to the Leveling System's `OnLevelUp` event with a named method, never a lambda. ADR-004: the project envelope and channels carry all messages; `SkillUnlockNotification` is R-OD to the owning client.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: N/A — no engine API involved; pure C# unlock logic, a Foundation codec and a send through the existing `ClientMessageOutbox` seam.

**Control Manifest Rules (this layer)**:
- Forbidden: lambda subscriptions on persistent events
- Required: wire schemas and codecs in `IronGrind.Foundation`; handlers and senders in `IronGrind.ServerLogic`

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [x] **AC-SK-11**: a skill with `UnlockLevel = N` has `IsUnlocked == true` immediately after `OnLevelUp(entity, N)` returns.
- [x] **AC-SK-14**: `IsUnlocked` never reverts from true to false across increasing `OnLevelUp` calls.
- [x] **AC-SK-45**: a level-up that unlocks exactly one skill sends exactly one `SkillUnlockNotification` with that `skillID` to the owner.
- [x] **AC-SK-46**: a level-up that crosses no threshold sends no `SkillUnlockNotification`.
- [x] A multi-level gain (CR-2.10 fires `OnLevelUp` once per level, in order) unlocks the skill of each level and sends one notification per unlocked skill, in level order.
- [x] `OnLevelUp` for an entity with no store (not spawned, or a mob) is ignored without error.
- [x] `SkillUnlockNotification` codec: 4-byte body `{ uint skillID }`, `MessageTypeId = 0xE151`, round-trips; a wrong-length body is rejected by the reader.

---

## Implementation Notes

- **Event**: `ILevelingEventBroadcaster.OnLevelUp` is `event Action<LevelUpEventArgs>` (`src/Foundation/LevelingSystem/ILevelingEventBroadcaster.cs`); `LevelingService` implements it. Subscribe with a named method, e.g. `HandleLevelUp(LevelUpEventArgs args)`, which calls `ISkillSystem.OnLevelUp(args.EntityId, args.NewLevel)` (check the field names in `LevelUpEventArgs.cs`). Nothing subscribes to it yet, so this is its first subscriber. The subscription is made in Story 011's composition; this story exposes subscribe and unsubscribe methods and tests them against a fake broadcaster.
- **Unlock rule (CR-SK-21)**: for each slot with `!IsUnlocked && def.UnlockLevel == newLevel`, set it unlocked and send the notification. Equality, not `<=`: CR-2.10 fires once per level gained, so every level is seen. A skill whose `UnlockLevel` is below the spawn level was already unlocked at spawn (Story 002).
- **Missing definition**: a slot whose skill id has no `SkillDatabase` definition (Story 002: error already logged at spawn) is skipped — it stays locked, sends no notification, and logs no second error.
- **Sending**: R-OD to the owning client through `ClientMessageOutbox` (`src/ServerLogic/Networking/Outbound/`), behind a narrow seam (`ISkillUnlockOutput`) with a recording fake in tests. The `EntityID` → client id lookup goes through the zone pool, as `AutoAttackDamageOutputEmitter` does for `SelfDamageEvent`.
- **Wire**: `SkillUnlockNotification` struct and codec in `src/Foundation/Networking/WireProtocol/` (the `AutoFaceEvent.cs` precedent). `MessageTypeId = 0xE151` (R-OD priority block 0xE150–0xE15F reserved for this epic: 0xE150 `SkillCooldownSnapshot`, 0xE151 `SkillUnlockNotification`; R-U / U-U ids 0x0308 `SkillCastRequest`, 0x0309 `SkillCastResult`, 0x030A `SkillCooldownUpdate`; reassigned 2026-11-02 from 0x030C because `ClientMessageOutbox` sends R-OD only in 0xE000–0xEFFF). Record the id in `networking-wire-protocol.md` next to the schema, as `HitCheckResult` did.
- `CharacterStats.IsFiringAndAssert` blocks stat mutation inside a stat event. This handler mutates no stats; confirm it is not reached from inside one.

---

## Performance

Runs once per level gained, not per tick. Each call loops over at most 10 slots doing compares, plus one 4-byte R-OD send per unlock. No allocation: the notification is a struct and the outbox buffers are pooled.

---

## Out of Scope

- Story 011: the subscription in the composition root and the same-tick ordering test (AC-SK-49)
- Client handling of the notification: Combat UI / HUD

---

## QA Test Cases

- **AC-SK-11**: a skill at `UnlockLevel` 5, entity spawned at 4 → `OnLevelUp(e, 5)` → unlocked before the call returns.
- **AC-SK-14**: `OnLevelUp` 2 through 60 in order → no slot ever goes from true to false (snapshot after each call).
- **AC-SK-45**: unlock exactly one → one notification with the right id and recipient.
- **AC-SK-46**: `OnLevelUp(e, 3)` with no skill at 3 → zero notifications.
- **Multi-level**: skills at 4 and 6, `OnLevelUp` 4, 5, 6 → two notifications in order 4 then 6.
- **Two skills at the same level**: both unlock, two notifications.
- **Unknown entity / mob**: no throw, no notification.
- **Missing definition**: `OnLevelUp` 1 through 60 → the slot stays locked, zero notifications, no new error log.
- **Subscription**: `Subscribe` then raise the fake event → handled; `Unsubscribe` then raise → not handled; the handler is a named method (reflection on the delegate's `Method.Name`, no compiler-generated `<` in it).
- **Codec**: round trip; body length 3 and 5 rejected; `MessageTypeId == 0xE151`.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_Unlock_tests.cs` — must exist and pass

**Status**: [x] Created and passing (`SkillSystem_Unlock_tests.cs` 32 cases + `SkillSystem_UnlockOutputEmitter_tests.cs` 6 cases; EditMode 6015 / 5886 / 0 failed / 129 skipped, run 2026-11-02 23:54:43Z)

---

## Dependencies

- Depends on: Story 002
- Unlocks: Story 011

---

## Completion Notes
**Completed**: 2026-11-02
**Criteria**: 7/7 passing (none deferred)
**Deviations**: Advisory, no tech debt logged. `MessageTypeId` 0x030C → `0xE151` (user decision 2026-11-02: `ClientMessageOutbox` sends R-OD only in 0xE000–0xEFFF; Skill System R-OD block 0xE150–0xE15F, `SkillCooldownSnapshot` 0xE150 — GDD, EPIC.md note 9 and Story 010 updated). `MessageRoutingRegistry` row for 0xE151 (`AddPriority`, `EarnedPower`, `ServerToOwningClient`; AC-MCR-04 requires one row per id). Not testable: the emitter's own fault-log path (the outbox catches its own faults, `ZoneSlotPool` sealed) and the not-cap-exempt flag. Carried to Story 011: the composition root holds the concrete `SkillSystemService`, calls `Subscribe` once and `Unsubscribe` at zone teardown; AC-SK-49 same-tick ordering; `OnLevelUp` may run inside an open stat transaction (handler mutates no stats). Extra files: `MessageRoutingRegistry.cs`, `AssemblyBoundaryLists.cs`, `SkillSystem_SkillStore_tests.cs`, `networking-wire-protocol.md`, `EPIC.md`, `story-010-cooldown-snapshot.md`.
**Test Evidence**: Logic: `tests/EditMode/SkillSystem/SkillSystem_Unlock_tests.cs` (32) + `SkillSystem_UnlockOutputEmitter_tests.cs` (6), all pass
**Code Review**: Complete (`/code-review` CHANGES REQUIRED → "fix all": `Subscribe` guard, two-pass `OnLevelUp`, emitter tests on the real pool + outbox, +11 Unlock cases; the emitter tests exposed the id range defect). QL-TEST-COVERAGE and LP-CODE-REVIEW skipped (lean).
