# Epic: Enhancement System

> **Layer**: Feature
> **GDD**: design/gdd/enhancement-system.md
> **Architecture Module**: Enhancement (Feature layer; "irreversible outcome — commit before broadcast" data flow in `architecture.md`)
> **Status**: Ready — **attempt-path stories blocked on two open gates: OQ-ENH-7 and TD-046**
> **Stories**: Not yet created — run `/create-stories enhancement-system`

> **Created ahead of the GDD's stated gate (user decision 2026-10-07).** The GDD header says OQ-ENH-7 and the wire-protocol Enhancement message set (TD-046) should close before `/create-epics`. The epic was created anyway so that the formula and bonus-provider work, which neither gate touches, can be planned alongside the Equipment epic. Every requirement the gates affect is marked below.

## Overview

The Enhancement System is the attempt-based upgrade engine for equippable items. At the Enhancement NPC in the town hub a player selects an item in their bag, spends one Enhancement Scroll of the matching gear tier, and the server resolves a probability-weighted attempt: on success the item's level rises by one; on failure the item is destroyed. The scroll is consumed either way. Levels +1 to +5 close the stat gap to the next gear tier; +6 and above are prestige. The system owns the bonus formulas other systems read (`IEnhancementBonusProvider`), the probability table, the attempt sequence with its commit-then-deliver rule and caller-owned rollback, the prestige band thresholds, and the server-wide broadcast at +9.

