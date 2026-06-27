# Party System

> **Status**: Approved (lean re-review 2026-05-17) — amended 2026-05-29 (MemberStatus.Dead added for Death & Respawn integration); amended 2026-06-19 (PartyMemberSlot data type + GetPartyMembersWithStatus added for Party Chat)
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-06-19 (Party Chat amendment: PartyMemberSlot struct + GetPartyMembersWithStatus method added to Exposed Data Types and Interactions; downstream Party Chat row updated; bidirectional check updated)
> **Implements Pillar**: Social Gravity (primary) — Earned Power (supporting)

## Overview

The Party System manages real-time group membership for 2–4 players within a shared zone instance. It is the authority on party state: who belongs to which party, in what join order, and what bonuses that membership confers. When a mob dies, the Party System provides the Loot Table System with the tagged party's membership array (`CharacterID[]`, join-order stable) and the round-robin counter (`rrNextIndex`) used to assign common drops. When a kill XP grant fires, the Party System computes each eligible member's bonus XP via F-PS-1 (derived from Leveling System F-LS-2) and calls `AddExperience()` individually per member. It emits full-state snapshots (`PartyStateUpdate`) to all party members on any membership change, and streams `PartyMemberHealthUpdate` ticks at 20 Hz so healers can see party HP in real time. A solo player is always assigned a unique per-character pseudo-PartyID — a non-zero PartyID distinct from all real party IDs — so loot attribution never collides across multiple solo players in the same zone.

For the player, the party system is invisible until it isn't: a clean solo grind, then a message — *"want to group?"* — and suddenly pulls are faster, XP climbs higher, and items hitting the ground feel like shared events rather than private accidents. The party doesn't change what you're doing. It changes who you're doing it with.

## Player Fantasy

The party is a contract, not a feature. When you accept an invite, you are agreeing — without saying so — that the four names now on your left-side HUD matter to your survival, and you to theirs. The healer is watching your HP bar. The tank is pulling so you don't get flanked. You hit harder when you know someone has your back. That shift in responsibility — from solitary grinder to party member — is felt immediately, not explained.

The fantasy is not efficiency (though parties are efficient). It is the specific weight of being *witnessed*. When a Steel item hits the ground in front of all six of you, nobody moves for a moment. Everyone saw it. Everyone knows what it's worth. The three seconds of silence before someone types anything — that is Social Gravity. The drop doesn't matter more because it's rare; it matters more because five other people are standing there knowing it's rare too. Solo grinding builds your character. Party grinding builds your reputation.

*Pillar alignment: Social Gravity (primary) — the party is the gravitational field; everything about it pulls players toward each other without locking the solo player out. Earned Power (supporting) — faster kill throughput delivers measurably better XP/hr and gold/hr despite per-member per-kill detriment; the party advantage is earned through kill speed, not a free multiplier. Legendary Gear (contextual) — the witnessed loot moment is where the gear's social value is first established.*

## Detailed Design

### Core Rules

**CR-PS-1 — Solo Player Representation**
Every player who is not a member of a formal party is assigned a solo PartyID by the server at login or zone entry. Solo parties have exactly one member (`MemberCount = 1`) and are flagged `IsSoloParty = true`. Solo PartyIDs are allocated from the same sequential counter as real party IDs. `PartyID = 0` is the uninitialized sentinel — transient during login only, and must never appear in stable game state. Solo parties are cleaned up when the player joins a formal party, or when the player disconnects beyond `SESSION_TTL_SECONDS`. The Loot Table System's `Dictionary<PartyID, damageRecord>` keys on this value — the solo design guarantees no two players ever share a damageRecord key.

**CR-PS-2 — Party Size and Membership**
- Solo party: exactly 1 member (`IsSoloParty = true`)
- Real party: 2–4 members (`MAX_PARTY_SIZE = 4`)
- Membership is tracked as a join-order-stable flat array of `MAX_PARTY_SIZE` (4) `CharacterID` slots. Empty slots are `CharacterID(0)`. Slots are compacted on removal (preserve join order; clamp `rrNextIndex` via modulo after each removal — see CR-PS-7). If `MAX_PARTY_SIZE` is raised via tuning, the array must be resized to match.
- A player belongs to exactly one party at a time. Accepting an invite while in a real party is rejected — the player must leave first.

**CR-PS-3 — Level Range Restriction (Join Gate)**
A player may only join a party if their current level is within ±5 of the party leader's current level: `|memberLevel − leaderLevel| ≤ 5`. The check runs at `PartyInviteRequest` receipt (server-authoritative). If the target is out of range, the invite is rejected with a visible error: *"Player is not within your level range."* If the party leader changes (CR-PS-8), the new leader's level becomes the reference going forward. Existing members are **not** forcibly removed — the gate applies only to future joins.

**CR-PS-4 — Party Formation (Invite Flow)**
Two entry points:
1. **Proximity tap-target**: Player taps another character in the game world → context menu → "Party Invite" → sends `PartyInviteRequest`.
2. **Character search**: From the party panel, enter a character name → "Invite."

