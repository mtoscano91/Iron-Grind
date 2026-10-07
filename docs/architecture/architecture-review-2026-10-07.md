# Architecture Review Report

> **Date:** 2026-10-07
> **Engine:** Unity 6.3 LTS (6000.3)
> **GDDs:** 38 Approved (+2 Draft primitives), 6 Not Started
> **ADRs Reviewed:** 11 (11 Accepted)
> **Mode:** `/architecture-review` (full, lean)
> **Prior review:** `architecture-review-2026-06-27.md` (PASS, 8 ADRs)
> **Verdict:** **CONCERNS** — nothing blocks the work in progress, but ADR-009, ADR-010 and ADR-011 left gaps and two cross-ADR conflicts, and two persisted-state requirements have no schema.

## Scope Note

Coverage is assessed at **domain granularity**, as in the 2026-06-21 and 2026-06-27 passes.

- **Read in full:** ADR-001, ADR-004, ADR-006, ADR-007, ADR-009, ADR-010, ADR-011, `architecture.md`, `design/gdd/systems-index.md`, `breaking-changes.md`, `deprecated-apis.md`.
- **Checked by grep only:** ADR-002, ADR-003, ADR-005, ADR-008 (each changed by one line since 2026-06-27), and the GDDs — at the rules the ADRs cite (`character-persistence.md` CR-CP-5/6/7, `zone-instancing.md` CR-ZI-12, `networking-core.md` CR-NET-5, `consumable-use-system.md`, `leveling-system.md`, `npc-shop.md`, `class-system.md`). The GDDs were not re-read in full.
- **Not run:** the `unity-specialist` consultation (user decision, as on 2026-06-27). Items marked *(engine, unconfirmed)* are the reviewer's reading only.
- **TR registry:** left empty (user decision). The 12 EPIC files use 94 TR-IDs that are not in `tr-registry.yaml`.

---

## Changes Since 2026-06-27

| Change | Date |
|---|---|
| ADR-009 Scene/Zone-Load Management — Accepted | 2026-06-27 |
| ADR-010 Event/Messaging Architecture — Accepted | 2026-06-27 |
| ADR-006 Amendment 1 — `inventory_slots` entries carry `enhancement_level` | 2026-10-01 |
| ADR-011 Asynchronous Persistence in the Server Tick Loop — Accepted, clarified the same day | 2026-10-07 |
| Control manifest 2026-10-07 (ADR-011 rules) | 2026-10-07 |
| GDD amendments: Inventory (per-slot enhancement level), Item Database #4 (scrolls), Loot Table CR-LT-9.1 and CR-LT-16, Currency (`AuctionBid`), Enhancement Passes 6/7 | 2026-10-01 … 2026-10-07 |

---

## Traceability Summary (domain level)

| Domain | Representative systems | ADR | Status |
|---|---|---|---|
| Networking library / envelope / clock | networking-core, wire-protocol, session, CSP, movement | ADR-004 | ✅ Covered |
| Navigation execution | navigation-pathfinding | ADR-002 | ✅ Covered |
| Navigation agent lifecycle | navigation-pathfinding, enemy-ai | ADR-003 | ✅ Covered |
| Shop purchase integrity | npc-shop, currency, inventory, consumable-use | ADR-001 | ⚠️ Partial — flow not restated for ADR-011 (P3) |
| HUD UI framework | hud | ADR-005 | ✅ Covered |
| Combat UI framework | combat-ui | ADR-008 | ✅ Covered |
| Persistence engine and schema | character-persistence, authentication | ADR-006 | ⚠️ Partial — two persisted items have no schema (G1, G2) |
| Hosting backend | zone-instancing, networking-core (infra) | ADR-007 | ⚠️ Conflict with ADR-009 (C1) |
| Zone scene load and teardown | zone-instancing, navigation-pathfinding | ADR-009 | ⚠️ Partial — teardown and startup incomplete (C2, P4) |
| Cross-system messaging | all gameplay systems | ADR-010 | ✅ Covered for events; ⚠️ inbound requests (P1) |
| Async persistence in the tick | enhancement, character-persistence, networking-core, loot-table | ADR-011 | ⚠️ Partial — P1, P2, P5 |
| Core gameplay / data / economy / progression | stats, damage, skill, status, equipment, loot, leveling, party, et al. | — | No per-system ADR, by design (unchanged) |
| URP render / VFX | VFX System, Map/Minimap (Not Started) | — | Deferred — GDDs not authored |
| Audio | Audio System (Not Started) | — | Deferred — GDD not authored |

Totals over the 11 ADR-backed domains: 5 covered, 6 partial or in conflict, plus 2 schema gaps.

---

## Coverage Gaps (no ADR or schema)

