# Networking Ghost Character State

> **Status**: Approved (Pass 2 lean, 2026-05-15)
> **Last Updated**: 2026-05-15 (Pass 1 revision: WAL + exhaustive snapshot fields + disconnectTickNumber; GD-CGS-2 no-HP-loss ghost death; GhostExpiredEvent(GhostDeath); DisconnectReason.GhostDeath; AC-CGS-5 added; AC-CGS-4 timestamp fix)
> **Parent**: networking-ghost-session.md

---

## Overview

Defines the authoritative ownership rules for ghost entity state during the
`Disconnected_SessionActive` phase of the session lifecycle. This primitive
specifies: HP write authority (server-only, no client prediction); `IsGhost`
flag ownership and broadcast semantics; the timing and content of the
pre-disconnect state snapshot; and the write-ordering guarantee that ensures
persistent character state is committed before any session resources are
released or events broadcast. It also resolves the death-vs-reconnect ordering
guarantee required by `networking-ghost-session.md` EC-GH-2, and the
write-ordering apparent contradiction between CR-GH-10.1 (pre-disconnect HP on
TTL expiry) and ghost death — resolved by GD-CGS-2: both paths persist
pre-disconnect HP; ghost death applies respawn position only. All cross-references
point back to specific CRs in the parent document.

---

## Player Fantasy

None. This is a pure state-ownership contract. Incorrect HP authority or
write-ordering produces invisible corruption — ghost HP changes that persist
past reconnect, or character state lost on zone crash.

---

## Detailed Rules

### CGS-1 — HP Authority During Ghost Period

The server is the sole authority for a ghost entity's HP value during
`Disconnected_SessionActive`. No client prediction, extrapolation, or
reconciliation is permitted for HP on ghost entities:

- HP in zone sync messages for a ghost entity is always server-authoritative.
  Clients MUST apply it directly — no blending with local predictions.
- Damage events processed against a ghost entity update server-side HP and
  broadcast the result via `EntityHealthUpdate` (R-U, S→RELEVANT) on the next
  zone tick (networking-channel-contract.md CCR-3).
- Ghost HP can reach zero via the standard damage pipeline (CR-GH-5/6 in
  `networking-ghost-session.md`). No ghost-specific HP floor applies.

---

### CGS-2 — IsGhost Flag Ownership and Lifecycle

The `IsGhost` flag is server-owned and server-written. Its lifecycle:

**Set**: `IsGhost = true` is written on the server at step 4 of the CR-GH-2
ghost promotion sequence, within one `ZONE_TICK_MS` of the
`Connected → Disconnected_SessionActive` transition.

**Broadcast**: Broadcast to all zone clients via the standard zone sync tick.
When false, the field is omitted from zone sync messages (zero-cost for
non-ghost entities). Clients receiving `IsGhost = true` MUST NOT apply
client-side prediction to that entity for any field (EC-GH-8 in
`networking-ghost-session.md`).

**Cleared**: `IsGhost = false` is written by the server on successful reconnect
(`Reconnecting → Connected` transition). It is broadcast within one
`ZONE_TICK_MS` of that transition.

**At expiry**: If `GHOST_COMBAT_TTL_MIN_S` expires without reconnect, `IsGhost`
is not explicitly cleared — the entity is removed from zone state entirely
(CR-GH-10). No `IsGhost = false` broadcast occurs; `GhostExpiredEvent` (R-OD,
S→ALL zone) replaces it as the authoritative removal signal.

---

### CGS-3 — Pre-Disconnect State Snapshot

When the session transitions from `Connected` to `Disconnected_SessionActive`
(heartbeat timeout, CR-GH-1), the server executes the following sequence before
setting `IsGhost = true`:

**Step 1 — Record disconnectTickNumber**: The `ServerTickNumber` of the
transition tick is stored as `disconnectTickNumber` (uint). This value is the
persistence idempotency key (EC-CGS-2).

**Step 2 — Snapshot character state**: The server takes an in-memory snapshot
containing all of the following fields as of the transition tick:

