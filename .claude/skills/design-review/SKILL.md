---
name: design-review
description: "Reviews a game design document for completeness, internal consistency, implementability, and adherence to project design standards. Run this before handing a design document to programmers."
argument-hint: "[path-to-design-doc] [--depth full|lean|solo]"
user-invocable: true
allowed-tools: Read, Glob, Grep, Write, Edit, Task, AskUserQuestion
---

## Phase 0: Parse Arguments

Extract `--depth [full|lean|solo]` if present.

**Note**: `--depth` controls the *analysis depth* of this skill (how many specialist agents are spawned). It is independent of the global review mode in `production/review-mode.txt`, which controls director gate spawning.

- **`full`**: Complete review — all phases + specialist agent delegation (Phase 3b)
- **`lean`**: All phases, no specialist agents — faster, single-session analysis
- **`solo`**: Phases 1-4 only, no delegation, no Phase 5 next-step prompt — use when called from within another skill

**If `--depth` is not explicitly specified**, read `design/gdd/reviews/[doc-name]-review-log.md` (if it exists) and apply this decision table before proceeding:

| Situation | Default depth | Reason |
|-----------|--------------|--------|
| No prior review log (first pass) | `full` | Discovery — all domains need fresh eyes |
| Prior verdict APPROVED | `full` | New content added; treat as fresh discovery |
| Prior verdict NEEDS REVISION, targeted fixes applied | `lean` | Verify specific gaps closed; escalate to full only if lean finds new structural issues |
| Prior verdict MAJOR REVISION NEEDED, structural rewrite (new sections, split, major rearrangement) | `full` | New structure needs full coverage |
| Prior verdict MAJOR REVISION NEEDED, targeted fix sprint (same structure, filling specific gaps) | `lean` | Specialist agents will re-find same structural gaps unless upstream contracts were written |
| 3+ consecutive MAJOR REVISION NEEDED entries in log | **STOP — see Convergence Check in Phase 1** | Reviewing again without extracting undefined primitives will produce another MAJOR REVISION NEEDED |

Tell the user which depth was selected and why, in one sentence, before proceeding.

---

## Phase 1: Load Documents

Read the target design document in full. Read CLAUDE.md to understand project context and standards. Read related design documents referenced or implied by the target doc (check `design/gdd/` for related systems).

**Dependency graph validation:** For every system listed in the Dependencies section, use Glob to check whether its GDD file exists in `design/gdd/`. Flag any that don't exist yet — these are broken references that downstream authors will hit.

**Lore/narrative alignment:** If `design/gdd/game-concept.md` or any file in `design/narrative/` exists, read it. Note any mechanical choices in this GDD that contradict established world rules, tone, or design pillars. Pass this context to `game-designer` in Phase 3b.

**Prior review check:** Check whether `design/gdd/reviews/[doc-name]-review-log.md` exists. If it does, read the most recent entry — note what verdict was given and what blocking items were listed. This session is a re-review; track whether prior items were addressed.

**Convergence Check (run after reading the review log — do this before Phase 2):**

Count consecutive MAJOR REVISION NEEDED verdicts in the log. If the count is ≥ 3:

1. List the blocker clusters that appear in two or more of those passes by name.
2. Check whether each of those clusters references an undefined primitive (e.g., "session token is referenced but never defined", "state machine transition missing from table"). Do this by grepping `design/gdd/` for the primitive name.
3. If the primitive still does not exist on disk: **surface a Convergence Warning** before proceeding with Phase 2:

> ⚠️ **Convergence Warning**: This document has had [N] consecutive MAJOR REVISION NEEDED verdicts. The following clusters have recurred without resolution: [list]. The root cause appears to be [primitive X] which is referenced in this document but has no spec file. Running another full review will reproduce these findings. **Recommended action: write [primitive X] as a standalone spec first, then re-review.** Continuing with `--depth lean` to document current state without burning full specialist budget.

Then automatically switch to `lean` depth and proceed. Do not run a full specialist review on a document in convergence stall.

**Primitive Readiness Check (run immediately after the Convergence Check):**

