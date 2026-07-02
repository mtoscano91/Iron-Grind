# Session State

*Updated: 2026-07-02*

## Current Status

**Task**: Story 007 complete. Story 008 is Blocked (depends on Leveling System epic). Next non-blocked story TBD.
**Stage**: Pre-Production
**GDD Count**: 38 Approved, 0 In Review = 38 of 38 MVP complete (6 Presentation GDDs deferred)
**ADR Count**: 10 written (ADR-001 through ADR-010), all Accepted
**UX Specs**: `design/ux/hud.md` (complete), `design/ux/interaction-patterns.md` (28 patterns), `design/accessibility-requirements.md` (Standard tier)

## Session Extract — /story-done 2026-06-29

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-002-modifier-stack.md` — F-1 Modifier Stack
- Tech debt logged: None (3 advisory items in Completion Notes)
- Pre-Story-003 action: fix B-01 (add IsFloatStat guard to GetEffectiveStat / GetEffectiveStatFloat)
- Next recommended: Story 003 — Modifier lifecycle (story-003-modifier-lifecycle.md)

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-004-resource-pools.md` — Resource Pools
- Files changed: `src/Foundation/CharacterStats/CharacterStats.cs` (5 edits: _currentHp/_currentMp fields, OnEntityDied stub, GetCurrentHP/MP accessors, GetEffectiveStat early-exit, RemoveEquipmentModifier MaxHP reconciliation, full method implementations)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` (12 test methods)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-004-resource-pools.md`

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-002-modifier-stack.md` — F-1 Modifier Stack
- Files changed:
  - `src/Foundation/CharacterStats/StatID.cs` — extended with float-schema stats (CritChance=12 through MovementSpeed=16)
  - `src/Foundation/CharacterStats/StatSchema.cs` — new file; schema routing, FloatStatArraySize=5, GetStatMin/GetStatMax
  - `src/Foundation/CharacterStats/CharacterStats.cs` — per-entity internal modifier dicts, FloatStatValues, GetBaseStatFloat/SetBaseStatFloat, GetEffectiveStat + GetEffectiveStatFloat with absent-stat guard
  - `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` — added SetFloatBaseStat, SetEquipmentModifiers, SetBuffModifiers, ClearModifiers
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — new file, 11 test methods covering AC-01, AC-02, AC-03, AC-04, AC-05, AC-24, AC-28a, AC-28b, AC-21, AC-30, AC-26
- Blockers: None

## Session Extract — /dev-story 2026-06-29 (Story 003)

- Story: `production/epics/character-stats/story-003-modifier-lifecycle.md` — Modifier Lifecycle
- Files changed:
  - `src/Foundation/CharacterStats/CharacterStats.cs` — storage migrated to per-entity-per-stat nested dicts; AddBuffModifier, AddEquipmentModifier, RemoveBuffModifier, RemoveEquipmentModifier implemented with write-lock guard, duplicate-ID overwrite (no double-stack), capacity overflow (log+return); GetEffectiveStat/GetEffectiveStatFloat updated to query per-stat buckets
  - `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` — SetEquipmentModifiers and SetBuffModifiers updated to require StatID parameter; ClearModifiers unchanged
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierStack_tests.cs` — all fixture calls updated to pass appropriate StatID (no AC changes, test plumbing only)
  - `tests/EditMode/CharacterStats/CharacterStats_ModifierLifecycle_tests.cs` — new file, 7 test methods covering AC-16, AC-17, AC-18, AC-19, AC-20, AC-22, capacity overflow
