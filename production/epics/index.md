# Epics Index

Last Updated: 2026-07-22
Engine: Unity 6.3 LTS (6000.3)
Control Manifest Version: 2026-06-28

| Epic | Layer | System | GDD | Stories | Status |
|------|-------|--------|-----|---------|--------|
| [Character Stats](character-stats/EPIC.md) | Foundation | Character Stats | design/gdd/character-stats.md | 8 stories (001-008 Complete) | Complete |
| [Item Database](item-database/EPIC.md) | Foundation | Item Database | design/gdd/item-database.md | 4 stories (001-004), all Complete | Complete |
| [Currency System](currency-system/EPIC.md) | Foundation | Currency System | design/gdd/currency-system.md | 6 stories (001-006), all Complete | Complete |
| [Networking Core](networking-core/EPIC.md) | Foundation | Networking Core (+ 10 sub-contracts) | design/gdd/networking-core.md | 29 stories (001-029), all Complete | Complete |
| [Authentication](authentication/EPIC.md) | Core | Authentication | design/gdd/authentication.md | Not yet created | Ready |
| [Damage Calculation](damage-calculation/EPIC.md) | Core | Damage Calculation | design/gdd/damage-calculation.md | Not yet created | Ready — **blocked on server/client assembly-boundary ADR before Story 001** |
| [Leveling System](leveling-system/EPIC.md) | Core | Leveling System | design/gdd/leveling-system.md | 13 stories (001-013 Complete) | Complete |
| [Inventory System](inventory-system/EPIC.md) | Core | Inventory System | design/gdd/inventory-system.md | 9 stories (001-009 Ready) | Ready |
| [Loot Table System](loot-table-system/EPIC.md) | Core | Loot Table System | design/gdd/loot-table-system.md | Not yet created | Ready |
| [Status Effects / Buffs](status-effects/EPIC.md) | Core | Status Effects | design/gdd/status-effects.md | Not yet created | Ready |

## Notes

- **TR registry**: `docs/architecture/tr-registry.yaml` is empty — no per-TR IDs have been minted. Populate before running `/story-readiness` checks on any story in these epics.
- **architecture.md stale**: Foundation module table still shows `⚠️` markers for ADR-009 and ADR-010, which were accepted 2026-06-27. Update `docs/architecture/architecture.md` Foundation module table entries.
- **Damage Calculation epic has a real, GDD-stated architecture gap**: the GDD's own Core Rules text requires an ADR for the `ServerLogic.asmdef` server/client assembly boundary before implementation begins — this is not yet written. Run `/architecture-decision` before starting Damage Calculation Story 001.
- **Authentication epic has a partial architecture gap**: CR-AUTH-4's standalone .NET sidecar/IPC process has no Accepted ADR, and its two primitive GDDs (`auth-wire-messages.md`, `auth-sidecar-ipc.md`) are still Draft. Only the sidecar-boundary stories are affected — the AccountID/credential-contract stories are not blocked.
- **Core layer epics now created**: Leveling, Inventory, and Loot Table appear in the Foundation module table of `architecture.md` but are classified as Core layer in `systems-index.md` (authoritative) — resolved by creating them here as Core-layer epics, per that authoritative classification.

## Layer Coverage

| Layer | Epics Created | Remaining |
|-------|--------------|-----------|
| Foundation | 4 / 4 (all Complete) | — |
| Core | 6 / 6 created (0 implemented) | Run `/create-stories [epic-slug]` per epic |
| Feature | 0 | Run `/create-epics layer: feature` after Core is nearly complete |
| Presentation | 0 | Run `/create-epics layer: presentation` after Feature is nearly complete |
