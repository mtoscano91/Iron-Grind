# Story 001: Shop Catalog and Startup Validation

> **Epic**: NPC Shop
> **Status**: Ready
> **Layer**: Feature
> **Type**: Logic
> **Estimate**: 3 h
> **Manifest Version**: 2026-11-06

## Context

**GDD**: `design/gdd/npc-shop.md` — CR-SHOP-2, CR-SHOP-10, F-NS-1 (overflow guard), F-NS-3, Edge Cases (Startup validation), Tuning Knobs
**Requirement**: `TR-shop-001`
*(Requirement text lives in `docs/architecture/tr-registry.yaml` — read fresh at review time)*

**ADR Governing Implementation**: ADR-001: Purchase Transaction Integrity (catalog feeds the buy path); ADR-012 (server assembly)
**ADR Decision Summary**: Shop logic is server-only in `IronGrind.ServerLogic`; buy prices are authored data, validated before any session begins.

**Engine**: Unity 6.3 LTS | **Risk**: LOW
**Engine Notes**: ScriptableObject data asset — serialized private fields only (`[SerializeField]` on properties is a compile error in 6.3).

**Control Manifest Rules (this layer)**:
- Required: gameplay values data-driven (no hardcoded prices); server-authoritative
- Forbidden: `[SerializeField]` on properties
- Guardrail: startup validation runs once; lookups allocation-free

---

## Acceptance Criteria

- [ ] **AC-1 Catalog asset**: an `NpcShopCatalog` ScriptableObject lists entries `(ItemID, BuyPrice: uint)` in authored order (scrolls first, then potions), plus `QuantityCap` (99) and the anti-arbitrage ratio as an integer pair (`RatioNumerator` 15, `RatioDenominator` 10). The authored asset holds exactly the 10 MVP items — Bronze / Iron / Steel / Dark Steel Enhancement Scroll (ids 35–38: 90 / 150 / 230 / 350 g) and HP / MP Potion Small / Medium / Large (ids 29–34: 3 / 10 / 30 g) (AC-NS-30).
- [ ] **AC-2 Lookup and reason**: the validated catalog's `TryGetEntry(ItemID, out entry)` returns true for the 10 items and false for any other id (e.g. an equipment id such as Iron Sword) — the basis of `RejectedItemNotInCatalog` (AC-NS-31 at Story 006). Each entry's transaction reason is derived, not authored: `ScrollData != null` → `ScrollPurchase`, otherwise `ItemPurchase` (G2).
- [ ] **AC-3 F-NS-3**: validation computes `(ulong)BuyPrice × RatioDenominator ≥ (ulong)SellPriceGold × RatioNumerator` for every entry whose Item Database `SellPriceGold > 0` (`≤ 0` is exempt; negatives are already rejected by `ItemDefinitionValidator`); vectors with 10 / 15: Small 30 ≥ 30 passes (boundary), Medium 100 ≥ 90, Large 300 ≥ 270 (AC-NS-27).
- [ ] **AC-4 Scroll exemption**: a catalog with a scroll (`SellPriceGold = 0`) and a potion at BuyPrice 2 / SellPriceGold 2 fails for the potion only; the scroll produces no failure (AC-NS-27 positive test).
- [ ] **AC-5 Validator**: `NpcShopCatalogValidator.Validate(catalog, IItemDatabase)` is a pure function returning every failure (no early stop), each naming ItemID, BuyPrice and SellPriceGold: F-NS-3 violation; `BuyPrice = 0`; an ItemID missing from the Item Database; a duplicate ItemID; an empty catalog; `QuantityCap` outside [1, 99] (wire `quantity` is a byte; GDD tuning range [10, 99]); `RatioNumerator` or `RatioDenominator` = 0; `(ulong)BuyPrice × QuantityCap > uint.MaxValue` (G3). On success it returns a read-only validated catalog (lookup of AC-2).
- [ ] **AC-6 S2 loader**: `ZoneShopCatalogLoader : IZoneStaticDataLoader, IZoneShopCatalogData` runs as the `rest` of `ZoneItemDataLoader` (it needs the item database that loader builds — take it through a `Func<IItemDatabase>` or the `IZoneItemData` it wraps), validates, logs one `[ZoneServer] S2: …` error per failure and returns false (S-fail, no partial catalog — AC-NS-28); on success exposes `LoadedShopCatalog`, then runs its own `rest`. A null catalog asset → one S2 error, false (G1).
- [ ] **AC-7 Data, not literals**: the quantity cap and the ratio are read from the validated catalog, never written as literals in shop code.

