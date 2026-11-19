# Story 004: Member Status and XP Eligibility

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-3 (leader reference), CR-PS-5 eligibility, Exposed Data Types (`PartyMemberSlot`), Interactions (Death & Respawn row); `death-and-respawn.md` CR-DR-5, CR-DR-12, CR-DR-18
**Requirement**: `TR-party-010`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: Death & Respawn calls the Party System through `IPartyMemberStatusSink` (direct call); the slot is retained during the countdown. Parties are zone-local at MVP (ADR-010 note 2026-11-18).

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: dependency injection; doc comments
- Guardrail: `GetPartyMembersWithStatus` fills a caller buffer (no per-call allocation)

---

## Readiness Decisions (2026-11-19, user "fix all")

1. **Order 004 → 003**: no leader-change API exists yet (`LeaderIndex` private set, always 0). Eligibility always reads the party's current `LeaderIndex`; AC-5 is tested through an internal `SetLeaderIndexForTests` seam. The real transfer, its eligibility flip, its `SequenceId` bump and the XP-ineligible notice are Story 003's.
2. **Zone**: parties are zone-local at MVP, so "in the same zone instance" (CR-PS-5) = status is not OutOfZone; it is covered by the Online check. No separate zone query.
3. **Status set**: the server enum equals the wire enum (4 values). GDD / TR "Disconnected" = Ghost at MVP (a disconnected player with a live session is a ghost, ADR-011). GDD wording fixed at story-done.
4. **Status is kept per character**, for every registered player (solo too), so a solo player who dies and then joins a party stays Dead. It follows the character through create / join / leave / re-solo and is cleared on `UnregisterPlayer`.
5. **Server type name** `PartyMemberStatusEntry` (not `PartyMemberSlot`, which is the 5-field wire struct Story 006 defines).
6. **XP-ineligible notice** (CR-PS-5, last paragraph): Story 006 defines the message; Story 003 sends it on leader change. Nothing is sent from this story.

---

## Acceptance Criteria

- [x] **AC-1 Status**: `PartyMemberStatus : byte` with Online = 0, Ghost = 1, OutOfZone = 2, Dead = 3 (exactly `networking-wire-protocol.md` §PartyMemberStatus). A newly registered player is Online. `GetMemberStatus(CharacterID)` returns the stored status (Online for an unregistered character; never throws). Status survives create / join / remove / re-solo unchanged; `UnregisterPlayer` clears it (re-register → Online).
- [x] **AC-2 Death sink**: `PartyMemberStatusSink : IPartyMemberStatusSink` (interface unchanged) maps `PartyMemberDeathStatus.Dead` → Dead and `Online` → Online through a registry EntityID → CharacterID lookup. The member's slot index (`GetMemberAtIndex`) is the same before Dead, during Dead and after Online (AC-DR-13 unit, AC-DR-NEW-4, AC-DR-NEW-8). A solo player's status is stored the same way. An unknown or `EntityID.Invalid` entity is a no-op: no state change, no log.
- [x] **AC-3 Change marker**: a status write that changes the value increments the party's `SequenceId` by 1 (for Story 008's broadcast); a write of the same value changes nothing (no `SequenceId` bump).
- [x] **AC-4 Members with status**: `int GetPartyMembersWithStatus(PartyID, Span<PartyMemberStatusEntry>)` writes `PartyMemberStatusEntry { CharacterID, EntityID, Status }` for indices `0 .. MaxPartySize − 1` in join order, empty indices as `CharacterID` 0 / `EntityID.Invalid` / Online, and returns the member count. Unknown party or a buffer shorter than `MaxPartySize` → returns 0 and writes nothing. Zero GC allocation per call.
- [x] **AC-5 Eligibility**: `bool IsXpEligible(PartyID, CharacterID)` is true only when the character is a member of that party, its status is Online, and `|memberLevel − leaderLevel| ≤ LevelRangeGate` (config, inclusive). Levels come from an injected `IPartyMemberLevelSource.TryGetLevel(EntityID, out int level)`. Member level unreadable → false for that member; leader level unreadable → false for every member. The leader is eligible when Online (difference 0).
- [x] **AC-6 Leader reference**: with 4 members and `SetLeaderIndexForTests` moving the leader to a member whose level puts two others outside the gate, all 4 stay members (AC-PS-5) and exactly those two report `IsXpEligible = false`; moving the leader back restores them.

---

## Implementation Notes