| Field | Description |
|-------|-------------|
| `currentHP` | HP at disconnect — not modified by ghost-period damage |
| `maxHP` | Maximum HP from current stat sheet |
| `currentMP` | MP at disconnect |
| `maxMP` | Maximum MP from current stat sheet |
| `Position` | World position at disconnect |
| `Rotation` | World rotation at disconnect |
| `Inventory` | Full inventory slot contents and item states |
| `XP` | Pre-disconnect XP pool (see CR-GH-9 for forfeit policy) |
| `Level` | Current level |
| `heldFreePoints` | Unallocated stat points |
| `allocatedStats` | Per-stat allocation map (Character Stats GDD) |
| `equipmentAppearanceFlags` | Equipped cosmetic/appearance state |
| `disconnectTickNumber` | ServerTickNumber of this transition (idempotency key) |
| `activeBuffs` | All active buff/debuff states and remaining durations |
| `skillCooldowns` | Per-skill cooldown remaining at disconnect |

No field may be omitted. "All other character fields" is not a valid specification;
every new field added to the character schema must be explicitly added to this
snapshot table.

**Step 3 — Write-ahead log (WAL)**: Immediately after the snapshot is taken,
the snapshot is written to the external WAL storage. The WAL entry is keyed on
`(characterId, disconnectTickNumber)`. This write must complete before ghost
promotion continues (before `IsGhost = true` is set in CGS-2).

**WAL durability guarantee**: The WAL entry survives all zone failure modes —
OOM kill, hardware shutdown, crash handler not running. If the zone crashes
before formal persistence (CGS-4/5), recovery reads the WAL entry to determine
the durable character state. Any WAL entry for a session in
`Disconnected_SessionActive` or later is applied as the canonical character state.

This snapshot is the source of truth for persistence on both TTL expiry (CGS-4)
and ghost death (CGS-5).

---

### CGS-4 — Write-Ordering on TTL Expiry (No Ghost Death)

When `GHOST_COMBAT_TTL_MIN_S` expires without ghost death and without reconnect,
the server MUST commit persistence before releasing session resources or
emitting zone events. This is an application of `networking-core.md` CR-NET-5
(commit-before-broadcast) to the ghost cleanup sequence:

1. Apply the WAL entry (CGS-3 Step 3) to the character persistence record.
   The WAL guarantees durability — this step is idempotent (same snapshot keyed
   on `characterId` + `disconnectTickNumber`).
2. On persistence confirmed: remove ghost entity from zone, release party slot,
   transition session to `Disconnected_SessionExpired`.
3. After session transition: emit `GhostExpiredEvent(reason: GhostTtlExpired)` (R-OD, S→ALL zone).

If a crash occurs between steps 1 and 3, character state is durable. Recovery
on server restart: the session is already `Disconnected_SessionExpired` or
recoverable from the persistence record — no action required.

**HP persisted**: Pre-disconnect snapshot HP (CGS-3). Ghost-period damage is
not persisted. The character resumes at their pre-disconnect HP on next login.

---

### CGS-5 — Write-Ordering on Ghost Death (GD-CGS-2: No HP Loss)

If the ghost entity's HP reaches zero during the ghost period (CR-GH-6), the
following sequence runs (commit-before-broadcast per CR-NET-5):

1. Record `wasKilledWhileDisconnected = true` in the session record.
2. Apply respawn position only: set the character's zone position to the zone
   entry respawn point (Death & Respawn GDD). **HP is not modified** — the
   pre-disconnect snapshot `currentHP` (CGS-3) is preserved as the canonical HP.
3. Persist character state: apply the WAL entry (CGS-3 Step 3) to external
   persistence with the respawn position substituted for the snapshot position.
   The persisted `currentHP` equals the pre-disconnect snapshot HP. This step
   must complete before any zone event is emitted (CR-NET-5).
4. After persistence confirmed: emit `GhostExpiredEvent(reason: GhostDeath)` (R-OD,
   S→ALL zone) so all zone clients remove the ghost entity from their local state.
5. Transition session to `Disconnected_SessionExpired`.
6. If the player reconnects after ghost death: the reconnect is rejected via
   `ZoneSessionEnded(reason: DisconnectReason.GhostDeath)` delivered to the
   reconnecting client transport. The client enters the respawn flow. Full
   rejection handshake spec deferred to Character Persistence GDD.