---

## Implementation Notes

- New folder `src/ServerLogic/NpcShop/`, namespace `IronGrind.NpcShop`. Asset under `Assets/data/` (e.g. `Assets/data/Shop/NpcShopCatalog.asset`), following the other data assets.
- Validation is a pure function over the catalog entries and an `IItemDatabase`, returning a failure list — testable without Unity assets (build entries in code for tests; one test loads the authored asset and validates it against the real item records).
- Loader pattern: copy `ZoneItemDataLoader` / `ZoneMobDataLoader` (`LOG_PREFIX = "[ZoneServer] S2: "`, `LoadAndValidate()` returns bool, forward the other `IZone*Data` interfaces of `rest`). The host chain today is `ZoneMobDataLoader → ZoneClassSkillDataLoader → ZoneItemDataLoader → StandInZoneStaticDataLoader`; the catalog loader goes between `ZoneItemDataLoader` and the stand-in. Outer loaders must forward `IZoneShopCatalogData`, as they forward `IZoneItemData`.
- `ItemDefinition.SellPriceGold` is `int`; cast after the `> 0` check. `ItemDefinition.ScrollData != null` is the scroll test.
- Item records exist (`Assets/data/items/consumables/ItemID(29–38)_*.asset`; sell 2 / 6 / 18 g, scrolls 0), so OQ-NS-4 is done.
- Equipment items (Sell tab only) are not in the catalog and are never checked by F-NS-3.

---

## Readiness Decisions (2026-11-19)

- **G1 Halt via S2.** Startup failure follows the static-data loader pattern (log `[ZoneServer] S2:` + return false), not a composition exception. This story builds the validator and `ZoneShopCatalogLoader`; Story 009 adds the `ZoneServerHost` serialized field, puts the loader in the host chain, references the asset in the zone server scene and reads `IZoneShopCatalogData` in the composition (with `NpcShopCatalogOverride` for tests).
- **G2 Derived reason.** No authored transaction reason; derived from `ScrollData`.
- **G3 Ranges.** Cap in [1, 99]; ratio parts > 0; overflow checked with the cap; `SellPriceGold ≤ 0` exempt.

---

## Out of Scope

- Story 002: sessions; Story 006: the buy path that reads the catalog
- Story 009: host field, loader in the host chain, scene reference, composition read

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refined at `/story-readiness` 2026-11-19.*

- **AC-1**: load the authored asset → 10 entries in order 35, 36, 37, 38, 29–34 with the listed prices; cap 99; ratio 15 / 10; no id repeated.
- **AC-2**: every catalog id → true with the right price; scrolls → `ScrollPurchase`, potions → `ItemPurchase`; Iron Sword id, 0, an unknown id → false.
- **AC-3**: authored catalog + real item records → no failures; Small potion at 3 / 2 → passes exactly at the boundary; at 2 / 2 → fails.
- **AC-4**: scroll + bad potion → exactly one failure, for the potion id.
- **AC-5**: one test per failure kind → that failure in the list naming the id; a catalog with two bad entries → two failures (no early stop); price 50,000,000 with cap 99 → overflow failure.
- **AC-6**: loader with a fake item data source: valid → true, `LoadedShopCatalog` set, `rest` run once; invalid → false, one `LogAssert.Expect(LogType.Error, "[ZoneServer] S2: …")` per failure, `rest` not run; null asset → false + one error.
- **AC-7**: a catalog with cap 50 → validated `QuantityCap` 50; grep of `src/ServerLogic/NpcShop/` finds no literal 99 / 15 outside the asset defaults.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Catalog_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 006, 009