Level range (CR-PS-3) is enforced server-side. Recipients see a push-style banner (inviter name, level, Accept/Decline). Banner expires after 60 seconds — treated as Decline. Server re-sends `PartyInviteReceived` once after 5 seconds if no `PartyInviteResponse` is received (mobile packet-loss mitigation, 1 retry only). One pending invite per recipient at a time; additional invites queue. Invites are delivered regardless of the recipient's combat state — client UX governs display mode.

**CR-PS-5 — XP Bonus (Party System Owns Computation)**
On mob kill, the Party System computes each eligible member's XP grant and calls `AddExperience(EntityID, amount)` per eligible member. The Leveling System is not party-aware and receives only per-player final amounts.

*Eligibility per kill event:*
- Member must be in the same zone instance as the kill event.
- Member's level must be within ±5 of the current party leader's level.
- Member must not be `Ghost`, `OutOfZone`, or `Dead` status. *(`Dead` added 2026-05-29 — death-and-respawn.md OQ-DR-1 resolution: dead members are ineligible for XP during the respawn countdown per CR-DR-19.)*

Bonus formula: **F-PS-1** (defined in Formulas section).

When a party member becomes XP-ineligible due to a leader change (CR-PS-8), the server sends that member a targeted system notification: *"You are outside your party's level range. XP grants are suspended."*

**CR-PS-6 — No Drop Rate Bonus**
The Party System provides no item drop rate bonus. Drop rates are fixed per mob type in the Loot Table System and are unmodified by party size or composition.

**CR-PS-7 — Round-Robin Cursor (`rrNextIndex`)**
The Party System owns `rrNextIndex` (type: `byte`, range `[0, MemberCount-1]`), stored in `PartyState`. The Loot Table System advances the cursor via `IPartySystem.AdvanceRrNextIndex(PartyID)` — it does not mutate `PartyState` directly.

On `AdvanceRrNextIndex()` the Party System:
1. Advances: `rrNextIndex = (rrNextIndex + 1) % MemberCount`
2. Skips ineligible slots with a visit-counted loop: advance at most `MemberCount` times. If no eligible member is found after `MemberCount` advances, reset `rrNextIndex` to its pre-step-1 value and return without assigning a target. Eligibility: `Status` is not Ghost, Disconnected, OutOfZone, or Dead; and `CharacterID != 0`. *(`Dead` added 2026-05-29 — CR-DR-20 resolution: dead members are skipped in loot round-robin.)*
3. Broadcasts updated `PartyStateUpdate` to all party members only if `rrNextIndex` changed.

If all remaining members are ineligible, `rrNextIndex` is unchanged (reset to pre-call value) and no loot is assigned. On party size decrease: if `rrNextIndex >= newSize`, clamp via `rrNextIndex = rrNextIndex % newSize`. Solo parties (`MemberCount = 1`) do not use `rrNextIndex` for loot; it is set to 0 and ignored.

**CR-PS-8 — Party Leader**
The first confirmed member is the initial leader. Leader is tracked as `LeaderIndex` (index into the 6-slot member array; derive CharacterID at callsite via `GetMember(state.LeaderIndex)`). Leadership transfers automatically:
- Leader leaves voluntarily **or** disconnect TTL expires → transfer to the **lowest non-empty member index**; if none below `LeaderIndex`, wrap to 0.
- On disconnect: transfer **immediately** (do not wait for TTL expiry).
- If the leader is the last member → party disbands (CR-PS-9), not transferred.

**LeaderIndex after array compaction:** When a member at array index `i` is removed and the slot is compacted: (a) if `i < LeaderIndex`, decrement `LeaderIndex` by 1 to track the same leader through the shift; (b) if `i == LeaderIndex`, apply the transfer rule above first, then compact the removed slot; (c) if `i > LeaderIndex`, no change to `LeaderIndex`. Ghost and Disconnected slots with active TTLs are retained in place — compaction applies only to voluntarily-departed or TTL-expired slots.

**CR-PS-10 — XP Distribution During Disbanding**
While a party is in `Disbanding` state, the server continues to process kill events and distribute XP via F-PS-1 to all eligible members. The disband cleanup does not freeze XP computation.

**CR-PS-11 — Reconnect Grace Window**
If a party member reconnects and completes the session handshake within 5 seconds of their disconnect event, the server suppresses Ghost state entirely — no `PartyMemberStatusChanged (Ghost)` message is sent to any party member. The 5-second window is a tuning knob (see Tuning Knobs).

**CR-PS-9 — Disbanding**
- **Leader calls disband**: Transitions to `Disbanding`; broadcasts `PartyDisbanded`; cleans up after all acknowledgments or a 10-second server timeout.
- **Last member leaves**: Auto-disband.
- **Voluntary leave**: Member slot cleared; array compacted; `rrNextIndex` clamped via `% newMemberCount`; remaining members receive `PartyStateUpdate`; departing member receives a new solo PartyID immediately.
- **Disconnect**: Slot held as Disconnected for up to `SESSION_TTL_SECONDS` (300s). At TTL expiry: treated as voluntary leave.

---

### States and Transitions

