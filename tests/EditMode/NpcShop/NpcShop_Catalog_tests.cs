#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;
using IronGrind.ServerLogic.NpcShop;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.NpcShop
{
    /// <summary>
    /// NPC Shop Story 001: the authored catalog asset, <see cref="NpcShopCatalogValidator"/> (F-NS-3, scroll exemption,
    /// every failure kind, no early stop, overflow) and the S2 <see cref="ZoneShopCatalogLoader"/>.
    /// </summary>
    [TestFixture]
    internal sealed class NpcShop_Catalog_Tests
    {
        private const string CATALOG_PATH = "Assets/data/Shop/NpcShopCatalog.asset";
        private const string CONSUMABLES_FOLDER = "Assets/data/items/consumables";
        private const uint LAST_SCROLL_ID = 38;
        private const uint FIRST_SCROLL_ID = 35;
        private const uint IRON_SWORD_ID = 2;
        private const uint SMALL_POTION_ID = 29;
        private const uint SCROLL_ID = 35;
        private const int EXPECTED_ENTRY_COUNT = 10;
        private const uint LARGEST_PRICE_FOR_CAP_99 = 43_383_508; // 43,383,508 x 99 = 4,294,967,292 <= uint.MaxValue
        private const string S2 = "[ZoneServer] S2: ";
        private const uint TEST_NUMERATOR = 15;
        private const uint TEST_DENOMINATOR = 10;
        private const int TEST_CAP = 99;

        private static readonly uint[] AUTHORED_IDS = { 35, 36, 37, 38, 29, 30, 31, 32, 33, 34 };
        private static readonly uint[] AUTHORED_PRICES = { 90, 150, 230, 350, 3, 10, 30, 3, 10, 30 };

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                Object.DestroyImmediate(obj);
            }

            _created.Clear();
        }

        private sealed class FakeRest : IZoneStaticDataLoader, IZoneItemData
        {
            public int Calls;
            public bool Result = true;
            public IItemDatabase Database;
            public IItemDatabase LoadedItemDatabase => Database;
            public bool LoadAndValidate() { Calls++; return Result; }
        }

        private sealed class PlainRest : IZoneStaticDataLoader
        {
            public int Calls;
            public bool LoadAndValidate() { Calls++; return true; }
        }

        private ItemDefinition MakeItem(uint id, int sell, bool scroll)
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            _created.Add(item);
            item.SetForTesting(new ItemID(id), "Item" + id, ItemCategory.Consumable, sell, false, TEST_CAP,
                scrollData: scroll ? ScrollData.CreateForTesting(GearTier.Bronze) : null);
            return item;
        }

        private static IItemDatabase MakeDb(params ItemDefinition[] items)
        {
            var db = new IronGrind.ItemDatabase.ItemDatabase();
            db.Initialize(items);
            return db;
        }

        private NpcShopCatalog MakeCatalog(int cap, uint num, uint den, params (uint id, uint price)[] lines)
        {
            var entries = new List<NpcShopCatalogEntry>();
            foreach ((uint id, uint price) in lines)
            {
                entries.Add(new NpcShopCatalogEntry(new ItemID(id), price));
            }

            NpcShopCatalog catalog = NpcShopCatalog.CreateForTests(entries, cap, num, den);
            _created.Add(catalog);
            return catalog;
        }

        private NpcShopCatalog MakeCatalog(params (uint id, uint price)[] lines)
        {
            return MakeCatalog(TEST_CAP, TEST_NUMERATOR, TEST_DENOMINATOR, lines);
        }

        private static NpcShopCatalog LoadAuthored()
        {
            NpcShopCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcShopCatalog>(CATALOG_PATH);
            Assert.IsNotNull(catalog, "authored catalog asset not found at " + CATALOG_PATH);
            return catalog;
        }

        private static Regex Exact(string message)
        {
            return new Regex("^" + Regex.Escape(message) + "$");
        }

        private static void AssertNames(string failure, uint id, uint price, string sell)
        {
            StringAssert.Contains("ItemID " + id + " (BuyPrice " + price + ", SellPriceGold " + sell + "): ", failure);
        }

        private static IItemDatabase RealDb()
        {
            var items = new List<ItemDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { CONSUMABLES_FOLDER }))
            {
                items.Add(AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)));
            }

            var db = new IronGrind.ItemDatabase.ItemDatabase();
            db.Initialize(items);
            return db;
        }

        private static void AssertSingleFailure(NpcShopCatalogValidationResult result, string expected)
        {
            Assert.IsFalse(result.IsValid);
            Assert.IsNull(result.Catalog);
            Assert.AreEqual(1, result.Failures.Count, string.Join(" | ", result.Failures));
            StringAssert.Contains(expected, result.Failures[0]);
        }

        // ---------------------------------------------------------------- AC-1 / AC-2 authored asset

        [Test]
        public void test_shop_catalog_authored_asset_has_ten_entries_in_order()
        {
            NpcShopCatalog catalog = LoadAuthored();

            Assert.IsNotNull(catalog);
            Assert.AreEqual(EXPECTED_ENTRY_COUNT, catalog.Entries.Count);
            var seen = new HashSet<uint>();
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                Assert.AreEqual(AUTHORED_IDS[i], catalog.Entries[i].ItemId.RawValue, "id " + i);
                Assert.AreEqual(AUTHORED_PRICES[i], catalog.Entries[i].BuyPrice, "price " + i);
                Assert.IsTrue(seen.Add(catalog.Entries[i].ItemId.RawValue), "no repeated id");
            }

            Assert.AreEqual(NpcShopCatalog.QUANTITY_CAP_DEFAULT, catalog.QuantityCap);
            Assert.AreEqual(NpcShopCatalog.RATIO_NUMERATOR_DEFAULT, catalog.RatioNumerator);
            Assert.AreEqual(NpcShopCatalog.RATIO_DENOMINATOR_DEFAULT, catalog.RatioDenominator);
        }

        [Test]
        public void test_shop_catalog_authored_asset_validates_against_real_item_records()
        {
            NpcShopCatalogValidationResult result = NpcShopCatalogValidator.Validate(LoadAuthored(), RealDb());

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Failures));
            Assert.IsNotNull(result.Catalog);
        }

        [Test]
        public void test_shop_catalog_lookup_returns_every_catalog_id_with_derived_reason()
        {
            ValidatedNpcShopCatalog catalog = NpcShopCatalogValidator.Validate(LoadAuthored(), RealDb()).Catalog;

            for (int i = 0; i < AUTHORED_IDS.Length; i++)
            {
                Assert.IsTrue(catalog.TryGetEntry(new ItemID(AUTHORED_IDS[i]), out ShopCatalogEntry entry), "id " + AUTHORED_IDS[i]);
                Assert.AreEqual(AUTHORED_PRICES[i], entry.BuyPrice);
                bool scroll = AUTHORED_IDS[i] >= FIRST_SCROLL_ID && AUTHORED_IDS[i] <= LAST_SCROLL_ID;
                Assert.AreEqual(scroll ? GoldTransactionReason.ScrollPurchase : GoldTransactionReason.ItemPurchase,
                    entry.Reason, "reason " + AUTHORED_IDS[i]);
            }

            Assert.AreEqual(AUTHORED_IDS.Length, catalog.Entries.Count);
            Assert.AreEqual(FIRST_SCROLL_ID, catalog.Entries[0].ItemId.RawValue, "authored order kept");
        }

        [Test]
        public void test_shop_catalog_lookup_unknown_ids_return_false()
        {
            ValidatedNpcShopCatalog catalog = NpcShopCatalogValidator.Validate(LoadAuthored(), RealDb()).Catalog;

            Assert.IsFalse(catalog.TryGetEntry(new ItemID(IRON_SWORD_ID), out _));
            Assert.IsFalse(catalog.TryGetEntry(new ItemID(0), out _));
            Assert.IsFalse(catalog.TryGetEntry(new ItemID(uint.MaxValue), out _));
        }

        // ---------------------------------------------------------------- AC-3 F-NS-3

        [Test]
        public void test_shop_catalog_boundary_price_three_for_sell_two_passes()
        {
            IItemDatabase db = MakeDb(MakeItem(SMALL_POTION_ID, 2, false));

            NpcShopCatalogValidationResult result = NpcShopCatalogValidator.Validate(MakeCatalog((SMALL_POTION_ID, 3)), db);

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Failures));
        }

        [Test]
        public void test_shop_catalog_price_two_for_sell_two_fails_f_ns_3()
        {
            IItemDatabase db = MakeDb(MakeItem(SMALL_POTION_ID, 2, false));

            NpcShopCatalogValidationResult result = NpcShopCatalogValidator.Validate(MakeCatalog((SMALL_POTION_ID, 2)), db);

            AssertSingleFailure(result, "F-NS-3");
            StringAssert.Contains("ItemID 29", result.Failures[0]);
            StringAssert.Contains("BuyPrice 2", result.Failures[0]);
            StringAssert.Contains("SellPriceGold 2", result.Failures[0]);
        }

        // ---------------------------------------------------------------- AC-4 scroll exemption

        [Test]
        public void test_shop_catalog_scroll_exempt_and_bad_potion_gives_exactly_one_failure()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true), MakeItem(SMALL_POTION_ID, 2, false));

            NpcShopCatalogValidationResult result =
                NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, 1), (SMALL_POTION_ID, 2)), db);

            AssertSingleFailure(result, "ItemID 29");
        }

        // ---------------------------------------------------------------- AC-5 failure kinds

        [Test]
        public void test_shop_catalog_null_catalog_fails()
        {
            AssertSingleFailure(NpcShopCatalogValidator.Validate(null, MakeDb()), "catalog missing");
        }

        [Test]
        public void test_shop_catalog_empty_catalog_fails()
        {
            AssertSingleFailure(NpcShopCatalogValidator.Validate(MakeCatalog(), MakeDb()), "no entries");
        }

        [Test]
        public void test_shop_catalog_zero_buy_price_fails_naming_the_id()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true));

            NpcShopCatalogValidationResult result = NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, 0)), db);

            AssertSingleFailure(result, "BuyPrice is 0");
            AssertNames(result.Failures[0], SCROLL_ID, 0, "0");
        }

        [Test]
        public void test_shop_catalog_item_missing_from_database_fails_naming_the_id()
        {
            NpcShopCatalogValidationResult result =
                NpcShopCatalogValidator.Validate(MakeCatalog((SMALL_POTION_ID, 3)), MakeDb());

            AssertSingleFailure(result, "missing from the item database");
            AssertNames(result.Failures[0], SMALL_POTION_ID, 3, "n/a");
        }

        [Test]
        public void test_shop_catalog_duplicate_item_id_fails()
        {
            IItemDatabase db = MakeDb(MakeItem(SMALL_POTION_ID, 2, false));

            NpcShopCatalogValidationResult result =
                NpcShopCatalogValidator.Validate(MakeCatalog((SMALL_POTION_ID, 3), (SMALL_POTION_ID, 4)), db);

            AssertSingleFailure(result, "duplicate ItemID");
            AssertNames(result.Failures[0], SMALL_POTION_ID, 4, "2");
        }

        [Test]
        public void test_shop_catalog_quantity_cap_zero_fails()
        {
            NpcShopCatalog catalog = MakeCatalog(0, TEST_NUMERATOR, TEST_DENOMINATOR, (SCROLL_ID, 1));
            AssertSingleFailure(NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SCROLL_ID, 0, true))), "QuantityCap");
        }

        [Test]
        public void test_shop_catalog_quantity_cap_above_maximum_fails()
        {
            NpcShopCatalog catalog = MakeCatalog(NpcShopCatalog.QUANTITY_CAP_MAX + 1, TEST_NUMERATOR, TEST_DENOMINATOR,
                (SCROLL_ID, 1));
            AssertSingleFailure(NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SCROLL_ID, 0, true))), "QuantityCap");
        }

        [Test]
        public void test_shop_catalog_ratio_numerator_zero_fails()
        {
            NpcShopCatalog catalog = MakeCatalog(TEST_CAP, 0, TEST_DENOMINATOR, (SCROLL_ID, 1));
            AssertSingleFailure(NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SCROLL_ID, 0, true))), "ratio");
        }

        [Test]
        public void test_shop_catalog_ratio_denominator_zero_fails()
        {
            NpcShopCatalog catalog = MakeCatalog(TEST_CAP, TEST_NUMERATOR, 0, (SCROLL_ID, 1));
            AssertSingleFailure(NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SCROLL_ID, 0, true))), "ratio");
        }

        [Test]
        public void test_shop_catalog_price_times_cap_overflow_fails()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true));

            NpcShopCatalogValidationResult result =
                NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, 50_000_000)), db);

            AssertSingleFailure(result, "overflows");
            AssertNames(result.Failures[0], SCROLL_ID, 50_000_000, "0");
        }

        [Test]
        public void test_shop_catalog_overflow_boundary_largest_price_passes_next_fails()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true));

            NpcShopCatalogValidationResult atLimit =
                NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, LARGEST_PRICE_FOR_CAP_99)), db);
            NpcShopCatalogValidationResult overLimit =
                NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, LARGEST_PRICE_FOR_CAP_99 + 1)), db);

            Assert.IsTrue(atLimit.IsValid, string.Join(" | ", atLimit.Failures));
            AssertSingleFailure(overLimit, "overflows");
        }

        [Test]
        public void test_shop_catalog_invalid_cap_suppresses_overflow_check()
        {
            NpcShopCatalog catalog = MakeCatalog(0, TEST_NUMERATOR, TEST_DENOMINATOR, (SCROLL_ID, 50_000_000));

            AssertSingleFailure(NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SCROLL_ID, 0, true))), "QuantityCap");
        }

        [Test]
        public void test_shop_catalog_zero_ratio_part_suppresses_f_ns_3()
        {
            NpcShopCatalog catalog = MakeCatalog(TEST_CAP, 0, TEST_DENOMINATOR, (SMALL_POTION_ID, 2));

            AssertSingleFailure(NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SMALL_POTION_ID, 2, false))), "ratio");
        }

        [Test]
        public void test_shop_catalog_quantity_cap_boundaries_one_and_ninety_nine_pass()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true));
            NpcShopCatalog capOne = MakeCatalog(NpcShopCatalog.QUANTITY_CAP_MIN, TEST_NUMERATOR, TEST_DENOMINATOR, (SCROLL_ID, 1));
            NpcShopCatalog capMax = MakeCatalog(NpcShopCatalog.QUANTITY_CAP_MAX, TEST_NUMERATOR, TEST_DENOMINATOR, (SCROLL_ID, 1));

            Assert.IsTrue(NpcShopCatalogValidator.Validate(capOne, db).IsValid);
            Assert.IsTrue(NpcShopCatalogValidator.Validate(capMax, db).IsValid);
        }

        [Test]
        public void test_shop_catalog_negative_sell_price_is_exempt_from_f_ns_3()
        {
            IItemDatabase db = MakeDb(MakeItem(SMALL_POTION_ID, -1, false));

            NpcShopCatalogValidationResult result = NpcShopCatalogValidator.Validate(MakeCatalog((SMALL_POTION_ID, 1)), db);

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Failures));
        }

        [Test]
        public void test_shop_catalog_null_item_database_fails()
        {
            AssertSingleFailure(NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, 1)), null), "item database missing");
        }

        [Test]
        public void test_shop_catalog_reason_derived_from_scroll_data_on_synthetic_items()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true), MakeItem(SMALL_POTION_ID, 2, false));

            ValidatedNpcShopCatalog catalog =
                NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, 1), (SMALL_POTION_ID, 3)), db).Catalog;

            Assert.IsTrue(catalog.TryGetEntry(new ItemID(SCROLL_ID), out ShopCatalogEntry scroll));
            Assert.IsTrue(catalog.TryGetEntry(new ItemID(SMALL_POTION_ID), out ShopCatalogEntry potion));
            Assert.AreEqual(GoldTransactionReason.ScrollPurchase, scroll.Reason);
            Assert.AreEqual(GoldTransactionReason.ItemPurchase, potion.Reason);
        }

        [Test]
        public void test_shop_catalog_two_bad_entries_give_two_failures()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true), MakeItem(SMALL_POTION_ID, 2, false));

            NpcShopCatalogValidationResult result =
                NpcShopCatalogValidator.Validate(MakeCatalog((SCROLL_ID, 0), (SMALL_POTION_ID, 2)), db);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(2, result.Failures.Count, string.Join(" | ", result.Failures));
        }

        // ---------------------------------------------------------------- AC-7 data, not literals

        [Test]
        public void test_shop_catalog_validated_cap_and_ratio_come_from_the_asset()
        {
            const int customCap = 50;
            const uint customNumerator = 2;
            const uint customDenominator = 1;
            NpcShopCatalog catalog = MakeCatalog(customCap, customNumerator, customDenominator, (SCROLL_ID, 7));

            ValidatedNpcShopCatalog validated =
                NpcShopCatalogValidator.Validate(catalog, MakeDb(MakeItem(SCROLL_ID, 0, true))).Catalog;

            Assert.AreEqual(customCap, validated.QuantityCap);
            Assert.AreEqual(customNumerator, validated.RatioNumerator);
            Assert.AreEqual(customDenominator, validated.RatioDenominator);
        }

        // ---------------------------------------------------------------- AC-6 loader

        [Test]
        public void test_shop_catalog_loader_valid_catalog_succeeds_and_exposes_data()
        {
            IItemDatabase db = RealDb();
            var rest = new FakeRest { Database = db };
            var loader = new ZoneShopCatalogLoader(LoadAuthored(), rest);

            Assert.IsTrue(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, rest.Calls);
            Assert.IsNotNull(loader.LoadedShopCatalog);
            Assert.AreEqual(EXPECTED_ENTRY_COUNT, loader.LoadedShopCatalog.Entries.Count);
            Assert.AreSame(db, loader.LoadedItemDatabase);
        }

        [Test]
        public void test_shop_catalog_loader_invalid_catalog_logs_one_error_per_failure_and_fails()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true), MakeItem(SMALL_POTION_ID, 2, false));
            var rest = new FakeRest { Database = db };
            var loader = new ZoneShopCatalogLoader(MakeCatalog((SCROLL_ID, 0), (SMALL_POTION_ID, 2)), rest);
            LogAssert.Expect(LogType.Error,
                Exact(S2 + "shop catalog: ItemID 35 (BuyPrice 0, SellPriceGold 0): BuyPrice is 0"));
            LogAssert.Expect(LogType.Error,
                Exact(S2 + "shop catalog: ItemID 29 (BuyPrice 2, SellPriceGold 2): F-NS-3 violated: BuyPrice x 10 < SellPriceGold x 15"));

            Assert.IsFalse(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, rest.Calls, "the wrapped item loader runs first (Story 001 deviation)");
            Assert.IsNull(loader.LoadedShopCatalog);
            Assert.IsNull(loader.LoadedItemDatabase);
        }

        [Test]
        public void test_shop_catalog_loader_null_asset_logs_one_error_and_does_not_run_rest()
        {
            var rest = new FakeRest { Database = MakeDb() };
            var loader = new ZoneShopCatalogLoader(null, rest);
            LogAssert.Expect(LogType.Error, Exact(S2 + "NpcShopCatalog missing"));

            Assert.IsFalse(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(0, rest.Calls);
            Assert.IsNull(loader.LoadedShopCatalog);
        }

        [Test]
        public void test_shop_catalog_loader_null_item_database_from_rest_logs_one_error_and_fails()
        {
            var rest = new FakeRest { Database = null };
            var loader = new ZoneShopCatalogLoader(MakeCatalog((SCROLL_ID, 1)), rest);
            LogAssert.Expect(LogType.Error, Exact(S2 + "item database missing for the shop catalog"));

            Assert.IsFalse(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.IsNull(loader.LoadedShopCatalog);
        }

        [Test]
        public void test_shop_catalog_loader_rest_failure_fails_without_catalog()
        {
            var rest = new FakeRest { Result = false, Database = MakeDb(MakeItem(SCROLL_ID, 0, true)) };
            var loader = new ZoneShopCatalogLoader(MakeCatalog((SCROLL_ID, 1)), rest);

            Assert.IsFalse(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, rest.Calls);
            Assert.IsNull(loader.LoadedShopCatalog);
            Assert.IsNull(loader.LoadedItemDatabase);
        }

        [Test]
        public void test_shop_catalog_loader_rest_without_item_data_logs_one_error_and_fails()
        {
            var rest = new PlainRest();
            var loader = new ZoneShopCatalogLoader(MakeCatalog((SCROLL_ID, 1)), rest);
            LogAssert.Expect(LogType.Error, Exact(S2 + "item database missing for the shop catalog"));

            Assert.IsFalse(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, rest.Calls);
            Assert.IsNull(loader.LoadedShopCatalog);
        }

        [Test]
        public void test_shop_catalog_loader_null_rest_logs_one_error_and_fails()
        {
            var loader = new ZoneShopCatalogLoader(MakeCatalog((SCROLL_ID, 1)), null);
            LogAssert.Expect(LogType.Error, Exact(S2 + "item database missing for the shop catalog"));

            Assert.IsFalse(loader.LoadAndValidate());

            LogAssert.NoUnexpectedReceived();
            Assert.IsNull(loader.LoadedShopCatalog);
        }

        [Test]
        public void test_shop_catalog_loader_reload_resets_state_both_ways()
        {
            IItemDatabase db = MakeDb(MakeItem(SCROLL_ID, 0, true));
            var rest = new FakeRest { Database = db };
            var loader = new ZoneShopCatalogLoader(MakeCatalog((SCROLL_ID, 1)), rest);

            Assert.IsTrue(loader.LoadAndValidate());
            rest.Result = false;
            Assert.IsFalse(loader.LoadAndValidate());
            Assert.IsNull(loader.LoadedShopCatalog, "a failed reload exposes nothing");
            Assert.IsNull(loader.LoadedItemDatabase);
            rest.Result = true;
            Assert.IsTrue(loader.LoadAndValidate());

            Assert.IsNotNull(loader.LoadedShopCatalog);
            Assert.AreSame(db, loader.LoadedItemDatabase);
            Assert.AreEqual(3, rest.Calls);
        }
    }
}
#endif
