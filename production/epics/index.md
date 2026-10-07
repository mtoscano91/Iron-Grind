# Epics Index

Last Updated: 2026-10-07
Engine: Unity 6.3 LTS (6000.3)
Control Manifest Version: 2026-06-28

| Epic | Layer | System | GDD | Stories | Status |
|------|-------|--------|-----|---------|--------|
| [Character Stats](character-stats/EPIC.md) | Foundation | Character Stats | design/gdd/character-stats.md | 8 stories (001-008 Complete) | Complete |
| [Item Database](item-database/EPIC.md) | Foundation | Item Database | design/gdd/item-database.md | 4 stories (001-004), all Complete | Complete |
| [Currency System](currency-system/EPIC.md) | Foundation | Currency System | design/gdd/currency-system.md | 6 stories (001-006), all Complete | Complete |
| [Networking Core](networking-core/EPIC.md) | Foundation | Networking Core (+ 10 sub-contracts) | design/gdd/networking-core.md | 30 stories (001-030): 29 Complete, 1 Ready (030) | In Progress |
| [Authentication](authentication/EPIC.md) | Core | Authentication | design/gdd/authentication.md | Not yet created | Ready |
| [Damage Calculation](damage-calculation/EPIC.md) | Core | Damage Calculation | design/gdd/damage-calculation.md | Not yet created | Ready — **blocked on server/client assembly-boundary ADR before Story 001** |
| [Leveling System](leveling-system/EPIC.md) | Core | Leveling System | design/gdd/leveling-system.md | 13 stories (001-013 Complete) | Complete |
| [Inventory System](inventory-system/EPIC.md) | Core | Inventory System | design/gdd/inventory-system.md | 9 stories (001-009 Ready) | Ready |
| [Loot Table System](loot-table-system/EPIC.md) | Core | Loot Table System | design/gdd/loot-table-system.md | Not yet created | Ready |
| [Status Effects / Buffs](status-effects/EPIC.md) | Core | Status Effects | design/gdd/status-effects.md | Not yet created | Ready |
| [Equipment System](equipment-system/EPIC.md) | Core | Equipment System | design/gdd/equipment-system.md | Not yet created | Ready — **design gate OQ-EQS-9 open before `/create-stories`** |
| [Enhancement System](enhancement-system/EPIC.md) | Feature | Enhancement System | design/gdd/enhancement-system.md | 11 stories (001-008 Complete; 009 Blocked on the request dispatcher, 010 Blocked on TD-046, 011 Blocked on Character Persistence) | In Progress |

## Notes

- **TR registry**: `docs/architecture/tr-registry.yaml` is empty — no per-TR IDs have been minted. Populate before running `/story-readiness` checks on any story in these epics.
- **architecture.md stale**: Foundation module table still shows `⚠️` markers for ADR-009 and ADR-010, which were accepted 2026-06-27. Update `docs/architecture/architecture.md` Foundation module table entries.
- **Damage Calculation epic has a real, GDD-stated architecture gap**: the GDD's own Core Rules text requires an ADR for the `ServerLogic.asmdef` server/client assembly boundary before implementation begins — this is not yet written. Run `/architecture-decision` before starting Damage Calculation Story 001.
- **Authentication epic has a partial architecture gap**: CR-AUTH-4's standalone .NET sidecar/IPC process has no Accepted ADR, and its two primitive GDDs (`auth-wire-messages.md`, `auth-sidecar-ipc.md`) are still Draft. Only the sidecar-boundary stories are affected — the AccountID/credential-contract stories are not blocked.
- **Core layer epics now created**: Leveling, Inventory, and Loot Table appear in the Foundation module table of `architecture.md` but are classified as Core layer in `systems-index.md` (authoritative) — resolved by creating them here as Core-layer epics, per that authoritative classification.
- **Equipment and Enhancement epics created 2026-10-07**: the two systems are mutually dependent (Equipment consumes `IEnhancementBonusProvider`; Enhancement relies on Equipment to store the level while equipped). Enhancement was created ahead of the gate its GDD states ("before `/create-epics`": OQ-ENH-7, TD-046) by user decision, so its formula and bonus-provider stories can be planned with Equipment; its attempt-path stories stay Blocked until both gates close.

## Layer Coverage

| Layer | Epics Created | Remaining |
|-------|--------------|-----------|
| Foundation | 4 / 4 (all Complete) | — |
| Core | 7 created (Equipment added 2026-10-07) | Character Persistence, Hit Detection, Movement and Skill System have no epic yet; run `/create-stories [epic-slug]` per created epic |
| Feature | 1 (Enhancement, created 2026-10-07 ahead of its two gates) | Run `/create-epics layer: feature` for the rest after Core is nearly complete |
| Presentation | 0 | Run `/create-epics layer: presentation` after Feature is nearly complete |
