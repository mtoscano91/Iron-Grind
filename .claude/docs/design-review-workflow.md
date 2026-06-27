# Design Review Workflow

This document codifies the repeatable design review pipeline used throughout
pre-production. Follow it exactly — do not improvise shortcuts.

## Core Principle

Design reviews are not conversations. They are a structured pipeline with
explicit phases, parallel specialist delegation, and a defined atomic update
unit. Every review ends with the same three files updated together or not at all.

---

## One Job Per Session

**Each session has exactly one goal.** Never mix authoring and reviewing in the same session.

| Session type | Goal | End state |
|-------------|------|-----------|
| **Authoring session** | Apply blocker fixes to one document | GDD + entities.yaml + session-state all updated; stop |
| **Review session** | Run one `/design-review` on one document | Verdict output + review log appended + session-state updated; stop |

Mixing the two (apply fixes → review in same session → more fixes) compounds context cost on every iteration. The context accumulated from reading files and making edits is still present when the review spawns 5-6 agents — each of which gets that full context reconstructed.

---

## Review Mode Decision

**Before running `/design-review`, choose the right depth.** Full reviews are expensive. Lean reviews are ~80% cheaper. Use the right tool:

| Situation | Use | Why |
|-----------|-----|-----|
| First pass on any document | `--depth full` | Discovery — all domains need coverage |
| Re-review after structural rewrite (sections added, document split, major rearrangement) | `--depth full` | New structure needs fresh specialist eyes |
| Re-review after **targeted blocker fixes** (same structure, specific gaps addressed) | `--depth lean` | Verify fixes closed the gaps; escalate to full only if lean finds new structural issues |
| 3+ consecutive MAJOR REVISION NEEDED entries in the review log | **Stop — primitive extraction first** | Reviewing again will reproduce the same findings. Write the missing contract first. |

**The default lean path for iterative fixes:**
```
Apply fixes (authoring session) → /clear → /design-review --depth lean (verify session)
```
Only escalate to `--depth full` if the lean review finds a new structural issue not present in the prior pass.

---

## Primitive Contract Check (Before Every Review)

Before running any `/design-review`, spend 2 minutes checking whether the document references primitives that don't exist yet on disk. A "primitive" is any concept that is *named and used* in the document but whose full specification lives elsewhere.

Signs of an undefined primitive:
- "type unspecified", "schema TBD", "see [ADR not yet written]"
- A token, message, or interface mentioned in rules but not defined anywhere
- A dependency GDD that is "Not Started" in systems-index.md but is cited as the source for a key value

**Check:** `grep -i "unspecified\|TBD\|pending\|not yet authored" design/gdd/[doc].md`

If a primitive is missing: **write the primitive spec first, then review.** A full review on a document with undefined primitives guarantees MAJOR REVISION NEEDED — specialists will independently re-discover the same gap from 6 different angles.

---

## Convergence Threshold — Stop Reviewing, Start Extracting

If a document has had **3 or more consecutive MAJOR REVISION NEEDED** verdicts:

1. Read the last 3 review log entries. List every blocker cluster that appears in at least 2 of the 3 entries.
2. For each recurring cluster: is the root cause a missing upstream contract (undefined token, unspecified state machine, missing interface)?
3. If yes: **do not run another review.** Extract the missing contract as a standalone spec, get it approved, then resume reviewing the parent document.

The pattern "same cluster, different pass" means the review is finding symptoms, not the root cause. Revision passes on the symptom document cannot fix a missing upstream contract.

---

## Propagation Check (Before Closing Any Authoring Session)

Before ending any session where you edited a GDD, run a quick cross-document consistency check:

1. Identify every concept you changed (rule names, message names, state names, values).
2. For each concept: `grep -l "[concept]" design/gdd/*.md` — list all files that reference it.
3. Open each file and verify the reference is consistent with the change you made.

This takes 2–3 minutes and eliminates the most common cause of re-review blockers: a fix applied in one place that leaves an inconsistency in a sibling document.

