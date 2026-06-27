# Resource Display Authority Contract

> **Status**: Active — 2026-06-09
> **Resolves**: OQ-CUS-4 (`design/gdd/consumable-use-system.md`)
> **Owner**: Network Programmer + Lead Programmer
> **Scope**: Local player HP/MP bar display reconciliation

## Problem

Two server-to-client messages both carry authoritative HP values for the local player's resource bars:

| Message | Channel | Delivery | HP/MP Payload |
|---------|---------|----------|---------------|
| `EntityHealthUpdate` | R-U batch | Tick-rate; may be dropped/reordered | `currentHP` (int) — absolute value at tick-flush |
| `UseItemResult` | R-OD | Priority path; guaranteed delivery | `newResourceValue` (int) — absolute value at mid-tick dispatch |

These messages travel different paths. Arrival order at the client is non-deterministic. Without a reconciliation rule the following race condition produces a visible HP bar regression:

1. Server processes `UseItemRequest` mid-tick T: HP 200 → 400. Dispatches `UseItemResult(newResourceValue=400, tick=T)` immediately.
2. Party healer applies heal in the same tick: HP 400 → 500. `EntityHealthUpdate(currentHP=500, tick=T)` emitted at tick-T batch flush.
3. `EntityHealthUpdate(500, T)` arrives first (R-U delivery is not throttled relative to R-OD on the same path).
4. `UseItemResult(400, T)` arrives second — without a rule, it overwrites the EHU. HP bar drops 500 → 400.

## Display Authority Rule

Every message envelope carries `ServerTickNumber` per CR-NET-7.1 (uint, 4 bytes): the server tick during which the message was authored. For R-U batch sub-messages, the tick is the **outer batch envelope's** `ServerTickNumber` — all sub-messages in a single flush share one tick. For R-OD messages, each message has its own envelope with its own tick.

The message handler layer (the component translating incoming messages into local HP/MP state writes) maintains two monotonic counters per local player session:

| Counter | Init | Updated by |
|---------|------|-----------|
| `HPDisplayTick: uint` | 0 on session start | `EntityHealthUpdate` only |
| `MPDisplayTick: uint` | 0 on session start | Future MP-bearing batch message, if any |

### Rule A — EntityHealthUpdate

```
OnEntityHealthUpdate(entityId, currentHP, batchTick):
    if batchTick >= HPDisplayTick:
        WriteLocalHP(currentHP)
        HPDisplayTick = batchTick
    // batchTick < HPDisplayTick: stale — discard
```

### Rule B — UseItemResult (RestoreHP)

```
OnUseItemResult(newResourceValue, messageTick) where effectType == RestoreHP:
    if messageTick > HPDisplayTick:          // strictly greater — see rationale
        WriteLocalHP(newResourceValue)
        // HPDisplayTick is NOT updated by UseItemResult
    // messageTick <= HPDisplayTick: EHU from same or later tick already applied; discard HP payload
    // Cooldown and inventory updates from UseItemResult are ALWAYS applied — this rule governs HP display only
```

### Rule C — UseItemResult (RestoreMP)

```
OnUseItemResult(newResourceValue, messageTick) where effectType == RestoreMP:
    if messageTick > MPDisplayTick:
        WriteLocalMP(newResourceValue)
        // MPDisplayTick is NOT updated by UseItemResult
    // else: discard MP payload
```

### Why strictly greater (`>`) for UseItemResult

`UseItemResult` is dispatched the moment the server finishes processing `UseItemRequest` — before the tick loop has finished applying all tick-T effects (including concurrent healer heals or other HP mutations in the same tick). `EntityHealthUpdate` is dispatched at tick-T flush, after all tick-T effects are resolved. For the same tick T:

- `UseItemResult.HP` = HP immediately after potion (mid-tick snapshot)
- `EntityHealthUpdate.HP` = HP after all tick-T effects resolved (tick-end snapshot)

Using `>` ensures a tick-T `EntityHealthUpdate` — which carries the more complete value — supersedes `UseItemResult` for the same tick, regardless of arrival order. Because `HPDisplayTick` is never advanced by `UseItemResult`, a same-tick EHU arriving after a UseItemResult still passes the `>= HPDisplayTick` check in Rule A and applies correctly.

## Scenario Traces

