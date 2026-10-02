#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Single source of truth for the 38 MVP item records (Stories 004 and 006). Shared by
    /// <see cref="ItemDatabaseSeeder"/> (creates the real <c>.asset</c> files in the
    /// Unity Editor) and the EditMode test suite (builds the same records in-memory to
    /// verify acceptance criteria without requiring the <c>.asset</c> files to exist).
    /// </summary>
    /// <remarks>
    /// Equipment values mirror <c>production/epics/item-database/story-004-mvp-item-records.md</c>,
    /// including its placeholder flat-bonus values (OQ-1 unresolved) — known-provisional MVP
    /// values, not final game balance. Potion and Enhancement Scroll values mirror
    /// <c>story-006-scroll-records-and-potion-value-alignment.md</c>: potion magnitudes and
    /// cooldowns from the Consumable Use System GDD, scroll records from Item Database
    /// GDD Rule 13.
    /// </remarks>
    internal static class MvpItemRecordData
    {
        private static readonly GearTier[] Tiers = { GearTier.Bronze, GearTier.Iron, GearTier.Steel, GearTier.DarkSteel };
        private static readonly string[] TierNames = { "Bronze", "Iron", "Steel", "DarkSteel" };
        private static readonly int[] TierSellPrice = { 10, 30, 90, 270 };

        // Placeholder equip requirements (STR gate). Bronze has none; higher tiers scale up.
        private static readonly float?[] TierStrRequirement = { null, 15f, 30f, 50f };

        // Placeholder flat-bonus tables (OQ-1 unresolved — see story Out of Scope).
        private static readonly float[] SwordApBonus       = { 5f, 12f, 25f, 45f };
        private static readonly float[] HelmetDefBonus     = { 3f, 8f, 18f, 32f };
        private static readonly float[] ChestDefBonus      = { 5f, 12f, 25f, 45f };
        private static readonly float[] ChestVitBonus      = { 2f, 5f, 10f, 18f };
        private static readonly float[] LegArmorDefBonus   = { 4f, 10f, 22f, 38f };
        private static readonly float[] BootsDefBonus      = { 2f, 5f, 11f, 20f };
        private static readonly float[] BootsMovBonus      = { 0.05f, 0.08f, 0.12f, 0.18f };
        private static readonly float[] RingIntBonus       = { 3f, 8f, 18f, 32f };
        private static readonly float[] NecklaceMaxHpBonus = { 20f, 50f, 110f, 200f };

        // Potions (Small / Medium / Large) — consumable-use-system.md F-CUS-3 table and
        // "CooldownSeconds — Authored Constants"; sell prices from Item Database F-2.
        private static readonly string[] PotionSizeNames = { "Small", "Medium", "Large" };
        private static readonly float[] PotionMagnitude  = { 80f, 220f, 500f };
        private static readonly float[] PotionCooldown   = { 20f, 30f, 45f };
        private static readonly int[] PotionSellPrice    = { 2, 6, 18 };

        // Inventory System per-slot maximum; authored on every potion and scroll record.
        private const int CONSUMABLE_STACK_LIMIT = 99;

        // Enhancement Scroll display names (Item Database GDD Rule 13 item 37), in Tiers order.
        private static readonly string[] ScrollTierNames = { "Bronze", "Iron", "Steel", "Dark Steel" };

        /// <summary>
        /// Builds all 38 MVP <see cref="ItemDefinition"/> records as in-memory
        /// <see cref="ScriptableObject"/> instances, in ItemID order 1–38. Callers are
        /// responsible for either persisting them via
        /// <c>UnityEditor.AssetDatabase.CreateAsset</c> (the seeder) or destroying them
        /// via <see cref="Object.DestroyImmediate(Object)"/> (EditMode tests) once finished.
        /// </summary>
        internal static List<ItemDefinition> BuildAll()
        {
            var records = new List<ItemDefinition>(38);
            uint nextId = 1;

            records.AddRange(BuildEquipmentFamily(ref nextId, "Sword", GearSlot.Weapon,
                t => new[] { StatModifierEntry.CreateForTesting(StatID.AttackPower, SwordApBonus[t]) }));
            records.AddRange(BuildEquipmentFamily(ref nextId, "Helmet", GearSlot.Helmet,
                t => new[] { StatModifierEntry.CreateForTesting(StatID.Defense, HelmetDefBonus[t]) }));
            records.AddRange(BuildEquipmentFamily(ref nextId, "Chestplate", GearSlot.Chest,
                t => new[]
                {
                    StatModifierEntry.CreateForTesting(StatID.Defense, ChestDefBonus[t]),
                    StatModifierEntry.CreateForTesting(StatID.Vitality, ChestVitBonus[t]),
                }));
            records.AddRange(BuildEquipmentFamily(ref nextId, "Leggings", GearSlot.Legs,
                t => new[] { StatModifierEntry.CreateForTesting(StatID.Defense, LegArmorDefBonus[t]) }));
            records.AddRange(BuildEquipmentFamily(ref nextId, "Boots", GearSlot.Boots,
                t => new[]
                {
                    StatModifierEntry.CreateForTesting(StatID.Defense, BootsDefBonus[t]),
                    StatModifierEntry.CreateForTesting(StatID.MovementSpeed, BootsMovBonus[t]),
                }));
            records.AddRange(BuildAccessoryFamily(ref nextId, "Ring", GearSlot.Ring,
                t => new[] { StatModifierEntry.CreateForTesting(StatID.Intelligence, RingIntBonus[t]) }));
            records.AddRange(BuildAccessoryFamily(ref nextId, "Necklace", GearSlot.Necklace,
                t => new[] { StatModifierEntry.CreateForTesting(StatID.MaxHP, NecklaceMaxHpBonus[t]) }));

            records.AddRange(BuildPotionFamily(ref nextId, "HP Potion", EffectType.RestoreHP));
            records.AddRange(BuildPotionFamily(ref nextId, "MP Potion", EffectType.RestoreMP));

            for (int t = 0; t < 4; t++)
            {
                records.Add(BuildScroll(nextId, $"{ScrollTierNames[t]} Enhancement Scroll", Tiers[t]));
                nextId++;
            }

            return records;
        }

        // HP / MP potions share magnitude, cooldown and sell price per size (Small, Medium, Large).
        private static List<ItemDefinition> BuildPotionFamily(ref uint nextId, string typeName, EffectType effect)
        {
            var list = new List<ItemDefinition>(3);
            for (int s = 0; s < 3; s++)
            {
                list.Add(BuildConsumable(nextId, $"{typeName} ({PotionSizeNames[s]})", effect,
                    PotionMagnitude[s], PotionCooldown[s], CONSUMABLE_STACK_LIMIT, PotionSellPrice[s]));
                nextId++;
            }
            return list;
        }

        // Sword..Boots: STR-gated at Iron+ (never on Ring/Necklace — see BuildAccessoryFamily).
        private static List<ItemDefinition> BuildEquipmentFamily(
            ref uint nextId, string typeName, GearSlot slot, Func<int, StatModifierEntry[]> modifiersForTier)
        {
            var list = new List<ItemDefinition>(4);
            for (int t = 0; t < 4; t++)
            {
                var equipmentData = EquipmentData.CreateForTesting(
                    slot, Tiers[t], modifiersForTier(t),
                    equipRequirementStat: TierStrRequirement[t].HasValue ? StatID.Strength : (StatID?)null,
                    equipRequirementMin: TierStrRequirement[t] ?? 0f);

                list.Add(BuildRecord(nextId, $"{TierNames[t]} {typeName}", TierSellPrice[t],
                    equipmentData, typeName, TierNames[t]));
                nextId++;
            }
            return list;
        }

        // Ring/Necklace: no stat gate at any tier (Equipment System GDD OQ-EQS-2 resolution);
        // no merge chain authored at MVP (CR-EQS-14 owns this — null here).
        private static List<ItemDefinition> BuildAccessoryFamily(
            ref uint nextId, string typeName, GearSlot slot, Func<int, StatModifierEntry[]> modifiersForTier)
        {
            var list = new List<ItemDefinition>(4);
            for (int t = 0; t < 4; t++)
            {
                var equipmentData = EquipmentData.CreateForTesting(
                    slot, Tiers[t], modifiersForTier(t),
                    equipRequirementStat: null, equipRequirementMin: 0f, mergeResultItemID: null);

                list.Add(BuildRecord(nextId, $"{TierNames[t]} {typeName}", TierSellPrice[t],
                    equipmentData, typeName, TierNames[t]));
                nextId++;
            }
            return list;
        }

        private static ItemDefinition BuildRecord(
            uint id, string displayName, int sellPriceGold, EquipmentData equipmentData, string typeName, string tierName)
        {
            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.SetForTesting(
                new ItemID(id), displayName, ItemCategory.Equipment,
                sellPriceGold, isUpgradeable: true, stackLimit: 1,
                equipmentData: equipmentData,
                description: $"{tierName}-tier {typeName.ToLowerInvariant()}. Placeholder flavour text (OQ-1 pending).",
                iconAddress: $"icons/items/{typeName.ToLowerInvariant()}_{tierName.ToLowerInvariant()}");
            return def;
        }

        private static ItemDefinition BuildConsumable(
            uint id, string displayName, EffectType effect, float magnitude, float cooldown, int stackLimit, int sellPriceGold)
        {
            var consumableData = ConsumableData.CreateForTesting(effect, magnitude, cooldown);
            string slug = displayName.ToLowerInvariant().Replace(" ", "_").Replace("(", "").Replace(")", "");

            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.SetForTesting(
                new ItemID(id), displayName, ItemCategory.Consumable,
                sellPriceGold, isUpgradeable: false, stackLimit: stackLimit,
                consumableData: consumableData,
                description: $"{displayName}. Placeholder flavour text.",
                iconAddress: $"icons/items/{slug}");
            return def;
        }

        // Enhancement Scroll: a Consumable with ScrollData set and no ConsumableData. Unsellable
        // (SellPriceGold = 0) — the buy price is owned by the NPC Shop, not stored here.
        private static ItemDefinition BuildScroll(uint id, string displayName, GearTier targetGearTier)
        {
            string slug = displayName.ToLowerInvariant().Replace(" ", "_");

            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.SetForTesting(
                new ItemID(id), displayName, ItemCategory.Consumable,
                sellPriceGold: 0, isUpgradeable: false, stackLimit: CONSUMABLE_STACK_LIMIT,
                description: $"{displayName}. Placeholder flavour text.",
                iconAddress: $"icons/items/{slug}",
                scrollData: ScrollData.CreateForTesting(targetGearTier));
            return def;
        }
    }
}

#endif
