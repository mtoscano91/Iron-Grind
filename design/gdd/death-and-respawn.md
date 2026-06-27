# Death & Respawn

> **Status**: Approved (lean re-review #2, 2026-05-29)
> **Author**: Manuel Toscano + Claude Code agents
> **Last Updated**: 2026-05-29 (lean re-review #2: OQ-DR-1/OQ-DR-2/OQ-DR-6 marked resolved; EC-DR-1 reconnect-while-DEAD behavior defined from zone-instancing.md CR-ZI-8 step 6; stale provisional notes removed; AC-DR-13 gate cleared)
> **Implements Pillar**: Earned Power (secondary), Social Gravity (secondary)

## Overview

Death & Respawn is the server-side system that handles the full player death lifecycle: detecting the zero-HP event, transitioning the character to a dead state, and returning them to a respawn point with health restored — without reloading character data from persistence. When a player's `CurrentHP` reaches 0.0, Character Stats emits `OnEntityDied(EntityID)`; Death & Respawn subscribes to this event and takes ownership from that point. The system cancels the character's active combat, relocates them to the zone's designated safe-area town respawn point, restores HP and MP to full, and broadcasts the state change to all players in the zone. No items are lost on death for MVP — the meaningful risk in Iron Grind is enhancement failure, not dying. Death serves as a soft reset and pacing mechanism: a reminder that a zone is too dangerous, a prompt to regroup with a party, or simply a moment to breathe before returning. Ghost-mode deaths — when the character was continuing in auto-combat after a player disconnect — are handled as a full session termination rather than a normal respawn, since there is no connected client to return to the world.

## Player Fantasy

Death in Iron Grind is not the disaster. That's a deliberate statement. When `CurrentHP` hits zero, the player loses the pull, the momentum, maybe a few minutes of progress. They do not lose their gear. The game keeps its real teeth somewhere else — at the enhancement anvil, where a 43% success chance and a +7 weapon that took two hours to reach can vanish in a single server-side roll. Standing there, later that same session, is when hands actually sweat. Death is the warm-up; the anvil is the duel.

That said, dying still stings — and it should. The cost is *pacing*, not loss. Death breaks the rhythm. For a game built on Rhythm Mastery, that broken streak is meaningful: you were in flow, reading the auto-attack windows, managing your skills cleanly — and then you weren't. The defeat screen lands quietly. You wake up in town at full HP and MP. You stare at the map.

In that pause, the social machinery kicks in. A zone that killed you solo becomes legible party content. The walk back from the respawn point is the world calibrating you — it tells you exactly which zone you belong in, which gear tier you haven't reached yet, and whether the answer is grinding harder or finding someone to go back with. Some players log off. Some open party chat. Either is an honest response.

*Pillar alignment*: Earned Power (death is information about what you haven't earned yet), Social Gravity (the natural "call for backup" moment), Legendary Gear (death's forgiving nature makes the anvil singular — the deliberate asymmetry is the design).

## Detailed Design

### Core Rules

**Death Resolution** — fires when `OnEntityDied(EntityID)` is received from Character Stats (CurrentHP reached 0.0). All steps are server-authoritative. Steps CR-DR-1 through CR-DR-7 execute within the same server tick.

**CR-DR-1** — Death is triggered by one path only: `OnEntityDied(EntityID)` from Character Stats. Any system that needs to kill an entity routes through Character Stats (reduce CurrentHP to 0.0). No other death entry point exists.

**CR-DR-Ghost** — Ghost death bifurcation: the **first** check in the `OnEntityDied` handler is `EntityState.isGhost`. If `isGhost == true`: record `GhostDeath=3` for the reconnect flow (no wire message is sent at this point — the client transport is already closed), release zone slot, and return. Do NOT enter `DEAD` state; do NOT start respawn timer; do NOT call SetCombatProhibited or ClearAll. Ghost deaths are session terminations, not respawns. `ZoneSessionEnded(DisconnectReason.GhostDeath=3)` is delivered to the client on reconnect via networking-ghost-session.md CR-GH-6, not as a live wire message at death time.

**CR-DR-2** — Duplicate suppression: if `entityId` is already in `DEAD` or `Respawning` state when `OnEntityDied` fires, the event is dropped silently to prevent race conditions when multiple damage sources reduce HP to 0.0 in the same tick. `Respawning` is treated identically to `Dead` for all deduplication and targeting purposes.

**CR-DR-3** — `SetCombatProhibited(entityId, true)` is called. Dead characters can neither auto-attack nor be validly targeted by new combat actions. (The attacker's auto-attack transitions to `IDLE` via `DamageResult.IsKill` — this call prohibits the dead entity itself from continuing to act.)

**CR-DR-4** — `StatusEffects.ClearAll(entityId)` is called after CR-DR-3. All active buffs and debuffs are stripped. `RemoveBuffModifier()` fires for each stat modifier so Character Stats emits `OnStatChanged` to all subscribers.

**CR-DR-5** — If the dead entity belongs to a party (`IsSoloParty == false`), the Party System is notified via `SetMemberStatus(partyId, entityId, MemberStatus.Dead)`. The party slot is **retained** — not released. `PartyStateUpdate` is broadcast to all party members. Dead members are skipped in XP distribution (F-PS-1) and loot round-robin (`rrNextIndex` advances past them). *(Provisional gap: Party System GDD must add `Dead` to the `MemberStatus` enum and to the CR-PS-5 ineligibility list.)*

**CR-DR-6** — Server records `deathTick`. Computes `respawnTick = deathTick + RESPAWN_DELAY_TICKS`. Entity enters `DEAD` state. Server sends `DeathStateEntered(entityId, respawnTimerSeconds)` to the dead player's client for countdown UI.

**CR-DR-7** — `EntityDied(entityId)` is broadcast to all zone clients (relevance-filtered). Drives death VFX, nameplate removal, and party HP bar clearing on nearby clients.

---

**Respawn Sequence** — executes when `currentTick >= respawnTick` (evaluated each server tick).

**CR-DR-8** — Server resolves the respawn position via `GetTownRespawnPoint(zoneId)`. This is a static per-zone dictionary lookup — no spatial query is performed. The call must be synchronous and complete within the same tick. If no valid position is resolved (content authoring error: zone data missing town respawn point), the system retries once per tick for up to `RESPAWN_ANCHOR_RETRY_TICKS` ticks, then applies the per-zone hardcoded fallback `Vector3` and logs a critical alert. *(API defined by zone-instancing.md CR-ZI-11 — O(1) synchronous lookup, fallback chain: authored point → zone centroid → Vector3.zero. OQ-DR-2 resolved.)*

**CR-DR-9** — HP and MP restored to full: `SetBaseStat(entityId, StatID.CurrentHP, GetEffectiveStat(entityId, StatID.MaxHP))` and `SetBaseStat(entityId, StatID.CurrentMP, GetEffectiveStat(entityId, StatID.MaxMP))`. `LoadCharacter` is **NOT** called — the character is already in server memory.

**CR-DR-10** — Entity's authoritative position is set to the resolved respawn point. Server broadcasts the position correction to all zone clients (delivered via `EntityRespawned` — CR-DR-13; no separate position broadcast is sent).

**CR-DR-11** — `SetCombatProhibited(entityId, false)`. Character can now attack and be validly targeted.

**CR-DR-12** — If the entity was in a party, Party System is notified: `SetMemberStatus(partyId, entityId, MemberStatus.Online)`. Slot index is unchanged — player returns to their original slot. XP eligibility (CR-PS-5) is restored.

**CR-DR-13** — `EntityRespawned(entityId, position)` broadcast to all zone clients. `RespawnConfirmed` sent to the respawning player's client; death countdown UI dismissed.

---

**Dead Player Permissions** (during DEAD state countdown):

**CR-DR-15** — Dead players **CAN**: view the zone *(PvE scope only — this rule must be re-evaluated before any PvP content is authored; zone visibility while dead is a scouting advantage in competitive contexts)*, send/receive chat (zone and party), read party HP bars (their own bar shows 0/MaxHP), open inventory and character stat panels in read-only mode, accept or decline party invites.

**CR-DR-16** — Dead players **CANNOT**: use skills, activate auto-attack, be a valid combat target for any entity, initiate a voluntary zone change, or initiate a party invite. Note: the system-initiated zone transition to the town respawn point on T-3 is not subject to this restriction — it is server-authoritative, not player-initiated.

**CR-DR-17** — No early manual respawn for MVP. The full `RESPAWN_DELAY_SECONDS` always elapses. There is no "respawn now" button — the pause is the cost.

---

**Respawn Position Selection:**

**CR-DR-14** — Each zone has exactly one designated **town respawn point**: a static server-side `Vector3` and `ZoneID` pair representing the safe-area town entry position associated with that zone. There is no proximity selection — one point per zone, defined at authoring time. *(API defined by zone-instancing.md CR-ZI-11. OQ-DR-2 resolved.)*

---

**Party Effects of Death:**

**CR-DR-18** — A dead party member's slot is held for the full respawn countdown. No invite or re-join flow is required on respawn — the player returns to their original slot automatically.

**CR-DR-19** — Dead members are ineligible for F-PS-1 XP during the countdown. Kill XP for kills made while the member is `Dead` is not queued — it is simply not computed for that entity.

**CR-DR-20** — `rrNextIndex` skips `Dead` members during loot round-robin (same as Ghost/Disconnected). Loot eligibility restores when `MemberStatus.Online` is set on respawn.

**CR-DR-21** — If the party disbands while a member is in `DEAD` state, the dead member receives their solo `PartyID` immediately (per CR-PS-9 voluntary leave path). On respawn, they enter the world as a solo player.

### States and Transitions

| State | Description | Targetable | Can Move | Can Chat |
|-------|-------------|-----------|----------|----------|
| **Alive** | Normal gameplay. No death tracking active. | Yes | Yes | Yes |
| **Dead** | `CurrentHP = 0`. Respawn countdown running. Combat prohibited. | No | No | Yes |
| **Respawning** | Transient single-tick processing state on countdown expiry. Not a durable client-visible state. | No | No | Yes |

| # | From | To | Trigger | Side Effects |
|---|------|----|---------|-------------|
| T-1 | Alive | Dead | `OnEntityDied` received; `isGhost == false` | CR-DR-3: combat prohibited; CR-DR-4: buffs cleared; CR-DR-5: party notified Dead; CR-DR-6: timer started; CR-DR-7: zone broadcast |
| T-2 | Dead | Respawning | `currentTick >= respawnTick` | Internal only — no client message |
| T-3 | Respawning | Alive | Side effects complete | CR-DR-9: HP/MP restored; CR-DR-10: repositioned; CR-DR-11: combat re-enabled; CR-DR-12: party restored Online; CR-DR-13: zone broadcast |
| T-4 | Alive | [Session terminated] | `OnEntityDied` received; `isGhost == true` | Ghost death recorded; zone slot released; `ZoneSessionEnded(DisconnectReason.GhostDeath=3)` delivered to client on reconnect (not at death time — per networking-ghost-session.md CR-GH-6); no Dead state entered |

**'Single-Tick' `Respawning` State Semantics:** The T-2 → T-3 transition is initiated in tick N. All side-effect field writes (HP/MP restore, position update, combat flag) complete within tick N's processing step. All outbound network messages (`EntityRespawned`, `RespawnConfirmed`) are enqueued and flushed at the end of tick N's network step — they are not sent inline mid-tick. No external game-logic interactions (targeting queries, incoming `OnEntityDied` events) are processed against an entity in `Respawning` state; CR-DR-2 treats `Respawning` identically to `Dead` for deduplication. The timer evaluation guard is: `CurrentState == Dead && (long)currentTick >= RespawnTick`.

**DeathState struct** (server-side, per player entity):

```csharp
struct DeathState
{
    DeathStateEnum CurrentState;   // Alive | Dead | Respawning
    long           DeathTick;      // 0 when Alive
    long           RespawnTick;    // DeathTick + RESPAWN_DELAY_TICKS; 0 when Alive
    ZoneID         RespawnZoneID;  // town zone associated with the death zone
    Vector3        RespawnPosition;
}
```

### Interactions with Other Systems

| System | Direction | Contract |
|--------|-----------|---------|
| **Character Stats** | Upstream | Subscribes to `OnEntityDied(EntityID)`. On respawn: calls `SetBaseStat(CurrentHP, MaxHP)` and `SetBaseStat(CurrentMP, MaxMP)`. |
| **Auto-Attack Combat** | DR → | `SetCombatProhibited(true)` on death. `SetCombatProhibited(false)` on respawn. Targeting validation must reject entities in `DEAD` state — this GDD provides that contract to Hit Detection and Auto-Attack Combat. |
| **Status Effects / Buffs** | DR → | `ClearAll(entityId)` on death. No interaction on respawn — player re-acquires buffs naturally. |
| **Party System** | DR → | `SetMemberStatus(Dead)` on death; `SetMemberStatus(Online)` on respawn. Dead members ineligible for F-PS-1 XP and loot round-robin. *(Provisional gap: Party System must add `Dead` to `MemberStatus` enum and CR-PS-5 ineligibility list.)* |
| **Zone Instancing** | DR → (provisional) | `GetTownRespawnPoint(ZoneID) : Vector3` — static per-zone dictionary lookup, synchronous, same-tick. Not yet authored; fallback is per-zone hardcoded position in `assets/data/zones/[zone-id]-respawn.json`. |
| **Networking Core** | ↔ DR | `EntityDied` broadcast to zone (relevance-filtered — RFR-7, `networking-relevance-filter.md`). `EntityRespawned` broadcast to zone. `DeathStateEntered` sent to dead player's client. `RespawnConfirmed` sent to respawning client. `ZoneSessionEnded(GhostDeath=3)` sent on reconnect after ghost death (per networking-ghost-session.md CR-GH-6, not at death time). **COMPLETED 2026-05-29** — `EntityDied`, `DeathStateEntered`, `EntityRespawned`, `RespawnConfirmed` registered in `networking-wire-protocol.md`; `RFR-7` added to `networking-relevance-filter.md`. `EntityState` in `ZoneStateSnapshotFragment` — `isInDeadState`/`respawnTicksRemaining` specified in zone-instancing.md CR-ZI-8 step 6 (OQ-DR-6 resolved at design level; wire struct amendment OQ-ZI-2 in zone-instancing.md). |

## Formulas

**F-DR-1 — Respawn Delay Ticks**

The respawn countdown duration is expressed in server ticks to align with the 20Hz server loop:

```
RESPAWN_DELAY_TICKS = RESPAWN_DELAY_SECONDS × TICK_RATE_HZ
```

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Respawn delay (seconds) | `RESPAWN_DELAY_SECONDS` | float (tuning knob) | [5.0, 10.0] | Configurable death-to-respawn wait |
| Server tick rate | `TICK_RATE_HZ` | int constant | 20 | Locked — owned by networking-core.md |
| Respawn delay (ticks) | `RESPAWN_DELAY_TICKS` | int (derived) | [100, 200] | Countdown duration in server ticks |

**Output range:** [100, 200] ticks. No runtime clamping needed if `RESPAWN_DELAY_SECONDS` is range-validated at config load.

**Example:** Default `RESPAWN_DELAY_SECONDS = 7.0` → `RESPAWN_DELAY_TICKS = 7.0 × 20 = 140 ticks` (7.0 seconds).

**Conversion rule:** `RESPAWN_DELAY_TICKS = (int)Mathf.RoundToInt(RESPAWN_DELAY_SECONDS * (float)TICK_RATE_HZ)`. Integer truncation (`(int)float`) must not be used — float precision at `7.0f × 20` can yield `139.999…` under some serialisation paths. A startup assertion that `RESPAWN_DELAY_TICKS >= 100` must follow computation.

**Timer evaluation guard:** `CurrentState == Dead && (long)currentTick >= RespawnTick`. Both fields are `long` in the `DeathState` struct. A default-zeroed `DeathState` (all fields 0) must never fire T-2 unless `CurrentState == Dead`.

**Implementation note:** The server stores `DeathTick` (long) and computes `RespawnTick = DeathTick + (long)RESPAWN_DELAY_TICKS`. Each server tick evaluates the guard above to trigger transition T-2. Timer does not drift because `TICK_RATE_HZ` is the authoritative clock — no floating-point time accumulation occurs in the countdown path. At 20Hz, a `long` tick counter overflows after approximately 14.6 billion years — no overflow handling required.

No additional formulas are required. HP/MP restoration on respawn uses `MaxHP`/`MaxMP` directly from `GetEffectiveStat()` — no formula applied.

## Edge Cases

**EC-DR-1 — Disconnect during DEAD countdown**
- **If a player disconnects while in `DEAD` state**: `DeathState` persists in server memory. The respawn timer continues running server-side. If `respawnTick` is reached while disconnected, the full respawn sequence (T-2 → T-3) executes: HP/MP restored, position set to respawn anchor, party slot restored to Online. When the client reconnects within `SESSION_TTL_SECONDS`, it receives a `ZoneStateSnapshot` showing the character already alive at the respawn anchor.
- **Reconnect-while-still-DEAD:** If the player reconnects before `respawnTick` elapses, Zone Instancing (CR-ZI-8 step 6) includes `isInDeadState = true` and `respawnTicksRemaining = max(0, RespawnTick − currentTick)` in the dead player's `EntityState` entry within the `ZoneStateSnapshot`. On receipt, the client re-renders the death overlay with the remaining seconds and resumes local countdown. *(Wire schema amendment — `EntityState` struct addition — tracked as OQ-ZI-2 in zone-instancing.md; OQ-DR-6 resolved at design level.)*

**EC-DR-2 — SESSION_TTL expires while player is DEAD**
- **If the player does not reconnect before `SESSION_TTL_SECONDS` (300s) expires while in `DEAD` state**: `SaveSession(SessionTTLExpiry)` writes `CurrentHP = 0` to persistence. On the next login, Character Persistence must detect `savedCurrentHP <= 0` and clamp `CurrentHP = MaxHP` before emitting `SessionReady`. **Cross-document action required**: Character Persistence GDD must add an explicit `if (savedCurrentHP <= 0) currentHP = maxHP` correction to CR-CP-3 step 8.

**EC-DR-3 — Zone teardown while player is DEAD**
- **If the zone instance closes (`ZoneSessionEnded(ZoneClosed=0)`) with one or more players in `DEAD` state**: zone teardown must enumerate dead entities and flush `DeathState` before releasing the slot pool. `SaveSession` is called with `CurrentHP = 0`; the EC-DR-2 load-time correction applies on next login. Zone Instancing GDD must account for dead entities in its teardown sequence.

**EC-DR-4 — Immediate re-death at town respawn point**
- **If a hostile mob is positioned at the town respawn point and deals lethal damage within the same tick as T-3 completing**: `OnEntityDied` fires again. The character has already transitioned to `Alive` (T-3 complete), so CR-DR-2 does not suppress it. T-1 re-executes as a fresh death — the state machine handles this correctly by design. Level Design constraint: the town respawn point must be within a safe area; by definition, towns are safe zones with no mob aggro radius.

**EC-DR-5 — `RESPAWN_DELAY_SECONDS` at or below minimum**
- **If `RESPAWN_DELAY_SECONDS` is configured at 0.0 or below**: `RESPAWN_DELAY_TICKS = 0`; `respawnTick = deathTick`; the entity transitions Dead → Respawning → Alive within the same server tick as death, eliminating all pacing cost. Server startup must **reject** (not silently clamp) any `RESPAWN_DELAY_SECONDS` value below 5.0. A startup validation failure with an explicit error log is required — a runtime assert is insufficient.

**EC-DR-6 — Tick counter overflow (resolved by field typing)**
- **`DeathTick` and `RespawnTick` are typed `long` in the `DeathState` struct** (see struct definition). At `TICK_RATE_HZ = 20`, a `long` tick counter overflows after approximately 14.6 billion years of continuous server operation — not a practical concern. No overflow mitigation is required beyond using the `long` types mandated in the struct.

**EC-DR-7 — Server restart while player is DEAD**
- **If the server restarts while a player is in `DEAD` state**: `DeathState` is ephemeral — not persisted. On reconnect, `LoadCharacter` runs the normal load sequence. `savedCurrentHP = 0` triggers the EC-DR-2 correction rule. Character loads in `Alive` state with full HP at `LastZoneID`. No death-recovery path is needed beyond the HP clamp.

**EC-DR-8 — Party leader dies**
- **If the party leader enters `DEAD` state**: party leadership is **not transferred** for MVP. The leader's slot is marked `MemberStatus.Dead`; other members continue earning XP (leader is ineligible during `DEAD`) and looting. On respawn, the leader's slot returns to `Online` and leadership is automatically restored — no extra messages required. If leader transfer on death is added post-MVP, the Party System GDD must define the selection rule.

**EC-DR-9 — `GetTownRespawnPoint` returns no valid position**
- **If the zone has no town respawn point defined** (content authoring error — zone data entry missing): CR-DR-8 retries once per tick. Since `GetTownRespawnPoint` is a static dictionary lookup (not a spatial query), the retry checks whether the zone data entry has become available since the last tick — unlikely in practice. Resolution: `RESPAWN_ANCHOR_RETRY_TICKS` (default 40 ticks / 2.0s; range [20, 200]). After this budget is exhausted: apply the per-zone hardcoded fallback `Vector3` unconditionally and log a critical server alert. If the countdown UI has already expired, display "Locating respawn point…" to the client during the retry window.

## Dependencies

**Upstream — Systems that Death & Respawn consumes**

| System | Dependency Type | Interface | Notes |
|--------|----------------|-----------|-------|
| **Character Stats** | Hard | `OnEntityDied(EntityID)` delegate; `SetBaseStat(StatID.CurrentHP/MP, value)` on respawn; `GetEffectiveStat(StatID.MaxHP/MP)` on respawn | Entry point for this system. CR-CP-2 (character-stats.md) defines `OnEntityDied` as `public delegate void EntityDiedHandler(EntityID entityId)`. |
| **Auto-Attack Combat** | Hard | `SetCombatProhibited(entityId, bool)` — called true on death, false on respawn | Auto-Attack Combat must also accept `DEAD` state as a valid reason for the Targeting System to reject this entity as a target. |
| **Status Effects / Buffs** | Hard | `StatusEffects.ClearAll(entityId)` on death | All active buff/debuff entries for the entity are stripped before the respawn countdown begins. |
| **Character Persistence** | Upstream context | `LoadCharacter` is **NOT** called on respawn — character is already in server memory. `SaveSession` is called on TTL expiry with `CurrentHP = 0`; EC-DR-2 HP clamp (`if savedCurrentHP <= 0 → MaxHP`) required in CR-CP-3 step 8. | **PATCHED** — CR-CP-3 step 8 updated in `character-persistence.md` (OQ-DR-3 resolved 2026-05-28). |
| **Networking Core** | Infrastructure | Tick counter for `DeathTick` / `RespawnTick` (long); broadcast delivery for `EntityDied`, `EntityRespawned`, `DeathStateEntered`, `RespawnConfirmed`, `ZoneSessionEnded`. `DisconnectReason.GhostDeath = 3` consumed by this system. | **COMPLETED 2026-05-29** — all 4 wire schemas registered in `networking-wire-protocol.md`; `RFR-7` added to `networking-relevance-filter.md`. Remaining: `EntityState` in `ZoneStateSnapshotFragment` needs `isInDeadState`/`respawnTicksRemaining` for reconnect-while-DEAD (OQ-DR-6). |
| **Zone Instancing** | Hard | `GetTownRespawnPoint(ZoneID) : Vector3` — static per-zone dictionary lookup, synchronous, same-tick. | Defined in zone-instancing.md CR-ZI-11. OQ-DR-2 resolved. |
| **Party System** | Soft | `SetMemberStatus(partyId, entityId, MemberStatus.Dead/Online)` | Party System GDD must add `Dead` to `MemberStatus` enum and to the CR-PS-5 XP ineligibility list. This is a cross-doc gap — flagged in Open Questions. |

**Downstream — Systems that depend on Death & Respawn**

| System | What it receives | Notes |
|--------|-----------------|-------|
| **Zone Instancing** | `GetTownRespawnPoint(ZoneID)` defined as CR-ZI-11 (OQ-DR-2 resolved); enumerates `DEAD` entities in teardown sequence (EC-DR-3, CR-ZI-12 step 2). | Bidirectional: Death & Respawn depends on Zone Instancing, and Zone Instancing's teardown is constrained by Death & Respawn behavior. |
| **Character Persistence** | EC-DR-2 HP clamp: `if (savedCurrentHP <= 0) currentHP = maxHP` in CR-CP-3 step 8. | **PATCHED** — CR-CP-3 step 8 updated in `character-persistence.md` (OQ-DR-3 resolved 2026-05-28). |
| **Combat UI / HUD** | `DeathStateEntered(respawnTimerSeconds)` message — drives death countdown UI. `EntityRespawned` — dismisses death UI, restores HP/MP bars. | UI GDD owns the display spec; this GDD defines the wire message triggers. |
| **Party System** | `EntityDied` and `EntityRespawned` indirectly inform party member XP/loot eligibility. | Bidirectional.  |

**Bidirectional verification status**

- Character Stats → references Death & Respawn implicitly (EC-08: "entity removal and respawn are the responsibility of the entity's owner system") ✓
- Character Persistence → references Death & Respawn in its dependencies table (Not yet authored) — must update once this GDD is approved ✓ (partially)
- Auto-Attack Combat → `DamageResult.IsKill` triggers IDLE; `SetCombatProhibited` API exists ✓
- Party System → requires `MemberStatus.Dead` addition — open gap ✗ (tracked in OQ-DR-1)
- Zone Instancing → Needs Revision; `GetTownRespawnPoint` API defined as CR-ZI-11 ✓ (OQ-DR-2 resolved); `isInDeadState`/`respawnTicksRemaining` in snapshot defined as CR-ZI-8 step 6 ✓ (OQ-DR-6 resolved at design level; wire amendment OQ-ZI-2 in zone-instancing.md)

## Tuning Knobs

| Knob | Constant | Default | Safe Range | Effect |
|------|----------|---------|------------|--------|
| Respawn delay | `RESPAWN_DELAY_SECONDS` | 7.0 | [5.0, 10.0] s | How long the player waits in the death screen before returning to the world. Below 5.0s: death loses its pacing cost — players don't feel the interruption. Above 10.0s: mobile players will close the app. At 7.0s: deliberate without being punishing. **Server startup must reject values below 5.0 — do not silently clamp (EC-DR-5).** |
| Respawn anchor retry budget | `RESPAWN_ANCHOR_RETRY_TICKS` | 40 | [20, 200] ticks | Server ticks the system retries `GetTownRespawnPoint` before applying the hardcoded fallback `Vector3` and logging a critical alert. 40 ticks = 2.0 seconds at `TICK_RATE_HZ=20`. Only relevant as a safeguard against content authoring errors (zone with no authored town respawn point). Not a gameplay-visible value under normal operation. |

`TICK_RATE_HZ = 20` (networking-core.md) must be used when converting `RESPAWN_ANCHOR_RETRY_TICKS` to wall-clock seconds in monitoring dashboards or documentation.

## Visual/Audio Requirements

**Governing principles**: Legibility First — dead state must be instantly recognizable without a tooltip; Hit Feedback Is the Primary Sensory Budget — the death moment is the primary visual event, not the countdown UI; cool hues signal loss; a brief warm pulse on respawn is justified by full HP restoration.

### Event Specifications

**Event 1 — Death Moment (dying player's screen)**
- **Screen:** Full-screen dark vignette pulse (black, 0.3s ease-in, holds at 40% opacity). Camera does not move.
- **Entity:** Immediate greyscale tint via material override. No custom death animation — a single-frame material swap is sufficient. Enhancement glow extinguished (glow intensity → 0.0).
- **Audio:** Single dull thud — muffled bass hit, not a dramatic death cry. Silence after.

**Event 2 — Death Countdown UI (dying player)**
- **Overlay:** Semi-transparent dark panel (`#1A1C1F` at 70% alpha) covering the bottom third of screen. Text: "DEFEATED — Respawning in [N]s" in Ascension White `#F0EFE8`, large, centered. Countdown ticks each second. Zone remains visible behind the panel.
- **No skull icons. No cinematic bars.** The panel is a UI element, not a spectacle.
- **Audio:** None during countdown. Silence communicates loss.

**Event 3 — Dead Character (other players' view in zone)**
- **Entity:** Greyscale tint persists. Nameplate hidden (removed at CR-DR-7). No health bar. Character model remains at death position — no ragdoll or corpse animation (no custom animations at MVP). The absent nameplate and health bar are the primary legibility cues.
- **Audio:** None on other clients.
- **Duration:** ~7 seconds until `EntityRespawned` clears it.

**Event 4 — Respawn (returning player + all zone clients)**
- **Entity:** Greyscale removed instantly. Color restored. Enhancement glow restored to pre-death level. Brief warm pulse ring from character's feet: Gold `#D4AF37` at 60% opacity, 0.4s expand + fade. No looping.
- **Screen (respawning player):** Countdown panel dismissed. Camera begins a 0.3s fade-to-black. Character position transitions to the town respawn point during the black frame. Camera fades in over 0.2s at the town respawn point. Camera fade sequence must complete before the player regains touch input.
- **Audio:** Short ascending chime — soft, warm, not triumphant. Return, not victory.
- **Duration:** Pulse ring completes in 0.4s. All effects clear before the player regains input.

**Event 5 — Ghost Death (session termination)**
- **Client-side:** Nothing. The client is already disconnected.
- **Other players:** `EntityDied` is **NOT** broadcast for ghost deaths (CR-DR-Ghost returns before CR-DR-7). The entity despawns cleanly as a standard zone departure — no greyscale corpse lingers for an entity that will never respawn.

### Required Assets

| Asset | File | Notes |
|---|---|---|
| Death vignette | `vfx_death_vignette.mat` | Full-screen overlay, black, holds at 40% opacity |
| Death desaturate | `vfx_death_desaturate.mat` | Per-entity greyscale material override |
| Respawn pulse ring | `vfx_respawn_pulse_ring.png` | Gold `#D4AF37`, 256×256 atlas slot, unlit additive |
| Death audio | `sfx_death_hit_01.ogg` | Dull thud/bass hit, mono, < 0.2s |
| Respawn audio | `sfx_respawn_chime_01.ogg` | Ascending soft chime, mono, < 0.5s |

> **📌 Asset Spec** — Visual/Audio requirements are defined. After the art bible is approved, run `/asset-spec system:death-and-respawn` to produce per-asset visual descriptions, dimensions, and generation prompts from this section.

## UI Requirements

**Death Countdown Overlay**

**Component:** A single `VisualElement` (UI Toolkit) displayed during `DEAD` state. Dismissed automatically on receipt of `RespawnConfirmed` — no player touch input required or registered on the panel itself.

**Placement:** Bottom third of the screen (landscape). Does not obscure the zone view — the player can watch their party continue fighting.

**Visual spec:**
- Background: `#1A1C1F` at 70% alpha (Panel Dark, semi-transparent)
- Text: "DEFEATED — Respawning in [N]s" — centered, Ascension White `#F0EFE8`, minimum 22sp
- Countdown: decrements once per second, driven by the client from the `respawnTimerSeconds` value in `DeathStateEntered`. Server confirms completion via `RespawnConfirmed`.
- **Countdown gap state:** When the local countdown reaches 0 and `RespawnConfirmed` has not yet arrived, the text transitions to "DEFEATED — Respawning…" (no number). The overlay remains displayed until `RespawnConfirmed` is received. This prevents a frozen "Respawning in 0s" display during mobile network latency.
- No buttons. No touch target. Display-only.

**Interaction during countdown:** The overlay captures all touch input within its bounds (bottom third of screen). Players who wish to open the inventory or character panel during the countdown must tap outside the overlay bounds (upper two-thirds) or swipe up from below the overlay to reveal underlying panels. This prevents accidental inventory interactions during the death reflex.

**Accessibility:** "DEFEATED" text does not rely on color alone — the word and countdown number communicate the state without color. Screen reader: announce once at state entry ("Defeated. Respawning in 7 seconds.") and once at exit ("Respawned."). Intermediate countdown updates must NOT fire automatic VoiceOver announcements — suppress live-region updates for intermediate values. Users can navigate focus to the countdown element on demand to read the current value.

> **📌 UX Flag — Death & Respawn**: This system has UI requirements. In Phase 4 (Pre-Production), run `/ux-design` to create a UX spec for the death countdown overlay before writing epics. Stories referencing the death screen UI should cite `design/ux/death-overlay.md`, not this GDD directly.

## Acceptance Criteria

All logic criteria are BLOCKING gates — no story is Done without a passing automated test. Visual/feel criteria require screenshot/frame-capture evidence plus lead sign-off.

**Logic criteria (BLOCKING):**

| ID | Given | When | Then |
|----|-------|------|------|
| AC-DR-1 | A live player entity with CurrentHP > 0 | `OnEntityDied(EntityID)` fires | System sets `DeathState.CurrentState = Dead`; `deathTick` recorded; `respawnTick = deathTick + RESPAWN_DELAY_TICKS` |
| AC-DR-2 | Entity is already in `DEAD` state | `OnEntityDied(EntityID)` fires again | Event dropped silently; `DeathState` unchanged; no duplicate timer restart |
| AC-DR-3 | Entity enters DEAD state | `OnEntityDied` handler completes | Subsequent `IsCombatProhibited(entityId)` returns `true` |
| AC-DR-4 | Entity is in DEAD state | Any targeting query (e.g., `GetValidTargets()`) runs in that zone | Dead entity's ID is excluded from all valid target lists |
| AC-DR-5 | Entity has 2 active buffs when `OnEntityDied` fires | Death resolution executes | `StatusEffects.GetActiveCount(entityId) == 0` immediately after CR-DR-4 |
| AC-DR-6 | Entity dies at `deathTick = T`; `RESPAWN_DELAY_TICKS = 140` | Server ticks advance | Respawn sequence begins at tick `T + 140` (exact — no tolerance; timer is a deterministic integer comparison with no floating-point accumulation) |
| AC-DR-7 | Entity respawns | T-3 completes | `GetEffectiveStat(CurrentHP) == GetEffectiveStat(MaxHP)` and `GetEffectiveStat(CurrentMP) == GetEffectiveStat(MaxMP)` |
| AC-DR-8 | Entity respawns | T-3 completes | Entity's authoritative position is within 0.1 world units of the zone's designated town respawn point (tolerance covers floating-point serialization only; position is a direct assignment, not a physics correction) |
| AC-DR-9 | Entity respawns | T-3 completes | `IsCombatProhibited(entityId)` returns `false` |
| AC-DR-10 | A ghost entity (`EntityState.isGhost == true`) receives lethal damage | `OnEntityDied(entityId)` fires | `ZoneSessionEnded(DisconnectReason.GhostDeath=3)` is emitted; `DeathState.CurrentState` remains `Alive` (never transitions to `Dead`) |
| AC-DR-11 | Ghost entity receives lethal damage | `OnEntityDied` fires (isGhost=true) | `EntityDied` wire message is **not** broadcast to zone clients |
| AC-DR-12 | Server starts with `RESPAWN_DELAY_SECONDS = 4.9` (or any value < 5.0) | Server initialization runs | Server logs a configuration error and refuses to start. Rejection applies to all values in `[0.0, 4.9]`, not only exactly 0. |
| AC-DR-13 | Entity in a party dies | Death resolution executes | Party slot retained; `SetMemberStatus(Dead)` was called. *(Unit: verify call via mocked Party System interface. Integration [AC-DR-13b]: dead member receives 0 XP in next F-PS-1 distribution — OQ-DR-1 resolved 2026-05-29; MemberStatus.Dead now in party-system.md, unblocked.)* |
| AC-DR-14 | `RESPAWN_DELAY_SECONDS = 7.0`, `TICK_RATE_HZ = 20` | `RESPAWN_DELAY_TICKS` is computed (F-DR-1) | `RESPAWN_DELAY_TICKS == 140` (integer, exact). `5.0 → 100`; `10.0 → 200`. |
| AC-DR-15 | Entity dies at `deathTick = (long)uint.MaxValue + 1L` (past the old uint boundary) | `respawnTick` is computed | `DeathTick` and `RespawnTick` are `long` — no overflow; `RespawnTick == DeathTick + RESPAWN_DELAY_TICKS` (exact); respawn fires correctly at the target tick |
| AC-DR-16 *(Integration — gate on Character Persistence CR-CP-3 step 8 correction being merged)* | Entity is in `DEAD` state (CurrentHP=0) when `SESSION_TTL_SECONDS` expires | `SaveSession(SessionTTLExpiry)` runs | `savedCurrentHP = 0` is written to persistence; subsequent `LoadCharacter` clamps `CurrentHP` to `MaxHP` — character loads alive. |
| AC-DR-17 | Entity is in `DEAD` state; a manual `RespawnNow` request arrives from the client | Server processes the request | Request is silently dropped; no wire message sent to the client; `DeathState` unchanged; full `RESPAWN_DELAY_TICKS` elapses before respawn |
| AC-DR-18 | Entity in a party dies; remaining party members kill a mob during the countdown | XP distribution runs (F-PS-1) | `N_eligible` excludes the dead member; dead member receives 0 XP for that kill |

**Visual/feel criteria (ADVISORY):**

| ID | Scenario | Pass condition |
|----|----------|---------------|
| AC-DR-19 | Entity dies | Greyscale material override active by end of Unity Update frame that processes `OnEntityDied`; nameplate `GameObject.activeSelf == false`; vignette coroutine started (PlayMode test: inject event, advance 1 frame, assert both flags) |
| AC-DR-20 | Entity respawns | Greyscale removed, gold pulse `ParticleSystem.isPlaying == true` within 1 frame of `EntityRespawned`; `ParticleSystem.main.duration <= 0.4f`; camera fade-in coroutine started (PlayMode test; manual screenshot sign-off required) |

| ID | Given | When | Then |
|----|-------|------|------|
| AC-DR-NEW-1 | Non-ghost entity dies; at least one other zone client is connected | `OnEntityDied` handler completes (CR-DR-7) | `ZoneMessageBus` contains exactly 1 `EntityDied(entityId)` message; it is addressed to zone clients (not only the dying player's client) |
| AC-DR-NEW-2 | Entity enters DEAD state | `OnEntityDied` handler completes (CR-DR-6) | `ClientMessageBus.GetPending(entityId, MessageType.DeathStateEntered)` contains exactly 1 message with a `respawnTimerSeconds` value matching `RESPAWN_DELAY_SECONDS` |
| AC-DR-NEW-3 | Entity completes respawn; T-3 done | Respawn sequence finalizes (CR-DR-13) | `ZoneMessageBus` contains `EntityRespawned(entityId, position)` addressed to zone clients; `ClientMessageBus` contains `RespawnConfirmed(entityId)` addressed to the respawning client specifically |
| AC-DR-NEW-4 | Entity was in a party and in `MemberStatus.Dead` | T-3 completes (CR-DR-12) | `PartySystem.GetMemberStatus(partyId, entityId) == MemberStatus.Online`; slot index unchanged |
| AC-DR-NEW-5 | Entity is in `DEAD` state (CR-DR-16) | `UseSkill(entityId, skillId)` is called | Call returns a rejected result; no skill effect applied; `IsCombatProhibited(entityId) == true` |
| AC-DR-NEW-6 | Entity is in `DEAD` state (CR-DR-16) | Client sends voluntary `RequestZoneChange(entityId, targetZone)` | Request rejected; entity remains in current zone; `DeathState` unchanged |
| AC-DR-NEW-7 | Party of 3: A (Alive), B (Dead, `rrNextIndex=1`), C (Alive); loot drop occurs (CR-DR-20) | `LootDistribution` runs | B is skipped; loot assigned to C; `rrNextIndex` advances to 2 (pointing at C), then to 0 (pointing at A) on next drop |
| AC-DR-NEW-8 | Entity in a party was `MemberStatus.Dead`; T-3 completes (CR-DR-18) | Respawn sequence finalizes | `PartySystem.GetPartySlotIndex(partyId, entityId)` returns the same slot index as before death; `GetMemberStatus == Online` |
| AC-DR-NEW-9 *(Unit — gate on OQ-DR-1: Party System MemberStatus.Dead)* | Party disbands while entity B is in `DEAD` state (CR-DR-21) | `PartyDisbanded` is processed | B immediately holds a solo `PartyID`; B's slot in the old party is released; on B's subsequent respawn (T-3), `IsSoloParty == true` and B enters the world as a solo player |

## Open Questions

| ID | Question | Blocking? | Owner | Status |
|----|----------|-----------|-------|--------|
| OQ-DR-1 | **Party System must add `MemberStatus.Dead` to its enum** and add `Dead` to the CR-PS-5 XP ineligibility list. Without this, AC-DR-13 and AC-DR-18 cannot pass. | **BLOCKING** — before Party System sprint | Party System GDD | **RESOLVED 2026-05-29** — `Dead = 3` added to `PartyMemberStatus` enum in `networking-wire-protocol.md`; CR-PS-5 and CR-PS-7 updated in `party-system.md` |
| OQ-DR-2 | **Zone Instancing must define `GetTownRespawnPoint(ZoneID) : Vector3`** — static per-zone lookup, synchronous, same-tick contract. The provisional per-zone hardcoded `Vector3` fallback is sufficient for MVP if Zone Instancing is implemented concurrently or after this system, but the contract must be agreed before implementation begins. | **BLOCKING** — before implementation | Zone Instancing GDD | **RESOLVED 2026-05-29** — CR-ZI-11 in zone-instancing.md defines the API: O(1) synchronous lookup, fallback chain (authored → centroid → Vector3.zero). |
| OQ-DR-3 | **Character Persistence must add the EC-DR-2 load-time HP clamp to CR-CP-3 step 8**: `if (savedCurrentHP <= 0) currentHP = maxHP`. Without this, players whose session TTL expires during a DEAD countdown will load in as dead on next login. | **BLOCKING** — before Character Persistence sprint | character-persistence.md (patch CR-CP-3) | **RESOLVED 2026-05-28** — CR-CP-3 step 8 updated in character-persistence.md |
| OQ-DR-4 | **Camera behavior during death countdown and respawn.** | Not blocking (MVP) | UX | **RESOLVED 2026-05-29** — Camera stays at death position during countdown. On respawn: 0.3s fade-to-black, instant position transition to town respawn point, 0.2s fade-in. Fade sequence completes before player regains touch input. |
| OQ-DR-5 | **Party leader death and leadership transfer.** MVP decision: no transfer — leader restores automatically on respawn (EC-DR-8). Post-MVP, the Party System must define a selection rule if mid-death leader transfer is added (seniority? next-in-slot?). | Not blocking (MVP) | Party System GDD | Open — deferred post-MVP |
| OQ-DR-6 | **ZoneStateSnapshot must carry DeathState for reconnect-while-DEAD.** EC-DR-1 covers reconnect-after-respawn but not reconnect-while-still-in-countdown. `EntityState` in `networking-wire-protocol.md` `ZoneStateSnapshotFragment` must add `isInDeadState: bool` and `respawnTicksRemaining: uint` (0 when Alive). EC-DR-1 must then add: "If the client reconnects before `respawnTick`, the `ZoneStateSnapshot` carries `isInDeadState=true` and `respawnTicksRemaining`; the client re-renders the death overlay with the remaining seconds and resumes local countdown." | **BLOCKING** — before Networking Core sprint | `networking-wire-protocol.md` (EntityState) + EC-DR-1 update in this GDD | **RESOLVED at design level 2026-05-29** — zone-instancing.md CR-ZI-8 step 6 specifies `isInDeadState=true` and `respawnTicksRemaining=max(0, RespawnTick−currentTick)` for dead player entries in the snapshot; EC-DR-1 updated in this GDD. Wire struct amendment tracked as OQ-ZI-2 in zone-instancing.md. |
