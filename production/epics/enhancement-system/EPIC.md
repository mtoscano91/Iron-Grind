# Epic: Enhancement System

> **Layer**: Feature
> **GDD**: design/gdd/enhancement-system.md
> **Architecture Module**: Enhancement (Feature layer; "irreversible outcome — commit before broadcast" data flow in `architecture.md`)
> **Status**: In Progress (7/11 — Stories 001–007 Complete 2026-10-07; 1 story Ready (008); 3 Blocked: Story 009 on OQ-ENH-7, Story 010 on TD-046, Story 011 on the tick-loop/async-persistence decision and Character Persistence)
> **Stories**: 10 stories created 2026-10-07 (001–010); Story 011 added the same day when Story 005 was split at its readiness check

> **Created ahead of the GDD's stated gate (user decision 2026-10-07).** The GDD header says OQ-ENH-7 and the wire-protocol Enhancement message set (TD-046) should close before `/create-epics`. The epic was created anyway so that the formula and bonus-provider work, which neither gate touches, can be planned alongside the Equipment epic. Every requirement the gates affect is marked below.

## Overview

The Enhancement System is the attempt-based upgrade engine for equippable items. At the Enhancement NPC in the town hub a player selects an item in their bag, spends one Enhancement Scroll of the matching gear tier, and the server resolves a probability-weighted attempt: on success the item's level rises by one; on failure the item is destroyed. The scroll is consumed either way. Levels +1 to +5 close the stat gap to the next gear tier; +6 and above are prestige. The system owns the bonus formulas other systems read (`IEnhancementBonusProvider`), the probability table, the attempt sequence with its commit-then-deliver rule and caller-owned rollback, the prestige band thresholds, and the server-wide broadcast at +9.

**Depends on**: Item Database (Complete — including the four Enhancement Scroll records and `ScrollData`, Item Database Stories 005–006, 2026-10-02), Inventory System (Complete 2026-10-07 — `LockSlot`, `UnlockSlot`, `SetEnhancementLevel`, `RemoveItem`, `ConsumeItem`, `ForceInsert` with a level), Currency System (Complete — no direct call at MVP), Character Persistence (`SaveIrreversibleOutcome` — **no epic exists yet**). **Mutual dependency with the Equipment System** (sibling epic): Equipment consumes `IEnhancementBonusProvider` and the prestige thresholds; this system relies on Equipment to store the level while an item is equipped and to encode the prestige band.

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
| TR-enh-004 | Attempt validation with no state change on rejection: item present and unlocked, `IsUpgradeable`, not a Ring / Necklace, below the maximum level, scroll present with `TargetGearTier` matching the item's `GearTier`, no concurrent attempt, NPC session active (CR-ENH-2 to CR-ENH-5, CR-ENH-8, CR-ENH-15 step 2) | ❌ No ADR (design-only, LOW risk) |
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
- **Character Persistence has no epic** — `SaveIrreversibleOutcome` and the CR-CP-5 failure protocol are designed, not implemented. Story 005 works against a consumer-side interface and a fake; AC-ENH-2 (level persists across sessions) is deferred to the Character Persistence epic.
- **Unblocked now**: Stories 001–008. Story 002 (the bonus provider) is also what the Equipment epic needs first.
- *Corrected 2026-10-07 at story creation:* this section first listed "Item Database Amendment #4 code follow-up" as a blocker. That was wrong — the scroll records and `ScrollData` are in code (Item Database Stories 005–006, Complete 2026-10-02). The "code follow-up story needed" notes in `enhancement-system.md` and `systems-index.md` are stale.
- **Not blocking**: OQ-ENH-1 (scroll prices, playtest), OQ-ENH-4 and OQ-ENH-5 (deferred to social and UX design), OQ-ENH-8 (result replay at login, deferred to Enhancement UI design).

## Definition of Done

This epic is complete when:
- All stories are implemented, reviewed, and closed via `/story-done`
- All acceptance criteria from `design/gdd/enhancement-system.md` (AC-ENH-1 to AC-ENH-39) are verified
- All Logic and Integration stories have passing test files in `tests/`
- Both gates above are closed and the stories they blocked are implemented, not left as placeholders
- All Visual/Feel and UI evidence belongs to the Enhancement UI, VFX and Audio epics, not this one

## Stories