| Scenario | Event sequence | Expected result | Rule outcome |
|----------|---------------|-----------------|-------------|
| OQ-CUS-4 bug case: EHU(500,T) then UseItemResult(400,T) | EHU: T≥0 → apply 500, HPDisplayTick=T. UseItemResult: T NOT > T → discard HP. | 500 ✓ | **Bug fixed** |
| Ideal order: UseItemResult(400,T) then EHU(500,T) | UseItemResult: T>0 → apply 400. EHU: T≥T → apply 500, HPDisplayTick=T. | 500 ✓ (self-corrects) | No regression |
| Solo play, no healer: UseItemResult(400,T) | UseItemResult: T>0 → apply 400. HPDisplayTick stays 0. | 400 ✓ | Rule is a no-op |
| Stale EHU from prior tick arrives late | EHU: T-2 NOT ≥ HPDisplayTick(T) → discard. | No regression ✓ | Stale-discard |
| Stale UseItemResult discarded | HPDisplayTick=T (EHU already applied). UseItemResult: T NOT > T → discard. | No regression ✓ | Stale-discard |
| Disconnect / zone entry | HPDisplayTick and MPDisplayTick reset to 0. SessionHandshake delivers authoritative HP/MP before any tick messages are processed. | Authoritative state ✓ | Counter reset |

## Scope

This contract governs **local player HP/MP display only**:

- ✓ Local player HP bar — `EntityHealthUpdate` vs `UseItemResult` conflict
- ✓ Local player MP bar — `UseItemResult` vs any future MP-bearing batch message
- ✗ Party member HP/MP — governed by `PartyMemberHealthUpdate` alone (no competing R-OD source for party-member display)
- ✗ Enemy/NPC HP — target HUD not driven by `UseItemResult`
- ✗ Inventory quantity display — always from `UseItemResult.newInventoryQuantity`; no competing source
- ✗ Cooldown state — always from `UseItemResult`; no competing source

## Implementation Notes

**1. WriteLocalHP / WriteLocalMP target:** These write to the local HP/MP display cache — whatever component drives the HUD's `OnStatChanged` event per `character-stats.md`. The stale-discard logic runs in the message handler layer, not the HUD render layer.

**2. Counter reset:** `HPDisplayTick` and `MPDisplayTick` must reset to 0 on every zone entry, including same-zone reconnect. Consistent with CUS cooldown reset (Rule 5 in `consumable-use-system.md`). The SessionHandshake delivers authoritative HP/MP before any tick-rate messages are processed in the new zone session — ensuring the first EHU or UseItemResult applied post-reset is always valid.

**3. R-U batch tick extraction:** EntityHealthUpdate sub-messages carry no individual `ServerTickNumber`. Extract the tick from the **outer batch envelope** before iterating sub-messages; apply the same tick value to all sub-messages in the same flush.

**4. HPDisplayTick is not a general-purpose sequence counter.** It tracks only the latest EHU-applied tick and is used solely to guard UseItemResult application. It does not gate other Character Stats writes (stat changes, level-up effects, etc.).

## Acceptance Criteria

**AC-RDA-01 — EHU from same tick supersedes UseItemResult when EHU arrives first**
Given HPDisplayTick=0, when EHU(currentHP=500, tick=T) is processed then UseItemResult(newResourceValue=400, tick=T) arrives, then display.HP = 500.
*Test type: Unit — injectable message ordering.*

**AC-RDA-02 — UseItemResult applied when no EHU from same or later tick**
Given HPDisplayTick=T-1, when UseItemResult(newResourceValue=400, tick=T) arrives, then display.HP = 400.
*Test type: Unit.*

**AC-RDA-03 — Stale UseItemResult discarded when same-tick EHU already applied**
Given HPDisplayTick=T (from EHU at tick T, display.HP=500), when UseItemResult(newResourceValue=400, tick=T) arrives, then display.HP = 500.
*Test type: Unit.*

**AC-RDA-04 — Stale EHU discarded**
Given HPDisplayTick=T (display.HP=500), when EHU(currentHP=300, tick=T-2) arrives, then display.HP = 500.
*Test type: Unit.*

**AC-RDA-05 — HPDisplayTick resets to 0 on zone entry**
Given HPDisplayTick=25, when zone entry signal fires, then HPDisplayTick=0 and MPDisplayTick=0.
*Test type: Unit.*

**AC-RDA-06 — Inventory and cooldown updates always applied regardless of tick check**
Given HPDisplayTick=T (UseItemResult HP payload discarded per Rule B), when UseItemResult arrives at tick T, then newInventoryQuantity and cooldown are still applied to local state.
*Test type: Unit.*

## Dependencies

| Document | Relationship |
|----------|-------------|
| `networking-wire-protocol.md` | CR-NET-7.1 — ServerTickNumber in all envelopes. R-U batch envelope provides tick for all sub-messages in the same flush. |
| `consumable-use-system.md` | Produces UseItemResult. OQ-CUS-4 resolved by this contract. |
| `networking-relevance-filter.md` | Governs which clients receive EntityHealthUpdate for a given entity — determines when Rule A fires for the local player. |
| `character-stats.md` | WriteLocalHP/WriteLocalMP target. HUD subscribes to OnStatChanged per character-stats.md UI requirements. |
| `HUD / Consumable UI` | Display consumer — applies floor-integer display per character-stats.md. Not authored yet. |