**Minimum propagation check targets for networking documents:**
- Any change to a state name → check all 4 networking files
- Any change to a message name or schema → check wire-protocol + test-harness
- Any change to a formula variable → check entities.yaml + any GDD that cites the formula

---

## The GDD Revision Triad

**Every GDD revision is a single atomic unit of work.** When a GDD is edited,
three files must be updated together before the task is considered complete:

| File | What to update |
|------|----------------|
| `design/gdd/[system].md` | The revised GDD content |
| `design/registry/entities.yaml` | Any new or changed entities, stats, or systems |
| `production/session-state/active.md` | Current task, what changed, open questions |

Never treat these as optional or sequential. If context runs out before all
three are updated, save state explicitly and continue in a new session.

**Note on `systems-index.md`:** `design/gdd/systems-index.md` is a fourth file
that must be updated when a GDD status changes (e.g., In Review → Approved).
Always update it alongside the triad when status changes.

---

## Specialist Reviews — Tiered, Not Always Full

Full specialist panels (6 agents) are for discovery and structural rewrites. Targeted fixes use lean. When a full review is warranted, spawn all specialists in parallel — never sequentially.

**When to spawn a full specialist panel:**
- First pass on a new document
- Any pass following a structural rewrite (document split, new major sections)
- After a lean re-review flags a new structural issue not present in the prior pass

**Baseline minimum for any full review:**
- `game-designer` — core mechanics and player fantasy alignment
- `systems-designer` — formula boundary checks and data schema
- `qa-lead` — acceptance criteria testability audit

**Add domain specialists based on GDD content** (see routing table in design-review SKILL.md).

**Why parallel, not sequential:**
Sequential specialist reviews consume the main-thread context budget. Spawning all specialists via parallel Task calls keeps analysis in subagent context windows, preserving the main thread for synthesis.

**Specialist prompt segmentation:**
Do NOT send the full GDD text to every specialist. Each agent receives only the sections relevant to their domain (see section mapping table in design-review SKILL.md). This reduces per-agent prompt cost by 30–50% without reducing review quality.

---

## Batched Blocker Fixes

When applying blocker fixes after a review:

1. **Group all fixes for the same GDD section into one Edit call** — do not
   apply one Edit per blocker. Identify all changes needed in a section, then
   apply them in a single operation.
2. **Order of edits:** GDD first, then entity registry, then session state.
3. **Do not start a new review pass until all three triad files are updated.**
4. **Run the Propagation Check before closing the session.**

This reduces Edit churn (the primary cause of context exhaustion mid-review)
and ensures the session state always reflects the current ground truth.

---

## Context Budget for Design Reviews

| Phase | Expected context cost | Reserve rule |
|-------|-----------------------|--------------|
| Load + Phase 1-3 analysis | ~15–20% | — |
| Primitive Readiness + Convergence Check | ~2–3% | Runs in main context, no agents |
| Phase 3b specialist spawning (full) | ~10–15% | Spawn all in parallel |
| Phase 3b lean analysis | ~5–8% | No agents |
| Blocker fix edits | ~20–30% | Batch per section |
| Final approval pass + triad update | ~15–20% | **Reserve this upfront** |

**Rule:** If context reaches 40% before the final approval pass begins, stop,
complete the triad update, and recommend re-review in a new session.

**Proactive checkpoint cadence:** Write a session state update after every
10 Edit/Write operations, or whenever context crosses 60% — whichever comes
first.

---

## What "Approved" Means

A GDD is only Approved when all of the following are true:

- [ ] Zero blocking items remain from the most recent specialist review
- [ ] All acceptance criteria are independently testable (no "feels" or "works correctly")
- [ ] All dependency GDD files exist on disk
- [ ] Entity registry reflects the GDD's entities and stats
- [ ] Review log entry appended to `design/gdd/reviews/[doc]-review-log.md`
- [ ] `systems-index.md` updated to Approved status

Skipping any of these makes the approval provisional, not real.