- Blockers: None
- Next: /code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/ then /story-done production/epics/character-stats/story-003-modifier-lifecycle.md
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-002-modifier-stack.md`

## Gate Check Session (2026-06-27)

All 4 prior blockers from the morning FAIL resolved in this session:

| Blocker | Resolution |
|---|---|
| No test framework | `/test-setup` → `tests/EditMode/`, `tests/PlayMode/`, `.github/workflows/tests.yml`, `tests/EditMode/SmokeTest.cs` |
| No architecture.md | `/create-architecture` → `docs/architecture/architecture.md` v1.0, TD sign-off |
| No accessibility requirements | `design/accessibility-requirements.md` — Standard tier committed |
| No interaction pattern library | `/ux-design patterns` → `design/ux/interaction-patterns.md`, 28 patterns |

Director panel (lean mode — all 4 run as PHASE-GATEs):
- Creative Director: CONCERNS (5 items)
- Technical Director: READY (2 tracked conditions)
- Producer: CONCERNS (4 items)
- Art Director: CONCERNS (4 items)

Gate report: `production/gate-checks/technical-setup-to-pre-production-2026-06-27.md`

## ADR Session — 2026-06-27

Both Foundation Required ADRs written (previously blocking Zone Instancing + Feature-layer stories):

| ADR | Title | Status | Domain | Engine Risk |
|-----|-------|--------|--------|-------------|
| ADR-009 | Scene/Zone-Load Management | Accepted | Core — Scene Management | HIGH |
| ADR-010 | Event/Messaging Architecture | Accepted | Core — C# Messaging | LOW |

Registry updated: 4 new forbidden patterns (`scene_handle_as_int`, `urp_setup_render_passes_loading_screen`, `central_event_bus`, `lambda_capture_persistent_subscription`) and 2 new interface contracts (`point_to_point_messaging`, `broadcast_messaging`).

## Top Priority Items for Pre-Production

1. **[IMMEDIATE]** Resolve party drop bonus contradiction (CD Concern 1 — HIGH) — amend game concept OR restore bonus
2. ~~Write Scene/Zone-Load Management ADR~~ ✓ DONE (ADR-009)
3. ~~Write Event/Messaging Architecture ADR~~ ✓ DONE (ADR-010)
4. ~~`/create-control-manifest`~~ ✓ DONE — `docs/architecture/control-manifest.md` v2026-06-28 (99 rules across 4 layers + global)
4b. ~~`/create-stories character-stats`~~ ✓ DONE — 8 stories written (001–007 Ready, 008 Blocked pending Leveling System); QA Lead gate passed with revisions; EPIC.md DoD updated to AC-01–AC-34
5. Name/license typeface (AD Concern 1 — before UI asset production)
6. Author 6 deferred MVP GDDs (Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding)

## Manual Step Pending

- Add `UNITY_LICENSE` to GitHub repository secrets (required for `.github/workflows/tests.yml` to pass)

## Revision Pass — Combat UI GDD (2026-06-20)

All 23 blocking items from the design-review specialist pass have been resolved.
Pending: systems-index update, review-log append, and Phase 5 closing widget.

| File | Change | Status |
|---|---|---|
| `design/gdd/combat-ui.md` | Status "In Design" → "In Review" (original triad) | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-9: bar state now persists server-side (1-bit barIsExpanded in SkillBarLayout) | ✓ Done |
| `design/gdd/combat-ui.md` | Bar States table: removed "or zone entry" from Expanded→Collapsed; Collapsed = "first zone entry" | ✓ Done |
| `design/gdd/combat-ui.md` | F-CUI-1: fixed uint underflow, NaN/div-zero, compile error; signed long subtraction | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-18: added Silenced (8) + CasterNotAlive (9) rejection codes | ✓ Done |
| `design/gdd/combat-ui.md` | CR-CUI-19: removed Painter2D mandate; implementation-defined renderer with perf AC | ✓ Done |
| `design/gdd/combat-ui.md` | EC-CUI-4: 5s timeout + re-request + fallback recovery path | ✓ Done |
| `design/gdd/combat-ui.md` | EC-CUI-5: zone entry now loads persisted barIsExpanded (not reset to Collapsed) | ✓ Done |
| `design/gdd/combat-ui.md` | Interactions table: removed stale OQ-CUI-3 reference from Notes | ✓ Done |
| `design/gdd/combat-ui.md` | Dependencies Pending Amendments: CR-SK-12 + hud.md marked RESOLVED | ✓ Done |
| `design/gdd/combat-ui.md` | Visual Requirements cooldown arc: removed "via Painter2D"; added countdown number row | ✓ Done |
| `design/gdd/combat-ui.md` | UI Requirements: replaced Painter2D driver line with implementation-defined perf note | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-2: 250ms → 200ms (matches CR-CUI-8) | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-8: added tick rate reference (40 ticks @ 20t/s = 2s), 60fps arc note | ✓ Done |
| `design/gdd/combat-ui.md` | AC-CUI-16: rewritten to test corridorWidth < 140dp (not safe area width) | ✓ Done |
| `design/gdd/combat-ui.md` | Added AC-CUI-18 (bar state persistence), AC-CUI-19 (expand gating), AC-CUI-20 (Silenced visual) | ✓ Done |
| `design/gdd/combat-ui.md` | OQ-CUI-3: marked RESOLVED | ✓ Done |
| `design/gdd/combat-ui.md` | OQ-CUI-6: updated to reference Silenced (8) resolution; marked RESOLVED | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-2: added V-0 (CasterNotAlive liveness check) + V-5b (Silenced check) | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-12: amended to require SkillCooldownUpdate with OnCooldown rejections (EC-CUI-4 guarantee) | ✓ Done |
| `design/gdd/skill-system.md` | CR-SK-23: added explicit SkillCooldownUpdate delivery contract | ✓ Done |
| `design/registry/entities.yaml` | SkillCastRejectionCode: added Silenced=8, CasterNotAlive=9 | ✓ Done |
| `production/session-state/active.md` | This update | ✓ Done |

## Combat UI GDD Summary

**File**: `design/gdd/combat-ui.md`
**Status**: In Review — ready for `/design-review`
**Implements Pillar**: Rhythm Mastery (primary), Earned Power (secondary)

### All 11 Sections Written and Approved

1. Overview ✓ — Zone E skill bar, UI Toolkit (ADR-005), 10-skill/8-slot bar, CUS potbar separate, SkillCastRequest + SkillCooldownUpdate flow
2. Player Fantasy ✓ — "Instrument Panel with consequence tone"; potions do NOT reset auto-attack cadence
3. Detailed Design ✓ — CR-CUI-1 through CR-CUI-22 (bar structure, layout, expand/collapse, slot binding, casting, cooldown, device adaptation)
4. Formulas ✓ — F-CUI-1 (cooldown fraction), F-CUI-2 (Zone E left boundary), F-CUI-3 (chat corridor), F-CUI-4 (implementation constants)
5. Edge Cases ✓ — EC-CUI-1 through EC-CUI-10
6. Dependencies ✓ — upstream/downstream dependencies; pending amendments table
7. Tuning Knobs ✓ — 9 knobs in CombatUIConfig.asset
8. Visual/Audio Requirements ✓ — slot states, cooldown arc, animations, toasts, audio ownership boundaries
9. UI Requirements ✓ — Unity 6.3 implementation constraints (UI Toolkit, Painter2D, safe area, PickingMode)
10. Acceptance Criteria ✓ — AC-CUI-1 through AC-CUI-17
11. Open Questions ✓ — OQ-CUI-1 through OQ-CUI-7

### Key Design Decisions Locked

- **Bar layout**: 4 primary slots (S1-S4) + [+] toggle to expand to 8 (S5-S8 expand above); Zone E footprint = 292dp
- **Consumables**: Skills-only bar; CUS potbar is a separate UI element (Option B)
- **Skill binding**: Players choose any 8 of 10 class skills via in-bar long-press context menu
- **Cooldown wire format**: `cooldownExpiryTick` (absolute server tick) — CR-SK-12 amended
- **Cooldown renderer**: Painter2D `generateVisualContent` callback; no RenderTexture, no shader dependency
- **SE3 chat corridor**: Combat-collapse when corridorWidth < 140dp (CHAT_CORRIDOR_MIN_WIDTH_DP)
- **Auto-attack toggle**: HUD owns it in Zone D — NOT Combat UI
- **Expand direction**: Expand above (primary row fixed; party frame overlap accepted MVP)

## HUD Pre-Implementation Gates

| Gate | Status | Notes |
|---|---|---|
| OQ-HUD-1: UI framework ADR | **RESOLVED** — ADR-005 Proposed | UI Toolkit chosen |
| OQ-HUD-7: design/ux/hud.md | **RESOLVED** — spec complete 2026-06-20 | XP bar color `#C4912A` decided |
| OQ-HUD-8: Status Effects event interface | Open | Blocks buff tray implementation only |