| # | Story | Type | Status | ADR |
|---|-------|------|--------|-----|
| 001 | [Enhancement Config — Level Cap, Thresholds, Probability Table, Prestige Band](story-001-enhancement-config.md) | Logic | Complete | None (design-only) |
| 002 | [Enhancement Bonus Provider](story-002-enhancement-bonus-provider.md) | Logic | Complete | ADR-010 |
| 003 | [Attempt Validation and Result Codes](story-003-attempt-validation.md) | Integration | Complete | ADR-010 |
| 004 | [Attempt Sequence and Outcome Resolution](story-004-attempt-sequence.md) | Integration | Complete | ADR-010 |
| 005 | [Rollback and the Pending Window](story-005-commit-and-rollback.md) | Integration | Complete | ADR-010 |
| 006 | [NPC Interaction Session](story-006-npc-interaction-session.md) | Integration | Complete (2026-10-07) | ADR-010 |
| 007 | [Outcome Events and +9 Broadcast Trigger](story-007-outcome-events-and-broadcast-trigger.md) | Logic | Complete | ADR-010 |
| 008 | [Scroll Source Restriction Scan](story-008-scroll-source-restriction.md) | Logic | Ready | None (design-only) |
| 009 | [Attempt Exclusivity — Held Requests](story-009-attempt-exclusivity.md) | Integration | **Blocked** — OQ-ENH-7 | None yet |
| 010 | [Client Requests and Result Delivery](story-010-client-requests-and-result-delivery.md) | Integration | **Blocked** — TD-046 | ADR-004 |
| 011 | [Commit Orchestration](story-011-commit-orchestration.md) | Integration | **Blocked** — tick-loop/async-persistence decision (ADR), Character Persistence, TD-046 for the client-facing criteria | ADR-006 |

**Order**: 001 first; 002 and 003 need 001; 004 needs 003; 005 and 007 need 004 (007 also needs 005 for its "nothing on a rollback" check); 006 needs 003; 008 is independent; 011 needs 005 and its blockers; 009 and 010 follow 011.

**GDD AC coverage**: 30 of the 39 acceptance criteria are assigned to these stories. The other nine are owned elsewhere:
- **AC-ENH-1** (new item starts at level 0) — already verified by Inventory System Story 010.
- **AC-ENH-2** (level persists across sessions) — deferred to the Character Persistence epic.
- **AC-ENH-15, 16, 17, 32** (prestige bits in `equipmentAppearanceFlags`) — the byte is written by the Equipment System; Story 001 covers the level-to-band mapping, the end-to-end check belongs to the Equipment epic (TR-equip-007).
- **AC-ENH-25, 26, 31** (probability display, heightened warning, acknowledgment) — deferred to the Enhancement UI epic (GDD not yet authored).

Assigned to Blocked stories: **AC-ENH-38** (Story 009), **AC-ENH-6** (Story 010) and **AC-ENH-13** (Story 011). **AC-ENH-23, 34 and 35** are split: their bag-state halves are in Story 005 (Ready), their commit halves in Story 011 (Blocked). Story 010 holds the delivery half of **AC-ENH-18**, whose server-side half is in Story 007. **AC-ENH-8** was moved from Story 005 to Story 004 and is Complete.

**Open points recorded in the stories for `/story-readiness`**:
- Story 011 (split out of Story 005 on 2026-10-07) — how server tick-loop code consumes the asynchronous `SaveIrreversibleOutcome`. `EnhancementService` is two-phase and synchronous (`BeginAttempt` → the caller commits → `CompleteAttempt` or `RollBackAttempt`) and never calls persistence; the existing `CommitBeforeBroadcastSequencer` is synchronous and would block the tick for the write. Needs a decision, probably an ADR, shared with OQ-ENH-7 and the other irreversible outcomes (level-up, respec, item consumption).
- Story 007 — *decided 2026-10-07 at readiness:* `OnPrestigeBandChange` is not implemented in this epic (the visible band only changes on equip, which the Equipment System does); the GDD rows naming it (Interactions and Downstream Dependencies, VFX) should move to the Equipment System when `enhancement-system.md` is next edited. The +9 trigger carries ids, and Story 010 resolves the display names. Also noted: the control manifest asks for event argument types in a shared `IronGrind.Events` namespace that no code uses.
- Story 006 — *decided 2026-10-07 at readiness:* the shared tracker and `INpcInteractionSessions` live in the neutral `IronGrind.NpcInteraction` namespace; `npcId` is stored and not validated (telling NPC types apart and rejecting an unknown id are deferred to Story 010 / NPC authoring — TD-058, npc-shop.md OQ-NS-1).
- Stories 002, 003, 004 — inputs the GDD gives no rule for (out-of-range level or slot index, non-equipment item in the item slot, an inventory call failing mid-attempt).
- Story 008 — whether production loot tables exist yet, and whether a scroll can be reached through a consumable pool.
- Story 010 — no request triggers `EnhancementStateUpdate`.

## Next Step

Stories 001–007 are Complete (2026-10-07) — config, bonus provider, validation, the two-phase attempt sequence, the rollback, the outcome events and the NPC session are in code. Next: `/story-readiness` then `/dev-story` for Story 008 (scroll source scan) — the last Ready story. To unblock 009, 010 and 011: one architecture decision on asynchronous persistence in the tick loop (covers OQ-ENH-7 too), the wire-protocol authoring session for TD-046, and a Character Persistence epic.
