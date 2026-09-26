# Story 003: Bag-Full Notification & 30-Second Dedup Window

> **Epic**: Inventory System
> **Status**: Ready
> **Layer**: Core
> **Type**: Logic
> **Manifest Version**: 2026-06-28
> **Estimate**: 2 hours

## Context

**GDD**: `design/gdd/inventory-system.md` — Rule 4 (Full Inventory and Blocked Drops)
**Requirement**: `TR-inv-003`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — registry is currently empty; TR-IDs are the epic's placeholders)*

**ADR Governing Implementation**: ADR-010: Event/Messaging Architecture
**ADR Decision Summary**: The bag-full signal is a Tier 2 broadcast (`OnInventoryFull`, `readonly struct` arg carrying `CharacterID`) consumed by the network layer (which sends the 0-byte `InventoryFullNotification` wire message) and later the Inventory UI. Inventory does not send wire messages itself.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: Plain C#. The 30s window must use an **injected time source** (e.g. an `IServerClock`/`Func<double>` seconds provider) — never `Time.time`/`DateTime.Now` directly — so tests are deterministic (coding-standards: no time-dependent assertions).

**Control Manifest Rules (Core layer)**:
- Required: `readonly struct` event args, `On`-prefixed names — ADR-010
- Forbidden: lambda captures for persistent subscriptions — ADR-010

---

## Acceptance Criteria

*From GDD `design/gdd/inventory-system.md`, scoped to this story:*

- [ ] **AC-INV-1**: With a full inventory (20/20, no partial stack for the incoming item), a drop returns `PickupResult(fail)`, no slot is mutated, and `OnInventoryFull` fires for that character — unless within the 30-second dedup window.
- [ ] **AC-INV-15**: With all 20 slots occupied, a Bronze Sword (Equipment) pickup returns `PickupResult(fail)`, no slot is mutated, and `OnInventoryFull` fires (subject to the dedup window).
- [ ] **Rule 4.10 dedup**: at most one `OnInventoryFull` per character per 30-second window; further blocked pickups inside the window return fail but do not fire the event. A successful pickup resets the window (the next blocked pickup fires immediately).
- [ ] **Rule 4.11 per-character isolation**: one character's full bag and dedup window never affect another character's pickups or notifications.
- [ ] **Tuning**: the 30-second window is a named constant/config value, not an inline literal.

---

## Implementation Notes

- Hook into Story 002's fail path: every `Pickup` that fails *because no space exists* (Rule 3 Step 3) is a "blocked drop". A pickup rejected for `quantity == 0` or an unknown ItemID is **not** a blocked drop and must not fire `OnInventoryFull`.
- Per-character state: `lastFullNotificationTime` (nullable — none yet). Fire when none, or `now − last ≥ 30s`; then set `last = now`. On any successful pickup, clear it.
- Boundary: exactly 30.0s after the last notification → fires (window is `[t, t+30)`).
- The persistent HUD bag-full indicator is UI-owned; it derives from `IsFull` (Story 001) + `OnInventoryChanged`, not from this event.

---

## Out of Scope

- `InventoryFullNotification` wire codec and sending (Networking)
- HUD bag-full indicator and toast (future Inventory UI epic)
- Drop fate of the blocked item (Loot Table System)

---

## QA Test Cases

**File**: `tests/EditMode/InventorySystem/InventorySystem_BagFullNotification_tests.cs`
*(Fake clock injected; all times in seconds)*

- **AC-INV-1** — Given 20 occupied non-potion slots, t=0; When `Pickup(HPPotion,1)`; Then fail, 20 slots unchanged, `OnInventoryFull` fired once with the right `CharacterID`, no `OnInventoryChanged`.
- **AC-INV-15** — Given 20 occupied slots; When `Pickup(BronzeSword,1)`; Then fail, no mutation, `OnInventoryFull` fired once.
- **Dedup window** — t=0 blocked → fires; t=10 blocked → no fire; t=29.9 blocked → no fire; t=30.0 blocked → fires (count 2). Each blocked call still returns fail.
- **Window reset** — t=0 blocked → fires; free a slot (seed), t=5 successful pickup; refill (seed), t=6 blocked → fires immediately (count 2).
- **Per-character isolation** — character A blocked at t=0 (fires) → character B blocked at t=1 → fires for B (A's window irrelevant).
- **Not a blocked drop** — `Pickup(HPPotion, 0)` on a full bag → fail, `OnInventoryFull` NOT fired.
- **Full bag but mergeable** — 20 occupied incl. HP Potion at 50/99 → `Pickup(HPPotion,1)` succeeds, no `OnInventoryFull` (F-INV-3 note).

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/InventorySystem/InventorySystem_BagFullNotification_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: Story 002 (pickup fail path)
- Unlocks: None
