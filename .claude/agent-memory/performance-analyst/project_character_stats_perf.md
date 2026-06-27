---
name: Character Stats performance review findings
description: Key performance risks identified in Character Stats GDD adversarial review for Project Iron Grind, including no-cache policy impact and query scaling math
type: project
---

Character Stats GDD was adversarially reviewed on 2026-04-22 and flagged three CRITICAL performance risks before implementation.

**Why:** The GDD's explicit prohibition on stat value caching ("no caller may cache a stat value across game ticks") combined with a HUD polling requirement ("display must reflect change on the next frame") creates a query explosion at 50-player zone scale. The math: 5 HUD stats x 50 entities x 60fps = 15,000 GetEffectiveStat calls/second, each iterating the full modifier stack.

**How to apply:** When reviewing any system that queries Character Stats (HUD, Damage Calculation, Auto-Attack Combat), apply the query-count math before approving the design. The three standing recommendations are:
1. A dirty-flag or OnStatChanged event for the HUD read path (OQ-5 must be resolved before HUD GDD is authored)
2. A batch query API GetEffectiveStatBatch() for Damage Calculation (5 separate F-1 traversals per hit today)
3. An ADR on CharacterStats architecture (MonoBehaviour vs. data-oriented layout) before implementation

**Unresolved cross-GDD gap:** Whether clients recompute F-1 locally (prediction) or receive pre-computed stat floats over the wire is undecided. This doubles all query counts above if local recomputation is chosen. Networking Core GDD must resolve this before any of the three systems are implemented.