### G1 — Hotbar assignments are not in the persistence schema
- `consumable-use-system.md` Rule 7 and AC-CUS-17 require hotbar assignments (2 × `AssignedItemID`) in the character record. Its bidirectionality table still lists `character-persistence.md` as "To verify".
- `character-persistence.md` (23 logical fields) has no hotbar field; the ADR-006 DDL has no column.
- **Needs:** a Character Persistence amendment and ADR-006 Amendment 2. Domain: Persistence. Engine risk: LOW.

### G2 — Respec reservation state is not in the persistence schema
- `leveling-system.md` CR-4.1: "Reservation state is persisted to the database during Phase 1 so it survives a server crash."
- Neither `character-persistence.md` nor ADR-006 has a table or column for it, and no interface method writes it.
- **Needs:** the same amendment pair as G1. Domain: Persistence. Engine risk: LOW.

---

## Cross-ADR Conflicts

### C1 — ADR-007 vs ADR-009: zone process supervision
Type: Integration.
- **ADR-007** runs zone processes as systemd units with `Restart=always` (also control manifest, Foundation). It also says "the gateway spawns and monitors child processes".
- **ADR-009** Decision 3 ends a deliberate zone close (T-5 / T-6) with a zero exit code.
- **Impact:** `Restart=always` restarts a unit that exits 0, so a zone that closed on purpose comes back empty. The restarted unit reuses its arguments, against `zone-instancing.md` CR-ZI-12 step 8 ("`ZoneID` removed from routing table and retired. Never reallocated."). Two supervisors (gateway and systemd) are named with no rule for which one starts a zone.
- **Resolution options:**
  1. `Restart=on-failure`; the gateway is the only component that starts a zone; the `ZoneID` is minted at gateway registration, not passed by the unit.
  2. Keep `Restart=always` and exit non-zero on crash only by never exiting deliberately — the gateway stops the unit instead. More moving parts.

### C2 — Zone teardown order is spread over five places
Type: Integration / Dependency.
- **ADR-002** Decision 3: stop the tick, wait, nav teardown.
- **ADR-009** Decision 3: tick stop → wait → `ZoneNavigationService.Teardown()` → CR-ZI-12 steps → exit.
- **`zone-instancing.md` CR-ZI-12:** eight steps; step 2 calls `SaveSession` for every occupied slot; step 5 requires those saves to be complete, or queued with guaranteed ordering, before `NotifyZoneExit`.
- **ADR-010** Decision 4: teardown must call `Dispose()` on all zone-scoped services before exit.
- **ADR-011** Decision 2: one bounded blocking wait on tracked tasks, then a last drain, "after the tick loop has stopped, before exit".
- **Impact:** ADR-009's list contains neither the dispose step nor the drain. CR-ZI-12 step 2 starts up to 50 `SaveSession` tasks after the tick loop has stopped, so the drain has to come after that step and before step 5; ADR-011 does not say so. An implementer following ADR-009 alone exits with saves in flight.
- **Resolution:** one ADR-009 amendment that writes the full sequence, including the `Dispose()` pass, the drain position, and gateway deregistration.

---

## Partial Coverage

### P1 — The inbound request dispatcher has no owning decision
- ADR-011 Decision 4 relies on "the session's inbound request dispatcher — the single point between the RPC guard chain and game logic (ADR-010 Decision 5)".
- ADR-010 Decision 5 defines per-handler `Queue<T>` instances drained by each system's own `Tick()`. It does not define a single dispatcher. Its example uses `[ServerRpc]` for an attack request, which ADR-004 Decision 4 excludes for gameplay messages (project envelope over `CustomMessagingManager`).
- Not defined in any ADR: request descriptors (`HeldDuringIrreversibleWrite`), arrival order across message types, where ADR-001 dedup and rate limiting sit relative to the hold queue, and the complete tick order (drain → held requests → new requests → ADR-002 phases 1–4 → batch send).
- `src/Foundation/Networking/` has `CrossCuttingRpcGuardChain` and `ServerTickLoop` (generic `RegisterTickDriven`, registration order) but no dispatcher.
- This is the blocker already recorded on Enhancement Story 009.
- **Needs:** ADR-012 (Inbound Request Dispatch and Tick Order) or an ADR-010 amendment. May also close ADR-004 OQ-ADR4-3.

### P2 — A second irreversible outcome while the gate is closed
- `ICharacterMutationGate.Close` throws if the gate is already closed.
- ADR-011 covers client requests (held) and server-originated bag mutations (deferred). It does not cover a server-originated irreversible outcome for the same character — for example a level-up from a kill (`character-persistence.md` CR-CP-5 lists the Leveling System as a `SaveIrreversibleOutcome` caller) while an enhancement write is in flight.
- `networking-core.md` CR-NET-5.1 also lists "gold mutation" as an irreversible outcome; which gold mutations go through `SaveIrreversibleOutcome` is not pinned down.
- **Needs:** an ADR-011 clarification — the coordinator queues or defers a second `Begin` for a character whose gate is closed — and a definitive list of irreversible callers.