**HP persistence (GD-CGS-2)**: Ghost death applies no HP penalty. The character
reconnects at their pre-disconnect HP, at the respawn position. This eliminates
the Pillar 1 (Earned Power) conflict — HP earned through gameplay cannot be
stripped by a disconnect event. It also closes the low-HP ghost exploit:
deliberately disconnecting at low HP to avoid a death penalty is moot, because
ghost death applies no HP penalty either.

**Post-disconnect XP forfeiture still applies** (CR-GH-9.1): XP shares
accumulated while `IsGhost = true` are forfeited on ghost death. Pre-disconnect
XP (CGS-3 snapshot) is unaffected.

**Reconciliation with CR-NET-6.5**: Both CGS-4 (TTL expiry) and CGS-5 (ghost
death) persist the CGS-3 WAL snapshot HP. The paths differ only in position
(snapshot position vs. respawn position) and zone signal
(`GhostExpiredEvent(GhostTtlExpired)` vs. `GhostExpiredEvent(GhostDeath)`).
No contradiction with CR-NET-6.5 or CR-GH-10.1.

---

### CGS-6 — Death-vs-Reconnect Ordering Guarantee (Resolves EC-GH-2)

A race exists between ghost death processing and a reconnect acknowledgment
arriving in the same server tick. Resolved by a single-tick priority rule:

**Rule**: In any server tick where both a killing blow and a reconnect
acknowledgment are queued for the same session, death processing takes
priority.

Sequence:
1. Process death first: record `wasKilledWhileDisconnected = true`. HP is
   **not** modified — the pre-disconnect snapshot HP is preserved (GD-CGS-2).
   Transition session to `Disconnected_SessionExpired` with reason `GHOST_DEATH`.
2. Discard the reconnect acknowledgment: the session is already
   `Disconnected_SessionExpired`; reconnect requires a new session.
3. Deliver `ZoneSessionEnded(reason: DisconnectReason.GhostDeath)` to the
   reconnecting client transport. The reconnect is rejected; the client enters
   the respawn flow (full rejection spec deferred to Character Persistence GDD).

**Implementation note**: Enforced by processing the death event queue before
the reconnect queue within each tick's event dispatch loop. The single-threaded
tick loop is the serialization point — no additional lock is required.

---

## Formulas

None. Timing values (`GHOST_COMBAT_TTL_MIN_S`, `ZONE_TICK_MS`, `MOB_AI_TICK_MS`)
are defined in `networking-ghost-session.md` Formulas and Tuning Knobs. This
document is a state-ownership and ordering contract, not a quantitative spec.

---

## Edge Cases

**EC-CGS-1: Ghost death and TTL expiry in same tick**
If ghost HP reaches zero AND the TTL timer fires in the same server tick, death
processing takes priority (CGS-6 priority rule applied analogously). The session
closes with reason `GHOST_DEATH`, not `GHOST_TTL_EXPIRED`. The death path runs;
the TTL expiry path is cancelled.

**EC-CGS-2: Double persistence prevention**
If a crash occurs during CGS-4 step 1 (persistence write in progress), the next
server process start must detect the partial write and either complete or roll
back before accepting any connection for that character. Character ID MUST be write-locked during persistence to prevent concurrent
double-writes. The persistence layer MUST be idempotent for the same snapshot
(keyed on `characterId` + `disconnectTickNumber` (as defined in CGS-3 Step 1)):
a re-submitted identical snapshot is a no-op, not a second write.

**EC-CGS-3: Zone crash during ghost death processing**
If the zone crashes after death processing begins (CGS-5 step 1) but before
persistence completes, recovery (CR-GH-11 in `networking-ghost-session.md`)
uses the latest available durable snapshot. If the WAL entry (CGS-3 Step 3) was committed before the crash: character
resumes at pre-disconnect snapshot HP and respawn position (WAL guarantees
the snapshot HP is always durable regardless of whether CGS-5 step 3 completed).
If the WAL entry was not yet written: character resumes at the last external
persistence record. `wasKilledWhileDisconnected` may be unset in either case.
The client's "You were defeated while offline" notification may not appear in
this edge case.

---

## Dependencies