## Open Items (non-blocking to Combat UI review)

| ID | Description | Owner | Priority |
|---|---|---|---|
| OQ-CUI-1 | CUS potbar position in Zone E | HUD + CUS GDD | Pre-impl |
| OQ-CUI-2 | Cooldown arc color | Art Director | Pre-impl |
| OQ-CUI-4 | Zone D auto-attack toggle spec | HUD GDD amendment | BLOCKING for HUD impl |
| OQ-CUI-6 | Status effect skill disable visual | Advisory | Low |
| OQ-CUI-7 | Expanded bar / party frame overlap — SE3 | Lead sign-off | Pre-impl |
| OQ-HUD-8 | Status Effects event interface | Must define before buff tray impl | Advisory |
| OQ-UX-HUD-1 | Charge bar obsolescence decision | Auto-Attack Combat + playtest | Advisory |
| OQ-UX-HUD-7 | Party chat input mode spec | Party Chat GDD | Advisory |

## Architecture Review — 2026-06-21

`/architecture-review full` completed. Verdict: **FAIL (advisory)**.

**Files written:**
- `docs/architecture/architecture-review-2026-06-21.md` — full report
- `docs/architecture/traceability-index.md` — domain-level coverage matrix

**Key findings:**

| Finding | Severity |
|---|---|
| Persistence Layer ADR missing — blocks ADR-001 + ADR-004 | 🔴 Blocking |
| ADR-001: 2 propagations never applied (character-persistence.md, networking-session.md) | 🔴 Blocking |
| ADR-005 still Proposed — auto-attack-combat.md + combat-ui.md already build on it | 🔴 Blocking |
| ADR-001 missing Engine Compatibility + ADR Dependencies sections | ⚠️ Template gap |
| ADR-002 link.xml uses wrong assembly (`UnityEngine` vs `UnityEngine.AIModule`) | 🔴 Build risk |
| ADR-005 `VisualElement.transform` "removed by 6.3" contradicts pinned reference (deprecated only) | 🟠 Factual |
| ADR-005 `ApplySafeArea` code references undefined `panelWidth`/`panelHeight` | 🟠 Compile error |
| Hosting Backend ADR missing | ❌ Gap |
| ADR-006 Combat UI framework missing | ❌ Gap |