Scan the GDD for terms that pattern-match as "referenced but not defined here": phrases like "defined in [other doc]", "see [ADR]", "pending [other GDD]", "type unspecified", "schema TBD". For each:
- Check whether the referenced document or definition actually exists on disk.
- If it does not: flag it as a **primitive gap** in the Phase 4 output (not a blocker in the traditional sense, but a readiness concern that will generate downstream blockers).

This check runs in the main session context — no agents needed. It prevents specialists from independently re-discovering the same "X is referenced but not defined" finding six times.

---

## Phase 2: Completeness Check

Evaluate against the Design Document Standard checklist:

- [ ] Has Overview section (one-paragraph summary)
- [ ] Has Player Fantasy section (intended feeling)
- [ ] Has Detailed Rules section (unambiguous mechanics)
- [ ] Has Formulas section (all math defined with variables)
- [ ] Has Edge Cases section (unusual situations handled)
- [ ] Has Dependencies section (other systems listed)
- [ ] Has Tuning Knobs section (configurable values identified)
- [ ] Has Acceptance Criteria section (testable success conditions)

---

## Phase 3: Consistency and Implementability

**Internal consistency:**
- Do the formulas produce values that match the described behavior?
- Do edge cases contradict the main rules?
- Are dependencies bidirectional (does the other system know about this one)?

**Implementability:**
- Are the rules precise enough for a programmer to implement without guessing?
- Are there any "hand-wave" sections where details are missing?
- Are performance implications considered?

**Cross-system consistency:**
- Does this conflict with any existing mechanic?
- Does this create unintended interactions with other systems?
- Is this consistent with the game's established tone and pillars?

---

## Phase 3b: Adversarial Specialist Review (full mode only)

**Skip this phase in `lean` or `solo` mode.**

**This phase is MANDATORY in full mode.** Do not skip it.

**Before spawning any agents**, print this notice:
> "Full review: spawning specialist agents in parallel. This typically takes 8–15 minutes. Use `--review lean` for faster single-session analysis."

### Step 1 — Identify all domains the GDD touches

Read the GDD and identify every domain present. A GDD can touch multiple domains simultaneously — be thorough. Common signals:

| If the GDD contains... | Spawn these agents |
|------------------------|-------------------|
| Costs, prices, drops, rewards, economy | `economy-designer` |
| Combat stats, damage, health, DPS | `game-designer`, `systems-designer` |
| AI behaviour, pathfinding, targeting | `ai-programmer` |
| Level layout, spawning, wave structure | `level-designer` |
| Player progression, XP, unlocks | `economy-designer`, `game-designer` |
| UI, HUD, menus, player-facing displays | `ux-designer`, `ui-programmer` |
| Dialogue, quests, story, lore | `narrative-director` |
| Animation, feel, timing, juice | `gameplay-programmer` |
| Multiplayer, sync, replication | `network-programmer` |
| Audio cues, music triggers | `audio-director` |
| Performance, draw calls, memory | `performance-analyst` |
| Engine-specific patterns or APIs | Primary engine specialist (from `.claude/docs/technical-preferences.md`) |
| Acceptance criteria, test coverage | `qa-lead` |
| Data schema, resource structure | `systems-designer` |
| Any gameplay system | `game-designer` (always) |

**Always spawn `game-designer` and `systems-designer` as a baseline minimum.** Every GDD touches their domain.

### Step 2 — Spawn all relevant specialists in parallel

**CRITICAL: Task in this skill spawns a SUBAGENT — a separate independent Claude session
with its own context window. It is NOT task tracking. Do NOT simulate specialist
perspectives internally. Do NOT reason through domain views yourself. You MUST issue
actual Task calls. A simulated review is not a specialist review.**

Issue all Task calls simultaneously. Do NOT spawn one at a time.

**Specialist prompt segmentation — REQUIRED:**

Do NOT paste the full GDD text into every specialist prompt. Each specialist receives only the sections relevant to their domain, plus a brief summary of other sections. This reduces per-agent prompt cost by 30–50% on large documents without reducing review quality.

Use this section mapping when constructing each prompt:

