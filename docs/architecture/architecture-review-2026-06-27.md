# Architecture Review Report

> **Date:** 2026-06-27
> **Engine:** Unity 6.3 LTS (6000.4)
> **GDDs Reviewed:** 38 Approved (+2 Draft primitives)
> **ADRs Reviewed:** 8 (5 Accepted, 3 Proposed)
> **Mode:** `/architecture-review` (full)
> **Prior review:** `architecture-review-2026-06-21.md` (FAIL)
> **Verdict:** **PASS** — coverage complete, no cross-ADR conflicts, all 8 ADRs Accepted as of 2026-06-27. Architecture is implementation-ready.

## Scope Note

Consistent with the 2026-06-21 pass, coverage is assessed at **domain granularity**
rather than minting 100+ per-requirement TR-IDs. `tr-registry.yaml` remains empty;
per-TR extraction is deferred and done incrementally as stories are written. All 8
ADRs and all engine-reference docs were read in full; every ADR→GDD propagation
claim was grep-validated.

---

## Progress Since 2026-06-21 — every prior blocker addressed

| Prior FAIL blocker | Status now | Evidence |
|---|---|---|
| Persistence Layer ADR missing | ✅ Written | ADR-006 (PostgreSQL + Npgsql + Dapper); resolves OQ-ADR1-1 & OQ-NET-5 |
| Hosting Backend ADR missing | ✅ Written | ADR-007 (self-hosted Hetzner VPS, co-located PG); resolves OQ-ADR4-1 |
| Combat UI ADR missing | ✅ Written | ADR-008 (Painter2D arc + MonoBehaviour presenter); resolves CR-CUI-19 |
| ADR-005 still Proposed | ✅ Accepted (2026-06-27) | Specialist fixes applied (transform wording, ApplySafeArea panel dims, sortingOrder note) |
| ADR-001 propagation incomplete | ✅ Both landed | `character-persistence.md` CR-CP-12 (PendingPurchase storage); `networking-session.md` step 2 (reconnect reconciliation) |
| Charge-bar amendment pending | ✅ Applied | `auto-attack-combat.md` — `style.scale` supersedes `Image.fillAmount` |

---

## Traceability Summary (domain-level)

| Domain | Representative Systems | ADR | Status |
|---|---|---|---|
| Networking transport/library | networking-core, wire-protocol, session, CSP, movement | ADR-004 | ✅ Covered |
| Navigation execution | navigation-pathfinding | ADR-002 | ✅ Covered |
| Navigation agent lifecycle | navigation-pathfinding, enemy-ai | ADR-003 | ✅ Covered |
| Shop transaction integrity | npc-shop, currency, inventory, consumable-use | ADR-001 | ✅ Covered (propagations complete) |
| HUD UI framework | hud, combat-ui | ADR-005 | ✅ Covered (Accepted) |
| Persistence storage engine | character-persistence, authentication | ADR-006 | ⚠️ Covered but **Proposed** |
| Hosting backend | zone-instancing, networking-core (infra) | ADR-007 | ⚠️ Covered but **Proposed** |
| Combat UI framework | combat-ui | ADR-008 | ⚠️ Covered but **Proposed** |
| Core gameplay/data/economy/progression | stats, damage, skill, status, equipment, enhancement, loot, leveling, party, et al. | — | ❌ No per-system ADR (by design — pure design/data, no deep architectural decision required) |

No remaining **coverage** gaps in the Foundation/Core layers. The only gap vs. the
literal coding-standard ("every system must have an ADR") is the pure design/data
system set, which does not warrant deep ADRs.

---

## Cross-ADR Conflicts

**None.** The 8 ADRs are mutually reinforcing rather than contradictory:

- **ADR-006 ↔ ADR-001**: ADR-006 Decision 3 implements ADR-001's `PendingPurchase`
  in a dedicated `pending_purchases` table; the INSERT and the `TrySpendGold` UPDATE
  share one `NpgsqlTransaction` (atomicity). Resolves OQ-ADR1-1.
