# Enemy AI — Design Review Log

---

## Review — 2026-05-25 (Pass 3 lean) — Verdict: APPROVED
Scope signal: L
Specialists: none (lean — single-session)
Blocking items: 0 | Recommended: 1 (R1 applied inline)
Summary: All three Pass 2 blockers confirmed resolved: F-AI-E-1 header uses `_rng.NextDouble()`, `IRandomProvider` interface fully defined in CR-AI-12, Dead state calls `ILootTableSystem.ResolveMobDrop()`. One advisory fix applied inline: Stat System dependency status corrected from "In Review" to "Approved". Document is internally consistent and implementable against `INavigationProvider` stub.
Prior verdict resolved: Yes — NEEDS REVISION → APPROVED

---

## Authoring Summary — 2026-05-25

All 11 sections authored in one session across two context windows.

Key design decisions made during authoring:
- **capturedForward**: Locked at WindingUp entry (T=0), passed unmodified to CheckHit on AttackExecution. Core of Rhythm Mastery mechanic.
- **Enraged mechanic**: Spawn-time roll only; SetBaseStat not AddBuffModifier; anti-bot gate for rewards.
- **Target selection**: First player to enter AggroRange; first-come-first-served; no transfer.
- **Attack cadence**: Cooldown starts at AttackExecution tick. Effective interval = AttackWindUpTicks + AttackRecoveryTicks + max(0, ATTACK_COOLDOWN_TICKS - AttackRecoveryTicks).
- **Wind-up desync**: Natural (no explicit stagger rule).
- **State machine**: 7 states — Dormant, Pursuing, WindingUp, AttackExecution, Recovering, Returning, Dead. Aggroed state removed as vestigial.
- **Navigation**: Provisional INavigationProvider stub interface. Full implementation blocked on Navigation/Pathfinding GDD.

Open items going into first review:
- OQ-AI-1: Loot Table System CR-LT-5 amendment (blocking for Enraged loot)
- OQ-AI-4: Mob AI tick budget at 150 mobs/zone (F-NET-6 carry from Hit Detection)
- OQ-AI-5: Re-aggro in party context on target death

GDD file: design/gdd/enemy-ai.md
Registry updates: MobTypeID, MIN_WIND_UP_TICKS added; TICK_RATE_HZ/ATTACK_RANGE_MIN/MAX_MOBS_PER_ZONE referenced_by updated.

---

## Review — 2026-05-25 (Pass 2 lean) — Verdict: NEEDS REVISION → Revision Applied (NEEDS REVISION, pending pass 3)
Scope signal: L
Specialists: none (lean — single-session)
Blocking items: 3 | Recommended: 4 (2 applied, 2 deferred)
Summary: Three targeted blockers found and fixed inline: F-AI-E-1 formula header retained `Random.value` (contradicting CR-AI-12); AC-AI-13 prescribed `IRandomProvider` injection but no interface was defined (CR-AI-12 extended with interface definition and injection requirement); OQ-AI-1 remained unresolved — Dead state called `LootTable.Roll()` which does not exist in the approved Loot Table GDD (resolved by specifying caller-side API contract `ILootTableSystem.ResolveMobDrop`). Recommendations R1 (CR-AI-11 NavMeshAgent leak) and R2 (Recovering timing wording) applied. Overall architecture sound; fixes are small and targeted.
Prior verdict resolved: Partially — 3 of the post-revision lingering issues addressed.

---

## Review — 2026-05-25 — Verdict: MAJOR REVISION NEEDED → Revision Applied (NEEDS REVISION)
Scope signal: L
Specialists: game-designer, systems-designer, ai-programmer, qa-lead, performance-analyst, unity-specialist, creative-director (senior)
Blocking items: 26 | Recommended: 20
Summary: The creative-director identified three root causes across 26 blockers: (1) FacingAngle type ambiguity (server float vs wire short conflated in F-AI-3 and ACs); (2) implementation hazards (int.MinValue overflow, `Random.value` thread-safety, `struct` with string field for IL2CPP); (3) behavioural underspecification (OQ-AI-5 unresolved, CR-LT-5 naming collision, AC-AI-12 contradicting itself). All 26 blockers addressed inline. OQ-AI-5 resolved (Option A — immediate re-scan on target death). EnragedChance default reduced 0.15 → 0.07. Four new rules added (CR-AI-13, CR-AI-14, EC-AI-9–12). Six new ACs added (AC-AI-21–26).
Prior verdict resolved: No — first review pass
