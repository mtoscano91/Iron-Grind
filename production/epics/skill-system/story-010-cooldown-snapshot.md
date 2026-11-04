# Story 010: SkillCooldownSnapshot on Zone Join

> **Epic**: Skill System
> **Status**: Complete
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

- [x] `SkillCooldownSnapshot` codec: 80-byte body, 10 entries of `{ uint skillID, uint cooldownExpiryTick }` in class-skill-list order, `MessageTypeId = 0xE150`; round-trips.
- [x] **AC-SK-09 / AC-SK-44**: on zone join with at least one skill on cooldown, the joiner receives one snapshot with 10 entries and the correct `cooldownExpiryTick` for that skill.
- [x] Locked skills and skills never cast carry `cooldownExpiryTick = 0`.
- [x] A class with fewer than 10 skills pads the remaining entries with `skillID = 0`, `cooldownExpiryTick = 0`.
- [x] An entity with no skill store (spawn not yet run, or unknown) gets a snapshot of 10 zero entries and a warning, never an exception.
- [x] The snapshot is sent after `SessionReady` (existing order in `ZoneEntryCompletion`); only the snapshot send changes.

---

## Implementation Notes

- **Where**: `IZoneEntryResponder` (`src/ServerLogic/ZoneInstancing/IZoneEntryResponder.cs`) has `SendSessionReady` and `SendSkillCooldownSnapshot`. The production default is `LoggingZoneEntryResponder` (`ZoneInstancingWiring.cs:278`, `options.Responder ?? new LoggingZoneEntryResponder()`).
- **Decision (readiness 2026-11-04): decorator.** Add a `SkillCooldownSnapshotResponder : IZoneEntryResponder` in `src/ServerLogic/SkillSystem/` that wraps an inner `IZoneEntryResponder`: `SendSessionReady` delegates to the inner responder unchanged, and `SendSkillCooldownSnapshot` builds the snapshot from the Skill System store and sends it. This story does not touch `SessionReady`; Story 011 wraps the zone's responder with it.
- **Ordering versus spawn**: `OnPlayerEntering` (the entry listener) runs before the snapshot (`ZoneEntryCompletion.cs:113` then 120). Story 011 hooks `OnEntitySpawned` to that listener, so the store exists when the snapshot is built. Assert this in Story 011's pipeline test; here, test the empty-store fallback.
- **Reconnect (verified at readiness 2026-11-04)**: the GDD also sends the snapshot on reconnect (CR-SK-12, CR-SK-23). `zone-instancing.md` says every reconnect re-enters the full CR-ZI-8 entry sequence from step 2, with no delta or resume path (line 378), and a duplicate handshake to the same zone reclaims the old entering slot first (CR-ZI-17 case (b), `ZoneEntryReclamation`). So a reconnect reaches the same `SendSkillCooldownSnapshot` call in `ZoneEntryCompletion`; this story adds nothing for it. Open for Story 011: on a same-zone reconnect of a session still in `Disconnected_SessionActive`, CR-SK-12 implies the snapshot carries the cooldowns still running, so the spawn hook must not reset an existing store for the same entity (unlike consumables, which reset on every zone entry, `consumable-use-system.md` rule 13).
- **Cooldowns across zones**: cooldowns are per zone process and not persisted (Story 006, EC-SK-13), so a player entering a new zone has no cooldowns. That matches "skill returns to Ready" in EC-SK-13; note it for Combat UI.
- **Wire**: struct and codec in `src/Foundation/Networking/WireProtocol/`; send R-OD through `ClientMessageOutbox`. Record `0xE150` in `networking-wire-protocol.md`.
- **Registry and allow-list (readiness 2026-11-04)**: a new message with no routing row fails the registry test (AC-MCR-04), as Story 009 found. Add an `AddPriority` row to `MessageRoutingRegistry.cs` for `SkillCooldownSnapshot` (`0xE150`, `MessageDirection.ServerToOwningClient`, R-OD priority path, not cap-exempt, pillar `RhythmMastery` to match `SkillCooldownUpdate`), next to the `SkillUnlockNotification` row. Add `SkillCooldownSnapshot` and its codec to the ADR-012 allow-list in `tests/EditMode/Architecture/AssemblyBoundaryLists.cs`.

---

## Out of Scope

- Story 011: the spawn hook that fills the store before the snapshot
- Client application of the snapshot: Combat UI

---

## QA Test Cases

- **Codec**: round trip of 10 entries; a body of 79 or 81 bytes rejected; id `0xE150`.
- **AC-SK-44**: store with slot 2 at expiry 1200, others 0 → snapshot entries match by index and id.
- **Locked / never cast**: zero expiry.
- **Short class**: 3 skills → entries 3–9 are `(0, 0)`.
- **No store**: unknown entity → 10 zero entries + one warning, no throw.
- **Entry order** (integration, real `ZoneEntryCompletion` with fakes): the recorded order is snapshot (zone state), `SessionReady`, `SkillCooldownSnapshot`, as today.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/SkillSystem/SkillSystem_CooldownSnapshot_tests.cs` — must exist and pass

**Status**: [x] Created 2026-11-04 (16 methods, 18 cases after code review; EditMode 6355 / 6226 / 0 failed / 129 skipped)

---

## Dependencies

- Depends on: Stories 002, 009
- Unlocks: Story 011

---

## Completion Notes

**Completed**: 2026-11-04
**Criteria**: 6/6 passing (all automated; none deferred)
**Deviations** (advisory, no tech debt):
- The "no skill store" warning is logged once per responder (code review); the snapshot is still sent every time.
- A locked slot carries expiry 0 even with a stored value (wire spec).
- A client unknown to the outbox is skipped silently, as `SkillUnlockOutputEmitter` does.
- The entry-order test records calls through a spy around the decorator (`ClientMessageOutbox` is sealed) and decodes the joiner's one packet.
- Added beyond the file list: `SkillSystemService.HasRecord` (internal; tells "no record" from "class with zero skills"), `SkillCooldownEntry` on the ADR-012 allow-list, `SessionReady` guarded with a log-once catch.
- Not wired: the zone still composes `LoggingZoneEntryResponder`; Story 011 wraps it.
**Test Evidence**: Integration: `tests/EditMode/SkillSystem/SkillSystem_CooldownSnapshot_tests.cs`, 16 methods / 18 cases. EditMode 6355 / 6226 / 0 failed / 129 skipped, started 15:06:11Z.
**Code Review**: Complete (unity-specialist CLEAN, qa-tester GAPS → fixed; "fix all" applied).
