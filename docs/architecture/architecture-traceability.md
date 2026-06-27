# Architecture Traceability Index

> **Last Updated:** 2026-06-27
> **Engine:** Unity 6.3 LTS (6000.4)
> **Source review:** `docs/architecture/architecture-review-2026-06-27.md`

## Coverage Summary (domain-level)

- Systems indexed (systems-index.md): 38 (33 Approved with docs, 2 Draft primitives, 5 Not Started UI/Audio/Meta)
- ADRs on disk: 8 (8 Accepted)
- Domains with ADR coverage: 8 (networking library, navigation execution, navigation lifecycle, shop transactions, HUD UI, persistence, hosting, combat UI)
- Domains with blocking coverage gaps: **none**
- Open issue: none — all 8 ADRs are Accepted as of 2026-06-27
- Per-requirement TR-IDs minted: **0** (tr-registry.yaml intentionally empty — see Scope Note in the review report)

## Domain Coverage Matrix

| Domain | Representative Systems | ADR | Status | Notes |
|---|---|---|---|---|
| Networking transport/library | networking-core, networking-wire-protocol, networking-session, client-side-prediction, movement-system | ADR-004 | ✅ Covered (Accepted) | Library only |
| Navigation execution | navigation-pathfinding | ADR-002 | ✅ Covered (Accepted) | `link.xml` → `UnityEngine.AIModule` fix pending (carryover) |
| Navigation agent lifecycle | navigation-pathfinding, enemy-ai | ADR-003 | ✅ Covered (Accepted) | Depends on ADR-002 (satisfied) |
| Shop transaction integrity | npc-shop, currency-system, inventory-system, consumable-use-system | ADR-001 | ✅ Covered (Accepted) | Both propagations landed; Engine Compat + ADR Deps sections still missing |
| HUD UI framework | hud, combat-ui | ADR-005 | ✅ Covered (Accepted 2026-06-27) | Specialist fixes applied; stale Combat-UI ADR-number refs to correct |
| Persistence storage engine | character-persistence, authentication | ADR-006 | ✅ Covered (Accepted 2026-06-27) | PostgreSQL + Npgsql + Dapper; resolves OQ-ADR1-1, OQ-NET-5 |
| Hosting backend | zone-instancing, networking-core (infra) | ADR-007 | ✅ Covered (Accepted 2026-06-27) | Self-hosted Hetzner VPS, co-located PG; resolves OQ-ADR4-1 |
| Combat UI framework | combat-ui | ADR-008 | ✅ Covered (Accepted 2026-06-27) | Painter2D + MonoBehaviour presenter; sortingOrder = 1 reserved |
| Core gameplay/data/economy/progression | character-stats, damage-calculation, skill-system, status-effects, equipment-system, enhancement-system, loot-table-system, leveling-system, party-system, et al. | — | ❌ No per-system ADR | By design — pure design/data; no deep architectural decision required |

## Known Gaps / Open Items

All priority gaps closed as of 2026-06-27. Architecture is implementation-ready.

**Remaining open items (minor, non-blocking):**
- ADR-001 OQ-ADR1-2: `SellRequest` atomicity — `PendingSell` record pattern; deferred to follow-up ADR-001 amendment
- ADR-004 OQ-ADR4-3: `CustomMessagingManager` vs. thin UTP wrapper — resolve during Networking Core spike
- 6 MVP GDDs not yet started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding/Beginner Zone (no ADR required until design is authored)

## Superseded Requirements

None recorded this pass.

## History

| Date | Verdict | ADRs | Notes |
|------|---------|------|-------|
| 2026-06-21 | FAIL | 5 (4 Accepted, 1 Proposed) | Foundation gaps: Persistence, Hosting, Combat UI ADRs missing |
| 2026-06-27 | CONCERNS → **PASS** | 8 (8 Accepted) | 3 new ADRs promoted to Accepted; all cleanup fixes applied |