| State | Description | Entry Condition | Exits To |
|-------|-------------|-----------------|----------|
| `Forming` | Leader exists; ≥1 unresolved pending invite | First `PartyInviteRequest` sent | → `Active` on first accepted invite; → disbanded if leader cancels before any acceptance |
| `Active` | ≥2 confirmed members | Second member accepts invite | → `Disbanding` on disband call or last member leaves; → `Ghost` if all members disconnect simultaneously |
| `Disbanding` | Disband in progress | Leader diband or auto-disband trigger | → [cleanup] after all acknowledgments or 10s timeout |
| `Ghost` | All members are in ghost-session | All confirmed members disconnect simultaneously | → `Active` on any ghost member reconnect within SESSION_TTL_SECONDS; → [cleanup] when last ghost TTL expires |

---

### Exposed Data Types

**`PartyMemberSlot`** — read-only snapshot used by external callers that need per-member MemberStatus alongside CharacterID. The Party System populates these from its internal state on each call; callers must not cache across calls.

```
PartyMemberSlot
{
    CharacterID Id;       // 4 bytes — CharacterID(0) for empty slots
    MemberStatus Status;  // 1 byte — Online, Dead, Ghost, OutOfZone (see CR-PC-2)
}
```

*Distinct from the Loot Table System's `CharacterID[]` path — that path requires only CharacterIDs and uses `GetPartyMembers`. Systems that also need MemberStatus (e.g. Party Chat for delivery eligibility) use `GetPartyMembersWithStatus`.*

---

### Interactions with Other Systems

| System | Direction | Interface | Trigger |
|--------|-----------|-----------|---------|
| **Leveling System** | → calls | `AddExperience(EntityID, int amount)` per eligible member | Mob kill; Party System computes F-PS-1 per member |
| **Loot Table System** | ← provides | `CharacterID[] GetPartyMembers(PartyID)` (join-order stable); `CharacterID GetMemberAtIndex(PartyID, int index)` | At drop spawn for round-robin assignment |
| **Loot Table System** | ← calls | `AdvanceRrNextIndex(PartyID)` | After each common drop assigned |
| **Loot Table System** | ← provides | `PartyID GetPartyID(CharacterID)` | For damageRecord keying at mob hit |
| **Networking Core** | → sends | `PartyStateUpdate` (R-OD, sequenceId: uint32) on any membership change | Join, leave, disconnect, status change |
| **Networking Core** | → sends | `PartyMemberHealthUpdate` (R-U, absolute HP/MP values only — never deltas) to online members at ≤20 Hz | Each tick; filtered by delta threshold and full-health suppression |
| **Networking Core** | → routes | `PartyInviteRequest / PartyInviteReceived / PartyInviteResponse` (R-OD) | Invite flow |
| **Networking Core** | → sends | `PartyDisbanded` (R-OD) | Disband |
| **Zone Instancing** *(provisional — no GDD yet)* | ← reads | `PartyID GetPartyID(CharacterID)` for zone-join routing; notifies Party System on zone transition → member flagged `OutOfZone` | Zone entry/exit |
| **Death & Respawn** | ← calls | `SetMemberStatus(PartyID, EntityID, MemberStatus.Dead)` on entity death (CR-DR-5); `SetMemberStatus(PartyID, EntityID, MemberStatus.Online)` on respawn (CR-DR-12); slot is retained — not released — during countdown | Entity HP reaches 0 / respawn sequence completes |
| **Party Chat** | ← provides | `PartyID GetPartyID(CharacterID)` → solo-discard check; `PartyMemberSlot[] GetPartyMembersWithStatus(PartyID)` → full slot array with MemberStatus for CR-PC-2 eligibility filter and sender SenderNotEligible check | Per-message relay; Party Chat calls both at each relay request |

**`PartyMemberHealthUpdate` bandwidth management:** At `MAX_PARTY_SIZE = 4`, 20 Hz, each member receives 3 updates per tick. Server gates sends on `status ∈ {Online, Dead}` — Dead members continue to receive `PartyMemberHealthUpdate` with HP=0 so the party panel tracks the death state and respawn transition (per EC-RFR-2, `networking-relevance-filter.md`). No updates sent for Ghost or OutOfZone slots. Delta threshold: suppress if `|currentHP − lastSentHP| < HP_DELTA_THRESHOLD_FRACTION × maxHP` (tuning knob, default 5%). Full-health + full-MP suppression: if both values equal their maximums, suppress the update entirely.

## Formulas

**F-PS-1 — Party XP Deduction Per Member**

`XP_member = Mathf.RoundToInt(Mathf.Max(0f, XP_base × (1.0f - XP_PARTY_DEDUCTION_RATE × (N_eligible − 1))))`

**Variables:**

| Variable | Symbol | Type | Range | Description |
|----------|--------|------|-------|-------------|
| Base kill XP | `XP_base` | int | [200, 2,000,000] | Kill XP from F-LS-1 lookup; owned by Leveling System |
| Eligible members | `N_eligible` | int | [1, 4] | Party members who pass CR-PS-5 eligibility at kill time |
| XP deduction rate | `XP_PARTY_DEDUCTION_RATE` | float | 0.10 (tuning constant) | Fraction of base XP deducted per additional eligible member |

**Output Range:**

| Party size (N_eligible) | Multiplier | Min output (XP_base=200) | Max output (XP_base=2,000,000) |
|-------------------------|-----------|--------------------------|-------------------------------|
| 1 (solo) | ×1.00 | 200 XP | 2,000,000 XP |
| 2 | ×0.90 | 180 XP | 1,800,000 XP |
| 3 | ×0.80 | 160 XP | 1,600,000 XP |
| 4 | ×0.70 | 140 XP | 1,400,000 XP |

