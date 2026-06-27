# Architecture Review Report

> **Date:** 2026-06-21
> **Engine:** Unity 6.3 LTS (6000.4)
> **GDDs Reviewed:** 38 Approved (+2 Draft primitives)
> **ADRs Reviewed:** 5
> **Mode:** `/architecture-review` (full)
> **Verdict:** **FAIL** (advisory) — foundation-layer coverage incomplete; two Accepted ADRs depend on unwritten/unpropagated architecture.

## Scope Note

With 38 GDDs and an empty `tr-registry.yaml`, coverage was assessed at **domain
granularity** rather than minting 100+ per-requirement TR-IDs from full reads of
every GDD. All 5 ADRs and all engine-reference docs were read in full; every
ADR→GDD propagation claim was grep-validated. Per-TR extraction should be done
incrementally as the missing ADRs are authored.

---

## Traceability Summary (domain-level)

| Domain | Systems | ADR | Status |
|---|---|---|---|
| Networking transport/library | Networking Core, Wire Protocol, Session, CSP, Movement (net) | ADR-004 (NGO) | ✅ Covered (library only) |
| Navigation execution | Navigation/Pathfinding | ADR-002 | ✅ Covered |
| Navigation agent lifecycle | Navigation, Enemy AI | ADR-003 | ✅ Covered |
| Shop transaction integrity | NPC Shop, Currency, Inventory, Consumable Use | ADR-001 | ⚠️ Partial — propagation incomplete |
| HUD UI framework | HUD, (Combat UI) | ADR-005 | ⚠️ Partial — ADR still **Proposed** |
| **Persistence storage engine** | Character Persistence, ADR-001 PendingPurchase, Auth | **none** | ❌ **GAP (blocking)** |
| **Hosting backend** | Zone Instancing, Networking Core infra | **none** | ❌ GAP (deferred, blocks infra) |
| Combat UI framework | Combat UI | none (ADR-006 anticipated) | ❌ GAP |
| Core gameplay/data/economy/progression | ~25 systems (Stats, Damage, Skill, Status, Equipment, Enhancement, Loot, Leveling, Zone Instancing logic, Party, etc.) | none | ❌ GAP vs. coding standard |

The coding standard ("every system must have a corresponding ADR") implies ~38
ADRs; **5 exist.** Many pure design/data systems arguably do not require a deep
ADR, but the Foundation/Core gaps in **Blocking Issues** are genuinely blocking.

---

## Cross-ADR Conflicts

**None.** The five ADRs occupy disjoint domains (transactions, navigation ×2,
networking library, UI). No data-ownership, integration-contract,
performance-budget, dependency, pattern, or state-authority conflicts detected.
The only inter-ADR tension is an **unresolved dependency** (see below), not a
contradiction.

---

## ADR Dependency Order

```
Foundation (no deps):
  ADR-002  NavMesh Execution Contract        [Accepted]
  ADR-004  Networking Library — NGO          [Accepted]
Depends on Foundation:
  ADR-003  Nav Agent Lifecycle  → ADR-002    [Accepted ✓ ordering satisfied]
Unaccepted / gated:
  ADR-005  HUD UI Framework                   [⚠️ PROPOSED — not Accepted]
Required but unwritten (flagged by accepted ADRs):
  Persistence Layer ADR  ← depended on by ADR-001 (OQ-ADR1-1) AND ADR-004 (OQ-NET-5)
  Hosting Backend ADR    ← enabled-by ADR-004 (OQ-ADR4-1)
  ADR-006 Combat UI      ← anticipated by ADR-005 (combat-ui.md already cites it)
```

⚠️ **Unresolved dependency:** ADR-001 (Accepted) persists `PendingPurchase` "via
Character Persistence," but no Persistence ADR exists and `character-persistence.md`
never defines the record. An Accepted ADR rests on an undefined storage primitive.

🔴 **No dependency cycles.**

---

## ADR-001 Propagation Audit (grep-validated)

