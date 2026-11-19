# Story 002: Party Registry — Solo PartyIDs and Membership

> **Epic**: Party System
> **Status**: Complete
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 4 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/party-system.md` — CR-PS-1, CR-PS-2, States and Transitions, Edge Cases (solo party join)
**Requirement**: `TR-party-001`, `TR-party-002`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-010: Event and Messaging Architecture
**ADR Decision Summary**: The Party System is a Tier 1 service reached by direct calls on injected interfaces; zone-local parties at MVP (note 2026-11-18).

**Engine**: Unity 6.3 LTS | **Risk**: LOW (pure C#)
**Engine Notes**: N/A — no engine API involved.

**Control Manifest Rules (this layer)**:
- Required: dependency injection; doc comments; no allocation per tick in steady state
- Guardrail: fixed-size member arrays (`MaxPartySize`), no LINQ in hot paths

---

## Readiness Decisions (2026-11-18)

Found at `/story-readiness`:
- CR-PS-9 gives a departing member a new solo PartyID at once, and CR-PS-1 cleans up a solo party when its player joins a formal party (leader included); the original ACs said neither.
- The existing `IPartyService.GetPartyMembers` contract (`IReadOnlyList<CharacterID>`, actual members only, a snapshot that does not change while the caller uses it) conflicts with a live `CharacterID[]` view.
- Story 009 AC-6 (slot release removes the registration) and Story 008 (disband cleanup) need registry primitives that were not listed.

Resolved (user approved):
- `RemoveMember` re-solos the removed character.
- `TryCreateParty` frees the leader's solo party.
- Member lists are immutable snapshots, replaced on change.
- New AC-6 adds `RegisterPlayer` failures, `UnregisterPlayer`, `DisbandParty` and counter exhaustion.
- Capacity comes from the injected config.
- Whether a real party that drops to one member auto-disbands is Story 008's rule (its AC-3), not this story's.

---

## Acceptance Criteria

- [x] **AC-1 Solo PartyID**: `RegisterPlayer(CharacterID, EntityID)` gives the player a solo party (`IsSoloParty = true`, `MemberCount = 1`, PartyID non-zero); no two registered players share a PartyID; solo and real ids come from one sequential per-process counter starting at 1; PartyID 0 is never returned (AC-PS-1).
- [x] **AC-2 Create and join**: `TryCreateParty(leader)` (leader registered and solo, else typed failure) turns the leader into a real party (new PartyID, `IsSoloParty = false`, leader at index 0, state `Forming`) and frees the leader's solo party; `TryAddMember(partyId, character)` appends in join order and moves `Forming → Active` at the second member; the joiner's solo party is freed. A freed solo PartyID no longer resolves (`TryGetParty` false).
- [x] **AC-3 Limits**: adding a member beyond `MaxPartySize` (injected config; default 4) fails with `PartyFull` and the count is unchanged (AC-PS-2); a character already in a real party cannot join another (`AlreadyInParty`); a solo party never accepts members (`SoloPartyCannotAccept`); unknown party / character → typed failure, no state change.
- [x] **AC-4 Compaction**: `RemoveMember(partyId, character)` clears the slot and compacts the array preserving join order ([A, B, C] remove B → [A, C]); `MemberCount` updates; the removed character gets a fresh solo party (new PartyID from the counter, never its old solo id or the party's id) (CR-PS-9); removing the last member frees the party (state cleared, PartyID never reused).
- [x] **AC-5 Queries**: `GetPartyID(CharacterID)`, `GetPartyMembers(PartyID)`, `GetMemberAtIndex`, `TryGetParty` return consistent state; join order is preserved across changes (AC-PS-23). `GetPartyMembers` returns an `IReadOnlyList<CharacterID>` of the actual members only (no empty slots), as the `IPartyService` contract requires: a per-party snapshot array replaced (never mutated) on every membership change, so a list handed out earlier never changes and a query allocates nothing; an unknown party returns one shared empty list.
- [x] **AC-6 Lifecycle**: `RegisterPlayer` with `CharacterID.Invalid` or an already-registered character → typed failure, no state change; `UnregisterPlayer(CharacterID)` frees a solo party, or removes a real-party member with compaction and no new solo party and reports the party's PartyID so the caller can notify the others (Story 009 slot release); `DisbandParty(PartyID)` frees a real party and gives every member a fresh solo party (Story 008 cleanup); when the counter is at `uint.MaxValue` an allocation logs one error and returns a typed failure (never wraps to 0) (OQ-PS-2).

---

## Implementation Notes

- New `PartyRegistry` (state) inside `src/ServerLogic/PartySystem/`; `PartyState` holds PartyID, `IsSoloParty`, state enum (`Forming`, `Active`, `Disbanding`, `Ghost`), member `CharacterID[MaxPartySize]`, `EntityID[]`, `LeaderIndex`, `RrNextIndex`, `SequenceId`.
- Counter overflow (OQ-PS-2): per-process `uint` counter; parties are zone-local and die with the process; log an error and refuse allocation at `uint.MaxValue` (never wrap to 0). Record the decision in the GDD open question at story-done.
- GDD leftovers: CR-PS-8 "6-slot" and the `rrNextIndex` "slot index 5" edge case mean 4 — fix the GDD text at story-done.
- Leader transfer, status and the cursor are Stories 003/004; here `RemoveMember` only compacts (leader index handling arrives in 003 and must be tested there).
- Reuse the existing id types: `IronGrind.LootTableSystem.PartyID` (`uint`, `PartyID.Uninitialized` = 0), `IronGrind.Currency.CharacterID`, `IronGrind.CharacterStats.EntityID`.
- The constructor takes `PartySystemConfigValues` and throws `ArgumentException` when `Validate()` fails (as `DeathAndRespawnService`); member arrays are sized from `MaxPartySize`.
- Failures are a typed result enum (e.g. `PartyRegistryResult`: `Ok`, `PartyFull`, `AlreadyInParty`, `SoloPartyCannotAccept`, `UnknownParty`, `UnknownCharacter`, `NotRegistered`, `AlreadyRegistered`, `InvalidCharacter`, `NotSolo`, `NotMember`, `IdsExhausted`), not exceptions.
- Counter exhaustion is tested through an internal seam that sets the next id (e.g. `SetNextPartyIdForTests`).

---

## Out of Scope

- Story 003: leader index rules, round-robin cursor, `IPartyService`
- Story 004: member status
- Story 007: invites (this story exposes the direct add only)

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-18; refine at `/story-readiness`.*

- **AC-1**: register 50 players → 50 distinct non-zero ids, all solo.
- **AC-2**: create + add → state Forming then Active; the leader's and the joiner's old solo ids no longer resolve; create by an unregistered or non-solo leader → typed failure.
- **AC-3**: 4 members + add → `PartyFull`, count 4; config `MaxPartySize` 3 → third add (fourth member) refused; member of party X joins Y → `AlreadyInParty`; add to solo → refused; unknown party / character → typed failure, state unchanged.
- **AC-4**: [A, B, C] remove B → [A, C]; B's new solo id is non-zero, `IsSoloParty`, and differs from its old solo id and the party id; remove all → party gone, ids not reused by the next allocation.
- **AC-5**: `GetPartyMembers` order after add / remove, no empty slots; a list taken before a join is unchanged after it and the new list is a different reference; unknown party → empty, same instance twice; GC alloc 0 on repeated query (`GC.GetAllocatedBytesForCurrentThread`).
- **AC-6**: register Invalid / twice → typed failure; unregister solo → id gone; unregister a member of [A, B, C] → [A, C], returns the party id, no solo for the leaver; disband [A, B] → both solo with new distinct ids, party id gone; counter at `uint.MaxValue` → one error logged, typed failure, no id 0.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/PartySystem/PartySystem_Registry_tests.cs` — must exist and pass