**TR-registry:** Still empty — no per-TR IDs minted this pass (domain-level only).

## Gate Check Result — 2026-06-21

`/gate-check pre-production` → **FAIL**
Report: `production/gate-checks/pre-production-to-production-2026-06-21.md`

Director panel: CD NOT READY · TD NOT READY · PR NOT READY · AD CONCERNS
Artifacts: 5/16 present · Quality checks: 0/10 · Vertical Slice: AUTO-FAIL (does not exist)

Top blockers (dependency order):
1. **Persistence Layer ADR** — write via `/architecture-decision persistence-layer`
2. **ADR-001 propagations** — apply to character-persistence.md + networking-session.md
3. **ADR-005 → Accepted** — after on-device profiling + specialist fixes
4. **Hosting Backend ADR** — `/architecture-decision hosting-backend`
5. **ADR-006 Combat UI** — `/architecture-decision combat-ui-framework`
6. No control manifest → `/create-control-manifest` after ADRs
7. No epics/stories → `/create-epics`, `/create-stories`
8. 6 MVP GDDs not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding
9. No Vertical Slice build (enhancement destruction required; blind-tested)
10. No real playtests (0 of 3 required)

## ADR-006 — Persistence Layer (2026-06-27)

Written: `docs/architecture/ADR-006-persistence-layer.md` — Status: Proposed
**Decisions**: PostgreSQL + Npgsql + Dapper; PendingPurchase → separate table, same DB; ≤50ms write budget
**Resolved**: ADR-001 OQ-ADR1-1 (PendingPurchase storage), ADR-004 OQ-NET-5 / OQ-ADR4-2 (write latency)
**Registry**: 4 new stances added (character_record ownership, pending_purchase ownership, persistence_database API, EF Core forbidden)
**Constraint added**: Hosting Backend ADR must enforce PostgreSQL co-location with game server.

