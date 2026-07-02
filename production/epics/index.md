# Epics Index

Last Updated: 2026-06-27
Engine: Unity 6.3 LTS (6000.4)
Control Manifest Version: 2026-06-27

| Epic | Layer | System | GDD | Stories | Status |
|------|-------|--------|-----|---------|--------|
| [Character Stats](character-stats/EPIC.md) | Foundation | Character Stats | design/gdd/character-stats.md | Not yet created | Ready |
| [Item Database](item-database/EPIC.md) | Foundation | Item Database | design/gdd/item-database.md | Not yet created | Ready |
| [Currency System](currency-system/EPIC.md) | Foundation | Currency System | design/gdd/currency-system.md | Not yet created | Ready |
| [Networking Core](networking-core/EPIC.md) | Foundation | Networking Core (+ 10 sub-contracts) | design/gdd/networking-core.md | Not yet created | Ready |

## Notes

- **TR registry**: `docs/architecture/tr-registry.yaml` is empty — no per-TR IDs have been minted. Populate before running `/story-readiness` checks on any story in these epics.
- **architecture.md stale**: Foundation module table still shows `⚠️` markers for ADR-009 and ADR-010, which were accepted 2026-06-27. Update `docs/architecture/architecture.md` Foundation module table entries.
- **Core layer epics**: Leveling, Inventory, and Loot Table appear in the Foundation module table of `architecture.md` but are classified as Core layer in `systems-index.md` (authoritative). Run `/create-epics layer: core` to create those epics.

## Layer Coverage

| Layer | Epics Created | Remaining |
|-------|--------------|-----------|
| Foundation | 4 / 4 | — |
| Core | 0 | Run `/create-epics layer: core` |
| Feature | 0 | Run `/create-epics layer: feature` after Core is nearly complete |
| Presentation | 0 | Run `/create-epics layer: presentation` after Feature is nearly complete |