**Depends on**: Item Database (Complete for gear; **the four Enhancement Scroll records and `ScrollData` from Amendment #4 are designed but not in code — follow-up Item Database story not yet created**), Inventory System (Complete 2026-10-07 — `LockSlot`, `UnlockSlot`, `SetEnhancementLevel`, `RemoveItem`, `ConsumeItem`, `ForceInsert` with a level), Currency System (Complete — no direct call at MVP), Character Persistence (`SaveIrreversibleOutcome` — **no epic exists yet**). **Mutual dependency with the Equipment System** (sibling epic): Equipment consumes `IEnhancementBonusProvider` and the prestige thresholds; this system relies on Equipment to store the level while an item is equipped and to encode the prestige band.

## Governing ADRs

| ADR | Decision Summary | Engine Risk |
|-----|-----------------|-------------|
| ADR-010: Event/Messaging Architecture | Direct Tier 1 calls into the injected Inventory service, acting on return values; `On…` events for cross-system notification (e.g. `OnEnhancementCompleted`) | LOW |
| ADR-006: Persistence Layer | One-transaction commit of an irreversible outcome; bag and gear entries carry `enhancement_level` (Amendment 1) | LOW |
| ADR-004: Networking Library (NGO) | Transport for the enhancement request, result and +9 broadcast messages | HIGH (applies to the wire-facing stories only) |
| ADR-007: Hosting Backend | Write latency leaves headroom under `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` (200 ms) | LOW |

## GDD Requirements

| TR-ID | Requirement | ADR Coverage |
|-------|-------------|--------------|
| TR-enh-001 | `IEnhancementBonusProvider`: `GetFlatBonus(level, baseFlatBonus, gearTier)` and `GetElementalBonus(level, baseElementalDamage, gearTier, isWeapon)`, with the tier parity constraint (a +5 item ≈ an unenhanced item of the next tier) (F-ENH-1, F-ENH-2, F-ENH-3) | ADR-010 (interface dependency) |
| TR-enh-002 | Level storage and range: a per-instance `byte`, default 0, valid 0 to `MAX_ENHANCEMENT_LEVEL` (10); not an Item Database field; prestige thresholds `PRESTIGE_MID_THRESHOLD = 5`, `ENHANCEMENT_GLOW_THRESHOLD = 7`, `PRESTIGE_HIGH_THRESHOLD = 8` owned here as tuning data (CR-ENH-1, CR-ENH-2, CR-ENH-12) | ❌ No ADR (design-only, LOW risk) |
| TR-enh-003 | Probability table and resolution: one uniform draw `r ∈ [0, 1)`; success when `r < P_s[k]`, otherwise destruction at every level including +0; the ten `P_s[k]` values are tuning knobs; RNG injectable for tests (CR-ENH-9, CR-ENH-10, F-ENH-4, F-ENH-5) | ❌ No ADR (design-only, LOW risk) |
| TR-enh-004 | Attempt validation with no state change on rejection: item present and unlocked, `IsUpgradeable`, not a Ring / Necklace, below the maximum level, scroll present with `TargetGearTier` matching the item's `GearTier`, no concurrent attempt, NPC session active (CR-ENH-2 to CR-ENH-5, CR-ENH-8, CR-ENH-15 step 2) | ❌ No ADR (design-only, LOW risk) — needs the scroll records in code |
| TR-enh-005 | Attempt sequence: lock the slot, consume one scroll with `ConsumeItem`, draw, apply to the bag (`SetEnhancementLevel` or `RemoveItem`), then unlock; the lock never outlives the attempt (CR-ENH-6, CR-ENH-7, CR-ENH-15 steps 3–7) | ADR-010 (Tier 1 calls) |
| TR-enh-006 | Commit-then-deliver: the outcome becomes durable through one `SaveIrreversibleOutcome` write before any result is sent; on a failed write the caller restores the item (previous level, or `ForceInsert`) and the scroll, unlocks, and logs `CriticalEnhancementWriteFailed` (CR-ENH-11, CR-ENH-15 step 6b and Rollback) | ADR-006 — needs Character Persistence |
| TR-enh-007 | Attempt exclusivity: from the lock until the attempt is idle, no other inventory-mutating request for that character is processed; held requests run in arrival order afterwards (CR-ENH-18) | ❌ **No ADR and no decision — blocked on OQ-ENH-7** |
| TR-enh-008 | NPC interaction session: `NPCInteractionActive` per session, opened only in the town hub, shared with the NPC Shop, 300 s wall-clock lifetime, read only at validation; clearing it never cancels an attempt past validation (CR-ENH-16, CR-ENH-17) | ADR-004 — ⚠️ request messages affected by TD-046 |
| TR-enh-009 | Client messages: `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)`, `CancelEnhancement`, `EnhancementStateUpdate`, `EnhancementAttemptResult { outcome, newLevel, resultCode }`; client-facing inventory changes from an attempt are held until the commit succeeds (CR-ENH-6, CR-ENH-11, CR-ENH-15 step 9) | ADR-004 — ⚠️ **blocked on TD-046** |
| TR-enh-010 | Server broadcast at +9: `ServerBroadcast_Enhancement9 { playerName, itemName }` to all online players, once per successful +9 transition, only after the commit (CR-ENH-14, CR-ENH-15 step 8) | ADR-004 — ⚠️ affected by TD-046 |
| TR-enh-011 | Outcome signals for presentation: `OnEnhancementSuccess(newLevel)`, `OnEnhancementDestruction()`, `OnPrestigeBandChange(band)`; the glow itself is rendered by the VFX System from `equipmentAppearanceFlags` (CR-ENH-13, Downstream Dependencies) | ADR-010 (events) |

> **TR registry note**: All TR-IDs above are placeholders — `docs/architecture/tr-registry.yaml` is empty. Populate the registry before running `/story-readiness` checks.

## Open Gates

- **OQ-ENH-7 (blocks TR-enh-007, and with it the full attempt path TR-enh-005 / TR-enh-006)** — which layer holds a character's other inventory-mutating requests while an attempt is in progress: one check in the session's request dispatcher (needs a Networking Core rule) or a check in each mutating system. The decision must also cover server-originated bag mutations such as the Loot Table System's pickup, which returns a synchronous result and cannot be deferred by a dispatcher. Owner: Lead Programmer / Networking Core. Likely needs an ADR (`/architecture-decision`).
- **TD-046 (blocks TR-enh-009; affects TR-enh-008 and TR-enh-010)** — `networking-wire-protocol.md` still defines an item-id based `EnhancementAttemptRequest` with a "TBD" result body, while the GDD uses the slot-based `ConfirmEnhancement(itemSlotIndex, scrollSlotIndex)`; and no wire message carries inventory slot changes to the owning client. Fix: a wire-protocol authoring session, then a lean re-review.
- **Item Database Amendment #4 code follow-up (blocks TR-enh-004 beyond a stubbed scroll)** — the four scroll records and `ScrollData { TargetGearTier }` are designed (item-database.md Rule 13) but not in code.
- **Character Persistence has no epic (blocks TR-enh-006 beyond an injected fake)** — `SaveIrreversibleOutcome` and the CR-CP-5 failure protocol are designed, not implemented.
- **Unblocked now**: TR-enh-001, TR-enh-002 and TR-enh-003 (pure formulas, constants and the probability table). TR-enh-001 is also what the Equipment epic needs first.
- **Not blocking**: OQ-ENH-1 (scroll prices, playtest), OQ-ENH-4 and OQ-ENH-5 (deferred to social and UX design), OQ-ENH-8 (result replay at login, deferred to Enhancement UI design).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/enhancement-system.md` (AC-ENH-1 to AC-ENH-39) are verified
- All Logic and Integration stories have passing test files in `tests/`
- Both gates above are closed and the stories they blocked are implemented, not left as placeholders
- All Visual/Feel and UI evidence belongs to the Enhancement UI, VFX and Audio epics, not this one

## Next Step

Run `/create-stories enhancement-system` to break this epic into stories — the formula, constant and probability-table stories can be written and implemented now; stories on the attempt path will be created as Blocked until OQ-ENH-7 and TD-046 close.