## ADR-001 Propagations Applied (2026-06-27)

| File | Change | Status |
|---|---|---|
| `design/gdd/networking-session.md` | CR-NET-6.4: inserted step 2 (PendingPurchase reconciliation before handshake emission) | ✓ Done |
| `design/gdd/character-persistence.md` | CR-CP-1: added BeginPurchase/CompletePurchase/RefundPurchase/LoadOutstandingPurchases to ICharacterPersistence + PendingPurchaseResult enum + PendingPurchaseRecord struct | ✓ Done |
| `design/gdd/character-persistence.md` | CR-CP-12: new rule — PendingPurchase storage and lifecycle (separate table, same DB, ADR-006 Decision 3) | ✓ Done |
| `design/gdd/character-persistence.md` | Interactions table: NPC Shop row added as upstream caller | ✓ Done |

## ADR-005 Promoted to Accepted (2026-06-27)

| Fix | Status |
|---|---|
| `ApplySafeArea`: added `panelSize = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height))` — removes undefined `panelWidth`/`panelHeight` references | ✓ Done |
| Constraints: corrected "`VisualElement.transform` removed effectively by 6.3" → "deprecated since 6.2, still compiles with warning in 6.3" (aligns with deprecated-apis.md) | ✓ Done |
| ADR Dependencies: updated Combat UI reference from ADR-006 → ADR-007 | ✓ Done |
| Status: Proposed → Accepted (2026-06-27) | ✓ Done |

Note: device profiling (iPhone SE 3rd gen, < 0.3ms HUD update) is a pre-implementation gate captured in ADR-005 Validation Criteria — not a pre-Accepted gate.

## ADR-007 — Hosting Backend (2026-06-27)

Written: `docs/architecture/ADR-007-hosting-backend.md` — Status: Proposed
**Decision**: Self-hosted Hetzner CPX41 VPS (Ubuntu 22.04 LTS) + co-located PostgreSQL (loopback, ~0.1ms)
**Zone server**: One Unity 6.3 IL2CPP headless process per active zone instance (systemd)
**Client connectivity**: Direct NGO UDP to public IP:port (no relay)
**Resolved**: ADR-004 OQ-ADR4-1 (hosting backend deferred), OQ-NET-5 (write latency)
**Registry**: 3 new stances added (game_server_hosting, zone_server_process_model, unity_relay_for_dedicated_server forbidden)
**Engine specialist correction**: `NetworkTransform.Update()` → `NetworkTransform.OnUpdate()` in NGO 6.3 — flagged as risk in ADR

## ADR-008 — Combat UI Framework (2026-06-27)

Written: `docs/architecture/ADR-008-combat-ui-framework.md` — Status: Proposed
**Decision**: Painter2D `generateVisualContent` for cooldown arcs; `SkillBarPresenter : MonoBehaviour` with `Queue<T>` decoupling; USS `transition: height 200ms` (GDD-locked, CR-CUI-8); Unity 6.0 event API names
**Resolved**: CR-CUI-19 (cooldown renderer implementation-defined → Painter2D chosen)
**Registry**: 4 new stances (combat_ui_arc_renderer, skill_bar_presenter_pattern, deprecated_ui_event_api_names, direct_visually_element_from_network_behaviour forbidden)
**Pre-sprint gate**: `performance-analyst` must validate 8× Painter2D arcs ≤0.3ms on iPhone SE 3rd gen before combat UI implementation begins (CR-CUI-19 blocking gate)

## ADR Count as of 2026-06-27

ADR-001 through ADR-008 written. All priority ADRs from architecture-review-2026-06-21 are now addressed.

## Foundation Epics — 2026-06-27

`/create-epics layer: foundation` complete. 4 epics written, index created.