- **ADR-006 ↔ ADR-004**: ADR-006 Decision 4 sets the ≤50ms P95 write budget,
  resolving OQ-NET-5 within `ENHANCEMENT_PROCESS_LATENCY_MAX_MS` = 200ms.
- **ADR-007 ↔ ADR-006**: ADR-007 fulfills ADR-006's co-location constraint via
  loopback PostgreSQL (~0.1ms — 500× headroom).
- **ADR-007 ↔ ADR-002/003**: ADR-007 "one process per zone instance" matches
  ADR-002 Decision 4's "one zone per server process" binding constraint.
- **ADR-008 ↔ ADR-005**: ADR-008 inherits all ADR-005 forbidden patterns
  (`VisualElement.transform` setter banned, explicit `PickingMode.Ignore`).

No data-ownership, integration-contract, performance-budget, dependency, pattern,
or state-authority contradictions detected.

---

## ADR Dependency Order

```
Foundation (no deps, Accepted):
  ADR-001  Purchase Transaction Integrity   [Accepted + Amendment A1]
  ADR-002  NavMesh Execution Contract        [Accepted]
  ADR-004  Networking Library — NGO          [Accepted]
Depends on Foundation (Accepted):
  ADR-003  Nav Agent Lifecycle → ADR-002     [Accepted ✓]
  ADR-005  HUD UI Framework (None)           [Accepted ✓]
Proposed, gated:
  ADR-006  Persistence Layer → ADR-004 ✓                      [PROPOSED]
  ADR-007  Hosting Backend → ADR-004 ✓ + ADR-006 (PROPOSED)   [PROPOSED]
  ADR-008  Combat UI → ADR-005 ✓ + ADR-004 ✓                  [PROPOSED]
```

⚠️ **Unresolved dependency:** ADR-007 depends on ADR-006, which is still `Proposed`.
ADR-007 itself states ADR-006 "should be promoted to Accepted before or alongside
this ADR." **Promote ADR-006 first.**

🔴 **No dependency cycles.**

### Recommended Promotion / Implementation Order

1. **ADR-006 → Accepted** — unblocks Character Persistence, NPC Shop `PendingPurchase`,
   Enhancement, Leveling, Consumable Use implementation chains.
2. **ADR-007 → Accepted** — after/with ADR-006; unblocks server infra & Networking
   Core implementation.
3. **ADR-008 → Accepted** — dependencies already Accepted; unblocks Combat UI sprint.

---

## The Verdict Driver — Acceptance Status, Not Coverage

The architecture is **complete and coherent in content**. The blocker is that
**ADR-006, ADR-007, and ADR-008 are all `Proposed`.** Per `docs/CLAUDE.md`
("stories referencing a `Proposed` ADR are auto-blocked"), this blocks the entire
persistence / economy / hosting / combat-UI implementation chain. This is an
acceptance-status issue, materially less severe than the prior coverage FAIL.

---

## Engine Compatibility (audited inline against pinned reference)

Engine specialist consultation was skipped this pass (user decision) — the
in-reference claims validate cleanly, and genuinely post-cutoff items are
device-verification-gated regardless of reviewer.

**Validated correct against `breaking-changes.md` / `deprecated-apis.md`:**
- ✅ **ADR-007** — `NetworkTransform.Update` override → `OnUpdate` (6.3 REMOVED) correctly identified and mitigated.
- ✅ **ADR-008** — Decision 3 uses correct Unity 6.0 event names (`HandleEventTrickleDown`, `HandleEventBubbleUp`, `StopPropagation()`) and bans the deprecated trio; `[SerializeField] private UIDocument` is field-only (6.3 rule); Painter2D correctly flagged post-cutoff with device verification required.
- ✅ **ADR-005** — `VisualElement.transform` wording now correct ("deprecated 6.2, not removed").
- ✅ **ADR-006** — server-side .NET; IL2CPP `link.xml` for Npgsql/Dapper correctly flagged.