ADR-001 mandates 5 downstream updates. Actual state on disk:

| Target | Required change | Landed? |
|---|---|---|
| networking-wire-protocol.md | `requestId` on Buy/Sell | ✅ yes (26 refs) |
| networking-channel-contract.md | register 17 shop msgs | ✅ referenced |
| networking-message-criticality.md | classify shop msgs | ✅ referenced |
| **character-persistence.md** | define `PendingPurchase` storage/lifecycle | ❌ **absent** |
| **networking-session.md** | reconciliation step in SessionHandshake | ❌ **absent** |

Two of five propagations from an **Accepted** ADR were never applied. The
transaction-integrity guarantee is not implementable as written until both land.

---

## GDD Revision Flags (Architecture → Design)

- ✅ **auto-attack-combat.md** — ADR-005 charge-bar amendment **already applied**
  (`style.scale`, "superseded by ADR-005"). *Caveat:* this GDD and `combat-ui.md`
  commit to ADR-005 while it is still **Proposed** — per `docs/CLAUDE.md`, stories
  referencing a Proposed ADR auto-block.
- ✅ **navigation-pathfinding.md / enemy-ai.md** — ADR-002/003 propagations landed
  (`Resume`, `RETURN_SPEED_CAP`, `TryGetValue` guard all present).
- No GDD assumption contradicts verified engine behavior.

---

## Engine Compatibility Issues

- All 5 ADRs target Unity 6.3 (6000.4) consistently. No version drift, no
  deprecated-API references in decisions, no post-cutoff API conflicts between ADRs.
- ADR-002/003/004/005 carry full Engine Compatibility sections with empirical
  verification items.
- ❌ **ADR-001 is missing both `Engine Compatibility` and `ADR Dependencies`
  sections** (template-required per `docs/CLAUDE.md`). Mostly server-logic, but the
  absence is an unverified blind spot and a template-compliance break.

### Engine Specialist Findings (unity-specialist consultation)

These carry the same weight as audit findings.

- 🔴 **ADR-002 — `link.xml` assembly name likely wrong.** The ADR preserves
  `<assembly fullname="UnityEngine">` with nested `UnityEngine.AI`. In Unity 6's
  modular assembly layout, `NavMeshAgent` lives in **`UnityEngine.AIModule.dll`**.
  The umbrella name may silently fail stripping — build links clean, then
  `MissingMethodException` at runtime (exactly the failure the ADR tries to
  prevent). Correct entry:
  ```xml
  <assembly fullname="UnityEngine.AIModule">
      <namespace fullname="UnityEngine.AI" preserve="all"/>
  </assembly>
  ```
  Verify empirically in a `UNITY_SERVER` build before the navigation sprint.
- ⚠️ **ADR-002 — `Application.targetFrameRate` lever nuance.** NavMeshAgent steering
  runs in the player loop on `Time.deltaTime`; if NGO's loop or another system
  overrides player-loop rate, `targetFrameRate` may be ignored. The ADR's
  `agent.enabled`-toggle fallback is the most reliable mitigation. Profiling gate
  covers it.
- ⚠️ **ADR-003 — `isStopped` persistence across `enabled` toggle** remains an
  empirical unknown (not in reference docs). The ADR's defensive "set `isStopped`
  explicitly on every reclaim" is the correct pattern regardless — keep it.
- ⚠️ **ADR-004 — `CustomMessagingManager` send API** should be the **first spike**
  before Networking Core implementation, not concurrent. The risk matrix rates this
  LOW (identifier-only); if the API shape changed in 6.3 NGO, hot-path impact may be
  understated.
- 🟠 **ADR-005 — factual mismatch with pinned reference.** ADR states
  `VisualElement.transform` setter is "removed effectively by 6.3." The reference
  (`deprecated-apis.md`) marks it **Deprecated** (6.2), not Removed; 6.3
  breaking-changes does not list it. Correct to "deprecated 6.2; compiler warning in
  6.3." Guidance (use `style.translate`) stays correct.
