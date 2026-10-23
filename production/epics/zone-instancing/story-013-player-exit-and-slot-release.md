# Story 013: Player Exit and Slot Release (CR-ZI-14 exit, AC-ZI-13)

> **Epic**: Zone Instancing
> **Status**: Ready — written 2026-10-22
> **Layer**: Core
> **Type**: Integration
> **Manifest Version**: 2026-10-19
> **Estimate**: 3 hours (provisional — confirm at `/story-readiness`)

## Context

**GDD**: `design/gdd/zone-instancing.md` — CR-ZI-14, EC-ZI-11, T-5 / T-7 triggers, AC-ZI-13, AC-ZI-22 (release side); `networking-session.md` ST-NET-2 (occupancy rules: disconnect, ghost TTL expiry, reconnect)
**Requirement**: `TR-zi-014` *(epic placeholder)*

**ADR Governing Implementation**: ADR-010 (Party by direct call); ADR-011 (exit save tracked)
**ADR Decision Summary**: the session layer reports exits; Zone Instancing releases the slot, notifies Party once, and the occupancy change drives the lifecycle.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: none.

**Control Manifest Rules (this layer)**:
- Required: direct calls on injected interfaces — ADR-010
- Forbidden: `await` in tick-driven code — ADR-011
- Required: `IronGrind.ServerLogic` — ADR-012

---

## Acceptance Criteria

- [ ] A slot-release entry point (for example `ReleasePlayer(EntityID, ExitCause)`) for the two non-teardown causes: voluntary logout (explicit disconnect) and `SESSION_TTL_SECONDS` expiry of a ghost. It frees the slot (Story 002), removes the connection directory entry, and calls `NotifyZoneExit(PartyID, EntityID)` once for a party member — after that character's exit save has completed (same ordering rule as CR-ZI-12 step 5).
- [ ] **AC-ZI-13**: for each single exit cause (logout, TTL expiry, teardown), a counting party fake records exactly 1 `NotifyZoneExit` for that EntityID — never 0, never 2. (The teardown case runs Story 012's steps.)
- [ ] A release for an EntityID that holds no slot is a no-op (no second notification) — EC-ZI-11.
- [ ] The release raises the occupancy change, so the lifecycle (Story 001) fires T-5 on the last release while Draining and starts the idle timer on the last release while Active.
- [ ] **AC-ZI-22 (release side)**: a ghost whose reconnect arrives while the zone is Draining is released here with its final save (it is re-routed by the gateway, `auth-wire-messages.md`; the zone process only releases it).

---

## Implementation Notes

- **From Story 010 readiness (2026-10-23):** on every slot release call `ZoneConnectionCharacterDirectory.Remove(clientId)` and `ZoneTickRoster.Deactivate(entityId)` (Story 010), and `IPartyZonePresence.NotifyZoneExit` only when `TryGetFormalParty` is true (solo pseudo-party: no call).
- Who calls the entry point: the session layer's existing disconnect / TTL handling (`ConnectionStateMachine`, ghost TTL) — readiness lists the call sites; wiring is in Story 014 if they live in the composition.
- The exit save itself (`SaveSession(Logout)` / TTL snapshot save) is Character Persistence Story 010's session-end triggers — this story orders `NotifyZoneExit` after it, it does not re-implement it.

---

## Out of Scope

- Story 012: teardown exits
- Zone transfers (CR-ZI-16, deferred)

---

## QA Test Cases

- **AC-ZI-13 logout** — party member logs out → 1 `NotifyZoneExit`, after the save fake completes.
- **AC-ZI-13 TTL** — ghost TTL expires → 1 `NotifyZoneExit`.
- **AC-ZI-13 teardown** — with Story 012 → 1.
- **Double release** — release twice → 1 notification.
- **Lifecycle** — Draining with 1 slot; release → T-5 on that tick. Active with 1 slot; release → idle timer starts.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/ZoneInstancing/ZoneInstancing_PlayerExit_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Stories 001, 002, 010
- Unlocks: Story 014
