# Story 006: NPC Interaction Session

> **Epic**: Enhancement System
> **Status**: Ready
> **Layer**: Feature
> **Type**: Integration
> **Manifest Version**: 2026-06-28
> **Estimate**: 3 hours

## Context

**GDD**: `design/gdd/enhancement-system.md` — CR-ENH-16 (town hub only), CR-ENH-17 (session flag lifecycle, 300 s wall-clock lifetime, in-flight attempts), AC-ENH-27, 28, 29, 30, 39. `design/gdd/npc-shop.md` — CR-SHOP-3 (the same `NPCInteractionActive` flag: opening any NPC clears an existing session silently; close triggers). `design/gdd/networking-wire-protocol.md` — `OpenNPCInteraction { npcId }`, `NPCInteractionOpened`, `RejectedNotInTownHub`, `CloseNPCInteraction` (already defined in the NPC Shop message set — not part of TD-046).
**Requirement**: `TR-enh-008`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture (interface dependency; zone-scoped state cleared through an explicit call, not a global bus).
**ADR Decision Summary**: Systems are injected through interfaces and call each other directly; no central event bus.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#; EditMode-testable with an injected clock. No engine time API in the logic.

**Control Manifest Rules (Feature layer)**:
- Required: interface dependency — ADR-010
- Forbidden: no `EventBus` class — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/enhancement-system.md`, scoped to this story:*

- [ ] **AC-ENH-28 (open in the town hub)**: the player is in the town hub with no session → opening an interaction with the Enhancement NPC reports `NPCInteractionOpened` and the session is active.
- [ ] **Not in the town hub**: opening from any other zone reports `RejectedNotInTownHub` and the session stays inactive.
- [ ] **AC-ENH-29 (close clears)**: with an active session, closing it makes the session inactive; a following `ConfirmEnhancement` returns `RejectedNoNPCSession`.
- [ ] **AC-ENH-30 (zone transition clears)**: with an active session, a zone transition makes it inactive without an explicit close; a following `ConfirmEnhancement` returns `RejectedNoNPCSession`.
- [ ] **AC-ENH-39 (wall-clock lifetime)**: a session opened at time T is inactive once the clock passes T + `SESSION_TTL_SECONDS` (300), whatever the client did in between; `ConfirmEnhancement` then returns `RejectedNoNPCSession`, locks nothing and consumes nothing. Only a new successful open starts a new lifetime.
- [ ] **AC-ENH-27 (no session)**: `ConfirmEnhancement` with no prior open returns `RejectedNoNPCSession` (end to end with the real session tracker; Story 003 covers the same code with a stub).
- [ ] **Re-open replaces**: opening an interaction while a session is already active (any NPC) clears the old session silently and starts a new one with a fresh lifetime (npc-shop.md CR-SHOP-3).
- [ ] **In-flight attempts are not cancelled**: closing, expiring or replacing the session after an attempt has passed validation does not affect that attempt — it runs to completion and its result is produced.
- [ ] **Disconnect clears**: the session is cleared when the player's session ends.

---

## Implementation Notes

- One per-player session tracker implementing the consumer-side query Story 003 declared (`IsActive(CharacterID)`), plus `Open(CharacterID, uint npcId)`, `Close(CharacterID)`, and notifications for zone transition and disconnect.
- **The flag is shared with the NPC Shop** (one flag per player for all NPCs). No NPC Shop code exists yet, so this story writes the tracker; the NPC Shop epic will consume the same instance. Where the type should live so both systems can reference it (this module, or a shared namespace) is not stated in either GDD — raise at `/story-readiness`.
- **Clock is injected** (`Func<…>` returning wall-clock seconds, or an equivalent seam). The lifetime is measured from the moment the open succeeds and is never extended by client activity. `SESSION_TTL_SECONDS = 300` already exists as a constant in `IronGrind.Networking`'s `CommitBeforeBroadcastSequencer` ("provisional — no other `src/` code owns it yet"); the GDD says the session lifetime is the same value. Reuse or re-home the constant rather than declaring a second 300 — note which at readiness.
- **Expiry can be lazy**: the GDD requires the flag to be false after the lifetime, and it is read only at `ConfirmEnhancement` validation, so checking the deadline on read satisfies every criterion. A ticking sweep is not required.
- **Town hub check is consumer-side**: Zone Instancing has no code. Declare a small query in this module (e.g. "is this character in the town hub") and stub it in tests.
- **`npcId`** is carried by `OpenNPCInteraction`. Neither GDD says how the server tells an Enhancement NPC from a shop NPC, or what happens for an unknown id. Raise at `/story-readiness`.
- The session is read only at validation (CR-ENH-17 "In-flight attempts"): do not add a callback that cancels an attempt.
- The wire handlers for the four messages are not in this story; it provides the server-side behaviour they will call.

---

## Out of Scope

- Story 003: the validation step that reads the session
- Story 010: wire handlers for `OpenNPCInteraction` / `CloseNPCInteraction` and their responses
- NPC Shop epic: shop-side use of the same session (`BuyRequest` / `SellRequest` validation)
- Zone Instancing epic: the real town hub query and the zone-transition notification source

---

## QA Test Cases

**File**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_NpcInteractionSession_integration_tests.cs` (new). Injected clock; town hub stub; real `EnhancementService` and `InventoryService` for the `ConfirmEnhancement` checks.

- **AC-ENH-28** — in town hub, open → `NPCInteractionOpened`; `IsActive` true.
- **Not in town hub** — open → `RejectedNotInTownHub`; `IsActive` false.
- **AC-ENH-29** — open, close → inactive; valid `ConfirmEnhancement` → `RejectedNoNPCSession`, nothing locked or consumed.
- **Close with no session** — no error, still inactive.
- **AC-ENH-30** — open, zone-transition notification → inactive; `ConfirmEnhancement` → `RejectedNoNPCSession`.
- **AC-ENH-39** — open at T; clock at T + 299 → active; at T + 300 → boundary as the GDD states it ("past T + `SESSION_TTL_SECONDS`": active at exactly T + 300, inactive after — confirm the boundary at readiness); at T + 301 → inactive, `ConfirmEnhancement` → `RejectedNoNPCSession`.
- **Activity does not extend** — repeated `IsActive` reads and close-less client activity between T and T + 301 → still expires.
- **Re-open restarts** — open at T, open again at T + 200 → active at T + 450, inactive after T + 500.
- **AC-ENH-27** — no open at all → `RejectedNoNPCSession`.
- **In-flight attempt** — commit held open (Story 005's fake), then close the session → the attempt completes with its normal result.
- **Disconnect** — open, disconnect notification → inactive.
- **Two players** — one player's close or expiry does not affect another player's session.

---

## Test Evidence

**Story Type**: Integration
**Required evidence**: `tests/EditMode/Integration/EnhancementSystem/Enhancement_NpcInteractionSession_integration_tests.cs` — must exist and pass.

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 003 (the session query it implements). The in-flight-attempt case also needs Story 005.
- Unlocks: Story 010 (wire handlers); NPC Shop epic (shared session)