| Epic Slug | Layer | GDD(s) | Status |
|---|---|---|---|
| character-stats | Foundation | design/gdd/character-stats.md | Ready |
| item-database | Foundation | design/gdd/item-database.md | Ready |
| currency-system | Foundation | design/gdd/currency-system.md | Ready |
| networking-core | Foundation | design/gdd/networking-core.md + 9 sub-contracts | Ready |

**Files written:**
- `production/epics/character-stats/EPIC.md`
- `production/epics/item-database/EPIC.md`
- `production/epics/currency-system/EPIC.md`
- `production/epics/networking-core/EPIC.md`
- `production/epics/index.md`

**Open items from this pass:**
1. `docs/architecture/tr-registry.yaml` is empty — populate before `/story-readiness` can run
2. `docs/architecture/architecture.md` Foundation module table still shows `⚠️` for ADR-009/ADR-010 — update to remove markers (both Accepted 2026-06-27)
3. Leveling/Inventory/Loot Table in architecture.md Foundation table → actually Core layer per systems-index; will appear in `/create-epics layer: core`

## Recommended Next Steps

1. **`/create-stories character-stats`** — first implementable stories (lowest risk, no engine surface)
2. **`/create-stories item-database`** — data layer stories
3. **`/create-stories currency-system`** — economy foundation stories
4. **`/create-stories networking-core`** — largest epic; 10 sub-contracts
5. **`/create-epics layer: core`** — Core layer epics after Foundation stories are underway
6. 6 MVP GDDs still not started: Inventory UI, Enhancement UI, Map/Minimap, Audio System, VFX System, Onboarding

## Prior Approved GDDs (37 total)

Character Stats, Item Database, Currency System, Class System, Leveling System, Auto-Attack Combat, Skill System, Damage Calculation, Networking Core, networking-session, networking-wire-protocol, networking-test-harness, networking-session-token, networking-ghost-session, networking-ghost-character-state, party-system, inventory-system, loot-table-system, networking-message-criticality, networking-channel-contract, networking-owl-compensation, networking-relevance-filter, status-effects, Authentication, Equipment System, Enhancement System, Enemy AI, Movement System, Death & Respawn, Zone Instancing, NPC Shop, Consumable Use System, Mob Spawning, Navigation/Pathfinding, Client-Side Prediction, Party Chat, **HUD**

## Session Extract — /architecture-review + promotions 2026-06-27
- Verdict: **PASS** (CONCERNS → PASS in same session)
- Requirements: domain-level — all 8 Foundation/Core domains covered, 0 coverage gaps, 0 cross-ADR conflicts
- ADRs: 8 / 8 Accepted (ADR-006, 007, 008 promoted to Accepted 2026-06-27)
- Cleanup applied: ADR-002 link.xml → UnityEngine.AIModule; ADR-008 sortingOrder=1 reserved; ADR-005 Combat-UI ADR# refs corrected; ADR-001 Engine Compat + ADR Deps backfilled
- GDD revision flags: None
- Remaining open items: ADR-001 OQ-ADR1-2 (SellRequest atomicity); ADR-004 OQ-ADR4-3 (CustomMessagingManager vs UTP wrapper); 6 MVP GDDs not yet authored
- Report: docs/architecture/architecture-review-2026-06-27.md

## Session Extract — /gate-check pre-production + /create-architecture 2026-06-27
- **Gate (Technical Setup → Pre-Production): FAIL** — report at production/gate-checks/technical-setup-to-pre-production-2026-06-27.md
- Gate blockers: (1) no test framework, (2) no master architecture doc, (3) no accessibility-requirements.md, (4) no interaction-patterns.md
- **Blocker #2 RESOLVED**: master architecture doc written → docs/architecture/architecture.md (TD sign-off: APPROVED WITH CONDITIONS 2026-06-27; LP feasibility skipped — lean mode)
- Traceability index renamed traceability-index.md → architecture-traceability.md (gate-expected filename)
- Required New ADRs surfaced (Foundation-first): (1) Scene/Zone-Load Management [HIGH], (2) Event/Messaging Architecture [LOW]; then (3) URP Render/VFX [HIGH], (4) Audio [LOW] — both blocked on unauthored GDDs
- **Remaining gate blockers**: /test-setup (tests/ + CI + example test); design/accessibility-requirements.md (pick tier); /ux-design patterns (interaction-patterns.md)
- Next: clear remaining 3 gate blockers → write 2 Foundation Required ADRs → re-run /gate-check pre-production