| Specialist | Include in full | Summarise (key rules only, no prose) | Omit |
|-----------|----------------|---------------------------------------|------|
| `network-programmer` | Detailed Rules, Edge Cases, Dependencies, Tuning Knobs | Formulas (values only, no derivation), Overview | Player Fantasy, ACs |
| `systems-designer` | Formulas, Tuning Knobs | Detailed Rules (data structures and state machines only), Overview | Player Fantasy, ACs |
| `qa-lead` | Acceptance Criteria | Detailed Rules (one sentence per rule, enough to judge whether the AC matches the rule) | Formulas, Player Fantasy, Tuning Knobs |
| `game-designer` | Overview, Player Fantasy, Detailed Rules | Edge Cases (summary only), Formulas (results only, no derivation) | ACs, Dependencies |
| `performance-analyst` | Formulas, Tuning Knobs, any section containing tick rates / batch sizes / memory estimates | Overview (1 sentence) | Player Fantasy, ACs, Edge Cases |
| `economy-designer` | Formulas, Tuning Knobs, any resource/cost/reward rules | Overview | Player Fantasy, ACs |
| `network-programmer` + `qa-lead` together | As above for each | — | — |

When a section is "summarised", extract only the rule names and their one-sentence definitions — not the full prose. Example: "CR-NET-6.1: Server preserves session state for 5 minutes on disconnect." not the full multi-paragraph rule text.

**Prompt each specialist adversarially:**
> "Here is the GDD for [system] and the main review's structural findings so far.
> Your job is NOT to validate this design — your job is to find problems.
> Challenge the design choices from your domain expertise. What is wrong,
> underspecified, likely to cause problems, or missing entirely?
> Be specific and critical. Disagreement with the main review is welcome."

**Additional instructions per agent type:**

- **`game-designer`**: Anchor your review to the Player Fantasy stated in Section B of this GDD. Does this design actually deliver that fantasy? Would a player feel the intended experience? Flag any rules that serve implementability but undermine the stated feeling.

- **`systems-designer`**: For every formula in the GDD, plug in boundary values (minimum and maximum plausible inputs). Report whether any outputs go degenerate — negative values, division by zero, infinity, or nonsensical results at the extremes.

- **`qa-lead`**: Review every acceptance criterion. Flag any that are not independently testable — phrases like "feels balanced", "works correctly", "performs well" are not ACs. Also list every rule in the Detailed Rules section that has no corresponding AC. Suggest concrete rewrites for untestable ACs.

### Step 3 — Senior lead review

After all specialists respond, spawn `creative-director` as the **senior reviewer**:
- Provide: the GDD, all specialist findings, any disagreements between them
- Ask: "Synthesise these findings. What are the most important issues? Do you agree with the specialists? What is your overall verdict on this design?"
- The creative-director's synthesis becomes the **final verdict** in Phase 4.

### Step 4 — Surface disagreements

If specialists disagree with each other or with the creative-director, do NOT silently pick one view. Present the disagreement explicitly in Phase 4 so the user can adjudicate.

Mark every finding with its source: `[game-designer]`, `[economy-designer]`, `[creative-director]` etc.

---

## Phase 4: Output Review

```
## Design Review: [Document Title]
Specialists consulted: [list agents spawned]
Re-review: [Yes — prior verdict was X on YYYY-MM-DD / No — first review]

### Completeness: [X/8 sections present]
[List missing sections]

### Dependency Graph
[List each declared dependency and whether its GDD file exists on disk]
- ✓ enemy-definition-data.md — exists
- ✗ loot-system.md — NOT FOUND (file does not exist yet)

### Required Before Implementation
[Numbered list — blocking issues only. Each item tagged with source agent.]

### Recommended Revisions
[Numbered list — important but not blocking. Source-tagged.]

### Specialist Disagreements
[Any cases where agents disagreed with each other or with the main review.
Present both sides — do not silently resolve.]

### Nice-to-Have
[Minor improvements, low priority.]

### Senior Verdict [creative-director]
[Creative director's synthesis and overall assessment.]

### Scope Signal
Estimate implementation scope based on: dependency count, formula count,
systems touched, and whether new ADRs are required.
- **S** — single system, no formulas, no new ADRs, <3 dependencies
- **M** — moderate complexity, 1-2 formulas, 3-6 dependencies
- **L** — multi-system integration, 3+ formulas, may require new ADR
- **XL** — cross-cutting concern, 5+ dependencies, multiple new ADRs likely
Label clearly: "Rough scope signal: M (producer should verify before sprint planning)"

### Verdict: [APPROVED / NEEDS REVISION / MAJOR REVISION NEEDED]
```

