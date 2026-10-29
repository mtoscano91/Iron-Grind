# Epics Index

Last Updated: 2026-10-29
Engine: Unity 6.3 LTS (6000.3)
Control Manifest Version: 2026-10-26

| Epic | Layer | System | GDD | Stories | Status |
|------|-------|--------|-----|---------|--------|
| [Character Stats](character-stats/EPIC.md) | Foundation | Character Stats | design/gdd/character-stats.md | 8 stories (001-008 Complete) | Complete |
| [Item Database](item-database/EPIC.md) | Foundation | Item Database | design/gdd/item-database.md | 4 stories (001-004), all Complete | Complete |
| [Currency System](currency-system/EPIC.md) | Foundation | Currency System | design/gdd/currency-system.md | 6 stories (001-006), all Complete | Complete |
| [Networking Core](networking-core/EPIC.md) | Foundation | Networking Core (+ 10 sub-contracts) | design/gdd/networking-core.md | 45 stories (001-045), all Complete (045, the zone server host and local verification, 2026-10-22); the Linux IL2CPP headless-build engine-risk gate is still open | Stories Complete |
| [Authentication](authentication/EPIC.md) | Core | Authentication | design/gdd/authentication.md | Not yet created | Ready |
| [Damage Calculation](damage-calculation/EPIC.md) | Core | Damage Calculation | design/gdd/damage-calculation.md | 7 stories (001-006 Complete; 007 Blocked on the client build pipeline) | In Progress |
| [Leveling System](leveling-system/EPIC.md) | Core | Leveling System | design/gdd/leveling-system.md | 16 stories (001-016 Complete) | Complete |
| [Inventory System](inventory-system/EPIC.md) | Core | Inventory System | design/gdd/inventory-system.md | 12 stories (001-012 Complete) | Complete |
| [Loot Table System](loot-table-system/EPIC.md) | Core | Loot Table System | design/gdd/loot-table-system.md | 15 stories (001-015 Complete) | Complete |
| [Status Effects / Buffs](status-effects/EPIC.md) | Core | Status Effects | design/gdd/status-effects.md | 9 stories (001-009), all Complete (009 on 2026-10-29); TR-se-006 (client buff display) deferred | Complete |
| [Equipment System](equipment-system/EPIC.md) | Core | Equipment System | design/gdd/equipment-system.md | 9 stories (001-009 written 2026-10-26): 9 Complete (001-008 on 2026-10-26, 009 on 2026-10-28) | Complete — unblocks Character Persistence Story 005 |
| [Zone Instancing](zone-instancing/EPIC.md) | Core | Zone Instancing (zone-process side) | design/gdd/zone-instancing.md | 14 stories (001-014, written 2026-10-22), all Complete (2026-10-26) | Complete — routing ACs (AC-ZI-1, 2, 3, 16, 19, 20) move with the gateway work (needs a gateway ADR + epic) |
| [Character Persistence](character-persistence/EPIC.md) | Core | Character Persistence | design/gdd/character-persistence.md | 17 stories (001-016 written 2026-10-12, 017 added 2026-10-16): 17 Complete (last 016 on 2026-10-28); DoD open: ADR-006 Verification 1–3, AC-CP-27/28/29 | In Progress |
| [Movement System](movement-system/EPIC.md) | Core | Movement System | design/gdd/movement-system.md | 13 stories (001-013 written 2026-10-29; server 001-009, client 010-013): 5 Complete (001-005), 8 Ready | In Progress |
| [Enhancement System](enhancement-system/EPIC.md) | Feature | Enhancement System | design/gdd/enhancement-system.md | 16 stories (001-016 Complete); DoD open: AC-ENH-2 and the other deferred criteria | In Progress |

## Notes

- **TR registry**: `docs/architecture/tr-registry.yaml` is empty — no per-TR IDs have been minted. Populate before running `/story-readiness` checks on any story in these epics.
- **architecture.md stale**: Foundation module table still shows `⚠️` markers for ADR-009 and ADR-010, which were accepted 2026-06-27. Update `docs/architecture/architecture.md` Foundation module table entries.
- **Damage Calculation epic has a real, GDD-stated architecture gap**: the GDD's own Core Rules text requires an ADR for the `ServerLogic.asmdef` server/client assembly boundary before implementation begins — this is not yet written. Run `/architecture-decision` before starting Damage Calculation Story 001. **Resolved 2026-10-08**: ADR-012 (Server/Client Assembly Boundary) is Accepted; Story 006 (first move and boundary test) is Ready and Story 007 (client-binary scan) waits for a client build pipeline.
- **Authentication epic has a partial architecture gap**: CR-AUTH-4's standalone .NET sidecar/IPC process has no Accepted ADR, and its two primitive GDDs (`auth-wire-messages.md`, `auth-sidecar-ipc.md`) are still Draft. Only the sidecar-boundary stories are affected — the AccountID/credential-contract stories are not blocked.
- **Core layer epics now created**: Leveling, Inventory, and Loot Table appear in the Foundation module table of `architecture.md` but are classified as Core layer in `systems-index.md` (authoritative) — resolved by creating them here as Core-layer epics, per that authoritative classification.
- **Equipment and Enhancement epics created 2026-10-07**: the two systems are mutually dependent (Equipment consumes `IEnhancementBonusProvider`; Enhancement relies on Equipment to store the level while equipped). Enhancement was created ahead of the gate its GDD states ("before `/create-epics`": OQ-ENH-7, TD-046) by user decision, so its formula and bonus-provider stories can be planned with Equipment; its attempt-path stories stay Blocked until both gates close.

## Layer Coverage

| Layer | Epics Created | Remaining |
|-------|--------------|-----------|
| Foundation | 4 / 4 (all Complete) | — |
| Core | 8 created (Equipment added 2026-10-07, Character Persistence 2026-10-12) | Hit Detection and Skill System have no epic yet (Movement added 2026-10-29); run `/create-stories [epic-slug]` per created epic |
| Feature | 1 (Enhancement, created 2026-10-07 ahead of its two gates) | Run `/create-epics layer: feature` for the rest after Core is nearly complete |
| Presentation | 0 | Run `/create-epics layer: presentation` after Feature is nearly complete |