**Example:** Mob awards 1,000 base XP. 4 eligible party members: `Mathf.RoundToInt(1,000 × (1.0 - 0.10 × 3)) = Mathf.RoundToInt(700) = 700 XP` per member.

**Design intent:** Solo players receive full kill XP. Each additional party member reduces per-kill XP by 10%, reaching 70% at MAX_PARTY_SIZE=4. Parties kill substantially faster, making net XP/hour higher in a party — the incentive is throughput, not per-kill rate.

**Notes:**
- `Mathf.RoundToInt` required (not `(int)` or `Mathf.FloorToInt`) — IL2CPP consistency with leveling system.
- `N_eligible` is computed at kill time from CR-PS-5 eligibility, not total party size.
- Minimum output at default settings (MAX_PARTY_SIZE=4, RATE=0.10) is 70% of `XP_base` ≥ 140 XP — positive. When tuning RATE upward, enforce the joint constraint `XP_PARTY_DEDUCTION_RATE × (MAX_PARTY_SIZE − 1) < 1.0` to prevent zero or negative multipliers. `Mathf.Max(0f, ...)` is applied as a safety floor regardless.
- `XP_PARTY_DEDUCTION_RATE` is a new constant, distinct from `PartyBonusIncrement` (leveling-system.md) which described an additive bonus. These are different values with opposite effects.

**Propagation flag:** `F-LS-2` in `leveling-system.md` described a party XP bonus formula. The Party System is the authoritative executor of party XP computation (leveling-system CR-1.5). `leveling-system.md` should be updated to reference `F-PS-1` rather than describing its own bonus formula. Flag for the next leveling-system authoring session.

**Kill rate dependency:** The economic rationale for F-PS-1 (parties kill faster, making net XP/hour higher despite per-kill deduction) assumes a meaningful kill rate multiplier at full party size. This multiplier cannot be validated until the Mob Spawning GDD is authored. This is an accepted design risk — see OQ-PS-7.

*Drop rate bonus does not exist (CR-PS-6). Gold drops use the same diminishing return model as XP: each eligible member receives `Gold_base × (1.0f − 0.10f × (N_eligible − 1))` with the same `XP_PARTY_DEDUCTION_RATE = 0.10`. The Loot Table System computes `Gold_base` per kill; the Party System applies the detriment per eligible member before calling `AddGold()`. Formal formula (F-PS-2) pending Loot Table System amendment.*

## Edge Cases

**Formula boundary (F-PS-1):**
- **If `N_eligible = 0` at kill time** (all members are Ghost/OutOfZone/Disconnected/Dead simultaneously): do not evaluate F-PS-1. Award 0 XP to all members. The unguarded formula would compute `XP_base × (1.0 − 0.10 × −1) = XP_base × 1.10`, which is a bug.
- **If `N_eligible = 1` and that member is not the party leader**: award full solo XP (`XP_base × 1.00`) to that member. A party of 6 where 5 are ineligible produces solo-equivalent XP for the one eligible member.
- **If a member is at `LEVEL_CAP = 60` and in party**: the capped member satisfies the ±5 eligibility check. `AddExperience()` to a capped character is a no-op in the Leveling System. The capped member **counts toward `N_eligible`** and reduces per-kill XP for other members — intentional. A level-capped support player (healer, buffer) enables harder content with higher XP_base per mob and contributes to party survival; the per-kill deduction reflects the presence of a capable party member, not a penalty for the healer's role. `AddExperience()` to a capped character is a no-op in the Leveling System.
- **If `Mathf.RoundToInt` produces a `.5` midpoint**: Unity's `Mathf.RoundToInt` uses round-half-away-from-zero (IEEE 754 standard), not banker's rounding, across all builds including IL2CPP. At midpoint `XP_base = 5, N_eligible = 2` → `5 × 0.90 = 4.5` → rounds to 5. No special handling required.

**Level gate interacting with leader changes (CR-PS-3 + CR-PS-8):**
- **If the leader changes and the new leader's level places existing members outside ±5**: existing members remain in the party but lose XP eligibility immediately. The server sends a `PartyStateUpdate` that includes each member's updated eligibility status so the HUD can indicate which members are currently XP-ineligible.
- **If the next-in-line for leadership (lowest non-empty index) is a Ghost slot**: Ghost slots are not eligible to receive leadership. CR-PS-8 skips Ghost and Disconnected slots when searching for the next leader, using the same do-while pattern as the rrNextIndex advance.
- **If `|memberLevel − leaderLevel| = 5` exactly**: the boundary is inclusive (`≤ 5`, not `< 5`). A member exactly 5 levels away may join and is XP-eligible.

**Invite flow race conditions (CR-PS-4):**
- **If the party reaches `MAX_PARTY_SIZE = 4` between invite-send and invite-accept**: the server re-validates capacity at accept time. A late accept that would exceed 4 members is rejected with "party is full."
- **If a second invite from a different party arrives while the recipient has a pending invite**: the new invite is queued and delivered immediately upon resolution (accept, decline, or timeout) of the current pending invite — not dropped.

