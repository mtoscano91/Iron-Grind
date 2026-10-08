# Architecture Traceability Index

> **Last Updated:** 2026-10-08
> **Engine:** Unity 6.3 LTS (6000.3)
> **Source review:** `docs/architecture/architecture-review-2026-10-07.md` (full); `docs/architecture/architecture-review-2026-10-08.md` (ADR-012 only); `docs/architecture/architecture-review-2026-10-08-rereview.md` (ADR-012 lean re-review)

## Coverage Summary (domain-level)

- Systems indexed (systems-index.md): 38 Approved with docs (+2 Draft primitives), 6 Not Started UI/Audio/Meta
- ADRs on disk: 12 (12 Accepted)
- Domains with ADR coverage: 12
  - Fully covered: 5 (networking library, navigation execution, navigation lifecycle, HUD UI, combat UI)
  - Partial or in conflict: 7 (shop transactions, persistence, hosting, zone load/teardown, messaging — inbound requests, async persistence, server/client code isolation — ADR Accepted, code not yet moved)
- Schema gaps: 2 (hotbar assignments, respec reservation state)
- Cross-ADR conflicts: 2 (C1 zone process supervision, C2 teardown order)
- Per-requirement TR-IDs in `tr-registry.yaml`: **0** — the 12 EPIC files use 94 TR-IDs that are not registered

## Domain Coverage Matrix

| Domain | Representative Systems | ADR | Status | Notes |
|---|---|---|---|---|
| Networking transport/library | networking-core, networking-wire-protocol, networking-session, client-side-prediction, movement-system | ADR-004 | ✅ Covered | OQ-ADR4-3 open |
| Navigation execution | navigation-pathfinding | ADR-002 | ✅ Covered | — |
| Navigation agent lifecycle | navigation-pathfinding, enemy-ai | ADR-003 | ✅ Covered | Depends on ADR-002 |
| Shop transaction integrity | npc-shop, currency-system, inventory-system, consumable-use-system | ADR-001 | ⚠️ Partial | P3: purchase flow not restated for ADR-011; OQ-ADR1-2 open |
| HUD UI framework | hud | ADR-005 | ✅ Covered | — |
| Combat UI framework | combat-ui | ADR-008 | ✅ Covered | `sortingOrder = 1` |
| Persistence storage engine and schema | character-persistence, authentication | ADR-006 (+ Amendment 1) | ⚠️ Partial | G1 hotbar, G2 respec reservation not in schema; stale text (TD-059, pool size) |
| Hosting backend | zone-instancing, networking-core (infra) | ADR-007 | ⚠️ Conflict | C1: `Restart=always` vs ADR-009 zero-exit close; stale "ADR-006 Proposed" |
| Zone scene load and teardown | zone-instancing, navigation-pathfinding | ADR-009 | ⚠️ Partial | C2: teardown lacks `Dispose()` pass and shutdown drain; P4: startup lacks NGO start, static-data-ready, wiring |
| Cross-system messaging | all gameplay systems | ADR-010 | ⚠️ Partial | Events covered; P1: no decision owns the inbound request dispatcher; naming rule out of step with `src/` |
| Async persistence in the tick loop | enhancement-system, character-persistence, networking-core, loot-table-system | ADR-011 | ⚠️ Partial | P1 dispatcher, P2 second irreversible outcome with a closed gate, P5 expected `SaveVersion` ownership |
| Server/client code isolation | damage-calculation, currency-system, hit-detection, enemy-ai, navigation-pathfinding | ADR-012 | ⚠️ Partial | Accepted 2026-10-08 (N1–N5 of the re-review folded in). Decision covered. Migration Plan step 2 implemented 2026-10-08: all nine systems are in `IronGrind.ServerLogic` and the boundary test (Decision 6 check 1) gates `IronGrind.Foundation`. Still partial: the client-binary scan (check 2, closes AC-DC-I-01 and AC-CS-G-01) and the content check (check 3) need a build pipeline |
| Core gameplay/data/economy/progression | character-stats, damage-calculation, skill-system, status-effects, equipment-system, enhancement-system, loot-table-system, leveling-system, party-system, et al. | — | No per-system ADR | By design — pure design/data |
| URP render / VFX | VFX System, Map/Minimap | — | Deferred | GDDs Not Started |
| Audio | Audio System | — | Deferred | GDD Not Started |