**Status**: [x] Created — 51 test methods, passing

---

## Dependencies

- Depends on: Story 001
- Unlocks: Stories 003, 004

---

## Completion Notes

**Completed**: 2026-11-18
**Criteria**: 6/6 passing (all automated)
**Deviations**: Advisory — (1) `PartyRegistryResult` adds `SoloParty` (remove / disband on a solo id) and `InvalidEntity` (`RegisterPlayer` refuses `EntityID.Invalid`); `UnknownCharacter` dropped (unregistered characters report `NotRegistered`). (2) `PartyState.IsFreed` + internal `Free()` (code review): every freed party (leader's / joiner's old solo, last-member removal, unregister, disband) clears its slots, empties `Members` and is flagged, so a held reference cannot look live. (3) Solo parties are created `Active` with a capacity-1 slot array. (4) An `Active` party that drops to one member stays `Active` (pinned by a test; auto-disband is Story 008 AC-3). GDD leftovers fixed: CR-PS-8 "6-slot" → 4-slot, the `rrNextIndex` "slot index 5" edge case → 3, Player Fantasy "six / five" → "four / three"; OQ-PS-2 resolved (per-process counter, never persisted, refuse at `uint.MaxValue`). Carried to Story 003: `LeaderIndex` / `RrNextIndex` are not adjusted on removal.
**Test Evidence**: Logic: `tests/EditMode/PartySystem/PartySystem_Registry_tests.cs` (51 methods); full EditMode `-testCategory "!Database"` 8184/8184 (2026-11-19 00:09Z)
**Code Review**: Complete — CHANGES REQUIRED (3 required + 9 suggestions), all applied ("fix all")