- 🟠 **ADR-005 — `ApplySafeArea` code references undefined `panelWidth`/`panelHeight`.**
  Verbatim use compile-errors. Source the values from
  `hudRoot.resolvedStyle.width/.height` (post-layout) or
  `panel.visualTree.layout`. Safe-area is a CI-blocking correctness requirement —
  fix the snippet.
- 🟡 **ADR-005 — `sortingOrder = 0` not reserved.** ADR-006 (Combat UI) will need a
  distinct UIDocument sorting order; if it also defaults to 0, draw order is
  undefined. Reserve 0 for HUD.

---

## Architecture Document Coverage

No `docs/architecture/architecture.md` exists. Phase 6 (architecture-doc vs.
systems-index validation) is N/A — the system has ADRs but no consolidated
architecture document. Consider authoring one once the foundation ADRs land.

---

## Verdict: FAIL (advisory) — gate: Technical Setup → Pre-Production

The existing ADRs are unusually thorough. The FAIL is driven by **incomplete
foundation-layer coverage that two Accepted ADRs already depend on**, not by
weakness in what exists.

### Blocking Issues

1. **Persistence Layer ADR does not exist** — explicitly BLOCKING per ADR-004
   (OQ-NET-5) and structurally required by ADR-001 (PendingPurchase storage,
   OQ-ADR1-1). Must define the storage engine, write-confirmation latency budget
   (inside `ENHANCEMENT_PROCESS_LATENCY_MAX_MS`), and unblocks the entire
   persistence/economy chain.
2. **ADR-005 is `Proposed`, not `Accepted`** — yet `auto-attack-combat.md` and
   `combat-ui.md` already build on it. Either accept it (after the iPhone SE
   profiling gate it names, plus the two specialist corrections above) or those GDDs
   reference a non-Accepted ADR and their stories auto-block.
3. **ADR-001 propagation incomplete** — `character-persistence.md` (PendingPurchase
   storage) and `networking-session.md` (reconnect reconciliation) were never
   updated. The integrity guarantee isn't realizable as written.

### Required ADRs (most foundational first)

1. **Persistence Layer ADR** — storage engine + write-latency budget; unblocks
   ADR-001 fully and resolves OQ-NET-5.
2. **Hosting Backend ADR** — self-hosted vs. relay vs. managed (Multiplay is dead);
   needed before Zone Instancing / Networking Core infra.
3. **Promote ADR-005 → Accepted** — after on-device profiling + specialist fixes
   (transform-deprecation wording, `ApplySafeArea` panel dims, sortingOrder
   reservation).
4. **ADR-006 Combat UI framework** — `combat-ui.md` already cites it.
5. **Backfill ADR-001** — add `Engine Compatibility` + `ADR Dependencies` sections;
   apply the 2 missing propagations.

### Immediate Engine-Verification Order (per specialist)

Run as the first tests once a `UNITY_SERVER` headless build exists — all are binary
sprint blockers:
1. `link.xml` with `UnityEngine.AIModule` allows `NavMeshAgent` construction (ADR-002).
2. `Application.targetFrameRate` controls NavMeshAgent sim cadence in headless build
   (ADR-002); else use `agent.enabled` toggle.
3. `isStopped` persistence across `enabled` toggle (ADR-003).
4. `CustomMessagingManager` send-API shape in 6.3 NGO (ADR-004) — spike before
   Networking Core sprint.

---

## Handoff

- **Top 3 ADRs to write:** (1) Persistence Layer, (2) Hosting Backend, (3) ADR-006
  Combat UI — open a fresh session per ADR via `/architecture-decision [system]`.
- **Gate guidance:** `/gate-check pre-production` should not pass until at least the
  Persistence Layer ADR exists and ADR-001's two propagations land.
- **Rerun trigger:** re-run `/architecture-review` after each new ADR to confirm
  coverage improves.