## Known Gaps / Open Items

Most foundational first (full text in the 2026-10-07 review; item 0 in the 2026-10-08 review):

0. **ADR-012 — Accepted 2026-10-08; follow-ups open.** `/create-control-manifest update` (assembly rules); GDD wording pass (ADR-012 Migration Plan step 5); rewrite Damage Calculation Story 006 against the two-list boundary test — its header still says the ADR does not exist. Currency Group G is eighth in the move order.
1. **P1 — Inbound request dispatch and full tick order.** No ADR. Suggested: `/architecture-decision` ADR-013 (the number 012 is taken by the assembly boundary), or an ADR-010 amendment. Blocks Enhancement Story 009.
2. **C2 / P4 — ADR-009 teardown and startup sequences.** ADR-009 Amendment 1; also the exit call (`Process.Exit(0)` is not a .NET API — engine, unconfirmed).
3. **C1 — Zone process supervision.** ADR-007 amendment (`Restart=on-failure`, single spawner, `ZoneID` minted at registration).
4. **P2 / P5 — ADR-011 clarification.** Second irreversible outcome while the gate is closed; who supplies the expected `SaveVersion` for a queued write.
5. **G1 / G2 — Persistence schema.** Hotbar assignments (`consumable-use-system.md` Rule 7) and respec reservation state (`leveling-system.md` CR-4.1): Character Persistence amendment + ADR-006 Amendment 2.
6. **P3 — ADR-001 Amendment A2.** Asynchronous purchase flow; `PendingSell` (OQ-ADR1-2).
7. **`architecture.md` refresh** — still at 8 ADRs; lists ADR-009/010 as missing.

Other open items:
- ADR-004 OQ-ADR4-3: `CustomMessagingManager` vs. thin UTP wrapper
- TD-046: Enhancement wire messages and an inventory slot-update message (wire-protocol authoring session)
- TD-059: Loot Table GDD has no "pickup deferred while the gate is closed" note
- ADR-011 headless-build checks (4) and the device/headless gates carried from ADR-002, 004, 005, 006, 008
- 6 MVP GDDs not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding/Beginner Zone

## Superseded Requirements

- ADR-006 `inventory_slots` entry shape `{item_id, count}` → `{item_id, count, enhancement_level}` (Amendment 1, 2026-10-01; `inventory-system.md` Rule 1.4, `character-persistence.md` `InventoryEnhancementLevels[20]`).
- `enhancement-system.md` OQ-ENH-7 (held requests during an attempt) → resolved by ADR-011 Decision 4.

## History

| Date | Verdict | ADRs | Notes |
|------|---------|------|-------|
| 2026-06-21 | FAIL | 5 (4 Accepted, 1 Proposed) | Foundation gaps: Persistence, Hosting, Combat UI ADRs missing |
| 2026-06-27 | CONCERNS → **PASS** | 8 (8 Accepted) | 3 new ADRs promoted to Accepted; all cleanup fixes applied |
| 2026-10-07 | **CONCERNS** | 11 (11 Accepted) | ADR-009/010/011 added. 2 conflicts (zone supervision, teardown order), 5 partial items, 2 schema gaps; `architecture.md` stale |
| 2026-10-08 | **CONCERNS** (ADR-012 only) | 12 (11 Accepted, 1 Proposed) | ADR-012 reviewed alone: decision sound and confirmed against the 6.3 manual; 3 items to fix before acceptance (migration order, HUD dependencies, boundary test granularity), 8 concerns |
| 2026-10-08 | ADR-012 lean re-review: no blocker (project-wide **CONCERNS** unchanged) | 12 (11 Accepted, 1 Proposed) | Amended ADR-012 (`54c7856`): B1–B3 and C1–C7 closed; 5 small items N1–N5; ready for acceptance |
| 2026-10-08 | ADR-012 **Accepted** (no review run) | 12 (12 Accepted) | N1–N5 folded into the ADR; status changed by the user's instruction |