## Session Extract — /test-setup 2026-06-27
- **Gate blocker #1 RESOLVED**: test framework scaffolded
- Files created: tests/README.md, tests/EditMode/README.md, tests/EditMode/SmokeTest.cs (example test), tests/PlayMode/README.md, tests/smoke/critical-paths.md, tests/evidence/.gitkeep, .github/workflows/tests.yml
- Framework: Unity Test Framework (NUnit, built-in) — EditMode (unit) + PlayMode (integration)
- CI: game-ci/unity-test-runner@v4, Unity 6000.4.0f1, runs on push to main + PRs
- One-time manual step required: add UNITY_LICENSE to GitHub repository secrets before first CI run
- **Remaining gate blockers**: design/accessibility-requirements.md (pick tier); /ux-design patterns (interaction-patterns.md)
- Next: accessibility doc (pick tier) → /ux-design patterns → re-run /gate-check pre-production

## Session Extract — accessibility-requirements.md 2026-06-27
- **Gate blocker #3 RESOLVED**: design/accessibility-requirements.md written, tier committed
- Tier: **Standard** (user decision)
- Rationale: visual (item rarity, HP bars) + motor (touch targets, Rhythm Mastery timing) are primary barriers; Standard covers both; Comprehensive deferred (VoiceOver, mono audio, subtitle customization)
- Key Standard commitments: 44×44pt touch targets (Apple HIG), colorblind modes (Protanopia/Deuteranopia/Tritanopia), text size adjustment (chat + menus), timing window multiplier for skill activation (1×/1.5×/2×), motion reduction toggle, safe area compliance
- Open questions: Unity 6.3 UI Toolkit accessibility node support for VoiceOver; timing window client-side feasibility; minimum iOS version
- **Remaining gate blocker**: /ux-design patterns (design/ux/interaction-patterns.md)
- Next: /ux-design patterns → re-run /gate-check pre-production

## Session Extract — /ux-design patterns 2026-06-27
- **Gate blocker #4 RESOLVED**: design/ux/interaction-patterns.md written
- 28 patterns catalogued and formalized; 8 gaps identified for future screens
- Pattern categories: Input Controls, Gesture, Combat UI, Feedback, Data Display, Layout/Navigation, Chat
- Key patterns: Resource Bar, Touch Toggle, Expand/Collapse, Skill Slot, Cooldown Arc, Long-Press Context Menu, Toast Notification, Shake Feedback, Status Effect Icon, Party Frame, Target Frame, Loot Countdown Notification, Context-Adaptive Overlay, Safe Area Container, Tabbed Panel, Scrollable Item List, Item Row, Quantity Selector, Confirm Button with Spinner, Locked Item State, Destructive Confirmation Overlay, Risk Warning Badge, Outcome Animation, Persistent Chat Panel, Compose Button, Input Field with Validation, Character Counter, Keyboard-Slide Layout Shift
- **All 4 gate blockers now resolved** — ready to re-run /gate-check pre-production
- Next: /gate-check pre-production

## Session Extract — /story-done 2026-06-29
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-003-modifier-lifecycle.md` — Modifier Lifecycle
- Tech debt logged: None (2 advisory items in Completion Notes)
- Next recommended: Story 004 — Resource Pools (`production/epics/character-stats/story-004-resource-pools.md`)

## Session Extract — /story-done 2026-06-29 (Story 004)
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-004-resource-pools.md` — CurrentHP / CurrentMP Lifecycle
- Tech debt logged: None (pre-existing TR-ID advisory)
- Next recommended: Story 005 — Events (`production/epics/character-stats/story-005-events.md`)


## Session Extract — /dev-story 2026-07-02 (Story 007)