**`rrNextIndex` correctness (CR-PS-7):**
- **If `AdvanceRrNextIndex` is called by two kill events in the same server tick**: rrNextIndex updates are serialized per party. The server processes kill events sequentially within a single party's context — the second kill reads the post-first-advance index.
- **If a freed slot (CharacterID = 0) is reached by the do-while skip loop**: the eligibility check includes `CharacterID != 0` in addition to status flags. An empty slot has no status and is always ineligible.
- **If a member joins and fills slot index 5 while `rrNextIndex = 5`**: the new member immediately becomes the next round-robin target. This is intentional — join order determines eligibility. No "skip-first-cycle" grace period exists unless explicitly added.
- **Clamp-then-advance order on size decrease**: clamp `rrNextIndex` via `rrNextIndex % newMemberCount` at the moment of member removal, before the next `AdvanceRrNextIndex` call.

**State machine ordering (CR-PS-9 + concurrent events):**
- **If a kill event and a member-leave event arrive in the same server tick**: XP snapshot (`N_eligible` count) is taken before any membership changes in that tick are applied. The leaving member receives XP for that kill, then is removed.
- **If a kill event fires while the party is in `Disbanding` state**: XP is distributed normally. `Disbanding` is a cleanup state, not a frozen state — members remain in the party until the disband completes server-side.
- **If `IsSoloParty = true` and a join request arrives**: hard server rejection. `IsSoloParty` is write-once at allocation time. A solo party cannot accept members; the invite flow allocates a new real PartyID.

**Disconnect and reconnect (mobile-specific):**
- **If a member reconnects within 5 seconds of disconnect**: ghost state is suppressed — no TTL starts, no `PartyMemberStatusChanged` is broadcast. Treated as a momentary blip; prevents flickering ghost indicators during routine mobile network handoffs.
- **If a Ghost slot's TTL expires exactly as the player is mid-reconnect handshake**: the server completes the expiry path (slot freed, array compacted, `PartyStateUpdate` sent). The reconnecting client receives a "party dissolved" error and transitions to solo state.
- **If a Ghost slot expires and the player was backgrounded**: upon next foreground, the server sends a system message: *"You were removed from your party while offline."* Solo PartyID is already assigned; no client action required.

## Dependencies

**Upstream (Party System depends on):**

| System | Status | What this system needs from it |
|--------|--------|-------------------------------|
| **Networking Core** | Approved | `PartyID` wire type; `PartyStateUpdate`, `PartyMemberHealthUpdate`, `PartyInviteRequest/Received/Response`, `PartyDisbanded` message definitions; `TICK_RATE_HZ = 20`; `SESSION_TTL_SECONDS = 300` |
| **Leveling System** | Approved | `AddExperience(EntityID, int)` API; `XP_base` values from F-LS-1; `LEVEL_CAP = 60` |
| **Zone Instancing** | Not Started *(provisional)* | Zone membership notification for `OutOfZone` flag; zone-join routing via `GetPartyID(CharacterID)`. **Revision required when Zone Instancing GDD is authored.** |

**Downstream (systems that depend on Party System):**

| System | Status | What it needs from Party System |
|--------|--------|---------------------------------|
| **Loot Table System** | In Review | `GetPartyID(CharacterID)`, `GetPartyMembers(PartyID)`, `GetMemberAtIndex(PartyID, int)`, `AdvanceRrNextIndex(PartyID)` |
| **Networking Relevance Filter** | Draft | Party membership array for relevance grouping decisions |
| **Party Chat** | MAJOR REVISION NEEDED | `GetPartyID(CharacterID)` for solo-discard; `GetPartyMembersWithStatus(PartyID) → PartyMemberSlot[]` for CR-PC-2 eligibility filter and sender SenderNotEligible check (see Exposed Data Types for PartyMemberSlot definition) |
| **HUD** | Not Started | `PartyStateUpdate` for member frames; `PartyMemberHealthUpdate` for HP bars; `ArchetypeRole` read at party join |
| **Death & Respawn** | Needs Revision | Calls `SetMemberStatus(Dead/Online)` API; relies on Dead status for XP/loot ineligibility (CR-DR-19, CR-DR-20). OQ-DR-1 resolved by this amendment. |

**Bidirectional consistency check:**
- Networking Core → mentions Party System in interaction table §5 ✓
- Leveling System → CR-1.5 confirms Party System owns XP computation ✓
- Loot Table System → Dependencies section references Party System interfaces ✓
- Networking Relevance Filter → lists Party System as dependency ✓
- Zone Instancing → **no GDD exists; bidirectional reference pending**
- Party Chat → GDD exists (design/gdd/party-chat.md); calls `GetPartyID` and `GetPartyMembersWithStatus`; bidirectional ✓ (amended 2026-06-19)
- Death & Respawn → references Party System for `SetMemberStatus(Dead/Online)`; OQ-DR-1 resolved by this amendment ✓
- HUD → **no GDD exists; must reference Party System when authored**