**Findings (all minor / carryover — none block the verdict on their own):**

1. 🟠 **ADR-002 `link.xml` assembly name (carryover, unaddressed).** Still uses the
   umbrella `<assembly fullname="UnityEngine">` with nested `UnityEngine.AI`. The
   2026-06-21 specialist pass flagged that in Unity 6's modular assembly layout
   `NavMeshAgent` lives in `UnityEngine.AIModule.dll`; the umbrella name may silently
   fail stripping → runtime `MissingMethodException`. Correct to:
   ```xml
   <assembly fullname="UnityEngine.AIModule">
       <namespace fullname="UnityEngine.AI" preserve="all"/>
   </assembly>
   ```
   Verify empirically in a `UNITY_SERVER` build before the navigation sprint.
2. 🟡 **ADR-005 mis-references the Combat UI ADR number.** Calls it "ADR-007" (line 25)
   and "ADR-006" (line 292); the Combat UI ADR is actually **ADR-008** (ADR-006 is
   Persistence, ADR-007 is Hosting). Documentation inconsistency only — correct the refs.
3. 🟡 **ADR-008 specifies no distinct `UIDocument` sortingOrder.** ADR-005 reserves
   `sortingOrder = 0` for the HUD; the prior specialist asked Combat UI to take a
   non-zero order. HUD↔Combat-UI panel draw order is currently undefined — add an
   explicit reservation to ADR-008.
4. 🟡 **ADR-001 still lacks dedicated `Engine Compatibility` + `ADR Dependencies`
   sections** (template-compliance carryover). Mostly server-logic; low risk.

---

## GDD Revision Flags (Architecture → Design)

**None.** The charge-bar amendment aligned `auto-attack-combat.md` with ADR-005; no
GDD assumption contradicts verified engine behavior.

Note: `combat-ui.md` now builds on **ADR-008 (Proposed)** — its stories auto-block
until ADR-008 is Accepted. (The prior auto-block on ADR-005 cleared when ADR-005
was Accepted; the dependency simply moved to ADR-008.)

---

## Architecture Document Coverage

No `docs/architecture/architecture.md` exists. Phase 6 is N/A — the project has ADRs
but no consolidated architecture document. Consider authoring one once the three
Proposed ADRs are promoted to Accepted.

---

## Verdict: PASS

Coverage complete, no cross-ADR conflicts, engine-consistent, all 8 ADRs Accepted.

All blockers-for-PASS resolved in the same session (2026-06-27):

| Action | Result |
|---|---|
| ADR-006 → Accepted | Persistence/economy implementation chain unblocked |
| ADR-007 → Accepted | Server infra / Networking Core implementation unblocked |
| ADR-008 → Accepted | Combat UI implementation sprint unblocked |
| ADR-002 `link.xml` → `UnityEngine.AIModule` | Stripping correctness fix applied |
| ADR-005 ADR-number refs corrected | Now correctly reference ADR-008 |
| ADR-008 `sortingOrder = 1` reserved | HUD/Combat-UI panel draw order defined |
| ADR-001 Engine Compatibility + ADR Dependencies backfilled | Template compliance restored |

### Open Items (non-blocking)

- ADR-001 OQ-ADR1-2: `SellRequest` atomicity (`PendingSell` pattern) — follow-up amendment
- ADR-004 OQ-ADR4-3: `CustomMessagingManager` vs. UTP wrapper — resolve during Networking Core spike
- 6 MVP GDDs not yet authored: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding

---

## Handoff

- **Gate guidance:** `/gate-check pre-production` — architecture is now PASS; run the
  gate check to confirm readiness to advance to Production.
- **Next production step:** `/create-control-manifest` → `/create-epics` →
  `/create-stories` — the control manifest locks implementation rules from the ADRs;
  epics and stories follow.
- **Re-run trigger:** re-run `/architecture-review` only if a new ADR is written or
  an existing ADR is superseded.
