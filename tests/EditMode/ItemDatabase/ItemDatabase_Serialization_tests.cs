using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode tests that an <see cref="ItemDefinition"/> survives Unity serialization
    /// (tech-debt TD-047). <see cref="Object.Instantiate(Object)"/> copies an object through
    /// Unity's serializer, so a field the serializer skips comes back at its default value
    /// in the clone — the same loss that happens when a record is saved to and loaded from
    /// a <c>.asset</c> file, reproduced here without any file I/O.
    /// </summary>
    /// <remarks>
    /// The other ItemDatabase suites work on in-memory records built through the
    /// <c>SetForTesting</c> seam and never serialize them, so they cannot detect a field
    /// that is set in memory but never written to disk.
    /// </remarks>
    [TestFixture]
    internal sealed class ItemDatabase_Serialization_Tests
    {
        private const uint ITEM_ID = 35u;
        private const uint MERGE_RESULT_ITEM_ID = 22u;

        private List<ItemDefinition> _itemsToDestroy;

        [SetUp]
        public void SetUp()
        {
            _itemsToDestroy = new List<ItemDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var def in _itemsToDestroy)
            {
                if (def != null)
                    UnityEngine.Object.DestroyImmediate(def);
            }
            _itemsToDestroy.Clear();
        }

        // Helper — builds a definition, clones it through Unity's serializer, and registers
        // both for cleanup. Returns the clone.
        private ItemDefinition BuildAndClone(
            ItemCategory category,
            int stackLimit,
            EquipmentData equipmentData = null,
            ConsumableData consumableData = null,
            ScrollData scrollData = null)
        {
            var original = ItemDefinitionBuilder.Build(
                ITEM_ID, "Serialized Item", category,
                stackLimit: stackLimit,
                equipmentData: equipmentData,
                consumableData: consumableData,
                scrollData: scrollData);
            _itemsToDestroy.Add(original);

            var clone = UnityEngine.Object.Instantiate(original);
            _itemsToDestroy.Add(clone);
            return clone;
        }

        [Test]
        public void ItemDefinition_SerializedClone_PreservesItemId()
        {
            // Arrange / Act
            var clone = BuildAndClone(ItemCategory.Consumable, stackLimit: 99,
                scrollData: ScrollData.CreateForTesting(GearTier.Bronze));

            // Assert
            Assert.AreEqual(new ItemID(ITEM_ID), clone.ItemId,
                "ItemId must survive serialization — a record loaded from an asset must not read ItemID(0).");
        }

        [Test]
        public void ItemDefinition_SerializedClone_PreservesMergeResultItemID()
        {
            // Arrange / Act
            var clone = BuildAndClone(ItemCategory.Equipment, stackLimit: 1,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Ring, GearTier.Bronze,
                    mergeResultItemID: new ItemID(MERGE_RESULT_ITEM_ID)));

            // Assert
            Assert.AreEqual(new ItemID(MERGE_RESULT_ITEM_ID), clone.EquipmentData.MergeResultItemID,
                "MergeResultItemID must survive serialization.");
        }

        [Test]
        public void ItemDefinition_SerializedClone_NullMergeResultItemIDStaysNull()
        {
            // Arrange / Act
            var clone = BuildAndClone(ItemCategory.Equipment, stackLimit: 1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Ring, GearTier.Bronze));

            // Assert
            Assert.IsNull(clone.EquipmentData.MergeResultItemID,
                "An item with no merge result must still have none after serialization.");
        }

        [Test]
        public void ItemDefinition_SerializedClone_KeepsOnlyTheAuthoredSubSchema()
        {
            // Arrange / Act — a scroll carries ScrollData and nothing else.
            var clone = BuildAndClone(ItemCategory.Consumable, stackLimit: 99,
                scrollData: ScrollData.CreateForTesting(GearTier.Steel));

            // Assert — [SerializeReference] must keep the two unused sub-schemas null.
            Assert.IsNotNull(clone.ScrollData, "ScrollData must survive serialization.");
            Assert.AreEqual(GearTier.Steel, clone.ScrollData.TargetGearTier);
            Assert.IsNull(clone.ConsumableData, "ConsumableData must stay null on a scroll.");
            Assert.IsNull(clone.EquipmentData, "EquipmentData must stay null on a scroll.");
        }
    }
}