**Propagation items flagged during authoring (must be addressed before re-reviewing dependent GDDs):**
1. `leveling-system.md` → mark F-LS-2 as superseded by F-PS-1; add reference to party-system.md Formulas section. N range `[1, 4]` is already correct for `MAX_PARTY_SIZE = 4` — no range change needed.
2. ✓ DONE — `entities.yaml` `PartyID` `special_value` updated to "0 = uninitialized sentinel, must not appear in stable game state" (2026-05-16).
3. ✓ DONE — `entities.yaml` `PartyBonusIncrement` annotated as not used by Party System; `XP_PARTY_DEDUCTION_RATE` is the active constant (2026-05-16).
4. ✓ RESOLVED — `game-concept.md` correctly describes party size as 2–4, consistent with `MAX_PARTY_SIZE = 4` (MVP decision 2026-05-16). No change needed.
5. ✓ RESOLVED — `loot-table-system.md` F-LT-2 N range `[1, 4]` is correct for `MAX_PARTY_SIZE = 4`. No change needed.

## Tuning Knobs

| Knob | Default | Safe Range | Effect if Too Low | Effect if Too High |
|------|---------|-----------|------------------|--------------------|
| `MAX_PARTY_SIZE` | 4 | [2, 6] | Less social density; elite zones feel lonely | Zone management complexity; HP update bandwidth scales with party size; joint constraint with RATE must hold |
| `XP_PARTY_DEDUCTION_RATE` | 0.10 | [0.00, 0.20] | Parties become XP-dominant; solo grind feels pointless | Parties feel punishing; detriment outweighs throughput gain; Social Gravity collapses |
| Level range gate (±N levels) | ±5 | [0, 15] | Hard to party with friends near level boundaries; Social Gravity friction | Enables trivial power-leveling (e.g., L60 + L1); Earned Power violated |
| Invite timeout | 60s | [30s, 120s] | Invitee misses invite during loading or combat | Long pending-invite queues accumulate; UX clutter |
| Ghost reconnect grace period | 5s | [2s, 30s] | Routine mobile handoffs flash ghost status on HUD | Exploitable: disconnect-reconnect to avoid rrNextIndex advancing |
| `HP_DELTA_THRESHOLD_FRACTION` | 0.05 | [0.00, 0.20] | At 0.00: full 20 Hz broadcast always; significant bandwidth at large party counts | Healer HUD shows stale HP; combat updates suppressed |

**Interactions between knobs:**
- `MAX_PARTY_SIZE` and `XP_PARTY_DEDUCTION_RATE` interact directly: at MAX_PARTY_SIZE=4 and rate=0.10, minimum per-kill XP is ×0.70. Raising MAX_PARTY_SIZE without adjusting the rate pushes per-kill XP lower — recalibrate together. Joint constraint: `XP_PARTY_DEDUCTION_RATE × (MAX_PARTY_SIZE − 1) < 1.0` must hold to keep the multiplier positive.
- Level range gate and `XP_PARTY_DEDUCTION_RATE` interact through `N_eligible`: a narrow level gate reduces N_eligible, reducing the effective deduction. A wide gate with a high rate maximises the per-kill penalty.

## Visual/Audio Requirements

The Party System has no standalone VFX. Visual and audio requirements are fulfilled at the HUD layer — the Party System provides state data; the HUD GDD specifies presentation. The following events must have HUD-observable outputs:

| Event | Required Signal | Notes |
|-------|----------------|-------|
| Member joins party | `PartyStateUpdate` → HUD adds member frame | Frame: name, level, HP bar, ArchetypeRole badge, leader crown if LeaderIndex |
| Member disconnects / Ghost | `PartyMemberStatusChanged (Ghost)` | HUD grays out member frame; Ghost indicator shown. Suppressed during 5s reconnect grace (CR-PS-11). |
| Member dies | `PartyStateUpdate` with `MemberStatus.Dead` | HUD shows HP=0 on member frame; `PartyMemberHealthUpdate` continues with HP=0 (EC-RFR-2). Visual treatment (greyscale frame or skull badge) defined in `death-and-respawn.md` Visual/Audio requirements. |
| Leader transfer | `PartyStateUpdate` with new LeaderIndex | HUD moves leader crown to new leader frame |
| XP ineligibility (level range) | `PartyStateUpdate` with per-member eligibility flags | HUD indicates ineligible members (dimmed frame or warning icon) |
| Party disband | `PartyDisbanded` | HUD collapses party panel; system message displayed |

*Audio:* Join sound (positive, brief), leave/disband sound (neutral), invite received sound (attention-getting but not alarming). Specific sounds specified in Audio System GDD when authored.

## UI Requirements

Party UI has two surfaces: the **persistent party panel** (visible in HUD during play) and the **invite banner** (transient, push-style).

**Party panel (persistent):**
- Displays up to 4 member frames in join order
- Each frame: character name, level, HP bar (updated via `PartyMemberHealthUpdate`), ArchetypeRole badge, leader crown at LeaderIndex
- Ghost/disconnected frames: grayed out with reconnect TTL countdown
- Out-of-zone frames: distinct out-of-zone indicator (different from Ghost)
- XP-ineligible members (outside ±5 of leader level): visual indicator showing bonus deduction does not apply
- Panel collapses to minimal display (names + HP bars only) during solo play
- All elements must be reachable with thumbs in landscape hold (per `technical-preferences.md`)