- Story: `production/epics/character-stats/story-007-transaction-api.md` — Transaction API
- Files changed: `src/Foundation/CharacterStats/CharacterStats.cs` (transaction fields + BeginStatTransaction/EndStatTransaction/RollbackStatTransaction/AddToDeferredDedup + SetBaseStat conditional), `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (new, 7 tests)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_Transaction_tests.cs` (7 tests)
- Blockers: None
- Next: /code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/ then /story-done production/epics/character-stats/story-007-transaction-api.md

## Session Extract — /story-done 2026-07-02 (Story 006)

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-006-write-ownership.md` — Write Ownership
- Tech debt logged: None (advisory: TR-stats-006 unregistered; AC-13/AC-15 deferred OQ-1 pending)
- Next recommended: Story 007 — Transaction API (`production/epics/character-stats/story-007-transaction-api.md`)

## Session Extract — /dev-story 2026-07-02

- Story: `production/epics/character-stats/story-006-write-ownership.md` — Write Ownership
- Files changed: `src/Foundation/CharacterStats/ILevelingService.cs` (new), `src/Foundation/CharacterStats/CharacterStats.cs` (constructor + AddExperience + OQ-1 TODO), `tests/EditMode/CharacterStats/TestHelpers/CharacterStatsFixture.cs` (NullLevelingService, CreateWithLeveling), `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (new, 6 tests)
- Test written: `tests/EditMode/CharacterStats/CharacterStats_WriteOwnership_tests.cs` (6 tests — AC-14 × 3, NEW AC × 3)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-006-write-ownership.md`

## Session Extract — /story-done 2026-07-02

- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-005-events.md` — OnStatChanged / OnEntityDied Events
- Tech debt logged: None (4 advisory items in Completion Notes)
- Code review fixes applied: count snapshot in FireOnStatChanged/FireOnEntityDied; IsFiringAndAssert guards on Subscribe/Unsubscribe
- Next recommended: Story 006 — Write Ownership (`production/epics/character-stats/story-006-write-ownership.md`)

## Session Extract — /dev-story 2026-06-29

- Story: `production/epics/character-stats/story-005-events.md` — OnStatChanged / OnEntityDied Events
- Files changed:
  - `src/Foundation/CharacterStats/CharacterStats.cs` — replaced OnEntityDied stub with full event infrastructure (StatChangedHandler/EntityDiedHandler delegates, 16-slot fixed arrays, Subscribe/Unsubscribe, _isFiring guard, FireOnStatChanged/FireOnEntityDied); added IsFiringAndAssert guard + OnStatChanged firing to SetBaseStat, SetBaseStatFloat, AddBuffModifier, RemoveBuffModifier, AddEquipmentModifier, RemoveEquipmentModifier; added guard to ApplyDamage/ApplyRegen/ConsumeMana/ApplyManaRegen; replaced OnEntityDied?.Invoke with FireOnEntityDied in ApplyDamage and RemoveEquipmentModifier MaxHP path
  - `tests/EditMode/CharacterStats/CharacterStats_ResourcePool_tests.cs` — replaced 5x `OnEntityDied += ...` with `Subscribe(_ => diedCount++)` (event syntax incompatible with fixed-array pattern)
  - `tests/EditMode/CharacterStats/TestHelpers/StatEventRecorder.cs` — added Subscribe/Unsubscribe wiring methods
- Test written: `tests/EditMode/CharacterStats/CharacterStats_Events_tests.cs` (8 test methods — AC-29, AC-29 edge, AC-29b, AC-29b edge, Unsubscribe, Unsubscribe re-subscribe, OnEntityDied re-entrance, OnEntityDied read-permitted)
- Blockers: None
- Next: `/code-review src/Foundation/CharacterStats/ tests/EditMode/CharacterStats/` then `/story-done production/epics/character-stats/story-005-events.md`

## Session Extract — /story-done 2026-07-02
- Verdict: COMPLETE WITH NOTES
- Story: `production/epics/character-stats/story-007-transaction-api.md` — Transaction API
- Tech debt logged: None (3 advisory items in Completion Notes)
- Next recommended: Story 008 is Blocked (depends on Leveling System epic)