| System | Contract required |
|--------|-------------------|
| `networking-ghost-session.md` | Parent. This document resolves CR-GH-10.1, EC-GH-2, and the ghost-death write-ordering gap. All CGS rules are referenced from specific CRs in that document. |
| `networking-session.md` | ST-NET-1 state machine (`Connected`, `Disconnected_SessionActive`, `Reconnecting`, `Disconnected_SessionExpired`). Transition triggers define when CGS-3 snapshot is taken and when CGS-4/5 write-ordering runs. |
| `networking-core.md` | CR-NET-5 (commit-before-broadcast) is the parent principle for CGS-4 write-ordering. |
| `networking-channel-contract.md` | CCR-3 channel assignments for `EntityHealthUpdate` (R-U, S→RELEVANT) and `GhostExpiredEvent` (R-OD, S→ALL). |
| **Death & Respawn** *(not yet authored)* | Respawn HP value used in CGS-5. This document defers the exact respawn HP to that GDD. |

---

## Tuning Knobs

None. This document defines state ownership and ordering rules, not tunable
parameters. All timing values are tuned in `networking-ghost-session.md`.

---

## Acceptance Criteria

**AC-CGS-1: Pre-disconnect HP not modified by ghost-period damage (TTL expiry path)**
Given a ghost entity that receives N damage during the ghost period and then
expires via TTL without reconnect,
when character state is written to persistence (CGS-4 step 1),
then the persisted HP equals the HP snapshot taken at the
`Connected → Disconnected_SessionActive` transition, not the ghost-period-
reduced HP.
Pass condition: `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostCombatTTLExpiry)` fires before `OnSessionStateTransitioned(_, Disconnected_SessionExpired)`; character persistence record HP = disconnect-moment HP. Automatable.

**AC-CGS-2: Ghost death persists pre-disconnect HP (no HP penalty on ghost death)**
Given a ghost entity whose HP reaches zero during the ghost period,
when death processing completes and character state is persisted (CGS-5),
then the persisted HP equals the pre-disconnect snapshot HP (CGS-3), not the
respawn HP; the persisted position equals the zone-entry respawn position.
Pass condition: `OnPersistenceWriteCompleted(characterId, PersistenceWriteReason.GhostDeath)` fires; character persistence record HP = pre-disconnect snapshot HP; character persistence record position = respawn position; `wasKilledWhileDisconnected = true` in session record. Automatable.

**AC-CGS-5: Ghost HP is server-authoritative — no client prediction applied**
Given a ghost entity receiving damage from a mob during the ghost period,
when the server processes the hit and updates ghost HP,
then the updated HP is broadcast via `EntityHealthUpdate` (R-U, S→RELEVANT) on
the next zone tick, and all zone clients apply the value directly without
client-side prediction or blending.
Pass condition: `INetworkTestObserver` records `EntityHealthUpdate` for the ghost entity on the tick following the damage event; the `EntityHealthUpdate.HP` value matches the server-authoritative ghost HP; no client receives or applies a predicted HP value for the ghost entity. Automatable.

**AC-CGS-3: Death takes priority over simultaneous reconnect**
Given a ghost entity whose HP reaches zero AND a reconnect acknowledgment
queued in the same server tick,
when the tick processes both events,
then the death event is processed first, session transitions to
`Disconnected_SessionExpired` with reason `GHOST_DEATH`, and the reconnecting
client receives `ZoneSessionEnded(reason: DisconnectReason.GhostDeath)`.
Pass condition: `OnSessionStateTransitioned(accountId, Reconnecting, Disconnected_SessionExpired, "GhostDeath")` fires; reconnect client receives `ZoneSessionEnded` with `reason = DisconnectReason.GhostDeath`. Automatable via `ITransportFaultInjector` (inject reconnect ACK in same tick as injected killing blow).

**AC-CGS-4: Persistence committed before session resource release on TTL expiry**
Given a ghost TTL expiry,
when the CR-GH-10 cleanup sequence runs,
then `OnPersistenceWriteCompleted` fires before the session transitions to
`Disconnected_SessionExpired` and before `GhostExpiredEvent` is emitted.
Pass condition: observer callback sequence — `OnPersistenceWriteCompleted` fires before `OnSessionStateTransitioned(_, _, Disconnected_SessionExpired, _)` fires, which fires before `GhostExpiredEvent` is observed on any zone client. Callback order is verified by sequence index (a monotonically incrementing counter reset per tick) — not timestamps, which are meaningless for events within the same 50ms tick. Automatable via `IServerCrashInjector.AfterGhostCleanupPersistenceWrite` (crash after step 1 — character state must be durable on recovery).