**Invite banner (transient):**
- Push-style notification at screen top: inviting player's name, level, class icon
- Two thumb-reachable buttons: Accept (primary) and Decline (secondary)
- Countdown timer showing remaining seconds of the 60s invite window
- Must render above all other UI layers including combat HUD
- Dismiss by explicit Accept/Decline or timeout only — tapping outside the banner is a no-op

📌 **UX Flag — Party System**: This system has UI requirements. Run `/ux-design` to create a UX spec for the party panel and invite banner before writing implementation epics. Stories referencing party UI should cite `design/ux/party-panel.md` and `design/ux/invite-banner.md`, not this GDD directly.

## Acceptance Criteria

**Solo / Party assignment:**

AC-PS-1: GIVEN a player completes login with no party affiliation, WHEN the server sends the initial PlayerState packet, THEN the PartyID field is non-zero AND no other currently connected player shares that PartyID — verified via server debug console PartyID uniqueness assertion.

AC-PS-2: GIVEN a party of 4 members, WHEN a 5th player attempts to accept a party invite, THEN the server rejects the accept and the 5th player's client receives a "party is full" error. Party membership count remains 4.

**Level gate:**

AC-PS-3: GIVEN a party leader at level 30, WHEN a player at level 36 attempts to accept an invite, THEN the server rejects with "not within your level range" and party membership does not change.

AC-PS-4: GIVEN a party leader at level 30, WHEN a player at exactly level 35 accepts an invite, THEN they join the party successfully (boundary is inclusive).

AC-PS-5: GIVEN a party of 4 where all members joined within ±4 of the old leader, WHEN leadership transfers to a new leader whose level places two existing members outside ±5, THEN those two members remain in the party and are not removed — verified via PartyStateUpdate showing all 4 members still present.

**Invite flow:**

AC-PS-6: GIVEN a party invite is sent and the recipient's client does not acknowledge, WHEN 5 server-side seconds elapse, THEN the server sends exactly one retry of the InviteNotification — verified via server outbound message log showing 2 sends (original + 1 retry) and 0 additional retries before 60s expiry.

AC-PS-7: GIVEN a party invite is sent and no response is received, WHEN 60 server-side seconds elapse, THEN the invite is marked expired, the recipient's pending invite slot is freed, and a subsequent invite to the same recipient from any leader can be sent successfully.

AC-PS-8: GIVEN player X has a pending invite from leader A, WHEN leader B sends player X an invite, THEN leader B's invite is queued server-side, player X sees no second banner until leader A's invite resolves, and leader B's client receives a send-acknowledgement (not an error). Upon leader A's invite resolving (accept, decline, or timeout), the queued invite from leader B is delivered to player X immediately as a new banner.

**XP formula (F-PS-1):**

AC-PS-9: GIVEN XP_base = 1000 and N_eligible = 1 (solo), WHEN a mob dies, THEN XP_member = 1000 (no deduction applied).

AC-PS-10: GIVEN XP_base = 1000 and N_eligible = 2, WHEN a mob dies, THEN each eligible member receives exactly 900 XP.

AC-PS-11: GIVEN XP_base = 1000 and N_eligible = 4, WHEN a mob dies, THEN each eligible member receives exactly 700 XP.

AC-PS-12: GIVEN XP_base = 1000 and N_eligible = 4 (MAX_PARTY_SIZE), WHEN a mob dies, THEN each eligible member receives exactly 700 XP.

AC-PS-13: GIVEN N_eligible = 0 at kill time (all members Ghost or OutOfZone), WHEN a mob dies, THEN 0 XP is distributed to all party members and the XP distribution log records zero awards.

AC-PS-14: GIVEN a party member is in a different zone instance from the kill event, WHEN a mob dies, THEN the out-of-zone member receives 0 XP and is not counted in N_eligible — verified via XP distribution log showing that member's awarded XP = 0.

AC-PS-15: GIVEN a party member's level differs from the current leader's level by more than 5 at kill time, WHEN a mob dies, THEN that member receives 0 XP and is excluded from N_eligible.

**Drop rate:**

AC-PS-16: GIVEN any party size (1–4), WHEN the server processes a kill event, THEN the server-side DropRollParameters passed to the loot resolver contain no party-size modifier — the PartyBonus field equals 1.0.

**Round-robin cursor:**

AC-PS-17: GIVEN a party [A, B, C] with rrNextIndex = 2, WHEN a common drop occurs, THEN the item is assigned to C and rrNextIndex is set to 0 on the subsequent AdvanceRrNextIndex call.

AC-PS-18: GIVEN rrNextIndex points at a Ghost member slot, WHEN AdvanceRrNextIndex is called, THEN the cursor skips the Ghost slot and lands on the next eligible online member.

AC-PS-19: GIVEN all party members are Ghost or OutOfZone, WHEN AdvanceRrNextIndex is called, THEN rrNextIndex is unchanged and no item is assigned.

AC-PS-20: GIVEN a party of 4 with rrNextIndex = 3, WHEN the member at index 3 leaves and the party shrinks to size 3, THEN rrNextIndex is immediately clamped to 0 (= 3 % 3) and the next drop is assigned to the member at index 0.

**Leader / membership:**