- `IPartyMemberStatusSink` and `PartyMemberDeathStatus` exist in `src/ServerLogic/DeathAndRespawn/` (Death & Respawn Story 002): implement the sink; keep the interface unchanged. Unknown `PartyMemberDeathStatus` value → treat as no-op (no exception).
- Put `PartyMemberStatus` in `src/Foundation/PartySystem/` (new folder, `IronGrind.Foundation` assembly, beside other wire enums such as `InventorySystem/DiscardFailReason.cs`) so Story 006's codec reuses it.
- Registry: add a reverse `Dictionary<EntityID, CharacterID>` maintained by `RegisterPlayer` / `UnregisterPlayer`, and a per-character status store (dictionary or field with the existing per-character data). `PartyState` gets an internal `MarkChanged()` (SequenceId + 1) used by status writes; keep `RemoveAt` / `Free` behaviour unchanged.
- Level source: production `StatReaderPartyMemberLevelSource` (named `CharacterStatsPartyMemberLevelSource` at readiness) wraps `IZoneEntityStatReader.GetEffectiveStat(entity, StatID.Level)` (same read as `ZoneEntityStateSource.cs:176`); return false when the entity is unknown to Character Stats. Unit tests use a fake.
- `SetLeaderIndexForTests(PartyID, int)` is `internal`; Story 003 replaces its use with the real transfer and keeps or removes it.
- Read members with indexed loops (no `foreach`, it allocates). Resolve parties by id, never a cached `PartyState`.
- Status writes for Ghost / OutOfZone happen in Story 009; this story exposes an internal setter (`SetMemberStatus(CharacterID, PartyMemberStatus)`) that the sink and Story 009 share.

---

## Out of Scope

- Story 003: real leader transfer, its eligibility flip and `SequenceId` bump, and sending the XP-ineligible notice
- Story 005: using eligibility to award XP
- Story 006: the XP-ineligible notice message schema and the wire `PartyMemberSlot` codec
- Story 008: broadcasting `PartyStateUpdate` (level / `xpEligible` bytes are filled there)
- Story 009: Ghost / OutOfZone transitions (this story stores and reports them)

---

## QA Test Cases

*Refined at `/story-readiness` 2026-11-19.*

- **AC-1**: enum numeric values pinned (0/1/2/3, byte underlying type); new player Online; unregistered character → Online, no throw; Dead solo player creates a party / joins a party → still Dead; removed member re-soloed → status kept; unregister then re-register → Online.
- **AC-2**: [A, B, C] B Dead → `GetMemberAtIndex(1)` is B before, during and after Online; solo player Dead → `GetMemberStatus` Dead; unknown entity and `EntityID.Invalid` → no change, `LogAssert.NoUnexpectedReceived()`; out-of-range `PartyMemberDeathStatus` cast → no change, no throw.
- **AC-3**: Online → Dead bumps `SequenceId` by 1; Dead → Dead no bump; Dead → Online bumps by 1; solo party bumps its own solo party only.
- **AC-4**: party of 3 → returns 3, index 3 empty (CharacterID 0, EntityID Invalid, Online); entries carry the right EntityIDs and statuses; unknown party → 0, buffer untouched; buffer length `MaxPartySize − 1` → 0, untouched; 1000-call loop allocates 0 bytes (`GC.GetAllocatedBytesForCurrentThread`).
- **AC-5**: leader 30: member 35 eligible, 36 not, 25 eligible, 24 not; gate from config (set gate 0 → only same level eligible); Dead / Ghost / OutOfZone member not eligible; member level unreadable → false; leader level unreadable → all false; character not in that party → false; unknown party → false; leader Online → true.
- **AC-6**: levels [30, 35, 25, 40]; leader index 3 (40) → members 25 and 30 ineligible, 35 eligible, member count 4; leader back to 0 → 25, 30, 35 eligible, 40 not (|40−30| = 10).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_MemberStatus_tests.cs` — must exist and pass

**Status**: [x] Created — 48 tests, pass

---

## Dependencies

- Depends on: Story 002 (Complete)
- Unlocks: Stories 003 (status, leader eligibility), 005, 009

---

## Completion Notes
**Completed**: 2026-11-19
**Criteria**: 6/6 passing (all automated; none deferred)
**Deviations** (advisory):
- Production level source is `StatReaderPartyMemberLevelSource` over `IZoneEntityStatReader` (story named `CharacterStatsPartyMemberLevelSource`); level ≤ 0 or `EntityID.Invalid` → unreadable (Character Stats returns 0 for an unknown entity).
- Code review added `PartyRegistryResult.EntityAlreadyRegistered = 13` (`RegisterPlayer` refuses an entity already held by another character) and an owner check on the reverse-map removal in `UnregisterPlayer`; the owner check has no direct test (unreachable through the public API once duplicates are refused).
- `PartyXpEligibility` (separate class; registry ctor unchanged) and `PartyMemberStatusSink` are not composed into the zone server yet (Story 011).
- Files outside the story list: `PartyRegistryResult.cs` (new value), `tests/EditMode/Architecture/AssemblyBoundaryLists.cs` (Foundation allow-list entry for `IronGrind.PartySystem.PartyMemberStatus`, manifest rule).
**Test Evidence**: Logic — `tests/EditMode/PartySystem/PartySystem_MemberStatus_tests.cs` (48 tests); full EditMode `-testCategory "!Database"` 8232/8232 (2026-11-19 12:30:02Z)
**Code Review**: Complete (2026-11-19, unity-specialist + qa-tester; all required changes and suggestions applied)
