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

- [ ] **AC-1 Catalog asset**: an `NpcShopCatalog` ScriptableObject lists entries `(ItemID, BuyPrice: uint, TransactionReason)`; the authored asset holds exactly the 10 MVP items — Bronze / Iron / Steel / Dark Steel Enhancement Scroll (90 / 150 / 230 / 350 g, `ScrollPurchase`) and HP / MP Potion Small / Medium / Large (3 / 10 / 30 g, `ItemPurchase`) — ItemIDs 29–38 (AC-NS-30).
- [ ] **AC-2 Lookup**: `TryGetEntry(ItemID, out entry)` returns true for the 10 items and false for any other id (e.g. an equipment id such as Iron Sword) — the basis of `RejectedItemNotInCatalog` (AC-NS-31 at Story 006).
- [ ] **AC-3 F-NS-3**: validation computes `(BuyPrice × 10) ≥ (SellPriceGold × 15)` in integer arithmetic for every entry whose Item Database `SellPriceGold > 0`; vectors: Small 30 ≥ 30 passes (boundary), Medium 100 ≥ 90, Large 300 ≥ 270 (AC-NS-27).
- [ ] **AC-4 Scroll exemption**: a catalog with a scroll (`SellPriceGold = 0`) and a potion at BuyPrice 2 / SellPriceGold 2 fails for the potion only; the scroll produces no error (AC-NS-27 positive test).
- [ ] **AC-5 Halt**: any failure — F-NS-3 violation, `BuyPrice = 0`, an ItemID missing from the Item Database, a duplicate ItemID, or `BuyPrice × 99` overflowing `uint` — makes validation fail, logs one error per offending entry naming ItemID, BuyPrice and SellPriceGold, and the composition throws so the server does not start (no partial catalog) (AC-NS-28).
- [ ] **AC-6 Quantity cap**: `QUANTITY_SELECTOR_CAP = 99` and `ANTI_ARBITRAGE_RATIO` (stored as the integer pair 10 / 15) come from the catalog / config data, not literals in the handlers.

---

## Implementation Notes

- New folder `src/ServerLogic/NpcShop/`, namespace `IronGrind.NpcShop`. Asset under `Assets/data/` (e.g. `Assets/data/Shop/NpcShopCatalog.asset`), following the other data assets.
- Validation is a pure function over the catalog entries and an `IItemDatabase`-style lookup, returning a result list — testable without Unity assets (build entries in code for tests; one test loads the authored asset and validates it).
- Scroll records exist (`Assets/data/items/consumables/ItemID(35–38)_*.asset`, `SellPriceGold = 0`), so OQ-NS-4 is done.
- Equipment items (Sell tab only) are not in the catalog and are never checked by F-NS-3.

---

## Out of Scope

- Story 002: sessions; Story 006: the buy path that reads the catalog; Story 009: loading the asset in the zone composition

---

## QA Test Cases

*Lean mode: written at `/create-stories` 2026-11-19; refine at `/story-readiness`.*

- **AC-1**: load the authored asset → 10 entries, ids 29–38, prices and reasons as listed; no id repeated.
- **AC-2**: every catalog id → true with the right price; Iron Sword id, 0, an unknown id → false.
- **AC-3**: authored catalog + real item records → validation passes; Small potion at 3 / 2 → passes exactly at the boundary.
- **AC-4**: scroll + bad potion → exactly one error, for the potion id.
- **AC-5**: each failure kind in its own test → `IsValid` false and one `LogAssert.Expect(LogType.Error, …)` naming the id; the composition-level helper throws `InvalidOperationException`.
- **AC-6**: a catalog with cap 50 → `QuantityCap` 50 read from data.

---

## Test Evidence

**Story Type**: Logic
**Required evidence**: `tests/EditMode/NpcShop/NpcShop_Catalog_tests.cs` — must exist and pass

**Status**: [ ] Not yet created

---

## Dependencies

- Depends on: None
- Unlocks: Stories 006, 009