AC-PS-21: GIVEN the party leader disconnects, WHEN measured within 1 server tick, THEN leadership transfers to the lowest non-empty non-Ghost array index and all party members receive a PartyStateUpdate reflecting the new leader.

AC-PS-22: GIVEN leadership transferred after the original leader disconnected, WHEN the original leader reconnects, THEN they do NOT automatically regain leadership — the current leader is unchanged.

AC-PS-23: GIVEN players A, B, C join a party in that order, WHEN a PartyStateUpdate is sent, THEN the member array order in the payload is [A, B, C] — join order is preserved across state changes.

**Disbanding and voluntary leave:**

AC-PS-24: GIVEN a party in Disbanding state (per CR-PS-10), WHEN a kill event fires before the 10-second cleanup timeout, THEN XP is distributed to eligible members normally and the kill event log records per-member XP awards.

AC-PS-25: GIVEN a member voluntarily leaves a party, WHEN the leave completes, THEN: (a) the departing member has a new non-zero unique solo PartyID; (b) the original party's member array no longer contains the departed member's CharacterID; (c) remaining members receive a PartyStateUpdate.

**Reconnect grace:**

AC-PS-26: GIVEN a party member disconnects and reconnects within 5 seconds, WHEN the reconnect handshake is confirmed, THEN the server outbound message log shows zero PartyMemberStatusChanged (Ghost) messages for that member during the grace window.

AC-PS-27: GIVEN all party members are simultaneously Ghost, Disconnected, or OutOfZone, WHEN `AdvanceRrNextIndex` is called, THEN `rrNextIndex` is unchanged from its pre-call value, no loot is assigned, and the server log records a frozen-cursor event — verified via server rrNextIndex state log showing the same value before and after the call.

AC-PS-28: GIVEN a party [A, B, C] with LeaderIndex=2 (C is leader), WHEN member B (index 1) voluntarily leaves, THEN after compaction the party is [A, C] and LeaderIndex=1 (still pointing to C) — verified via PartyStateUpdate showing LeaderIndex=1 and member at index 1 = C's CharacterID.

AC-PS-29: GIVEN player X has a pending invite from leader A, WHEN leader B sends player X an invite, THEN leader B's client receives a send-acknowledgement (not an error), and after leader A's invite times out (60s), player X receives the invite from leader B as a new banner — verified via invite event log showing both invite messages and queue delivery order.

AC-PS-30: GIVEN a level-60 member is in a party of 4 with 3 leveling members, WHEN a mob dies, THEN N_eligible = 4 (the level-60 member is counted), each leveling member receives XP at the ×0.70 multiplier, and the level-60 member's awarded XP = 0 (AddExperience no-op) — verified via XP distribution log.

## Open Questions

| ID | Question | Owner | Blocks |
|----|----------|-------|--------|
| OQ-PS-1 | Zone Instancing architecture — can party members zone-transfer individually or only as a group? Does "OutOfZone" mean a different instance of the same zone or a different zone entirely? | Zone Instancing GDD (not yet authored) | Provisional assumption in CR-PS-5 and Dependencies |
| OQ-PS-2 | PartyID counter range and persistence — does the sequential counter reset between server restarts? What is the maximum value before wrap-around, and how is wrap-around handled? | systems-designer, network-programmer | CR-PS-1 |
| OQ-PS-3 | Original leader reconnect — does the system offer the reconnected original leader an option to reclaim leadership, or is the transfer permanent? Current spec: permanent (no reclaim). | Game Designer | CR-PS-8, AC-PS-22 |
| OQ-PS-4 | Leveling System F-LS-2 supersession — `leveling-system.md` still contains F-LS-2 as an active bonus formula (opposite direction to F-PS-1). Must be marked superseded and replaced with a reference to F-PS-1. `entities.yaml` already has the SUPERSEDED note. N range in both documents is correctly [1, 4] for MAX_PARTY_SIZE=4 — no range change needed. | Next leveling-system authoring session (priority: HIGH — two live formulas with opposite XP effects are a programmer trap) | None (flagged in Dependencies) |
| OQ-PS-5 | ~~Level range gate reference point~~ — **RESOLVED (2026-05-16):** Reference point is **leader's level** (current spec maintained). Simpler to compute; level gate shifts on leader transfer but existing members are never forcibly removed (CR-PS-3). | Resolved | — |
| ~~OQ-PS-6~~ | ~~Wire schemas pending~~ — **RESOLVED (2026-05-17):** `PartyStateUpdate` (56-byte body, R-OD), `PartyDisbanded` (4-byte body, R-OD), `PartyInviteRequest` (4-byte, C→S), `PartyInviteReceived` (15–39-byte, S→C), `PartyInviteResponse` (5-byte, C→S) added to `networking-wire-protocol.md` Party System Messages section. `PartyMemberHealthUpdate` updated to 20-byte body (added `maxHP`, `maxMP`). `PartyMemberStatus` enum added. | Resolved | — |
| OQ-PS-7 | Mob Spawning kill rate multiplier — F-PS-1's economic rationale ("parties kill faster, net XP/hour higher despite per-kill deduction") depends on an unvalidated kill rate multiplier at full party size. Cannot be confirmed until Mob Spawning GDD is authored. | Mob Spawning GDD | F-PS-1 design risk |