### P3 — ADR-001 purchase flow under ADR-011
- ADR-011 Related Decisions: "`BeginPurchase` / `CompletePurchase` are persistence calls and follow Decision 1 and 2."
- ADR-001 Decision 2 and `npc-shop.md` CR-SHOP-5 still describe a synchronous sequence. Not specified: when in-memory gold is debited relative to the `BeginPurchase` task, what a duplicate `BuyRequest` receives while the first is in flight (no cached result yet), and whether a purchase closes the mutation gate (ADR-011 says the gate applies only to `SaveIrreversibleOutcome`).
- **Needs:** ADR-001 Amendment A2, naturally together with the open OQ-ADR1-2 (`PendingSell`).

### P4 — ADR-009 startup sequence is missing steps
- No NGO server start, although the authoritative tick is `NetworkManager.ServerTime.Tick` (ADR-004 Decision 5) and the sequence starts the tick loop.
- No "static data ready" step. `loot-table-system.md` CR-LT-16 (2026-10-07) requires a ready Item Database before loot table validation; `IItemDatabase` exposes `IsReady` / `OnDatabaseReady`.
- No service wiring step, which ADR-010 Decision 2 assigns to `IZoneStartupService.StartZone()`.
- No persistence pool initialisation.
- **Needs:** the ADR-009 amendment of C2.

### P5 — Expected `SaveVersion` ownership under queued writes
- ADR-011 Decision 3: everything a persistence method needs is snapshotted in the synchronous prefix.
- ADR-006 / CR-CP-7: writes for one character are queued, never parallel. AC-CP-21 expects two queued `SaveIrreversibleOutcome` calls to complete as N+1 then N+2.
- That holds only if the persistence layer supplies the expected version when a write leaves the queue, not when the call is made. No document says which. ADR-011's risk mitigation ("the session-TTL save of the rolled-back state fails on `save_version`") depends on the same rule.
- **Needs:** one sentence in ADR-011 Decision 3 or ADR-006.

---

## ADR Dependency Order

No dependency cycles. Every `Depends On` target is Accepted.

```
Level 0 (no dependencies):
  ADR-001  Purchase Transaction Integrity
  ADR-002  NavMesh Service Execution Contract
  ADR-004  Networking Library — NGO
  ADR-005  HUD UI Framework
Level 1:
  ADR-003  Nav Agent Lifecycle            → ADR-002
  ADR-006  Persistence Layer              → ADR-004
  ADR-008  Combat UI Framework            → ADR-005, ADR-004
  ADR-010  Event/Messaging Architecture   → ADR-004
Level 2:
  ADR-007  Hosting Backend                → ADR-004, ADR-006
Level 3:
  ADR-009  Scene/Zone-Load Management     → ADR-002, ADR-004, ADR-007
Level 4:
  ADR-011  Async Persistence in the Tick  → ADR-006, ADR-007, ADR-010
```

ADR-001 declares "Depends On: None" but now relies on ADR-006 Decision 3 and ADR-011; record that in Amendment A2.

---

## Engine Compatibility

Engine: Unity 6.3 LTS (6000.3). ADRs with an Engine Compatibility section: 11 / 11. All agree on 6.3.

- **Deprecated API references:** none.
- **Stale version references:** none.
- **Post-cutoff API conflicts:** none. ADR-009 uses the `Scene` struct (not `Scene.handle`), field-only `[SerializeField]`, and bans `SetupRenderPasses` — consistent with `breaking-changes.md`. ADR-011 uses no post-cutoff API and deliberately avoids `UnityEngine.Awaitable`.
- **Open verification gates:** ADR-011's four headless-build checks (thread-pool completion without the player loop plus one real Npgsql write, shutdown drain, EditMode determinism, SIGTERM), added to those carried from earlier ADRs.

