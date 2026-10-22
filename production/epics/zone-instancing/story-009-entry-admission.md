# Story 009: Player Entry, Part 1 — Admission (CR-ZI-8 steps 3–5, CR-ZI-7)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 4 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-7, CR-ZI-8 steps 3–5, EC-ZI-1, EC-ZI-4, AC-ZI-4, AC-ZI-27
**Requirement**: `TR-zi-006`, `TR-zi-007` *(epic placeholders)*

**ADR Governing Implementation**: ADR-014 (connection-level `SessionHandshake`); ADR-011 (persistence task tracked, never awaited on the tick); ADR-006 (`LoadCharacter`)
**ADR Decision Summary**: the handshake is handled per connection; `LoadCharacter` is started as a `Task` and passed to `ITickCompletionQueue.Track`; its continuation runs on the main thread, where the state and capacity check and the slot write happen together.

**Engine**: Unity 6.3 LTS | **Risk**: MEDIUM (persistence task completion on the main thread)
**Engine Notes**: none beyond ADR-011.

**Control Manifest Rules (this layer)**:
- Forbidden: `await`, `async` methods, `.Result`, `.Wait()`, `ContinueWith` in tick-driven code — start the `Task` and `Track` it — ADR-011
- Required: zone state mutated only on the main thread — ADR-011
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] **Seam (user decision 2026-10-22)**: the handler takes an already-decoded entry request (connection id, `AccountID`, `CharacterID`, target `ZoneID`, session token). The `SessionHandshake` codec is not part of this story (schema pending OQ-NC-SER-2).
- [ ] **Token check (user decision 2026-10-22)**: through an injected validator interface; its stand-in rejects every token and logs "[ZoneServer] zone entry token validation not implemented" once (until the gateway / Authentication work defines cross-process validation). A rejected token sends no slot and no `LoadCharacter` (reply per open decision 1).
- [ ] **Step 3**: `LoadCharacter` started and tracked; `LoadCharacterCode != Success` → `ZoneFullResponse(LoadCharacterFailed, retryAfterSeconds 0)`, abort.
- [ ] **Step 4 / EC-ZI-1 / AC-ZI-27**: when the load completes, if the zone is not Active (Loading, Draining or Closed) → `ZoneFullResponse(ZoneDoesNotExist)`, no slot, no EntityID, no `SaveSession`; if at cap → `ZoneFullResponse(ZoneFull, retryAfterSeconds 30)`, no slot.
- [ ] **AC-ZI-4 (integration)**: two handshakes whose loads complete in the same tick against 49 / 50 → one slot, one `ZoneFull`; occupied count 50.
- [ ] **EC-ZI-4**: the losing join's loaded data is discarded; no persistence call follows.
- [ ] **Step 5**: on success, EntityID minted and the slot written with `CharacterID` and `Connected`; the admitted player is handed to Story 010's continuation.
- [ ] A handshake for a `ZoneID` that is not this zone → `ZoneDoesNotExist`.

---

## Implementation Notes

- `retryAfterSeconds` default 30 for `ZoneFull` as a `public const` knob through the constructor.
- A connection that drops while its load is pending: the continuation finds no connection and discards the result (no slot).

**Open decisions for `/story-readiness`**

1. Reply on a rejected token: `ZoneFullResponse(Other)`, or close the connection silently. The GDD lists no `JoinRejectedReason` for auth failure.
2. A second handshake from the same connection while its first load is pending: ignore, or replace.

---

## Out of Scope

- Story 010: steps 6–10 (snapshot, `SessionReady`, party, `PlayerJoinedZone`, tick loop) and the connection-directory fill
- Story 011: duplicate-`CharacterID` handshake (stale slot)
- The `SessionHandshake` codec and the real token validator

---

## QA Test Cases

- **LoadCharacterFailed** — fake persistence returns a failure → `ZoneFullResponse(LoadCharacterFailed, 0)`; no slot.
- **AC-ZI-27** — load pending; zone moves to Draining; load completes → `ZoneDoesNotExist`; no slot, no EntityID, no `SaveSession`.
- **AC-ZI-4** — 49 / 50; two loads complete in one tick → one admitted, one `ZoneFull(30)`; count 50.
- **Token stand-in** — default composition → rejected, log once.
- **Wrong zone** — target `ZoneID` ≠ this zone → `ZoneDoesNotExist`.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_EntryAdmission_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 006
- Unlocks: Stories 010, 011