This skill is read-only — no files are written during Phase 4.

---

## Phase 5: Next Steps

Use `AskUserQuestion` for ALL closing interactions. Never plain text.

**First widget — what to do next:**

If APPROVED (first-pass, no revision needed), proceed directly to the systems-index widget, review-log widget, then the final closing widget. Do not show a separate "what to do" widget — the final closing widget covers next steps.

If NEEDS REVISION or MAJOR REVISION NEEDED, options:
- `[A] Revise the GDD now — address blocking items together`
- `[B] Stop here — revise in a separate session`
- `[C] Accept as-is and move on (only if all items are advisory)`

**If user selects [A] — Revise now:**

Work through all blocking items, asking for design decisions only where you cannot resolve the issue from the GDD and existing docs alone. Group all design-decision questions into a single multi-tab `AskUserQuestion` before making any edits — do not interrupt mid-revision for each blocker individually.

**Batched edits rule:** When applying fixes, group all changes to the same GDD section into a single Edit call. Do not apply one Edit per blocker. Identify every change needed within a section, then apply them together. This significantly reduces context consumption and Edit churn.

**GDD Revision Triad:** After all GDD edits are complete, always update `design/registry/entities.yaml` (for any new or changed entities/stats) and `production/session-state/active.md` (current progress) before considering the revision done.

After all revisions are complete, show a summary table (blocker → fix applied) and use `AskUserQuestion` for a **post-revision closing widget**:

- Prompt: "Revisions complete — [N] blockers resolved. What next?"
- Note current context usage: if context is above ~40%, add: "(Recommended: /clear before re-review — this session has used X% context. A full re-review spawns 6+ agents and needs clean context.)"
- Options:
  - `[A] Re-review in a new session — run /design-review [doc-path] after /clear`
  - `[B] Accept revisions and mark Approved — update systems index, skip re-review`
  - `[C] Move to next system — /design-system [next-system] (#N in design order)`
  - `[D] Stop here`

Never end the revision flow with plain text. Always close with this widget.

**Second widget — systems index update (always show this separately):**

Use a second `AskUserQuestion`:
- Prompt: "May I update `design/gdd/systems-index.md` to mark [system] as [In Review / Approved]?"
- Options: `[A] Yes — update it` / `[B] No — leave it as-is`

**Third widget — review log (always offer):**

Use a third `AskUserQuestion`:
- Prompt: "May I append this review summary to `design/gdd/reviews/[doc-name]-review-log.md`? This creates a revision history so future re-reviews can track what changed."
- Options: `[A] Yes — append to review log` / `[B] No — skip`

If yes, append an entry in this format:
```
## Review — [YYYY-MM-DD] — Verdict: [APPROVED / NEEDS REVISION / MAJOR REVISION NEEDED]
Scope signal: [S/M/L/XL]
Specialists: [list]
Blocking items: [count] | Recommended: [count]
Summary: [2-3 sentence summary of key findings from creative-director verdict]
Prior verdict resolved: [Yes / No / First review]
```

---

**Final closing widget — always show after all file writes complete:**

Once the systems-index and review-log widgets are answered, check project state and show one final `AskUserQuestion`:

Before building options, read:
- `design/gdd/systems-index.md` — find any system with Status: In Review or NEEDS REVISION (other than the one just reviewed)
- Count `.md` files in `design/gdd/` (excluding game-concept.md, systems-index.md) to determine if `/review-all-gdds` is worth offering (≥2 GDDs)
- Find the next system with Status: Not Started in design order

Build the option list dynamically — only include options that are genuinely next:
- `[_] Run /design-review [other-gdd-path] — [system name] is still [In Review / NEEDS REVISION]` (include if another GDD needs review)
- `[_] Run /consistency-check — verify this GDD's values don't conflict with existing GDDs` (always include if ≥1 other GDD exists)
- `[_] Run /review-all-gdds — holistic design-theory review across all designed systems` (include if ≥2 GDDs exist)
- `[_] Run /design-system [next-system] — next in design order` (always include, name the actual system)
- `[_] Stop here`

Assign letters A, B, C… only to included options. Mark the most pipeline-advancing option as `(recommended)`.

Never end the skill with plain text after file writes. Always close with this widget.
