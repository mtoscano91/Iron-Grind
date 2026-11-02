# Story 010: SkillCooldownSnapshot on Zone Join

> **Epic**: Skill System
> **Status**: Ready
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-26
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/skill-system.md` — CR-SK-12, CR-SK-23; `design/gdd/networking-wire-protocol.md` — `SkillCooldownSnapshot`; `design/gdd/zone-instancing.md` — zone-entry sequence
**Requirement**: `TR-sk-010`

**ADR Governing Implementation**: ADR-004: Networking — NGO Substrate
**ADR Decision Summary**: R-OD to the owning client in the project envelope. The zone-entry sequence already calls `IZoneEntryResponder.SendSkillCooldownSnapshot(clientId, entityId)` after `SessionReady` (`ZoneEntryCompletion.cs:120`); today it goes to `LoggingZoneEntryResponder`, a stub.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: wire schemas and codecs in `IronGrind.Foundation`
- Required: zone-entry steps never throw (`ZoneEntryCompletion` catches and logs)

---

## Acceptance Criteria

*From GDD `design/gdd/skill-system.md`, scoped to this story:*

- [ ] `SkillCooldownSnapshot` codec: 80-byte body, 10 entries of `{ uint skillID, uint cooldownExpiryTick }` in class-skill-list order, `MessageTypeId = 0x030B`; round-trips.
- [ ] **AC-SK-09 / AC-SK-44**: on zone join with at least one skill on cooldown, the joiner receives one snapshot with 10 entries and the correct `cooldownExpiryTick` for that skill.
- [ ] Locked skills and skills never cast carry `cooldownExpiryTick = 0`.
- [ ] A class with fewer than 10 skills pads the remaining entries with `skillID = 0`, `cooldownExpiryTick = 0`.
- [ ] An entity with no skill store (spawn not yet run, or unknown) gets a snapshot of 10 zero entries and a warning, never an exception.
- [ ] The snapshot is sent after `SessionReady` (existing order in `ZoneEntryCompletion`); only the snapshot send changes.

---

## Implementation Notes

- **Where**: `IZoneEntryResponder` (`src/ServerLogic/ZoneInstancing/IZoneEntryResponder.cs`) has `SendSessionReady` and `SendSkillCooldownSnapshot`. The production default is `LoggingZoneEntryResponder` (`ZoneInstancingWiring.cs:278`, `options.Responder ?? new LoggingZoneEntryResponder()`). Two choices; pick at readiness:
  1. a real responder that sends `SessionReady` (if it is still a stub too, check) and the snapshot; or
  2. **(recommended)** a decorator that delegates `SendSessionReady` and implements `SendSkillCooldownSnapshot` from the Skill System, so this story does not touch `SessionReady`.
- **Ordering versus spawn**: `OnPlayerEntering` (the entry listener) runs before the snapshot (`ZoneEntryCompletion.cs:113` then 120). Story 011 hooks `OnEntitySpawned` to that listener, so the store exists when the snapshot is built. Assert this in Story 011's pipeline test; here, test the empty-store fallback.
- **Reconnect**: the GDD also sends the snapshot on reconnect. In this codebase a reconnect re-enters the zone through the same entry path (check `zone-instancing.md`); if so, nothing extra is needed. Note what you found.
- **Cooldowns across zones**: cooldowns are per zone process and not persisted (Story 006, EC-SK-13), so a player entering a new zone has no cooldowns. That matches "skill returns to Ready" in EC-SK-13; note it for Combat UI.
- **Wire**: struct and codec in `src/Foundation/Networking/WireProtocol/`; send R-OD through `ClientMessageOutbox`. Record `0x030B` in `networking-wire-protocol.md`.

---

## Out of Scope

- Story 011: the spawn hook that fills the store before the snapshot
- Client application of the snapshot: Combat UI

---

## QA Test Cases

- **Codec**: round trip of 10 entries; a body of 79 or 81 bytes rejected; id `0x030B`.
- **AC-SK-44**: store with slot 2 at expiry 1200, others 0 → snapshot entries match by index and id.
- **Locked / never cast**: zero expiry.
- **Short class**: 3 skills → entries 3–9 are `(0, 0)`.
- **No store**: unknown entity → 10 zero entries + one warning, no throw.
- **Entry order** (integration, real `ZoneEntryCompletion` with fakes): the recorded order is snapshot (zone state), `SessionReady`, `SkillCooldownSnapshot`, as today.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_CooldownSnapshot_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 002, 009
- Unlocks: Story 011