**Findings:**
1. 🟠 *(engine, unconfirmed)* `Process.Exit(0)` appears in ADR-009 (6 places), ADR-011 (2) and the control manifest (2). `System.Diagnostics.Process` has no static `Exit`. The intended call is `Environment.Exit(0)` or `Application.Quit(0)`; they differ in whether Unity's quit callbacks run, which matters to ADR-011's "not from `OnApplicationQuit`" rule.
2. 🟡 *(engine, unconfirmed)* ADR-009 Decision 4 step 7 writes `NetworkManager.Singleton.StartClient(newIP, newPort)`. In NGO `StartClient()` takes no arguments; the address is set on the transport (`UnityTransport.SetConnectionData`). Post-MVP path.
3. 🟡 ADR-007 still calls ADR-006 "Proposed" (Depends On, Related Decisions).
4. 🟡 ADR-006 is stale in three places: "Mono or IL2CPP" and the Mono suggestion in the risk table (TD-059; ADR-007 fixes IL2CPP), `Maximum Pool Size=20` and `Username=srv` (the control manifest makes ADR-007's `MaxPoolSize=10` authoritative), and "Hosting Backend ADR (future)".
5. 🟡 ADR-010's naming rule (`I[SystemName]Service`, two grandfathered exceptions) no longer matches practice: `src/` has about 15 role-named interfaces (`IItemDatabase`, `IClassRegistry`, `IEnhancementBonusProvider`, `IItemReservation`, `ILootDropSink`, …) and ADR-011 adds `ITickCompletionQueue` and `ICharacterMutationGate`. Scope the rule to a system's facade interface.

**Source scan (`src/`, 2026-10-07):** no `await`, `.Result`, `.Wait(`, `GetResult(` or `ContinueWith(`; no `EventBus` or `UnityEvent` outside comments; no lambda event subscriptions; no `FindObjectsOfType`, `SetupRenderPasses` or `Scene.handle`; all 14 event argument types are `readonly struct`.

---

## GDD Revision Flags (Architecture → Design)

No GDD assumption conflicts with verified engine behaviour.

Design follow-ups from accepted ADRs (not engine-driven):

| GDD | Item | Source | Action |
|---|---|---|---|
| `loot-table-system.md` | No "pickup deferred while the gate is closed" note | ADR-011 Decision 4 | Already logged as TD-059 |
| `character-persistence.md` | Hotbar assignments and respec reservation not in the record | G1, G2 | Amend with ADR-006 Amendment 2 |
| `class-system.md` SA-1 | "mob spawn events on the same event bus" | ADR-010 forbids a central bus | Reword at next edit |
| `npc-shop.md` CR-SHOP-5 | Synchronous purchase sequence | ADR-011 | Update with ADR-001 Amendment A2 |

No status changed in `systems-index.md`.

---

## Architecture Document Coverage

`docs/architecture/architecture.md` (v1.0, 2026-06-27) is stale:

- Header and ADR Audit list ADR-001…008 only.
- The layer map and Module Ownership mark Scene/Zone-Load Management and Event/Messaging as "NO ADR (Required)"; both are Accepted (ADR-009, ADR-010). "Required ADRs" items 1–2 and the first two Open Questions are resolved.
- Data Flow 2 shows the synchronous commit; ADR-011 splits it across ticks (coordinator, completion queue, mutation gate).
- Initialisation order has no scene load, bounds validation or gateway registration (ADR-009 Decision 2).
- API Boundaries lack `ITickCompletionQueue`, `ICharacterMutationGate`, `IZoneStartupService` and the ADR-010 tiers.
- Principle 3 should name ADR-011; Module Ownership should show `enhancement_level` on inventory slots (ADR-006 Amendment 1).

Every system in `systems-index.md` appears in the layer map. No orphaned architecture.

---

## Verdict: CONCERNS

No Foundation or Core requirement of a system in implementation is uncovered, and no conflict blocks the stories in progress. The conflicts and partial items are all resolvable by amendment; none requires reversing a decision.

### Required Work (most foundational first)

1. **Inbound request dispatch and full tick order** (P1) — ADR-012 or an ADR-010 amendment. Unblocks Enhancement Story 009.
2. **ADR-009 Amendment 1** — full teardown and startup sequences (C2, P4), the exit call (Engine finding 1).
3. **ADR-007 amendment** — supervision model (C1), stale ADR-006 status.
4. **ADR-011 clarification** — second irreversible outcome with a closed gate (P2), expected `SaveVersion` ownership (P5).
5. **Character Persistence amendment + ADR-006 Amendment 2** — hotbar and respec reservation (G1, G2), plus the TD-059 cleanup.
6. **ADR-001 Amendment A2** — asynchronous purchase flow and `PendingSell` (P3, OQ-ADR1-2).
7. **Refresh `architecture.md`** to 11 ADRs.

Items 1, 2 and 4 touch the planned `TickCompletionQueue` / `CharacterMutationGate` story. The queue and the gate can be built first; the coordinator and the dispatcher depend on those answers.

### Open Items Carried Forward

- ADR-001 OQ-ADR1-2 (`SellRequest` atomicity) — folded into item 6.
- ADR-004 OQ-ADR4-3 (`CustomMessagingManager` vs a thin UTP wrapper) — may close with item 1.
- TD-046 (Enhancement wire messages, inventory slot-update message) — wire-protocol authoring session.
- 6 MVP GDDs not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding.
- `tr-registry.yaml` empty while 94 TR-IDs are in use in EPIC files.

---

## Handoff

- **Re-run trigger:** re-run `/architecture-review` after items 1–4 are written.
- **Gate guidance:** the project is already in Pre-Production; this verdict is advisory.
