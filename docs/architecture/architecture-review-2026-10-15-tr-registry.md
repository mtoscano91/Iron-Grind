# Architecture Review — TR Registry Registration

> **Date:** 2026-10-15
> **Engine:** Unity 6.3 LTS (6000.3)
> **Mode:** `/architecture-review`, scope chosen by the user: register the existing epic TR-IDs. No requirement re-extraction from the GDDs, no cross-ADR conflict check, no engine audit — the last full review is `architecture-review-2026-10-07.md`.
> **Verdict:** CONCERNS (advisory) — all 107 TR-IDs now resolve; one GDD rule added after its epic has no TR-ID.

## Starting state

- `tr-registry.yaml` held 0 entries (`requirements: []`; its three `TR-combat-*` entries were commented examples). The 2026-10-09 traceability index recorded the gap.
- The 13 `production/epics/*/EPIC.md` tables define 107 TR-IDs, each with requirement text, its GDD rule and its ADR coverage. No ID is defined twice; every TR-ID cited by a story is defined in an epic table.

| System | IDs | System | IDs | System | IDs |
|---|---|---|---|---|---|
| auth | 5 | enh | 11 | loot | 12 |
| cp | 13 | equip | 11 | net | 9 |
| stats | 6 | inv | 4 | se | 6 |
| currency | 5 | itemdb | 6 | lvl | 13 |
| dmg | 6 | | | | |

## Checks

1. **Cited rules exist.** Every rule ID a TR row names (CR-, F-, AC-, Rule N) is still present in its GDD (for `networking-core`, in the networking sub-contracts).
2. **Wording drift.** Ten GDDs were changed after their epic's TR rows were last edited. The rule IDs touched by those changes were compared with the rows. Six rows were found stale and are registered with revised text (`revised: 2026-10-15`):

| TR-ID | GDD change | Revised text adds |
|---|---|---|
| TR-cp-003 | CR-CP-3 load split (2026-10-15) | `LoadCharacter` validates only; `ApplyLoadedRecord` writes on the tick thread |
| TR-cp-004 | CR-CP-4 (2026-10-15) | Every `SaveIrreversibleOutcome` writes the whole record |
| TR-cp-006 | CR-CP-6 (2026-10-15) | The `SaveVersion` re-read after an unknown outcome |
| TR-cp-007 | CR-CP-7 (2026-10-15) | Snapshot before the queue; per-method version rule |
| TR-cp-010 | CR-CP-10 reorder (2026-10-15) | Corrects "steps 1–6 gather state": steps 2–6 are read first, then step 1, then step 7; `SaveIrreversibleOutcome` runs steps 2–7 |
| TR-enh-007 | CR-ENH-18 failure branch (ADR-011 Decision 4) | Held requests are discarded with the disconnect when the write fails |

   TR-itemdb-005 is registered with `revised: 2026-10-02` from its own "Amendment #4" note. Inline "*(added …)*" / "*(revised …)*" notes were removed from the text; `created` is the date of the commit that first added each ID's row.
3. **No drift found** in the remaining touched rules: currency Rule 11 is unchanged since 2026-06-27 and `CompensatingRefund = 8` exists; damage calculation still has 11 steps; leveling CR-1.1 and F-LS-1, inventory Rules 1 and 3, and the enhancement message names (the GDD gives the wire names as aliases) still match their rows.

## Flags

- **CR-LT-16** (loot table validation needs a ready Item Database; scrolls excluded from loot tables), added 2026-10-07, has no TR-ID. Candidate: `TR-loot-013`, with a matching row in the loot epic table. Not registered — this pass registers existing IDs only.
- **TR-currency-003** says "rows-affected = 0 → `ConcurrencyConflict`"; currency Rule 11 first retries once inside Currency. Long-standing, not new drift; left unchanged.
- The EPIC tables keep their original text for the six revised rows. The registry is the authority for requirement text (`/story-done` and `/story-readiness` read it).

## Written

- `docs/architecture/tr-registry.yaml` — 107 entries, `version: 2`, example block removed
- `docs/architecture/architecture-traceability.md` — TR-ID count and source review line
